using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        bool startMinimized = false;
        if (args != null)
        {
            foreach (var arg in args)
            {
                if (arg.Equals("/startup", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("/background", StringComparison.OrdinalIgnoreCase))
                {
                    startMinimized = true;
                    break;
                }
            }
        }

        Application.Run(new ScreenOffForm(startMinimized));
    }
}

class ScreenOffForm : Form
{
    // ================================================================
    // WIN32 — HOTKEYS + MONITOR/SCREENSAVER CONTROL
    // ================================================================
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    
    // For window dragging
    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MONITORPOWER = 0xF170;
    private const int SC_SCREENSAVE = 0xF140;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HT_CAPTION = 2;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;
    
    private const int HOTKEY_ID_OFF = 999;
    private const int HOTKEY_ID_SS = 998;

    // ================================================================
    // DATA & CONFIG
    // ================================================================
    private string ConfigDir { get { return Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "screen-off"); } }
    private string ConfigPath { get { return Path.Combine(ConfigDir, "config.json"); } }

    // Screen off config
    private string _modifier = "Alt";
    private string _key = "D";
    private int _delaySec = 0;

    // Screensaver config
    private string _ssModifier = "Alt";
    private string _ssKey = "S";

    private bool _runAtStartup = false;
    private readonly bool _startMinimized;

    // UI elements
    private NotifyIcon _tray;
    private HotkeyWindow _hotkeyWindow;
    
    // Panel/Controls
    private Panel _titleBar;
    private Label _titleLabel;
    private Button _closeBtn;
    private Button _minimizeBtn;
    
    private Label _statusLabel;
    
    // Config panel (Screen Off)
    private ComboBox _modCombo;
    private ComboBox _keyCombo;
    private ComboBox _delayCombo;
    
    // Config panel (Screensaver)
    private ComboBox _ssModCombo;
    private ComboBox _ssKeyCombo;

    private CheckBox _startupCheck;
    private Button _actionBtn;
    private Button _ssActionBtn;
    private Button _shortcutBtn;
    private TextBox _logBox;

    private Timer _countdownTimer;
    private int _countdownRemaining = 0;

    public ScreenOffForm(bool startMinimized)
    {
        _startMinimized = startMinimized;

        // Form properties
        Text = "Screen Off & Screensaver Utility";
        Size = new Size(560, 460);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(18, 18, 20);
        ForeColor = Color.FromArgb(243, 244, 246);
        DoubleBuffered = true;

        // Custom window border painting
        Paint += (s, e) =>
        {
            using (var pen = new Pen(Color.FromArgb(139, 92, 246), 2)) // Neon purple accent border
            {
                e.Graphics.DrawRectangle(pen, 1, 1, ClientSize.Width - 2, ClientSize.Height - 2);
            }
        };

        InitializeTray();
        InitializeComponents();
        LoadConfig();
        ApplyStartupStateFromRegistry();
        SetupHotkeys();

        _countdownTimer = new Timer();
        _countdownTimer.Interval = 1000;
        _countdownTimer.Tick += CountdownTimer_Tick;

        Log("Screen Off Utility started successfully.");
    }

    private void InitializeTray()
    {
        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Screen Off & Screensaver Utility",
            Visible = true
        };
        _tray.DoubleClick += (s, e) => ShowDashboard();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Dashboard", null, (s, e) => ShowDashboard());
        menu.Items.Add("Turn Off Screen Now", null, (s, e) => TriggerScreenOff(0));
        menu.Items.Add("Start Screensaver Now", null, (s, e) => TriggerScreensaver());
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (s, e) => ExitApplication());
        _tray.ContextMenuStrip = menu;
    }

    private void InitializeComponents()
    {
        // 1. Title bar panel
        _titleBar = new Panel
        {
            Height = 40,
            Dock = DockStyle.Top,
            BackColor = Color.FromArgb(26, 26, 30)
        };
        _titleBar.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        };

        _titleLabel = new Label
        {
            Text = "SCREEN OFF & SCREENSAVER UTILITY",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(139, 92, 246),
            Location = new Point(14, 11),
            AutoSize = true
        };
        _titleBar.Controls.Add(_titleLabel);

        _closeBtn = new Button
        {
            Text = "×",
            Font = new Font("Segoe UI Semibold", 12),
            Size = new Size(30, 30),
            Location = new Point(520, 5),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(156, 163, 175)
        };
        _closeBtn.FlatAppearance.BorderSize = 0;
        _closeBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(239, 68, 68);
        _closeBtn.FlatAppearance.MouseDownBackColor = Color.FromArgb(185, 28, 28);
        _closeBtn.Click += (s, e) => HideToTray();
        _titleBar.Controls.Add(_closeBtn);

        _minimizeBtn = new Button
        {
            Text = "—",
            Font = new Font("Segoe UI Semibold", 8),
            Size = new Size(30, 30),
            Location = new Point(486, 5),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(156, 163, 175)
        };
        _minimizeBtn.FlatAppearance.BorderSize = 0;
        _minimizeBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 65, 81);
        _minimizeBtn.Click += (s, e) => HideToTray();
        _titleBar.Controls.Add(_minimizeBtn);

        Controls.Add(_titleBar);

        // 2. Main content container panel
        var mainPanel = new Panel
        {
            Location = new Point(16, 56),
            Size = new Size(528, 388),
            BackColor = Color.Transparent
        };
        Controls.Add(mainPanel);

        // 3. Status card (pill display)
        var statusCard = new Panel
        {
            Size = new Size(528, 48),
            Location = new Point(0, 0),
            BackColor = Color.FromArgb(26, 26, 30)
        };
        mainPanel.Controls.Add(statusCard);

        _statusLabel = new Label
        {
            Text = "Hotkeys active: Alt+D (Off) | Alt+S (Screensaver)",
            Font = new Font("Segoe UI Semibold", 9f),
            ForeColor = Color.FromArgb(16, 185, 129),
            Location = new Point(12, 14),
            AutoSize = true
        };
        statusCard.Controls.Add(_statusLabel);

        // 4. Config panel: Screen Off (left column, top)
        var configCard = new Panel
        {
            Size = new Size(256, 156),
            Location = new Point(0, 60),
            BackColor = Color.FromArgb(26, 26, 30)
        };
        mainPanel.Controls.Add(configCard);

        var offTitle = new Label { Text = "SCREEN OFF HOTKEY", Font = new Font("Segoe UI", 8f, FontStyle.Bold), ForeColor = Color.FromArgb(139, 92, 246), Location = new Point(12, 8), AutoSize = true };
        configCard.Controls.Add(offTitle);

        var lbl1 = new Label { Text = "Modifier", Font = new Font("Segoe UI", 7.5f), ForeColor = Color.FromArgb(156, 163, 175), Location = new Point(12, 28), AutoSize = true };
        _modCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(31, 41, 55),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8f),
            Location = new Point(12, 44),
            Size = new Size(110, 22)
        };
        _modCombo.Items.AddRange(new object[] { "Alt", "Ctrl", "Shift", "Win", "None" });
        _modCombo.SelectedIndexChanged += ConfigChanged;

        var lbl2 = new Label { Text = "Trigger Key", Font = new Font("Segoe UI", 7.5f), ForeColor = Color.FromArgb(156, 163, 175), Location = new Point(134, 28), AutoSize = true };
        _keyCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(31, 41, 55),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8f),
            Location = new Point(134, 44),
            Size = new Size(110, 22)
        };
        for (char c = 'A'; c <= 'Z'; c++) _keyCombo.Items.Add(c.ToString());
        for (int f = 1; f <= 12; f++) _keyCombo.Items.Add("F" + f);
        _keyCombo.SelectedIndexChanged += ConfigChanged;

        var lbl3 = new Label { Text = "Activation Delay", Font = new Font("Segoe UI", 7.5f), ForeColor = Color.FromArgb(156, 163, 175), Location = new Point(12, 78), AutoSize = true };
        _delayCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(31, 41, 55),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8f),
            Location = new Point(12, 94),
            Size = new Size(232, 22)
        };
        _delayCombo.Items.AddRange(new object[] { "Immediate", "1 Second", "2 Seconds", "3 Seconds", "5 Seconds" });
        _delayCombo.SelectedIndexChanged += ConfigChanged;

        configCard.Controls.Add(lbl1);
        configCard.Controls.Add(_modCombo);
        configCard.Controls.Add(lbl2);
        configCard.Controls.Add(_keyCombo);
        configCard.Controls.Add(lbl3);
        configCard.Controls.Add(_delayCombo);

        // 5. Config panel: Screensaver (left column, bottom)
        var ssConfigCard = new Panel
        {
            Size = new Size(256, 156),
            Location = new Point(0, 228),
            BackColor = Color.FromArgb(26, 26, 30)
        };
        mainPanel.Controls.Add(ssConfigCard);

        var ssTitle = new Label { Text = "SCREENSAVER HOTKEY", Font = new Font("Segoe UI", 8f, FontStyle.Bold), ForeColor = Color.FromArgb(139, 92, 246), Location = new Point(12, 8), AutoSize = true };
        ssConfigCard.Controls.Add(ssTitle);

        var lbl4 = new Label { Text = "Modifier", Font = new Font("Segoe UI", 7.5f), ForeColor = Color.FromArgb(156, 163, 175), Location = new Point(12, 28), AutoSize = true };
        _ssModCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(31, 41, 55),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8f),
            Location = new Point(12, 44),
            Size = new Size(110, 22)
        };
        _ssModCombo.Items.AddRange(new object[] { "Alt", "Ctrl", "Shift", "Win", "None" });
        _ssModCombo.SelectedIndexChanged += ConfigChanged;

        var lbl5 = new Label { Text = "Trigger Key", Font = new Font("Segoe UI", 7.5f), ForeColor = Color.FromArgb(156, 163, 175), Location = new Point(134, 28), AutoSize = true };
        _ssKeyCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(31, 41, 55),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8f),
            Location = new Point(134, 44),
            Size = new Size(110, 22)
        };
        for (char c = 'A'; c <= 'Z'; c++) _ssKeyCombo.Items.Add(c.ToString());
        for (int f = 1; f <= 12; f++) _ssKeyCombo.Items.Add("F" + f);
        _ssKeyCombo.SelectedIndexChanged += ConfigChanged;

        var ssHint = new Label
        {
            Text = "Starts the screensaver immediately using standard Windows system calls.",
            Font = new Font("Segoe UI", 7.5f, FontStyle.Italic),
            ForeColor = Color.FromArgb(156, 163, 175),
            Location = new Point(12, 84),
            Size = new Size(232, 60)
        };

        ssConfigCard.Controls.Add(lbl4);
        ssConfigCard.Controls.Add(_ssModCombo);
        ssConfigCard.Controls.Add(lbl5);
        ssConfigCard.Controls.Add(_ssKeyCombo);
        ssConfigCard.Controls.Add(ssHint);

        // 6. Actions card (right column, top)
        var actionsCard = new Panel
        {
            Size = new Size(256, 156),
            Location = new Point(272, 60),
            BackColor = Color.FromArgb(26, 26, 30)
        };
        mainPanel.Controls.Add(actionsCard);

        _startupCheck = new CheckBox
        {
            Text = "Launch on Windows startup",
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.FromArgb(209, 213, 219),
            Location = new Point(16, 8),
            Size = new Size(224, 22),
            FlatStyle = FlatStyle.Flat
        };
        _startupCheck.CheckedChanged += StartupCheck_CheckedChanged;
        actionsCard.Controls.Add(_startupCheck);

        _actionBtn = new Button
        {
            Text = "TURN OFF SCREEN NOW",
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            BackColor = Color.FromArgb(139, 92, 246),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(16, 34),
            Size = new Size(224, 30)
        };
        _actionBtn.FlatAppearance.BorderSize = 0;
        _actionBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(124, 58, 237);
        _actionBtn.FlatAppearance.MouseDownBackColor = Color.FromArgb(109, 40, 217);
        _actionBtn.Click += (s, e) => TriggerScreenOff(_delaySec);
        actionsCard.Controls.Add(_actionBtn);

        _ssActionBtn = new Button
        {
            Text = "PLAY SCREENSAVER NOW",
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            BackColor = Color.FromArgb(109, 40, 217),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(16, 70),
            Size = new Size(224, 30)
        };
        _ssActionBtn.FlatAppearance.BorderSize = 0;
        _ssActionBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(124, 58, 237);
        _ssActionBtn.Click += (s, e) => TriggerScreensaver();
        actionsCard.Controls.Add(_ssActionBtn);

        _shortcutBtn = new Button
        {
            Text = "CREATE DESKTOP SHORTCUT",
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            BackColor = Color.FromArgb(31, 41, 55),
            ForeColor = Color.FromArgb(229, 231, 235),
            FlatStyle = FlatStyle.Flat,
            Location = new Point(16, 110),
            Size = new Size(224, 26)
        };
        _shortcutBtn.FlatAppearance.BorderSize = 0;
        _shortcutBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 65, 81);
        _shortcutBtn.Click += CreateDesktopShortcut;
        actionsCard.Controls.Add(_shortcutBtn);

        // 7. Logs card (right column, bottom)
        var logsCard = new Panel
        {
            Size = new Size(256, 156),
            Location = new Point(272, 228),
            BackColor = Color.FromArgb(26, 26, 30)
        };
        mainPanel.Controls.Add(logsCard);

        var logHeader = new Panel
        {
            Bounds = new Rectangle(0, 0, 256, 24),
            BackColor = Color.FromArgb(22, 22, 26)
        };
        logsCard.Controls.Add(logHeader);

        var logTitle = new Label
        {
            Text = "ACTIVITY LOG",
            Font = new Font("Segoe UI", 7f, FontStyle.Bold),
            ForeColor = Color.FromArgb(156, 163, 175),
            Location = new Point(8, 5),
            AutoSize = true
        };
        logHeader.Controls.Add(logTitle);

        var clearBtn = new Button
        {
            Text = "Clear",
            Font = new Font("Segoe UI", 7f),
            Size = new Size(40, 16),
            Location = new Point(130, 4),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(156, 163, 175),
            BackColor = Color.Transparent
        };
        clearBtn.FlatAppearance.BorderSize = 0;
        clearBtn.Click += (s, e) => _logBox.Clear();
        logHeader.Controls.Add(clearBtn);

        var configFolderBtn = new Button
        {
            Text = "Folder",
            Font = new Font("Segoe UI", 7f),
            Size = new Size(46, 16),
            Location = new Point(176, 4),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(156, 163, 175),
            BackColor = Color.Transparent
        };
        configFolderBtn.FlatAppearance.BorderSize = 0;
        configFolderBtn.Click += (s, e) => {
            try { Process.Start(ConfigDir); }
            catch (Exception ex) { Log("Failed to open folder: " + ex.Message); }
        };
        logHeader.Controls.Add(configFolderBtn);

        _logBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            BackColor = Color.FromArgb(17, 24, 39),
            ForeColor = Color.FromArgb(16, 185, 129),
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 8f),
            Bounds = new Rectangle(0, 24, 256, 132),
            ScrollBars = ScrollBars.Vertical
        };
        logsCard.Controls.Add(_logBox);
    }

    // ================================================================
    // LOGIC & EVENTS
    // ================================================================
    private void Log(string msg)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<string>(Log), msg);
            return;
        }
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        _logBox.AppendText(string.Format("[{0}] {1}{2}", timestamp, msg, Environment.NewLine));
        _logBox.SelectionStart = _logBox.Text.Length;
        _logBox.ScrollToCaret();
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                
                // Load Off config
                var modMatch = Regex.Match(json, "\"hotkey_modifier\"\\s*:\\s*\"([^\"]*)\"");
                if (modMatch.Success) _modifier = modMatch.Groups[1].Value;

                var keyMatch = Regex.Match(json, "\"hotkey_key\"\\s*:\\s*\"([^\"]*)\"");
                if (keyMatch.Success) _key = keyMatch.Groups[1].Value;

                var delayMatch = Regex.Match(json, "\"turnoff_delay_sec\"\\s*:\\s*(\\d+)");
                if (delayMatch.Success) int.TryParse(delayMatch.Groups[1].Value, out _delaySec);

                // Load SS config
                var ssModMatch = Regex.Match(json, "\"ss_hotkey_modifier\"\\s*:\\s*\"([^\"]*)\"");
                if (ssModMatch.Success) _ssModifier = ssModMatch.Groups[1].Value;

                var ssKeyMatch = Regex.Match(json, "\"ss_hotkey_key\"\\s*:\\s*\"([^\"]*)\"");
                if (ssKeyMatch.Success) _ssKey = ssKeyMatch.Groups[1].Value;
                
                Log("Configuration file loaded.");
            }
            else
            {
                Log("Config file not found, using defaults (Alt+D / Alt+S).");
            }
        }
        catch (Exception ex)
        {
            Log("Error loading config: " + ex.Message);
        }

        // Apply Config to UI
        SetComboBoxSilently(_modCombo, _modifier);
        SetComboBoxSilently(_keyCombo, _key);
        SetComboBoxSilently(_ssModCombo, _ssModifier);
        SetComboBoxSilently(_ssKeyCombo, _ssKey);

        string delayText = "Immediate";
        if (_delaySec == 1) delayText = "1 Second";
        else if (_delaySec > 1) delayText = _delaySec + " Seconds";
        SetComboBoxSilently(_delayCombo, delayText);
    }

    private void SetComboBoxSilently(ComboBox cb, string val)
    {
        cb.SelectedIndexChanged -= ConfigChanged;
        int idx = cb.FindStringExact(val);
        if (idx >= 0) cb.SelectedIndex = idx;
        else if (cb.Items.Count > 0) cb.SelectedIndex = 0;
        cb.SelectedIndexChanged += ConfigChanged;
    }

    private void SaveConfig()
    {
        try
        {
            if (!Directory.Exists(ConfigDir))
            {
                Directory.CreateDirectory(ConfigDir);
            }
            var json = string.Format(
                "{{\n  \"hotkey_modifier\": \"{0}\",\n  \"hotkey_key\": \"{1}\",\n  \"turnoff_delay_sec\": {2},\n  \"ss_hotkey_modifier\": \"{3}\",\n  \"ss_hotkey_key\": \"{4}\"\n}}",
                _modifier, _key, _delaySec, _ssModifier, _ssKey
            );
            File.WriteAllText(ConfigPath, json);
            Log("Configuration saved.");
        }
        catch (Exception ex)
        {
            Log("Failed to save config: " + ex.Message);
        }
    }

    private void ConfigChanged(object sender, EventArgs e)
    {
        _modifier = _modCombo.SelectedItem.ToString();
        _key = _keyCombo.SelectedItem.ToString();
        _ssModifier = _ssModCombo.SelectedItem.ToString();
        _ssKey = _ssKeyCombo.SelectedItem.ToString();

        string delayStr = _delayCombo.SelectedItem.ToString();
        if (delayStr == "Immediate") _delaySec = 0;
        else if (delayStr.Contains("1")) _delaySec = 1;
        else if (delayStr.Contains("2")) _delaySec = 2;
        else if (delayStr.Contains("3")) _delaySec = 3;
        else if (delayStr.Contains("5")) _delaySec = 5;

        SaveConfig();
        SetupHotkeys();
    }

    private void SetupHotkeys()
    {
        if (_hotkeyWindow != null)
        {
            UnregisterHotKey(_hotkeyWindow.Handle, HOTKEY_ID_OFF);
            UnregisterHotKey(_hotkeyWindow.Handle, HOTKEY_ID_SS);
            _hotkeyWindow.Dispose();
            _hotkeyWindow = null;
        }

        _hotkeyWindow = new HotkeyWindow(this);
        
        string offDesc;
        bool offSuccess = RegisterSingleHotkey(HOTKEY_ID_OFF, _modifier, _key, out offDesc);
        
        string ssDesc;
        bool ssSuccess = RegisterSingleHotkey(HOTKEY_ID_SS, _ssModifier, _ssKey, out ssDesc);

        // Update UI status
        if (offSuccess && ssSuccess)
        {
            _statusLabel.Text = string.Format("Hotkeys active: {0} (Off) | {1} (Screensaver)", offDesc, ssDesc);
            _statusLabel.ForeColor = Color.FromArgb(16, 185, 129);
        }
        else if (offSuccess)
        {
            _statusLabel.Text = string.Format("Screen Off active ({0}). Screensaver failed.", offDesc);
            _statusLabel.ForeColor = Color.FromArgb(245, 158, 11);
        }
        else if (ssSuccess)
        {
            _statusLabel.Text = string.Format("Screensaver active ({0}). Screen Off failed.", ssDesc);
            _statusLabel.ForeColor = Color.FromArgb(245, 158, 11);
        }
        else
        {
            _statusLabel.Text = "Hotkey registrations failed!";
            _statusLabel.ForeColor = Color.FromArgb(239, 68, 68);
        }

        _tray.Text = string.Format("Screen Off & Screensaver ({0} / {1})", offDesc, ssDesc);
    }

    private bool RegisterSingleHotkey(int id, string mod, string keyName, out string desc)
    {
        uint modFlags = MOD_NOREPEAT;
        if (mod == "Alt") modFlags |= MOD_ALT;
        else if (mod == "Ctrl") modFlags |= MOD_CONTROL;
        else if (mod == "Shift") modFlags |= MOD_SHIFT;
        else if (mod == "Win") modFlags |= MOD_WIN;
        else if (mod == "None") modFlags = 0;

        uint vk = 0;
        if (keyName.Length == 1)
        {
            vk = (uint)keyName[0];
        }
        else if (keyName.StartsWith("F") && keyName.Length > 1)
        {
            int fNum = int.Parse(keyName.Substring(1));
            vk = (uint)(0x6F + fNum);
        }

        desc = (mod == "None" ? "" : mod + "+") + keyName;

        if (vk == 0)
        {
            desc = "Disabled";
            return false;
        }

        if (RegisterHotKey(_hotkeyWindow.Handle, id, modFlags, vk))
        {
            Log(string.Format("Registered hotkey [{0}]: {1}", id == HOTKEY_ID_OFF ? "Off" : "SS", desc));
            return true;
        }
        else
        {
            Log(string.Format("ERROR: Failed to register hotkey [{0}]: {1}", id == HOTKEY_ID_OFF ? "Off" : "SS", desc));
            return false;
        }
    }

    private void ApplyStartupStateFromRegistry()
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
            {
                if (key != null)
                {
                    var val = key.GetValue("ScreenOffUtility");
                    _runAtStartup = (val != null);
                }
            }
        }
        catch (Exception ex)
        {
            Log("Startup check failed: " + ex.Message);
        }

        _startupCheck.CheckedChanged -= StartupCheck_CheckedChanged;
        _startupCheck.Checked = _runAtStartup;
        _startupCheck.CheckedChanged += StartupCheck_CheckedChanged;
    }

    private void StartupCheck_CheckedChanged(object sender, EventArgs e)
    {
        _runAtStartup = _startupCheck.Checked;
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (key != null)
                {
                    if (_runAtStartup)
                    {
                        key.SetValue("ScreenOffUtility", "\"" + Application.ExecutablePath + "\" /startup");
                        Log("Enabled Run at Windows Startup.");
                    }
                    else
                    {
                        key.DeleteValue("ScreenOffUtility", false);
                        Log("Disabled Run at Windows Startup.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log("Failed to modify startup registry key: " + ex.Message);
            ApplyStartupStateFromRegistry();
        }
    }

    private void CreateDesktopShortcut(object sender, EventArgs e)
    {
        try
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string shortcutPath = Path.Combine(desktopPath, "Screen Off.lnk");
            
            string psScript = string.Format(
                "$s=(New-Object -COM WScript.Shell).CreateShortcut('{0}');$s.TargetPath='{1}';$s.Save()",
                shortcutPath.Replace("'", "''"),
                Application.ExecutablePath.Replace("'", "''")
            );
            
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + psScript.Replace("\"", "\\\"") + "\"",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false
            };
            
            var proc = Process.Start(psi);
            proc.WaitForExit(5000);
            
            if (File.Exists(shortcutPath))
            {
                Log("Desktop shortcut created successfully.");
                MessageBox.Show("Desktop shortcut created successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                Log("ERROR: Desktop shortcut creation failed.");
            }
        }
        catch (Exception ex)
        {
            Log("Failed to create shortcut: " + ex.Message);
        }
    }

    public void OnHotkeyOffReceived()
    {
        Log("Screen Off hotkey triggered.");
        TriggerScreenOff(_delaySec);
    }

    public void OnHotkeySSReceived()
    {
        Log("Screensaver hotkey triggered.");
        TriggerScreensaver();
    }

    private void TriggerScreenOff(int delay)
    {
        if (delay <= 0)
        {
            Log("Turning off screen now.");
            SendMessage((IntPtr)0xFFFF, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, (IntPtr)2);
        }
        else
        {
            _countdownRemaining = delay;
            _actionBtn.Enabled = false;
            _countdownTimer.Start();
            Log(string.Format("Screen turning off in {0} seconds...", _countdownRemaining));
            UpdateActionButtonText();
        }
    }

    private void TriggerScreensaver()
    {
        try
        {
            string localVideosDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "videos");
            string userVideosDir = Path.Combine(ConfigDir, "videos");
            string selectedVideo = null;

            foreach (var dir in new[] { localVideosDir, userVideosDir })
            {
                if (Directory.Exists(dir))
                {
                    var files = Directory.GetFiles(dir, "*.*");
                    foreach (var file in files)
                    {
                        string ext = Path.GetExtension(file).ToLower();
                        if (ext == ".mp4" || ext == ".mkv" || ext == ".avi" || ext == ".mov" || ext == ".wmv")
                        {
                            selectedVideo = file;
                            break;
                        }
                    }
                }
                if (selectedVideo != null) break;
            }

            if (selectedVideo != null)
            {
                Log("Playing video screensaver: " + Path.GetFileName(selectedVideo));
                Process.Start("wmplayer.exe", string.Format("/play /close /fullscreen \"{0}\"", selectedVideo));
            }
            else
            {
                Log("No videos found. Launching system screensaver.");
                SendMessage(Handle, WM_SYSCOMMAND, (IntPtr)SC_SCREENSAVE, IntPtr.Zero);
            }
        }
        catch (Exception ex)
        {
            Log("Failed to start screensaver: " + ex.Message);
            SendMessage(Handle, WM_SYSCOMMAND, (IntPtr)SC_SCREENSAVE, IntPtr.Zero);
        }
    }

    private void CountdownTimer_Tick(object sender, EventArgs e)
    {
        _countdownRemaining--;
        if (_countdownRemaining <= 0)
        {
            _countdownTimer.Stop();
            _actionBtn.Enabled = true;
            _actionBtn.Text = "TURN OFF SCREEN NOW";
            Log("Turning off screen now.");
            SendMessage((IntPtr)0xFFFF, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, (IntPtr)2);
        }
        else
        {
            UpdateActionButtonText();
        }
    }

    private void UpdateActionButtonText()
    {
        _actionBtn.Text = string.Format("TURNING OFF IN {0}...", _countdownRemaining);
    }

    private void ShowDashboard()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        ShowInTaskbar = true;
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        _tray.ShowBalloonTip(1500, "Screen Off Utility", "Running in system tray. Double-click tray icon to open.", ToolTipIcon.None);
    }

    private void ExitApplication()
    {
        Log("Exiting application.");
        _tray.Visible = false;
        if (_hotkeyWindow != null)
        {
            UnregisterHotKey(_hotkeyWindow.Handle, HOTKEY_ID_OFF);
            UnregisterHotKey(_hotkeyWindow.Handle, HOTKEY_ID_SS);
            _hotkeyWindow.Dispose();
        }
        Application.Exit();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!IsHandleCreated)
        {
            if (_startMinimized)
            {
                value = false;
                CreateHandle();
            }
            else
            {
                ShowInTaskbar = true;
            }
        }
        base.SetVisibleCore(value);
    }

    // ================================================================
    // HOTKEY WINDOW LISTENER
    // ================================================================
    class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private readonly ScreenOffForm _parent;

        public HotkeyWindow(ScreenOffForm parent)
        {
            _parent = parent;
            var cp = new CreateParams
            {
                Caption = "ScreenOffHotkeyListener",
                ExStyle = 0x80 // Tool window (no taskbar)
            };
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = (int)m.WParam;
                if (id == HOTKEY_ID_OFF)
                {
                    _parent.OnHotkeyOffReceived();
                }
                else if (id == HOTKEY_ID_SS)
                {
                    _parent.OnHotkeySSReceived();
                }
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            DestroyHandle();
        }
    }
}

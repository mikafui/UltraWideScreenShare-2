using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace UltraWideScreenShare.WinForms
{
    public partial class MainWindow : Form
    {
        private readonly Timer _dispatcherTimer = new Timer() { Interval = 2 }; //30fps
        private Point _tittleBarLocation = new Point();
        private Magnifier _magnifier;
        private bool _isTransparent = false;
        private Color _frameColor = Color.FromArgb(255, 53, 89, 224); //#3559E0
        const int _borderWidth = 6;
        private bool _showMagnifierScheduled = true;
        private readonly AppSettings _settings = AppSettings.Load();
        private Rectangle _lastNormalBounds;
        private bool _wasMaximized;
        private bool _startInTray;
        private NotifyIcon _trayIcon = null!;
        private ToolStripMenuItem _autostartMenuItem = null!;
        private readonly Timer _displayChangedTimer = new Timer() { Interval = 1000 };
        private DateTime _restoreDeadline;
        private string? _settlingLayout;
        private bool _applyingPlacement;
        private bool _userResizing;

        public MainWindow(bool startInTray = false)
        {
            _startInTray = startInTray;
            InitializeComponent();
            TitleBar.BringToFront();
            TitleBar.Visible = _settings.ShowTitleBar;
            InitializePaddingsForBorders();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            RestoreWindowPosition();
            InitializeAutostart();
            InitializeTrayIcon();
            _displayChangedTimer.Tick += (s, e) => OnMonitorLayoutTick();
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            Log.Write($"start, layout {AppSettings.CurrentLayout}, bounds {Bounds}");
        }

        private void RestoreWindowPosition()
        {
            var placement = _settings.GetPlacement(allowFallback: true);
            if (placement is not null)
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = placement.Bounds;
            }
            _lastNormalBounds = Bounds;
            MaximizedBounds = new Rectangle(Point.Empty, Screen.GetWorkingArea(Location).Size);

            if (placement is not null && placement.Maximized)
            {
                WindowState = FormWindowState.Maximized;
                _wasMaximized = true;
                SetupMaximizeButton();
            }
        }

        private void SaveWindowPosition()
        {
            if (_applyingPlacement) return;
            _settings.SetPlacement(_lastNormalBounds, _wasMaximized);
            _settings.Save();
            Log.Write($"saved {_lastNormalBounds} maximized={_wasMaximized} for layout {AppSettings.CurrentLayout}");
        }

        // Monitors (e.g. behind a KVM switch) can take a few seconds to come back and Windows
        // rearranges windows on its own meanwhile, so keep restoring the saved placement for a while.
        private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (!IsHandleCreated) return;
            BeginInvoke(() =>
            {
                Log.Write($"display settings changed, layout {AppSettings.CurrentLayout}, bounds {Bounds}");
                _settlingLayout = null;
                _restoreDeadline = DateTime.Now.AddSeconds(15);
                _displayChangedTimer.Start();
            });
        }

        private void OnMonitorLayoutTick()
        {
            if (DateTime.Now > _restoreDeadline)
            {
                _displayChangedTimer.Stop();
                return;
            }

            // wait until the layout is the same for two ticks in a row
            var layout = AppSettings.CurrentLayout;
            if (layout != _settlingLayout)
            {
                _settlingLayout = layout;
                return;
            }

            var placement = _settings.GetPlacement(allowFallback: false);
            if (placement is null || _userResizing) return; // unknown layout - keep whatever Windows did

            if (!Visible)
            {
                _lastNormalBounds = placement.Bounds;
                _wasMaximized = placement.Maximized;
                return;
            }

            if (IsAtPlacement(placement)) return;
            Log.Write($"restoring {placement.Bounds} maximized={placement.Maximized} for layout {layout}, was {Bounds}");
            _lastNormalBounds = placement.Bounds;
            _wasMaximized = placement.Maximized;
            ApplyLastPlacement();
        }

        private bool IsAtPlacement(WindowPlacement placement)
        {
            if (placement.Maximized)
                return WindowState == FormWindowState.Maximized
                    && Screen.FromRectangle(placement.Bounds).DeviceName == Screen.FromControl(this).DeviceName;
            return WindowState == FormWindowState.Normal && Bounds == placement.Bounds;
        }

        private void ApplyLastPlacement()
        {
            var bounds = _lastNormalBounds;
            var maximized = _wasMaximized;
            _applyingPlacement = true;
            try
            {
                WindowState = FormWindowState.Normal;
                Bounds = bounds;
                MaximizedBounds = new Rectangle(Point.Empty, Screen.GetWorkingArea(Location).Size);
                if (maximized) WindowState = FormWindowState.Maximized;
                _lastNormalBounds = bounds;
                _wasMaximized = maximized;
            }
            finally
            {
                _applyingPlacement = false;
            }
            SetupMaximizeButton();
            _showMagnifierScheduled = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!GlobalHotkey.Register(Handle, _settings.Hotkey))
            {
                Trace.WriteLine($"hotkey {_settings.Hotkey} could not be registered");
                _trayIcon?.ShowBalloonTip(5000, Text,
                    $"The shortcut {_settings.Hotkey} is already used by another program. You can change it in settings.json.", ToolTipIcon.Warning);
            }
        }

        private void ToggleFromHotkey()
        {
            if (Visible && WindowState != FormWindowState.Minimized && Form.ActiveForm == this)
                HideToTray();
            else
                ShowFromTray();
        }

        private void InitializeAutostart()
        {
            try
            {
                if (!_settings.AutostartInitialized)
                {
                    Autostart.Enable();
                    _settings.AutostartInitialized = true;
                    _settings.Save();
                }
                else
                {
                    Autostart.RefreshPath();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"autostart: {ex.Message}");
            }
        }

        private void InitializeTrayIcon()
        {
            _autostartMenuItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = Autostart.IsEnabled };
            _autostartMenuItem.CheckedChanged += (s, e) =>
            {
                if (_autostartMenuItem.Checked) Autostart.Enable(); else Autostart.Disable();
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add($"Open ({_settings.Hotkey})", null, (s, e) => ShowFromTray());
            var titleBarMenuItem = new ToolStripMenuItem("Show title bar") { CheckOnClick = true, Checked = _settings.ShowTitleBar };
            titleBarMenuItem.CheckedChanged += (s, e) =>
            {
                TitleBar.Visible = _settings.ShowTitleBar = titleBarMenuItem.Checked;
                _settings.Save();
            };

            menu.Items.Add(titleBarMenuItem);
            menu.Items.Add(_autostartMenuItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => Close());

            _trayIcon = new NotifyIcon
            {
                Icon = Icon,
                Text = Text,
                ContextMenuStrip = menu,
                Visible = true,
            };
            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) ShowFromTray();
            };
        }

        public void ShowFromTray()
        {
            // Windows may have moved the hidden window around while monitors changed
            var placement = _settings.GetPlacement(allowFallback: false);
            if (!Visible && placement is not null)
            {
                _lastNormalBounds = placement.Bounds;
                _wasMaximized = placement.Maximized;
            }
            _applyingPlacement = true;
            if (!Visible) WindowState = FormWindowState.Normal;
            Show();
            _applyingPlacement = false;
            ApplyLastPlacement();
            Activate();
        }

        private void HideToTray()
        {
            _magnifier?.HideMagnifier();
            Hide();
        }

        protected override void SetVisibleCore(bool value)
        {
            if (_startInTray)
            {
                // started by autostart: create the window, but keep it in the tray
                _startInTray = false;
                if (!IsHandleCreated) CreateHandle();
                value = false;
            }
            base.SetVisibleCore(value);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized)
            {
                if (Visible && !_applyingPlacement) HideToTray();
                return;
            }

            var isMaximized = WindowState == FormWindowState.Maximized;
            if (!isMaximized) _lastNormalBounds = Bounds;
            if (isMaximized != _wasMaximized)
            {
                _wasMaximized = isMaximized;
                SaveWindowPosition();
            }
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();

            this.InitializeMainWindowStyle();
        }

        private void InitializePaddingsForBorders()
        {
            Padding = new Padding(_borderWidth, _borderWidth, _borderWidth, _borderWidth);
            TitleBar.Width += (_borderWidth * 2);
            TitleBar.Height += (_borderWidth);
            TitleBar.Padding = new Padding(_borderWidth, 0, _borderWidth, _borderWidth);
        }

        protected override void OnMove(EventArgs e)
        {
            MaximizedBounds = new Rectangle(Point.Empty, Screen.GetWorkingArea(Location).Size);
            if (WindowState == FormWindowState.Normal) _lastNormalBounds = Bounds;
            base.OnMove(e);
        }
        private void MainWindow_Load(object sender, EventArgs e)
        {
            _magnifier = new Magnifier(magnifierPanel.Handle);
            _dispatcherTimer.Start();
            _dispatcherTimer.Tick += (s, a) =>
            {
                _magnifier.UpdateMagnifierWindow();
                if (magnifierPanel.Bounds.Contains(PointToClient(Cursor.Position)) && !(TitleBar.Visible && TitleBar.Bounds.Contains(PointToClient(Cursor.Position))))
                {
                    if (!_isTransparent)
                    { this.SetTransparency(_isTransparent = true); Trace.WriteLine("enter"); }
                }
                else
                {
                    if (_isTransparent)
                    { this.SetTransparency(_isTransparent = false); Trace.WriteLine("leave"); }
                }
                if (_showMagnifierScheduled)
                {
                    _magnifier.ShowMagnifier();
                    _showMagnifierScheduled = false;
                }
            };
        }

        private void MainWindow_ResizeBegin(object sender, EventArgs e)
        {
            _userResizing = true;
            _magnifier.HideMagnifier();
        }

        private void MainWindow_ResizeEnd(object sender, EventArgs e)
        {
            _userResizing = false;
            _showMagnifierScheduled = true;
            SaveWindowPosition();
        }


        private void TittleButton_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                this.Drag();
                SetupMaximizeButton();
            }
        }


        const int WM_NCCALCSIZE = 0x0083;
        const int WM_NCACTIVATE = 0x0086;
        const int WM_NCHITTEST = 0x0084;
        protected override void WndProc(ref Message m)
        {
            var message = m.Msg;
            if (message == WM_NCCALCSIZE)
            {
                return;
            }

            if (message == WM_NCACTIVATE)
            {
                m.Result = new IntPtr(-1);
                return;
            }

            if (message == GlobalHotkey.WM_HOTKEY && m.WParam == (IntPtr)GlobalHotkey.HotkeyId)
            {
                ToggleFromHotkey();
                return;
            }

            base.WndProc(ref m);

            if (message == WM_NCHITTEST)
            {
                this.TryResize(ref m, _borderWidth);
            }
        }

        private void MainWindow_Paint(object sender, PaintEventArgs e)
        { 
            ControlPaint.DrawBorder(e.Graphics, ClientRectangle,
                _frameColor, _borderWidth, ButtonBorderStyle.Solid,
                _frameColor, _borderWidth, ButtonBorderStyle.Solid,
                _frameColor, _borderWidth, ButtonBorderStyle.Solid,
                _frameColor, _borderWidth, ButtonBorderStyle.Solid);
        }

        private void TitleBar_Paint(object sender, PaintEventArgs e)
        {
            ControlPaint.DrawBorder(e.Graphics, TitleBar.ClientRectangle,
               _frameColor, _borderWidth, ButtonBorderStyle.Solid,
               _frameColor, 0, ButtonBorderStyle.Solid,
               _frameColor, _borderWidth, ButtonBorderStyle.Solid,
               _frameColor, _borderWidth, ButtonBorderStyle.Solid);
        }

        private void closeButton_Click(object sender, EventArgs e) => Close();

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveWindowPosition();
            _dispatcherTimer.Stop();
            _dispatcherTimer.Dispose();
            SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            _displayChangedTimer.Dispose();
            GlobalHotkey.Unregister(Handle);
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            base.OnFormClosing(e);
        }
        private void minimizeButton_Click(object sender, EventArgs e) => HideToTray();

        private void maximizeButton_Click(object sender, EventArgs e)
        {
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal : FormWindowState.Maximized;
            SetupMaximizeButton();
        }

        private void SetupMaximizeButton()
        {
            if (WindowState == FormWindowState.Maximized)
            {
                maximizeButton.Image = Properties.Resources.restore;
            }
            else
            {
                maximizeButton.Image = Properties.Resources.maximize;
            }
        }

        private void DragButton_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _tittleBarLocation = e.Location;
            }
        }

        private void DragButton_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                TitleBar.Left = Math.Clamp(value: e.X + TitleBar.Left - _tittleBarLocation.X,
                    min: 0, max: Width - TitleBar.Width);

            }
        }
    }
}

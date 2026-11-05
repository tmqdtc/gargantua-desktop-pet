using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Gargantua
{
    public static class Pet
    {
        [STAThread]
        public static void Main()
        {
            try { Run(); }
            catch (Exception error)
            {
                MessageBox.Show(error.Message, "Gargantua", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void Run()
        {
            using (var instance = new Mutex(false, @"Local\GargantuaDesktopPet"))
            {
                if (!instance.WaitOne(0)) return;
                try
                {
                    try
                    {
                        if (!Native.SetProcessDpiAwarenessContext(new IntPtr(-4)))
                            Native.SetProcessDPIAware();
                    }
                    catch (EntryPointNotFoundException) { Native.SetProcessDPIAware(); }
                    Application.EnableVisualStyles();
                    using (var window = new LensWindow())
                    using (var menu = new ContextMenuStrip())
                    using (var trayIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                    using (var tray = new NotifyIcon())
                    using (var timer = new System.Windows.Forms.Timer())
                    {
                        var pause = new ToolStripMenuItem("暂停漂移") { CheckOnClick = true };
                        var settings = PetSettings.Load();
                        var clock = Stopwatch.StartNew();
                        double previous = 0;
                        double driftTime = 0;
                        double cycle = 0;
                        bool settingsOpen = false;
                        Action<Point> openSettings = location =>
                        {
                            if (settingsOpen) return;
                            settingsOpen = true;
                            window.PetInputEnabled = false;
                            timer.Stop();
                            try
                            {
                                using (var dialog = new SettingsDialog(settings, window.CanCapture, window.AutomaticMaximum, location))
                                {
                                    if (dialog.ShowDialog() == DialogResult.OK)
                                    {
                                        PetSettings updated = dialog.Settings;
                                        if (!settings.SameSizing(updated)) cycle = 0;
                                        settings = updated;
                                        if (!settings.Save())
                                            MessageBox.Show("设置已应用，但这次无法保存到 Windows 用户配置。",
                                                "Gargantua", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                    }
                                }
                            }
                            finally
                            {
                                settingsOpen = false;
                                window.PetInputEnabled = !menu.Visible;
                                previous = clock.Elapsed.TotalSeconds;
                                timer.Start();
                            }
                        };
                        menu.Opened += (sender, args) =>
                        {
                            window.PetInputEnabled = false;
                            window.MenuBounds = menu.Bounds;
                        };
                        menu.LocationChanged += (sender, args) =>
                        {
                            if (menu.Visible) window.MenuBounds = menu.Bounds;
                        };
                        menu.Closed += (sender, args) =>
                        {
                            window.MenuBounds = Rectangle.Empty;
                            window.PetInputEnabled = !settingsOpen;
                        };
                        window.MenuDismissRequested += () => menu.Close(ToolStripDropDownCloseReason.AppClicked);
                        window.MenuRequested += location =>
                        {
                            if (!settingsOpen && !menu.Visible) menu.Show(location);
                        };
                        menu.Items.Add("设置…", null, (sender, args) => openSettings(Cursor.Position));
                        menu.Items.Add(pause);
                        menu.Items.Add(window.HasScreenShortcut ? "切换屏幕 (F7)" : "切换屏幕",
                            null, (sender, args) => window.SwitchScreen());
                        menu.Items.Add("退出黑洞", null, (sender, args) => Application.ExitThread());
                        tray.Text = "Gargantua 黑洞桌宠";
                        tray.Icon = trayIcon ?? SystemIcons.Application;
                        tray.ContextMenuStrip = menu;
                        tray.Visible = true;

                        window.ScreenChanged += () =>
                        {
                            if (settings.CycleEnabled && cycle >= settings.GrowSeconds) cycle = 0;
                        };
                        Action draw = () =>
                        {
                            double now = clock.Elapsed.TotalSeconds;
                            double elapsed = Math.Min(now - previous, 0.10);
                            if (window.HasPendingDrag && cycle >= settings.GrowSeconds) cycle = 0;
                            if (!window.HoldsPet && !menu.Visible)
                            {
                                if (!pause.Checked && (!settings.CycleEnabled || cycle < settings.GrowSeconds))
                                    driftTime += elapsed * settings.DriftSpeedPercent / 100.0;
                                cycle = settings.CycleEnabled ? (cycle + elapsed) % settings.CycleSeconds : 0;
                            }
                            previous = now;
                            window.Draw(now, driftTime, cycle, settings);
                        };
                        draw();
                        timer.Interval = 33;
                        timer.Tick += (sender, args) => draw();
                        timer.Start();
                        Application.Run();
                        tray.Visible = false;
                    }
                }
                finally { instance.ReleaseMutex(); }
            }
        }
    }

    internal sealed class PetSettings
    {
        private const string RegistryPath = @"Software\GargantuaDesktopPet";
        internal const int MinimumSize = 48;
        internal const int MaximumSize = 8192;
        internal const int MinimumDuration = 500;
        internal const int MaximumDuration = 60000;
        internal const int MaximumDriftSpeed = 300;

        public readonly bool LensEnabled, CycleEnabled;
        public readonly int MinSide, MaxSide, FixedSide, CycleCentiseconds, DriftSpeedPercent;

        public PetSettings(bool lens = true, bool cycle = true, int minSide = 144, int maxSide = 0,
            int fixedSide = 384, int cycleCentiseconds = 3883, int driftSpeedPercent = 100)
        {
            LensEnabled = lens;
            CycleEnabled = cycle;
            MinSide = minSide;
            MaxSide = maxSide;
            FixedSide = fixedSide;
            CycleCentiseconds = cycleCentiseconds;
            DriftSpeedPercent = driftSpeedPercent;
        }

        public double CycleSeconds { get { return CycleCentiseconds / 100.0; } }
        public double GrowSeconds { get { return CycleSeconds - LensWindow.SwallowSeconds - LensWindow.ResetSeconds; } }

        public bool SameSizing(PetSettings other)
        {
            return CycleEnabled == other.CycleEnabled && MinSide == other.MinSide && MaxSide == other.MaxSide
                && FixedSide == other.FixedSide && CycleCentiseconds == other.CycleCentiseconds;
        }

        public static PetSettings Load()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    string saved = key == null ? null : key.GetValue("Settings") as string;
                    if (!string.IsNullOrEmpty(saved))
                    {
                        string[] fields = saved.Split(',');
                        if (fields.Length != 6 && fields.Length != 7) return new PetSettings();
                        var values = new[] { 0, 0, 0, 0, 0, 0, 100 };
                        for (int i = 0; i < fields.Length; i++)
                            if (!int.TryParse(fields[i], out values[i])) return new PetSettings();

                        if (ValidSize(values[2]) && (values[3] == 0 || (ValidSize(values[3]) && values[3] >= values[2]))
                            && ValidSize(values[4]) && values[5] >= MinimumDuration && values[5] <= MaximumDuration
                            && values[6] >= 0 && values[6] <= MaximumDriftSpeed)
                            return new PetSettings(values[0] == 1, values[1] == 1, values[2], values[3], values[4], values[5], values[6]);
                    }
                }
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.IO.IOException) { }
            return new PetSettings();
        }

        public bool Save()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    if (key == null) return false;
                    key.SetValue("Settings", string.Join(",", new[] {
                        LensEnabled ? 1 : 0, CycleEnabled ? 1 : 0, MinSide, MaxSide, FixedSide, CycleCentiseconds, DriftSpeedPercent
                    }));
                    return true;
                }
            }
            catch (System.Security.SecurityException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (System.IO.IOException) { return false; }
        }

        private static bool ValidSize(int value) { return value >= MinimumSize && value <= MaximumSize; }
    }

    internal sealed class SettingsDialog : Form
    {
        private readonly CheckBox lens, automatic;
        private readonly RadioButton cycle, fixedMode;
        private readonly NumericUpDown minSide, maxSide, fixedSide, duration, driftSpeed;
        public PetSettings Settings { get; private set; }

        public SettingsDialog(PetSettings current, bool canCapture, int automaticSize, Point location)
        {
            Settings = current;
            Text = "Gargantua 桌宠设置";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            TopMost = true;
            ClientSize = new Size(442, 371);
            StartPosition = FormStartPosition.Manual;

            lens = new CheckBox { Text = "开启桌面引力透镜", AutoSize = true, Checked = current.LensEnabled, Enabled = canCapture };
            if (!canCapture) lens.Text = "开启桌面引力透镜（当前无法采样）";
            cycle = new RadioButton { Text = "生长并吞屏", Checked = current.CycleEnabled, AutoSize = true };
            fixedMode = new RadioButton { Text = "固定大小", Checked = !current.CycleEnabled, AutoSize = true };
            automatic = new CheckBox { Text = "按屏幕自动", Checked = current.MaxSide == 0, AutoSize = true };
            minSide = SizeField(current.MinSide);
            maxSide = SizeField(current.MaxSide == 0 ? Math.Min(PetSettings.MaximumSize, Math.Max(automaticSize, current.MinSide)) : current.MaxSide);
            fixedSide = SizeField(current.FixedSide);
            duration = new NumericUpDown {
                Width = 100, TextAlign = HorizontalAlignment.Right, DecimalPlaces = 2, Increment = 1,
                Minimum = PetSettings.MinimumDuration / 100m, Maximum = PetSettings.MaximumDuration / 100m,
                Value = current.CycleCentiseconds / 100m
            };

            driftSpeed = new NumericUpDown {
                Width = 100, TextAlign = HorizontalAlignment.Right, Increment = 10,
                Minimum = 0, Maximum = PetSettings.MaximumDriftSpeed, Value = current.DriftSpeedPercent
            };

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 12, 18, 12), ColumnCount = 2, RowCount = 10 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int rowHeight in new[] { 32, 34, 34, 34, 34, 34, 34, 34, 25, 42 })
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));

            AddWide(layout, Row(lens), 0);
            AddRow(layout, "动画模式", Row(cycle, fixedMode), 1);
            AddRow(layout, "最小尺寸（像素）", Row(minSide), 2);
            AddRow(layout, "最大尺寸（像素）", Row(maxSide, automatic), 3);
            AddRow(layout, "循环总时长（秒）", Row(duration), 4);
            AddRow(layout, "固定尺寸（像素）", Row(fixedSide), 5);
            AddRow(layout, "漂移速度（%）", Row(driftSpeed), 6);
            var hint = new Label
            {
                AutoSize = true, ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Left,
                Text = "总时长包含生长、吞屏和复位。尺寸受当前屏幕限制。"
            };
            AddWide(layout, hint, 7);
            var status = new Label { AutoSize = true, ForeColor = Color.Firebrick, Anchor = AnchorStyles.Left };
            AddWide(layout, status, 8);

            var ok = new Button { Text = "确定", AutoSize = true };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
            var buttons = Row(ok, cancel);
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Controls.SetChildIndex(cancel, 0);
            var restore = new Button { Text = "恢复默认", AutoSize = true };
            layout.Controls.Add(Row(restore), 0, 9);
            layout.Controls.Add(buttons, 1, 9);
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(layout);

            Action refresh = () =>
            {
                bool growing = cycle.Checked;
                minSide.Enabled = duration.Enabled = automatic.Enabled = growing;
                maxSide.Enabled = growing && !automatic.Checked;
                fixedSide.Enabled = !growing;
                status.Text = "";
            };
            cycle.CheckedChanged += (sender, args) => refresh();
            automatic.CheckedChanged += (sender, args) => refresh();
            refresh();
            restore.Click += (sender, args) =>
            {
                var defaults = new PetSettings();
                lens.Checked = defaults.LensEnabled;
                cycle.Checked = defaults.CycleEnabled;
                fixedMode.Checked = !defaults.CycleEnabled;
                automatic.Checked = defaults.MaxSide == 0;
                minSide.Value = defaults.MinSide;
                maxSide.Value = Math.Min(PetSettings.MaximumSize, Math.Max(automaticSize, defaults.MinSide));
                fixedSide.Value = defaults.FixedSide;
                duration.Value = defaults.CycleCentiseconds / 100m;
                driftSpeed.Value = defaults.DriftSpeedPercent;
                refresh();
                status.ForeColor = SystemColors.GrayText;
                status.Text = "已填入默认设置，点确定后应用。";
            };
            ok.Click += (sender, args) =>
            {
                int maximum = automatic.Checked ? 0 : (int)maxSide.Value;
                if (maximum != 0 && maximum < minSide.Value)
                {
                    if (cycle.Checked)
                    {
                        status.ForeColor = Color.Firebrick;
                        status.Text = "最大尺寸必须大于或等于最小尺寸。";
                        maxSide.Focus();
                        return;
                    }
                    maximum = (int)minSide.Value;
                }
                Settings = new PetSettings(lens.Checked, cycle.Checked, (int)minSide.Value, maximum,
                    (int)fixedSide.Value, (int)(duration.Value * 100), (int)driftSpeed.Value);
                DialogResult = DialogResult.OK;
            };

            Shown += (sender, args) =>
            {
                Rectangle area = Screen.FromPoint(location).WorkingArea;
                Left = Math.Max(area.Left, Math.Min(location.X + 8, area.Right - Width));
                Top = Math.Max(area.Top, Math.Min(location.Y + 8, area.Bottom - Height));
                Activate();
            };
        }

        private static NumericUpDown SizeField(int value)
        {
            return new NumericUpDown {
                Width = 100, TextAlign = HorizontalAlignment.Right, Increment = 8, ThousandsSeparator = true,
                Minimum = PetSettings.MinimumSize, Maximum = PetSettings.MaximumSize, Value = value
            };
        }

        private static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            foreach (var control in controls)
            {
                control.Margin = new Padding(0, 5, 10, 0);
                row.Controls.Add(control);
            }
            return row;
        }

        private static void AddRow(TableLayoutPanel layout, string caption, Control control, int row)
        {
            layout.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            layout.Controls.Add(control, 1, row);
        }

        private static void AddWide(TableLayoutPanel layout, Control control, int row)
        {
            layout.Controls.Add(control, 0, row);
            layout.SetColumnSpan(control, 2);
        }
    }

    internal sealed class LensWindow : NativeWindow, IDisposable
    {
        internal const double SwallowSeconds = 2.15;
        internal const double ResetSeconds = 0.68;
        private readonly LensRenderer renderer;
        private DesktopLens local;
        private const int ScreenShortcut = 1;
        private Point displayPoint;
        private bool registeredHotkey;
        private Native.HookCallback keyboardCallback;
        private IntPtr keyboardHook;
        private bool f7Down;
        internal bool HasScreenShortcut { get { return registeredHotkey || keyboardHook != IntPtr.Zero; } }
        internal event Action ScreenChanged;
        private BurstRenderer burst;
        private bool burstCaptureFailed;
        private Rectangle display;
        private FilteredDesktopCapture capture;
        internal bool CanCapture { get { return capture != null && capture.IsReady; } }
        private Point center;
        private int visibleSide;
        private const int ShowMenuMessage = 0x8001;
        private const int DismissMenuMessage = 0x8002;
        private Native.HookCallback mouseCallback;
        private IntPtr mouseHook;
        private bool petRightDown, petLeftDown, dragMoved;
        private Point dragStart, grabOffset, driftOffset, dragPointer;
        private readonly Size dragThreshold = SystemInformation.DragSize;
        private Point? dragTarget;
        private double dragTime;
        internal bool HoldsPet { get { return petLeftDown; } }
        internal bool HasPendingDrag { get { return dragTarget.HasValue; } }
        internal bool PetInputEnabled { get; set; }
        internal event Action<Point> MenuRequested;
        internal event Action MenuDismissRequested;
        internal Rectangle MenuBounds { get; set; }

        public LensWindow()
        {
            displayPoint = Cursor.Position;
            renderer = new LensRenderer(384);

            try
            {
                CreateHandle(new CreateParams
                {
                    Caption = "Gargantua Desktop Pet",
                    Style = unchecked((int)0x80000000), // WS_POPUP
                    ExStyle = 0x080800A8, // NOACTIVATE | LAYERED | TOOLWINDOW | TRANSPARENT | TOPMOST
                    Width = 1,
                    Height = 1
                });
                capture = new FilteredDesktopCapture(Handle);
                PetInputEnabled = true;
                mouseCallback = ObserveMouse;
                mouseHook = Native.SetWindowsHookEx(14, mouseCallback, Native.GetModuleHandle(null), 0);
                registeredHotkey = Native.RegisterHotKey(Handle, ScreenShortcut, 0x4000, (uint)Keys.F7);
                if (!registeredHotkey)
                {
                    // If registration fails, observe F7 and pass the keystroke through.
                    keyboardCallback = ObserveF7;
                    keyboardHook = Native.SetWindowsHookEx(13, keyboardCallback, Native.GetModuleHandle(null), 0);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Draw(double time, double driftTime, double cycle, PetSettings settings)
        {
            if (dragTarget.HasValue)
                displayPoint = dragPointer;
            var monitor = Screen.FromPoint(displayPoint);
            if (display != monitor.Bounds)
            {
                ReleaseBurst();
                display = monitor.Bounds;
            }

            Rectangle area = monitor.WorkingArea;
            int limit = Math.Min(area.Width, area.Height);
            int minSide = Math.Min(settings.CycleEnabled ? settings.MinSide : settings.FixedSide, limit);
            // The default outer lens circle covers approximately a quarter of the monitor.
            int automaticMax = AutomaticSize(monitor);
            int configuredMax = settings.MaxSide == 0 ? automaticMax : settings.MaxSide;
            int maxSide = settings.CycleEnabled ? Math.Max(minSide, Math.Min(configuredMax, limit)) : minSide;
            if (local == null || local.Capacity != maxSide)
            {
                ReleaseBurst();
                if (local != null) local.Dispose();
                local = new DesktopLens(maxSide);
            }
            // The magnifier must register its filter after the overlay is visible to DWM.
            bool canSample = CanCapture && Native.IsWindowVisible(Handle);
            bool localLens = settings.LensEnabled && canSample;
            double attack = cycle - settings.GrowSeconds;
            bool useBurst = settings.CycleEnabled && attack >= 0 && canSample;
            double growth = attack < 0 ? cycle / settings.GrowSeconds : 1 - attack / 0.75;
            int side = minSide + (int)Math.Round((maxSide - minSide) * Ease(growth));
            if (!useBurst || burst == null)
                PositionPet(area, maxSide, useBurst ? maxSide : side, driftTime);

            IntPtr screen = Native.GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) return;
            try
            {
                if (!useBurst)
                {
                    burstCaptureFailed = false;
                    ReleaseBurst();
                    DrawLocal(screen, time, side, localLens);
                    return;
                }

                if (burstCaptureFailed)
                {
                    DrawLocal(screen, time, minSide, localLens);
                    return;
                }

                if (burst == null)
                {
                    var next = new BurstRenderer(display, center, maxSide);
                    if (!next.Capture(capture))
                    {
                        next.Dispose();
                        burstCaptureFailed = true;
                        DrawLocal(screen, time, minSide, false);
                        return;
                    }
                    burst = next;
                }

                var disk = renderer.Render(time);
                visibleSide = burst.Draw(disk, renderer.Side, minSide, maxSide, attack, localLens);
                Present(screen, burst.Surface, display);
            }
            finally { Native.ReleaseDC(IntPtr.Zero, screen); }
        }

        private void PositionPet(Rectangle area, int maxSide, int visibleSize, double driftTime)
        {
            double across = 0.5 + 0.40 * Math.Sin(driftTime * 0.132 + 1.1)
                + 0.09 * Math.Sin(driftTime * 0.271);
            double down = 0.5 + 0.39 * Math.Sin(driftTime * 0.169 + 0.65)
                + 0.10 * Math.Sin(driftTime * 0.313 + 1.8);
            int x = area.Left + maxSide / 2 + (int)(across * (area.Width - maxSide));
            int y = area.Top + maxSide / 2 + (int)(down * (area.Height - maxSide));
            if (dragTarget.HasValue)
            {
                Point target = dragTarget.Value;
                driftOffset = new Point(Fit(target.X, area.Left, area.Right, visibleSize) - x,
                    Fit(target.Y, area.Top, area.Bottom, visibleSize) - y);
                dragTarget = null;
                dragTime = driftTime;
            }

            // Rejoin the usual drift path gradually from the spot where the pet was dropped.
            double remaining = 1 - Ease((driftTime - dragTime) / 12);
            center = new Point(
                Fit(x + (int)Math.Round(driftOffset.X * remaining), area.Left, area.Right, visibleSize),
                Fit(y + (int)Math.Round(driftOffset.Y * remaining), area.Top, area.Bottom, visibleSize));
        }

        private static int Fit(int center, int start, int end, int side)
        {
            return Math.Max(start + side / 2, Math.Min(center, end - (side - side / 2)));
        }

        internal int AutomaticMaximum { get { return AutomaticSize(Screen.FromPoint(displayPoint)); } }

        private static int AutomaticSize(Screen monitor)
        {
            Rectangle bounds = monitor.Bounds;
            Rectangle area = monitor.WorkingArea;
            return Math.Min(Math.Min(area.Width, area.Height),
                (int)(Math.Sqrt((double)bounds.Width * bounds.Height / Math.PI) * 45.5 / 44));
        }

        internal static double Ease(double value)
        {
            double t = Math.Max(0, Math.Min(1, value));
            return t * t * (3 - 2 * t);
        }

        private void DrawLocal(IntPtr screen, double time, int side, bool lens)
        {
            int x = center.X - side / 2;
            int y = center.Y - side / 2;
            local.Draw(lens ? capture : null, new Rectangle(x, y, side, side), display, renderer.Render(time), renderer.Side);
            Present(screen, local.Surface, new Rectangle(x, y, side, side));
            visibleSide = side;
        }

        private void Present(IntPtr screen, Dib surface, Rectangle bounds)
        {
            var position = new Native.Point(bounds.X, bounds.Y);
            var origin = new Native.Point(0, 0);
            var size = new Native.Point(bounds.Width, bounds.Height);
            var blend = new Native.Blend { Alpha = 255, Format = 1 };
            if (!Native.UpdateLayeredWindow(Handle, screen, ref position, ref size,
                surface.Dc, ref origin, 0, ref blend, 2))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            if (!Native.IsWindowVisible(Handle))
            {
                Native.ShowWindow(Handle, 4);
                Native.DwmFlush();
            }
        }

        internal void SwitchScreen()
        {
            Screen[] screens = Screen.AllScreens;
            if (screens.Length < 2) return;
            Array.Sort(screens, (a, b) =>
            {
                int horizontal = a.Bounds.Left.CompareTo(b.Bounds.Left);
                return horizontal != 0 ? horizontal : a.Bounds.Top.CompareTo(b.Bounds.Top);
            });
            string current = Screen.FromPoint(displayPoint).DeviceName;
            int index = Array.FindIndex(screens, screen => screen.DeviceName == current);
            Rectangle destination = screens[(index + 1) % screens.Length].Bounds;
            displayPoint = new Point(destination.Left + destination.Width / 2, destination.Top + destination.Height / 2);
            dragTarget = null;
            driftOffset = Point.Empty;
            ReleaseBurst();
            if (ScreenChanged != null) ScreenChanged();
        }

        private IntPtr ObserveF7(int code, IntPtr message, IntPtr data)
        {
            if (code == 0 && Marshal.ReadInt32(data) == (int)Keys.F7)
            {
                int kind = message.ToInt32();
                if (kind == 0x101 || kind == 0x105) f7Down = false;
                if ((kind == 0x100 || kind == 0x104) && !f7Down)
                {
                    f7Down = true;
                    bool modified = kind == 0x104 || Native.GetAsyncKeyState((int)Keys.ShiftKey) < 0
                        || Native.GetAsyncKeyState((int)Keys.ControlKey) < 0
                        || Native.GetAsyncKeyState((int)Keys.LWin) < 0 || Native.GetAsyncKeyState((int)Keys.RWin) < 0;
                    if (!modified)
                        Native.PostMessage(Handle, 0x0312, new IntPtr(ScreenShortcut), IntPtr.Zero);
                }
            }
            return Native.CallNextHookEx(keyboardHook, code, message, data);
        }

        private IntPtr ObserveMouse(int code, IntPtr message, IntPtr data)
        {
            if (code == 0)
            {
                int kind = message.ToInt32();
                if (kind == 0x0201 && !MenuBounds.IsEmpty
                    && !MenuBounds.Contains(Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4)))
                    Native.PostMessage(Handle, DismissMenuMessage, IntPtr.Zero, IntPtr.Zero);
                if (kind == 0x0201 && PetInputEnabled && !petRightDown && HitsPet(data)) // WM_LBUTTONDOWN
                {
                    petLeftDown = true;
                    dragMoved = false;
                    dragStart = new Point(Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4));
                    grabOffset = new Point(center.X - dragStart.X, center.Y - dragStart.Y);
                    return new IntPtr(1);
                }
                if (petLeftDown && (kind == 0x0200 || kind == 0x0202)) // WM_MOUSEMOVE / WM_LBUTTONUP
                {
                    int x = Marshal.ReadInt32(data);
                    int y = Marshal.ReadInt32(data, 4);
                    dragMoved = dragMoved || Math.Abs(x - dragStart.X) >= Math.Max(2, dragThreshold.Width / 2)
                        || Math.Abs(y - dragStart.Y) >= Math.Max(2, dragThreshold.Height / 2);
                    if (dragMoved)
                    {
                        dragPointer = new Point(x, y);
                        dragTarget = new Point(x + grabOffset.X, y + grabOffset.Y);
                    }
                    if (kind == 0x0202)
                    {
                        petLeftDown = false;
                        return new IntPtr(1);
                    }
                }

                if (kind == 0x0204 && PetInputEnabled && !petLeftDown && HitsPet(data)) // WM_RBUTTONDOWN
                {
                    petRightDown = true;
                    return new IntPtr(1);
                }
                if (kind == 0x0205 && petRightDown) // WM_RBUTTONUP
                {
                    petRightDown = false;
                    Native.PostMessage(Handle, ShowMenuMessage,
                        new IntPtr(Marshal.ReadInt32(data)), new IntPtr(Marshal.ReadInt32(data, 4)));
                    return new IntPtr(1);
                }
            }
            return Native.CallNextHookEx(mouseHook, code, message, data);
        }

        private bool HitsPet(IntPtr point)
        {
            if (visibleSide == 0) return false;
            double dx = (Marshal.ReadInt32(point) - center.X) / (visibleSide * 0.43);
            double dy = (Marshal.ReadInt32(point, 4) - center.Y) / (visibleSide * 0.24);
            return dx * dx + dy * dy <= 1;
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312 && message.WParam.ToInt32() == ScreenShortcut) // WM_HOTKEY
            {
                SwitchScreen();
                return;
            }
            if (message.Msg == DismissMenuMessage)
            {
                if (MenuDismissRequested != null) MenuDismissRequested();
                return;
            }
            if (message.Msg == ShowMenuMessage)
            {
                if (MenuRequested != null)
                    MenuRequested(new Point(message.WParam.ToInt32(), message.LParam.ToInt32()));
                return;
            }
            if (message.Msg == 0x10) // WM_CLOSE
            {
                Application.ExitThread();
                return;
            }
            base.WndProc(ref message);
        }

        private void ReleaseBurst()
        {
            if (burst == null) return;
            burst.Dispose();
            burst = null;
        }

        public void Dispose()
        {
            if (registeredHotkey)
            {
                Native.UnregisterHotKey(Handle, ScreenShortcut);
                registeredHotkey = false;
            }
            if (keyboardHook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(keyboardHook);
                keyboardHook = IntPtr.Zero;
            }
            if (mouseHook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(mouseHook);
                mouseHook = IntPtr.Zero;
            }
            if (capture != null) capture.Dispose();
            DestroyHandle();
            ReleaseBurst();
            if (local != null) local.Dispose();
        }
    }

    // The magnifier's private filter keeps the pet out of its input without excluding it from recordings.
    internal sealed class FilteredDesktopCapture : IDisposable
    {
        private static readonly Guid NativePixelFormat = new Guid("f5c7ad2d-6a8d-43dd-a7a8-a29935261ae9");
        private readonly Native.MagCallback callback;
        private readonly IntPtr overlay;
        private bool initialized;
        private IntPtr host, control;
        private Size controlSize;
        private int[] target;
        private int width, height, stride, cropX, cropY;
        private bool copied;

        public FilteredDesktopCapture(IntPtr overlay)
        {
            this.overlay = overlay;
            callback = OnImage;
            try
            {
                initialized = Native.MagInitialize();
                if (!initialized) return;
                host = Native.CreateWindowEx(0x00080000, "Static", null, unchecked((int)0x80000000),
                    0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                IsReady = host != IntPtr.Zero;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            finally { if (!IsReady) Dispose(); }
        }

        public bool IsReady { get; private set; }

        public bool Copy(Rectangle bounds, Rectangle display, int[] destination, int destinationStride)
        {
            if (!IsReady) return false;
            var size = new Size(destinationStride, destination.Length / destinationStride);
            if (control == IntPtr.Zero || controlSize != size)
            {
                // Recreate the control when the sample capacity changes to keep its backing surface fixed.
                if (control != IntPtr.Zero) Native.DestroyWindow(control);
                control = Native.CreateWindowEx(0, "Magnifier", null, 0x50000000,
                    0, 0, size.Width, size.Height, host, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (control == IntPtr.Zero) return false;
                if (!Native.MagSetImageScalingCallback(control, callback)
                    || !Native.MagSetWindowFilterList(control, 0, 1, new[] { overlay }))
                {
                    Native.DestroyWindow(control);
                    control = IntPtr.Zero;
                    return false;
                }
                controlSize = size;
            }

            int x = Math.Max(display.Left, Math.Min(bounds.Left, display.Right - size.Width));
            int y = Math.Max(display.Top, Math.Min(bounds.Top, display.Bottom - size.Height));
            cropX = bounds.Left - x;
            cropY = bounds.Top - y;
            width = bounds.Width;
            height = bounds.Height;
            stride = destinationStride;
            target = destination;
            copied = false;
            var source = new Native.Rect(new Rectangle(x, y, size.Width, size.Height));
            try { return Native.MagSetWindowSource(control, source) && copied; }
            finally { target = null; }
        }
        private bool OnImage(IntPtr window, IntPtr source, Native.MagImage image, IntPtr output,
            Native.MagImage outputImage, Native.Rect unclipped, Native.Rect clipped, IntPtr dirty)
        {
            if (target == null || source == IntPtr.Zero || image.Width != controlSize.Width || image.Height != controlSize.Height
                || image.Format != NativePixelFormat || image.Stride < image.Width * 4
                || image.Length.ToUInt64() < (ulong)image.Offset + (ulong)image.Stride * (uint)(cropY + height - 1) + (uint)((cropX + width) * 4))
                return true;

            IntPtr first = IntPtr.Add(source, image.Offset + cropY * image.Stride + cropX * 4);
            if (image.Stride == width * 4 && stride == width)
                Marshal.Copy(first, target, 0, width * height);
            else
                for (int row = 0; row < height; row++)
                    Marshal.Copy(IntPtr.Add(first, row * image.Stride), target, row * stride, width);
            copied = true;
            return true;
        }

        public void Dispose()
        {
            IsReady = false;
            if (host != IntPtr.Zero)
            {
                Native.DestroyWindow(host);
                host = control = IntPtr.Zero;
            }
            if (initialized)
            {
                Native.MagUninitialize();
                initialized = false;
            }
        }
    }

    internal sealed class DesktopLens : IDisposable
    {
        private const int TableSize = 2048;
        private static readonly float[] deflection = new float[TableSize];
        private static readonly float[] radialFilter = new float[TableSize];
        private static readonly int[] opacity = new int[TableSize];
        private readonly int[] scene;
        private readonly int[] frame;
        public Dib Surface { get; private set; }
        public int Capacity { get; private set; }

        static DesktopLens()
        {
            for (int i = 0; i < TableSize; i++)
            {
                double b = 22 * Math.Sqrt((double)i / TableSize);
                double fade = LensWindow.Ease((22 - b) / 6);
                // Fade coverage only at the rim, where the displacement is already subpixel.
                opacity[i] = (int)(255 * LensWindow.Ease((22 - b) / 0.35));
                if (b >= 5 && b < 8.5)
                    radialFilter[i] = (float)((65 / (b * b) - 0.90) * 0.6 * 45.5 / b);
                deflection[i] = (float)(1 - fade * 65 / Math.Max(b * b, 19));
            }
        }

        public DesktopLens(int side)
        {
            Capacity = side;
            scene = new int[side * side];
            frame = new int[scene.Length];
            Surface = new Dib(side, side);
        }

        public void Draw(FilteredDesktopCapture capture, Rectangle bounds, Rectangle display, int[] disk, int diskSize)
        {
            int side = bounds.Width;
            bool captured = capture != null && capture.Copy(bounds, display, scene, Capacity);
            float center = (side - 1) / 2f;
            float radius = side * (22f / 45.5f);
            float toTable = TableSize / (radius * radius);
            float toDisk = (float)(diskSize - 1) / side;

            DesktopPixels.ForRows(side, side, y =>
            {
                float dy = y - center;
                float yy = dy * dy;
                for (int x = 0; x < side; x++)
                {
                    float dx = x - center;
                    int ring = (int)((dx * dx + yy) * toTable);
                    int color = 0;
                    if (ring < TableSize)
                    {
                        int diskPixel = DesktopPixels.Sample(disk, diskSize, x * toDisk, y * toDisk);
                        if (captured && opacity[ring] != 0 && (uint)diskPixel < 0xff000000u)
                        {
                            float scale = deflection[ring];
                            float sx = center + dx * scale;
                            float sy = center + dy * scale;
                            int warped = DesktopPixels.Sample(scene, Capacity, sx, sy);
                            // Near the shadow, several source pixels can fit in one output pixel.
                            float footprint = radialFilter[ring] / side;
                            if (footprint > 0)
                            {
                                float ox = dx * footprint, oy = dy * footprint;
                                int inner = DesktopPixels.Sample(scene, Capacity, sx - ox, sy - oy);
                                int outer = DesktopPixels.Sample(scene, Capacity, sx + ox, sy + oy);
                                warped = DesktopPixels.Mix(warped, DesktopPixels.Mix(inner, outer, 128), 128);
                            }
                            color = DesktopPixels.WithOpacity(warped, opacity[ring]);
                        }
                        color = DesktopPixels.Over(diskPixel, color);
                    }
                    frame[y * Capacity + x] = color;
                }
            });
            Marshal.Copy(frame, 0, Surface.Bits, Capacity * side);
        }

        public void Dispose()
        {
            Surface.Dispose();
        }
    }

    internal static class DesktopPixels
    {
        private static readonly ParallelOptions workers = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount)
        };

        public static void ForRows(int width, int height, Action<int> draw)
        {
            if (width * height >= 180000) Parallel.For(0, height, workers, draw);
            else for (int row = 0; row < height; row++) draw(row);
        }

        public static int Sample(int[] pixels, int stride, float x, float y)
        {
            int ix = (int)x, iy = (int)y;
            int fx = (int)((x - ix) * 256), fy = (int)((y - iy) * 256);
            int i = iy * stride + ix;
            int top = Mix(pixels[i], pixels[i + 1], fx);
            int bottom = Mix(pixels[i + stride], pixels[i + stride + 1], fx);
            return Mix(top, bottom, fy);
        }

        // Interpolate the two alternating byte pairs independently in a packed ARGB pixel.
        internal static int Mix(int a, int b, int weight)
        {
            const uint mask = 0x00ff00ff;
            uint wa = (uint)(256 - weight), wb = (uint)weight;
            uint first = ((((uint)a & mask) * wa + ((uint)b & mask) * wb) >> 8) & mask;
            uint second = ((((uint)a >> 8 & mask) * wa + ((uint)b >> 8 & mask) * wb)) & 0xff00ff00;
            return (int)(first | second);
        }

        public static int WithOpacity(int color, int alpha)
        {
            int rgb = alpha == 255 ? color : Mix(0, color, alpha);
            return (alpha << 24) | (rgb & 0xffffff);
        }

        public static int Over(int top, int bottom)
        {
            int alpha = (int)((uint)top >> 24);
            if (alpha == 0) return bottom;
            if (alpha == 255) return top;
            return top + Mix(0, bottom, 255 - alpha);
        }
    }

    internal sealed class BurstRenderer : IDisposable
    {
        private const int RingCount = 4096;
        private const int RayCount = 2048;
        private const int RayFraction = 32;
        private struct Fragment
        {
            public int Color;
            public float Speed, Tail;
        }
        private readonly Rectangle display;
        private readonly Dib buffer;
        private readonly int[] snapshot, frame;
        private readonly ushort[] rings, directions;
        private readonly Fragment[] fragments = new Fragment[RayCount];
        private readonly float[] crossStrip = new float[RayCount];
        private readonly float[] stripStep = new float[RayCount];
        private readonly int[] twistByRing = new int[RingCount];
        private readonly float leftEdge, rightEdge, topEdge, bottomEdge;
        private readonly int[] opacityByRing = new int[RingCount];
        private readonly float[] radial = new float[RingCount];
        private readonly float[] tangential = new float[RingCount];
        private readonly int width, height;
        private readonly double centerX, centerY;

        public BurstRenderer(Rectangle display, Point center, int maxSide)
        {
            this.display = display;
            width = display.Width;
            height = display.Height;
            centerX = center.X - display.Left;
            centerY = center.Y - display.Top;
            snapshot = new int[width * height];
            frame = new int[snapshot.Length];
            rings = new ushort[snapshot.Length];
            directions = new ushort[snapshot.Length];
            leftEdge = 1f / (float)centerX;
            rightEdge = 1f / (float)(width - 1 - centerX);
            topEdge = 1f / (float)centerY;
            bottomEdge = 1f / (float)(height - 1 - centerY);
            double impactScale = 45.5 / maxSide;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                double dx = x - centerX;
                double dy = y - centerY;
                rings[y * width + x] = (ushort)Math.Min(RingCount - 1,
                    Math.Sqrt(dx * dx + dy * dy) * impactScale * 32);
                directions[y * width + x] = (ushort)((int)((Math.Atan2(dy, dx) + Math.PI)
                    * (RayCount * RayFraction / (2 * Math.PI))) & (RayCount * RayFraction - 1));
            }
            buffer = new Dib(width, height);
        }

        public Dib Surface { get { return buffer; } }

        public bool Capture(FilteredDesktopCapture capture)
        {
            if (!capture.Copy(display, display, snapshot, width)) return false;
            SeedFragments();
            return true;
        }

        public int Draw(int[] disk, int diskSize, int minSide, int maxSide, double attack, bool localLens)
        {
            double release = LensWindow.Ease((attack - LensWindow.SwallowSeconds) / LensWindow.ResetSeconds);
            int sceneOpacity = (int)(255 * LensWindow.Ease(attack / 0.22) * (1 - release));
            float spin = (float)LensWindow.Ease(attack / 1.85);
            float tear = (float)LensWindow.Ease((attack - 0.12) / 0.89);
            float fall = (float)LensWindow.Ease((attack - 0.63) / 1.46);
            float visibility = 1 - (float)LensWindow.Ease((attack - 1.80) / 0.35);
            float contraction = 11 * fall * fall * fall;
            float edgeFeather = Math.Max(1, Math.Min(width, height) * (0.008f + 0.032f * tear));

            // Both the desktop samples and the bright filaments use this same inverse spiral.
            for (int i = 0; i < RingCount; i++)
            {
                double b = i / 32.0;
                double fade = localLens ? LensWindow.Ease((22 - b) / 6) : 0;
                opacityByRing[i] = localLens ? Math.Max(sceneOpacity,
                    (int)(255 * LensWindow.Ease((22 - b) / 0.35) * (1 - release))) : sceneOpacity;
                double lens = 1 - fade * 65 / Math.Max(b * b, 19);
                double angle = -spin * (1.48 + 2.9 / (1 + b * b / 330));
                radial[i] = (float)(lens * Math.Cos(angle));
                tangential[i] = (float)(lens * Math.Sin(angle));
                twistByRing[i] = (int)((angle + 0.026 * tear * Math.Sin(b * 0.29))
                    * (RayCount * RayFraction / (2 * Math.PI)));
            }

            DesktopPixels.ForRows(width, height, y =>
            {
                float dy = (float)(y - centerY);
                float edgeY = dy >= 0 ? dy * bottomEdge : -dy * topEdge;
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (visibility == 0)
                    {
                        frame[index] = sceneOpacity << 24;
                        continue;
                    }

                    int ring = rings[index];
                    float dx = (float)(x - centerX);
                    float edge = Math.Min(1, Math.Max(edgeY, dx >= 0 ? dx * rightEdge : -dx * leftEdge));
                    int direction = (directions[index] + twistByRing[ring]) & (RayCount * RayFraction - 1);
                    int ray = direction / RayFraction;
                    Fragment fragment = fragments[ray];
                    float across = Math.Abs(crossStrip[ray] + (direction & (RayFraction - 1)) * stripStep[ray]);
                    float halfWidth = Math.Max(0.10f, 1.12f - tear * (0.19f + 0.34f * fragment.Speed)
                        - fall * (0.68f + 0.10f * fragment.Speed));
                    float ribbon = Ramp((halfWidth - across) * 12);
                    if (ribbon == 0)
                    {
                        frame[index] = opacityByRing[ring] << 24;
                        continue;
                    }

                    // Adjacent strips slip by different amounts as the seams open.
                    float warp = 1 + tear * (0.46f * (1 - edge) * (2 - edge) + 0.33f * fragment.Speed)
                        + contraction * (0.72f + fragment.Speed * 0.76f);
                    float sx = (float)centerX + warp * (dx * radial[ring] - dy * tangential[ring]);
                    float sy = (float)centerY + warp * (dx * tangential[ring] + dy * radial[ring]);
                    float border = Math.Min(Math.Min(sx, width - 1 - sx), Math.Min(sy, height - 1 - sy));
                    int color = 0;
                    if (border > 0)
                        color = DesktopPixels.Mix(0, DesktopPixels.Sample(snapshot, width, sx, sy),
                            (int)(256 * Ramp(border / edgeFeather)));

                    // The narrow center of each strip glows in its own sampled desktop color.
                    float filament = Ramp((0.40f - across) * 3.6f);
                    float tail = Ramp((fragment.Tail - edge * warp) * 2.1f);
                    float heat = tear * filament * tail * (0.43f + 0.54f * fall);
                    color = DesktopPixels.Mix(color, fragment.Color, (int)(256 * heat));
                    color = DesktopPixels.Mix(0, color, (int)(256 * ribbon * visibility));
                    frame[index] = DesktopPixels.WithOpacity(color, opacityByRing[ring]);
                }
            });
            double collapse = LensWindow.Ease((attack - 1.24) / (LensWindow.SwallowSeconds + 0.25 - 1.24));
            int side = (int)Math.Round(maxSide + (minSide - maxSide) * collapse);
            int left = (int)centerX - side / 2;
            int top = (int)centerY - side / 2;
            float toDisk = (float)(diskSize - 1) / side;
            DesktopPixels.ForRows(side, side, row =>
            {
                int y = top + row;
                for (int x = 0; x < side; x++)
                {
                    int diskPixel = DesktopPixels.Sample(disk, diskSize, x * toDisk, row * toDisk);
                    int index = y * width + left + x;
                    frame[index] = DesktopPixels.Over(diskPixel, frame[index]);
                }
            });

            Marshal.Copy(frame, 0, buffer.Bits, frame.Length);
            return side;
        }
        private void SeedFragments()
        {
            // Every strip derives its color from the screen pixels that it tears away.
            var random = new Random(461);
            for (int start = 0; start < RayCount;)
            {
                int count = Math.Min(RayCount - start, random.Next(13, 35));
                double angle = (start + count * 0.5) * (2 * Math.PI / RayCount) - Math.PI;
                float dx = (float)Math.Cos(angle), dy = (float)Math.Sin(angle);
                float edge = Math.Max(dx >= 0 ? dx * rightEdge : -dx * leftEdge,
                    dy >= 0 ? dy * bottomEdge : -dy * topEdge);
                int brightest = 0, brightness = -1;
                for (int sample = 3; sample <= 24; sample++)
                {
                    float radius = sample / (25 * edge);
                    int rgb = DesktopPixels.Sample(snapshot, width,
                        (float)centerX + radius * dx, (float)centerY + radius * dy);
                    int luminance = (rgb >> 16 & 255) * 3 + (rgb >> 8 & 255) * 6 + (rgb & 255);
                    if (luminance <= brightness) continue;
                    brightness = luminance;
                    brightest = rgb;
                }

                var fragment = new Fragment
                {
                    Color = DesktopPixels.Mix(brightest, 0xffeed5, brightness < 520 ? 175 : 84),
                    Speed = (float)random.NextDouble(),
                    Tail = 1.17f + (float)random.NextDouble() * 0.65f
                };
                for (int offset = 0; offset < count; offset++)
                {
                    int ray = start + offset;
                    fragments[ray] = fragment;
                    crossStrip[ray] = -1 + 2f * offset / count;
                    stripStep[ray] = 2f / (count * RayFraction);
                }
                start += count;
            }
        }
        private static float Ramp(float value) { return Math.Max(0, Math.Min(1, value)); }

        public void Dispose() { buffer.Dispose(); }
    }
    internal sealed class Dib : IDisposable
    {
        public readonly IntPtr Dc;
        public readonly IntPtr Bits;
        private readonly IntPtr bitmap;
        private readonly IntPtr oldBitmap;

        public Dib(int width, int height)
        {
            Dc = Native.CreateCompatibleDC(IntPtr.Zero);
            var header = new Native.BitmapHeader
            {
                Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32
            };
            bitmap = Native.CreateDIBSection(Dc, ref header, 0, out Bits, IntPtr.Zero, 0);
            if (Dc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                Dispose();
                throw new System.ComponentModel.Win32Exception();
            }
            oldBitmap = Native.SelectObject(Dc, bitmap);
        }

        public void Dispose()
        {
            if (oldBitmap != IntPtr.Zero) Native.SelectObject(Dc, oldBitmap);
            if (bitmap != IntPtr.Zero) Native.DeleteObject(bitmap);
            if (Dc != IntPtr.Zero) Native.DeleteDC(Dc);
        }
    }

    public sealed class LensRenderer
    {
        private const double CriticalImpact = 5.196152423;
        private const double OuterDisk = 19.5;
        private const double InnerDisk = 5.75;
        private const int TextureSize = 1024;
        private const int TextureMask = TextureSize - 1;
        private const int BandCount = 8;
        private static readonly float[][] Gas = MakeGasTexture();
        private readonly int[] shifts = new int[BandCount];

        private struct Pixel
        {
            public byte Shadow, Band;
            public int Azimuth;
            public float BandMix, Light, Heat, Glow;
        }

        public int Side { get; private set; }
        private readonly Pixel[] pixels;
        private readonly int[] frame;

        public LensRenderer(int side)
        {
            Side = side;
            pixels = new Pixel[side * side];
            frame = new int[pixels.Length];
            double center = (side - 1) / 2.0;
            double scale = side / 45.5;

            for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                double dx = (x - center) / scale;
                double dy = (y - center) / scale;
                double b = Math.Sqrt(dx * dx + dy * dy);
                if (b >= 22) continue;

                var pixel = new Pixel();
                pixel.Shadow = (byte)(255 * Smooth((CriticalImpact + 0.09 - b) / 0.19));

                double radius, angle;
                if (b > 0.03 && TraceDisk(dx, dy, b, out radius, out angle))
                {
                    double inner = Smooth((radius - InnerDisk) / 0.65);
                    double outer = Smooth((OuterDisk - radius) / 7);
                    double glow = inner * outer * Math.Pow(7 / Math.Max(radius, 7), 0.85);
                    double filament = 0.91 + 0.09 * Math.Sin(radius * 12 + Math.Sin(radius * 3.1));
                    // A gentle brightness gradient keeps both sides of the disk visible.
                    pixel.Light = (float)(glow * filament * (1 + 0.14 * dx / b));
                    pixel.Heat = (float)Clamp(7.7 / radius, 0.28, 1);
                    double band = (radius - InnerDisk) * (BandCount - 1) / (OuterDisk - InnerDisk);
                    pixel.Band = (byte)Math.Min(BandCount - 2, (int)band);
                    pixel.BandMix = (float)(band - pixel.Band);
                    pixel.Azimuth = Phase(angle);
                }

                double photonArc = Math.Exp(-Math.Pow((b - CriticalImpact - 0.09) / 0.10, 2));
                pixel.Glow = (float)(0.24 * photonArc * Smooth((Math.Abs(dy) - 0.5) / 1.4));
                pixels[y * side + x] = pixel;
            }
            BakeBloom();
        }

        private static bool TraceDisk(double x, double y, double impact, out double radius, out double angle)
        {
            // Integrate Schwarzschild null rays in their orbital plane. A ray can cross
            // the equatorial disk several times after bending around the black hole.
            const double sinElevation = 0.115;
            const double cosElevation = 0.993365;
            const double step = 0.023;
            double tangent = -y / impact;
            double crossing = Math.Atan2(sinElevation, -tangent * cosElevation);
            double u = 0;
            double velocity = 1 / impact;

            for (double phi = 0; phi < 10.0; phi += step)
            {
                double halfVelocity = velocity + 0.5 * step * u * (3 * u - 1);
                double next = u + step * halfVelocity;
                if (phi + step >= crossing)
                {
                    double atDisk = u + (next - u) * (crossing - phi) / step;
                    if (atDisk >= 1.0 / OuterDisk && atDisk <= 1.0 / InnerDisk)
                    {
                        radius = 1 / atDisk;
                        double diskX = x / impact * Math.Sin(crossing);
                        double diskY = cosElevation * Math.Cos(crossing)
                            - tangent * sinElevation * Math.Sin(crossing);
                        angle = Math.Atan2(diskX, diskY);
                        return true;
                    }
                    crossing += Math.PI;
                }
                if (next <= 0 || next >= 0.5) break;
                velocity = halfVelocity + 0.5 * step * next * (3 * next - 1);
                u = next;
            }
            radius = angle = 0;
            return false;
        }

        private void BakeBloom()
        {
            var horizontal = new float[pixels.Length];
            // Separable Gaussian spreads light a few pixels past the bright filaments.
            float[] weights = { 0.14f, 0.13f, 0.112f, 0.086f, 0.061f, 0.038f, 0.022f };
            for (int y = 0; y < Side; y++)
            for (int x = 6; x < Side - 6; x++)
            {
                int i = y * Side + x;
                for (int k = -6; k <= 6; k++)
                    horizontal[i] += pixels[i + k].Light * weights[Math.Abs(k)];
            }
            for (int y = 6; y < Side - 6; y++)
            for (int x = 0; x < Side; x++)
            {
                int i = y * Side + x;
                for (int k = -6; k <= 6; k++)
                    pixels[i].Glow += 0.40f * horizontal[i + k * Side] * weights[Math.Abs(k)];
            }
        }

        public int[] Render(double time)
        {
            // Orbital angular speed drops with radius. Neighboring bands blend where they shear.
            for (int band = 0; band < BandCount; band++)
            {
                double radius = InnerDisk + (OuterDisk - InnerDisk) * band / (BandCount - 1);
                double angularSpeed = 1.45 * Math.Pow(8 / radius, 1.5);
                shifts[band] = Phase(time * angularSpeed % (2 * Math.PI));
            }

            for (int i = 0; i < pixels.Length; i++)
            {
                Pixel p = pixels[i];
                int alpha = p.Shadow;
                int r = 0, g = 0, b = 0;
                if (p.Light > 0 || p.Glow > 0)
                {
                    float texture = 0;
                    if (p.Light > 0)
                    {
                        int next = p.Band + 1;
                        float inner = Gas[p.Band][(p.Azimuth - shifts[p.Band]) & TextureMask];
                        float outer = Gas[next][(p.Azimuth - shifts[next]) & TextureMask];
                        texture = inner + (outer - inner) * p.BandMix;
                    }
                    float light = Math.Min(1, p.Light * texture * 1.52f);
                    r = (int)(255 * Math.Min(1, light + p.Glow));
                    g = (int)(255 * Math.Min(1, light * (0.40f + 0.55f * p.Heat) + p.Glow * 0.49f));
                    b = (int)(255 * Math.Min(1, light * (0.15f + 0.73f * p.Heat * p.Heat) + p.Glow * 0.18f));
                    alpha += (255 - alpha) * r / 255;
                }
                frame[i] = (alpha << 24) | (r << 16) | (g << 8) | b;
            }
            return frame;
        }
        private static int Phase(double angle) { return (int)(angle * TextureSize / (2 * Math.PI)); }
        private static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(max, value)); }
        private static double Smooth(double value)
        {
            double t = Clamp(value, 0, 1);
            return t * t * (3 - 2 * t);
        }
        private static float[][] MakeGasTexture()
        {
            var bands = new float[BandCount][];
            for (int band = 0; band < BandCount; band++)
            {
                var cells = new float[TextureSize];
                double phase = band * 0.82;
                for (int i = 0; i < cells.Length; i++)
                {
                    double a = i * 2 * Math.PI / cells.Length;
                    double stream = 0.5 + 0.5 * Math.Cos(3 * a + phase + 0.50 * Math.Sin(5 * a - phase));
                    double knot = 0.5 + 0.5 * Math.Cos(2 * a + phase + 0.65 * Math.Sin(3 * a + phase));
                    double wake = 0.5 + 0.5 * Math.Sin(11 * a - 2 * phase + Math.Cos(4 * a));
                    double detail = 0.5 + 0.5 * Math.Sin(31 * a + phase + 2 * Math.Sin(7 * a));
                    cells[i] = (float)(0.16 + 0.47 * stream * stream + 0.62 * Math.Pow(knot, 8)
                        + (0.09 * wake + 0.07 * detail) * stream);
                }
                bands[band] = cells;
            }
            return bands;
        }
    }
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            public int X, Y;
            public Point(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Blend { public byte Operation, Flags, Alpha, Format; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BitmapHeader
        {
            public int Size, Width, Height;
            public short Planes, BitCount;
            public int Compression, ImageSize, XResolution, YResolution, ColorsUsed, ImportantColors;
        }

        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            public int Left, Top, Right, Bottom;
            public Rect(Rectangle bounds)
            {
                Left = bounds.Left; Top = bounds.Top; Right = bounds.Right; Bottom = bounds.Bottom;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MagImage
        {
            public int Width, Height;
            public Guid Format;
            public int Stride, Offset;
            public UIntPtr Length;
        }

        internal delegate bool MagCallback(IntPtr window, IntPtr source, MagImage image, IntPtr output,
            MagImage outputImage, Rect unclipped, Rect clipped, IntPtr dirty);

        [DllImport("Magnification.dll")] internal static extern bool MagInitialize();
        [DllImport("Magnification.dll")] internal static extern bool MagUninitialize();
        [DllImport("Magnification.dll")] internal static extern bool MagSetImageScalingCallback(IntPtr window, MagCallback callback);
        [DllImport("Magnification.dll")] internal static extern bool MagSetWindowFilterList(IntPtr window, uint mode, int count, IntPtr[] excluded);
        [DllImport("Magnification.dll")] internal static extern bool MagSetWindowSource(IntPtr window, Rect source);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr CreateWindowEx(int exStyle, string className, string name, int style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr module, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool DestroyWindow(IntPtr window);

        internal delegate IntPtr HookCallback(int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr SetWindowsHookEx(int kind, HookCallback callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("dwmapi.dll")] internal static extern int DwmFlush();
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destination, ref Point position,
            ref Point size, IntPtr source, ref Point origin, int colorKey, ref Blend blend, int flags);
        [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")]
        internal static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapHeader header, int usage,
            out IntPtr pixels, IntPtr section, int offset);
    }
}
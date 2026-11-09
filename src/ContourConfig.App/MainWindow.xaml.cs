using ContourConfig.Core;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ContourConfig.App
{
    internal sealed partial class MainWindow : Window
    {
        private readonly SettingsStore settingsStore = new(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContourConfig", "settings.json"));
        private readonly CancellationTokenSource lifetime = new();
        private readonly DispatcherTimer displayTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly DispatcherTimer reconnectTimer = new() { Interval = TimeSpan.FromSeconds(5) };
        private bool updatingStartup;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        // Guards state shared between the HID read loop, the boost thread and the UI thread.
        private readonly Lock gate = new();
        private EndMarkerMap markers = new();
        private RollerMouseHid? device;
        private CancellationTokenSource? connection;
        private EndDriftBooster? booster;
        private RollerEnd currentEnd;
        private RollerEnd? shownEnd;
        private Thread? boostThread;
        private HwndSource? source;
        private bool hotkeyRegistered;
        private bool closed;
        private bool started;
        private bool trayHintShown;
        private TrayIcon? tray;

        public MainWindow()
        {
            InitializeComponent();
            displayTimer.Tick += OnDisplayTick;
        }

        // Shows the window, e.g. from the tray icon or when the app is launched again.
        internal void ShowFromTray()
        {
            if (closed)
            {
                return;
            }
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            _ = Activate();
        }

        // Starts everything without showing the window (autostart into the notification area).
        internal void StartHidden() => _ = new WindowInteropHelper(this).EnsureHandle();

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WindowProc);
            tray = new();
            tray.OpenRequested += (_, _) => ShowFromTray();
            tray.BoostChangeRequested += (_, enabled) => EnableBoost.IsChecked = enabled;
            tray.ExitRequested += (_, _) => Close();
            _ = InitializeAsync();
        }

        private void HideToTray()
        {
            Hide();
            if (!trayHintShown)
            {
                trayHintShown = true;
                tray?.ShowNotification("Contour Config is still running",
                    "It keeps boosting from the notification area. Right-click the icon to exit.");
            }
        }

        private async Task InitializeAsync()
        {
            try
            {
                DriftConfiguration? saved = await settingsStore.LoadAsync(lifetime.Token);
                if (saved is not null)
                {
                    LeftSpeed.Value = saved.Options.LeftAdditionalSpeed;
                    RightSpeed.Value = saved.Options.RightAdditionalSpeed;
                    Delay.Value = saved.Options.ActivationDelayMilliseconds;
                    markers = saved.Markers;
                    EnableOnStart.IsChecked = saved.EnableBoostOnStart;
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                ShowError($"Could not load settings: {exception.Message} Defaults are used.");
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                return;
            }
            if (closed)
            {
                return;
            }

            const uint ctrlAltNoRepeat = 0x0002 | 0x0001 | 0x4000;
            const uint f10 = 0x79;
            hotkeyRegistered = source is not null &&
                NativeMethods.RegisterHotKey(source.Handle, NativeMethods.StopHotkeyId, ctrlAltNoRepeat, f10);
            if (!hotkeyRegistered)
            {
                EnableBoost.IsEnabled = false;
                ShowError($"Could not register Ctrl+Alt+F10 ({new Win32Exception(Marshal.GetLastWin32Error()).Message}). Boost is unavailable without its emergency stop.");
            }
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            boostThread = new(BoostLoop) { IsBackground = true, Name = "Drift boost", Priority = ThreadPriority.AboveNormal };
            boostThread.Start();
            Connect();
            displayTimer.Start();
            reconnectTimer.Tick += OnReconnectTick;
            reconnectTimer.Start();
            updatingStartup = true;
            StartWithWindows.IsChecked = StartupRegistration.IsEnabled();
            updatingStartup = false;
            started = true;
            if (EnableOnStart.IsChecked == true && hotkeyRegistered)
            {
                EnableBoost.IsChecked = true;
            }
        }

        private void OnReconnectTick(object? sender, EventArgs e)
        {
            // Bluetooth devices disappear while asleep; quietly retry so autostart works without clicking Reconnect.
            if (device is null && !closed)
            {
                Connect();
            }
        }

        private void SetStartup(bool enabled)
        {
            try
            {
                StartupRegistration.Set(enabled);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                ShowError($"Could not change Start with Windows: {exception.Message}");
                updatingStartup = true;
                StartWithWindows.IsChecked = StartupRegistration.IsEnabled();
                updatingStartup = false;
            }
        }

        private void OnStartWithWindowsChanged(object sender, RoutedEventArgs e)
        {
            if (updatingStartup || !started)
            {
                return;
            }
            bool enabled = StartWithWindows.IsChecked == true;
            SetStartup(enabled);
            if (StartupRegistration.IsEnabled() == enabled)
            {
                Status.Text = enabled
                    ? "Contour Config will start in the notification area when you sign in to Windows."
                    : "Contour Config will no longer start with Windows.";
            }
        }

        private void Connect()
        {
            Disconnect();
            try
            {
                device = RollerMouseHid.Open();
            }
            catch (Win32Exception exception)
            {
                DeviceStatus.Text = $"Error: {exception.Message}";
                return;
            }
            if (device is null)
            {
                DeviceStatus.Text = "RollerMouse not found. Waiting for it to connect (move it to wake it up)...";
                return;
            }
            DeviceStatus.Text = "Connected to the RollerMouse end-marker channel.";
            connection = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            _ = ReadLoopAsync(device, connection.Token);
        }

        private void Disconnect()
        {
            connection?.Cancel();
            connection?.Dispose();
            connection = null;
            device?.Dispose();
            device = null;
            SetEnd(RollerEnd.None);
        }

        private async Task ReadLoopAsync(RollerMouseHid hid, CancellationToken cancellationToken)
        {
            string message = "RollerMouse disconnected. Reconnecting automatically when it is back...";
            try
            {
                await hid.ReadAsync(OnReport, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                message = $"Lost the RollerMouse ({exception.Message}). Reconnecting automatically when it is back...";
            }
            SetEnd(RollerEnd.None);
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (!closed && device == hid)
                {
                    Disconnect();
                    DeviceStatus.Text = message;
                }
            });
        }

        private void OnReport(byte[] report)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            lock (gate)
            {
                if (markers.Decode(report) is RollerEnd end)
                {
                    currentEnd = end;
                    booster?.Update(end, now);
                }
            }
        }

        private void SetEnd(RollerEnd end)
        {
            lock (gate)
            {
                currentEnd = end;
                booster?.Update(end, clock.Elapsed.TotalMilliseconds);
            }
        }

        private void BoostLoop()
        {
            NativeMethods.Input[] input = [new() { Type = 0 }];
            while (!lifetime.IsCancellationRequested)
            {
                int dx;
                lock (gate)
                {
                    dx = booster?.Tick(clock.Elapsed.TotalMilliseconds) ?? 0;
                }
                if (dx != 0)
                {
                    input[0].Data.Mouse = new()
                    {
                        X = dx,
                        Flags = NativeMethods.MouseMoveRelative,
                        ExtraInfo = NativeMethods.InjectionTag
                    };
                    _ = NativeMethods.SendInput(1, input, Marshal.SizeOf<NativeMethods.Input>());
                }
                Thread.Sleep(5);
            }
        }

        private DriftOptions ReadOptions() => new(LeftSpeed.Value, RightSpeed.Value, (int)Delay.Value);

        private void StartBoost()
        {
            EndDriftBooster created = new(ReadOptions());
            lock (gate)
            {
                created.Update(currentEnd, clock.Elapsed.TotalMilliseconds);
                booster = created;
            }
        }

        private void StopBoost()
        {
            lock (gate)
            {
                booster = null;
            }
        }

        private void EmergencyStop()
        {
            StopBoost();
            EnableBoost.IsChecked = false;
            Status.Text = "Boost stopped.";
        }

        private void OnDisplayTick(object? sender, EventArgs e)
        {
            RollerEnd end;
            bool boosting;
            lock (gate)
            {
                end = currentEnd;
                boosting = booster is not null;
            }
            if (end != shownEnd)
            {
                shownEnd = end;
                ShowIndicator(LeftIndicator, LeftIndicatorText, end == RollerEnd.Left);
                ShowIndicator(RightIndicator, RightIndicatorText, end == RollerEnd.Right);
            }
            BoostState.Text = boosting ? "Boost is on" : "Boost is off";
            tray?.Update($"Contour Config\nBoost {(boosting ? "on" : "off")} · {(device is null ? "RollerMouse not connected" : "RollerMouse connected")}",
                EnableBoost.IsChecked == true, EnableBoost.IsEnabled);
        }

        // Theme brushes are referenced (not copied) so the indicators follow light/dark mode changes.
        private static void ShowIndicator(Border indicator, TextBlock text, bool active)
        {
            indicator.SetResourceReference(Border.BackgroundProperty,
                active ? "AccentFillColorDefaultBrush" : "ControlFillColorDefaultBrush");
            indicator.SetResourceReference(Border.BorderBrushProperty,
                active ? "AccentFillColorDefaultBrush" : "ControlStrokeColorDefaultBrush");
            text.SetResourceReference(TextBlock.ForegroundProperty,
                active ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
            System.Windows.Automation.AutomationProperties.SetHelpText(indicator, active ? "Active" : "Inactive");
        }

        private async void OnSaveSettings(object sender, RoutedEventArgs e)
        {
            try
            {
                await settingsStore.SaveAsync(new()
                {
                    Options = ReadOptions(),
                    Markers = markers,
                    EnableBoostOnStart = EnableOnStart.IsChecked == true
                }, lifetime.Token);
                Status.Text = "Settings saved.";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                ShowError($"Could not save settings: {exception.Message}");
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                // Closing the window intentionally cancels outstanding file operations.
            }
        }

        private void OnOptionsChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (started && EnableBoost.IsChecked == true)
            {
                StartBoost();
            }
        }

        private void OnEnableBoost(object sender, RoutedEventArgs e)
        {
            StartBoost();
            Status.Text = "Boost enabled. Hold a roller end to drift faster.";
        }

        private void OnDisableBoost(object sender, RoutedEventArgs e)
        {
            StopBoost();
            Status.Text = "Boost is off.";
        }

        private void ShowError(string message) => Status.Text = $"Error: {message}";

        private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
        {
            if (message == NativeMethods.WmHotkey && wParam == NativeMethods.StopHotkeyId)
            {
                EmergencyStop();
                handled = true;
            }
            // The close button, Alt+F4 and minimize send the app to the tray. A plain WM_CLOSE
            // (tray Exit, installer, Windows shutdown) still closes it.
            else if (message == NativeMethods.WmSysCommand &&
                (wParam.ToInt64() & 0xFFF0) is NativeMethods.ScClose or NativeMethods.ScMinimize)
            {
                HideToTray();
                handled = true;
            }
            return 0;
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            bool resumed = e.Mode == PowerModes.Resume;
            _ = Dispatcher.BeginInvoke(() =>
            {
                EmergencyStop();
                Status.Text = "Power state changed. Boost is off.";
                if (resumed)
                {
                    Connect();
                    RestoreAutomaticBoost();
                }
            });
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            bool returned = e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon
                or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect;
            _ = Dispatcher.BeginInvoke(() =>
            {
                EmergencyStop();
                Status.Text = "Windows session changed. Boost is off.";
                if (returned)
                {
                    RestoreAutomaticBoost();
                }
            });
        }

        // Only the explicit "enable automatically" setting turns boost back on after a lock or sleep.
        private void RestoreAutomaticBoost()
        {
            if (!closed && hotkeyRegistered && EnableOnStart.IsChecked == true)
            {
                EnableBoost.IsChecked = true;
            }
        }

        // Runs however the window goes away (Exit, installer WM_CLOSE, or its hidden owner being destroyed).
        private void OnClosed(object? sender, EventArgs e)
        {
            closed = true;
            tray?.Dispose();
            tray = null;
            StopBoost();
            lifetime.Cancel();
            displayTimer.Stop();
            reconnectTimer.Stop();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            if (hotkeyRegistered && source is not null)
            {
                _ = NativeMethods.UnregisterHotKey(source.Handle, NativeMethods.StopHotkeyId);
            }
            source?.RemoveHook(WindowProc);
            boostThread?.Join(500);
            Disconnect();
            lifetime.Dispose();
        }

        private void OnReconnect(object sender, RoutedEventArgs e) => Connect();
        private void OnStopBoost(object sender, RoutedEventArgs e) => EmergencyStop();
    }
}

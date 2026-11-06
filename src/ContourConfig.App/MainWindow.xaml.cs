using ContourConfig.Core;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ContourConfig.App
{
    internal sealed partial class MainWindow : Window
    {
        private const int LearnMilliseconds = 4000;
        private const int MinimumLearnedHoldMilliseconds = 1500;
        private readonly SettingsStore settingsStore = new(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContourConfig", "settings.json"));
        private readonly CancellationTokenSource lifetime = new();
        private readonly DispatcherTimer displayTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly Stopwatch clock = Stopwatch.StartNew();
        // Guards state shared between the HID read loop, the boost thread and the UI thread.
        private readonly Lock gate = new();
        private EndMarkerMap markers = new();
        private RollerMouseHid? device;
        private CancellationTokenSource? connection;
        private EndDriftBooster? booster;
        private MarkerLearner? learner;
        private RollerEnd currentEnd;
        private byte? lastMarkers;
        private long reportCount;
        private long sentUnits;
        private Thread? boostThread;
        private HwndSource? source;
        private bool hotkeyRegistered;
        private bool closed;

        public MainWindow()
        {
            InitializeComponent();
            displayTimer.Tick += OnDisplayTick;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
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

            source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WindowProc);
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
            ShowMarkerMapping();
            Connect();
            displayTimer.Start();
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
                DeviceStatus.Text = "RollerMouse not found. Make sure it is connected and awake, then click Reconnect.";
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
            string message = "RollerMouse disconnected. Click Reconnect when it is back.";
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
                message = $"Lost the RollerMouse ({exception.Message}). Click Reconnect when it is back.";
            }
            SetEnd(RollerEnd.None);
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (!closed && device == hid)
                {
                    DeviceStatus.Text = message;
                }
            });
        }

        private void OnReport(byte[] report)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            lock (gate)
            {
                reportCount++;
                if (EndMarkerMap.ReadMarkerByte(report) is not byte value)
                {
                    return;
                }
                lastMarkers = value;
                learner?.Observe(value, now);
                currentEnd = markers.Decode(report) ?? RollerEnd.None;
                booster?.Update(currentEnd, now);
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
                    if (NativeMethods.SendInput(1, input, Marshal.SizeOf<NativeMethods.Input>()) == 1)
                    {
                        Interlocked.Add(ref sentUnits, Math.Abs(dx));
                    }
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

        private async Task LearnAsync(RollerEnd end)
        {
            EmergencyStop();
            LearnLeft.IsEnabled = LearnRight.IsEnabled = false;
            Status.Text = $"Hold the {(end == RollerEnd.Left ? "left" : "right")} end of the roller now for {LearnMilliseconds / 1000} seconds...";
            lock (gate)
            {
                learner = new();
                learner.Observe(lastMarkers ?? 0, clock.Elapsed.TotalMilliseconds);
            }
            try
            {
                await Task.Delay(LearnMilliseconds, lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                LearnLeft.IsEnabled = LearnRight.IsEnabled = true;
            }
            byte? mask;
            lock (gate)
            {
                mask = learner?.Finish(clock.Elapsed.TotalMilliseconds, MinimumLearnedHoldMilliseconds);
                learner = null;
            }
            if (mask is not byte learned)
            {
                ShowError("No end marker was held long enough. Check the Contour drift setting is Slow or Fast and try again.");
                return;
            }
            EndMarkerMap updated = end == RollerEnd.Left ? markers with { LeftMask = learned } : markers with { RightMask = learned };
            try
            {
                updated.Validate();
            }
            catch (ArgumentException exception)
            {
                ShowError($"Learned 0x{learned:X2}, but {exception.Message} Learn the other end again.");
                return;
            }
            lock (gate)
            {
                markers = updated;
            }
            ShowMarkerMapping();
            Status.Text = string.Create(CultureInfo.InvariantCulture, $"Learned {end} end = 0x{learned:X2}. Click Save settings to keep it.");
        }

        private void ShowMarkerMapping() => MarkerMapping.Text = string.Create(CultureInfo.InvariantCulture,
            $"Mapping: left = 0x{markers.LeftMask:X2}, right = 0x{markers.RightMask:X2} (report 0x{EndMarkerMap.ReportId:X2}, byte {EndMarkerMap.MarkerByteIndex})");

        private void OnDisplayTick(object? sender, EventArgs e)
        {
            byte? value;
            RollerEnd end;
            long reports;
            bool boosting;
            lock (gate)
            {
                value = lastMarkers;
                end = currentEnd;
                reports = reportCount;
                boosting = booster is not null;
            }
            string left = end == RollerEnd.Left ? "[LEFT]" : " left ";
            string right = end == RollerEnd.Right ? "[RIGHT]" : " right ";
            MarkerMonitor.Text = string.Create(CultureInfo.InvariantCulture,
                $"{left}  {right}   markers=0x{value ?? 0:X2}  reports={reports}\nBoost: {(boosting ? "ON" : "off")}  extra units sent: {Interlocked.Read(ref sentUnits)}");
        }

        private async void OnSaveSettings(object sender, RoutedEventArgs e)
        {
            try
            {
                await settingsStore.SaveAsync(new() { Options = ReadOptions(), Markers = markers }, lifetime.Token);
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
            if (IsLoaded && EnableBoost.IsChecked == true)
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
            return 0;
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                EmergencyStop();
                Status.Text = "Power state changed. Boost is off.";
            });
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                EmergencyStop();
                Status.Text = "Windows session changed. Boost is off.";
            });
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            closed = true;
            StopBoost();
            lifetime.Cancel();
            displayTimer.Stop();
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
        private async void OnLearnLeft(object sender, RoutedEventArgs e) => await LearnAsync(RollerEnd.Left);
        private async void OnLearnRight(object sender, RoutedEventArgs e) => await LearnAsync(RollerEnd.Right);
        private void OnStopBoost(object sender, RoutedEventArgs e) => EmergencyStop();

        private void OnResetMarkers(object sender, RoutedEventArgs e)
        {
            lock (gate)
            {
                markers = new();
            }
            ShowMarkerMapping();
            Status.Text = "Marker mapping reset to defaults. Click Save settings to keep it.";
        }
    }
}

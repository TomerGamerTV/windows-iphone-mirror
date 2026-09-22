using Microsoft.Win32;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;using iPhoneMirror.Core;

namespace iPhoneMirror.App;

public partial class MainWindow : Window
{
    private readonly RuntimePaths _paths = new();
    private readonly AppSettings _settings;
    private readonly CommandLineOptions _launchOptions;
    private WorkerProtocolClient? _worker;
    private MpvSession? _mpv;
    private double _sourceWidth = 390;
    private double _sourceHeight = 844;
    private bool _touchActive;
    private bool _sessionActive;
    private bool _sessionRunning;
    private bool _stopping;
    private bool _closing;
    private bool _allowClose;
    private bool _softwareFallbackUsed;
    private string? _lastErrorCode;
    private readonly System.Windows.Threading.DispatcherTimer _viewerToolbarTimer;
    private readonly System.Windows.Threading.DispatcherTimer _inputOverlayTimer;
    private ViewerToolbarWindow? _viewerToolbarWindow;
    private bool _automaticReconnect;
    private readonly HashSet<int> _heldUsages = [];
    private readonly HashSet<Key> _suppressedPasteKeys = [];
    private BackdropType _currentBackdrop = BackdropType.Acrylic;
    private bool _sheetDragArmed;
    private Point _sheetDragStart;

    public MainWindow(CommandLineOptions launchOptions)
    {
        _launchOptions = launchOptions;
        _paths.EnsureCreated();
        _settings = SettingsStore.Load(_paths.SettingsFile);
        if (launchOptions.Connection is not null) _settings.Connection = launchOptions.Connection.Value;
        if (!string.IsNullOrWhiteSpace(launchOptions.Serial)) _settings.Serial = launchOptions.Serial;
        _currentBackdrop = ParseBackdrop(_settings.Backdrop);

        InitializeComponent();
        WifiAddressBox.Text = _settings.WifiAddress ?? string.Empty;
        WifiPortBox.Text = _settings.WifiPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Loaded += MainWindow_Loaded;
        SourceInitialized += (_, _) => NativeTheme.Apply(this, _currentBackdrop);
        Deactivated += async (_, _) => await ReleaseInputAsync();
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewKeyUp += MainWindow_PreviewKeyUp;
        MpvHost.NativeMouse += MpvHost_NativeMouse;
        _viewerToolbarTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _viewerToolbarTimer.Tick += (_, _) =>
        {
            _viewerToolbarTimer.Stop();
            _viewerToolbarWindow?.HideAnimated();
        };
        _inputOverlayTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _inputOverlayTimer.Tick += (_, _) =>
        {
            if (_sessionActive && !_stopping) MpvHost.BringInputOverlayToFront();
        };
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        SelectConnectionMode(_settings.Connection);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        NativeTheme.Apply(this, _currentBackdrop);
        HwDecodeBox.IsChecked = _settings.PreferHardwareDecode;
        AlwaysOnTopBox.IsChecked = _settings.AlwaysOnTop;
        Topmost = _settings.AlwaysOnTop;
        UpdateBackdropButtons();
        _viewerToolbarWindow ??= new ViewerToolbarWindow(this);
        _viewerToolbarWindow.ActionRequested += ViewerToolbarWindow_ActionRequested;
        await RefreshDevicesAsync();
        if (_launchOptions.Command is "start" or "restart") await ConnectAsync();
    }

    private static BackdropType ParseBackdrop(string? name) => name switch
    {
        "Mica" => BackdropType.Mica,
        "MicaAlt" => BackdropType.MicaAlt,
        "None" or "Solid" => BackdropType.None,
        _ => BackdropType.Acrylic,
    };

    private void ApplyBackdrop(BackdropType backdrop)
    {
        _currentBackdrop = backdrop;
        _settings.Backdrop = backdrop switch
        {
            BackdropType.Mica => "Mica",
            BackdropType.MicaAlt => "MicaAlt",
            BackdropType.None => "Solid",
            _ => "Acrylic",
        };
        SettingsStore.Save(_paths.SettingsFile, _settings);
        NativeTheme.Apply(this, _currentBackdrop);
        if (_viewerToolbarWindow is not null)
            NativeTheme.Apply(_viewerToolbarWindow, _currentBackdrop);
        UpdateBackdropButtons();
    }

    private void UpdateBackdropButtons()
    {
        if (!IsLoaded) return;
        SetSegmentSelected(BackdropAcrylicBtn, _currentBackdrop == BackdropType.Acrylic);
        SetSegmentSelected(BackdropMicaBtn, _currentBackdrop == BackdropType.Mica);
        SetSegmentSelected(BackdropSolidBtn, _currentBackdrop == BackdropType.None);
    }

    private static void SetSegmentSelected(Button button, bool selected)
    {
        if (selected)
        {
            button.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
            button.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        }
        else
        {
            button.SetResourceReference(Control.BackgroundProperty, "SegmentBrush");
            button.SetResourceReference(Control.ForegroundProperty, "SegmentTextBrush");
        }
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) =>
        Dispatcher.Invoke(() =>
        {
            NativeTheme.Apply(this, _currentBackdrop);
            UpdateBackdropButtons();
        });

    private void SelectConnectionMode(ConnectionMode mode)
    {
        foreach (var item in ConnectionModeBox.Items.OfType<ComboBoxItem>())
        {
            item.IsSelected = string.Equals(item.Tag?.ToString(), mode.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        WifiFieldsGrid.Visibility = mode == ConnectionMode.Wifi ? Visibility.Visible : Visibility.Collapsed;
    }

    private ConnectionMode SelectedConnectionMode() =>
        (ConnectionModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
        {
            "usb" => ConnectionMode.Usb,
            "wifi" => ConnectionMode.Wifi,
            _ => ConnectionMode.Auto,
        };

    private string? SelectedSerial() => (DeviceBox.SelectedItem as DeviceInfo)?.Serial ?? _settings.Serial;

    private string? SelectedWifiAddress() => string.IsNullOrWhiteSpace(WifiAddressBox.Text) ? null : WifiAddressBox.Text.Trim();

    private int SelectedWifiPort() => int.TryParse(WifiPortBox.Text, out var port) && port is > 0 and <= 65535 ? port : 49152;

    private async Task EnsureWorkerAsync()
    {
        if (_worker is not null && !_worker.HasExited) return;
        var python = RuntimeLocator.FindPython() ?? throw new WorkerCommandException("worker_missing", ErrorCatalog.MessageFor("worker_missing"));
        var script = RuntimeLocator.FindWorker() ?? throw new WorkerCommandException("worker_missing", ErrorCatalog.MessageFor("worker_missing"));
        _worker = await WorkerProtocolClient.StartAsync(python, script);
        _worker.EventReceived += Worker_EventReceived;
        _worker.WorkerExited += Worker_WorkerExited;
    }

    private async Task RefreshDevicesAsync()
    {
        try
        {
            await EnsureWorkerAsync();
            var data = await _worker!.SendCommandAsync("list_devices", timeout: TimeSpan.FromSeconds(10));
            var devices = data.GetProperty("devices").EnumerateArray()
                .Select(item => new DeviceInfo(
                    item.GetProperty("serial").GetString() ?? string.Empty,
                    item.TryGetProperty("name", out var name) ? name.GetString() : null,
                    item.GetProperty("transport").GetString() ?? "usb",
                    item.GetProperty("trusted").GetBoolean(),
                    item.GetProperty("developer_mode").GetBoolean(),
                    item.GetProperty("wifi_paired").GetBoolean(),
                    item.TryGetProperty("state_error", out var stateError) ? stateError.GetString() : null))
                .ToList();
            DeviceBox.ItemsSource = devices;
            DeviceBox.SelectedItem = devices.FirstOrDefault(device => device.Serial == _settings.Serial)
                ?? (devices.Count == 1 ? devices[0] : null);
            SetupStatusText.Text = devices.Count == 0
                ? "No iPhone detected."
                : devices.Count > 1 && DeviceBox.SelectedItem is null
                    ? $"Detected {devices.Count} iPhones. Select one before connecting."
                    : $"Detected {devices.Count} iPhone{(devices.Count == 1 ? string.Empty : "s")}.";
        }
        catch (WorkerCommandException error)
        {
            SetError(error.Code);
        }
        catch
        {
            SetError("connection_failed");
        }
    }

    private void SetCaptionStatus(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            CaptionStatus.Visibility = Visibility.Collapsed;
            CaptionStatus.Text = string.Empty;
            return;
        }
        CaptionStatus.Text = text;
        CaptionStatus.Visibility = Visibility.Visible;
    }

    private async Task ConnectAsync(bool preferSoftwareDecode = false)
    {
        if (_stopping) return;
        try
        {
            ConnectButton.IsEnabled = false;
            StatusText.Text = "Connecting…";
            SetCaptionStatus("Connecting…");
            EmptyMessage.Text = "Opening the CoreDevice display stream…";
            await StopSessionAsync(updateUi: false);
            await EnsureWorkerAsync();

            var mpv = RuntimeLocator.FindMpv();
            if (mpv is null) throw new WorkerCommandException("player_missing", ErrorCatalog.MessageFor("player_missing"));
            // At idle the host is collapsed so the WPF empty-state renders (an HWND
            // always covers WPF siblings). Recreate it here and wait for the handle.
            MpvHost.Visibility = Visibility.Visible;
            if (!await WaitForHostHandleAsync()) throw new InvalidOperationException("The video host is not ready.");

            var videoPipeName = "iPhoneMirror.Video." + Guid.NewGuid().ToString("N");
            _mpv = new MpvSession(mpv, MpvHost.HostHandle, _settings.PreferHardwareDecode && !preferSoftwareDecode);
            _mpv.Exited += Mpv_Exited;
            _mpv.Start(videoPipeName);
            MpvHost.BringInputOverlayToFront();
            ReassertInputOverlayZOrder();

            _settings.Connection = SelectedConnectionMode();
            _settings.Serial = SelectedSerial();
            _settings.WifiAddress = SelectedWifiAddress();
            _settings.WifiPort = SelectedWifiPort();
            SettingsStore.Save(_paths.SettingsFile, _settings);
            await _worker!.SendCommandAsync("start_session", new
            {
                connection = _settings.Connection.ToString().ToLowerInvariant(),
                serial = _settings.Serial,
                wifi_address = _settings.WifiAddress,
                wifi_port = _settings.WifiPort,
                video_pipe = videoPipeName,
            }, TimeSpan.FromSeconds(8));
            _sessionActive = true;
            _softwareFallbackUsed = preferSoftwareDecode;
            _inputOverlayTimer.Start();
        }
        catch (WorkerCommandException error)
        {
            await StopSessionAsync(updateUi: false);
            SetError(error.Code);
        }
        catch
        {
            await StopSessionAsync(updateUi: false);
            SetError("connection_failed");
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private async Task<bool> WaitForHostHandleAsync()
    {
        for (var i = 0; i < 40; i++)
        {
            if (MpvHost.HostHandle != IntPtr.Zero) return true;
            await Task.Delay(50);
        }
        return MpvHost.HostHandle != IntPtr.Zero;
    }

    private void ViewerFrame_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Idle outside-tap: no HWND exists while the host is collapsed, so WPF
        // handles viewer clicks directly here (the native handler covers live video).
        if (SettingsSheet.Visibility != Visibility.Visible) return;
        for (DependencyObject? d = e.OriginalSource as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is ButtonBase || d is TextBoxBase || d is ComboBox || d is CheckBox || d is Thumb
                || d is System.Windows.Controls.Primitives.ScrollBar) return;
        }
        HideSettingsSheet();
    }

    private async Task StopSessionAsync(bool updateUi = true)
    {
        if (_stopping) return;
        _stopping = true;
        try
        {
            await ReleaseInputAsync();
            if (_worker is not null && !_worker.HasExited && _sessionActive)
            {
                try { await _worker.SendCommandAsync("stop_session", timeout: TimeSpan.FromSeconds(14)); } catch { }
            }
            _sessionActive = false;
            _inputOverlayTimer.Stop();
            // Collapse the host so the HWND no longer covers the WPF idle UI.
            MpvHost.Visibility = Visibility.Collapsed;
            if (_mpv is not null)
            {
                await _mpv.DisposeAsync();
                _mpv = null;
            }
            if (updateUi)
            {
                StatusText.Text = "Disconnected";
                SetCaptionStatus(string.Empty);
                DeviceStatusText.Text = "No active session";
                EmptyOverlay.Visibility = Visibility.Visible;
                EmptyMessage.Text = "Connect over USB or a previously paired Wi-Fi connection.";
                ConnectionDot.Fill = (Brush)FindResource("MutedTextBrush");
            }
        }
        finally
        {
            _stopping = false;
        }
    }

    private async Task ReleaseInputAsync()
    {
        _touchActive = false;
        _heldUsages.Clear();
        _suppressedPasteKeys.Clear();
        if (_worker is not null && !_worker.HasExited && _sessionActive)
        {
            try { await _worker.SendCommandAsync("release_input", timeout: TimeSpan.FromSeconds(2)); } catch { }
        }
    }

    private void Worker_EventReceived(object? sender, WorkerEvent e) => Dispatcher.InvokeAsync(() => ApplyWorkerEventAsync(e));

    private async Task ApplyWorkerEventAsync(WorkerEvent e)
    {
        if (e.Name == "video_format")
        {
            _sourceWidth = e.Data.GetProperty("width").GetDouble();
            _sourceHeight = e.Data.GetProperty("height").GetDouble();
            MpvHost.BringInputOverlayToFront();
            ReassertInputOverlayZOrder();
            return;
        }
        if (e.Name == "input_state")
        {
            StatusText.Text = e.Data.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean()
                ? "Input reconnected"
                : "Input disconnected";
            return;
        }
        if (e.Name != "state") return;

        var state = e.Data.TryGetProperty("state", out var stateValue) ? stateValue.GetString() : null;
        var transport = e.Data.TryGetProperty("active_connection", out var connectionValue) ? connectionValue.GetString() : null;
        switch (state)
        {
            case "starting":
                _lastErrorCode = null;
                _sessionRunning = false;
                StatusText.Text = "Starting display stream…";
                SetCaptionStatus("Starting…");
                DeviceStatusText.Text = transport is null ? "Connecting" : $"Connecting over {transport.ToUpperInvariant()}";
                break;
            case "running":
                _lastErrorCode = null;
                _sessionRunning = true;
                StatusText.Text = "Connected";
                SetCaptionStatus(string.Empty);
                DeviceStatusText.Text = transport is null ? "Live" : $"Live · {transport.ToUpperInvariant()}";
                EmptyOverlay.Visibility = Visibility.Collapsed;
                ConnectionDot.Fill = (Brush)FindResource("AccentBrush");
                MpvHost.BringInputOverlayToFront();
                ReassertInputOverlayZOrder();
                break;
            case "stopping":
                StatusText.Text = "Disconnecting…";
                SetCaptionStatus("Disconnecting…");
                break;
            case "stopped":
                _sessionRunning = false;
                await ClearSessionResourcesAsync();
                StatusText.Text = "Disconnected";
                SetCaptionStatus(string.Empty);
                EmptyOverlay.Visibility = Visibility.Visible;
                ConnectionDot.Fill = (Brush)FindResource("MutedTextBrush");
                break;
            case "error":
                var wasRunning = _sessionRunning || _sessionActive;
                _sessionRunning = false;
                var errorCode = e.Data.TryGetProperty("error_code", out var codeValue)
                    ? codeValue.GetString() ?? "connection_failed"
                    : "connection_failed";
                await ClearSessionResourcesAsync();
                if (wasRunning && IsTransientSessionError(errorCode))
                {
                    StartAutomaticReconnect(errorCode);
                }
                else
                {
                    SetError(errorCode);
                }
                break;
        }
    }

    private async Task ClearSessionResourcesAsync()
    {
        _touchActive = false;
        _sessionRunning = false;
        _heldUsages.Clear();
        _suppressedPasteKeys.Clear();
        _sessionActive = false;
        _inputOverlayTimer.Stop();
        SetCaptionStatus(string.Empty);
        MpvHost.Visibility = Visibility.Collapsed;
        if (_mpv is not null)
        {
            try { await _mpv.DisposeAsync(); }
            catch { }
            _mpv = null;
        }
    }

    private void SetError(string? code)
    {
        _lastErrorCode = code ?? "connection_failed";
        var message = ErrorCatalog.MessageFor(code);
        StatusText.Text = "Error";
        SetCaptionStatus("Error");
        EmptyMessage.Text = message;
        EmptyOverlay.Visibility = Visibility.Visible;
        ConnectionDot.Fill = (Brush)FindResource("DangerBrush");
    }

    private static bool IsTransientSessionError(string code) => code is
        "stream_timeout" or "stream_ended" or "player_disconnected" or
        "connection_failed" or "worker_crashed";

    private void StartAutomaticReconnect(string errorCode)
    {
        if (_automaticReconnect || _closing) return;
        _automaticReconnect = true;
        _ = AutomaticReconnectAsync(errorCode);
    }

    private async Task AutomaticReconnectAsync(string errorCode)
    {
        try
        {
            for (var attempt = 0; attempt < 3 && !_closing; attempt++)
            {
                StatusText.Text = $"Reconnecting ({attempt + 1}/3)…";
                SetCaptionStatus($"Reconnecting {attempt + 1}/3…");
                EmptyMessage.Text = "The display stream was interrupted. Retrying…";
                EmptyOverlay.Visibility = Visibility.Visible;
                ConnectionDot.Fill = (Brush)FindResource("DangerBrush");
                await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)));
                if (_closing) return;
                await ConnectAsync();

                for (var wait = 0; wait < 40 && !_closing; wait++)
                {
                    if (_sessionRunning)
                    {
                        SetCaptionStatus(string.Empty);
                        return;
                    }
                    if (!_sessionActive) break;
                    await Task.Delay(250);
                }
            }
            if (!_closing) SetError(errorCode);
        }
        catch (Exception) when (!_closing)
        {
            SetError(errorCode);
        }
        finally
        {
            _automaticReconnect = false;
            if (!_sessionRunning) SetCaptionStatus(string.Empty);
        }
    }

    private async void ReassertInputOverlayZOrder()
    {
        // MPV may create or raise its video child after Start() and after the
        // first format event. Reassert a few times while playback settles so
        // the native input overlay remains the top child and receives mouse
        // and wheel messages.
        try
        {
            foreach (var delay in new[] { 50, 150, 400 })
            {
                await Task.Delay(delay);
                if (_stopping || _mpv is null) return;
                MpvHost.BringInputOverlayToFront();
            }
        }
        catch (ObjectDisposedException) { }
    }

    private void Worker_WorkerExited(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(HandleWorkerExitedAsync);

    private async Task HandleWorkerExitedAsync()
    {
        // The worker is already gone, so a protocol release command cannot be
        // delivered. Clear the host-side state immediately and dispose MPV so
        // a reconnect starts with no stale contact, modifiers, or video child.
        await ClearSessionResourcesAsync();
        SetError("worker_crashed");
    }

    private async void Mpv_Exited(object? sender, EventArgs e)
    {
        // Disposing the previous player during a fast restart can deliver its
        // Exited event after the replacement player is already active. That
        // stale event must not tear down or reconnect the new session.
        if (!ReferenceEquals(sender, _mpv)) return;
        if (_stopping || !_sessionActive) return;
        await Dispatcher.InvokeAsync(async () =>
        {
            if (!_softwareFallbackUsed && _settings.PreferHardwareDecode)
            {
                StatusText.Text = "Decoder failed; retrying…";
                SetCaptionStatus("Retrying…");
                await ConnectAsync(preferSoftwareDecode: true);
            }
            else
            {
                await ClearSessionResourcesAsync();
                SetError("connection_failed");
            }
        });
    }

    private void MpvHost_NativeMouse(object? sender, NativeMouseEventArgs e)
    {
        UpdateViewerToolbar(e);
        // Clicking the phone while the sheet is open dismisses the sheet
        // (outside-tap) instead of sending a touch to the iPhone.
        if (e.Kind == "down" && SettingsSheet.Visibility == Visibility.Visible)
        {
            HideSettingsSheet();
            return;
        }
        if (!_sessionActive || _worker is null || _worker.HasExited) return;
        var point = ViewportMapper.MapToPhone(e.X, e.Y, e.Width, e.Height, _sourceWidth, _sourceHeight, 1, clamp: _touchActive);
        try
        {
            if (e.Kind == "down" && point is not null)
            {
                _touchActive = true;
                _ = _worker.SendCommandNoWaitAsync("touch", new { phase = "down", x = point.Value.X, y = point.Value.Y });
            }
            else if (e.Kind == "move" && _touchActive && point is not null)
            {
                _ = _worker.SendCommandNoWaitAsync("touch", new { phase = "move", x = point.Value.X, y = point.Value.Y });
            }
            else if (e.Kind == "up" && _touchActive)
            {
                _touchActive = false;
                if (point is not null)
                    _ = _worker.SendCommandNoWaitAsync("touch", new { phase = "up", x = point.Value.X, y = point.Value.Y });
                else
                    _ = _worker.SendCommandNoWaitAsync("release_input");
            }
            else if (e.Kind == "cancel" && _touchActive)
            {
                _touchActive = false;
                _ = _worker.SendCommandNoWaitAsync("release_input");
            }
            else if (e.Kind == "wheel" && point is not null && e.Delta != 0)
            {
                _ = _worker.SendCommandNoWaitAsync("scroll", new { x = point.Value.X, y = point.Value.Y, delta = Math.Clamp(e.Delta / 120.0, -4, 4) });
            }
        }
        catch { }
    }

    private void UpdateViewerToolbar(NativeMouseEventArgs e)
    {
        var edgeHeight = Math.Max(44, e.Height * 0.12);
        if (e.Y <= edgeHeight)
        {
            _viewerToolbarWindow?.ShowAnimated(MpvHost);
            _viewerToolbarTimer.Stop();
        }
        else if (_viewerToolbarWindow?.IsVisible == true)
        {
            _viewerToolbarTimer.Stop();
            _viewerToolbarTimer.Start();
        }
    }

    private void ViewerToolbarWindow_ActionRequested(object? sender, string action)
    {
        if (action == "settings")
        {
            ToggleSettingsSheet();
            return;
        }
        if (!_sessionActive || _worker is null || _worker.HasExited) return;
        // Host controls are input events. Do not make the click wait for a
        // worker response over Wi-Fi; the worker still processes commands in
        // stdin order and returns a response that is intentionally ignored.
        _ = _worker.SendCommandNoWaitAsync(action);
    }

    private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // Esc always dismisses the sheet (even with no session).
        if (key == Key.Escape && SettingsSheet.Visibility == Visibility.Visible)
        {
            e.Handled = true;
            HideSettingsSheet();
            return;
        }
        if (!_sessionActive || _worker is null || _worker.HasExited) return;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && key is Key.D1 or Key.NumPad1 or Key.D2 or Key.NumPad2 or Key.D3 or Key.NumPad3)
        {
            e.Handled = true;
            var command = key is Key.D1 or Key.NumPad1
                ? "home"
                : key is Key.D2 or Key.NumPad2
                    ? "app_switcher"
                    : "spotlight";
            _ = _worker.SendCommandNoWaitAsync(command);
            return;
        }
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && key == Key.V)
        {
            e.Handled = true;
            _suppressedPasteKeys.Add(key);
            if (Keyboard.IsKeyDown(Key.LeftCtrl)) _suppressedPasteKeys.Add(Key.LeftCtrl);
            if (Keyboard.IsKeyDown(Key.RightCtrl)) _suppressedPasteKeys.Add(Key.RightCtrl);
            _heldUsages.Clear();
            try
            {
                await _worker.SendCommandAsync("key_state", new { usages = Array.Empty<int>() }, TimeSpan.FromSeconds(2));
                if (!Clipboard.ContainsText(TextDataFormat.UnicodeText))
                {
                    StatusText.Text = "Clipboard is not plain text.";
                    return;
                }
                var text = Clipboard.GetText(TextDataFormat.UnicodeText);
                if (Encoding.UTF8.GetByteCount(text) > 1024 * 1024)
                {
                    StatusText.Text = "Clipboard text exceeds 1 MiB.";
                    return;
                }
                await _worker.SendCommandAsync("paste", new { text }, TimeSpan.FromSeconds(10));
                text = string.Empty;
                StatusText.Text = "Pasted to iPhone";
            }
            catch
            {
                    StatusText.Text = "Paste failed — use plain text ≤ 1 MiB.";
            }
            return;
        }

        var usage = KeyboardMapper.ToHidUsage(key);
        if (usage is null) return;
        e.Handled = true;
        if (_heldUsages.Add(usage.Value))
        {
            try { await SendHeldKeysAsync(); } catch { }
        }
    }

    private async void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_suppressedPasteKeys.Remove(key))
        {
            e.Handled = true;
            return;
        }
        if (!_sessionActive || _worker is null || _worker.HasExited) return;
        var usage = KeyboardMapper.ToHidUsage(key);
        if (usage is null) return;
        e.Handled = true;
        if (_heldUsages.Remove(usage.Value))
        {
            try { await SendHeldKeysAsync(); } catch { }
        }
    }

    private Task SendHeldKeysAsync() =>
        _worker!.SendCommandNoWaitAsync("key_state", new { usages = _heldUsages.OrderBy(value => value).ToArray() });

    private async Task RunSetupActionAsync(string action, bool mutating)
    {
        var serial = SelectedSerial();
        if (mutating)
        {
            var explanation = action switch
            {
                "pair-usb" => "This asks the iPhone to trust this computer and saves USB trust credentials. Approve Trust and enter the passcode only on the iPhone.",
                "reveal-developer-mode" => "This reveals the Developer Mode setting. It does not enable Developer Mode or restart the iPhone.",
                "prepare-image" => "This may download and mount an Apple developer image. It will not replace an already mounted image.",
                "pair-wifi" => "This saves a CoreDevice Wi-Fi pairing record on this computer using the trusted USB connection.",
                _ => "This action changes phone setup state.",
            };
            if (MessageBox.Show(explanation + "\n\nContinue?", "Confirm phone setup action", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        }

        try
        {
            await StopSessionAsync(updateUi: false);
            await EnsureWorkerAsync();
            SetupStatusText.Text = "Working…";
            await _worker!.SendCommandAsync("setup", new { action, serial, approved = mutating }, TimeSpan.FromMinutes(action == "prepare-image" ? 6 : 2));
            SetupStatusText.Text = action switch
            {
                "check" => "Read-only setup check completed.",
                "check-display" => "Display service reports supported media features.",
                "reveal-developer-mode" => "Developer Mode was revealed. Enable it on the iPhone, restart, unlock, and confirm Turn On.",
                "prepare-image" => "Developer image preparation completed.",
                "pair-wifi" => "Wi-Fi pairing completed. Disconnect USB only after a successful connection test.",
                "pair-usb" => "USB trust command completed.",
                _ => "Action completed.",
            };
            await RefreshDevicesAsync();
        }
        catch (WorkerCommandException error)
        {
            SetupStatusText.Text = ErrorCatalog.MessageFor(error.Code);
        }
        catch
        {
            SetupStatusText.Text = ErrorCatalog.MessageFor("connection_failed");
        }
    }

    public async Task ReconnectAsync(ConnectionMode? connection = null, string? serial = null)
    {
        if (connection is not null) SelectConnectionMode(connection.Value);
        if (!string.IsNullOrWhiteSpace(serial)) _settings.Serial = serial;
        await ConnectAsync();
    }

    public async Task<string> HandleInstanceCommandAsync(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var command = root.GetProperty("command").GetString();
            if (command == "start")
            {
                var requestedConnection = root.TryGetProperty("connection", out var requestedConnectionValue)
                    ? requestedConnectionValue.GetString()
                    : null;
                var requestedSerial = root.TryGetProperty("serial", out var requestedSerialValue)
                    ? requestedSerialValue.GetString()
                    : null;
                var activeConnection = _settings.Connection.ToString().ToLowerInvariant();
                if (_sessionActive &&
                    ((!string.IsNullOrWhiteSpace(requestedConnection) && requestedConnection != activeConnection) ||
                     (!string.IsNullOrWhiteSpace(requestedSerial) && requestedSerial != _settings.Serial)))
                {
                    return "{\"ok\":false,\"error\":\"session_mode_conflict\"}";
                }
                if (_sessionActive)
                {
                    await Dispatcher.InvokeAsync(FocusExistingWindow);
                }
                else
                {
                    var mode = requestedConnection switch
                    {
                        "usb" => ConnectionMode.Usb,
                        "wifi" => ConnectionMode.Wifi,
                        "auto" => ConnectionMode.Auto,
                        _ => (ConnectionMode?)null,
                    };
                    await Dispatcher.InvokeAsync(async () => await ReconnectAsync(mode, requestedSerial));
                }
                return "{\"ok\":true}";
            }
            if (command == "focus")
            {
                await Dispatcher.InvokeAsync(FocusExistingWindow);
                return "{\"ok\":true}";
            }
            if (command == "stop")
            {
                _ = Dispatcher.InvokeAsync(Close);
                return "{\"ok\":true}";
            }
            if (command == "restart")
            {
                var connection = root.TryGetProperty("connection", out var c) ? c.GetString() : null;
                var serial = root.TryGetProperty("serial", out var s) ? s.GetString() : null;
                var mode = connection switch { "usb" => ConnectionMode.Usb, "wifi" => ConnectionMode.Wifi, "auto" => ConnectionMode.Auto, _ => (ConnectionMode?)null };
                await Dispatcher.InvokeAsync(async () => await ReconnectAsync(mode, serial));
                return "{\"ok\":true}";
            }
            if (command == "status")
            {
                return JsonSerializer.Serialize(new
                {
                    running = true,
                    state = _sessionActive ? "running" : "stopped",
                    connection = _settings.Connection.ToString().ToLowerInvariant(),
                    serial = _settings.Serial,
                    error_code = _lastErrorCode,
                });
            }
        }
        catch { }
        return "{\"ok\":false}";
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }
        e.Cancel = true;
        if (_closing)
        {
            return;
        }
        _closing = true;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        try
        {
            await StopSessionAsync(updateUi: false);
            if (_worker is not null)
            {
                await _worker.DisposeAsync();
                _worker = null;
            }
        }
        finally
        {
            _allowClose = true;
            _closing = false;
            _ = Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Normal,
                new Action(Close));
        }
    }

    private void FocusExistingWindow()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        var keepOnTop = Topmost;
        Topmost = true;
        Topmost = keepOnTop;
        Focus();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e) => await ConnectAsync();
    private async void ReconnectButton_Click(object sender, RoutedEventArgs e) => await ConnectAsync();
    private async void DisconnectButton_Click(object sender, RoutedEventArgs e) => await StopSessionAsync();

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void MinBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaxBtn_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            MaxGlyph.Text = "";
        }
        else
        {
            WindowState = WindowState.Maximized;
            MaxGlyph.Text = "";
        }
    }

    private void SetupButton_Click(object sender, RoutedEventArgs e)
    {
        // Phone Setup tab.
        if (SettingsSheet.Visibility == Visibility.Visible && SettingsSection.Visibility == Visibility.Visible)
        {
            HideSettingsSheet();
            return;
        }
        ShowSettingsSheet();
        TabSettings_Click(sender, e);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        // Cog toggles (fixes: previously only Done closed the sheet).
        if (SettingsSheet.Visibility == Visibility.Visible)
        {
            HideSettingsSheet();
            return;
        }
        ShowSettingsSheet();
        TabPhoneSetup_Click(sender, e);
    }

    private void ToggleSettingsSheet()
    {
        if (SettingsSheet.Visibility == Visibility.Visible) HideSettingsSheet();
        else
        {
            ShowSettingsSheet();
            TabPhoneSetup_Click(this, new RoutedEventArgs());
        }
    }

    private void ShowSettingsSheet()
    {
        SettingsSheet.Visibility = Visibility.Visible;
        _viewerToolbarWindow?.Hide();
        _viewerToolbarTimer.Stop();
    }

    private void HideSettingsSheet() => SettingsSheet.Visibility = Visibility.Collapsed;

    private void CloseSetupButton_Click(object sender, RoutedEventArgs e) => HideSettingsSheet();

    private void SheetGrabber_Down(object sender, MouseButtonEventArgs e)
    {
        _sheetDragArmed = true;
        _sheetDragStart = e.GetPosition(this);
        try { ((UIElement)sender).CaptureMouse(); } catch { }
    }

    private void SheetGrabber_Move(object sender, MouseEventArgs e)
    {
        if (!_sheetDragArmed || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(this);
        // Drag down ~70px dismisses the sheet (iOS-style slide-down).
        if (pos.Y - _sheetDragStart.Y > 70)
        {
            _sheetDragArmed = false;
            try { ((UIElement)sender).ReleaseMouseCapture(); } catch { }
            HideSettingsSheet();
        }
    }

    private void SheetGrabber_Up(object sender, MouseButtonEventArgs e)
    {
        _sheetDragArmed = false;
        try { ((UIElement)sender).ReleaseMouseCapture(); } catch { }
    }

    private void TabPhoneSetup_Click(object sender, RoutedEventArgs e)
    {
        PhoneSetupSection.Visibility = Visibility.Visible;
        SettingsSection.Visibility = Visibility.Collapsed;
        TabPhoneSetupBtn.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
        TabPhoneSetupBtn.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        TabSettingsBtn.SetResourceReference(Control.BackgroundProperty, "SegmentBrush");
        TabSettingsBtn.SetResourceReference(Control.ForegroundProperty, "SegmentTextBrush");
        PanelTitle.Text = "Settings & Options";
    }

    private void TabSettings_Click(object sender, RoutedEventArgs e)
    {
        PhoneSetupSection.Visibility = Visibility.Collapsed;
        SettingsSection.Visibility = Visibility.Visible;
        TabSettingsBtn.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
        TabSettingsBtn.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        TabPhoneSetupBtn.SetResourceReference(Control.BackgroundProperty, "SegmentBrush");
        TabPhoneSetupBtn.SetResourceReference(Control.ForegroundProperty, "SegmentTextBrush");
        PanelTitle.Text = "Settings & Options";
    }

    private void HwDecode_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            _settings.PreferHardwareDecode = HwDecodeBox.IsChecked == true;
            SettingsStore.Save(_paths.SettingsFile, _settings);
        }
    }

    private void AlwaysOnTop_Changed(object sender, RoutedEventArgs e)
    {
        Topmost = AlwaysOnTopBox.IsChecked == true;
        if (IsLoaded)
        {
            _settings.AlwaysOnTop = AlwaysOnTopBox.IsChecked == true;
            SettingsStore.Save(_paths.SettingsFile, _settings);
        }
    }

    private void BackdropAcrylic_Click(object sender, RoutedEventArgs e) => ApplyBackdrop(BackdropType.Acrylic);
    private void BackdropMica_Click(object sender, RoutedEventArgs e) => ApplyBackdrop(BackdropType.Mica);
    private void BackdropSolid_Click(object sender, RoutedEventArgs e) => ApplyBackdrop(BackdropType.None);

    private void Scale75_Click(object sender, RoutedEventArgs e)
    {
        // Phone aspect (~0.466) at 75%.
        Width = 320;
        Height = 690;
    }

    private void Scale100_Click(object sender, RoutedEventArgs e)
    {
        Width = 410;
        Height = 880;
    }

    private void Scale125_Click(object sender, RoutedEventArgs e)
    {
        Width = 510;
        Height = 1090;
    }

    private async void RefreshDevicesButton_Click(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();
    private async void CheckSetupButton_Click(object sender, RoutedEventArgs e) => await RunSetupActionAsync("check", false);
    private async void PairUsbButton_Click(object sender, RoutedEventArgs e) => await RunSetupActionAsync("pair-usb", true);
    private async void RevealDeveloperModeButton_Click(object sender, RoutedEventArgs e) => await RunSetupActionAsync("reveal-developer-mode", true);
    private async void PrepareImageButton_Click(object sender, RoutedEventArgs e) => await RunSetupActionAsync("prepare-image", true);
    private async void PairWifiButton_Click(object sender, RoutedEventArgs e) => await RunSetupActionAsync("pair-wifi", true);
    private async void CheckDisplayButton_Click(object sender, RoutedEventArgs e) => await RunSetupActionAsync("check-display", false);

    private void ConnectionModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            _settings.Connection = SelectedConnectionMode();
            SettingsStore.Save(_paths.SettingsFile, _settings);
        }
    }
}

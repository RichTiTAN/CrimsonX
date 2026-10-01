/*
 * CrimsonX - A GUI VPN client that fetches, tests and load-balances multiple xray configs suited for your network.
 * Copyright (C) 2026 RichTiTAN
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Net.NetworkInformation;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Input.Platform;
using CrimsonX.Services;

namespace CrimsonX;

public enum ToastKind
{
    Info,

    Success,

    Error,
}

public partial class MainWindow
{
    private bool _isInitializingSettings = false;
    private bool _isModeHotSwapping = false;
    private global::Avalonia.Threading.DispatcherTimer? _saveDebounceTimer;
    private global::Avalonia.Threading.DispatcherTimer? _xrayRestartTimer;
    private global::Avalonia.Threading.DispatcherTimer? _toastTimer;

    private System.Collections.Generic.List<int> _staggerQueue = new();

    private readonly CrimsonX.Services.NetworkDiagnosticsService _netDiag =
        new CrimsonX.Services.NetworkDiagnosticsService();

    // ── Session clock 
    private readonly CrimsonX.Services.SessionManager _session =
        new CrimsonX.Services.SessionManager();

    // ── Toast palette ──

    private static readonly Color ToastSuccessColour = Color.FromRgb(104, 211, 145);

    private static readonly Color ToastErrorColour = Color.FromRgb(245, 101, 101);

    private static readonly Color ToastInfoColour = Color.FromRgb(226, 232, 240);

    public static Color ToastColour(ToastKind kind) => kind switch
    {
        ToastKind.Success => ToastSuccessColour,
        ToastKind.Error => ToastErrorColour,
        _ => ToastInfoColour,
    };

    public static IBrush ToastBrush(ToastKind kind) => new SolidColorBrush(ToastColour(kind));

    public static string AsToastText(string message) => (message ?? "").ToUpperInvariant();
    private static readonly SolidColorBrush BrWhite = new SolidColorBrush(Color.FromRgb(226, 232, 240)); // #E2E8F0

    private System.Windows.Forms.NotifyIcon? _trayIcon;

    private global::Avalonia.Controls.WindowState _restoreState = global::Avalonia.Controls.WindowState.Normal;



    // ── Config Persistence ──

    internal void RequestConfigSave()
    {
        if (_saveDebounceTimer == null)
        {
            _saveDebounceTimer = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveDebounceTimer.Tick += (s, e) => { _saveDebounceTimer.Stop(); SaveConfig(); };
        }
        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Start();
    }

    public void SaveConfig()
    {
        ConfigService.Save(_cfg, _state, _cfg.CfgFile);
    }


    public string GetAppPath(string relPath)
    {
        return Path.Combine(_cfg.BaseDir, relPath);
    }

    private static void TryDeleteFile(string path)

    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception ex) { CrimsonX.Services.SimpleLogger.Log(ex); }
    }


    // ── Engine Process Output Capture ──

    private int? StartDebugProcess(string exePath, string args, string workingDir, string label, bool warnOnly = true)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName         = exePath,
                Arguments        = args,
                WorkingDirectory = workingDir,
                UseShellExecute  = false,
                CreateNoWindow   = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            var proc = new Process { StartInfo = psi };
            proc.Start();
            try { CrimsonX.Services.JobManager.AddProcess(proc); } catch { }

            int pid = proc.Id;

            _ = Task.Run(async () =>
            {
                try
                {
                    var outTask = Task.Run(async () =>
                    {
                        try
                        {
                            string? line;
                            while ((line = await proc.StandardOutput.ReadLineAsync()) != null)
                                if (!string.IsNullOrWhiteSpace(line) && ShouldLog(line, warnOnly))
                                    CrimsonX.Services.SimpleLogger.Log($"[{label}] {line}");
                        }
                        catch { }
                    });
                    var errTask = Task.Run(async () =>
                    {
                        try
                        {
                            string? line;
                            while ((line = await proc.StandardError.ReadLineAsync()) != null)
                                if (!string.IsNullOrWhiteSpace(line) && ShouldLog(line, warnOnly))
                                    CrimsonX.Services.SimpleLogger.Log($"[{label}] {line}");
                        }
                        catch { }
                    });
                    await Task.WhenAll(outTask, errTask);
                }
                catch { }
                finally { try { proc.Dispose(); } catch { } }
            });

            return pid;
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
            return null;
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _seenLogs = new();

    private static bool ShouldLog(string line, bool warnOnly)
    {
        if (!warnOnly) return true;

        if (line.IndexOf("is relative and will resolve to", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;

        bool isWarn = line.IndexOf("warn",  StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("alert", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("emerg", StringComparison.OrdinalIgnoreCase) >= 0;

        if (!isWarn) return false;

        string payload = line;
        string[] tags = { "[warn]", "[warning]", "[error]", "[err]", "[fatal]", "[alert]", "[emerg]" };
        foreach (var tag in tags)
        {
            int idx = line.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                payload = line.Substring(idx + tag.Length).Trim();
                break;
            }
        }

        if (_seenLogs.Count > 2000) _seenLogs.Clear();

        if (payload.Length > 0 && !_seenLogs.TryAdd(payload, 1))
        {
            return false;
        }

        return true;
    }

    // ── LAN IP / Port Display ──

    private async Task UpdateLanIpAsync()
    {
        try
        {
            var ip = await Task.Run(() => 
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                              && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                    .Where(ua => ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(ua => ua.Address.ToString())
                    .Where(ipStr => !ipStr.StartsWith("127.") && !ipStr.StartsWith("169.254."))
                    .FirstOrDefault();
            });
            _state.LanIp = ip ?? "Unknown";
        }
        catch { _state.LanIp = "Unknown"; }
    }

    private void UpdateLocalPortUI()
    {

        if (lblLocalIp == null) return;
        
        lblLocalIp.ClearValue(global::Avalonia.Controls.TextBlock.ForegroundProperty);
        
        if (_state.IsConnected || _state.IsEngineRunning)
        {
            lblLocalIp.Text = "127.0.0.1:" + CrimsonX.Services.ExitNodeChain.ActivePort;
        }
        else
        {
            lblLocalIp.Text = CrimsonX.Localization.AppStrings.StatusDisconnected;
        }
    }

    internal void UpdateLanPortUI()
    {
        var lblLanIp = this.FindControl<global::Avalonia.Controls.TextBlock>("lblLanIp");
        if (lblLanIp == null) return;

        lblLanIp.ClearValue(global::Avalonia.Controls.TextBlock.ForegroundProperty);

        if (!_cfg.AllowLanConnections)
        {
            lblLanIp.Text = CrimsonX.Localization.AppStrings.Disabled;
        }
        else
        {
            if (_state.IsConnected || _state.IsEngineRunning)
            {
                string displayIp = (_cfg.EnableAdapterBinding && !string.IsNullOrWhiteSpace(_cfg.SelectedAdapterIp)) ? _cfg.SelectedAdapterIp : (_state.LanIp ?? "Unknown");
                    lblLanIp.Text = displayIp + ":" + CrimsonX.Services.ExitNodeChain.ActivePort;
            }
            else
            {
                lblLanIp.Text = CrimsonX.Localization.AppStrings.StatusDisconnected;
            }
        }
    }



    // ── Toast Notifications ──

    public void ShowToast(string message, ToastKind kind = ToastKind.Info)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var toast = this.FindControl<Border>("ToastBorder");
            var toastText = this.FindControl<TextBlock>("ToastText");
            if (toast == null || toastText == null) return;

            var carousel = this.FindControl<global::Avalonia.Controls.Carousel>("MainCarousel");
            bool onAppsGames = carousel != null && carousel.SelectedIndex == 5;
            const double toastBottom = 25, appsGamesLift = 45;
            toast.Margin = new global::Avalonia.Thickness(0, 0, 0, onAppsGames ? toastBottom + appsGamesLift : toastBottom);

            _toastTimer?.Stop();

            bool isFa = CrimsonX.Localization.AppStrings.IsPersian;

            toastText.Text = AsToastText(message);
            toastText.FontFamily = isFa
                ? new global::Avalonia.Media.FontFamily("Segoe UI")
                : global::Avalonia.Media.FontFamily.Default;
            toastText.FlowDirection = isFa
                ? global::Avalonia.Media.FlowDirection.RightToLeft
                : global::Avalonia.Media.FlowDirection.LeftToRight;
            toastText.FontWeight = global::Avalonia.Media.FontWeight.Bold;

            toastText.LetterSpacing = 0.5;
            toastText.Foreground = ToastBrush(kind);

            toast.Opacity = 0;
            toast.IsVisible = true;
            global::Avalonia.Threading.DispatcherTimer.RunOnce(() => { toast.Opacity = 1; }, TimeSpan.FromMilliseconds(20));

            if (_toastTimer == null)
            {
                _toastTimer = new global::Avalonia.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                _toastTimer.Tick += (s, e) =>
                {
                    _toastTimer?.Stop();
                    if (toast != null)
                    {
                        toast.Opacity = 0;
                        global::Avalonia.Threading.DispatcherTimer.RunOnce(() => { toast.IsVisible = false; }, TimeSpan.FromMilliseconds(300));
                    }
                };
            }
            _toastTimer.Start();
        });
    }




    // ── Connect Progress ──

    private global::Avalonia.Threading.DispatcherTimer? _fillAnimTimer;
    private double _currentFillPct = 0;
    private double _targetFillPct = -1;

    private global::Avalonia.Controls.TextBlock? _connectText;
    private global::Avalonia.Controls.TextBlock? _connectConnectedText;
    private global::Avalonia.Controls.Border? _connectBox;
    private global::Avalonia.Controls.Border? _connectPress;
    private global::Avalonia.Controls.Border? _connectBreathControl;
    private readonly CrimsonX.Services.ConnectBreath _connectBreath = new CrimsonX.Services.ConnectBreath();

    private void EnsureConnectBoxResources()
    {
        _connectText ??= this.FindControl<global::Avalonia.Controls.TextBlock>("txtConnectBtn");
        _connectConnectedText ??= this.FindControl<global::Avalonia.Controls.TextBlock>("txtConnectedBtn");
        _connectBox ??= this.FindControl<global::Avalonia.Controls.Border>("panConnectBox");
        _connectPress ??= this.FindControl<global::Avalonia.Controls.Border>("panConnectPress");
        _connectBreathControl ??= this.FindControl<global::Avalonia.Controls.Border>("panConnectBreath");
    }

    private void ApplyConnectBreath(bool comingUp)
    {
        EnsureConnectBoxResources();
        if (_connectBreathControl == null) return;

        _connectBreathControl.Opacity = _connectBreath.Next(DateTime.UtcNow, comingUp);
    }

    // ── Connect box press feedback (same feel as the Apps & Games strip) ──

    private void HomeConnectBoxPress(object? sender, global::Avalonia.Input.PointerPressedEventArgs e)
        => SetHomeConnectBoxPressed(true);

    private void HomeConnectBoxRelease(object? sender, global::Avalonia.Input.PointerReleasedEventArgs e)
        => SetHomeConnectBoxPressed(false);

    private void HomeConnectBoxCaptureLost(object? sender, global::Avalonia.Input.PointerCaptureLostEventArgs e)
        => SetHomeConnectBoxPressed(false);

    private void SetHomeConnectBoxPressed(bool pressed)
    {
        EnsureConnectBoxResources();

        var inner = this.FindControl<global::Avalonia.Controls.Panel>("panConnectBoxInner");
        if (inner != null)
        {
            double s = pressed ? 0.97 : 1.0;
            inner.RenderTransform = new global::Avalonia.Media.ScaleTransform(s, s);
        }

        if (_connectPress != null) _connectPress.Opacity = pressed ? 1.0 : 0.0;
    }

    private void SetConnectButtonProgress(int percent)
    {
        CrimsonX.Services.UiEventBus.Instance.PublishConnectionProgress(percent);

        EnsureConnectBoxResources();

        if (percent < 0)
        {
            _targetFillPct = -1;
            _currentFillPct = 0;
            _fillAnimTimer?.Stop();

            _connectBox?.Classes.Remove("connected");
            ApplyConnectBreath(false);

            if (_connectText != null)
            {
                _connectText.Text = CrimsonX.Localization.AppStrings.StatusConnect;
                _connectText.Opacity = 1;
            }
            if (_connectConnectedText != null) _connectConnectedText.Opacity = 0;
            return;
        }

        double target = System.Math.Clamp(percent / 100.0, 0.0, 1.0);
        if (_targetFillPct >= 0 && target <= _targetFillPct) return; 
        
        if (!_state.IsEngineRunning && !_state.IsConnected) return; 

        if (_fillAnimTimer == null)
        {
            _fillAnimTimer = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _fillAnimTimer.Tick += (s, e) => {
                if (_targetFillPct < 0) return;

                double diff = _targetFillPct - _currentFillPct;
                if (System.Math.Abs(diff) < 0.005) _currentFillPct = _targetFillPct;
                else _currentFillPct += diff * 0.35;

                EnsureConnectBoxResources();

                bool comingUp = CrimsonX.Services.ConnectPhaseUi.IsComingUp(_state.IsConnected, _state.IsEngineRunning, _state.IsReconnecting);

                if (!_state.IsConnected)
                {
                    if (_connectText != null && _connectText.Opacity < 1) _connectText.Opacity = 1;
                    if (_connectConnectedText != null && _connectConnectedText.Opacity > 0) _connectConnectedText.Opacity = 0;
                }

                ApplyConnectBreath(comingUp);

                if (_currentFillPct >= 0.999 && _targetFillPct >= 1.0 && _state.IsConnected)
                {
                    ApplyConnectBreath(false);

                    if (_connectBox != null && !_connectBox.Classes.Contains("connected")) _connectBox.Classes.Add("connected");

                    SetSparklinesVisible(true);
                    SetReadoutTilesGrown(true);

                    if (_connectText != null) _connectText.Opacity = 0;
                    if (_connectConnectedText != null)
                    {
                        _connectConnectedText.Text = CrimsonX.Localization.AppStrings.StatusConnected;
                        _connectConnectedText.Opacity = 1;
                    }

                    _targetFillPct = -1;
                    _fillAnimTimer.Stop();
                }
            };
        }

        if (!_fillAnimTimer.IsEnabled)
            _fillAnimTimer.Start();

        _targetFillPct = System.Math.Clamp(percent / 100.0, 0.0, 1.0);
    }


    // ── Stop Engines / Disconnect Cleanup ──

    private void StopAllEngines(bool isClosing = false)
    {
        try { _reapplyCts?.Cancel(); } catch { }

        CrimsonX.Services.ExitNodeChain.SetChainActive(false);
        _lastEngineChangeUtc = DateTime.UtcNow;
        lock (_pipelineCtsLock) { try { _pipelineCts?.Cancel(); } catch { } }
        CrimsonX.Services.BackgroundTask.Run("stop engine", () => Task.Run(() => CrimsonX.Services.XrayPipelineManager.StopXray()));
        CrimsonX.Services.TunnelEngine.Stop();

        _state.AbortBoot       = true;
        _state.IsEngineRunning = false;
        



        lock (_staggerQueue) { _staggerQueue.Clear(); }
        _session.Stop(); 

        
        _netDiag.StopStatsPolling();
        _netDiag.StopGeoTrace();

        _logClearTimer?.Stop();
        ProxyService.SetSystemProxy(false);

        SetConnectButtonProgress(-1);

        EnsureConnectBoxResources();
        _connectBox?.Classes.Remove("connected");

        int? xrayDebugPid = _xrayDebugPid; _xrayDebugPid = null;
        int? sbDebugPid = _sbDebugPid; _sbDebugPid = null;
        int? xrayPid = _xrayPid; _xrayPid = null;
        int? sbPid = _sbPid; _sbPid = null;

        var killTask = Task.Run(() => {

            CrimsonX.Services.ProcessService.KillVpnProcess(xrayDebugPid);
            CrimsonX.Services.ProcessService.KillVpnProcess(sbDebugPid);
            CrimsonX.Services.ProcessService.KillVpnProcess(xrayPid);
            CrimsonX.Services.ProcessService.KillVpnProcess(sbPid);

            TryDeleteFile(GetAppPath(@"Data\Xray\access.log"));
            TryDeleteFile(GetAppPath(@"Data\Xray\error.log"));
            TryDeleteFile(GetAppPath(@"Data\Xray\access.log.tmp"));

            CrimsonX.Services.GeneratedArtifacts.Cleanup();
        });
        if (isClosing)
        {
            killTask.Wait(3000);
            CrimsonX.Services.JobManager.Shutdown();
        }
        else
        {
            CrimsonX.Services.BackgroundTask.Run("dns restore", () => CrimsonX.Services.SystemDnsService.RestoreAsync());
        }

        CrimsonX.Services.SimpleLogger.Log($"[Disconnect] Mode={_pollMode}, isClosing={isClosing}");

        _state.IsConnected      = false;
        _state.IsReconnecting   = false;
        _state.LastTotalBytes   = 0;
        _state.SessionDataBytes = 0;
        _state.SessionStartTime = null;
        _state.SpeedSamples     = _state.SpeedSamples ?? new double[5]; Array.Clear(_state.SpeedSamples, 0, _state.SpeedSamples.Length);

        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            var graphUpload = this.FindControl<global::Avalonia.Controls.Shapes.Path>("graphUpload");
        var graphDownload = this.FindControl<global::Avalonia.Controls.Shapes.Path>("graphDownload");
        var graphUploadFill = this.FindControl<global::Avalonia.Controls.Shapes.Path>("graphUploadFill");
        var graphDownloadFill = this.FindControl<global::Avalonia.Controls.Shapes.Path>("graphDownloadFill");
        if (graphUpload != null) graphUpload.Data = null;
        if (graphDownload != null) graphDownload.Data = null;
        if (graphUploadFill != null) graphUploadFill.Data = null;
        if (graphDownloadFill != null) graphDownloadFill.Data = null;

        SetSparklinesVisible(false);
        SetReadoutTilesGrown(false);

        var panTimerContent = this.FindControl<global::Avalonia.Controls.StackPanel>("panTimerContent");
            if (panTimerContent != null) panTimerContent.IsVisible = false;
            var lblDisconnected = this.FindControl<TextBlock>("lblDisconnected");
            if (lblDisconnected != null) lblDisconnected.IsVisible = true;

            var lblPing = this.FindControl<TextBlock>("lblPing");
            if (lblPing != null) lblPing.Text = "0 ms";

            UpdateLocalPortUI();
            UpdateLanPortUI();

            var lblTimer = this.FindControl<TextBlock>("lblTimer");
            if (lblTimer != null) lblTimer.Text = "00:00:00";
            var lblCountryName = this.FindControl<TextBlock>("lblCountryName");
            if (lblCountryName != null) lblCountryName.Text = CrimsonX.Localization.AppStrings.StatusDisconnected;
            var lblPublicIp = this.FindControl<TextBlock>("lblPublicIp");
            if (lblPublicIp != null) lblPublicIp.Text = CrimsonX.Localization.AppStrings.StatusDisconnected;

            _exitIpFull = "";
            SetPublicIpTip("");

            UpdateStatusText();
        });


        if (!isClosing)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (txtConnectBtn != null)
                {
                    txtConnectBtn.Text = CrimsonX.Localization.AppStrings.StatusConnect;
                    txtConnectBtn.Foreground = BrWhite;
                }


                var lblTot = this.FindControl<TextBlock>("lblTotalData");
                if (lblTot != null) lblTot.Text = "0 MB";
                var lblDn = this.FindControl<TextBlock>("lblDownloadSpeed");
                if (lblDn != null) lblDn.Text = "0 KB/s";
                var lblUp = this.FindControl<TextBlock>("lblUploadSpeed");
                if (lblUp != null) lblUp.Text = "0 KB/s";
                var lblPing = this.FindControl<TextBlock>("lblPing");
                if (lblPing != null) lblPing.Text = "0 ms";
            });
        }
    }




    // ── Port Display, Copy & Ping Refresh ──

    private void LocalPort_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var lbl = this.FindControl<TextBlock>("lblLocalIp");
        if (lbl != null) DoCopyIp(lbl.Text);
    }
    
    private void LanPort_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var lbl = this.FindControl<TextBlock>("lblLanIp");
        if (lbl != null) DoCopyIp(lbl.Text);
    }

    private void PublicIp_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var lbl = this.FindControl<TextBlock>("lblPublicIp");
        if (lbl != null) DoCopyPublicIp(_exitIpFull.Length > 0 ? _exitIpFull : lbl.Text);
    }

    private void DoCopyPublicIp(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        string value = text.Trim();
        if (value == CrimsonX.Localization.AppStrings.StatusDisconnected
            || value == CrimsonX.Localization.AppStrings.Disabled
            || value == CrimsonX.Localization.AppStrings.GeoTracing
            || value == CrimsonX.Localization.AppStrings.GeoTimeout) return;

        if (!System.Net.IPAddress.TryParse(value, out _)) return;

        var clipboard = global::Avalonia.Controls.TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null) _ = clipboard.SetTextAsync(value);

        ShowToast(CrimsonX.Localization.AppStrings.ToastCopiedToClipboard, kind: ToastKind.Success);
    }

    private void DoCopyIp(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text) && text.Contains(":") && text != CrimsonX.Localization.AppStrings.StatusDisconnected && text != CrimsonX.Localization.AppStrings.Disabled)
        {
            var clipboard = global::Avalonia.Controls.TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null) _ = clipboard.SetTextAsync(text);
            
            string msg = CrimsonX.Localization.AppStrings.ToastCopiedToClipboard;
            ShowToast(msg, kind: ToastKind.Success);
        }
    }

    private void RefreshPing_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_state.IsConnected && !_state.IsGeoTracing)
        {
            StartGeoPing();
        }
    }


    private DateTime _lastEngineChangeUtc = DateTime.MinValue;
    private const int ConnectSettleWindowMs = 1500;

    // ── Connect / Disconnect Trigger ──

    internal enum OfflineGuard
    {
        Ask,
        AbortIfOffline,
        Skip
    }

    private async void btnConnect_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) =>
        await HandleConnectRequestAsync(OfflineGuard.Ask);

    internal void ConnectDisconnect() => CrimsonX.Services.BackgroundTask.Run("connect request", () => HandleConnectRequestAsync(OfflineGuard.Ask));
    internal void ConnectAfterCheck() => CrimsonX.Services.BackgroundTask.Run("connect after check", () => HandleConnectRequestAsync(OfflineGuard.Skip));
    internal void BeginAutoConnect()  => CrimsonX.Services.BackgroundTask.Run("auto-connect", () => HandleConnectRequestAsync(OfflineGuard.AbortIfOffline));

    private async Task HandleConnectRequestAsync(OfflineGuard guard)
    {
        try
        {
        bool busy = _state.IsConnected || _state.IsEngineRunning;

        if (busy)
        {
            if (guard != OfflineGuard.Ask)
            {
                CrimsonX.Services.SimpleLogger.Log(
                    $"[Connect] request ignored (guard={guard}, already {(_state.IsConnected ? "connected" : "connecting")}).");
                return;
            }

            if (!_state.IsConnected && (DateTime.UtcNow - _lastEngineChangeUtc).TotalMilliseconds < ConnectSettleWindowMs)
            {
                CrimsonX.Services.SimpleLogger.Log(
                    $"[Connect] the click landed within {ConnectSettleWindowMs} ms of a fresh connect; ignored so the engine is not torn down.");
                return;
            }

            CrimsonX.Services.SimpleLogger.Log($"[Connect] disconnecting on request (guard={guard}).");
            StopAllEngines();
            return;
        }

        if (_cfg.LastXrayMode == "VPN Mode")
        {
            if (IsVpnAdapterInUse())
            {
                bool isFa = CrimsonX.Localization.AppStrings.IsPersian;
                ShowToast(CrimsonX.Localization.AppStrings.ToastVpnInUse, ToastKind.Error);
                return;
            }
        }



        if (_cfg.EnableAdapterBinding && !string.IsNullOrWhiteSpace(_cfg.SelectedAdapterName))
        {
            var adapters = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            bool exists = false;
            foreach (var adapter in adapters)
            {
                if (adapter.Name == _cfg.SelectedAdapterName && adapter.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                {
                    exists = true;
                    break;
                }
            }
            if (!exists)
            {
                ShowToast(CrimsonX.Localization.AppStrings.ToastAdapterNotAvailable, ToastKind.Error);
                return;
            }
        }

        if (_cfg.EnableDirectUDP && !string.IsNullOrWhiteSpace(_cfg.DirectUdpAdapterName))
        {
            var udpAdapters = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            bool udpAdapterExists = false;
            foreach (var adapter in udpAdapters)
            {
                if (adapter.Name == _cfg.DirectUdpAdapterName && adapter.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                {
                    udpAdapterExists = true;
                    break;
                }
            }
            if (!udpAdapterExists)
            {
                ShowToast(CrimsonX.Localization.AppStrings.ToastDirectUdpAdapterFallback);
                _cfg.DirectUdpAdapterName = "";
                _cfg.DirectUdpAdapterIp = "";
                RequestConfigSave();
            }
        }

        if (guard != OfflineGuard.Skip && !CrimsonX.Services.ConnectivityService.HasUsableConnection())
        {
            if (guard == OfflineGuard.AbortIfOffline)
            {
                CrimsonX.Services.SimpleLogger.Log("[Connect] No usable internet connection detected; auto-connect skipped.");

                if (IsVisible && WindowState != WindowState.Minimized)
                    ShowToast(CrimsonX.Localization.AppStrings.NoInternetTitle, ToastKind.Error);

                return;
            }

            if (!IsVisible || WindowState == WindowState.Minimized)
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            }

            var noNetDialog = new CrimsonX.Dialogs.ConfirmDialog(
                CrimsonX.Localization.AppStrings.NoInternetTitle,
                CrimsonX.Localization.AppStrings.NoInternetMessage,
                CrimsonX.Localization.AppStrings.Yes,
                CrimsonX.Localization.AppStrings.No);

            string? noNetResult = await noNetDialog.ShowDialog<string>(this);
            if (noNetResult != "Yes") return;
        }

        StartEnginesAsync();
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
        }
    }

    // ── VPN Adapter Check & Mode Hot-Swap ──

    private bool IsVpnAdapterInUse()
    {
        try
        {
            return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Any(ni => (ni.Name.IndexOf("singbox", StringComparison.OrdinalIgnoreCase) >= 0
                         || ni.Name.IndexOf("wintun", StringComparison.OrdinalIgnoreCase) >= 0
                         || ni.Description.IndexOf("wintun", StringComparison.OrdinalIgnoreCase) >= 0)
                        && ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up);
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
            return false;
        }
    }

    private async Task<bool> HotSwapConnectionModeAsync(string oldMode, string newMode)
    {
        bool wasVpn = oldMode == "VPN Mode";
        bool isVpn = newMode == "VPN Mode";

        try
        {
            if (wasVpn == isVpn)
            {
                ProxyService.SetSystemProxy(newMode == "Proxy Mode");
                CrimsonX.Services.SimpleLogger.Log($"[ModeHotSwap] {oldMode} -> {newMode} (system proxy only)");
                return true;
            }

            if (isVpn)
            {
                ProxyService.SetSystemProxy(false);

                var outbounds = CrimsonX.Services.XrayPipelineManager.ActiveOutbounds;
                if (outbounds == null || outbounds.Count == 0)
                {
                    ShowToast(CrimsonX.Localization.AppStrings.ToastModeSwitchFailed, ToastKind.Error);
                    return false;
                }

                await CrimsonX.Services.XrayPipelineManager.SwapOutboundsAsync(outbounds, _cfg, _cfg.XrayDir, true);

                if (!await StartOrRestartSingBoxVerifiedAsync(killRunning: false, RestartExitNodeTimeoutSeconds))
                {
                    ShowToast(CrimsonX.Localization.AppStrings.ToastStartVpnFailed, ToastKind.Error);
                    return false;
                }

                CrimsonX.Services.SimpleLogger.Log($"[ModeHotSwap] {oldMode} -> VPN Mode (sing-box pid={_sbPid})");
            }
            else
            {
                int? sbPid = _sbPid;
                _sbPid = null;
                CrimsonX.Services.ProcessService.KillVpnProcess(sbPid);

                CrimsonX.Services.TunnelEngine.Stop(CrimsonX.Services.TunnelEngine.GroupRules);

                var outbounds = CrimsonX.Services.XrayPipelineManager.ActiveOutbounds;
                if (outbounds != null && outbounds.Count > 0)
                    await CrimsonX.Services.XrayPipelineManager.SwapOutboundsAsync(outbounds, _cfg, _cfg.XrayDir, true);

                ProxyService.SetSystemProxy(newMode == "Proxy Mode");
                CrimsonX.Services.SimpleLogger.Log($"[ModeHotSwap] VPN Mode -> {newMode} (sing-box stopped)");
            }

            Dispatcher.UIThread.Post(() =>
            {
                UpdateLocalPortUI();
                UpdateLanPortUI();
            });

            return true;
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
            ShowToast(CrimsonX.Localization.AppStrings.ToastModeSwitchFailed, ToastKind.Error);
            return false;
        }
    }


    // ── Engine Start & Restart ──

    private async void StartEnginesAsync()
    {
        try
        {
        await RunDynamicPipelineAsyncCore();
        }
        catch (OperationCanceledException)
        {
            StopAllEngines();
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
            ShowToast(CrimsonX.Localization.AppStrings.ToastEngineStartFailedPrefix + ex.Message, ToastKind.Error);
            StopAllEngines();
        }
    }


    private const int RestartExitNodeTimeoutSeconds = 15;

    private readonly System.Threading.SemaphoreSlim _singBoxRestartLock = new System.Threading.SemaphoreSlim(1, 1);

    private CancellationTokenSource? _reapplyCts;

    internal void SetConnectVisualState(bool connecting)
    {
        Dispatcher.UIThread.Post(() =>
        {
            EnsureConnectBoxResources();

            if (connecting)
            {
                if (_connectText != null)
                {
                    _connectText.Text = CrimsonX.Localization.AppStrings.StatusConnecting;
                    _connectText.Opacity = 1;
                }
                if (_connectConnectedText != null) _connectConnectedText.Opacity = 0;

                _connectBox?.Classes.Remove("connected");

                SetConnectButtonProgress(15);
                return;
            }

            if (_state.IsConnected)
            {
                if (_connectText != null) _connectText.Text = CrimsonX.Localization.AppStrings.StatusConnected;
                if (_connectConnectedText != null) _connectConnectedText.Text = CrimsonX.Localization.AppStrings.StatusConnected;

                if (_connectBox != null && !_connectBox.Classes.Contains("connected")) _connectBox.Classes.Add("connected");

                SetSparklinesVisible(true);
                SetReadoutTilesGrown(true);
            }

            SetConnectButtonProgress(100);
        });
    }

    internal async Task<bool> StartOrRestartSingBoxVerifiedAsync(bool killRunning, int timeoutSeconds, CancellationToken ct = default)
    {
        try { await _singBoxRestartLock.WaitAsync(ct); }
        catch (OperationCanceledException) { return false; }

        bool chained = CrimsonX.Services.ExitNodeChain.ShouldChain(_cfg);

        bool showConnecting = chained && killRunning;

        try
        {
            CrimsonX.Services.ExitNodeChain.SetChainActive(false);

            if (killRunning)
            {
                int? previous = _sbPid;
                _sbPid = null;
                CrimsonX.Services.ProcessService.KillVpnProcess(previous);
            }

            if (showConnecting) SetConnectVisualState(true);

            CrimsonX.Services.AppRulesSingboxBuilder.EnsureRuleTunnels(_cfg, out string ruleTunnelError);
            if (ruleTunnelError.Length > 0)
                CrimsonX.Services.SimpleLogger.Log($"[SingBox] Rule tunnels: {ruleTunnelError}");
            else
                CrimsonX.Services.SimpleLogger.Log($"[Tunnel] engines: rules({CrimsonX.Services.TunnelEngine.Describe(CrimsonX.Services.TunnelEngine.GroupRules)})");

            if (!await Task.Run(() => CrimsonX.Services.SingboxConfigWriter.Write(_cfg, _cfg.SbDir), ct)) return false;

            var process = await Task.Run(() => ProcessService.StartProcessDirect(GetAppPath(@"Data\sing_box\sing-box.exe"), "run -c config.json", _cfg.SbDir), ct);
            if (process == null) return false;

            _sbPid = process.Id;

            try { await Task.Delay(350, ct); }
            catch (OperationCanceledException)
            {
                try { CrimsonX.Services.ProcessService.KillVpnProcess(process.Id); } catch { }
                return false;
            }

            if (!chained)
            {
                if (HasExited(process))
                {
                    CrimsonX.Services.SimpleLogger.Log($"[SingBox] the instance exited right after start (code {ExitCodeOf(process)}); see the [sing-box.exe] lines above.");
                    return false;
                }

                CrimsonX.Services.SimpleLogger.Log($"[SingBox] started (pid={_sbPid}).");
                return true;
            }

            if (await AwaitExitNodeEstablishedAsync(process, ct, timeoutSeconds))
            {
                CrimsonX.Services.ExitNodeChain.SetChainActive(true);
                Dispatcher.UIThread.Post(() => StartGeoPing());
                return true;
            }

            if (ct.IsCancellationRequested) return false;

            CrimsonX.Services.SimpleLogger.Log("[ExitNode] Falling back to the entry nodes only.");
            try { CrimsonX.Services.ProcessService.KillVpnProcess(process.Id); } catch { }

            if (!await Task.Run(() => CrimsonX.Services.SingboxConfigWriter.Write(_cfg, _cfg.SbDir, skipExitNode: true))) return false;

            var retry = await Task.Run(() => ProcessService.StartProcessDirect(GetAppPath(@"Data\sing_box\sing-box.exe"), "run -c config.json", _cfg.SbDir));
            if (retry == null) return false;

            _sbPid = retry.Id;
            CrimsonX.Services.SimpleLogger.Log(
                $"[ExitNode] The exit node was dropped; proxied traffic is back on xray 127.0.0.1:{CrimsonX.Services.ExitNodeChain.ActivePort}.");

            Dispatcher.UIThread.Post(() => ShowToast(CrimsonX.Localization.AppStrings.ToastExitNodeFailed, ToastKind.Error));
            return true;
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
            return false;
        }
        finally
        {
            try { _singBoxRestartLock.Release(); } catch { }

            if (showConnecting && _state.IsConnected) SetConnectVisualState(false);

            Dispatcher.UIThread.Post(() =>
            {
                UpdateLocalPortUI();
                UpdateLanPortUI();
            });
        }
    }

    internal async Task<bool> RestartSingBoxOnlyAsync()
    {
        if (_cfg.LastXrayMode != "VPN Mode" || !_state.IsConnected || _state.IsReconnecting) return false;

        _reapplyCts?.Dispose();
        _reapplyCts = new CancellationTokenSource();
        var ct = _reapplyCts.Token;

        _state.IsReconnecting = true;
        SetConnectVisualState(true);
        SetConnectButtonProgress(15);

        try
        {
            var restart = StartOrRestartSingBoxVerifiedAsync(killRunning: true, RestartExitNodeTimeoutSeconds, ct);

            int progress = 15;
            while (!restart.IsCompleted && !ct.IsCancellationRequested && progress < 90)
            {
                try { await Task.Delay(150); } catch { }

                if (restart.IsCompleted) break;

                progress += 5;
                SetConnectButtonProgress(progress);
            }

            bool ok = await restart;

            if (ok)
            {
                CrimsonX.Services.SimpleLogger.Log($"[SingBox] Restarted with updated app rules (pid={_sbPid}).");
            }
            else if (ct.IsCancellationRequested)
            {
                CrimsonX.Services.SimpleLogger.Log(
                    "[AppRules] The apply was interrupted by a disconnect; the rule changes stay pending.");
            }

            return ok;
        }
        finally
        {
            _state.IsReconnecting = false;

            if (_state.IsConnected) SetConnectVisualState(false);
        }
    }

    public void SmartRestartXray()
    {
        if (_cfg.LastXrayMode == "VPN Mode")
        {
            if (_state.IsConnected)
                ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectSafely);
            else if (_state.IsEngineRunning)
                ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
                
            return;
        }

        if (_state.IsEngineRunning || _state.IsConnected)
        {
            DoRestartXray();
        }
    }

    private void DoRestartXray()
    {
        if (_xrayRestartTimer == null)
        {
            _xrayRestartTimer = new global::Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _xrayRestartTimer.Tick += OnXrayRestartTick;
        }

        _xrayRestartTimer.Stop();
        _xrayRestartTimer.Start();
    }

    private async void OnXrayRestartTick(object? sender, EventArgs e)
    {
        try
        {
        _xrayRestartTimer?.Stop();
        
        await CrimsonX.Services.XrayPipelineManager.SwapOutboundsAsync(
            CrimsonX.Services.XrayPipelineManager.ActiveOutbounds, 
            _cfg, 
            _cfg.XrayDir,
            true);

        ProxyService.SetSystemProxy(_cfg.LastXrayMode == "Proxy Mode");

        if (_state.IsConnected)
        {
            await UpdateLanIpAsync();
            UpdateLanPortUI();

            UpdateLocalPortUI();

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1500).ConfigureAwait(false);
                    if (_state.IsConnected)
                        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => StartGeoPing());
                }
                catch { }
            });
        }
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
        }
    }



    private CrimsonX.Dialogs.TrayWidget? _trayWidget;

    // ── System Tray Icon ──

    internal void InitTrayIcon()
    {
        using var iconStream = global::Avalonia.Platform.AssetLoader.Open(new Uri("avares://CrimsonX/Assets/CrimsonX.ico"));
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "CrimsonX",
            Icon = new System.Drawing.Icon(iconStream),
            Visible = true
        };

        _trayIcon.MouseClick += (s, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    using var _busy = CrimsonX.Services.UiBusy.Scope("tray: restore window");

                    var sw = System.Diagnostics.Stopwatch.StartNew();

                    WindowState = _restoreState;
                    Show();
                    Activate();

                    sw.Stop();

                    if (sw.ElapsedMilliseconds > 150)
                        CrimsonX.Services.SimpleLogger.Log($"[UI] restoring the window from the tray took {sw.ElapsedMilliseconds} ms (state {_restoreState})");
                });
            }
            else if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_trayWidget != null)
                    {
                        _trayWidget.Close();
                        _trayWidget = null;
                    }
                    else
                    {
                        _trayWidget = new CrimsonX.Dialogs.TrayWidget(this);
                        
                        var pt = System.Windows.Forms.Cursor.Position;
                        int width = 220;
                        int height = 195;
                        
                        _trayWidget.Position = new global::Avalonia.PixelPoint(pt.X - (width / 2), pt.Y - height - 10);
                        
                        _trayWidget.Closed += (ws, we) => { _trayWidget = null; };
                        _trayWidget.Show();
                        _trayWidget.Activate();
                    }
                });
            }
        };
    }

    internal void DisposeTrayIcon()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Icon?.Dispose();
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }
}

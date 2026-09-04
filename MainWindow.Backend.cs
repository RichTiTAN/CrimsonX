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

public partial class MainWindow
{
    private bool _isInitializingSettings = false;
    private bool _isModeHotSwapping = false;
    private global::Avalonia.Threading.DispatcherTimer? _saveDebounceTimer;
    private global::Avalonia.Threading.DispatcherTimer? _xrayRestartTimer;
    private global::Avalonia.Threading.DispatcherTimer? _toastTimer;

    private System.Collections.Generic.List<int> _staggerQueue = new();

    private System.Threading.CancellationTokenSource? _graphAnimCts;

    private readonly CrimsonX.Services.NetworkDiagnosticsService _netDiag =
        new CrimsonX.Services.NetworkDiagnosticsService();

    // ── Session clock 
    private readonly CrimsonX.Services.SessionManager _session =
        new CrimsonX.Services.SessionManager();

    private static readonly SolidColorBrush _brGreenFallback = new SolidColorBrush(Color.FromRgb(104, 211, 145));
    internal static SolidColorBrush BrGreen
    {
        get
        {
            if (global::Avalonia.Application.Current?.Resources.TryGetValue("ThemeGlowBrush", out var res) == true && res is SolidColorBrush b)
                return b;
            return _brGreenFallback;
        }
    }
    private static readonly SolidColorBrush BrWhite = new SolidColorBrush(Color.FromRgb(226, 232, 240)); // #E2E8F0
    private static readonly SolidColorBrush BrPink  = new SolidColorBrush(Color.FromRgb(252, 129, 129)); // #FC8181

    private System.Windows.Forms.NotifyIcon? _trayIcon;



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
            lblLocalIp.Text = "127.0.0.1:10919";
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
                lblLanIp.Text = (_state.LanIp ?? "Unknown") + ":10919";
            }
            else
            {
                lblLanIp.Text = CrimsonX.Localization.AppStrings.StatusDisconnected;
            }
        }
    }



    // ── Connect Button Ring Animation ──

        private void UpdateRingAnimation(string state)
    {
        var panConnectGlow = this.FindControl<global::Avalonia.Controls.Border>("panConnectGlow");
        var connectGlowRect = this.FindControl<global::Avalonia.Controls.Shapes.Rectangle>("connectGlowRect");
        
        if (panConnectGlow != null)
        {
            bool isConnecting = (state == "Connecting");
            panConnectGlow.Opacity = isConnecting ? 1.0 : 0.0;
            
            if (connectGlowRect != null)
            {
                if (isConnecting)
                {
                    if (!connectGlowRect.Classes.Contains("is-connected"))
                        connectGlowRect.Classes.Add("is-connected");
                }
                else
                {
                    connectGlowRect.Classes.Remove("is-connected");
                }
            }
        }
    }



    // ── Toast Notifications ──

    public void ShowToast(string message, bool success = false)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var toast = this.FindControl<Border>("ToastBorder");
            var toastText = this.FindControl<TextBlock>("ToastText");
            if (toast == null || toastText == null) return;

            _toastTimer?.Stop();

            bool isFa = CrimsonX.Localization.AppStrings.IsPersian;

            toastText.Text = isFa
                ? message
                : message.ToUpperInvariant();
            toastText.FontFamily = isFa
                ? new global::Avalonia.Media.FontFamily("Segoe UI")
                : global::Avalonia.Media.FontFamily.Default;
            toastText.FlowDirection = isFa
                ? global::Avalonia.Media.FlowDirection.RightToLeft
                : global::Avalonia.Media.FlowDirection.LeftToRight;
            toastText.FontWeight = global::Avalonia.Media.FontWeight.Bold;
            toastText.LetterSpacing = 1;
            toastText.Foreground = success ? BrGreen : BrPink;

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




    // ── Connect Progress & Engine Adjustment ──

    internal async Task OnEngineCountChanged(int newCount)
    {
        RequestConfigSave();
    }

    private global::Avalonia.Threading.DispatcherTimer? _fillAnimTimer;
    private double _currentFillPct = 0;
    private double _targetFillPct = -1;

    private global::Avalonia.Controls.TextBlock? _fillTxtBg;
    private global::Avalonia.Controls.TextBlock? _fillTxtConnected;
    private global::Avalonia.Media.LinearGradientBrush? _fillBrush;
    private global::Avalonia.Media.GradientStop? _fillStop1;  
    private global::Avalonia.Media.GradientStop? _fillStop2;  
    private global::Avalonia.Media.GradientStop? _fillStop3;  
    private global::Avalonia.Media.GradientStop? _fillStop4;  

    private void EnsureFillResources()
    {
        if (_fillTxtBg == null)
            _fillTxtBg = this.FindControl<global::Avalonia.Controls.TextBlock>("txtConnectBtn");
        if (_fillTxtConnected == null)
            _fillTxtConnected = this.FindControl<global::Avalonia.Controls.TextBlock>("txtConnectedBtn");

        global::Avalonia.Media.Color fillCol = global::Avalonia.Media.Color.Parse("#DD6B20");
        if (this.TryFindResource("ThemeGlow", out var glowObj) && glowObj is global::Avalonia.Media.Color glow)
        {
            fillCol = glow;
        }

        if (_fillBrush == null)
        {
            _fillStop1 = new global::Avalonia.Media.GradientStop(fillCol, 0.0);
            _fillStop2 = new global::Avalonia.Media.GradientStop(fillCol, 0.0);
            _fillStop3 = new global::Avalonia.Media.GradientStop(global::Avalonia.Media.Colors.White, 0.0);
            _fillStop4 = new global::Avalonia.Media.GradientStop(global::Avalonia.Media.Colors.White, 1.0);
            _fillBrush = new global::Avalonia.Media.LinearGradientBrush
            {
                StartPoint = new global::Avalonia.RelativePoint(0, 0, global::Avalonia.RelativeUnit.Relative),
                EndPoint   = new global::Avalonia.RelativePoint(1, 0, global::Avalonia.RelativeUnit.Relative),
                GradientStops = new global::Avalonia.Media.GradientStops
                    { _fillStop1, _fillStop2, _fillStop3, _fillStop4 }
            };
        }
        else
        {
            if (_fillStop1 != null) _fillStop1.Color = fillCol;
            if (_fillStop2 != null) _fillStop2.Color = fillCol;
        }
    }

    private void SetConnectButtonProgress(int percent)
    {
        CrimsonX.Services.UiEventBus.Instance.PublishConnectionProgress(percent);
        if (percent < 0)
        {
            _targetFillPct = -1;
            _currentFillPct = 0;
            _fillAnimTimer?.Stop();

            if (_fillTxtBg == null)
                _fillTxtBg = this.FindControl<global::Avalonia.Controls.TextBlock>("txtConnectBtn");
            if (_fillTxtConnected == null)
                _fillTxtConnected = this.FindControl<global::Avalonia.Controls.TextBlock>("txtConnectedBtn");

            if (_fillTxtBg != null) { _fillTxtBg.Foreground = BrWhite; _fillTxtBg.Opacity = 1; }
            if (_fillTxtConnected != null) { _fillTxtConnected.Opacity = 0; }
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

                EnsureFillResources();

                if (_fillTxtBg != null)
                {
                    if (_currentFillPct <= 0.001)
                    {
                        _fillTxtBg.Foreground = BrWhite;
                    }
                    else
                    {
                        _fillStop2!.Offset = _currentFillPct;
                        _fillStop3!.Offset = _currentFillPct;
                        _fillTxtBg.Foreground = _fillBrush;
                    }
                }

                if (_currentFillPct >= 0.999 && _targetFillPct >= 1.0 && _state.IsConnected)
                {
                    if (_fillTxtBg != null && _fillTxtConnected != null)
                    {
                        _fillTxtConnected.Text = CrimsonX.Localization.AppStrings.StatusConnected;
                        _fillTxtBg.Opacity = 0;
                        _fillTxtConnected.Opacity = 1;
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
        try { _pipelineCts?.Cancel(); } catch { }
        _ = Task.Run(() => CrimsonX.Services.XrayPipelineManager.StopXray());

        _state.AbortBoot       = true;
        _state.IsEngineRunning = false;
        



        lock (_staggerQueue) { _staggerQueue.Clear(); }
        _session.Stop(); 

        
        _netDiag.StopStatsPolling();
        _netDiag.StopGeoTrace();

        if (_graphAnimCts != null) { try { _graphAnimCts.Cancel(); _graphAnimCts.Dispose(); } catch (Exception ex) { CrimsonX.Services.SimpleLogger.Log(ex); } _graphAnimCts = null; }
        _logTimer?.Stop();
        _logClearTimer?.Stop();
        ProxyService.SetSystemProxy(false);
        _ = RestoreSystemDnsAsync();

        
        SetConnectButtonProgress(-1);

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
        });
        if (isClosing)
        {
            killTask.Wait(3000);
            CrimsonX.Services.JobManager.Shutdown();
        }

        CrimsonX.Services.SimpleLogger.Log($"[Disconnect] Mode={_pollMode}, isClosing={isClosing}");

        try
        {
            foreach (var f in System.IO.Directory.GetFiles(_cfg.XrayDir, "test_*.json"))
                TryDeleteFile(f);
        }
        catch { }

        _state.IsConnected      = false;
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


                var txtXrayLogs = this.FindControl<TextBox>("txtXrayLogs");
                if (txtXrayLogs != null) { txtXrayLogs.Text = ""; _xrayLogLines.Clear(); System.Threading.Interlocked.Exchange(ref _lastXrayLogPos, 0); }

                var lblTot = this.FindControl<TextBlock>("lblTotalData");
                if (lblTot != null) lblTot.Text = "0 MB";
                var lblDn = this.FindControl<TextBlock>("lblDownloadSpeed");
                if (lblDn != null) lblDn.Text = "0 KB/s";
                var lblUp = this.FindControl<TextBlock>("lblUploadSpeed");
                if (lblUp != null) lblUp.Text = "0 KB/s";
                var lblPing = this.FindControl<TextBlock>("lblPing");
                if (lblPing != null) lblPing.Text = "0 ms";

                UpdateRingAnimation("Idle");
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

    private void DoCopyIp(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text) && text.Contains(":") && text != CrimsonX.Localization.AppStrings.StatusDisconnected && text != CrimsonX.Localization.AppStrings.Disabled)
        {
            var clipboard = global::Avalonia.Controls.TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null) _ = clipboard.SetTextAsync(text);
            
            string msg = CrimsonX.Localization.AppStrings.ToastCopiedToClipboard;
            ShowToast(msg, success: true);
        }
    }

    private void RefreshPing_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_state.IsConnected && !_state.IsGeoTracing)
        {
            StartGeoPing();
        }
    }


    // ── Connect / Disconnect Trigger ──

    private async void btnConnect_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_state.IsConnected || _state.IsEngineRunning)
        {
            StopAllEngines();
            return;
        }

        if (_cfg.LastXrayMode == "VPN Mode")
        {
            if (IsVpnAdapterInUse())
            {
                bool isFa = CrimsonX.Localization.AppStrings.IsPersian;
                ShowToast(CrimsonX.Localization.AppStrings.ToastVpnInUse);
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
                ShowToast(CrimsonX.Localization.AppStrings.ToastAdapterNotAvailable);
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

        if (sender != null && !System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
        {
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
                    ShowToast(CrimsonX.Localization.AppStrings.ToastModeSwitchFailed);
                    return false;
                }

                await CrimsonX.Services.XrayPipelineManager.SwapOutboundsAsync(outbounds, _cfg, _cfg.XrayDir, true);

                if (!SingboxConfigWriter.Write(_cfg, _cfg.SbDir))
                {
                    ShowToast(CrimsonX.Localization.AppStrings.ToastWriteVpnFailed);
                    return false;
                }

                var sbProc = ProcessService.StartProcessDirect(
                    GetAppPath(@"Data\sing_box\sing-box.exe"), "run -c config.json", _cfg.SbDir);
                if (sbProc == null)
                {
                    ShowToast(CrimsonX.Localization.AppStrings.ToastStartVpnFailed);
                    return false;
                }
                _sbPid = sbProc.Id;
                CrimsonX.Services.SimpleLogger.Log($"[ModeHotSwap] {oldMode} -> VPN Mode (sing-box pid={_sbPid})");
            }
            else
            {
                int? sbPid = _sbPid;
                _sbPid = null;
                CrimsonX.Services.ProcessService.KillVpnProcess(sbPid);

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
            ShowToast(CrimsonX.Localization.AppStrings.ToastModeSwitchFailed);
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
            ShowToast(CrimsonX.Localization.AppStrings.ToastEngineStartFailedPrefix + ex.Message);
            StopAllEngines();
        }
    }


    internal bool RestartSingBoxOnly()
    {
        if (_cfg.LastXrayMode != "VPN Mode" || !_state.IsConnected) return false;

        try
        {
            if (_sbPid != null) { CrimsonX.Services.ProcessService.KillVpnProcess(_sbPid); _sbPid = null; }

            if (!CrimsonX.Services.SingboxConfigWriter.Write(_cfg, _cfg.SbDir))
            {
                CrimsonX.Services.SimpleLogger.Log("[SingBox] Config write failed during rule apply.");
                return false;
            }

            var proc = CrimsonX.Services.ProcessService.StartProcessDirect(
                GetAppPath(@"Data\sing_box\sing-box.exe"), "run -c config.json", _cfg.SbDir);
            if (proc == null) return false;

            _sbPid = proc.Id;
            CrimsonX.Services.SimpleLogger.Log($"[SingBox] Restarted with updated app rules (pid={_sbPid}).");
            return true;
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
            return false;
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


    private global::Avalonia.Controls.TextBlock? _lblTimerCache;

    // ── Session Clock ──

    private void StartSessionClock()
    {
        var panTimerContent = this.FindControl<StackPanel>("panTimerContent");
        if (panTimerContent != null) panTimerContent.IsVisible = true;
        var lblDisconnected = this.FindControl<TextBlock>("lblDisconnected");
        if (lblDisconnected != null) lblDisconnected.IsVisible = false;

        _session.Start();
    }


    private global::Avalonia.Threading.DispatcherTimer? _logTimer;
    private global::Avalonia.Threading.DispatcherTimer? _logClearTimer;
    private long _lastXrayLogPos = 0;
    private int _isReadingLogs = 0; 
    private readonly System.Collections.Generic.List<string> _xrayLogLines = new();

    // ── Stats & Logs Mini-Panels ──

    private void StartLogsTimers()
    {
        if (_logTimer != null)
        {
            _logTimer.Stop();
            _logTimer.Tick -= LogTimer_Tick;
        }
        _logTimer = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _logTimer.Tick += LogTimer_Tick;
        _logTimer.Start();
        _logClearTimer?.Start();
    }

    private void StopLogsTimers()
    {
        if (_logTimer != null)
        {
            _logTimer.Stop();
            _logTimer.Tick -= LogTimer_Tick;
            _logTimer = null;
        }
        _logClearTimer?.Stop();
    }

    internal void InitLogClearTimer()
    {
        _logClearTimer = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(2) };
        _logClearTimer.Tick += (s, e) =>
        {
            foreach (var lf in new[] { @"Data\Xray\access.log", @"Data\Xray\error.log" })
            {
                var fp = GetAppPath(lf);
                if (File.Exists(fp))
                    try { using var fs = new FileStream(fp, FileMode.Truncate, FileAccess.Write, FileShare.ReadWrite); } catch (Exception ex) { CrimsonX.Services.SimpleLogger.Log(ex); }
            }
        };
    }

    private void chkLogs_CheckedChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isInitializingSettings) return;
        var panLogs = this.FindControl<global::Avalonia.Controls.Border>("panLogs");
        var chkLogs = sender as global::Avalonia.Controls.ToggleSwitch;
        if (panLogs != null && chkLogs != null)
        {
            if (chkLogs.IsChecked ?? false)
            {
                panLogs.MaxHeight       = 500;
                panLogs.Opacity         = 1;
                panLogs.BorderThickness = new global::Avalonia.Thickness(1);
                _state.IsLogsOpen       = true;
                
                var txtLogs = this.FindControl<global::Avalonia.Controls.TextBox>("txtXrayLogs");
                if (txtLogs != null) { txtLogs.Text = string.Join("\n", _xrayLogLines); txtLogs.CaretIndex = txtLogs.Text.Length; }
                
                StartLogsTimers();
            }
            else
            {
                panLogs.MaxHeight       = 0;
                panLogs.Opacity         = 0;
                panLogs.BorderThickness = new global::Avalonia.Thickness(0);
                _state.IsLogsOpen       = false;
                StopLogsTimers();
            }
            RequestConfigSave();
        }
    }

    private int _activeMiniNav = 0;

    private void UpdateMiniNavUnderline()
    {
        var container = this.FindControl<global::Avalonia.Controls.Panel>("panMiniNavContainer");
        var underline = this.FindControl<global::Avalonia.Controls.Shapes.Rectangle>("rectStatsUnderline");
        var btnStat = this.FindControl<global::Avalonia.Controls.Button>("btnStatNav");
        var btnLog = this.FindControl<global::Avalonia.Controls.Button>("btnLogNav");

        if (container == null || underline == null || btnStat == null || btnLog == null) return;

        var activeBtn = _activeMiniNav == 0 ? btnStat : btnLog;
        if (activeBtn.Bounds.Width == 0) return;

        var point = activeBtn.TranslatePoint(new global::Avalonia.Point(0, 0), container);
        if (!point.HasValue) return;

        double width = activeBtn.Bounds.Width;
        double xPos = point.Value.X;
        double underlineWidth = 28;
        double centerOffset = xPos + (width / 2) - (underlineWidth / 2);

        underline.Margin = new global::Avalonia.Thickness(centerOffset, 0, 0, 0);
    }

    private void StatNav_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var statsCarousel = this.FindControl<global::Avalonia.Controls.Carousel>("statsCarousel");
        var btnStat      = this.FindControl<global::Avalonia.Controls.Button>("btnStatNav");
        var btnLog       = this.FindControl<global::Avalonia.Controls.Button>("btnLogNav");
        if (statsCarousel != null) statsCarousel.SelectedIndex = 0;
        _activeMiniNav = 0;
        UpdateMiniNavUnderline();
        if (btnStat      != null) { var tb = btnStat.Content as global::Avalonia.Controls.TextBlock; if (tb != null) tb.Foreground = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#A0AEC0")); }
        if (btnLog       != null) { var tb = btnLog.Content  as global::Avalonia.Controls.TextBlock; if (tb != null) tb.Foreground = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#8B949E")); }
        
        _state.IsLogsOpen = false;
        StopLogsTimers();
    }

    private void LogNav_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var statsCarousel = this.FindControl<global::Avalonia.Controls.Carousel>("statsCarousel");
        var btnStat      = this.FindControl<global::Avalonia.Controls.Button>("btnStatNav");
        var btnLog       = this.FindControl<global::Avalonia.Controls.Button>("btnLogNav");
        if (statsCarousel != null) statsCarousel.SelectedIndex = 1;
        _activeMiniNav = 1;
        UpdateMiniNavUnderline();
        if (btnStat      != null) { var tb = btnStat.Content as global::Avalonia.Controls.TextBlock; if (tb != null) tb.Foreground = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#8B949E")); }
        if (btnLog       != null) { var tb = btnLog.Content  as global::Avalonia.Controls.TextBlock; if (tb != null) tb.Foreground = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#A0AEC0")); }
        
        _state.IsLogsOpen = true;
        StartLogsTimers();
    }

    private void RestoreHomeMiniNav()
    {
        var statsCarousel = this.FindControl<global::Avalonia.Controls.Carousel>("statsCarousel");
        var btnStat = this.FindControl<global::Avalonia.Controls.Button>("btnStatNav");
        var btnLog  = this.FindControl<global::Avalonia.Controls.Button>("btnLogNav");

        if (_state.IsLogsOpen)
        {
            if (statsCarousel != null) statsCarousel.SelectedIndex = 1;
            _activeMiniNav = 1;
            StartLogsTimers();
            LogTimer_Tick(null, EventArgs.Empty);
        }
        else
        {
            if (statsCarousel != null) statsCarousel.SelectedIndex = 0;
            _activeMiniNav = 0;
        }

        var statColor = _activeMiniNav == 0 ? "#A0AEC0" : "#8B949E";
        var logColor  = _activeMiniNav == 1 ? "#A0AEC0" : "#8B949E";
        if (btnStat != null) { var tb = btnStat.Content as global::Avalonia.Controls.TextBlock; if (tb != null) tb.Foreground = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse(statColor)); }
        if (btnLog  != null) { var tb = btnLog.Content  as global::Avalonia.Controls.TextBlock; if (tb != null) tb.Foreground = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse(logColor)); }

        UpdateMiniNavUnderline();
        global::Avalonia.Threading.Dispatcher.UIThread.Post(UpdateMiniNavUnderline, global::Avalonia.Threading.DispatcherPriority.Render);
    }

    private void LogTimer_Tick(object? sender, EventArgs e)
    {
        if (!_state.IsLogsOpen) return;
        var selCount = _state.IsEngineRunning ? _activeEngines : 1;

        if (!_state.IsEngineRunning)
        {
            var txtLogs = this.FindControl<global::Avalonia.Controls.TextBox>("txtXrayLogs");
            if (txtLogs != null) txtLogs.Text = "";
            _xrayLogLines.Clear();
            System.Threading.Interlocked.Exchange(ref _lastXrayLogPos, 0);
            return;
        }

        if (System.Threading.Interlocked.CompareExchange(ref _isReadingLogs, 1, 0) != 0) return;
        var xrayLogPath = GetAppPath(@"Data\Xray\access.log");
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                var txtXrayLogs = this.FindControl<global::Avalonia.Controls.TextBox>("txtXrayLogs");
                if (txtXrayLogs != null)
                {
                    if (File.Exists(xrayLogPath))
                    {
                        using var fs = new FileStream(xrayLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var sr = new StreamReader(fs);
                        var fullText = sr.ReadToEnd();
                        var lines = fullText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                            .Where(l => !l.Contains(":10999"))
                                            .ToArray();
                        var last15 = lines.Skip(Math.Max(0, lines.Length - 15)).ToArray();
                        
                        var cleanLines = new List<string>();
                        foreach (var line in last15)
                        {
                            int firstSpace = line.IndexOf(' ');
                            if (firstSpace > 0 && firstSpace + 1 < line.Length)
                            {
                                int secondSpace = line.IndexOf(' ', firstSpace + 1);
                                if (secondSpace > 0 && secondSpace + 1 < line.Length)
                                {
                                    cleanLines.Add(line.Substring(secondSpace + 1));
                                    continue;
                                }
                            }
                            cleanLines.Add(line);
                        }
                        
                        if (cleanLines.Count > 0)
                        {
                            txtXrayLogs.Text = string.Join("\n", cleanLines);
                            txtXrayLogs.CaretIndex = txtXrayLogs.Text.Length;
                        }
                        else if (fs.Length == 0)
                        {
                            txtXrayLogs.Text = "Waiting for traffic logs...";
                        }
                    }
                    else
                    {
                        txtXrayLogs.Text = "Log file not created yet.";
                    }
                }
            }
            catch (Exception ex)
            {
                var txtXrayLogs = this.FindControl<global::Avalonia.Controls.TextBox>("txtXrayLogs");
                if (txtXrayLogs != null) txtXrayLogs.Text = "Error: " + ex.Message;
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _isReadingLogs, 0);
            }
        });
    }


    private void chkStats_CheckedChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var panStats = this.FindControl<global::Avalonia.Controls.Border>("panStats");
        var chkStats = sender as global::Avalonia.Controls.ToggleSwitch;
        if (panStats != null && chkStats != null)
        {
            if (chkStats.IsChecked ?? false)
            {
                panStats.MaxHeight       = 100;
                panStats.Opacity         = 1;
                panStats.BorderThickness = new global::Avalonia.Thickness(1);
            }
            else
            {
                panStats.MaxHeight       = 0;
                panStats.Opacity         = 0;
                panStats.BorderThickness = new global::Avalonia.Thickness(0);
            }
        }
    }


    // ── Network diagnostics event subscriptions 

    // ── Geo Ping & Network Diagnostics ──

    internal void InitNetDiag()
    {
        _netDiag.GeoTraceCompleted += OnGeoTraceCompleted;
        _netDiag.StatsUpdated      += OnStatsUpdated;

        CrimsonX.Services.UiEventBus.Instance.ToastRequested += evt =>
            Dispatcher.UIThread.Post(() => ShowToast(evt.Message, evt.Success));

        _session.ElapsedTimeUpdated += elapsed =>
            Dispatcher.UIThread.Post(() =>
            {
                if (_lblTimerCache == null)
                    _lblTimerCache = this.FindControl<global::Avalonia.Controls.TextBlock>("lblTimer");
                if (_lblTimerCache != null) _lblTimerCache.Text = elapsed;
            });
    }

    private void StartGeoPing()
    {
        _state.IsGeoTracing = true;

        var lblCountry = this.FindControl<TextBlock>("lblCountryName");
        var lblPing    = this.FindControl<TextBlock>("lblPing");
        if (lblCountry != null) lblCountry.Text = CrimsonX.Localization.AppStrings.GeoTracing;
        if (lblPing    != null) lblPing.Text    = "0 ms";

        _netDiag.StartGeoTrace();
    }

    private void OnGeoTraceCompleted(CrimsonX.Services.GeoTraceResult result)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _state.IsGeoTracing = false;
            if (!_state.IsConnected) return;

            var lblCountry = this.FindControl<TextBlock>("lblCountryName");
            var lblPing    = this.FindControl<TextBlock>("lblPing");

            bool isFa    = CrimsonX.Localization.AppStrings.IsPersian;
            string country = result.Country;
            if (isFa)
            {
                country = CrimsonX.Localization.GeoTranslation.GetCountryFa(result.CountryCode, country);
            }

            string displayName = string.IsNullOrWhiteSpace(country)
                ? (result.PingMs == 0
                    ? CrimsonX.Localization.AppStrings.GeoTimeout
                    : CrimsonX.Localization.AppStrings.StatusDisconnected)
                : country;

            if (lblCountry != null) lblCountry.Text = displayName;
            if (lblPing    != null) lblPing.Text    = result.PingMs > 0 ? $"{result.PingMs}ms" : "0 ms";
        });
    }

    // ── Stats Polling & Live Graph ──

    private void StartStatsPolling()
    {
        UpdateLanPortUI();
        _logClearTimer?.Stop();
        _logClearTimer?.Start();

        _netDiag.StartStatsPolling(() => _state.IsConnected);
    }

    private void OnStatsUpdated(CrimsonX.Services.StatsSnapshot snap)
    {
        _state.SessionDataBytes += snap.DiffUpBytes + snap.DiffDnBytes;

        string tot = _state.SessionDataBytes >= 1_073_741_824
            ? $"{Math.Round(_state.SessionDataBytes / 1_073_741_824.0, 2)} GB"
            : _state.SessionDataBytes >= 1_048_576
                ? $"{Math.Round(_state.SessionDataBytes / 1_048_576.0, 1)} MB"
                : $"{Math.Round(_state.SessionDataBytes / 1024.0, 1)} KB";

        Dispatcher.UIThread.Post(() =>
        {
            if (lblTotalData      != null) lblTotalData.Text      = tot;
            if (lblDownloadSpeed  != null) lblDownloadSpeed.Text  = snap.SpeedDn;
            if (lblUploadSpeed    != null) lblUploadSpeed.Text    = snap.SpeedUp;
            DrawGraph(snap.UpHistory, snap.DnHistory);
        });
    }



    private global::Avalonia.Controls.Shapes.Path? _graphDownload;
    private global::Avalonia.Controls.Shapes.Path? _graphUpload;
    private global::Avalonia.Controls.Shapes.Path? _graphDownloadFill;
    private global::Avalonia.Controls.Shapes.Path? _graphUploadFill;
    private readonly System.Collections.Generic.List<global::Avalonia.Point> _ptsUpCache = new System.Collections.Generic.List<global::Avalonia.Point>(40);
    private readonly System.Collections.Generic.List<global::Avalonia.Point> _ptsDnCache = new System.Collections.Generic.List<global::Avalonia.Point>(40);
    private global::Avalonia.Threading.DispatcherTimer? _graphAnimTimer;
    private DateTime _graphAnimStartTime;
    private double _graphAnimStep;
    private global::Avalonia.Media.TranslateTransform? _graphTransform;

    private void DrawGraph(double[] upHistory, double[] dnHistory)
    {
        if (_graphDownload == null)
        {
            _graphDownload     = this.FindControl<global::Avalonia.Controls.Shapes.Path>("graphDownload");
            _graphUpload       = this.FindControl<global::Avalonia.Controls.Shapes.Path>("graphUpload");
            _graphDownloadFill = this.FindControl<global::Avalonia.Controls.Shapes.Path>("graphDownloadFill");
            _graphUploadFill   = this.FindControl<global::Avalonia.Controls.Shapes.Path>("graphUploadFill");
        }
        var graphDownload     = _graphDownload;
        var graphUpload       = _graphUpload;
        var graphDownloadFill = _graphDownloadFill;
        var graphUploadFill   = _graphUploadFill;

        if (graphUpload == null || graphDownload == null || graphUploadFill == null || graphDownloadFill == null) return;

        const double width         = 150;
        const double height        = 40;
        const double topPadding    = 4;
        const double bottomPadding = 2;
        int count = Math.Min(upHistory.Length, dnHistory.Length);
        if (count < 2) return;

        double step   = width / (40 - 1);
        double maxUp  = upHistory.Length > 0 ? upHistory.Max() : 0;
        double maxDn  = dnHistory.Length > 0 ? dnHistory.Max() : 0;
        double maxVal = Math.Max(maxUp, maxDn);
        if (maxVal < 1024) maxVal = 1024;

        _ptsUpCache.Clear();
        _ptsDnCache.Clear();

        int    startIdx    = 40 - count;
        double drawHeight  = height - topPadding - bottomPadding;

        for (int i = 0; i < count; i++)
        {
            double x   = (startIdx + i) * step;
            double yUp = (height - bottomPadding) - (upHistory[i] / maxVal * drawHeight);
            double yDn = (height - bottomPadding) - (dnHistory[i] / maxVal * drawHeight);
            _ptsUpCache.Add(new global::Avalonia.Point(x, yUp));
            _ptsDnCache.Add(new global::Avalonia.Point(x, yDn));
        }

        graphUpload.Data       = GenerateSmoothSpline(_ptsUpCache, false, width, height);
        graphDownload.Data     = GenerateSmoothSpline(_ptsDnCache, false, width, height);
        graphUploadFill.Data   = GenerateSmoothSpline(_ptsUpCache, true,  width, height);
        graphDownloadFill.Data = GenerateSmoothSpline(_ptsDnCache, true,  width, height);

        var canvas = graphUpload.Parent as global::Avalonia.Controls.Canvas;
        if (canvas != null && canvas.RenderTransform is global::Avalonia.Media.TranslateTransform t)
        {
            if (_graphAnimTimer == null)
            {
                _graphAnimTimer = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
                _graphAnimTimer.Tick += (s, e) =>
                {
                    if (_graphTransform != null)
                    {
                        double elapsed = (DateTime.UtcNow - _graphAnimStartTime).TotalMilliseconds;
                        if (elapsed >= 1000)
                        {
                            _graphTransform.X = -_graphAnimStep;
                            _graphAnimTimer.Stop();
                        }
                        else
                        {
                            _graphTransform.X = -_graphAnimStep * (elapsed / 1000.0);
                        }
                    }
                };
            }
            
            _graphAnimStartTime = DateTime.UtcNow;
            _graphAnimStep = step;
            _graphTransform = t;
            t.X = 0;
            _graphAnimTimer.Start();
        }
    }

    private global::Avalonia.Media.StreamGeometry GenerateSmoothSpline(System.Collections.Generic.List<global::Avalonia.Point> points, bool isFill, double width, double height)
    {
        var geom = new global::Avalonia.Media.StreamGeometry();
        using (var ctx = geom.Open())
        {
            if (points.Count == 0) return geom;
            
            if (isFill)
            {
                ctx.BeginFigure(new global::Avalonia.Point(points[0].X, height), true);
                ctx.LineTo(points[0]);
            }
            else
            {
                ctx.BeginFigure(points[0], false);
            }

            for (int i = 1; i < points.Count; i++)
            {
                var p0 = i >= 2 ? points[i - 2] : points[i - 1];
                var p1 = points[i - 1];
                var p2 = points[i];
                var p3 = i + 1 < points.Count ? points[i + 1] : points[i];

                double t = 0.25;
                var cp1 = new global::Avalonia.Point(p1.X + (p2.X - p0.X) * t, p1.Y + (p2.Y - p0.Y) * t);
                var cp2 = new global::Avalonia.Point(p2.X - (p3.X - p1.X) * t, p2.Y - (p3.Y - p1.Y) * t);

                ctx.CubicBezierTo(cp1, cp2, p2);
            }

            if (isFill)
            {
                ctx.LineTo(new global::Avalonia.Point(points[points.Count - 1].X, height));
            }
        }
        return geom;
    }


    private static int ReadVarint(byte[] data, ref int p)
    {
        int result = 0, shift = 0;
        while (p < data.Length)
        {
            byte b = data[p++];
            if (shift < 32) result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
        }
        return result;
    }

    private static long ReadVarint64(byte[] data, ref int p)
    {
        long result = 0; int shift = 0;
        while (p < data.Length)
        {
            byte b = data[p++];
            result |= (long)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
        }
        return result;
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
                    WindowState = global::Avalonia.Controls.WindowState.Normal;
                    Show();
                    Activate();
                    Topmost = true;
                    Topmost = false;
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




    

    private void btnSplitClose_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var panSplitOverlay = this.FindControl<global::Avalonia.Controls.Border>("panSplitOverlay");
        if (panSplitOverlay != null)
        {
            panSplitOverlay.Classes.Remove("popupOpen"); global::Avalonia.Threading.DispatcherTimer.RunOnce(() => { panSplitOverlay.IsVisible = false; }, TimeSpan.FromMilliseconds(200));
        }
    }


    

    

    // ── Custom Xray Exit Node ──

    internal void btnXrayExitNodeToggle_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {

        var src = e.Source as global::Avalonia.Controls.Control;
        while (src != null)
        {
            if (src.Name == "togXrayExitNode")
                return;
            src = src.Parent as global::Avalonia.Controls.Control;
        }
        var panToggle = this.FindControl<global::Avalonia.Controls.Border>("panXrayExitNodeToggle");
        var btnToggle = this.FindControl<global::Avalonia.Controls.Button>("btnXrayExitNodeToggle");
        var pan = this.FindControl<global::Avalonia.Controls.Border>("panXrayExitNode");
        var ico = this.FindControl<global::Avalonia.Controls.PathIcon>("icoXrayExitNodeExpander");
        var txt = this.FindControl<global::Avalonia.Controls.TextBox>("txtXrayJson");
        var tog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togXrayExitNode");
        
        if (pan != null && ico != null && txt != null && tog != null)
        {
            if (pan.MaxHeight == 0)
            {
                txt.Text = _cfg.V2rayChainJson;
                tog.IsChecked = _cfg.EnableV2rayChain;
                
                pan.MaxHeight = 350;
                pan.Opacity = 1;
                
                var transform = new global::Avalonia.Media.RotateTransform(180);
                ico.RenderTransform = transform;
                
                if (panToggle != null) panToggle.CornerRadius = new global::Avalonia.CornerRadius(8, 8, 0, 0);
                if (btnToggle != null) btnToggle.CornerRadius = new global::Avalonia.CornerRadius(8, 8, 0, 0);
            }
            else
            {
                pan.MaxHeight = 0;
                pan.Opacity = 0;
                
                var transform = new global::Avalonia.Media.RotateTransform(0);
                ico.RenderTransform = transform;
                
                if (panToggle != null) panToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
                if (btnToggle != null) btnToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
            }
        }
    }

    internal void btnXrayCancel_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var pan = this.FindControl<global::Avalonia.Controls.Border>("panXrayExitNode");
        var ico = this.FindControl<global::Avalonia.Controls.PathIcon>("icoXrayExitNodeExpander");
        var panToggle = this.FindControl<global::Avalonia.Controls.Border>("panXrayExitNodeToggle");
        var btnToggle = this.FindControl<global::Avalonia.Controls.Button>("btnXrayExitNodeToggle");
        if (pan != null && ico != null)
        {
            pan.MaxHeight = 0;
            pan.Opacity = 0;
            var transform = new global::Avalonia.Media.RotateTransform(0);
            ico.RenderTransform = transform;
            if (panToggle != null) panToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
            if (btnToggle != null) btnToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
        }
    }

    internal async void btnXraySave_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var txt = this.FindControl<global::Avalonia.Controls.TextBox>("txtXrayJson");
        var tog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togXrayExitNode");
        
        if (txt != null && tog != null)
        {
            var text = txt.Text ?? "";
            bool enable = tog.IsChecked ?? false;
            
            if (string.IsNullOrWhiteSpace(text))
            {
                _cfg.V2rayChainJson = "";
                _cfg.EnableV2rayChain = enable;
                ConfigService.Save(_cfg, _state, _cfg.CfgFile);
                
                btnXrayCancel_Click(sender, e);
                return;
            }
            
            try
            {
                var parsed = Newtonsoft.Json.Linq.JObject.Parse(text);
                Newtonsoft.Json.Linq.JToken? testNode = parsed["outbounds"] is Newtonsoft.Json.Linq.JArray arr ? arr.FirstOrDefault() : parsed;
                if (testNode?["protocol"] == null)
                    throw new Exception("Missing 'protocol' field.");
                
                var streamSettings = testNode["streamSettings"];
                if (streamSettings != null)
                {

                }
                
                var settings = testNode["settings"];
                if (settings != null)
                {
                    var ports = settings.SelectTokens("..port").ToList();
                    foreach (var portToken in ports)
                    {
                        if (int.TryParse(portToken.ToString(), out int port))
                        {
                            if (port != 80 && port != 443)
                            {
                                ShowToast(CrimsonX.Localization.AppStrings.ToastPortsSupported);
                                return;
                            }
                        }
                    }
                }
                
                string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
                try
                {
                    System.IO.File.WriteAllText(tempFile, text);
                    
                    string xrayExe = System.IO.Path.Combine(_cfg.BaseDir, "Data", "xray", "xray.exe");
                    if (System.IO.File.Exists(xrayExe))
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = xrayExe,
                            Arguments = $"-test -config \"{tempFile}\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        
                        using (var proc = System.Diagnostics.Process.Start(psi))
                        {
                            if (proc != null)
                            {
                                var outTask = proc.StandardOutput.ReadToEndAsync();
                                var errTask = proc.StandardError.ReadToEndAsync();
                                await proc.WaitForExitAsync();
                                if (proc.ExitCode != 0)
                                {
                                    string err = await errTask;
                                    string outStr = await outTask;
                                    string msg = string.IsNullOrWhiteSpace(err) ? outStr : err;
                                    var lines = msg.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                                    msg = string.Join(" ", lines.Where(l => !l.Contains("Xray, Penetrates Everything") && !l.Contains("unified platform")));
                                    msg = msg.Trim();
                                    ShowToast(CrimsonX.Localization.AppStrings.ToastXrayRejected + msg.Substring(0, System.Math.Min(msg.Length, 150)));
                                    return;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    try { if (System.IO.File.Exists(tempFile)) System.IO.File.Delete(tempFile); } catch (Exception ex) { CrimsonX.Services.SimpleLogger.Log(ex); }
                }

                _cfg.V2rayChainJson = text.Trim();
                _cfg.EnableV2rayChain = true;
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    tog.IsChecked = true;
                });
                
                ConfigService.Save(_cfg, _state, _cfg.CfgFile);
                if (_state.IsEngineRunning) SmartRestartXray();
                
                btnXrayCancel_Click(sender, e);
            }
            catch (Exception ex)
            {
                CrimsonX.Services.SimpleLogger.Log(ex);
                ShowToast(CrimsonX.Localization.AppStrings.ToastInvalidJson + " " + ex.Message);
            }
        }
    }


    internal void txtXrayJson_TextChanged(object? sender, global::Avalonia.Controls.TextChangedEventArgs e)
    {
        var txt = sender as global::Avalonia.Controls.TextBox;
        if (txt == null || string.IsNullOrWhiteSpace(txt.Text)) return;

        string text = txt.Text.Trim();
        


        if (CrimsonX.Services.XrayLinkParser.TryParseLink(text, out string json))
        {
            txt.Text = json;
            ShowToast(CrimsonX.Localization.AppStrings.ToastLinkConverted, success: true);
        }
    }

    internal async void btnXrayImport_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var topLevel = global::Avalonia.Controls.TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select Xray JSON File",
                AllowMultiple = false,
                FileTypeFilter = new[] { new global::Avalonia.Platform.Storage.FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } }, new global::Avalonia.Platform.Storage.FilePickerFileType("All Files") { Patterns = new[] { "*.*" } } }
            });

            if (files != null && files.Count > 0)
            {
                var file = files[0];
                var path = file.Path.LocalPath;
                if (System.IO.File.Exists(path))
                {
                    var txt = this.FindControl<global::Avalonia.Controls.TextBox>("txtXrayJson");
                    if (txt != null)
                        txt.Text = System.IO.File.ReadAllText(path);
                }
            }
        }
        catch
        {
            ShowToast(CrimsonX.Localization.AppStrings.ToastFailedImport);
        }
    }

    // ── Startup & System Setting Toggles ──

    internal async void SettingTog_CheckedChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isInitializingSettings) return;

        var tog = sender as global::Avalonia.Controls.ToggleSwitch;
        if (tog == null) return;

        bool val = tog.IsChecked ?? false;

        switch (tog.Name)
        {
            case "btnBootTog":
                try {
                    string exe = System.Environment.ProcessPath ?? "";
                    await CrimsonX.Services.ProcessService.UpdateBootScheduledTask(val, exe);
                    _cfg.LaunchOnBoot = val;
                } catch (System.Exception ex) {
                    _cfg.LaunchOnBoot = false;
                    tog.IsChecked = false;
                    ShowToast(CrimsonX.Localization.AppStrings.ToastTaskFailed + ex.Message);
                }
                break;
            case "btnAutoTog":
                _cfg.AutoStart = val;
                break;
            case "btnStartMinTog":
                _cfg.StartMinimized = val;
                break;
            case "btnTrayTog":
                _cfg.MinimizeToTray = val;
                break;
            case "btnAdBlockTog":
                _cfg.EnableAdBlock = val;
                if (_state.IsEngineRunning) SmartRestartXray();
                break;
            case "btnLanTog":
                _cfg.AllowLanConnections = val;
                UpdateLanPortUI();
                SmartRestartXray();
                break;
            case "btnDebugTog":
                _cfg.DebugMode = val;
                CrimsonX.Services.SimpleLogger.EnableLogging = val;
                break;
        }

        RequestConfigSave();
    }

    // ── Desktop / Start-Menu Shortcuts ──

    internal void Shortcut_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var btn = sender as global::Avalonia.Controls.Button;
        if (btn == null) return;

        try
        {
            Type? wshType = Type.GetTypeFromProgID("WScript.Shell");
            if (wshType == null) return;
            var ws = (dynamic)Activator.CreateInstance(wshType)!;

            string destPath = "";
            if (btn.Name == "btnDesktopShortcut")
            {
                destPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), "CrimsonX.lnk");
            }
            else if (btn.Name == "btnStartMenuShortcut")
            {
                string programsPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.StartMenu), "Programs");
                if (!System.IO.Directory.Exists(programsPath)) System.IO.Directory.CreateDirectory(programsPath);
                destPath = System.IO.Path.Combine(programsPath, "CrimsonX.lnk");
            }

            dynamic sc = ws.CreateShortcut(destPath);
            sc.TargetPath = System.Environment.ProcessPath ?? "";
            sc.WorkingDirectory = _cfg.BaseDir;
            sc.Save();

            ShowToast(CrimsonX.Localization.AppStrings.ToastShortcutCreated, success: true);
        }
        catch
        {
            ShowToast(CrimsonX.Localization.AppStrings.ToastShortcutFailed);
        }
    }


    // ── Xray Exit-Node & Direct-UDP Toggle Actions ──

        internal void togXrayExitNode_IsCheckedChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isInitializingSettings) return;
        var tog = sender as global::Avalonia.Controls.ToggleSwitch;
        if (tog != null)
        {
            if (tog.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(_cfg.V2rayChainJson))
                {
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() => tog.IsChecked = false);
                    
                    var panXrayExitNode = this.FindControl<global::Avalonia.Controls.Border>("panXrayExitNode");
                    var icoXrayExitNodeExpander = this.FindControl<global::Avalonia.Controls.PathIcon>("icoXrayExitNodeExpander");
                    
                    if (panXrayExitNode != null && panXrayExitNode.MaxHeight == 0)
                    {
                        panXrayExitNode.MaxHeight = 500;
                        panXrayExitNode.Opacity = 1;
                        if (icoXrayExitNodeExpander != null)
                            icoXrayExitNodeExpander.RenderTransform = new global::Avalonia.Media.RotateTransform(180);
                        
                        var panToggle = this.FindControl<global::Avalonia.Controls.Border>("panXrayExitNodeToggle");
                        var btnToggle = this.FindControl<global::Avalonia.Controls.Button>("btnXrayExitNodeToggle");
                        if (panToggle != null) panToggle.CornerRadius = new global::Avalonia.CornerRadius(8, 8, 0, 0);
                        if (btnToggle != null) btnToggle.CornerRadius = new global::Avalonia.CornerRadius(8, 8, 0, 0);
                    }
                    return;
                }
                else if (!_cfg.EnableV2rayChain)
                {
                    _cfg.EnableV2rayChain = true;
                    RequestConfigSave();
                    if (_state.IsEngineRunning) SmartRestartXray();
                }
            }
            else
            {
                if (_cfg.EnableV2rayChain)
                {
                    _cfg.EnableV2rayChain = false;
                    RequestConfigSave();
                    if (_state.IsEngineRunning) SmartRestartXray();
                }
            }
        }
    }



    private void togDirectUDP_IsCheckedChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isInitializingSettings) return;
        var tog = sender as global::Avalonia.Controls.ToggleSwitch;
        if (tog != null)
        {
            _cfg.EnableDirectUDP = tog.IsChecked == true;
            RequestConfigSave();
            
            string runningMode = _cfg.LastXrayMode;

            if (_state.IsEngineRunning)
            {
                if (runningMode == "Proxy Mode" || runningMode == "Clear Proxy")
                    SmartRestartXray();
                else
                    ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
            }
        }
    }

    // ── Adapter Binding & Adapter Scan ──

    internal void btnAdapterBindingToggle_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var src = e.Source as global::Avalonia.Controls.Control;
        while (src != null)
        {
            if (src.Name == "togAdapterBinding") return;
            src = src.Parent as global::Avalonia.Controls.Control;
        }

        var pan = this.FindControl<global::Avalonia.Controls.Border>("panAdapterBinding");
        var ico = this.FindControl<global::Avalonia.Controls.PathIcon>("icoAdapterBindingExpander");
        var panToggle = this.FindControl<global::Avalonia.Controls.Border>("panAdapterBindingToggle");
        var btnToggle = this.FindControl<global::Avalonia.Controls.Button>("btnAdapterBindingToggle");
        if (pan != null)
        {
            if (pan.MaxHeight == 0)
            {
                pan.MaxHeight = 200;
                pan.Opacity = 1;
                if (ico != null) ico.RenderTransform = new global::Avalonia.Media.RotateTransform(180);
                if (panToggle != null) panToggle.CornerRadius = new global::Avalonia.CornerRadius(8, 8, 0, 0);
                if (btnToggle != null) btnToggle.CornerRadius = new global::Avalonia.CornerRadius(8, 8, 0, 0);
                
                var cmb = this.FindControl<global::Avalonia.Controls.ComboBox>("cmbAdapters");
                if (cmb != null && cmb.Items.Count == 0)
                {
                    btnScanAdapters_Click(null, null);
                }
            }
            else
            {
                pan.MaxHeight = 0;
                pan.Opacity = 0;
                if (ico != null) ico.RenderTransform = new global::Avalonia.Media.RotateTransform(0);
                if (panToggle != null) panToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
                if (btnToggle != null) btnToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
            }
        }
    }

    internal void togAdapterBinding_IsCheckedChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isInitializingSettings) return;
        var tog = sender as global::Avalonia.Controls.ToggleSwitch;
        if (tog != null)
        {
            if (tog.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(_cfg.SelectedAdapterIp))
                {
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { tog.IsChecked = false; });
                    var pan = this.FindControl<global::Avalonia.Controls.Border>("panAdapterBinding");
                    if (pan != null && pan.MaxHeight == 0)
                    {
                        pan.MaxHeight = 200;
                        pan.Opacity = 1;
                        var ico = this.FindControl<global::Avalonia.Controls.PathIcon>("icoAdapterBindingExpander");
                        if (ico != null) ico.RenderTransform = new global::Avalonia.Media.RotateTransform(180);
                        
                        var cmb = this.FindControl<global::Avalonia.Controls.ComboBox>("cmbAdapters");
                        if (cmb != null && cmb.Items.Count == 0)
                        {
                            btnScanAdapters_Click(null, null);
                        }
                    }
                    return;
                }
                else if (!_cfg.EnableAdapterBinding)
                {
                    _cfg.EnableAdapterBinding = true;
                    RequestConfigSave();
                    if (_state.IsEngineRunning)
                        ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
                }
            }
            else
            {
                if (_cfg.EnableAdapterBinding)
                {
                    _cfg.EnableAdapterBinding = false;
                    RequestConfigSave();
                    if (_state.IsEngineRunning) ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
                }
            }
            }
    }

    internal void cmbAdapters_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        var cmb = sender as global::Avalonia.Controls.ComboBox;
        if (cmb != null && cmb.SelectedItem is string selectedText && !string.IsNullOrWhiteSpace(selectedText))
        {
            var parts = selectedText.Split(new[] { " - " }, StringSplitOptions.None);
            if (parts.Length >= 2)
            {
                var newIp   = parts[parts.Length - 1];
                var newName = string.Join(" - ", parts, 0, parts.Length - 1);

                bool changed = newIp != _cfg.SelectedAdapterIp;

                _cfg.SelectedAdapterName = newName;
                _cfg.SelectedAdapterIp = newIp;
                RequestConfigSave();

                if (changed && _cfg.EnableAdapterBinding && _state.IsEngineRunning)
                {
                    ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
                }
            }
        }
    }

    internal void btnScanAdapters_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs? e = null)
    {
        var cmb = this.FindControl<global::Avalonia.Controls.ComboBox>("cmbAdapters");
        if (cmb == null) return;
        
        cmb.Items.Clear();
        var adapters = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
        foreach (var adapter in adapters)
        {
            if (adapter.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && 
                adapter.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
            {
                var properties = adapter.GetIPProperties();
                var ipv4 = properties.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (ipv4 != null && !string.IsNullOrWhiteSpace(ipv4.Address.ToString()))
                {
                    cmb.Items.Add($"{adapter.Name} - {ipv4.Address}");
                }
            }
        }
        
        if (!string.IsNullOrWhiteSpace(_cfg.SelectedAdapterName) && !string.IsNullOrWhiteSpace(_cfg.SelectedAdapterIp))
        {
            var toSelect = $"{_cfg.SelectedAdapterName} - {_cfg.SelectedAdapterIp}";
            var itemsList = cmb.Items.Cast<string>().ToList();
            var index = itemsList.IndexOf(toSelect);
            if (index >= 0)
            {
                cmb.SelectedIndex = index;
            }
            else
            {
                ShowToast(CrimsonX.Localization.AppStrings.ToastAdapterNoLongerAvail);
                _cfg.SelectedAdapterName = "";
                _cfg.SelectedAdapterIp = "";
                RequestConfigSave();
                
                if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
            }
        }
        else if (cmb.Items.Count > 0)
        {
            cmb.SelectedIndex = 0;
        }
    }

    // ─── System DNS state 
    private string?   _savedDnsAdapterName;
    private string[]? _savedDnsServers;

    // ─── DNS Settings panel toggle (expand/collapse) 

    // ── DNS Settings (DoH / System DNS) ──

    internal void btnDnsToggle_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var src = e.Source as global::Avalonia.Controls.Control;
        while (src != null)
        {
            if (src.Name == "togDnsSettings" || src.Name == "togSysDns")
                return;
            src = src.Parent as global::Avalonia.Controls.Control;
        }

        var panToggle = this.FindControl<global::Avalonia.Controls.Border>("panDnsToggle");
        var btnToggle = this.FindControl<global::Avalonia.Controls.Button>("btnDnsToggle");
        var pan       = this.FindControl<global::Avalonia.Controls.Border>("panDnsSettings");
        var ico       = this.FindControl<global::Avalonia.Controls.PathIcon>("icoDnsExpander");
        var cmbDohUrl = this.FindControl<global::Avalonia.Controls.ComboBox>("cmbDohUrl");

        if (pan != null && ico != null && cmbDohUrl != null)
        {
            if (pan.MaxHeight == 0)
            {
                cmbDohUrl.Text = _cfg.UpstreamDohUrl;

                var txtPrimary   = this.FindControl<global::Avalonia.Controls.TextBox>("txtSysDnsPrimary");
                var txtSecondary = this.FindControl<global::Avalonia.Controls.TextBox>("txtSysDnsSecondary");
                if (txtPrimary   != null) txtPrimary.Text   = _cfg.SystemDnsPrimary;
                if (txtSecondary != null) txtSecondary.Text = _cfg.SystemDnsSecondary;

                pan.MaxHeight = 340;
                pan.Opacity   = 1;

                var transform = new global::Avalonia.Media.RotateTransform(180);
                ico.RenderTransform = transform;

                if (panToggle != null) panToggle.CornerRadius = new global::Avalonia.CornerRadius(8, 8, 0, 0);
                if (btnToggle != null) btnToggle.CornerRadius = new global::Avalonia.CornerRadius(8, 8, 0, 0);
            }
            else
            {
                CloseDnsPanel();
            }
        }
    }

    internal void CloseDnsPanel()
    {
        var pan       = this.FindControl<global::Avalonia.Controls.Border>("panDnsSettings");
        var ico       = this.FindControl<global::Avalonia.Controls.PathIcon>("icoDnsExpander");
        var panToggle = this.FindControl<global::Avalonia.Controls.Border>("panDnsToggle");
        var btnToggle = this.FindControl<global::Avalonia.Controls.Button>("btnDnsToggle");
        if (pan != null && ico != null)
        {
            pan.MaxHeight = 0;
            pan.Opacity   = 0;
            var transform = new global::Avalonia.Media.RotateTransform(0);
            ico.RenderTransform = transform;
            if (panToggle != null) panToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
            if (btnToggle != null) btnToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
        }
    }
    // ─── DoH URL inline SAVE button 
    internal void btnDohSave_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var cmbDohUrl = this.FindControl<global::Avalonia.Controls.ComboBox>("cmbDohUrl");
        var tog       = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togDnsSettings");

        if (cmbDohUrl != null && tog != null)
        {
            var url = cmbDohUrl.Text?.Trim() ?? "";
            _cfg.UpstreamDohUrl    = url;
            _cfg.EnableUpstreamDoh = true;

            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                tog.IsChecked = true;
            });

            RequestConfigSave();
            if (_state.IsEngineRunning) SmartRestartXray();
        }
    }

    // ─── DoH toggle 
    internal void togDnsSettings_IsCheckedChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isInitializingSettings) return;
        var tog = sender as global::Avalonia.Controls.ToggleSwitch;
        if (tog == null) return;

        if (tog.IsChecked == true)
        {
            var cmbDohUrl = this.FindControl<global::Avalonia.Controls.ComboBox>("cmbDohUrl");
            var liveUrl   = cmbDohUrl?.Text?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(liveUrl))
                _cfg.UpstreamDohUrl = liveUrl;

            if (string.IsNullOrWhiteSpace(_cfg.UpstreamDohUrl))
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { tog.IsChecked = false; });
                return;
            }

            _cfg.EnableUpstreamDoh = true;
            RequestConfigSave();
            if (_state.IsEngineRunning) SmartRestartXray();
        }
        else
        {
            if (_cfg.EnableUpstreamDoh)
            {
                _cfg.EnableUpstreamDoh = false;
                RequestConfigSave();
                if (_state.IsEngineRunning) SmartRestartXray();
            }
        }
    }

    // ─── System DNS SAVE button 
    internal void btnSysDnsSave_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var txtPrimary   = this.FindControl<global::Avalonia.Controls.TextBox>("txtSysDnsPrimary");
        var txtSecondary = this.FindControl<global::Avalonia.Controls.TextBox>("txtSysDnsSecondary");
        var tog          = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togSysDns");

        var primary   = txtPrimary?.Text?.Trim()   ?? "";
        var secondary = txtSecondary?.Text?.Trim() ?? "";

        if (!DnsService.IsValidIpv4(primary))
        {
            ShowToast(CrimsonX.Localization.AppStrings.ToastInvalidDnsPrimary);
            return;
        }
        if (!string.IsNullOrWhiteSpace(secondary) && !DnsService.IsValidIpv4(secondary))
        {
            ShowToast(CrimsonX.Localization.AppStrings.ToastInvalidDnsSecondary);
            return;
        }

        _cfg.SystemDnsPrimary   = primary;
        _cfg.SystemDnsSecondary = secondary;
        _cfg.EnableSystemDns    = true;

        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            if (tog != null) tog.IsChecked = true;
        });

        RequestConfigSave();
        if (_state.IsEngineRunning)
            ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectDns);
    }

    // ─── System DNS toggle 
    internal void togSysDns_IsCheckedChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isInitializingSettings) return;
        var tog = sender as global::Avalonia.Controls.ToggleSwitch;
        if (tog == null) return;

        if (tog.IsChecked == true)
        {
            var txtPrimary   = this.FindControl<global::Avalonia.Controls.TextBox>("txtSysDnsPrimary");
            var txtSecondary = this.FindControl<global::Avalonia.Controls.TextBox>("txtSysDnsSecondary");
            var livePrimary   = txtPrimary?.Text?.Trim()   ?? "";
            var liveSecondary = txtSecondary?.Text?.Trim() ?? "";

            if (!string.IsNullOrWhiteSpace(livePrimary))
            {
                if (!DnsService.IsValidIpv4(livePrimary))
                {
                    ShowToast(CrimsonX.Localization.AppStrings.ToastInvalidDnsPrimary);
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { tog.IsChecked = false; });
                    return;
                }
                if (!string.IsNullOrWhiteSpace(liveSecondary) && !DnsService.IsValidIpv4(liveSecondary))
                {
                    ShowToast(CrimsonX.Localization.AppStrings.ToastInvalidDnsSecondary);
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { tog.IsChecked = false; });
                    return;
                }
                _cfg.SystemDnsPrimary   = livePrimary;
                _cfg.SystemDnsSecondary = liveSecondary;
            }

            if (string.IsNullOrWhiteSpace(_cfg.SystemDnsPrimary))
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { tog.IsChecked = false; });
                return;
            }

            _cfg.EnableSystemDns = true;
            RequestConfigSave();
            if (_state.IsEngineRunning)
                ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectDns);
        }
        else
        {
            if (_cfg.EnableSystemDns)
            {
                _cfg.EnableSystemDns = false;
                RequestConfigSave();
                if (_state.IsEngineRunning)
                    ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectDns);
            }
        }
    }

    // ─── Allow LAN expandable panel 

    // ── LAN Connections & Authentication ──

    internal void btnLanToggle_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var src = e.Source as global::Avalonia.Controls.Control;
        while (src != null)
        {
            if (src.Name == "btnLanTog" || src.Name == "togLanAuth") return;
            src = src.Parent as global::Avalonia.Controls.Control;
        }

        var panToggle = this.FindControl<global::Avalonia.Controls.Border>("panLanToggle");
        var btnToggle = this.FindControl<global::Avalonia.Controls.Button>("btnLanToggle");
        var pan       = this.FindControl<global::Avalonia.Controls.Border>("panLanSettings");
        var ico       = this.FindControl<global::Avalonia.Controls.PathIcon>("icoLanExpander");

        if (pan == null || ico == null) return;

        if (pan.MaxHeight == 0)
        {
            var txtUser = this.FindControl<global::Avalonia.Controls.TextBox>("txtLanUser");
            var txtPass = this.FindControl<global::Avalonia.Controls.TextBox>("txtLanPass");
            var tog     = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togLanAuth");
            if (txtUser != null) txtUser.Text = _cfg.LanAuthUsername;
            if (txtPass != null) txtPass.Text = _cfg.LanAuthPassword;
            if (tog     != null) tog.IsChecked = _cfg.EnableLanAuth;

            pan.MaxHeight = 160;
            pan.Opacity   = 1;
            ico.RenderTransform = new global::Avalonia.Media.RotateTransform(180);
            if (panToggle != null) panToggle.CornerRadius = new global::Avalonia.CornerRadius(8, 8, 0, 0);
            if (btnToggle != null) btnToggle.CornerRadius = new global::Avalonia.CornerRadius(8, 8, 0, 0);
        }
        else
        {
            pan.MaxHeight = 0;
            pan.Opacity   = 0;
            ico.RenderTransform = new global::Avalonia.Media.RotateTransform(0);
            if (panToggle != null) panToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
            if (btnToggle != null) btnToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
        }
    }

    // ─── LAN auth toggle 
    internal void togLanAuth_IsCheckedChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isInitializingSettings) return;
        var tog = sender as global::Avalonia.Controls.ToggleSwitch;
        if (tog == null) return;

        if (tog.IsChecked == true)
        {
            var txtUser = this.FindControl<global::Avalonia.Controls.TextBox>("txtLanUser");
            var txtPass = this.FindControl<global::Avalonia.Controls.TextBox>("txtLanPass");
            var liveUser = txtUser?.Text?.Trim() ?? "";
            var livePass = txtPass?.Text?.Trim() ?? "";

            if (!string.IsNullOrWhiteSpace(liveUser))
            {
                _cfg.LanAuthUsername = liveUser;
                _cfg.LanAuthPassword = livePass;
            }

            if (string.IsNullOrWhiteSpace(_cfg.LanAuthUsername))
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { tog.IsChecked = false; });
                return;
            }

            _cfg.EnableLanAuth = true;
            RequestConfigSave();

            if (_state.IsEngineRunning)
            {
                if (_pollMode == "VPN Mode")
                    ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
                else
                    SmartRestartXray();
            }
        }
        else
        {
            if (_cfg.EnableLanAuth)
            {
                _cfg.EnableLanAuth = false;
                RequestConfigSave();

                if (_state.IsEngineRunning)
                {
                    if (_pollMode == "VPN Mode")
                        ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
                    else
                        SmartRestartXray();
                }
            }
        }
    }

    // ─── LAN auth SAVE button 
    internal void btnLanAuthSave_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var txtUser = this.FindControl<global::Avalonia.Controls.TextBox>("txtLanUser");
        var txtPass = this.FindControl<global::Avalonia.Controls.TextBox>("txtLanPass");
        var tog     = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togLanAuth");

        var user = txtUser?.Text?.Trim() ?? "";
        var pass = txtPass?.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(user))
        {
            ShowToast(CrimsonX.Localization.AppStrings.ToastUsernameEmpty);
            return;
        }

        _cfg.LanAuthUsername = user;
        _cfg.LanAuthPassword = pass;
        _cfg.EnableLanAuth   = true;

        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            if (tog != null) tog.IsChecked = true;
        });

        RequestConfigSave();

        if (_state.IsEngineRunning)
        {
            if (_pollMode == "VPN Mode")
                ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
            else
                SmartRestartXray();
        }
        else
        {
            ShowToast(CrimsonX.Localization.AppStrings.ToastCredentialsSaved, success: true);
        }
    }

    // ─── LAN password show/hide eye 
    internal bool _lanPassVisible = false;
    internal void btnLanPassEye_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var txtPass = this.FindControl<global::Avalonia.Controls.TextBox>("txtLanPass");
        var ico     = this.FindControl<global::Avalonia.Controls.PathIcon>("icoLanPassEye");
        if (txtPass == null) return;

        _lanPassVisible = !_lanPassVisible;
        txtPass.PasswordChar = _lanPassVisible ? '\0' : '\u2022';

        if (ico != null)
            ico.Data = _lanPassVisible
                ? global::Avalonia.Media.Geometry.Parse("M12 7c2.76 0 5 2.24 5 5 0 .65-.13 1.26-.36 1.83l2.92 2.92c1.51-1.26 2.7-2.89 3.43-4.75-1.73-4.39-6-7.5-11-7.5-1.4 0-2.74.25-3.98.7l2.16 2.16C10.74 7.13 11.35 7 12 7zM2 4.27l2.28 2.28.46.46C3.08 8.3 1.78 10.02 1 12c1.73 4.39 6 7.5 11 7.5 1.55 0 3.03-.3 4.38-.84l.42.42L19.73 22 21 20.73 3.27 3 2 4.27zM7.53 9.8l1.55 1.55c-.05.21-.08.43-.08.65 0 1.66 1.34 3 3 3 .22 0 .44-.03.65-.08l1.55 1.55c-.67.33-1.41.53-2.2.53-2.76 0-5-2.24-5-5 0-.79.2-1.53.53-2.2zm4.31-.78l3.15 3.15.02-.16c0-1.66-1.34-3-3-3l-.17.01z")
                : global::Avalonia.Media.Geometry.Parse("M12 4.5C7 4.5 2.73 7.61 1 12c1.73 4.39 6 7.5 11 7.5s9.27-3.11 11-7.5c-1.73-4.39-6-7.5-11-7.5zM12 17c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5zm0-8c-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3-1.34-3-3-3z");
    }

    // ─── Apply system DNS at connect time 

    // ── System DNS Apply / Restore ──

    private async Task ApplySystemDnsAsync()
    {
        if (!_cfg.EnableSystemDns) return;
        if (string.IsNullOrWhiteSpace(_cfg.SystemDnsPrimary)) return;

        await Task.Run(() =>
        {
            try
            {
                System.Net.NetworkInformation.NetworkInterface? nic = null;
                if (_cfg.EnableAdapterBinding && !string.IsNullOrWhiteSpace(_cfg.SelectedAdapterName))
                {
                    nic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                        .FirstOrDefault(a => a.Name == _cfg.SelectedAdapterName && a.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up);
                }
                
                if (nic == null)
                {
                    nic = DnsService.GetMainPhysicalAdapter();
                }

                if (nic == null)
                {
                    CrimsonX.Services.SimpleLogger.Log("[DnsService] No valid adapter found for DNS.");
                    return;
                }
                _savedDnsAdapterName = nic.Name;
                _savedDnsServers     = DnsService.GetCurrentDns(nic);

                DnsService.SetDns(nic.Name, _cfg.SystemDnsPrimary, _cfg.SystemDnsSecondary);
                CrimsonX.Services.SimpleLogger.Log($"[DnsService] Applied DNS {_cfg.SystemDnsPrimary}/{_cfg.SystemDnsSecondary} to {nic.Name}");
            }
            catch (Exception ex)
            {
                CrimsonX.Services.SimpleLogger.Log(ex);
            }
        });
    }

    // ─── Restore system DNS at disconnect / app close 

    private async Task RestoreSystemDnsAsync()
    {
        if (_savedDnsAdapterName == null) return;

        await Task.Run(() =>
        {
            try
            {
                DnsService.RestoreDns(_savedDnsAdapterName, _savedDnsServers ?? Array.Empty<string>());
                CrimsonX.Services.SimpleLogger.Log($"[DnsService] Restored DNS on {_savedDnsAdapterName}");
            }
            catch (Exception ex)
            {
                CrimsonX.Services.SimpleLogger.Log(ex);
            }
            finally
            {
                _savedDnsAdapterName = null;
                _savedDnsServers     = null;
            }
        });
    }
}







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
                string displayIp = (_cfg.EnableAdapterBinding && !string.IsNullOrWhiteSpace(_cfg.SelectedAdapterIp)) ? _cfg.SelectedAdapterIp : (_state.LanIp ?? "Unknown");
                    lblLanIp.Text = displayIp + ":10919";
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

            var carousel = this.FindControl<global::Avalonia.Controls.Carousel>("MainCarousel");
            bool onAppsGames = carousel != null && carousel.SelectedIndex == 5;
            const double toastBottom = 25, appsGamesLift = 45;
            toast.Margin = new global::Avalonia.Thickness(0, 0, 0, onAppsGames ? toastBottom + appsGamesLift : toastBottom);

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

    internal void OnEngineCountChanged()
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
        lock (_pipelineCtsLock) { try { _pipelineCts?.Cancel(); } catch { } }
        _ = Task.Run(() => CrimsonX.Services.XrayPipelineManager.StopXray());

        _state.AbortBoot       = true;
        _state.IsEngineRunning = false;
        



        lock (_staggerQueue) { _staggerQueue.Clear(); }
        _session.Stop(); 

        
        _netDiag.StopStatsPolling();
        _netDiag.StopGeoTrace();

        _logTimer?.Stop();
        _logClearTimer?.Stop();
        ProxyService.SetSystemProxy(false);
        _ = CrimsonX.Services.SystemDnsService.RestoreAsync();

        
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
        try
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
}

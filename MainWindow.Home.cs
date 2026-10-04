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
    private global::Avalonia.Controls.TextBlock? _lblTimerCache;

    // Session Clock

    private void StartSessionClock()
    {
        var panTimerContent = this.FindControl<StackPanel>("panTimerContent");
        if (panTimerContent != null) panTimerContent.IsVisible = true;
        var lblDisconnected = this.FindControl<TextBlock>("lblDisconnected");
        if (lblDisconnected != null) lblDisconnected.IsVisible = false;
        _session.Start();
    }

    private global::Avalonia.Threading.DispatcherTimer? _logClearTimer;
    private int _isReadingLogs = 0; 

    private const int LogReadCapBytes = 64 * 1024;

    private global::Avalonia.Threading.DispatcherTimer? _uiStallTimer;
    private DateTime _uiStallLast = DateTime.UtcNow;

    private void StartUiStallWatch()
    {
        if (_uiStallTimer != null) return;
        _uiStallLast = DateTime.UtcNow;
        _uiStallTimer = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _uiStallTimer.Tick += (s, e) =>
        {
            var now = DateTime.UtcNow;
            int gap = (int)(now - _uiStallLast).TotalMilliseconds;
            _uiStallLast = now;
            if (gap < 400) return;
            if (!(MainWindow.Instance?.Config?.DebugMode ?? false)) return;
            string busy = CrimsonX.Services.UiBusy.Current;
            CrimsonX.Services.SimpleLogger.Log($"[UI] the interface thread was blocked for {gap} ms while: {(busy.Length > 0 ? busy : "nothing claimed")} (gc {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}, working set {Environment.WorkingSet / (1024 * 1024)} MB)");
        };
        _uiStallTimer.Start();
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

    // Network diagnostics event subscriptions 

    // Geo Ping & Network Diagnostics

    internal void InitNetDiag()
    {
        _netDiag.GeoTraceCompleted += OnGeoTraceCompleted;
        _netDiag.StatsUpdated      += OnStatsUpdated;
        CrimsonX.Services.UiEventBus.Instance.ToastRequested += evt =>
            Dispatcher.UIThread.Post(() => ShowToast(evt.Message, evt.Success ? ToastKind.Success : ToastKind.Error));
        CrimsonX.Services.UiEventBus.Instance.ConnectionProgress += percent =>
        {
            _statusTargetPercent = percent;
            UpdateStatusText();
        };
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
        var lblPublicIp = this.FindControl<TextBlock>("lblPublicIp");
        if (lblPublicIp != null) lblPublicIp.Text = CrimsonX.Localization.AppStrings.GeoTracing;
        _exitIpFull = "";
        SetPublicIpTip("");
        _netDiag.StartGeoTrace();
    }

    private string _exitIpFull = "";

    private void SetPublicIpTip(string fullIp)
    {
        var tile = this.FindControl<global::Avalonia.Controls.Button>("btnCopyPublicIp");
        if (tile == null) return;
        if (fullIp.Length == 0) tile.ClearValue(global::Avalonia.Controls.ToolTip.TipProperty);
        else                    global::Avalonia.Controls.ToolTip.SetTip(tile, fullIp);
    }

    private void OnGeoTraceCompleted(CrimsonX.Services.GeoTraceResult result)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _state.IsGeoTracing = false;
            if (!_state.IsConnected) return;
            var lblCountry = this.FindControl<TextBlock>("lblCountryName");
            var lblPing    = this.FindControl<TextBlock>("lblPing");
            var lblPublicIp = this.FindControl<TextBlock>("lblPublicIp");
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
            string ipText = string.IsNullOrWhiteSpace(result.Ip)
                ? (result.PingMs == 0
                    ? CrimsonX.Localization.AppStrings.GeoTimeout
                    : CrimsonX.Localization.AppStrings.StatusDisconnected)
                : result.Ip;
            _exitIpFull = System.Net.IPAddress.TryParse(ipText, out _) ? ipText : "";
            if (lblPublicIp != null) lblPublicIp.Text = CrimsonX.Services.IpDisplay.ForTile(ipText);
            SetPublicIpTip(_exitIpFull);
        });
    }

    // Status Readout

    private global::Avalonia.Controls.TextBlock? _statusValue;
    private global::Avalonia.Threading.DispatcherTimer? _statusTimer;
    private int _statusTargetPercent = -1;
    private double _statusShownPercent = -1;
    private CrimsonX.Services.StatusKind _statusPhase = CrimsonX.Services.StatusKind.Idle;

    private void UpdateStatusText()
    {
        _statusValue ??= this.FindControl<global::Avalonia.Controls.TextBlock>("lblStatusValue");
        if (_statusValue == null) return;
        var phase = CrimsonX.Services.ConnectPhaseUi.Status(_state.IsConnected, _state.IsEngineRunning, _state.IsReconnecting);
        if (phase != _statusPhase)
        {
            _statusPhase = phase;
            if (phase == CrimsonX.Services.StatusKind.ComingUp)
            {
                _statusShownPercent = 0;
                _statusValue.Text = "0%";
            }
        }
        switch (phase)
        {
            case CrimsonX.Services.StatusKind.ComingUp:
                StartStatusTimer();
                break;
            case CrimsonX.Services.StatusKind.Connected:
                StopStatusTimer();
                _statusValue.Text = CrimsonX.Localization.AppStrings.StatusConnectedWord;
                break;
            default:
                StopStatusTimer();
                _statusValue.Text = CrimsonX.Localization.AppStrings.StatusOffline;
                break;
        }
    }

    private void StartStatusTimer()
    {
        if (_statusTimer == null)
        {
            _statusTimer = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _statusTimer.Tick += (s, e) => TickStatusText();
        }
        if (!_statusTimer.IsEnabled) _statusTimer.Start();
    }

    private void StopStatusTimer()
    {
        if (_statusTimer != null && _statusTimer.IsEnabled) _statusTimer.Stop();
    }

    private void TickStatusText()
    {
        if (_statusValue == null) return;
        double target = System.Math.Clamp(_statusTargetPercent, 0, 100);
        _statusShownPercent = CrimsonX.Services.ConnectPhaseUi.Approach(_statusShownPercent, target);
        _statusValue.Text = ((int)System.Math.Round(_statusShownPercent)) + "%";
        if (_statusPhase != CrimsonX.Services.StatusKind.ComingUp || System.Math.Abs(_statusShownPercent - target) < 0.5)
            StopStatusTimer();
    }

    // Stats Polling & Live Graph

    private void StartStatsPolling()
    {
        UpdateLanPortUI();
        _logClearTimer?.Stop();
        _logClearTimer?.Start();
        StartUiStallWatch();
        _netDiag.StartStatsPolling(() => _state.IsConnected);
    }

    private void OnStatsUpdated(CrimsonX.Services.StatsSnapshot snap)
    {
        _state.SessionDataBytes += snap.DiffUpBytes + snap.DiffDnBytes;
        string tot = _state.SessionDataBytes >= 1_073_741_824
            ? $"{Math.Round(_state.SessionDataBytes / 1_073_741_824.0, 2)} GB"
            : _state.SessionDataBytes >= 1_048_576
                ? $"{(long)(_state.SessionDataBytes / 1_048_576.0)} MB"
                : $"{(long)(_state.SessionDataBytes / 1024.0)} KB";
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
    private global::Avalonia.Threading.DispatcherTimer? _sparkHideTimer;
    private DateTime _graphAnimStartTime;
    private double _graphAnimStep;
    private global::Avalonia.Media.TranslateTransform? _graphUpTransform;
    private global::Avalonia.Media.TranslateTransform? _graphDnTransform;

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
        const double width         = 51;
        const double height        = 30;
        const double topPadding    = 3;
        const double bottomPadding = 2;
        int count = Math.Min(upHistory.Length, dnHistory.Length);
        if (count < 2) return;
        double step   = width / (NetworkDiagnosticsService.HistorySamples - 1);
        double maxUp  = upHistory.Length > 0 ? upHistory.Max() : 0;
        double maxDn  = dnHistory.Length > 0 ? dnHistory.Max() : 0;
        if (maxUp < 1024) maxUp = 1024;
        if (maxDn < 1024) maxDn = 1024;
        _ptsUpCache.Clear();
        _ptsDnCache.Clear();
        int    startIdx    = NetworkDiagnosticsService.HistorySamples - count;
        double drawHeight  = height - topPadding - bottomPadding;
        for (int i = 0; i < count; i++)
        {
            double x   = (startIdx + i) * step;
            double yUp = (height - bottomPadding) - (upHistory[i] / maxUp * drawHeight);
            double yDn = (height - bottomPadding) - (dnHistory[i] / maxDn * drawHeight);
            _ptsUpCache.Add(new global::Avalonia.Point(x, yUp));
            _ptsDnCache.Add(new global::Avalonia.Point(x, yDn));
        }
        graphUpload.Data       = GenerateSmoothSpline(_ptsUpCache, false, width, height);
        graphDownload.Data     = GenerateSmoothSpline(_ptsDnCache, false, width, height);
        graphUploadFill.Data   = GenerateSmoothSpline(_ptsUpCache, true,  width, height);
        graphDownloadFill.Data = GenerateSmoothSpline(_ptsDnCache, true,  width, height);
        _graphUpTransform = (graphUpload.Parent   as global::Avalonia.Controls.Canvas)?.RenderTransform as global::Avalonia.Media.TranslateTransform;
        _graphDnTransform = (graphDownload.Parent as global::Avalonia.Controls.Canvas)?.RenderTransform as global::Avalonia.Media.TranslateTransform;
        if (_graphUpTransform != null || _graphDnTransform != null)
        {
            if (_graphAnimTimer == null)
            {
                _graphAnimTimer = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
                _graphAnimTimer.Tick += (s, e) =>
                {
                    double elapsed = (DateTime.UtcNow - _graphAnimStartTime).TotalMilliseconds;
                    bool   finished = elapsed >= 1000;
                    double x        = finished ? -_graphAnimStep : -_graphAnimStep * (elapsed / 1000.0);
                    if (_graphUpTransform != null) _graphUpTransform.X = x;
                    if (_graphDnTransform != null) _graphDnTransform.X = x;
                    if (finished) _graphAnimTimer.Stop();
                };
            }
            _graphAnimStartTime = DateTime.UtcNow;
            _graphAnimStep      = step;
            if (_graphUpTransform != null) _graphUpTransform.X = 0;
            if (_graphDnTransform != null) _graphDnTransform.X = 0;
            _graphAnimTimer.Start();
        }
    }

    internal void SetSparklinesVisible(bool show)
    {
        var dn = this.FindControl<global::Avalonia.Controls.Button>("cellSparkDn");
        var up = this.FindControl<global::Avalonia.Controls.Button>("cellSparkUp");
        if (dn == null || up == null) return;
        _sparkHideTimer?.Stop();
        if (show)
        {
            dn.IsVisible = true;
            up.IsVisible = true;
            double width = this.FindControl<global::Avalonia.Controls.Canvas>("graphDnCanvas")?.Width ?? 51;
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                dn.Width = width;
                up.Width = width;
            });
            return;
        }
        dn.Width = 0;
        up.Width = 0;
        _sparkHideTimer ??= new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
        _sparkHideTimer.Tick -= OnSparkHideTick;
        _sparkHideTimer.Tick += OnSparkHideTick;
        _sparkHideTimer.Start();
    }

    private void OnSparkHideTick(object? sender, EventArgs e)
    {
        _sparkHideTimer?.Stop();
        if (_state.IsConnected) return;
        var dn = this.FindControl<global::Avalonia.Controls.Button>("cellSparkDn");
        var up = this.FindControl<global::Avalonia.Controls.Button>("cellSparkUp");
        if (dn != null) dn.IsVisible = false;
        if (up != null) up.IsVisible = false;
    }

    // ── The two readout tiles an invisible ghost holds open ──

    private readonly System.Collections.Generic.Dictionary<string, double> _readoutGhostWidth = new(StringComparer.Ordinal);

    internal void SetReadoutTilesGrown(bool grown)
    {
        using var _busy = CrimsonX.Services.UiBusy.Scope("readout fold");
        GrowReadoutGhost("lblStatusGhost", grown);
        GrowReadoutGhost("lblUploadGhost", grown);
    }

    private void GrowReadoutGhost(string name, bool grown)
    {
        var ghost = this.FindControl<global::Avalonia.Controls.TextBlock>(name);
        if (ghost == null) return;
        if (!grown)
        {
            ghost.Width = 0;
            return;
        }
        if (!_readoutGhostWidth.TryGetValue(name, out double width))
        {
            ghost.Width = double.NaN;
            ghost.Measure(new global::Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));
            width = ghost.DesiredSize.Width;
            if (width <= 0) return;
            _readoutGhostWidth[name] = width;
        }
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => ghost.Width = width);
    }

    // ── The readout bar's pin ──

    private const string ReadoutHostTabs   = "panReadoutHostTabs";

    private bool _readoutResizeHooked;

    internal void ApplyTopBarPin(string viewName)
    {
        using var _busy = CrimsonX.Services.UiBusy.Scope("readout bar pin");
        var bar = this.FindControl<global::Avalonia.Controls.StackPanel>("panReadouts");
        if (bar == null) return;
        HookReadoutResize(bar);
        bool pinned = _cfg?.PinTopBar ?? false;
        string host = pinned && viewName is "SplitTunneling" or "Themes" or "UdpScanner" or "About"
            ? ReadoutHostTabs
            : "";
        MoveReadoutBar(bar, host.Length > 0 ? ReadoutHost(host) : null);
        ApplyAboutReadoutInset(pinned && viewName == "About");
        ApplySplitReadoutInset(pinned && viewName == "SplitTunneling");
        ApplyUdpReadoutInset(pinned && viewName == "UdpScanner");
        PaintTopBarPin();
    }

    private void MoveReadoutBar(global::Avalonia.Controls.Control bar, global::Avalonia.Controls.Panel? target)
    {
        target ??= this.FindControl<global::Avalonia.Controls.Grid>("viewHome");
        if (target == null || ReferenceEquals(bar.Parent, target)) return;
        if (bar.Parent is global::Avalonia.Controls.Panel parent) parent.Children.Remove(bar);
        global::Avalonia.Controls.Grid.SetRow(bar, 0);
        target.Children.Add(bar);
    }

    private global::Avalonia.Controls.Panel? ReadoutHost(string name)
        => this.FindControl<global::Avalonia.Controls.Panel>(name);

    internal double ReadoutBarHeight()
    {
        var bar = this.FindControl<global::Avalonia.Controls.StackPanel>("panReadouts");
        if (bar == null) return 0;
        double height = bar.Bounds.Height > 0 ? bar.Bounds.Height : bar.DesiredSize.Height;
        return height > 0 ? height + bar.Margin.Top : 0;
    }

    private void ApplyAboutReadoutInset(bool on)
    {
        var page = this.FindControl<CrimsonX.Pages.AboutPage>("pageAbout");
        page?.SetReadoutInset(on ? ReadoutBarHeight() : 0);
    }

    private void ApplySplitReadoutInset(bool on)
    {
        CrimsonX.Pages.SplitTunnelPage.Instance?.SetReadoutInset(on ? ReadoutBarHeight() : 0);
    }

    private void ApplyUdpReadoutInset(bool on)
    {
        this.FindControl<CrimsonX.Pages.UdpScannerPage>("pageUdpScanner")?.SetReadoutInset(on ? ReadoutBarHeight() : 0);
    }

    private void ReapplyReadoutInset()
    {
        bool pinned = _cfg?.PinTopBar ?? false;
        if (_previousNav == "About") ApplyAboutReadoutInset(pinned);
        if (_previousNav == "SplitTunneling") ApplySplitReadoutInset(pinned);
        if (_previousNav == "UdpScanner") ApplyUdpReadoutInset(pinned);
    }

    private void PaintTopBarPin()
    {
        bool pinned = _cfg?.PinTopBar ?? false;
        var on  = this.FindControl<global::Avalonia.Controls.PathIcon>("icoPinTopBarOn");
        var off = this.FindControl<global::Avalonia.Controls.PathIcon>("icoPinTopBarOff");
        if (on != null) on.IsVisible = pinned;
        if (off != null) off.IsVisible = !pinned;
    }

    private void HookReadoutResize(global::Avalonia.Controls.StackPanel bar)
    {
        if (_readoutResizeHooked) return;
        bar.SizeChanged += (_, _) => ReapplyReadoutInset();
        bar.AttachedToVisualTree += (_, _) => ReapplyReadoutInset();
        _readoutResizeHooked = true;
    }

    private void PinTopBar_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_cfg == null) return;
        _cfg.PinTopBar = !_cfg.PinTopBar;
        RequestConfigSave();
        ApplyTopBarPin(_previousNav);
    }

    // ── The four stat tiles resize instead of snapping ──

    private bool _statResizeHooked;

    private sealed class StatTile
    {
        public StatTile(global::Avalonia.Controls.Button tile, global::Avalonia.Controls.TextBlock measurer)
        {
            Tile = tile;
            Measurer = measurer;
        }

        public global::Avalonia.Controls.Button Tile { get; }

        public global::Avalonia.Controls.TextBlock Measurer { get; }

        public System.Collections.Generic.List<global::Avalonia.Controls.TextBlock> Labels { get; } = new();

        public double LastTarget { get; set; } = double.NaN;
    }

    private readonly System.Collections.Generic.List<StatTile> _statTiles = new();

    private void HookStatTileResize()
    {
        if (!_statResizeHooked)
        {
            _statResizeHooked = true;
            HookStatTile("panStatLocation", "lblLocationLabel", "lblCountryName");
            HookStatTile("panStatIp", "lblPublicIpLabel", "lblPublicIp");
            HookStatTile("panStatLocalPort", "lblLocalPortLabel", "lblLocalIp");
            HookStatTile("panStatLanPort", "lblLanPortLabel", "lblLanIp");
        }
        RefreshStatWidths();
    }

    private void HookStatTile(string boxName, params string[] labelNames)
    {
        var box = this.FindControl<global::Avalonia.Controls.Border>(boxName);
        if (box?.Child is not global::Avalonia.Controls.Button tile) return;
        var entry = new StatTile(tile, new global::Avalonia.Controls.TextBlock
        {
            TextWrapping = global::Avalonia.Media.TextWrapping.NoWrap,
            TextTrimming = global::Avalonia.Media.TextTrimming.None
        });
        foreach (string name in labelNames)
        {
            var text = this.FindControl<global::Avalonia.Controls.TextBlock>(name);
            if (text == null) continue;
            entry.Labels.Add(text);
            text.PropertyChanged += (_, e) =>
            {
                if (e.Property != global::Avalonia.Controls.TextBlock.TextProperty) return;
                SmoothStatWidth(entry);
            };
        }
        if (entry.Labels.Count > 0) _statTiles.Add(entry);
    }

    private void RefreshStatWidths()
    {
        using var _busy = CrimsonX.Services.UiBusy.Scope("stat tiles resize");
        foreach (var tile in _statTiles) SmoothStatWidth(tile);
    }

    private static void SmoothStatWidth(StatTile tile)
    {
        double widest = 0;
        foreach (var label in tile.Labels)
        {
            var measurer = tile.Measurer;
            measurer.FontFamily = label.FontFamily;
            measurer.FontSize = label.FontSize;
            measurer.FontWeight = label.FontWeight;
            measurer.FontStyle = label.FontStyle;
            measurer.Text = label.Text;
            measurer.InvalidateMeasure();
            measurer.Measure(new global::Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));
            if (measurer.DesiredSize.Width > widest) widest = measurer.DesiredSize.Width;
        }
        double target = Math.Ceiling(widest) + 1
                      + tile.Tile.Padding.Left + tile.Tile.Padding.Right
                      + tile.Tile.BorderThickness.Left + tile.Tile.BorderThickness.Right;
        if (target <= 1 || Math.Abs(tile.LastTarget - target) < 0.5) return;
        tile.LastTarget = target;
        if (double.IsNaN(tile.Tile.Width))
        {
            var transitions = tile.Tile.Transitions;
            tile.Tile.Transitions = null;
            tile.Tile.Width = target;
            tile.Tile.Transitions = transitions;
            return;
        }
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => tile.Tile.Width = target);
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
}

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


    private global::Avalonia.Threading.DispatcherTimer? _logTimer;
    private global::Avalonia.Threading.DispatcherTimer? _logClearTimer;
    private long _lastXrayLogPos = 0;
    private int _isReadingLogs = 0; 
    private readonly System.Collections.Generic.List<string> _xrayLogLines = new();

    // Stats & Logs Mini-Panels 

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

                        if (fs.Length < _lastXrayLogPos)
                            _lastXrayLogPos = 0; 

                        fs.Seek(_lastXrayLogPos, SeekOrigin.Begin);

                        var newRawLines = new List<string>();
                        using (var sr = new StreamReader(fs, System.Text.Encoding.UTF8, true, 1 << 16, leaveOpen: true))
                        {
                            string? line;
                            while ((line = sr.ReadLine()) != null)
                            {
                                if (!line.Contains(":10999"))
                                    newRawLines.Add(line);
                            }
                            _lastXrayLogPos = fs.Position;
                        }

                        if (newRawLines.Count > 0)
                        {
                            _xrayLogLines.AddRange(newRawLines);
                            if (_xrayLogLines.Count > 300)
                                _xrayLogLines.RemoveRange(0, _xrayLogLines.Count - 300);
                        }

                        var last15 = _xrayLogLines.Skip(Math.Max(0, _xrayLogLines.Count - 15)).ToArray();

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


    // Network diagnostics event subscriptions 

    // Geo Ping & Network Diagnostics

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

    // Stats Polling & Live Graph

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
}

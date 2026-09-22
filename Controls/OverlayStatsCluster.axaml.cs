/*
  CrimsonX - A GUI VPN client that fetches, tests and load-balances multiple xray configs suited for your network.
  Copyright (C) 2026 RichTiTAN

  This program is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CrimsonX.Localization;
using CrimsonX.Services;

namespace CrimsonX.Controls
{
    public partial class OverlayStatsCluster : UserControl
    {
        private const string NoValue = "—";

        private const double CompactWidth = 330;

        private static readonly IBrush DotIdle       = new SolidColorBrush(Color.Parse("#4A5568"));
        private static readonly IBrush DotConnecting = new SolidColorBrush(Color.Parse("#D69E2E"));
        private static readonly IBrush DotConnected  = new SolidColorBrush(Color.Parse("#68D391"));

        private static readonly IBrush ValueReady = new SolidColorBrush(Color.Parse("#E2E8F0"));
        private static readonly IBrush ValueBlank = new SolidColorBrush(Color.Parse("#4A5568"));
        private static readonly FontFamily PersianFont = new FontFamily("Segoe UI");

        private static readonly IBrush TipName  = new SolidColorBrush(Color.Parse("#E2E8F0"));
        private static readonly IBrush TipValue = new SolidColorBrush(Color.Parse("#A0AEC0"));
        private static readonly IBrush TipNote  = new SolidColorBrush(Color.Parse("#718096"));
        private static readonly IBrush TipRule  = new SolidColorBrush(Color.Parse("#1FFFFFFF"));

        private const int TipMaxRows = 6;

        private readonly TextBlock _txtState = null!;
        private readonly TextBlock _txtIdle = null!;
        private readonly TextBlock _txtSep1 = null!;
        private readonly TextBlock _txtActive = null!;
        private readonly TextBlock _txtActiveWord = null!;
        private readonly TextBlock _txtTimer = null!;
        private readonly TextBlock _txtDown = null!;
        private readonly TextBlock _txtUp = null!;
        private readonly StackPanel _panIdle = null!;
        private readonly StackPanel _panDown = null!;
        private readonly StackPanel _panUp = null!;
        private readonly Ellipse _dotState = null!;
        private readonly Ellipse _dotIdle = null!;
        private readonly Border _chipMode = null!;
        private readonly TextBlock _txtMode = null!;

        private readonly Border _hotCount = null!;
        private readonly Border _hotSpeed = null!;
        private readonly TipBox _countTip = new();
        private readonly TipBox _speedTip = new();

        private bool _hoverLive;

        private DispatcherTimer? _timer;
        private SingboxConnectionsClient? _client;
        private CancellationTokenSource? _cts;

        private bool _tickBusy;
        private bool _compact;
        private bool _persian;

        private int _visualState;
        private bool _vpnMode;
        private bool _hasTelemetry;
        private string _tipText = "";

        private int _activeConnections;
        private TimeSpan? _sessionElapsed;

        private HashSet<string>? _ruleNames;

        private List<(string Label, HashSet<string> Names)> _ruleApps = new();

        private DateTime _ruleNamesStamp = DateTime.MinValue;

        private readonly Dictionary<string, (long Dn, long Up)> _prevTotals = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (double Dn, double Up)> _procSpeed = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastSample = DateTime.MinValue;
        private double _speedDown;
        private double _speedUp;
        private IReadOnlyList<RuleProcessTraffic> _lastProcesses = Array.Empty<RuleProcessTraffic>();

        public OverlayStatsCluster()
        {
            InitializeComponent();

            _txtState      = this.FindControl<TextBlock>("txtState")!;
            _txtIdle       = this.FindControl<TextBlock>("txtIdle")!;
            _txtSep1       = this.FindControl<TextBlock>("txtSep1")!;
            _txtActive     = this.FindControl<TextBlock>("txtActive")!;
            _txtActiveWord = this.FindControl<TextBlock>("txtActiveWord")!;
            _txtTimer      = this.FindControl<TextBlock>("txtTimer")!;
            _txtDown       = this.FindControl<TextBlock>("txtDown")!;
            _txtUp         = this.FindControl<TextBlock>("txtUp")!;
            _panIdle       = this.FindControl<StackPanel>("panIdle")!;
            _panDown       = this.FindControl<StackPanel>("panDown")!;
            _panUp         = this.FindControl<StackPanel>("panUp")!;
            _dotState      = this.FindControl<Ellipse>("dotState")!;
            _dotIdle       = this.FindControl<Ellipse>("dotIdle")!;
            _chipMode      = this.FindControl<Border>("chipMode")!;
            _txtMode       = this.FindControl<TextBlock>("txtMode")!;
            _hotCount      = this.FindControl<Border>("hotCount")!;
            _hotSpeed      = this.FindControl<Border>("hotSpeed")!;

            ToolTip.SetTip(_hotCount, _countTip.Root);
            ToolTip.SetTip(_hotSpeed, _speedTip.Root);
            ToolTip.SetShowDelay(_hotCount, 250);
            ToolTip.SetShowDelay(_hotSpeed, 250);

            SizeChanged += (_, e) => ApplyDensity(e.NewSize.Width);
        }

        protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            _timer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick -= OnTick;
            _timer.Tick += OnTick;
            _timer.Start();

            OnTick(null, EventArgs.Empty);
        }

        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnTick;
            }

            try { _cts?.Cancel(); _cts?.Dispose(); } catch { }
            _cts = null;

            try { _client?.Dispose(); } catch (Exception ex) { SimpleLogger.Log(ex); }
            _client = null;

            ResetTelemetry();
        }

        private async void OnTick(object? sender, EventArgs e)
        {
            if (_tickBusy) return;

            _tickBusy = true;
            try
            {
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
            finally
            {
                _tickBusy = false;
            }
        }

        // Refresh 

        private async Task RefreshAsync()
        {
            if (!IsEffectivelyVisible) return;

            var mw = MainWindow.Instance;
            if (mw == null) return;

            var state = mw.State;
            var cfg   = mw.Config;

            _visualState = state.IsConnected ? 2 : (state.IsEngineRunning ? 1 : 0);
            _vpnMode     = string.Equals(cfg.LastXrayMode, "VPN Mode", StringComparison.OrdinalIgnoreCase);
            _sessionElapsed = state.IsConnected && state.SessionStartTime.HasValue
                ? DateTime.Now - state.SessionStartTime.Value
                : null;

            bool wantTelemetry = state.IsConnected && _vpnMode && cfg.EnableAppRules;

            if (wantTelemetry)
            {
                var names = GetRuleNames();
                if (names.Count > 0)
                {
                    _client ??= new SingboxConnectionsClient();
                    _cts ??= new CancellationTokenSource();

                    var snapshot = await _client.GetAsync(names, _cts.Token);
                    if (snapshot != null && snapshot.Available)
                        ApplyTelemetry(snapshot);
                    else
                        ResetTelemetry();
                }
                else
                {
                    ResetTelemetry();
                }
            }
            else
            {
                ResetTelemetry();
            }

            Render();
        }

        private HashSet<string> GetRuleNames()
        {
            RefreshRules();
            return _ruleNames ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private List<(string Label, HashSet<string> Names)> GetRuleApps()
        {
            RefreshRules();
            return _ruleApps;
        }

        private void RefreshRules()
        {
            if (_ruleNames != null && (DateTime.UtcNow - _ruleNamesStamp).TotalSeconds < 5)
                return;

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var apps = new List<(string Label, HashSet<string> Names)>();
            try
            {
                foreach (var rule in AppRulesService.Load())
                {
                    if (rule == null || !rule.IsEnabled) continue;

                    var source = rule.ProcessNames != null && rule.ProcessNames.Count > 0
                        ? (IEnumerable<string>)rule.ProcessNames
                        : new[] { rule.ExeName ?? "" };

                    var names = new HashSet<string>(AppRulesSingboxBuilder.BuildProcessNames(source),
                                                    StringComparer.OrdinalIgnoreCase);
                    foreach (var name in names)
                        set.Add(name);

                    if (names.Count > 0)
                        apps.Add((AppLabel(string.IsNullOrWhiteSpace(rule.DisplayName) ? rule.ExeName : rule.DisplayName,
                                           rule.ProcessNames), names));
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }

            _ruleNames = set;
            _ruleApps = apps;
            _ruleNamesStamp = DateTime.UtcNow;
        }

        private static string AppLabel(string exeName, IReadOnlyList<string>? processNames)
        {
            string name = exeName ?? "";
            if (string.IsNullOrWhiteSpace(name) && processNames != null && processNames.Count > 0)
                name = processNames[0];

            name = name.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);

            return Capitalize(name);
        }

        private void ResetTelemetry()
        {
            _hasTelemetry = false;
            _activeConnections = 0;
            _speedDown = 0;
            _speedUp = 0;
            _lastSample = DateTime.MinValue;
            _prevTotals.Clear();
            _procSpeed.Clear();
            _lastProcesses = Array.Empty<RuleProcessTraffic>();
        }

        private void ApplyTelemetry(RuleConnectionsSnapshot snapshot)
        {
            var now = DateTime.UtcNow;
            double elapsed = _lastSample == DateTime.MinValue ? 0 : (now - _lastSample).TotalSeconds;

            double downPerSec = 0;
            double upPerSec   = 0;
            _procSpeed.Clear();

            if (elapsed >= 0.25)
            {
                foreach (var proc in snapshot.Processes)
                {
                    if (!_prevTotals.TryGetValue(proc.ProcessName, out var prev)) continue;

                    double dn = Math.Max(0, proc.DownloadBytes - prev.Dn) / elapsed;
                    double up = Math.Max(0, proc.UploadBytes - prev.Up) / elapsed;

                    downPerSec += dn;
                    upPerSec   += up;
                    _procSpeed[proc.ProcessName] = (dn, up);
                }
            }

            const double alpha = 0.45;
            _speedDown = _speedDown <= 0 ? downPerSec : _speedDown + (downPerSec - _speedDown) * alpha;
            _speedUp   = _speedUp   <= 0 ? upPerSec   : _speedUp   + (upPerSec   - _speedUp)   * alpha;

            _prevTotals.Clear();
            foreach (var proc in snapshot.Processes)
                _prevTotals[proc.ProcessName] = (proc.DownloadBytes, proc.UploadBytes);

            _lastSample = now;
            _activeConnections = snapshot.ActiveConnections;
            _lastProcesses = snapshot.Processes;
            _hasTelemetry = true;
        }

        // Rendering 

        private void Render()
        {
            ApplyDensity();
            ApplyStrings();
            ApplyDot();
            ApplyValues();
            ApplyModeChip();
            ApplyTooltip();
            ApplyAppTips();
        }

        private void ApplyStrings()
        {
            _persian = AppStrings.IsPersian;

            string stateText = _visualState switch
            {
                2 => AppStrings.StatusConnected,
                1 => AppStrings.StatusConnecting,
                _ => AppStrings.StatusNotConnected
            };

            _txtState.Text = stateText;
            _txtIdle.Text  = stateText;

            _txtActiveWord.Text = AppStrings.OverlayStatsActive;
            _txtMode.Text       = AppStrings.OverlayStatsProxyMode;

            var label = _persian ? PersianFont : FontFamily.Default;
            _txtState.FontFamily      = label;
            _txtIdle.FontFamily       = label;
            _txtActiveWord.FontFamily = label;
            _txtMode.FontFamily       = label;

            FlowDirection = _persian ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            _txtActive.FlowDirection = FlowDirection.LeftToRight;
            _txtTimer.FlowDirection  = FlowDirection.LeftToRight;
            _txtDown.FlowDirection   = FlowDirection.LeftToRight;
            _txtUp.FlowDirection     = FlowDirection.LeftToRight;
        }

        private void ApplyDot()
        {
            var fill = _visualState switch
            {
                2 => DotConnected,
                1 => DotConnecting,
                _ => DotIdle
            };

            _dotState.Fill = fill;
            _dotIdle.Fill  = fill;
        }

        private void ApplyValues()
        {
            if (_visualState != 2)
            {
                SetValue(_txtActive, NoValue, false);
                SetValue(_txtTimer, NoValue, false);
                SetValue(_txtDown, NoValue, false);
                SetValue(_txtUp, NoValue, false);
                return;
            }

            SetValue(_txtActive,
                _hasTelemetry ? _activeConnections.ToString(CultureInfo.InvariantCulture) : NoValue,
                _hasTelemetry);

            SetValue(_txtTimer, FormatDuration(_sessionElapsed ?? TimeSpan.Zero), _sessionElapsed.HasValue);

            SetValue(_txtDown, _hasTelemetry ? FormatSpeed(_speedDown, _compact) : NoValue, _hasTelemetry);
            SetValue(_txtUp,   _hasTelemetry ? FormatSpeed(_speedUp,   _compact) : NoValue, _hasTelemetry);
        }

        private static void SetValue(TextBlock block, string text, bool ready)
        {
            if (block.Text != text) block.Text = text;

            var brush = ready ? ValueReady : ValueBlank;
            if (!ReferenceEquals(block.Foreground, brush)) block.Foreground = brush;
        }

        private void ApplyDensity(double? width = null)
        {
            double available = width ?? Bounds.Width;
            _compact = available > 0 && available < CompactWidth;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            bool connected   = _visualState == 2;
            bool showNumbers = connected;

            bool showStateWord = !connected || !_compact;

            _panIdle.IsVisible  = !connected;
            _txtTimer.IsVisible = showNumbers;

            _dotState.IsVisible      = connected;
            _txtState.IsVisible      = connected && showStateWord;
            _txtSep1.IsVisible       = showNumbers && showStateWord;
            _txtActive.IsVisible     = showNumbers;
            _txtActiveWord.IsVisible = showNumbers;

            _panDown.IsVisible = showNumbers;
            _panUp.IsVisible   = showNumbers;

            if (_hoverLive == showNumbers) return;

            _hoverLive = showNumbers;
            _hotCount.Classes.Set("statHot", showNumbers);
            _hotSpeed.Classes.Set("statHot", showNumbers);
            ToolTip.SetTip(_hotCount, showNumbers ? _countTip.Root : null);
            ToolTip.SetTip(_hotSpeed, showNumbers ? _speedTip.Root : null);
        }

        private void ApplyModeChip()
        {
            _chipMode.IsVisible = _visualState == 2 && !_vpnMode;
        }

        private void ApplyTooltip()
        {
            var sb = new StringBuilder();
            sb.Append(AppStrings.TtOverlayStatsTitle);

            if (_visualState == 2)
            {
                sb.Append('\n')
                  .Append(AppStrings.TtOverlayStatsSession)
                  .Append(' ')
                  .Append(FormatDuration(_sessionElapsed ?? TimeSpan.Zero));

                if (_hasTelemetry)
                {
                    sb.Append("   ·   ")
                      .Append(_activeConnections.ToString(CultureInfo.InvariantCulture))
                      .Append(' ').Append(AppStrings.OverlayStatsActive)
                      .Append("   ·   ↓ ").Append(FormatSpeed(_speedDown, false))
                      .Append("   ↑ ").Append(FormatSpeed(_speedUp, false));

                    foreach (var proc in _lastProcesses.Take(5))
                    {
                        sb.Append('\n')
                          .Append(Capitalize(proc.ProcessName))
                          .Append(" — ")
                          .Append(proc.ActiveConnections.ToString(CultureInfo.InvariantCulture))
                          .Append(" · ↓ ");

                        if (_procSpeed.TryGetValue(proc.ProcessName, out var speed))
                            sb.Append(FormatSpeed(speed.Dn, false)).Append(" ↑ ").Append(FormatSpeed(speed.Up, false));
                        else
                            sb.Append(NoValue);
                    }

                    sb.Append('\n').Append(AppStrings.TtOverlayStatsHint);
                }
                else
                {
                    sb.Append('\n').Append(_vpnMode ? AppStrings.TtOverlayStatsNoRules : AppStrings.TtOverlayStatsProxy);
                }
            }
            else
            {
                sb.Append('\n').Append(_visualState == 1 ? AppStrings.StatusConnecting : AppStrings.StatusNotConnected);
            }

            string text = sb.ToString();
            if (text == _tipText) return;

            _tipText = text;
            AppStrings.ApplyToolTip(this, text);
        }

        // Per-app hover boxes

        private void ApplyAppTips()
        {
            var totals = new Dictionary<string, (int Count, double Dn, double Up)>(StringComparer.OrdinalIgnoreCase);

            if (_hasTelemetry)
            {
                foreach (var app in GetRuleApps())
                {
                    int count = 0;
                    double down = 0, up = 0;

                    foreach (var proc in _lastProcesses)
                    {
                        if (!app.Names.Contains(proc.ProcessName)) continue;

                        count += proc.ActiveConnections;
                        if (_procSpeed.TryGetValue(proc.ProcessName, out var speed))
                        {
                            down += speed.Dn;
                            up   += speed.Up;
                        }
                    }

                    if (count == 0 && down <= 0 && up <= 0) continue;

                    if (totals.TryGetValue(app.Label, out var seen))
                    {
                        count += seen.Count;
                        down  += seen.Dn;
                        up    += seen.Up;
                    }

                    totals[app.Label] = (count, down, up);
                }
            }

            SyncTip(_countTip, BuildTipLines(totals, bySpeed: false), AppStrings.OverlayStatsNoConnections, _persian);
            SyncTip(_speedTip, BuildTipLines(totals, bySpeed: true), AppStrings.OverlayStatsNoTraffic, _persian);
        }

        private static List<TipLine> BuildTipLines(Dictionary<string, (int Count, double Dn, double Up)> totals,
                                                   bool bySpeed)
        {
            var lines = new List<TipLine>();

            var busy = totals
                .Where(kv => bySpeed ? kv.Value.Dn + kv.Value.Up > 0 : kv.Value.Count > 0)
                .OrderByDescending(kv => bySpeed ? kv.Value.Dn + kv.Value.Up : (double)kv.Value.Count)
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int shown = Math.Min(busy.Count, TipMaxRows);
            for (int i = 0; i < shown; i++)
            {
                var (label, value) = busy[i];
                lines.Add(new TipLine(label, bySpeed
                    ? "↓ " + FormatSpeed(value.Dn, false) + "   ↑ " + FormatSpeed(value.Up, false)
                    : value.Count.ToString(CultureInfo.InvariantCulture) + " "
                      + (value.Count == 1 ? AppStrings.OverlayStatsConnection : AppStrings.OverlayStatsConnections)));
            }

            if (busy.Count > shown && shown > 0)
                lines.Add(new TipLine(
                    string.Format(CultureInfo.InvariantCulture, AppStrings.OverlayStatsMoreApps, busy.Count - shown),
                    "", true));

            return lines;
        }

        private static void SyncTip(TipBox box, List<TipLine> lines, string emptyText, bool persian)
        {
            var font = persian ? PersianFont : FontFamily.Default;

            box.Empty.IsVisible = lines.Count == 0;
            box.Table.IsVisible = lines.Count > 0;

            if (box.Empty.Text != emptyText) box.Empty.Text = emptyText;
            box.Empty.FontFamily = font;
            box.Root.FlowDirection = persian ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

            for (int i = 0; i < lines.Count; i++)
            {
                if (i >= box.Rows.Count) box.AddRow();

                var row  = box.Rows[i];
                var line = lines[i];

                if (row.Name.Text != line.Label) row.Name.Text = line.Label;
                if (row.Value.Text != line.Value) row.Value.Text = line.Value;

                row.Name.FontFamily  = font;
                row.Value.FontFamily = font;
                row.Name.FontStyle   = line.Note ? FontStyle.Italic : FontStyle.Normal;
                row.Name.Foreground  = line.Note ? TipNote : TipName;

                row.Name.IsVisible      = true;
                row.Value.IsVisible     = true;
                row.Separator.IsVisible = i < lines.Count - 1;
            }

            for (int i = lines.Count; i < box.Rows.Count; i++)
            {
                var row = box.Rows[i];
                row.Name.IsVisible      = false;
                row.Value.IsVisible     = false;
                row.Separator.IsVisible = false;
            }
        }

        private readonly struct TipLine
        {
            public TipLine(string label, string value, bool note = false)
            {
                Label = label;
                Value = value;
                Note  = note;
            }

            public readonly string Label;
            public readonly string Value;
            public readonly bool Note;
        }

        private sealed class TipBox
        {
            public TipBox()
            {
                Empty = new TextBlock
                {
                    FontSize   = 11,
                    FontStyle  = FontStyle.Italic,
                    Foreground = TipValue
                };

                Table = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

                Root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
                Root.Children.Add(Empty);
                Root.Children.Add(Table);
            }

            public StackPanel Root { get; }

            public Grid Table { get; }

            public TextBlock Empty { get; }

            public List<(TextBlock Name, TextBlock Value, Border Separator)> Rows { get; } = new();

            public void AddRow()
            {
                int line = Rows.Count * 2;
                EnsureRows(line + 1);

                var name = new TextBlock
                {
                    FontSize          = 11,
                    FontWeight        = FontWeight.SemiBold,
                    Foreground        = TipName,
                    MaxWidth          = 150,
                    TextTrimming      = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var value = new TextBlock
                {
                    FontSize          = 11,
                    Foreground        = TipValue,
                    Margin            = new Thickness(14, 0, 0, 0),
                    FlowDirection     = FlowDirection.LeftToRight,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var separator = new Border
                {
                    Height              = 1,
                    Background          = TipRule,
                    Margin              = new Thickness(0, 5, 0, 5),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                Grid.SetRow(name, line);
                Grid.SetColumn(name, 0);
                Grid.SetRow(value, line);
                Grid.SetColumn(value, 1);
                Grid.SetRow(separator, line + 1);
                Grid.SetColumn(separator, 0);
                Grid.SetColumnSpan(separator, 2);

                Table.Children.Add(name);
                Table.Children.Add(value);
                Table.Children.Add(separator);

                Rows.Add((name, value, separator));
            }

            private void EnsureRows(int count)
            {
                while (Table.RowDefinitions.Count < count)
                    Table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }
        }

        // Formatting

        internal static string FormatDuration(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;

            int hours = (int)span.TotalHours;
            return hours.ToString("00", CultureInfo.InvariantCulture) + ":"
                 + span.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":"
                 + span.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }

        internal static string FormatSpeed(double bytesPerSec, bool compact)
        {
            if (bytesPerSec < 1)
                return compact ? "0" : "0 B/s";

            if (bytesPerSec < 1024)
                return Math.Round(bytesPerSec).ToString("0", CultureInfo.InvariantCulture) + (compact ? "" : " B/s");

            if (bytesPerSec < 1024 * 1024)
            {
                double kb = bytesPerSec / 1024.0;
                return kb.ToString(kb >= 100 ? "0" : "0.#", CultureInfo.InvariantCulture) + (compact ? "K" : " KB/s");
            }

            double mb = bytesPerSec / (1024.0 * 1024.0);
            return mb.ToString(mb >= 100 ? "0" : "0.#", CultureInfo.InvariantCulture) + (compact ? "M" : " MB/s");
        }

        private static string Capitalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }
    }
}

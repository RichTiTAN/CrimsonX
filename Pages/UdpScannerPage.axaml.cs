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
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CrimsonX.Localization;
using CrimsonX.Services;

namespace CrimsonX.Pages
{
    public class UdpScanItem : INotifyPropertyChanged
    {
        private static readonly IBrush GoodBrush = new SolidColorBrush(Color.FromRgb(0x68, 0xD3, 0x91));
        private static readonly IBrush WeakBrush = new SolidColorBrush(Color.FromRgb(0xF6, 0xAD, 0x55));
        private static readonly IBrush BadBrush = new SolidColorBrush(Color.FromRgb(0xFC, 0x81, 0x81));
        private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E));

        internal static IBrush Good => GoodBrush;
        internal static IBrush Weak => WeakBrush;
        internal static IBrush Bad => BadBrush;
        internal static IBrush Muted => MutedBrush;

        internal static IBrush ForPing(long ping) => ping <= 120 ? GoodBrush : ping <= 250 ? WeakBrush : BadBrush;

        public string Location { get; set; } = "";
        public string OutboundJson { get; set; } = "";
        public string Source { get; set; } = "";
        public string CountryCode { get; set; } = "";
        public string StabilityLabel { get; set; } = "STABILITY";
        public string StabilityTip { get; set; } = "Stability test";
        public string CopyTip { get; set; } = "Copy share link";
        public string SaveTip { get; set; } = "";
        public long Ping { get; set; }

        public string PingText => Ping > 0 ? $"{Ping} ms" : "-";

        public IBrush PingBrush { get; set; } = MutedBrush;

        private string _locationDetail = "";
        public string LocationDetail
        {
            get => _locationDetail;
            set { _locationDetail = value; Raise(nameof(Detail)); }
        }

        private string _stabilityDetail = "";
        public string StabilityDetail
        {
            get => _stabilityDetail;
            set { _stabilityDetail = value; Raise(nameof(Detail)); }
        }

        public string Detail => string.IsNullOrWhiteSpace(_stabilityDetail)
            ? _locationDetail
            : $"{_locationDetail}\n{_stabilityDetail}";

        private string _stabilityText = "";
        public string StabilityText
        {
            get => _stabilityText;
            set
            {
                _stabilityText = value;
                Raise(nameof(StabilityText));
                Raise(nameof(HasStability));
            }
        }

        public bool HasStability => !string.IsNullOrWhiteSpace(_stabilityText);

        private IBrush _stabilityBrush = MutedBrush;
        public IBrush StabilityBrush
        {
            get => _stabilityBrush;
            set { _stabilityBrush = value; Raise(nameof(StabilityBrush)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    public partial class UdpScannerPage : UserControl
    {
        private readonly List<UdpScanItem> _items = new List<UdpScanItem>();
        private readonly HashSet<string> _seen = new HashSet<string>();
        private readonly CancellationTokenSource _lifetimeCts = new CancellationTokenSource();
        private CancellationTokenSource? _scanCts;
        private CancellationTokenSource? _stabilityCts;
        private bool _isScanning;
        private bool _isStabilityRunning;
        private bool _hasStabilityResult;
        private DispatcherTimer? _dotsTimer;
        private int _dotsPhase;
        private string _statusBase = "";
        private const double EmptyDotsWidth = 14;

        private static MainWindow Main => MainWindow.Instance;
        internal event EventHandler? BackRequested;

        public UdpScannerPage()
        {
            InitializeComponent();
            LoadOptions();
            ApplyLanguage();
        }

        // ── Options ──

        private bool _loadingOptions;

        private void LoadOptions()
        {
            _loadingOptions = true;
            try
            {
                var cfg = Main.Config;

                SetComboIndex("cmbAmount", cfg.UdpScanAmount == 5 ? 0 : cfg.UdpScanAmount == 15 ? 2 : 1);
                SetComboIndex("cmbConcurrency", cfg.UdpScanConcurrency >= 10 ? 1 : 0);
                SetComboIndex("cmbDiscard", cfg.UdpScanDiscardMs <= 0 ? 0 : Math.Clamp(cfg.UdpScanDiscardMs / 100, 1, 9));
            }
            finally
            {
                _loadingOptions = false;
            }
        }

        private void SetComboIndex(string name, int index)
        {
            var cmb = this.FindControl<ComboBox>(name);
            if (cmb != null && index >= 0 && index < cmb.Items.Count) cmb.SelectedIndex = index;
        }

        private void ScannerOption_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (_loadingOptions) return;

            if ((sender as ComboBox)?.SelectedIndex < 0) return;

            var cfg = Main.Config;
            int previousDiscard = cfg.UdpScanDiscardMs;

            cfg.UdpScanAmount = SelectedAmount();
            cfg.UdpScanConcurrency = SelectedConcurrency();
            cfg.UdpScanDiscardMs = SelectedDiscardPing();
            cfg.UdpScanAdapterName = SelectedAdapterName();
            cfg.UdpScanAdapterIp = SelectedAdapterIp();
            Main.RequestConfigSave();

            if (cfg.UdpScanDiscardMs != previousDiscard) _lastPadded = 0;

            if (!_isScanning && !_isStabilityRunning) RefreshStatusText();
        }
        internal void OnEnter()
        {
            LoadAdapters();

            ApplyLanguage();
        }

        private void Back_Click(object? sender, RoutedEventArgs e)
        {
            BackRequested?.Invoke(this, EventArgs.Empty);
        }
        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            try { _lifetimeCts.Cancel(); } catch (Exception ex) { SimpleLogger.Log(ex); }
            try { _stabilityCts?.Cancel(); } catch (Exception ex) { SimpleLogger.Log(ex); }

            _dotsTimer?.Stop();

            base.OnDetachedFromVisualTree(e);
        }

        // ── Localization ──

        internal void ApplyLanguage()
        {
            TextBlock? T(string name) => this.FindControl<TextBlock>(name);
            Button? B(string name) => this.FindControl<Button>(name);

            FlowDirection = AppStrings.IsPersian ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight;

            AppStrings.Apply(T("lblTitle"), AppStrings.UdpScannerTitle);
            RenderEmptyLabel();
            AppStrings.Apply(T("lblAmount"), AppStrings.UdpScannerAmount);
            AppStrings.Apply(T("lblConcurrency"), AppStrings.UdpScannerConcurrency);
            AppStrings.Apply(T("lblDiscard"), AppStrings.UdpScannerDiscard);
            AppStrings.Apply(T("lblAdapter"), AppStrings.UdpScannerAdapter);
            AppStrings.Apply(T("lblGraphTitle"), AppStrings.UdpScannerGraphTitle);
            AppStrings.ApplyToolTip(T("lblAmount"), AppStrings.TtUdpScannerAmount);
            AppStrings.ApplyToolTip(T("lblConcurrency"), AppStrings.TtUdpScannerConcurrency);
            AppStrings.ApplyToolTip(T("lblDiscard"), AppStrings.TtUdpScannerDiscard);
            AppStrings.ApplyToolTip(T("lblAdapter"), AppStrings.TtUdpScannerAdapter);
            AppStrings.ApplyToolTip(B("btnStart"), AppStrings.TtUdpScanner);
            AppStrings.ApplyToolTip(B("btnBackTop"), AppStrings.UdpScannerBack);
            AppStrings.ApplyToolTip(B("btnBack"), AppStrings.UdpScannerBack);

            var cbiNoLimit = this.FindControl<ComboBoxItem>("cbiDiscardNone");
            if (cbiNoLimit != null) cbiNoLimit.Content = AppStrings.UdpScannerNoLimit;

            var cbiAdapterDefault = this.FindControl<ComboBoxItem>("cbiAdapterDefault");
            if (cbiAdapterDefault != null) cbiAdapterDefault.Content = AppStrings.UdpScannerAdapterDefault;

            if (!_isStabilityRunning && !_hasStabilityResult)
                UpdateGraphInfo(AppStrings.UdpScannerGraphIdle);

            if (B("btnStart") is Button startBtn)
                AppStrings.ApplyBtn(startBtn, _isScanning ? AppStrings.UdpScannerStop : AppStrings.UdpScannerStart);

            AppStrings.ApplyBtn(B("btnBack"), AppStrings.UdpScannerBack);

            if (!_isScanning && !_isStabilityRunning)
                RefreshStatusText();

            foreach (var item in _items)
            {
                item.StabilityLabel = AppStrings.UdpScannerStability;
                item.StabilityTip = AppStrings.TtUdpScannerStability;
                item.CopyTip = AppStrings.TtUdpScannerCopy;
            }
            RefreshResults();
        }

        // ── Results list ──

        private void RefreshResults()
        {
            var left = new List<UdpScanItem>();
            var right = new List<UdpScanItem>();

            for (int i = 0; i < _items.Count; i++)
            {
                if (i % 2 == 0) left.Add(_items[i]);
                else right.Add(_items[i]);
            }

            var lstLeft = this.FindControl<ItemsControl>("lstResultsLeft");
            if (lstLeft != null) lstLeft.ItemsSource = left;

            var lstRight = this.FindControl<ItemsControl>("lstResultsRight");
            if (lstRight != null) lstRight.ItemsSource = right;

            var empty = this.FindControl<StackPanel>("pnlEmpty");
            if (empty != null)
            {
                empty.IsVisible = _items.Count == 0;
                RenderEmptyLabel();
            }
        }

        private void AddResult(ConfigTestResult r, bool overLimit = false)
        {
            _items.Add(new UdpScanItem
            {
                Location = FormatLocation(r),
                LocationDetail = FormatDetail(r, overLimit),
                Ping = r.Ping,
                PingBrush = UdpScanItem.ForPing(r.Ping),
                OutboundJson = r.OutboundJson,
                Source = string.IsNullOrWhiteSpace(r.Link) ? r.OutboundJson : r.Link,
                CountryCode = r.CountryCode ?? "",
                StabilityLabel = AppStrings.UdpScannerStability,
                StabilityTip = AppStrings.TtUdpScannerStability,
                CopyTip = AppStrings.TtUdpScannerCopy,
                SaveTip = AppStrings.TtSavedConfigsAdd
            });

            RefreshResults();
        }
        private static string BuildCopyText(UdpScanItem item)
        {
            string name = string.IsNullOrWhiteSpace(item.CountryCode)
                ? "CrimsonX"
                : $"CrimsonX-{item.CountryCode.ToUpperInvariant()}";

            string candidate = string.IsNullOrWhiteSpace(item.Source) ? item.OutboundJson : item.Source;

            if (XrayLinkParser.TryBuildShareLink(candidate, out string link, name) && !string.IsNullOrWhiteSpace(link))
                return link;

            return item.OutboundJson;
        }

        // ── Formatting ──

        private static string FormatLocation(ConfigTestResult r)
        {
            string code = r.CountryCode ?? "";
            string name = r.Country ?? "";

            if (AppStrings.IsPersian)
                name = GeoTranslation.GetCountryFa(code, string.IsNullOrWhiteSpace(name) ? code : name);

            if (string.IsNullOrWhiteSpace(name)) name = code;
            if (string.IsNullOrWhiteSpace(name)) name = XrayLinkParser.ExtractServerAddress(r.OutboundJson);
            if (string.IsNullOrWhiteSpace(name)) name = "UNKNOWN";

            return name;
        }

        private static string FormatDetail(ConfigTestResult r, bool overLimit = false)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(r.Country)) parts.Add(r.Country!);
            if (!string.IsNullOrWhiteSpace(r.CountryCode)) parts.Add(r.CountryCode!.ToUpperInvariant());
            if (!string.IsNullOrWhiteSpace(r.Continent)) parts.Add(r.Continent!);

            string server = XrayLinkParser.ExtractServerAddress(r.OutboundJson);
            if (!string.IsNullOrWhiteSpace(server)) parts.Add(server);

            if (r.Ping > 0) parts.Add(string.Format(AppStrings.UdpScannerDetailRealPing, r.Ping));
            if (r.UdpPing > 0) parts.Add(string.Format(AppStrings.UdpScannerDetailUdpPing, r.UdpPing));
            if (overLimit) parts.Add(AppStrings.UdpScannerOverLimit);

            return string.Join(" • ", parts);
        }

        // ── Actions on a result ──

        private async void Copy_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not UdpScanItem item) return;

            await CopyItemAsync(item);
        }

        private Control? _pressedRow;
        private void ScanRow_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control row || row.DataContext is not UdpScanItem) return;
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;

            for (var visual = e.Source as Avalonia.Visual; visual != null && !ReferenceEquals(visual, row); visual = visual.GetVisualParent())
            {
                if (visual is Button) return;
            }

            _pressedRow = row;
            e.Pointer.Capture(row);
            row.Classes.Add("pressed");
        }
        private void ScanRow_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (sender is not Control row) return;

            row.Classes.Remove("pressed");

            bool startedHere = ReferenceEquals(_pressedRow, row);
            _pressedRow = null;

            if (!startedHere || e.InitialPressMouseButton != MouseButton.Left) return;
            if (row.DataContext is not UdpScanItem item) return;

            _ = CopyItemAsync(item);
        }
        private void ScanRow_PointerCaptureLost(object? sender, RoutedEventArgs e)
        {
            if (sender is Control row) row.Classes.Remove("pressed");
            _pressedRow = null;
        }

        private async Task CopyItemAsync(UdpScanItem item)
        {
            string text = BuildCopyText(item);
            if (string.IsNullOrWhiteSpace(text)) return;

            try
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null) await clipboard.SetTextAsync(text);
            }
            catch (Exception ex)
            {
                SimpleLogger.Log($"[UdpScanner] Copy failed: {ex.Message}");
                return;
            }

            Main.ShowToast(AppStrings.ToastCopiedToClipboard, success: true);
        }

        private void Save_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not UdpScanItem item) return;

            SaveItem(item);
        }

        private void SaveItem(UdpScanItem item)
        {
            var cfg = Main.Config;
            if (cfg == null) return;

            string raw = BuildCopyText(item);
            if (string.IsNullOrWhiteSpace(raw)) return;

            switch (AppCustomConfigStore.Store(cfg, raw, out string label))
            {
                case CustomConfigSaveResult.Saved:
                case CustomConfigSaveResult.Updated:
                    Main.ShowToast($"{AppStrings.ToastCustomProxySaved}: {label}", success: true);
                    break;

                case CustomConfigSaveResult.PoolFull:
                    Main.ShowToast(AppStrings.ToastCustomProxyPoolFull);
                    break;

                default:
                    Main.ShowToast(AppStrings.ToastCustomProxyInvalid);
                    break;
            }
        }

        private void UpdateStatus(string text)
        {
            _statusBase = text;
            ApplyStatusLabel();
        }

        private static string Dots(int phase) => new string('.', phase + 1);

        private void ApplyStatusLabel()
        {
            var lbl = this.FindControl<TextBlock>("lblScanStatus");
            if (lbl == null) return;

            bool animating = _isScanning && !_isStabilityRunning && _dotsTimer?.IsEnabled == true;
            AppStrings.Apply(lbl, animating ? _statusBase + Dots(_dotsPhase) : _statusBase);
        }

        private void RenderEmptyLabel()
        {
            var lbl = this.FindControl<TextBlock>("lblEmpty");
            var dots = this.FindControl<TextBlock>("lblEmptyDots");
            if (lbl == null || dots == null) return;

            if (_isScanning)
            {
                AppStrings.Apply(lbl, AppStrings.UdpScannerTesting);
                AppStrings.Apply(dots, Dots(_dotsPhase));
                dots.Width = EmptyDotsWidth;
            }
            else
            {
                AppStrings.Apply(lbl, AppStrings.UdpScannerEmpty);
                AppStrings.Apply(dots, "");
                dots.Width = 0;
            }
        }

        private void StartDots()
        {
            if (_dotsTimer == null)
            {
                _dotsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _dotsTimer.Tick += (_, _) => OnDotsTick();
            }

            _dotsPhase = 0;
            _dotsTimer.Start();
            ApplyStatusLabel();
            RenderEmptyLabel();
        }

        private void StopDots()
        {
            _dotsTimer?.Stop();
            _dotsPhase = 0;
            ApplyStatusLabel();
            RenderEmptyLabel();
        }

        private void OnDotsTick()
        {
            if (!_isScanning)
            {
                StopDots();
                return;
            }

            if (_isStabilityRunning) return;

            _dotsPhase = (_dotsPhase + 1) % 3;
            ApplyStatusLabel();
            RenderEmptyLabel();
        }

        private void UpdateGraphInfo(string text)
        {
            var lbl = this.FindControl<TextBlock>("lblGraphInfo");
            if (lbl != null) AppStrings.Apply(lbl, text);
        }

        // ── Stability test ──

        private async void Stability_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not UdpScanItem item) return;

            if (_isStabilityRunning)
            {
                try { _stabilityCts?.Cancel(); } catch { }
            }

            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;

            var graph = this.FindControl<CrimsonX.Controls.StabilityGraph>("graphStability");
            graph?.Reset();

            var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            cts.CancelAfter(TimeSpan.FromSeconds(40));
            _stabilityCts = cts;

            _isStabilityRunning = true;
            _hasStabilityResult = true;
            UpdateGraphInfo(string.Format(AppStrings.UdpScannerStabilityRunning, item.Location));

            try
            {
                UpdateStatus(AppStrings.UdpScannerStabilityTesting);

                int sent = 0;
                int loss = 0;

                var res = await ConfigTester.TestUdpStabilityAsync(
                    item.OutboundJson,
                    Main.Config,
                    cts.Token,
                    onSample: (ok, ping) =>
                    {
                        sent++;
                        if (!ok) loss++;

                        int probes = sent;
                        int losses = loss;
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (!ReferenceEquals(_stabilityCts, cts)) return;

                            graph?.AddSample(ok, ping);
                            UpdateGraphInfo(string.Format(AppStrings.UdpScannerGraphLive, probes, losses, ok ? ping.ToString() : "-"));
                        });
                    },
                    sendThroughIp: SelectedAdapterIp());

                if (!ReferenceEquals(_stabilityCts, cts)) return;

                if (!res.HasSamples)
                {
                    item.StabilityText = AppStrings.UdpScannerStabilityFailed;
                    item.StabilityBrush = UdpScanItem.Bad;
                    UpdateGraphInfo(AppStrings.UdpScannerStabilityFailed);
                    return;
                }

                int rate = (int)Math.Round(res.SuccessRate * 100);
                item.StabilityText = $"{rate}% • {res.AvgPingMs} ms";
                item.StabilityBrush = rate >= 90 ? UdpScanItem.Good : rate >= 60 ? UdpScanItem.Weak : UdpScanItem.Bad;
                item.StabilityDetail = string.Format(AppStrings.UdpScannerStabilityDetail, rate, res.Ok, res.Sent, res.AvgPingMs, res.MinPingOrZero, res.MaxPingMs);

                UpdateGraphInfo(string.Format(AppStrings.UdpScannerStabilityResult, rate, res.AvgPingMs));
            }
            catch (OperationCanceledException)
            {
                if (ReferenceEquals(_stabilityCts, cts)) UpdateGraphInfo(AppStrings.UdpScannerStabilityFailed);
            }
            catch (Exception ex)
            {
                SimpleLogger.Log($"[UdpScanner] Stability test failed: {ex.Message}");
                if (ReferenceEquals(_stabilityCts, cts)) UpdateGraphInfo(AppStrings.UdpScannerStabilityFailed);
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;

                if (ReferenceEquals(_stabilityCts, cts))
                {
                    _stabilityCts = null;
                    _isStabilityRunning = false;
                    RefreshStatusText();
                }

                cts.Dispose();
            }
        }
    }
}

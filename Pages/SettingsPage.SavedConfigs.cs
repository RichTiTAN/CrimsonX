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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
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
    public class SavedConfigItem : INotifyPropertyChanged
    {
        private static readonly IBrush GoodBrush = new SolidColorBrush(Color.FromRgb(0x68, 0xD3, 0x91));
        private static readonly IBrush WeakBrush = new SolidColorBrush(Color.FromRgb(0xF6, 0xAD, 0x55));
        private static readonly IBrush BadBrush = new SolidColorBrush(Color.FromRgb(0xFC, 0x81, 0x81));
        private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E));

        public string Raw { get; set; } = "";
        public string Detail { get; set; } = "";

        private string _label = "";
        public string Label
        {
            get => _label;
            set { _label = value; Raise(nameof(Label)); }
        }

        private string _editLabel = "";
        public string EditLabel
        {
            get => _editLabel;
            set { _editLabel = value; Raise(nameof(EditLabel)); }
        }

        private bool _isRenaming;
        public bool IsRenaming
        {
            get => _isRenaming;
            set
            {
                if (_isRenaming == value) return;
                _isRenaming = value;
                Raise(nameof(IsRenaming));
                Raise(nameof(ShowLabel));
            }
        }

        public bool ShowLabel => !_isRenaming;
        public string CopyTip { get; set; } = "";
        public string DeleteTip { get; set; } = "";
        public string PingTip { get; set; } = "";

        private string _pingLabel = "";
        public string PingLabel
        {
            get => _pingLabel;
            set { _pingLabel = value; Raise(nameof(PingLabel)); }
        }

        private string _pingText = "";
        public string PingText
        {
            get => _pingText;
            set { _pingText = value; Raise(nameof(PingText)); }
        }

        private IBrush _pingBrush = MutedBrush;
        public IBrush PingBrush
        {
            get => _pingBrush;
            set { _pingBrush = value; Raise(nameof(PingBrush)); }
        }

        public void SetPing(long ping, bool isTcp = false)
        {
            string number = isTcp
                ? CrimsonX.Localization.AppStrings.TcpPingPrefix + ping + "ms"
                : $"{ping} ms";
            PingText = ping > 0 ? number : "-";
            PingBrush = ping <= 0 ? MutedBrush : ping <= 120 ? GoodBrush : ping <= 250 ? WeakBrush : BadBrush;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class SettingsPage
    {
        private readonly List<SavedConfigItem> _savedConfigs = new();
        private bool _isSavedConfigPinging;
        private Control? _pressedSavedRow;

        private static readonly string[] ConfigComboNames = new[] { "cbCustomConfig1", "cbCustomConfig2", "cbXrayExitNode" };

        private List<AppCustomConfigEntry> _configComboEntries = new();
        private bool _suppressConfigComboSync;

        internal void RefreshConfigCombos()
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return;
            _configComboEntries = AppCustomConfigStore.Load(cfg);
            var options = AppCustomConfigStore.DisplayOptions(_configComboEntries);
            bool wasSuppressed = _suppressConfigComboSync;
            _suppressConfigComboSync = true;
            try
            {
                foreach (var name in ConfigComboNames)
                {
                    var cb = this.FindControl<ComboBox>(name);
                    if (cb == null) continue;
                    string text = cb.Text ?? "";
                    cb.ItemsSource = options;
                    cb.SelectedIndex = -1;
                    cb.Text = text;
                }
            }
            finally
            {
                _suppressConfigComboSync = wasSuppressed;
            }
            AttachCustomConfigInput();
            ApplyCustomConfigTitles();
            ShowQuickSettingsConfigs();
        }

        private void CustomConfigSaved_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isInitializingSettings || _suppressConfigComboSync) return;
            if (sender is not ComboBox cb || cb.SelectedIndex < 0) return;
            int index = cb.SelectedIndex;
            var cfg = MainWindow.Instance?.Config;
            bool usable = true;
            string raw = "";
            string reason = "";
            int slot = cb.Name != null && cb.Name.EndsWith("2", StringComparison.Ordinal) ? 2 : 1;
            if (index > 0 && index - 1 < _configComboEntries.Count)
            {
                var entry = _configComboEntries[index - 1];
                usable = ConfigConverter.TryAcceptFor(entry.Raw, ConfigTarget.Xray, out reason);
                if (usable) raw = entry.Raw.Trim();
            }
            _suppressConfigComboSync = true;
            try
            {
                cb.SelectedIndex = -1;
                if (usable)
                {
                    SetConfigRaw(cfg, slot, raw);
                    cb.Text = AppCustomConfigStore.LabelFor(cfg, raw);
                    MainWindow.Instance?.RequestConfigSave();
                }
            }
            finally
            {
                _suppressConfigComboSync = false;
            }
            if (!usable)
            {
                MainWindow.Instance?.ShowToast(AppStrings.ToastConfigNotForXray + reason, ToastKind.Error);
                return;
            }
            _ = ValidateSlotAfterPickAsync(slot, raw);
        }

        private async Task ValidateSlotAfterPickAsync(int slot, string raw)
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null || raw.Length == 0) return;

            // OpenVPN / WireGuard are dialed by sing-box
            if (ConfigConverter.TryTunnel(raw, out _, out _)) return;
            string xrayError = await XrayConfigValidator.CheckAsync(cfg, raw);
            if (xrayError.Length == 0) return;
            string shortError = ConfigValidator.ShortReason(xrayError);
            MainWindow.Instance?.ShowToast(AppStrings.ToastXrayRejected + shortError, ToastKind.Error);
            SetConfigRaw(cfg, slot, "");
            var cb = this.FindControl<ComboBox>(ConfigComboName(slot));
            if (cb != null) cb.Text = "";
            ConfigBoxes.Show(QuickSlotBoxKey(slot), "");
        }

        internal void ApplySavedConfigsLanguage()
        {
            AppStrings.Apply(this.FindControl<TextBlock>("lblSavedConfigsTitle"), AppStrings.SavedConfigsTitle);
            AppStrings.ApplyToolTip(this.FindControl<TextBlock>("lblSavedConfigsTitle"), AppStrings.TtSavedConfigs);
            AppStrings.ApplyToolTip(this.FindControl<Button>("btnSavedConfigAdd"), AppStrings.TtSavedConfigsAdd);
            AppStrings.ApplyBtn(this.FindControl<Button>("btnSavedConfigAdd"), AppStrings.Save);
            AppStrings.Apply(this.FindControl<TextBlock>("lblSavedEmpty"), AppStrings.SavedConfigsEmpty);
            var browse = this.FindControl<Button>("btnSavedConfigBrowse");
            if (browse != null) AppStrings.ApplyToolTip(browse, AppStrings.ImportConfigTooltip);
            foreach (var name in new[] { "btnCustomConfigImport1", "btnCustomConfigImport2" })
            {
                var add = this.FindControl<Button>(name);
                if (add != null) AppStrings.ApplyToolTip(add, AppStrings.ImportConfigTooltip);
            }
            var box = this.FindControl<TextBox>("txtSavedConfigImport");
            if (box != null) box.PlaceholderText = AppStrings.SavedConfigsImportPlaceholder;
            RefreshSavedConfigs();
        }

        internal void RefreshSavedConfigs()
        {
            var cfg = MainWindow.Instance?.Config;
            _savedConfigs.Clear();
            if (cfg != null)
            {
                foreach (var entry in AppCustomConfigStore.Load(cfg))
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Raw)) continue;
                    string raw = entry.Raw.Trim();
                    _savedConfigs.Add(new SavedConfigItem
                    {
                        Raw = raw,
                        Label = string.IsNullOrWhiteSpace(entry.Label) ? SingboxLinkParser.LabelOf(raw) : entry.Label,
                        Detail = raw.Length > 300 ? raw.Substring(0, 300) + "..." : raw,
                        CopyTip = AppStrings.TtSavedConfigsCopy,
                        DeleteTip = AppStrings.TtSavedConfigsDelete,
                        PingTip = AppStrings.TtSavedConfigsPing,
                        PingLabel = AppStrings.PingBtn
                    });
                }
            }
            var left = new List<SavedConfigItem>();
            var right = new List<SavedConfigItem>();
            for (int i = 0; i < _savedConfigs.Count; i++)
            {
                if (i % 2 == 0) left.Add(_savedConfigs[i]);
                else right.Add(_savedConfigs[i]);
            }
            var lstLeft = this.FindControl<ItemsControl>("lstSavedLeft");
            if (lstLeft != null) lstLeft.ItemsSource = left;
            var lstRight = this.FindControl<ItemsControl>("lstSavedRight");
            if (lstRight != null) lstRight.ItemsSource = right;
            var empty = this.FindControl<StackPanel>("pnlSavedEmpty");
            if (empty != null) empty.IsVisible = _savedConfigs.Count == 0;
            RefreshConfigCombos();
        }

        private void SetSavedConfigsExpanded(bool expanded)
        {
            var pan = this.FindControl<Border>("panSavedConfigs");
            if (pan == null) return;
            if (expanded) RefreshSavedConfigs();
            var ico = this.FindControl<PathIcon>("icoSavedConfigsExpander");
            var panToggle = this.FindControl<Border>("panSavedConfigsToggle");
            var btnToggle = this.FindControl<Button>("btnSavedConfigsToggle");
            pan.MaxHeight = expanded ? 270 : 0;
            pan.Opacity = expanded ? 1 : 0;
            if (ico != null) ico.RenderTransform = new RotateTransform(expanded ? 180 : 0);
            if (panToggle != null) panToggle.CornerRadius = expanded ? new CornerRadius(8, 8, 0, 0) : new CornerRadius(8);
            if (btnToggle != null) btnToggle.CornerRadius = expanded ? new CornerRadius(8, 8, 0, 0) : new CornerRadius(8);
        }

        private void btnSavedConfigsToggle_Click(object? sender, RoutedEventArgs e)
        {
            var pan = this.FindControl<Border>("panSavedConfigs");
            if (pan != null) SetSavedConfigsExpanded(pan.MaxHeight == 0);
        }

        private void StoreSavedConfigRaw(string raw)
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null || string.IsNullOrWhiteSpace(raw)) return;
            switch (AppCustomConfigStore.Store(cfg, raw.Trim(), out var label))
            {
                case CustomConfigSaveResult.Saved:
                case CustomConfigSaveResult.Updated:
                    MainWindow.Instance?.ShowToast($"{AppStrings.ToastCustomProxySaved}: {label}", ToastKind.Success);
                    RefreshSavedConfigs();
                    RefreshConfigCombos();
                    break;
                case CustomConfigSaveResult.PoolFull:
                    MainWindow.Instance?.ShowToast(AppStrings.ToastCustomProxyPoolFull, ToastKind.Error);
                    RefreshSavedConfigs();
                    break;
                default:
                    MainWindow.Instance?.ShowToast(AppStrings.ToastConfigUnreadable, ToastKind.Error);
                    break;
            }
        }

        private void SavedConfigAdd_Click(object? sender, RoutedEventArgs e)
        {
            var box = this.FindControl<TextBox>("txtSavedConfigImport");
            try
            {
                var cfg = MainWindow.Instance?.Config;
                if (cfg == null) return;
                string raw = box?.Text?.Trim() ?? "";
                if (raw.Length == 0)
                {
                    MainWindow.Instance?.ShowToast(AppStrings.CustomProxyEmpty, ToastKind.Error);
                    box?.Focus();
                    return;
                }
                switch (AppCustomConfigStore.Store(cfg, raw, out var label))
                {
                    case CustomConfigSaveResult.Saved:
                    case CustomConfigSaveResult.Updated:
                        MainWindow.Instance?.ShowToast($"{AppStrings.ToastCustomProxySaved}: {label}", ToastKind.Success);
                        if (box != null) box.Text = "";
                        RefreshSavedConfigs();
                        break;
                    case CustomConfigSaveResult.PoolFull:
                        MainWindow.Instance?.ShowToast(AppStrings.ToastCustomProxyPoolFull, ToastKind.Error);
                        break;
                    default:
                        MainWindow.Instance?.ShowToast(AppStrings.ToastCustomProxyInvalid, ToastKind.Error);
                        break;
                }
                box?.Focus();
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
        }

        private async void SavedConfigPing_Click(object? sender, RoutedEventArgs e)
        {
            if (_isSavedConfigPinging) return;
            if ((sender as Control)?.DataContext is not SavedConfigItem item) return;
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return;
            var btn = sender as Button;
            _isSavedConfigPinging = true;
            item.PingLabel = AppStrings.ValidatingConfig;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                using var cts = new CancellationTokenSource(15000);
                var res = await CustomConfigPinger.ProbeAsync(item.Raw, cfg, cfg.UdpScanAdapterName, cfg.UdpScanAdapterIp, cts.Token);
                bool measured = res != null && res.Measured;
                long ping = measured ? res.Ping : -1;
                bool timedOut = !measured && res != null && res.TimedOut;
                item.SetPing(ping, res != null && res.IsTcpPing);
                string msg = measured ? res.Text(item.Label)
                           : timedOut ? AppStrings.CustomProxyNoResponse
                           : AppStrings.InvalidConfig + ReasonOf(res);
                MainWindow.Instance?.ShowToast(msg, measured ? ToastKind.Success : ToastKind.Error);
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                MainWindow.Instance?.ShowToast(AppStrings.InvalidConfig, ToastKind.Error);
            }
            finally
            {
                item.PingLabel = AppStrings.PingBtn;
                if (btn != null) btn.IsEnabled = true;
                _isSavedConfigPinging = false;
            }
        }

        private async void SavedConfigCopy_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not SavedConfigItem item) return;
            await CopySavedConfigAsync(item);
        }

        private void SavedConfigDelete_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not SavedConfigItem item) return;
            try
            {
                var cfg = MainWindow.Instance?.Config;
                if (cfg == null) return;
                if (AppCustomConfigStore.Delete(cfg, item.Raw) > 0)
                {
                    ClearSlotsForDeletedConfig(item.Raw);
                    MainWindow.Instance?.ShowToast(AppStrings.ToastCustomProxyDeleted, ToastKind.Success);
                    RefreshSavedConfigs();
                }
                else
                {
                    MainWindow.Instance?.ShowToast(AppStrings.ToastCustomProxyNotSaved, ToastKind.Error);
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
        }

        private void SavedConfigRow_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control row || row.DataContext is not SavedConfigItem) return;
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            for (var visual = e.Source as Visual; visual != null && !ReferenceEquals(visual, row); visual = visual.GetVisualParent())
            {
                if (visual is Button || visual is TextBox) return;
                if (visual is TextBlock tb && tb.Classes.Contains("savedLabel")) return;
            }
            _pressedSavedRow = row;
            e.Pointer.Capture(row);
            row.Classes.Add("pressed");
        }

        private void SavedConfigRow_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (sender is not Control row) return;
            row.Classes.Remove("pressed");
            bool startedHere = ReferenceEquals(_pressedSavedRow, row);
            _pressedSavedRow = null;
            if (!startedHere || e.InitialPressMouseButton != MouseButton.Left) return;
            if (row.DataContext is not SavedConfigItem item) return;
            _ = CopySavedConfigAsync(item);
        }

        private void SavedConfigRow_PointerCaptureLost(object? sender, RoutedEventArgs e)
        {
            if (sender is Control row) row.Classes.Remove("pressed");
            _pressedSavedRow = null;
        }

        private void SavedConfigLabel_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control row || row.DataContext is not SavedConfigItem item) return;
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            item.EditLabel = item.Label;
            item.IsRenaming = true;
            e.Handled = true;
            var root = row as Visual;
            while (root != null && root is not Border) root = root.GetVisualParent();
            var box = root?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            if (box == null) return;
            Dispatcher.UIThread.Post(() =>
            {
                box.Focus();
                box.SelectAll();
            }, DispatcherPriority.Background);
        }

        private void SavedConfigLabelEdit_KeyDown(object? sender, KeyEventArgs e)
        {
            if ((sender as Control)?.DataContext is not SavedConfigItem item) return;
            if (e.Key == Key.Enter)
            {
                CommitSavedConfigRename(item);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                item.IsRenaming = false;
                e.Handled = true;
            }
        }

        private void SavedConfigLabelEdit_LostFocus(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is SavedConfigItem item)
                CommitSavedConfigRename(item);
        }

        private void CommitSavedConfigRename(SavedConfigItem item)
        {
            if (!item.IsRenaming) return;
            item.IsRenaming = false;
            string name = (item.EditLabel ?? "").Trim();
            if (name.Length == 0 || name == item.Label) return;
            try
            {
                var cfg = MainWindow.Instance?.Config;
                if (cfg == null) return;
                var entries = AppCustomConfigStore.Load(cfg);
                var entry = entries.FirstOrDefault(en => string.Equals(en.Raw.Trim(), item.Raw, StringComparison.Ordinal));
                if (entry == null) return;
                entry.Label = name;
                AppCustomConfigStore.Save(entries);
                item.Label = name;
                RefreshConfigCombos();
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
        }

        private async Task CopySavedConfigAsync(SavedConfigItem item)
        {
            string text = XrayLinkParser.TryBuildShareLink(item.Raw, out string link, "CrimsonX") && !string.IsNullOrWhiteSpace(link)
                ? link
                : item.Raw;
            if (string.IsNullOrWhiteSpace(text)) return;
            try
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null) await clipboard.SetTextAsync(text);
            }
            catch (Exception ex)
            {
                SimpleLogger.Log($"[Settings] Copy failed: {ex.Message}");
                return;
            }
            MainWindow.Instance?.ShowToast(AppStrings.ToastCopiedToClipboard, ToastKind.Success);
        }
    }
}

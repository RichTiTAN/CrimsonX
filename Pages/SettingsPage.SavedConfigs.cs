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

        public void SetPing(long ping)
        {
            PingText = ping > 0 ? $"{ping} ms" : "-";
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

        private void SyncCustomConfigsToSavedList()
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return;

            bool changed = false;
            foreach (var raw in new[] { cfg.CustomConfig1, cfg.CustomConfig2 })
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var result = AppCustomConfigStore.Store(cfg, raw.Trim(), out _);
                if (result == CustomConfigSaveResult.Saved || result == CustomConfigSaveResult.Updated) changed = true;
            }

            if (changed) RefreshSavedConfigs();
        }

        private static readonly string[] ConfigComboNames = new[] { "cbCustomConfig1", "cbCustomConfig2" };

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
        }

        private void CustomConfigSaved_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isInitializingSettings || _suppressConfigComboSync) return;
            if (sender is not ComboBox cb || cb.SelectedIndex < 0) return;

            int index = cb.SelectedIndex;

            bool usable = true;
            string text = "";

            if (index > 0 && index - 1 < _configComboEntries.Count)
            {
                var entry = _configComboEntries[index - 1];

                usable = XrayLinkParser.TryParseCustomConfig(entry.Raw, out var normalized)
                         && normalized.Contains("\"protocol\"");

                if (usable)
                {
                    text = XrayLinkParser.TryBuildShareLink(entry.Raw, out var link, entry.Label) && !string.IsNullOrWhiteSpace(link)
                        ? link
                        : entry.Raw;
                }
            }

            _suppressConfigComboSync = true;
            try
            {
                cb.SelectedIndex = -1;
                if (usable) cb.Text = text;
            }
            finally
            {
                _suppressConfigComboSync = false;
            }

            if (!usable) MainWindow.Instance?.ShowToast(AppStrings.ToastSavedConfigNotUsable);
        }

        internal void ApplySavedConfigsLanguage()
        {
            AppStrings.Apply(this.FindControl<TextBlock>("lblSavedConfigsTitle"), AppStrings.SavedConfigsTitle);
            AppStrings.ApplyToolTip(this.FindControl<TextBlock>("lblSavedConfigsTitle"), AppStrings.TtSavedConfigs);
            AppStrings.ApplyToolTip(this.FindControl<Button>("btnSavedConfigAdd"), AppStrings.TtSavedConfigsAdd);
            AppStrings.Apply(this.FindControl<TextBlock>("lblSavedEmpty"), AppStrings.SavedConfigsEmpty);

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
                    MainWindow.Instance?.ShowToast(AppStrings.CustomProxyEmpty);
                    box?.Focus();
                    return;
                }

                switch (AppCustomConfigStore.Store(cfg, raw, out var label))
                {
                    case CustomConfigSaveResult.Saved:
                    case CustomConfigSaveResult.Updated:
                        MainWindow.Instance?.ShowToast($"{AppStrings.ToastCustomProxySaved}: {label}", true);
                        if (box != null) box.Text = "";
                        RefreshSavedConfigs();
                        break;

                    case CustomConfigSaveResult.PoolFull:
                        MainWindow.Instance?.ShowToast(AppStrings.ToastCustomProxyPoolFull);
                        break;

                    default:
                        MainWindow.Instance?.ShowToast(AppStrings.ToastCustomProxyInvalid);
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
                var res = await SingboxConfigTester.TestAsync(item.Raw, cfg, cfg.UdpScanAdapterName, cfg.UdpScanAdapterIp, cts.Token);

                long ping = res != null && res.Success ? res.Ping : -1;
                bool timedOut = res != null && !res.Success && res.TimedOut;

                item.SetPing(ping);

                string msg = ping != -1 ? $"{item.Label}: {ping}ms"
                           : timedOut ? AppStrings.CustomProxyNoResponse
                           : AppStrings.InvalidConfig;
                MainWindow.Instance?.ShowToast(msg, ping != -1);
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                MainWindow.Instance?.ShowToast(AppStrings.InvalidConfig);
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
                    MainWindow.Instance?.ShowToast(AppStrings.ToastCustomProxyDeleted, true);
                    RefreshSavedConfigs();
                }
                else
                {
                    MainWindow.Instance?.ShowToast(AppStrings.ToastCustomProxyNotSaved);
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

            MainWindow.Instance?.ShowToast(AppStrings.ToastCopiedToClipboard, true);
        }
    }
}

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
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CrimsonX.Localization;
using CrimsonX.Models;
using CrimsonX.Services;

namespace CrimsonX.Pages
{
    public partial class SettingsPage
    {
        private bool _customConfigInputAttached;

        private static string ConfigComboName(int slot) => slot == 2 ? "cbCustomConfig2" : "cbCustomConfig1";

        private static string GetConfigRaw(AppConfig cfg, int slot)
            => cfg == null ? "" : (slot == 2 ? cfg.CustomConfig2 : cfg.CustomConfig1) ?? "";

        private static void SetConfigRaw(AppConfig cfg, int slot, string raw)
        {
            if (cfg == null) return;
            if (slot == 2) cfg.CustomConfig2 = raw ?? "";
            else cfg.CustomConfig1 = raw ?? "";
        }

        internal void AttachCustomConfigInput()
        {
            if (_customConfigInputAttached) return;

            var cb1 = this.FindControl<ComboBox>("cbCustomConfig1");
            var cb2 = this.FindControl<ComboBox>("cbCustomConfig2");
            if (cb1 == null || cb2 == null) return;

            var exitBox   = this.FindControl<ComboBox>("cbXrayExitNode");
            var importBox = this.FindControl<TextBox>("txtSavedConfigImport");

            ConfigBoxes.Attach(SlotBoxKey(1), cb1);
            ConfigBoxes.Attach(SlotBoxKey(2), cb2);
            ConfigBoxes.Attach(ExitNodeBoxKey, exitBox);
            ConfigBoxes.Attach(SavedConfigImportKey, importBox);

            ConfigBoxes.AttachPaste(SlotBoxKey(1), text => OnSlotPasteAsync(1, text));
            ConfigBoxes.AttachPaste(SlotBoxKey(2), text => OnSlotPasteAsync(2, text));
            ConfigBoxes.AttachPaste(ExitNodeBoxKey, ApplyExitNodeRawAsync);
            ConfigBoxes.AttachPaste(SavedConfigImportKey, OnSavedImportPasteAsync);

            _customConfigInputAttached = true;
        }

        private Task OnSlotPasteAsync(int slot, string text) => ApplyCustomConfigRawAsync(slot, text);

        private async Task OnSavedImportPasteAsync(string text)
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return;

            var verdict = await ConfigIntake.AcceptAsync(text, cfg, ConfigTarget.Pool, AppStrings.SavedConfigsTitle);

            if (!verdict.Accepted)
            {
                MainWindow.Instance?.ShowToast(verdict.Toast, ToastKind.Error);
                return;
            }

            if (verdict.Toast.Length > 0) MainWindow.Instance?.ShowToast(verdict.Toast, ToastKind.Error);
            text = verdict.Raw;

            if (text.Contains('\n') || text.Contains('\r'))
            {
                StoreSavedConfigRaw(text);
                return;
            }

            var box = this.FindControl<TextBox>("txtSavedConfigImport");
            if (box != null) box.Text = text;
        }

        internal void ApplyCustomConfigTitles() => ApplyCustomConfigTitles(false);

        internal void ApplyCustomConfigTitles(bool force) => ConfigBoxes.ShowAll(force);

        internal async Task<bool> ApplyCustomConfigRawAsync(int slot, string raw)
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return false;

            raw = (raw ?? "").Trim();

            if (raw.Length == 0)
            {
                SetConfigRaw(cfg, slot, "");
                ConfigBoxes.Show(SlotBoxKey(slot), "");
                return true;
            }

            var verdict = await ConfigIntake.AcceptAsync(raw, cfg, ConfigTarget.Xray, AppStrings.PaneCustomConfig);

            if (!verdict.Accepted)
            {
                MainWindow.Instance?.ShowToast(verdict.Toast, ToastKind.Error);
                return false;
            }

            if (verdict.Toast.Length > 0) MainWindow.Instance?.ShowToast(verdict.Toast, ToastKind.Error);
            raw = verdict.Raw;

            SetConfigRaw(cfg, slot, raw);
            RefreshConfigCombos();
            RefreshSavedConfigs();

            ConfigBoxes.Show(SlotBoxKey(slot), raw);

            MainWindow.Instance?.RequestConfigSave();
            return true;
        }

        private string ResolveCustomConfigRaw(int slot)
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return "";

            return ConfigBoxes.Resolve(SlotBoxKey(slot),
                visible => AppCustomConfigStore.CanStore(visible));
        }

        internal void ResolveCustomConfigSlots()
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return;

            for (int slot = 1; slot <= 2; slot++)
            {
                string resolved = ResolveCustomConfigRaw(slot);
                bool cleared = ConfigBoxes.IsEmpty(SlotBoxKey(slot));

                if (resolved.Length > 0 || cleared || GetConfigRaw(cfg, slot).Length == 0)
                    SetConfigRaw(cfg, slot, resolved);
            }
        }

        internal void ClearSlotsForDeletedConfig(string raw)
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null || string.IsNullOrWhiteSpace(raw)) return;

            bool changed = false;
            for (int slot = 1; slot <= 2; slot++)
            {
                string stored = GetConfigRaw(cfg, slot);
                if (stored.Length == 0 || !AppCustomConfigStore.SameConfig(stored, raw)) continue;

                SetConfigRaw(cfg, slot, "");
                var cb = this.FindControl<ComboBox>(ConfigComboName(slot));
                if (cb != null) cb.Text = "";
                changed = true;
            }

            if (!changed) return;

            RefreshConfigCombos();
            MainWindow.Instance?.RequestConfigSave();
            SimpleLogger.Log("[CustomConfigs] A saved config was deleted, so its custom config slot was cleared.");
        }

        internal void SaveCustomConfigSlot(int slot)
        {
            string raw = ResolveCustomConfigRaw(slot);
            if (raw.Length == 0)
            {
                MainWindow.Instance?.ShowToast(AppStrings.CustomProxyEmpty, ToastKind.Error);
                return;
            }

            StoreSavedConfigRaw(raw);
        }

        private void btnCustomConfigsSave1_Click(object? sender, RoutedEventArgs e) => SaveCustomConfigSlot(1);

        private void btnCustomConfigsSave2_Click(object? sender, RoutedEventArgs e) => SaveCustomConfigSlot(2);

        private async void btnCustomConfigImport1_Click(object? sender, RoutedEventArgs e) => await ImportConfigIntoSlotAsync(1);

        private async void btnCustomConfigImport2_Click(object? sender, RoutedEventArgs e) => await ImportConfigIntoSlotAsync(2);

        private async Task ImportConfigIntoSlotAsync(int slot)
        {
            string text = await PickConfigFileTextAsync();
            if (text.Length > 0) await ApplyCustomConfigRawAsync(slot, text);
        }

        internal async void SavedConfigBrowse_Click(object? sender, RoutedEventArgs e)
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return;

            string text = await PickConfigFileTextAsync();
            if (text.Length == 0) return;

            switch (AppCustomConfigStore.Store(cfg, text, out string label))
            {
                case CustomConfigSaveResult.Saved:
                case CustomConfigSaveResult.Updated:
                    MainWindow.Instance?.ShowToast($"{AppStrings.ToastCustomProxySaved}: {label}", ToastKind.Success);
                    RefreshSavedConfigs();
                    RefreshConfigCombos();
                    break;

                case CustomConfigSaveResult.PoolFull:
                    MainWindow.Instance?.ShowToast(AppStrings.ToastCustomProxyPoolFull, ToastKind.Error);
                    break;

                default:
                    MainWindow.Instance?.ShowToast(AppStrings.ToastConfigUnreadable, ToastKind.Error);
                    break;
            }
        }

        internal async Task<string> PickConfigFileTextAsync()
        {
            try
            {
                var top = TopLevel.GetTopLevel(this);
                if (top == null) return "";

                var files = await top.StorageProvider.OpenFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = AppStrings.ImportConfigTooltip,
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new global::Avalonia.Platform.Storage.FilePickerFileType("Config Files") { Patterns = new[] { "*.ovpn", "*.conf", "*.json", "*.txt" } },
                        new global::Avalonia.Platform.Storage.FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
                    }
                });

                if (files == null || files.Count == 0) return "";

                string path = files[0].Path.LocalPath;
                if (!System.IO.File.Exists(path)) return "";

                return System.IO.File.ReadAllText(path).Trim();
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                MainWindow.Instance?.ShowToast(AppStrings.ToastFailedImport, ToastKind.Error);
                return "";
            }
        }

        // ── the Quick Settings panel's two boxes: the same slots, seen twice ────────────────────────

        private bool _quickSettingsBoxesAttached;

        internal void AttachQuickSettingsConfigBoxes(ComboBox? box1, ComboBox? box2)
        {
            if (box1 == null && box2 == null) return;

            foreach ((int slot, ComboBox? box) in new[] { (1, box1), (2, box2) })
            {
                if (box == null) continue;

                string key = QuickSlotBoxKey(slot);
                ConfigBoxes.Attach(key, box);

                if (_quickSettingsBoxesAttached) continue;

                ConfigBoxes.AttachPaste(key, text => QuickSettingsPasteAsync(slot, text));

                box.SelectionChanged += CustomConfigSaved_SelectionChanged;
            }

            _quickSettingsBoxesAttached = true;

            ShowQuickSettingsConfigs();
        }

        private async Task QuickSettingsPasteAsync(int slot, string text)
        {
            await ApplyCustomConfigRawAsync(slot, text);

            ConfigBoxes.Show(QuickSlotBoxKey(slot), ReadConfigBoxRaw(QuickSlotBoxKey(slot)));
        }

        internal void ShowQuickSettingsConfigs()
        {
            RefreshQuickSettingsConfigList();

            for (int slot = 1; slot <= 2; slot++)
            {
                string key = QuickSlotBoxKey(slot);
                if (ConfigBoxes.Box(key) == null) continue;

                ConfigBoxes.Show(key, ReadConfigBoxRaw(key));
            }
        }

        private void RefreshQuickSettingsConfigList()
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return;

            _configComboEntries = AppCustomConfigStore.Load(cfg);
            var options = AppCustomConfigStore.DisplayOptions(_configComboEntries);

            bool wasSuppressed = _suppressConfigComboSync;
            _suppressConfigComboSync = true;
            try
            {
                for (int slot = 1; slot <= 2; slot++)
                {
                    if (ConfigBoxes.Box(QuickSlotBoxKey(slot)) is not ComboBox cb) continue;

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
    }
}

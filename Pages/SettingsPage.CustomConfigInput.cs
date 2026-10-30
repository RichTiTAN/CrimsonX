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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
        private bool _customConfigsDirty;
        private bool _customConfigsExpanded;
        private bool _customConfigsTextDirty;
        private bool _customConfigDraftActive;
        private string _draftConfig1 = "";
        private string _draftConfig2 = "";

        private void MarkCustomConfigsTextDirty()
        {
            if (_isInitializingSettings) return;
            _customConfigsTextDirty = true;
            RecalculateCustomConfigsDirty();
        }

        private void RecalculateCustomConfigsDirty()
        {
            if (_isInitializingSettings) return;
            var cfg = MainWindow.Instance?.Config;
            bool textDirty = CustomConfigTextDirty(cfg);
            _customConfigsTextDirty = textDirty;
            bool dirty = textDirty;
            if (cfg != null)
            {
                var fetch = this.FindControl<ToggleButton>("togFetchConfigWhenOne");
                if (fetch != null)
                    dirty |= (fetch.IsChecked ?? false) != cfg.FetchConfigWhenOneProvided;

                var obfuscation = this.FindControl<ToggleButton>("togCustomConfigObfuscation");
                if (obfuscation != null)
                    dirty |= (obfuscation.IsChecked ?? false) != cfg.EnableCustomConfigObfuscation;

                var per = this.FindControl<ToggleButton>("togPerConfigAdapter");
                bool perOn = per?.IsChecked == true;
                dirty |= perOn != cfg.EnablePerConfigAdapters;
                if (perOn)
                {
                    dirty |= !string.Equals(StagedAdapterPick(1), (cfg.CustomConfig1Adapter ?? "").Trim(), StringComparison.Ordinal);
                    dirty |= !string.Equals(StagedAdapterPick(2), (cfg.CustomConfig2Adapter ?? "").Trim(), StringComparison.Ordinal);
                }
            }
            _customConfigsDirty = dirty;
            UpdateCustomConfigsSubmitVisibility();
        }

        private bool CustomConfigTextDirty(AppConfig? cfg)
        {
            if (cfg == null) return _customConfigsTextDirty;
            if (_customConfigDraftActive
                && (!string.Equals(_draftConfig1 ?? "", cfg.CustomConfig1 ?? "", StringComparison.Ordinal)
                    || !string.Equals(_draftConfig2 ?? "", cfg.CustomConfig2 ?? "", StringComparison.Ordinal)))
                return true;

            for (int slot = 1; slot <= 2; slot++)
            {
                string stored = slot == 2 ? cfg.CustomConfig2 ?? "" : cfg.CustomConfig1 ?? "";
                string title = stored.Length == 0 ? "" : ConfigBoxTitle(stored);
                if (ConfigBoxDiffers(SlotBoxKey(slot), stored, title)
                    || ConfigBoxDiffers(QuickSlotBoxKey(slot), stored, title))
                    return true;
            }
            return false;
        }

        private bool ConfigBoxDiffers(string key, string stored, string title)
            => ConfigBoxes.Box(key) != null
               && !ConfigBoxMatchesStored(ConfigBoxes.Text(key), stored, title);

        internal static bool ConfigBoxMatchesStored(string visible, string stored, string title)
        {
            visible = (visible ?? "").Trim();
            stored = (stored ?? "").Trim();
            if (visible.Length == 0) return stored.Length == 0;
            return string.Equals(visible, stored, StringComparison.Ordinal)
                || string.Equals(visible, title ?? "", StringComparison.Ordinal);
        }

        private string StagedAdapterPick(int slot)
        {
            var combo = this.FindControl<ComboBox>(slot == 2 ? "cmbCustomConfig2Adapter" : "cmbCustomConfig1Adapter");
            return combo?.SelectedIndex > 0 ? (combo.SelectedItem as string ?? "").Trim() : "";
        }

        private void EnsureCustomConfigDraft()
        {
            if (_customConfigDraftActive) return;
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return;
            _draftConfig1 = cfg.CustomConfig1 ?? "";
            _draftConfig2 = cfg.CustomConfig2 ?? "";
            _customConfigDraftActive = true;
        }

        private void CommitCustomConfigRawDraft()
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null || !_customConfigDraftActive) return;
            cfg.CustomConfig1 = _draftConfig1;
            cfg.CustomConfig2 = _draftConfig2;
        }

        private void DiscardCustomConfigsDraft()
        {
            _customConfigDraftActive = false;
            _draftConfig1 = "";
            _draftConfig2 = "";
            _customConfigsTextDirty = false;
            _customConfigsDirty = false;

            var cfg = MainWindow.Instance?.Config;
            if (cfg == null)
            {
                UpdateCustomConfigsSubmitVisibility();
                return;
            }

            ShowCustomConfigSlot(1, cfg.CustomConfig1 ?? "");
            ShowCustomConfigSlot(2, cfg.CustomConfig2 ?? "");
            SyncCustomConfigSwitchesFromConfig();
            UpdateCustomConfigsSubmitVisibility();
        }

        internal void SyncCustomConfigSwitchesFromConfig()
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return;
            bool wasInit = _isInitializingSettings;
            _isInitializingSettings = true;
            try
            {
                var fetch = this.FindControl<ToggleButton>("togFetchConfigWhenOne");
                if (fetch != null) fetch.IsChecked = cfg.FetchConfigWhenOneProvided;
                var obfuscation = this.FindControl<ToggleButton>("togCustomConfigObfuscation");
                if (obfuscation != null) obfuscation.IsChecked = cfg.EnableCustomConfigObfuscation;
            }
            finally
            {
                _isInitializingSettings = wasInit;
            }
            SyncPerConfigAdapterPanel();
        }

        private void ResetStagedAdapter(int slot)
        {
            var combo = this.FindControl<ComboBox>(slot == 2 ? "cmbCustomConfig2Adapter" : "cmbCustomConfig1Adapter");
            if (combo == null) return;
            bool wasInit = _isInitializingSettings;
            _isInitializingSettings = true;
            try { combo.SelectedIndex = 0; }
            finally { _isInitializingSettings = wasInit; }
        }

        private void UpdateCustomConfigsSubmitVisibility()
        {
            bool pending = _customConfigsDirty && !_isInitializingSettings;
            var submit = this.FindControl<Button>("btnCustomConfigsSubmit");
            if (submit != null)
                submit.IsVisible = pending && _customConfigsExpanded;
            MainWindow.Instance?.FindControl<CrimsonX.Controls.QuickSettingsPanel>("quickSettings")
                ?.SetCustomConfigsSubmitVisible(pending);
        }

        internal void RefreshCustomConfigsSubmit() => UpdateCustomConfigsSubmitVisibility();

        private void ConfigBoxTextChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != ComboBox.TextProperty) return;
            if (_isInitializingSettings || _suppressConfigComboSync || ConfigBoxes.Suppressed) return;
            MarkCustomConfigsTextDirty();
        }

        private void ExitNodeBoxTextChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != ComboBox.TextProperty) return;
            if (_isInitializingSettings || _suppressConfigComboSync || ConfigBoxes.Suppressed) return;
            string text = ((sender as ComboBox)?.Text ?? "").Trim();
            _exitNodeNoneSelected = string.Equals(text, CrimsonX.Localization.AppStrings.CustomProxyNone, StringComparison.Ordinal);
            MarkExitNodeDirty();
        }

        private static string ConfigComboName(int slot) => slot == 2 ? "cbCustomConfig2" : "cbCustomConfig1";

        private string GetConfigRaw(AppConfig cfg, int slot)
        {
            if (cfg == null) return "";
            if (_customConfigDraftActive) return slot == 2 ? _draftConfig2 : _draftConfig1;
            return slot == 2 ? cfg.CustomConfig2 ?? "" : cfg.CustomConfig1 ?? "";
        }

        private void SetConfigRaw(AppConfig cfg, int slot, string raw)
        {
            if (cfg == null) return;
            if (_customConfigDraftActive)
            {
                if (slot == 2) _draftConfig2 = raw ?? "";
                else _draftConfig1 = raw ?? "";
                return;
            }
            if (slot == 2) cfg.CustomConfig2 = raw ?? "";
            else cfg.CustomConfig1 = raw ?? "";
        }

        private static void SetConfigRawCommitted(AppConfig cfg, int slot, string raw)
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
            ConfigBoxes.AttachPaste(ExitNodeBoxKey, ExitNodePasteAsync);
            ConfigBoxes.AttachPaste(SavedConfigImportKey, OnSavedImportPasteAsync);
            if (exitBox != null)
                exitBox.PropertyChanged += ExitNodeBoxTextChanged;
            cb1.PropertyChanged += ConfigBoxTextChanged;
            cb2.PropertyChanged += ConfigBoxTextChanged;
            _customConfigInputAttached = true;
        }

        private async Task ExitNodePasteAsync(string text)
        {
            if (await ApplyExitNodeRawAsync(text))
            {
                _exitNodeDirty = false;
                UpdateExitNodeSubmitVisibility();
            }
        }

        private Task OnSlotPasteAsync(int slot, string text) => ApplyCustomConfigRawAsync(slot, text);

        private Task OnSavedImportPasteAsync(string text)
        {
            var box = this.FindControl<TextBox>("txtSavedConfigImport");
            if (box != null) box.Text = (text ?? "").Trim();
            return Task.CompletedTask;
        }

        internal void ApplyCustomConfigTitles() => ApplyCustomConfigTitles(false);

        internal void ApplyCustomConfigTitles(bool force) => ConfigBoxes.ShowAll(force);

        internal async Task<bool> ApplyCustomConfigRawAsync(int slot, string raw)
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return false;
            EnsureCustomConfigDraft();
            raw = (raw ?? "").Trim();
            if (raw.Length == 0)
            {
                SetConfigRaw(cfg, slot, "");
                ShowCustomConfigSlot(slot, "");
                ResetStagedAdapter(slot);
                MarkCustomConfigsTextDirty();
                return true;
            }
            var verdict = await ConfigIntake.AcceptAsync(raw, cfg, ConfigTarget.Xray, AppStrings.PaneCustomConfig);
            if (!verdict.Accepted)
            {
                CrimsonX.Services.SimpleLogger.Log($"[Paste] {AppStrings.PaneCustomConfig}: {verdict.Reason}");
                MainWindow.Instance?.ShowToast(verdict.Toast, ToastKind.Error);
                return false;
            }
            if (verdict.Toast.Length > 0) MainWindow.Instance?.ShowToast(verdict.Toast, ToastKind.Error);
            if (verdict.Raw.Length > 0) raw = verdict.Raw;
            SetConfigRaw(cfg, slot, raw);
            RefreshConfigCombos();
            RefreshSavedConfigs();
            ShowCustomConfigSlot(slot, raw);
            MarkCustomConfigsTextDirty();
            return true;
        }

        internal void ShowCustomConfigSlotsFromConfig()
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return;
            ShowCustomConfigSlot(1, cfg.CustomConfig1 ?? "");
            ShowCustomConfigSlot(2, cfg.CustomConfig2 ?? "");
        }

        private void ShowCustomConfigSlot(int slot, string raw)
        {
            ConfigBoxes.Show(SlotBoxKey(slot), raw);
            ConfigBoxes.Show(QuickSlotBoxKey(slot), raw);
        }

        private string ResolveCustomConfigRaw(int slot) => ResolveCustomConfigRaw(SlotBoxKey(slot));

        private string ResolveCustomConfigRaw(string key)
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return "";
            EnsureCustomConfigDraft();
            return ConfigBoxes.Resolve(key,
                visible => AdoptableConfigText(visible),
                visible => SavedConfigRawForTitle(cfg, visible));
        }

        private static bool AdoptableConfigText(string visible)
            => AppCustomConfigStore.CanStore(visible)
            || ConfigConverter.TryAcceptFor(visible, ConfigTarget.Xray, out _);

        private static string SavedConfigRawForTitle(AppConfig cfg, string visible)
        {
            if (cfg == null || visible.Length == 0) return "";
            foreach (var entry in AppCustomConfigStore.Load(cfg))
            {
                if (entry == null) continue;
                if (string.Equals((entry.Label ?? "").Trim(), visible, StringComparison.Ordinal))
                    return (entry.Raw ?? "").Trim();
            }
            return "";
        }

        private async Task<bool> AcceptTypedConfigAsync(string key)
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return true;
            EnsureCustomConfigDraft();
            string visible = ConfigBoxes.Text(key);
            if (visible.Length == 0) return true;
            string stored = (ReadConfigBoxRaw(key) ?? "").Trim();
            if (stored.Length > 0
                && (string.Equals(visible, stored, StringComparison.Ordinal)
                 || string.Equals(visible, ConfigBoxTitle(stored), StringComparison.Ordinal)))
                return true;
            if (SavedConfigRawForTitle(cfg, visible).Length > 0) return true;
            var verdict = await ConfigIntake.AcceptAsync(visible, cfg, ConfigTarget.Xray, AppStrings.PaneCustomConfig);
            if (!verdict.Accepted)
            {
                CrimsonX.Services.SimpleLogger.Log($"[Submit] {AppStrings.PaneCustomConfig}: {verdict.Reason}");
                MainWindow.Instance?.ShowToast(verdict.Toast, ToastKind.Error);
                return false;
            }
            if (verdict.Toast.Length > 0) MainWindow.Instance?.ShowToast(verdict.Toast, ToastKind.Error);
            int slot = SlotOfKey(key);
            SetConfigRaw(cfg, slot, verdict.Raw.Length > 0 ? verdict.Raw : visible);
            ShowCustomConfigSlot(slot, GetConfigRaw(cfg, slot));
            MarkCustomConfigsTextDirty();
            return true;
        }

        private static int SlotOfKey(string key)
            => string.Equals(key, SlotBoxKey(2), StringComparison.Ordinal)
            || string.Equals(key, QuickSlotBoxKey(2), StringComparison.Ordinal) ? 2 : 1;

        internal void ResolveCustomConfigSlots() => ResolveCustomConfigSlots(SlotBoxKey(1), SlotBoxKey(2));

        internal void ResolveCustomConfigSlots(string key1, string key2, bool rejected1 = false, bool rejected2 = false)
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return;
            EnsureCustomConfigDraft();
            for (int slot = 1; slot <= 2; slot++)
            {
                if (slot == 1 ? rejected1 : rejected2) continue;
                string key = slot == 2 ? key2 : key1;
                string resolved = ResolveCustomConfigRaw(key);
                bool cleared = ConfigBoxes.IsEmpty(key);
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
                string committed = slot == 2 ? cfg.CustomConfig2 ?? "" : cfg.CustomConfig1 ?? "";
                bool draftMatches = _customConfigDraftActive && stored.Length > 0 && AppCustomConfigStore.SameConfig(stored, raw);
                bool committedMatches = committed.Length > 0 && AppCustomConfigStore.SameConfig(committed, raw);
                if (!draftMatches && !committedMatches) continue;
                if (draftMatches)
                {
                    if (slot == 2) _draftConfig2 = "";
                    else _draftConfig1 = "";
                }
                if (committedMatches)
                    SetConfigRawCommitted(cfg, slot, "");
                ShowCustomConfigSlot(slot, GetConfigRaw(cfg, slot));
                if (GetConfigRaw(cfg, slot).Length == 0)
                    ClearPerConfigAdapterForSlot(slot);
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

        private void btnCustomConfigImport1_Click(object? sender, RoutedEventArgs e)
            => CrimsonX.Services.BackgroundTask.RunOnceOnUi("settings.custom-config.import1", () => ImportConfigIntoSlotAsync(1));

        private void btnCustomConfigImport2_Click(object? sender, RoutedEventArgs e)
            => CrimsonX.Services.BackgroundTask.RunOnceOnUi("settings.custom-config.import2", () => ImportConfigIntoSlotAsync(2));

        private async Task ImportConfigIntoSlotAsync(int slot)
        {
            string text = await PickConfigFileTextAsync();
            if (text.Length > 0) await ApplyCustomConfigRawAsync(slot, text);
        }

        internal void SavedConfigBrowse_Click(object? sender, RoutedEventArgs e)
            => CrimsonX.Services.BackgroundTask.RunOnceOnUi("settings.saved-config.browse", () => SavedConfigBrowseAsync());

        internal async Task SavedConfigBrowseAsync()
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return;
            string text = await PickConfigFileTextAsync();
            if (text.Length == 0) return;
            switch (AppCustomConfigStore.Store(cfg, text, out string label, out string reason))
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
                    MainWindow.Instance?.ShowToast(AppStrings.ToastConfigUnreadable + ConfigValidator.RefusalSuffix(reason), ToastKind.Error);
                    break;
            }
        }

        internal async Task<string> PickConfigFileTextAsync()
        {
            try
            {
                var top = TopLevel.GetTopLevel(this) ?? MainWindow.Instance;
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
                box.PropertyChanged += ConfigBoxTextChanged;
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

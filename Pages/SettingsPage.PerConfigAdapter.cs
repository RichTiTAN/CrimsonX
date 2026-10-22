using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CrimsonX.Localization;
using CrimsonX.Models;

namespace CrimsonX.Pages
{
    public partial class SettingsPage
    {
        // ── Per-config adapters ──

        private static string PerConfigAdapterComboName(int slot) =>
            slot == 2 ? "cmbCustomConfig2Adapter" : "cmbCustomConfig1Adapter";

        private static string PerConfigAdapterPick(AppConfig? cfg, int slot) =>
            cfg == null ? "" : ((slot == 2 ? cfg.CustomConfig2Adapter : cfg.CustomConfig1Adapter) ?? "").Trim();

        private static void StorePerConfigAdapterPick(AppConfig? cfg, int slot, string pick)
        {
            if (cfg == null) return;
            if (slot == 2) cfg.CustomConfig2Adapter = pick ?? "";
            else cfg.CustomConfig1Adapter = pick ?? "";
        }

        internal void SyncPerConfigAdapterPanel()
        {
            var main = MainWindow.Instance;
            var cfg = main?.Config;
            if (main == null || cfg == null) return;
            PrunePerConfigAdapterConfig();
            if (!cfg.EnableCustomConfigs && cfg.EnablePerConfigAdapters)
            {
                LeavePerConfigMode(cfg);
                main.RequestConfigSave();
            }
            bool wasInit = _isInitializingSettings;
            _isInitializingSettings = true;
            try
            {
                var tog = this.FindControl<ToggleSwitch>("togPerConfigAdapter");
                if (tog != null)
                {
                    tog.IsEnabled = cfg.EnableCustomConfigs;
                    tog.IsChecked = cfg.EnableCustomConfigs && cfg.EnablePerConfigAdapters;
                }
                ApplyPerConfigAdapterVisibility();
                RefreshPerConfigAdapterCombos();
            }
            finally
            {
                _isInitializingSettings = wasInit;
            }
            RecalculateCustomConfigsDirty();
        }

        internal void ApplyPerConfigAdapterLanguage()
        {
            AppStrings.Apply(this.FindControl<TextBlock>("lblPerConfigAdapterTitle"), AppStrings.PerConfigAdapterTitle);
            AppStrings.ApplyToolTip(this.FindControl<TextBlock>("lblPerConfigAdapterTitle"), AppStrings.TtPerConfigAdapter);
            AppStrings.ApplyToolTip(this.FindControl<ToggleSwitch>("togPerConfigAdapter"), AppStrings.TtPerConfigAdapter);
            var cb1 = this.FindControl<ComboBox>("cmbCustomConfig1Adapter");
            if (cb1 != null) cb1.PlaceholderText = AppStrings.PerConfigAdapterSlot(1);
            var cb2 = this.FindControl<ComboBox>("cmbCustomConfig2Adapter");
            if (cb2 != null) cb2.PlaceholderText = AppStrings.PerConfigAdapterSlot(2);
            AppStrings.Apply(this.FindControl<TextBlock>("lblCustomConfig1AdapterTag"), AppStrings.AdapterTag);
            AppStrings.Apply(this.FindControl<TextBlock>("lblCustomConfig2AdapterTag"), AppStrings.AdapterTag);
            AppStrings.ApplyBtn(this.FindControl<Button>("btnCustomConfig1Scan"), AppStrings.ScanAdapters);
            AppStrings.ApplyBtn(this.FindControl<Button>("btnCustomConfig2Scan"), AppStrings.ScanAdapters);
            RefreshPerConfigAdapterCombos();
        }

        private void btnPerConfigScanAdapters_Click(object? sender, RoutedEventArgs e) => ScanAdapters();

        private void ApplyPerConfigAdapterVisibility()
        {
            var row = this.FindControl<Border>("rowPerConfigAdapters");
            if (row == null) return;
            var tog = this.FindControl<ToggleSwitch>("togPerConfigAdapter");
            bool on = tog?.IsChecked == true;
            row.MaxHeight = on ? 36 : 0;
            row.Opacity = on ? 1 : 0;
            var grid = this.FindControl<Grid>("pnlPerConfigAdapters");
            if (grid != null) grid.IsEnabled = on;
        }

        private void RefreshPerConfigAdapterCombos()
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return;
            bool wasInit = _isInitializingSettings;
            _isInitializingSettings = true;
            try
            {
                var all = AdapterItems();
                string pick1 = PerConfigAdapterPick(cfg, 1);
                string pick2 = PerConfigAdapterPick(cfg, 2);
                if (pick1.Length > 0 && string.Equals(pick1, pick2, StringComparison.Ordinal))
                {
                    StorePerConfigAdapterPick(cfg, 2, "");
                    pick2 = "";
                    MainWindow.Instance?.RequestConfigSave();
                }
                for (int slot = 1; slot <= 2; slot++)
                {
                    var cmb = this.FindControl<ComboBox>(PerConfigAdapterComboName(slot));
                    if (cmb == null) continue;
                    string keep  = slot == 2 ? pick2 : pick1;
                    string other = slot == 2 ? pick1 : pick2;
                    var items = new List<string> { AppStrings.AdapterDefault };
                    foreach (string adapter in all)
                    {
                        if (!string.IsNullOrWhiteSpace(other) && string.Equals(adapter, other, StringComparison.Ordinal)) continue;
                        items.Add(adapter);
                    }
                    cmb.ItemsSource = items;
                    int index = 0;
                    for (int i = 1; i < items.Count; i++)
                    {
                        if (string.Equals(items[i], keep, StringComparison.Ordinal)) { index = i; break; }
                    }
                    cmb.SelectedIndex = index;
                }
            }
            finally
            {
                _isInitializingSettings = wasInit;
            }
        }

        internal void PrunePerConfigAdapterConfig()
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return;
            var available = AdapterItems();
            bool changed = false;
            for (int slot = 1; slot <= 2; slot++)
            {
                string pick = PerConfigAdapterPick(cfg, slot);
                if (pick.Length == 0 || available.Contains(pick)) continue;
                StorePerConfigAdapterPick(cfg, slot, "");
                changed = true;
            }
            if (changed) MainWindow.Instance.RequestConfigSave();
        }

        internal void ClearPerConfigAdapterForSlot(int slot)
        {
            var main = MainWindow.Instance;
            var cfg = main?.Config;
            if (main == null || cfg == null) return;
            if (PerConfigAdapterPick(cfg, slot).Length == 0) return;
            StorePerConfigAdapterPick(cfg, slot, "");
            RefreshPerConfigAdapterCombos();
            main.RequestConfigSave();
        }

        private void togPerConfigAdapter_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            if (_isInitializingSettings) return;
            var tog = sender as ToggleSwitch;
            if (tog == null) return;
            var main = MainWindow.Instance;
            var cfg = main?.Config;
            if (main == null || cfg == null) return;
            if (tog.IsChecked == true && !cfg.EnableCustomConfigs)
            {
                Dispatcher.UIThread.Post(() => { tog.IsChecked = false; });
                return;
            }
            ApplyPerConfigAdapterVisibility();
            RecalculateCustomConfigsDirty();
        }

        private void NotifyPerConfigAdapterChanged()
        {
            if (MainWindow.Instance?.State.IsEngineRunning == true)
                MainWindow.Instance.ShowToast(AppStrings.ToastReconnectChanges);
        }

        internal static void EnterPerConfigMode(AppConfig cfg)
        {
            if (cfg == null) return;
            if (!cfg.EnablePerConfigAdapters)
                cfg.AdapterBindingBeforePerConfig = cfg.EnableAdapterBinding;
            cfg.EnablePerConfigAdapters = true;
            cfg.EnableAdapterBinding = false;
            cfg.EnableLoadBalanceAdapters = false;
        }

        internal static bool LeavePerConfigMode(AppConfig cfg)
        {
            if (cfg == null || !cfg.EnablePerConfigAdapters) return false;
            cfg.EnablePerConfigAdapters = false;
            bool restore = cfg.AdapterBindingBeforePerConfig
                           && !string.IsNullOrWhiteSpace(cfg.SelectedAdapterIp);
            cfg.AdapterBindingBeforePerConfig = false;
            if (restore) cfg.EnableAdapterBinding = true;
            return restore;
        }

        private bool ClearPerConfigAdapterMode(bool save, bool restoreBinding = true)
        {
            var main = MainWindow.Instance;
            var cfg = main?.Config;
            if (main == null || cfg == null || !cfg.EnablePerConfigAdapters) return false;
            bool restored;
            if (restoreBinding)
            {
                restored = LeavePerConfigMode(cfg);
            }
            else
            {
                cfg.EnablePerConfigAdapters = false;
                cfg.AdapterBindingBeforePerConfig = false;
                restored = false;
            }
            var tog = this.FindControl<ToggleSwitch>("togPerConfigAdapter");
            if (tog != null)
            {
                bool wasInit = _isInitializingSettings;
                _isInitializingSettings = true;
                try { tog.IsChecked = false; }
                finally { _isInitializingSettings = wasInit; }
            }
            if (restored)
            {
                var bindTog = this.FindControl<ToggleSwitch>("togAdapterBinding");
                if (bindTog != null)
                {
                    bool wasInit = _isInitializingSettings;
                    _isInitializingSettings = true;
                    try { bindTog.IsChecked = true; }
                    finally { _isInitializingSettings = wasInit; }
                }
            }
            ApplyPerConfigAdapterVisibility();
            RecalculateCustomConfigsDirty();
            if (save) main.RequestConfigSave();
            return restored;
        }

        private void cmbCustomConfigAdapter_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isInitializingSettings) return;
            if (sender is not ComboBox) return;
            RecalculateCustomConfigsDirty();
        }
    }
}

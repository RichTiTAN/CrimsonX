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
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using CrimsonX.Models;
using CrimsonX.Services;

namespace CrimsonX.Pages
{
    public sealed class CountryComboItem
    {
        public string Code { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public IImage? FlagImage { get; set; }

        public bool HasFlag => FlagImage != null;

        public override string ToString() => DisplayName;
    }

    public partial class SettingsPage
    {
        private bool _exitNodeDefaultMode;
        private bool _exitNodeDirty;
        private bool _exitNodeExpanded;
        private bool _exitNodeNoneSelected;

        private void MarkExitNodeDirty()
        {
            if (_isInitializingSettings) return;
            _exitNodeDirty = true;
            UpdateExitNodeSubmitVisibility();
        }

        private void UpdateExitNodeSubmitVisibility()
        {
            var submit = this.FindControl<Button>("btnXrayExitNodeSubmit");
            if (submit != null)
                submit.IsVisible = _exitNodeDirty && _exitNodeExpanded && !_isInitializingSettings;
        }

        internal static List<CountryComboItem> BuildCountryItems(string keepCode = "")
        {
            string keep = (keepCode ?? "").Trim();
            var allowed = CountryExitFeed.AllowedContinents(MainWindow.Instance?.Config);
            return CountryExitFeed.Nodes
                .Where(node => CountryExitFeed.IsVisible(node, allowed)
                               || (keep.Length > 0 && string.Equals(node.Code, keep, StringComparison.OrdinalIgnoreCase)))
                .Select(node => new CountryComboItem
                {
                    Code = node.Code,
                    DisplayName = CountryExitFeed.DisplayName(node),
                    FlagImage = CrimsonX.Controls.LocationFlags.For(node.Code),
                }).ToList();
        }

        internal void RefreshExitNodePane()
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return;
            bool keepStagedMode = _exitNodeDirty;
            bool wasInit = _isInitializingSettings;
            _isInitializingSettings = true;
            try
            {
                var tog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togXrayExitNode");
                if (tog != null) tog.IsChecked = cfg.EnableV2rayChain;
                if (!keepStagedMode)
                    _exitNodeDefaultMode = string.Equals(cfg.ExitNodeSource, "DEFAULT", StringComparison.OrdinalIgnoreCase);
                var btnDefault = this.FindControl<global::Avalonia.Controls.Primitives.ToggleButton>("btnExitNodeDefault");
                var btnCustom = this.FindControl<global::Avalonia.Controls.Primitives.ToggleButton>("btnExitNodeCustom");
                if (btnDefault != null) btnDefault.IsChecked = _exitNodeDefaultMode;
                if (btnCustom != null) btnCustom.IsChecked = !_exitNodeDefaultMode;
                var panDefault = this.FindControl<Grid>("panExitNodeDefault");
                var panCustom = this.FindControl<Grid>("panExitNodeCustom");
                if (panDefault != null) panDefault.IsVisible = _exitNodeDefaultMode;
                if (panCustom != null) panCustom.IsVisible = !_exitNodeDefaultMode;
                RefreshCountryComboSelection(cfg, preserveCurrent: keepStagedMode);
            }
            finally
            {
                _isInitializingSettings = wasInit;
            }
            UpdateExitNodeSubmitVisibility();
        }

        private void ExitNodeModeDefault_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => SetExitNodeMode(defaultMode: true);

        private void ExitNodeModeCustom_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => SetExitNodeMode(defaultMode: false);

        private void SetExitNodeMode(bool defaultMode)
        {
            if (_isInitializingSettings) return;
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return;
            var btnDefault = this.FindControl<global::Avalonia.Controls.Primitives.ToggleButton>("btnExitNodeDefault");
            var btnCustom = this.FindControl<global::Avalonia.Controls.Primitives.ToggleButton>("btnExitNodeCustom");
            if (_exitNodeDefaultMode == defaultMode)
            {
                if (btnDefault != null) btnDefault.IsChecked = defaultMode;
                if (btnCustom != null) btnCustom.IsChecked = !defaultMode;
                return;
            }

            _exitNodeDefaultMode = defaultMode;
            _exitNodeNoneSelected = false;
            if (btnDefault != null) btnDefault.IsChecked = defaultMode;
            if (btnCustom != null) btnCustom.IsChecked = !defaultMode;
            var panDefault = this.FindControl<Grid>("panExitNodeDefault");
            var panCustom = this.FindControl<Grid>("panExitNodeCustom");
            if (panDefault != null) panDefault.IsVisible = defaultMode;
            if (panCustom != null) panCustom.IsVisible = !defaultMode;

            if (defaultMode)
            {
                RefreshCountryComboSelection(cfg);
            }
            else
            {
                string custom = string.Equals(cfg.ExitNodeSource, "CUSTOM", StringComparison.OrdinalIgnoreCase)
                    ? cfg.V2rayChainJson ?? ""
                    : cfg.ExitNodeCustomJson ?? "";
                ShowExitNodeRaw(custom);
            }
            MarkExitNodeDirty();
        }

        private void RefreshCountryComboSelection(AppConfig cfg, bool preserveCurrent = true)
        {
            var combo = this.FindControl<ComboBox>("cbExitNodeCountry");
            if (combo == null) return;
            bool wasInit = _isInitializingSettings;
            _isInitializingSettings = true;
            try
            {
                string keep = preserveCurrent ? (combo.SelectedItem as CountryComboItem)?.Code ?? "" : "";
                if (keep.Length == 0) keep = cfg.ExitNodeCountry;
                var items = BuildCountryItems(keep);
                combo.ItemsSource = items;
                int index = items.FindIndex(i => string.Equals(i.Code, keep, StringComparison.OrdinalIgnoreCase));
                if (index < 0 && items.Count > 0) index = 0;
                combo.SelectedIndex = index;
            }
            finally
            {
                _isInitializingSettings = wasInit;
            }
        }

        private void ExitNodeCountry_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
        {
            if (_isInitializingSettings) return;
            if (sender is global::Avalonia.Controls.ComboBox cb && cb.SelectedItem is CountryComboItem)
                MarkExitNodeDirty();
        }

        private void btnXrayExitNodeSubmit_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            _ = ApplyExitNodeSubmitAsync();
        }

        private async Task ApplyExitNodeSubmitAsync()
        {
            bool applied = _exitNodeDefaultMode
                ? ApplyDefaultExitNode()
                : await SubmitCustomExitNodeAsync();
            if (!applied) return;
            _exitNodeDirty = false;
            CollapseExitNodePane(discardPending: false);
        }

        private bool ApplyDefaultExitNode()
        {
            var window = MainWindow.Instance;
            var cfg = window?.Config;
            var combo = this.FindControl<ComboBox>("cbExitNodeCountry");
            if (window == null || cfg == null || combo?.SelectedItem is not CountryComboItem item) return false;
            var node = CountryExitFeed.NodeFor(item.Code);
            if (node == null) return false;
            CountryExitFeed.SelectCountry(cfg, node);
            bool wasInit = _isInitializingSettings;
            _isInitializingSettings = true;
            try
            {
                var tog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togXrayExitNode");
                if (tog != null) tog.IsChecked = true;
            }
            finally { _isInitializingSettings = wasInit; }
            window.RequestConfigSave();
            if (window.State.IsEngineRunning) window.SmartRestartXray();
            RefreshExitNodePane();
            return true;
        }

        private async Task<bool> SubmitCustomExitNodeAsync()
        {
            var cfg = MainWindow.Instance?.Config;
            var box = this.FindControl<ComboBox>("cbXrayExitNode");
            if (cfg == null || box == null) return false;
            bool none = _exitNodeNoneSelected || box.SelectedIndex == 0
                || string.Equals((box.Text ?? "").Trim(), CrimsonX.Localization.AppStrings.CustomProxyNone, StringComparison.Ordinal);
            string raw = none ? "" : VisibleExitNodeRaw(cfg);
            if (none || raw.Length == 0)
            {
                ClearCustomExitNode();
                return true;
            }
            bool applied = await ApplyExitNodeRawAsync(raw);
            if (!applied) return false;
            cfg.ExitNodeSource = "CUSTOM";
            cfg.ExitNodeCustomJson = cfg.V2rayChainJson ?? raw;
            cfg.ExitNodeCustomEnabled = cfg.EnableV2rayChain;
            cfg.ExitNodeSuspended = false;
            cfg.ExitNodeAuto = false;
            MainWindow.Instance?.RequestConfigSave();
            RefreshExitNodePane();
            return true;
        }

        private string VisibleExitNodeRaw(AppConfig cfg)
        {
            var box = this.FindControl<ComboBox>("cbXrayExitNode");
            string visible = (box?.Text ?? "").Trim();
            return visible.Length == 0 ? "" : ResolveExitNodeRaw(cfg, visible);
        }

        private string ResolveExitNodeRaw(AppConfig cfg, string visible)
        {
            var entry = _configComboEntries.FirstOrDefault(e =>
                string.Equals((e.Raw ?? "").Trim(), visible, StringComparison.Ordinal)
                || string.Equals((e.Label ?? "").Trim(), visible, StringComparison.Ordinal));
            if (entry != null) return (entry.Raw ?? "").Trim();
            string stored = (cfg.V2rayChainJson ?? "").Trim();
            if (stored.Length > 0
                && (string.Equals(visible, stored, StringComparison.Ordinal)
                    || string.Equals(visible, CrimsonX.Services.AppCustomConfigStore.LabelFor(cfg, stored), StringComparison.Ordinal)))
                return stored;
            return visible;
        }

        private void ClearCustomExitNode()
        {
            var window = MainWindow.Instance;
            var cfg = window?.Config;
            if (window == null || cfg == null) return;
            bool wasOn = cfg.EnableV2rayChain;
            bool changed = CountryExitFeed.ClearCustom(cfg);
            bool wasInit = _isInitializingSettings;
            _isInitializingSettings = true;
            try
            {
                var box = this.FindControl<ComboBox>("cbXrayExitNode");
                if (box != null)
                {
                    box.SelectedIndex = -1;
                    box.Text = "";
                }
                var tog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togXrayExitNode");
                if (tog != null) tog.IsChecked = false;
            }
            finally { _isInitializingSettings = wasInit; }
            ShowExitNodeRaw("");
            _exitNodeDirty = false;
            _exitNodeNoneSelected = false;
            UpdateExitNodeSubmitVisibility();
            window.RequestConfigSave();
            if (changed && wasOn && window.State.IsEngineRunning) window.SmartRestartXray();
            CrimsonX.Services.SimpleLogger.Log("[ExitNode] Custom exit node cleared (NONE).");
        }
    }
}

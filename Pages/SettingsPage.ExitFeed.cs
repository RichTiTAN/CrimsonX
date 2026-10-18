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
using Avalonia.Controls;
using Avalonia.Media;
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
            bool wasInit = _isInitializingSettings;
            _isInitializingSettings = true;
            try
            {
                var tog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togXrayExitNode");
                if (tog != null) tog.IsChecked = cfg.EnableV2rayChain;
                _exitNodeDefaultMode = string.Equals(cfg.ExitNodeSource, "DEFAULT", StringComparison.OrdinalIgnoreCase);
                var btnDefault = this.FindControl<global::Avalonia.Controls.Primitives.ToggleButton>("btnExitNodeDefault");
                var btnCustom = this.FindControl<global::Avalonia.Controls.Primitives.ToggleButton>("btnExitNodeCustom");
                if (btnDefault != null) btnDefault.IsChecked = _exitNodeDefaultMode;
                if (btnCustom != null) btnCustom.IsChecked = !_exitNodeDefaultMode;
                var panDefault = this.FindControl<Grid>("panExitNodeDefault");
                var panCustom = this.FindControl<Grid>("panExitNodeCustom");
                if (panDefault != null) panDefault.IsVisible = _exitNodeDefaultMode;
                if (panCustom != null) panCustom.IsVisible = !_exitNodeDefaultMode;
                var combo = this.FindControl<ComboBox>("cbExitNodeCountry");
                if (combo != null)
                {
                    var items = BuildCountryItems(cfg.ExitNodeCountry);
                    combo.ItemsSource = items;
                    combo.SelectedIndex = items.FindIndex(i =>
                        string.Equals(i.Code, cfg.ExitNodeCountry, StringComparison.OrdinalIgnoreCase));
                }
            }
            finally
            {
                _isInitializingSettings = wasInit;
            }
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
            bool wasOn = cfg.EnableV2rayChain;
            bool wasCustom = string.Equals(cfg.ExitNodeSource, "CUSTOM", StringComparison.OrdinalIgnoreCase);
            string wasLink = cfg.V2rayChainJson ?? "";
            if (defaultMode)
            {
                if (!string.Equals(cfg.ExitNodeSource, "DEFAULT", StringComparison.OrdinalIgnoreCase))
                {
                    cfg.ExitNodeCustomJson = cfg.V2rayChainJson ?? "";
                    cfg.ExitNodeCustomEnabled = wasOn;
                }
                cfg.ExitNodeSource = "DEFAULT";
                if (string.IsNullOrWhiteSpace(cfg.ExitNodeCountry))
                {
                    var allowed = CountryExitFeed.AllowedContinents(cfg);
                    cfg.ExitNodeCountry = CountryExitFeed.Nodes
                        .FirstOrDefault(n => CountryExitFeed.IsVisible(n, allowed))?.Code ?? "";
                }
                if (wasCustom && wasOn)
                {
                    var handOver = CountryExitFeed.NodeFor(cfg.ExitNodeCountry);
                    if (handOver != null) CountryExitFeed.SelectCountry(cfg, handOver);
                }
            }
            else
            {
                CountryExitFeed.SelectCustom(cfg);
                bool wasInit = _isInitializingSettings;
                _isInitializingSettings = true;
                try
                {
                    var tog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togXrayExitNode");
                    if (tog != null) tog.IsChecked = cfg.EnableV2rayChain;
                }
                finally { _isInitializingSettings = wasInit; }
                ShowExitNodeRaw(cfg.V2rayChainJson);
            }
            bool chainChanged = wasOn != cfg.EnableV2rayChain
                             || (cfg.EnableV2rayChain && !string.Equals(wasLink, cfg.V2rayChainJson ?? "", StringComparison.Ordinal));
            MainWindow.Instance?.RequestConfigSave();
            RefreshExitNodePane();
            if (chainChanged && MainWindow.Instance?.State.IsEngineRunning == true) MainWindow.Instance.SmartRestartXray();
        }

        private void ExitNodeCountry_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
        {
        }

        private void btnExitNodeSubmit_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            var window = MainWindow.Instance;
            var cfg = window?.Config;
            var combo = this.FindControl<ComboBox>("cbExitNodeCountry");
            if (cfg == null || combo?.SelectedItem is not CountryComboItem item) return;
            var node = CountryExitFeed.NodeFor(item.Code);
            if (node == null) return;
            if (!CountryExitFeed.SelectCountry(cfg, node)) return;
            bool wasInit = _isInitializingSettings;
            _isInitializingSettings = true;
            try
            {
                var tog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togXrayExitNode");
                if (tog != null) tog.IsChecked = true;
            }
            finally { _isInitializingSettings = wasInit; }
            window!.RequestConfigSave();
            if (window.State.IsEngineRunning) window.SmartRestartXray();
            RefreshExitNodePane();
        }
    }
}

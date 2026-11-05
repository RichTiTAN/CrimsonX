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

using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using CrimsonX.Dialogs;
using CrimsonX.Localization;
using CrimsonX.Services;

namespace CrimsonX.Pages
{
    public partial class SettingsPage
    {
        internal void ApplyIpsecProfileVisibility()
        {
            var toggle = this.FindControl<ToggleButton>("togShowIpsec");
            bool on = toggle?.IsChecked == true;
            var first = this.FindControl<Border>("panCustomConfigIpsec1");
            if (first != null) first.IsVisible = on;
            var second = this.FindControl<Border>("panCustomConfigIpsec2");
            if (second != null) second.IsVisible = on;
            if (toggle != null) toggle.Content = on ? AppStrings.HideIpsec : AppStrings.ShowIpsec;
        }

        private void togShowIpsec_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            var cfg = MainWindow.Instance?.Config;
            bool on = (sender as ToggleButton)?.IsChecked == true;
            if (cfg != null) cfg.ShowIpsecProfileButton = on;
            ApplyIpsecProfileVisibility();
            if (_isInitializingSettings) return;
            RecalculateCustomConfigsDirty();
            MainWindow.Instance?.RequestConfigSave();
        }

        private void btnCustomConfigIpsec1_Click(object? sender, RoutedEventArgs e)
            => BackgroundTask.RunOnceOnUi("settings.ipsec.1", () => BuildIpsecProfileAsync(1));

        private void btnCustomConfigIpsec2_Click(object? sender, RoutedEventArgs e)
            => BackgroundTask.RunOnceOnUi("settings.ipsec.2", () => BuildIpsecProfileAsync(2));

        private async Task BuildIpsecProfileAsync(int slot)
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return;
            if (!IpsecEngine.IsInstalled(cfg.SbDir))
            {
                MainWindow.Instance?.ShowToast(AppStrings.ToastIpsecEngineMissing, ToastKind.Error);
                return;
            }
            string existing = slot == 2 ? cfg.CustomConfig2 ?? "" : cfg.CustomConfig1 ?? "";
            string? profile = await IpsecProfileDialog.ShowAsync(existing);
            if (string.IsNullOrWhiteSpace(profile)) return;
            await ApplyCustomConfigRawAsync(slot, profile!);
        }
    }
}

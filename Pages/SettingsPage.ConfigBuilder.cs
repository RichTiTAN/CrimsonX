/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System.Threading.Tasks;
using Avalonia.Interactivity;
using CrimsonX.Dialogs;
using CrimsonX.Localization;
using CrimsonX.Services;

namespace CrimsonX.Pages
{
    public partial class SettingsPage
    {
        private void btnCustomConfigBuild1_Click(object? sender, RoutedEventArgs e) => OpenConfigBuilder(1, edit: false);

        private void btnCustomConfigBuild2_Click(object? sender, RoutedEventArgs e) => OpenConfigBuilder(2, edit: false);

        private void btnCustomConfigEdit1_Click(object? sender, RoutedEventArgs e) => OpenConfigBuilder(1, edit: true);

        private void btnCustomConfigEdit2_Click(object? sender, RoutedEventArgs e) => OpenConfigBuilder(2, edit: true);

        private void OpenConfigBuilder(int slot, bool edit)
            => BackgroundTask.RunOnceOnUi($"settings.config-builder.{slot}.{edit}", () => OpenConfigBuilderAsync(slot, edit));

        private async Task OpenConfigBuilderAsync(int slot, bool edit)
        {
            if (!edit)
            {
                string? built = await ConfigBuilderDialog.ShowAsync("", ConfigBuilderScope.CustomConfig);
                if (string.IsNullOrWhiteSpace(built)) return;
                await ApplyCustomConfigRawAsync(slot, built);
                return;
            }

            string existing = ResolveCustomConfigRaw(slot);
            if (existing.Length == 0)
            {
                MainWindow.Instance?.ShowToast(AppStrings.CustomProxyEmpty, ToastKind.Error);
                return;
            }
            var cfg = MainWindow.Instance?.Config;
            string preferredLabel = cfg == null ? "" : AppCustomConfigStore.EditableLabelFor(cfg, existing);
            string? raw = await ShowConfigEditorAsync(existing, ConfigBuilderScope.CustomConfig, preferredLabel);
            if (string.IsNullOrWhiteSpace(raw)) return;
            await ApplyCustomConfigRawAsync(slot, raw);
        }
    }
}

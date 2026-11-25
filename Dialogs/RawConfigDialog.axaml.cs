/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CrimsonX.Localization;
using CrimsonX.Services;

namespace CrimsonX.Dialogs
{
    public partial class RawConfigDialog : Window
    {
        private readonly ConfigBuilderScope _scope;

        public RawConfigDialog() : this("", ConfigBuilderScope.CustomConfig)
        {
        }

        public RawConfigDialog(string existing, ConfigBuilderScope scope)
        {
            InitializeComponent();
            _scope = scope;
            FlowDirection = AppStrings.IsPersian ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            Title = AppStrings.RawEditorTitle;
            lblRawHint.Text = AppStrings.RawEditorHint;
            txtRawConfig.Text = existing ?? "";
            txtRawConfig.AttachedToVisualTree += (_, _) => txtRawConfig.Focus();
        }

        public static Task<string?> ShowAsync(string existing, ConfigBuilderScope scope)
        {
            var owner = MainWindow.Instance;
            if (owner == null) return Task.FromResult<string?>(null);
            return Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dialog = new RawConfigDialog(existing, scope);
                return await dialog.ShowDialog<string?>(owner);
            });
        }

        private void ShowError(string message)
        {
            lblRawError.Text = message;
            lblRawError.IsVisible = message.Length > 0;
        }

        private void Save_Click(object? sender, RoutedEventArgs e) => Accept();

        private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

        private void Dialog_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                Accept();
                e.Handled = true;
            }
        }

        private void Text_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            Accept();
            e.Handled = true;
        }

        private void Accept()
        {
            string raw = (txtRawConfig.Text ?? "").Trim();
            if (raw.Length == 0)
            {
                ShowError(AppStrings.RawEditorEmpty);
                return;
            }
            if (!ConfigBuilder.Validate(raw, _scope, out string error))
            {
                ShowError(error);
                return;
            }
            Close(raw);
        }
    }
}

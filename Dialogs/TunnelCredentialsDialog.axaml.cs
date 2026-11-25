/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CrimsonX.Services;
using AS = CrimsonX.Localization.AppStrings;

namespace CrimsonX.Dialogs
{
    public partial class TunnelCredentialsDialog : Window
    {
        private readonly string _label;
        private readonly string _user;
        private readonly string _password;

        public TunnelCredentialsDialog()
            : this("", "", "")
        {
        }

        public TunnelCredentialsDialog(string label, string user, string password)
        {
            _label = label ?? "";
            _user = user ?? "";
            _password = password ?? "";
            InitializeComponent();
            ApplyLanguage();
            var userBox = this.FindControl<TextBox>("txtTunnelUser");
            if (userBox != null)
            {
                userBox.Text = _user;
                userBox.AttachedToVisualTree += (_, __) => userBox.Focus();
            }
            var passBox = this.FindControl<TextBox>("txtTunnelPass");
            if (passBox != null) passBox.Text = _password;
        }

        private void ApplyLanguage()
        {
            FlowDirection = AS.IsPersian
                ? Avalonia.Media.FlowDirection.RightToLeft
                : Avalonia.Media.FlowDirection.LeftToRight;
            Title = AS.TunnelCredsTitle;
            AS.Apply(this.FindControl<TextBlock>("lblTunnelSub"),
                     _label.Length > 0 ? AS.TunnelCredsSubtitle + " " + _label : AS.TunnelCredsSubtitle);
            AS.Apply(this.FindControl<TextBlock>("lblTunnelUser"), AS.TunnelCredsUser);
            AS.Apply(this.FindControl<TextBlock>("lblTunnelPass"), AS.TunnelCredsPass);
            var remember = this.FindControl<CheckBox>("chkTunnelRemember");
            if (remember != null) remember.Content = AS.TunnelCredsRemember;
            var ok = this.FindControl<Button>("btnTunnelOk");
            if (ok != null) ok.Content = AS.StatusConnect;
            var cancel = this.FindControl<Button>("btnTunnelCancel");
            if (cancel != null) cancel.Content = AS.TunnelCredsCancel;
        }

        private void Ok_Click(object? sender, RoutedEventArgs e) => Accept();

        private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

        private void Dialog_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Accept();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
            }
        }

        private void Accept()
        {
            string user = this.FindControl<TextBox>("txtTunnelUser")?.Text?.Trim() ?? "";
            if (user.Length == 0) return;
            var remember = this.FindControl<CheckBox>("chkTunnelRemember");
            Close(new TunnelCredential
            {
                User = user,
                Password = this.FindControl<TextBox>("txtTunnelPass")?.Text ?? "",
                Remember = remember?.IsChecked == true
            });
        }
    }
}

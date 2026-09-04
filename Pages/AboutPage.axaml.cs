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

using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace CrimsonX.Pages;

public partial class AboutPage : UserControl
{
    internal static AboutPage? Instance { get; private set; }

    public AboutPage()
    {
        InitializeComponent();
        Instance = this;
        var lblVersion = this.FindControl<TextBlock>("lblVersion");
        if (lblVersion != null)
        {
            lblVersion.Text = Services.UpdateService.AppVersion;
        }
    }

    // ── External Links ──

    private void BtnGithub_Click(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/RichTiTAN") { UseShellExecute = true })?.Dispose();
    }

    private void BtnTelegram_Click(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://t.me/itsTitanVPN") { UseShellExecute = true })?.Dispose();
    }

    private void BtnOtherApps_Click(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/RichTiTAN/CrimsonOnion") { UseShellExecute = true })?.Dispose();
    }

    // ── Language & Localization ──

    public void ApplyLanguage()
    {
        var F = new System.Func<string, Avalonia.Controls.TextBlock>(name => this.FindControl<Avalonia.Controls.TextBlock>(name));
        CrimsonX.Localization.AppStrings.Apply(F("lblOtherApps"), CrimsonX.Localization.AppStrings.OtherApps);
    }

    // ── Copy Wallet Address ──

    private async void BtnCopyAddress_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Content is string address)
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(address);
                MainWindow.Instance.ShowToast(Localization.AppStrings.ToastAddressCopied, success: true);
            }
        }
    }

    // ── Update Check & Status ──

    private void BtnCheckUpdate_Click(object? sender, RoutedEventArgs e)
    {
        MainWindow.Instance.BtnCheckUpdate_Click(sender, e);
    }
    
    internal void SetUpdateStatus(string status)
    {
        var btnCheckUpdate = this.FindControl<Button>("btnCheckUpdate");
        if (btnCheckUpdate != null) btnCheckUpdate.Content = status;
    }

    // ── Localized Text Refresh ──

    internal void UpdateLocalization()
    {
        var Apply = new System.Action<TextBlock?, string>((tb, s) => { if (tb != null) tb.Text = s; });
        
        Apply(this.FindControl<TextBlock>("lblAboutVersion"), Localization.AppStrings.AboutVersion);
        Apply(this.FindControl<TextBlock>("lblAboutCreator"), Localization.AppStrings.AboutCreator);
        Apply(this.FindControl<TextBlock>("lblAboutLicense"), Localization.AppStrings.AboutLicense);
        Apply(this.FindControl<TextBlock>("lblDonations"), Localization.AppStrings.DonationsTitle);
        Apply(this.FindControl<TextBlock>("lblDonationsDesc"), Localization.AppStrings.DonationsDesc);
        
        var btnCheckUpdate = this.FindControl<Button>("btnCheckUpdate");
        if (btnCheckUpdate != null) btnCheckUpdate.Content = Localization.AppStrings.CheckForUpdates;
    }
}

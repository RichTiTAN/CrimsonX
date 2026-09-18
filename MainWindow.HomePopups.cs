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
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Input.Platform;
using CrimsonX.Models;
using CrimsonX.Services;
using CrimsonX.Localization;

namespace CrimsonX;

public partial class MainWindow
{
    // -- Home popup state (owns the Home language / LB popups) --
    private bool _wasLanguagePopupOpen = false;

    private bool _wasLbPolicyPopupOpen = false;

    private async Task ClosePopupAnimatedAsync()
    {
        bool closeLanguage = LanguagePopup != null && LanguagePopup.IsOpen;
        bool closeLbPolicy = LbPolicyPopup != null && LbPolicyPopup.IsOpen;

        if (!closeLanguage && !closeLbPolicy) return;

        if (closeLanguage && LanguagePopup?.Child is Border lBorder) lBorder.Classes.Remove("popupOpen");
        if (closeLbPolicy && LbPolicyPopup?.Child is Border lpBorder) lpBorder.Classes.Remove("popupOpen");

        await Task.Delay(200);

        if (closeLanguage && LanguagePopup != null) LanguagePopup.IsOpen = false;
        if (closeLbPolicy && LbPolicyPopup != null) LbPolicyPopup.IsOpen = false;
        
        bool anyPopupOpen = (LanguagePopup != null && LanguagePopup.IsOpen) || (LbPolicyPopup != null && LbPolicyPopup.IsOpen);
        if (!anyPopupOpen)
        {
            var sld = this.FindControl<Border>("SettingsLightDismiss");
            if (sld != null) sld.IsVisible = false;
            
            var panSettings = this.FindControl<Border>("panSettingsOverlay");
            var panSplit = this.FindControl<Border>("panSplitOverlay");
            var panAbout = this.FindControl<Border>("panAboutOverlay");
            if ((panSettings == null || !panSettings.IsVisible) &&
                (panSplit == null || !panSplit.IsVisible) &&
                (panAbout == null || !panAbout.IsVisible))
            {
                LightDismissOverlay.IsVisible = false;
            }
        }
    }

    // ── Language Selector Popup ──

internal async void BtnLanguage_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
        var target = sender as Control;
        bool isSelf = LanguagePopup != null && LanguagePopup.IsOpen && LanguagePopup.PlacementTarget == target;
        if (isSelf)
        {
            _ = ClosePopupAnimatedAsync();
            return;
        }

        if (LanguagePopup != null && LanguagePopup.IsOpen)
        {
            LanguagePopup.IsOpen = false;
            if (LanguagePopup.Child is Border oldBorder) oldBorder.Classes.Remove("popupOpen");
        }

        _ = ClosePopupAnimatedAsync();

        if (LanguagePopup != null)
        {
            LanguagePopup.PlacementTarget  = target;
            LanguagePopup.Placement        = PlacementMode.Bottom;
            LanguagePopup.HorizontalOffset = 0;
            LanguagePopup.VerticalOffset   = 5;
            LanguagePopup.IsOpen           = true;
            
            var sld = this.FindControl<Border>("SettingsLightDismiss");
            if (sld != null) sld.IsVisible = true;
            
            await Task.Delay(10);
            if (LanguagePopup.Child is Border border) border.Classes.Add("popupOpen");
        }
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
        }
    }

    internal void LanguageOption_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string lang)
        {
            var lbl = this.FindControl<TextBlock>("lblCurrentLanguage");
            if (lbl != null) lbl.Text = lang;

            _cfg.Language = lang;
            SaveConfig();
            ApplyLanguage();

            _ = ClosePopupAnimatedAsync();
        }
    }

    // ── Load-Balance Policy Popup ──

    internal async void BtnLbPolicy_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
        bool isSelf = LbPolicyPopup != null && LbPolicyPopup.IsOpen && LbPolicyPopup.PlacementTarget?.Name == "btnLbPolicy";
        if (isSelf)
        {
            _ = ClosePopupAnimatedAsync();
            return;
        }

        if (LbPolicyPopup != null && LbPolicyPopup.IsOpen)
        {
            LbPolicyPopup.IsOpen = false;
            if (LbPolicyPopup.Child is Border oldBorder) oldBorder.Classes.Remove("popupOpen");
        }

        _ = ClosePopupAnimatedAsync();

        if (LbPolicyPopup != null)
        {
            LbPolicyPopup.PlacementTarget  = this.FindControl<Control>("btnLbPolicy");
            LbPolicyPopup.Placement        = PlacementMode.Bottom;
            LbPolicyPopup.HorizontalOffset = 0;
            LbPolicyPopup.VerticalOffset   = 5;
            LbPolicyPopup.IsOpen           = true;

            var sld = this.FindControl<Border>("SettingsLightDismiss");
            if (sld != null) sld.IsVisible = true;

            await Task.Delay(10);
            if (LbPolicyPopup.Child is Border border) border.Classes.Add("popupOpen");
        }
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
        }
    }

    internal void LbPolicyOption_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string policy)
        {
            string displayName = policy switch
            {
                "leastload"  => "LEAST LOAD",
                "roundrobin" => "ROUND ROBIN",
                "leastping"  => "LEAST PING",
                "random"     => "RANDOM",
                _            => policy.ToUpperInvariant()
            };

            var lbl = this.FindControl<TextBlock>("lblCurrentLbPolicy");
            if (lbl != null) lbl.Text = displayName;

            bool wasConnected = _state.IsConnected || _state.IsEngineRunning;
            _cfg.XrayBalancePolicy = policy;
            SaveConfig();

            if (wasConnected)
                SmartRestartXray();

            _ = ClosePopupAnimatedAsync();
        }
    }

    private void SettingsLightDismiss_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((LanguagePopup != null && LanguagePopup.IsOpen) || (LbPolicyPopup != null && LbPolicyPopup.IsOpen))
        {
            _ = ClosePopupAnimatedAsync();
        }
    }
}

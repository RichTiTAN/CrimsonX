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

namespace CrimsonX.Pages;

public partial class ThemesPage : UserControl
{
    public static ThemesPage? Instance { get; private set; }

    public ThemesPage()
    {
        _isInitializing = true;
        InitializeComponent();
        Instance = this;
        SyncUI();
    }

    // ── Theme Selection ──

    private void ThemeSelect_Click(object? sender, RoutedEventArgs e)
    {
        var btn = sender as Button;
        if (btn == null) return;
        
        string themeName = btn.CommandParameter?.ToString() ?? "Crimson";
        
        MainWindow.Instance.Config.ThemeColor = themeName;
        MainWindow.Instance.RequestSave();

        Avalonia.Threading.DispatcherTimer.RunOnce(() => {
            MainWindow.Instance.ApplyTheme(themeName);
        }, System.TimeSpan.FromMilliseconds(300));
    }

    
    private bool _isInitializing = false;

    public void SyncUI()
    {
        _isInitializing = true;
        
        UpdateGlowButtons();

        MainWindow.Instance.UpdateGlobalAnimations();

        _isInitializing = false;
    }

    // ── Glow Animation Controls ──

    private void UpdateGlowButtons()
    {
        var btnPause = this.FindControl<Button>("btnPauseGlows");
        var btnDisable = this.FindControl<Button>("btnDisableGlows");

        if (btnPause != null)
        {
            if (MainWindow.Instance.Config.PauseGlows)
                btnPause.Classes.Add("activeMode");
            else
                btnPause.Classes.Remove("activeMode");
        }

        if (btnDisable != null)
        {
            if (MainWindow.Instance.Config.DisableGlows)
                btnDisable.Classes.Add("activeMode");
            else
                btnDisable.Classes.Remove("activeMode");
        }
    }

    private void btnPauseGlows_Click(object? sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        MainWindow.Instance.Config.PauseGlows = !MainWindow.Instance.Config.PauseGlows;
        UpdateGlowButtons();
        MainWindow.Instance.RequestSave();
        
        MainWindow.Instance.UpdateGlobalAnimations();
    }

    private void btnDisableGlows_Click(object? sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        MainWindow.Instance.Config.DisableGlows = !MainWindow.Instance.Config.DisableGlows;
        UpdateGlowButtons();
        MainWindow.Instance.RequestSave();
        
        MainWindow.Instance.UpdateGlobalAnimations();
    }

    // ── Localization ──

    internal void UpdateLocalization()
    {
    }

    public void ApplyLanguage()
    {
        var F = new System.Func<string, Avalonia.Controls.TextBlock>(name => this.FindControl<Avalonia.Controls.TextBlock>(name));
        CrimsonX.Localization.AppStrings.Apply(F("lblChooseAColour"), CrimsonX.Localization.AppStrings.ChooseAColour);
        CrimsonX.Localization.AppStrings.Apply(F("lblColorCrimson"), CrimsonX.Localization.AppStrings.ColorCrimson);
        CrimsonX.Localization.AppStrings.Apply(F("lblColorBlue"), CrimsonX.Localization.AppStrings.ColorBlue);
        CrimsonX.Localization.AppStrings.Apply(F("lblColorPurple"), CrimsonX.Localization.AppStrings.ColorPurple);
        CrimsonX.Localization.AppStrings.Apply(F("lblColorGreen"), CrimsonX.Localization.AppStrings.ColorGreen);
        CrimsonX.Localization.AppStrings.Apply(F("lblColorPink"), CrimsonX.Localization.AppStrings.ColorPink);
        CrimsonX.Localization.AppStrings.Apply(F("lblColorYellow"), CrimsonX.Localization.AppStrings.ColorYellow);
    
        CrimsonX.Localization.AppStrings.Apply(F("lblManageGlow"), CrimsonX.Localization.AppStrings.ThemeManageGlow);
        CrimsonX.Localization.AppStrings.Apply(F("lblPauseGlows"), CrimsonX.Localization.AppStrings.ThemePauseGlows);
        CrimsonX.Localization.AppStrings.Apply(F("lblDisableGlows"), CrimsonX.Localization.AppStrings.ThemeDisableGlows);
    }
}

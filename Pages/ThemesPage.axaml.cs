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
        InitializeComponent();
        Instance = this;
    }

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
    }
}

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
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace CrimsonX.Controls;

public class LocationOption
{
    public string Tag { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public bool IsSelected { get; set; }

    public bool IsNotSelected => !IsSelected;
}

public partial class NavigationBar : UserControl
{
    public event EventHandler<string>? NavChanged;

    public event EventHandler<bool>? AdBlockerToggled;

    private bool _syncingAdBlocker;

    private static readonly IBrush AdBlockShieldIdle = new SolidColorBrush(Color.Parse("#909090"));

    private static readonly IBrush AdBlockShieldOn = new SolidColorBrush(Color.Parse("#68D391"));

    private bool _themesTabOpen;

    public NavigationBar()
    {
        InitializeComponent();

        var lstLocations = this.FindControl<ItemsControl>("lstLocations");
        if (lstLocations != null) lstLocations.ItemsSource = LocationOptions;
    }

    // Localization

    public void ApplyLanguage()
    {
        SetTip("btnNavHome", CrimsonX.Localization.AppStrings.NavHome);
        SetTip("btnNavSplit", CrimsonX.Localization.AppStrings.NavSplitTunneling);
        SetTip("btnNavThemes", CrimsonX.Localization.AppStrings.NavThemes);
        SetTip("btnNavUdp", CrimsonX.Localization.AppStrings.UdpScannerTitle);
        SetTip("btnNavLocations", CrimsonX.Localization.AppStrings.LocationsTitle);
        SetTip("btnNavAppsGames", CrimsonX.Localization.AppStrings.NavAppsGames);
        SetTip("btnNavSettings", CrimsonX.Localization.AppStrings.NavSettings);
        SetTip("btnNavAdBlock", CrimsonX.Localization.AppStrings.AdBlocker);

        var locationsTitle = this.FindControl<TextBlock>("lblLocationsTitle");
        if (locationsTitle != null) locationsTitle.Text = CrimsonX.Localization.AppStrings.LocationsTitle;
        SetTip("btnLocationsClose", CrimsonX.Localization.AppStrings.LocationsClose);
        RefreshLocationOptions();
    }

    private void SetTip(string name, string text)
    {
        var control = this.FindControl<Control>(name);
        if (control != null) ToolTip.SetTip(control, text);
    }

    // Ad & tracker blocker tile

    private void AdBlocker_Changed(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton tile)
        {
            ShowAdBlockerIcon(tile.IsChecked == true);

            if (_syncingAdBlocker) return;

            AdBlockerToggled?.Invoke(this, tile.IsChecked == true);
        }
    }

    public void SetAdBlockerState(bool enabled)
    {
        var tile = this.FindControl<ToggleButton>("btnNavAdBlock");
        if (tile == null) return;

        ShowAdBlockerIcon(enabled);

        if (tile.IsChecked == enabled) return;

        _syncingAdBlocker = true;
        try
        {
            tile.IsChecked = enabled;
        }
        finally
        {
            _syncingAdBlocker = false;
        }
    }

    private void ShowAdBlockerIcon(bool enabled)
    {
        var icon = this.FindControl<PathIcon>("adBlockIcon");
        if (icon != null) icon.Foreground = enabled ? AdBlockShieldOn : AdBlockShieldIdle;
    }

    // Themes tile colour

    public static IBrush? ThemesIconBrush(bool selected) => selected ? Brushes.White : null;

    public void ApplyTheme() => ShowThemeIcon();

    private void ShowThemeIcon()
    {
        var icon = this.FindControl<PathIcon>("themeNavIcon");
        if (icon == null) return;

        var brush = ThemesIconBrush(_themesTabOpen);

        if (brush == null) icon.ClearValue(PathIcon.ForegroundProperty);
        else               icon.Foreground = brush;
    }

    // Locations overlay

    public const string AllTag = "*";

    public static readonly string[] ContinentNames =
    {
        "Asia", "Europe", "North America", "South America", "Africa", "Oceania",
    };

    public ObservableCollection<LocationOption> LocationOptions { get; } = new();

    public static List<string> ExcludedContinentsFor(IEnumerable<string> allowed)
    {
        var keep = new HashSet<string>(allowed, StringComparer.Ordinal);
        var excluded = new List<string>();
        foreach (var name in ContinentNames)
            if (!keep.Contains(name)) excluded.Add(name);

        return excluded;
    }

    private static IEnumerable<(string FullName, string DisplayName)> ContinentOptions()
    {
        yield return ("Asia", CrimsonX.Localization.AppStrings.ExcludeContinentAsia);
        yield return ("Europe", CrimsonX.Localization.AppStrings.ExcludeContinentEurope);
        yield return ("North America", CrimsonX.Localization.AppStrings.ExcludeContinentNorthAmerica);
        yield return ("South America", CrimsonX.Localization.AppStrings.ExcludeContinentSouthAmerica);
        yield return ("Africa", CrimsonX.Localization.AppStrings.ExcludeContinentAfrica);
        yield return ("Oceania", CrimsonX.Localization.AppStrings.ExcludeContinentOceania);
    }

    private void RefreshLocationOptions()
    {
        var cfg = MainWindow.Instance?.Config;
        var excluded = cfg?.ExcludedContinents ?? new List<string>();
        int excludedCount = ContinentNames.Count(name => excluded.Contains(name));

        if (cfg != null && cfg.EnableExcludedContinents && (excludedCount == 0 || excludedCount >= ContinentNames.Length))
            cfg.EnableExcludedContinents = false;

        bool filtering = cfg != null && cfg.EnableExcludedContinents
                         && excludedCount > 0 && excludedCount < ContinentNames.Length;

        LocationOptions.Clear();
        LocationOptions.Add(new LocationOption
        {
            Tag = AllTag,
            DisplayName = CrimsonX.Localization.AppStrings.FilterAll,
            IsSelected = !filtering,
        });

        foreach (var (fullName, displayName) in ContinentOptions())
        {
            LocationOptions.Add(new LocationOption
            {
                Tag = fullName,
                DisplayName = displayName,
                IsSelected = filtering && !excluded.Contains(fullName),
            });
        }
    }

    private async void NavLocations_Changed(object? sender, RoutedEventArgs e)
    {
        var tile = sender as ToggleButton;
        var popup = this.FindControl<Popup>("LocationsPopup");
        if (tile == null || popup == null) return;

        if (tile.IsChecked == true)
        {
            CrimsonX.Pages.SettingsPage.Instance?.ClosePopups();
            QuickSettingsPanel.Instance?.ClosePopups();

            RefreshLocationOptions();
            popup.PlacementTarget = tile;

            if (popup.Child is Border card) card.Classes.Remove("popupOpen");

            popup.IsOpen = true;
            ShowLightDismissLayer(visible: true);

            await Task.Delay(10);
            if (popup.IsOpen && popup.Child is Border opened) opened.Classes.Add("popupOpen");
        }
        else if (popup.IsOpen)
        {
            await CloseLocationsAnimatedAsync();
        }
    }

    private void LocationsPopup_Closed(object? sender, EventArgs e)
    {
        var tile = this.FindControl<ToggleButton>("btnNavLocations");
        if (tile != null && tile.IsChecked == true) tile.IsChecked = false;
    }

    private void LocationsClose_Click(object? sender, RoutedEventArgs e) => ClosePopups();

    private async Task CloseLocationsAnimatedAsync()
    {
        var popup = this.FindControl<Popup>("LocationsPopup");
        if (popup == null || !popup.IsOpen) return;

        if (popup.Child is Border card)
        {
            card.Classes.Remove("popupOpen");
            await Task.Delay(200);
        }

        popup.IsOpen = false;
        ShowLightDismissLayer(visible: false);
    }

    public void ClosePopups() => _ = CloseLocationsAnimatedAsync();

    private void ShowLightDismissLayer(bool visible) => MainWindow.Instance?.SetLightDismissLayer(visible);

    private void LocationOption_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not LocationOption option) return;

        var window = MainWindow.Instance;
        var cfg = window?.Config;
        if (cfg == null) return;

        var selected = LocationOptions.Where(o => o.IsSelected && o.Tag != AllTag).Select(o => o.Tag).ToList();

        if (option.Tag == AllTag) selected.Clear();
        else if (!selected.Remove(option.Tag)) selected.Add(option.Tag);

        bool allowEverything = selected.Count == 0;
        cfg.EnableExcludedContinents = !allowEverything;
        cfg.ExcludedContinents = allowEverything ? new List<string>() : ExcludedContinentsFor(selected);

        RefreshLocationOptions();

        window!.RequestConfigSave();
        if (window.State.IsEngineRunning)
            window.ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
    }

    // Nav Selection


    public void SelectTab(string tag)
    {
        var navStack = this.FindControl<StackPanel>("NavStack");
        if (navStack != null)
        {
            foreach (var child in navStack.Children)
            {
                if (child is RadioButton rb && rb.Tag is string rTag && rTag == tag)
                {
                    rb.IsChecked = true;
                    break;
                }
            }
        }
    }

    private void NavButton_Checked(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb)
        {
            if (rb.IsChecked == true)
            {
                _themesTabOpen = rb.Tag is "Themes";
                ShowThemeIcon();

                ClosePopups();

                if (rb.Tag is string tag)
                {
                    NavChanged?.Invoke(this, tag);
                }
            }
        }
    }

}

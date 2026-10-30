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
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using CrimsonX.Models;
using CrimsonX.Services;

namespace CrimsonX.Controls;

public class LocationOption
{
    public string Tag { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public bool IsSelected { get; set; }

    public bool IsNotSelected => !IsSelected;

    public string Kind { get; set; } = "region";

    public string FlagCode { get; set; } = "";

    public IImage? FlagImage { get; set; }

    public bool HasFlag => FlagImage != null;

    public Avalonia.Media.Geometry? IconGeometry { get; set; }

    public bool HasIcon => IconGeometry != null;
}

internal static class LocationIcons
{
    public static readonly Avalonia.Media.Geometry? Auto = Avalonia.Media.StreamGeometry.Parse("M3.9 12c0-1.71 1.39-3.1 3.1-3.1h4V7H7c-2.76 0-5 2.24-5 5s2.24 5 5 5h4v-1.9H7c-1.71 0-3.1-1.39-3.1-3.1zM8 13h8v-2H8v2zm9-6h-4v1.9h4c1.71 0 3.1 1.39 3.1 3.1s-1.39 3.1-3.1 3.1h-4V17h4c2.76 0 5-2.24 5-5s-2.24-5-5-5z");

    public static readonly Avalonia.Media.Geometry? Custom = Avalonia.Media.StreamGeometry.Parse("M3 13h2v-2H3v2zm0 4h2v-2H3v2zm0-8h2V7H3v2zm4 4h14v-2H7v2zm0 4h14v-2H7v2zM7 7v2h14V7H7z");
}

internal static class LocationFlags
{
    private static readonly Dictionary<string, IImage?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IImage? For(string? code)
    {
        string cc = (code ?? "").Trim().ToLowerInvariant();
        if (cc.Length != 2) return null;
        if (Cache.TryGetValue(cc, out var cached)) return cached;
        IImage? image = null;
        try
        {
            var uri = new Uri($"avares://CrimsonX/Assets/Flags/{cc}.png");
            using var stream = Avalonia.Platform.AssetLoader.Open(uri);
            image = new Avalonia.Media.Imaging.Bitmap(stream);
        }
        catch
        {
            image = null;
        }
        Cache[cc] = image;
        return image;
    }
}

internal static class ContinentIcons
{
    private static readonly Dictionary<string, IImage?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IImage? For(string? region)
    {
        string slug = Slug(region);
        if (slug.Length == 0) return null;
        if (Cache.TryGetValue(slug, out var cached)) return cached;
        IImage? image = null;
        try
        {
            var uri = new Uri($"avares://CrimsonX/Assets/Continents/{slug}.png");
            using var stream = Avalonia.Platform.AssetLoader.Open(uri);
            image = new Avalonia.Media.Imaging.Bitmap(stream);
        }
        catch
        {
            image = null;
        }
        Cache[slug] = image;
        return image;
    }

    private static string Slug(string? region) => (region ?? "").Trim().ToLowerInvariant() switch
    {
        "all" => "all",
        "africa" => "africa",
        "asia" => "asia",
        "europe" => "europe",
        "oceania" => "oceania",
        "north america" => "north-america",
        "south america" => "south-america",
        _ => ""
    };
}

public partial class NavigationBar : UserControl
{
    public event EventHandler<string>? NavChanged;

    public event EventHandler<bool>? AdBlockerToggled;

    private bool _syncingAdBlocker;

    private static readonly IBrush AdBlockShieldIdle = new SolidColorBrush(Color.Parse("#909090"));

    private static readonly IBrush AdBlockShieldOn = new SolidColorBrush(Color.Parse("#68D391"));

    private bool _themesTabOpen;

    private string _mode = "REGIONS";

    public NavigationBar()
    {
        InitializeComponent();
        var lstLocations = this.FindControl<ItemsControl>("lstLocations");
        if (lstLocations != null) lstLocations.ItemsSource = LocationOptions;
        CountryExitFeed.Changed += OnCountryFeedChanged;
    }

    private void OnCountryFeedChanged()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var popup = this.FindControl<Popup>("LocationsPopup");
            if (popup?.IsOpen == true && CountriesMode) RefreshLocationOptions();
        });
    }

    private bool CountriesMode => string.Equals(_mode, "COUNTRIES", StringComparison.OrdinalIgnoreCase);

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
        var modeRegions = this.FindControl<TextBlock>("lblLocModeRegions");
        if (modeRegions != null) modeRegions.Text = CrimsonX.Localization.AppStrings.LocationsRegions;
        var modeCountries = this.FindControl<TextBlock>("lblLocModeCountries");
        if (modeCountries != null) modeCountries.Text = CrimsonX.Localization.AppStrings.LocationsCountries;
        RefreshLocationOptions();
    }

    private void SetTip(string name, string text)
    {
        var control = this.FindControl<Control>(name);
        if (control != null) CrimsonX.Localization.AppStrings.ApplyToolTip(control, text);
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

    public const string CustomTag = "custom";

    public const string AutoTag = "auto";

    public static readonly string[] ContinentNames = CrimsonX.Localization.CountryContinents.Names;

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
        _mode = string.Equals(cfg?.LocationsMode, "COUNTRIES", StringComparison.OrdinalIgnoreCase) ? "COUNTRIES" : "REGIONS";
        UpdateModeButtons();
        LocationOptions.Clear();
        if (CountriesMode) BuildCountryOptions(cfg);
        else BuildRegionOptions(cfg);
    }

    private void BuildRegionOptions(AppConfig? cfg)
    {
        var excluded = cfg?.ExcludedContinents ?? new List<string>();
        int excludedCount = ContinentNames.Count(name => excluded.Contains(name));
        if (cfg != null && cfg.EnableExcludedContinents && (excludedCount == 0 || excludedCount >= ContinentNames.Length))
            cfg.EnableExcludedContinents = false;
        bool filtering = cfg != null && cfg.EnableExcludedContinents
                         && excludedCount > 0 && excludedCount < ContinentNames.Length;
        LocationOptions.Add(new LocationOption
        {
            Kind = "region",
            Tag = AllTag,
            DisplayName = CrimsonX.Localization.AppStrings.FilterAll,
            FlagImage = ContinentIcons.For("all"),
            IsSelected = !filtering,
        });
        foreach (var (fullName, displayName) in ContinentOptions())
        {
            LocationOptions.Add(new LocationOption
            {
                Kind = "region",
                Tag = fullName,
                DisplayName = displayName,
                FlagImage = ContinentIcons.For(fullName),
                IsSelected = filtering && !excluded.Contains(fullName),
            });
        }
    }

    private void BuildCountryOptions(AppConfig? cfg)
    {
        bool sourceIsCustom = cfg == null
            || string.Equals(cfg.ExitNodeSource, "CUSTOM", StringComparison.OrdinalIgnoreCase);
        bool auto = cfg?.ExitNodeAuto == true;
        bool exitOn = cfg?.EnableV2rayChain == true;
        string chosen = (cfg?.ExitNodeCountry ?? "").Trim();
        LocationOptions.Add(new LocationOption
        {
            Kind = "auto",
            Tag = AutoTag,
            DisplayName = CrimsonX.Localization.AppStrings.ExitNodeAutoRow,
            IconGeometry = LocationIcons.Auto,
            IsSelected = auto,
        });
        if (CountryExitFeed.CustomExitAvailable(cfg))
        {
            LocationOptions.Add(new LocationOption
            {
                Kind = "custom",
                Tag = CustomTag,
                DisplayName = CrimsonX.Localization.AppStrings.ExitNodeCustomRow,
                IconGeometry = LocationIcons.Custom,
                IsSelected = sourceIsCustom && !auto,
            });
        }
        var allowed = CountryExitFeed.AllowedContinents(cfg);
        foreach (var node in CountryExitFeed.Nodes)
        {
            if (!CountryExitFeed.IsVisible(node, allowed)) continue;
            LocationOptions.Add(new LocationOption
            {
                Kind = "country",
                Tag = node.Code,
                FlagCode = node.Code,
                FlagImage = LocationFlags.For(node.Code),
                DisplayName = CountryExitFeed.DisplayName(node),
                IsSelected = !sourceIsCustom && exitOn
                             && string.Equals(node.Code, chosen, StringComparison.OrdinalIgnoreCase),
            });
        }
        if (LocationOptions.All(o => o.Kind != "country"))
        {
            LocationOptions.Add(new LocationOption
            {
                Kind = "info",
                Tag = "",
                DisplayName = CountryExitFeed.Loaded
                    ? CrimsonX.Localization.AppStrings.ExitNodeNoCountries
                    : CrimsonX.Localization.AppStrings.ExitNodeFetching,
            });
        }
    }

    private void UpdateModeButtons()
    {
        SetSelectedClass(this.FindControl<Button>("btnLocModeRegions"), !CountriesMode);
        SetSelectedClass(this.FindControl<Button>("btnLocModeCountries"), CountriesMode);
    }

    private static void SetSelectedClass(Button? button, bool selected)
    {
        if (button == null) return;
        if (selected)
        {
            if (!button.Classes.Contains("selected")) button.Classes.Add("selected");
        }
        else
        {
            button.Classes.Remove("selected");
        }
    }

    private void NavLocations_Changed(object? sender, RoutedEventArgs e)
        => CrimsonX.Services.BackgroundTask.RunOnUi("nav.locations", () => NavLocationsChangedAsync(sender, e));

    private async Task NavLocationsChangedAsync(object? sender, RoutedEventArgs e)
    {
        var tile = sender as ToggleButton;
        var popup = this.FindControl<Popup>("LocationsPopup");
        if (tile == null || popup == null) return;
        if (tile.IsChecked == true)
        {
            CrimsonX.Pages.SettingsPage.Instance?.ClosePopups();
            QuickSettingsPanel.Instance?.ClosePopups();
            RefreshLocationOptions();
            if (CountriesMode) _ = RefreshCountryFeedAsync();
            popup.PlacementTarget = tile;
            if (popup.Child is Border card) card.Classes.Remove("popupOpen");
            popup.IsOpen = true;
            ShowLightDismissLayer(visible: true);
            await Task.Delay(10);
            FitLocationsCard();
            if (popup.IsOpen && popup.Child is Border opened) opened.Classes.Add("popupOpen");
        }
        else if (popup.IsOpen)
        {
            await CloseLocationsAnimatedAsync();
        }
    }

    private void FitLocationsCard()
    {
        var card = this.FindControl<Border>("LocationsCard");
        var regions = this.FindControl<Button>("btnLocModeRegions");
        var countries = this.FindControl<Button>("btnLocModeCountries");
        if (card == null || regions == null || countries == null) return;
        if (regions.Bounds.Width <= 0 || countries.Bounds.Width <= 0) return;

        double pills = countries.Bounds.Right - regions.Bounds.X;
        double sides = card.BorderThickness.Left + card.BorderThickness.Right
                       + card.Padding.Left + card.Padding.Right;
        double gap = card.Padding.Left;
        double width = Math.Ceiling(pills + sides + gap);

        if (double.IsNaN(card.Width) || Math.Abs(card.Width - width) > 0.5) card.Width = width;
    }

    private void LocationsPopup_Closed(object? sender, EventArgs e)
    {
        var tile = this.FindControl<ToggleButton>("btnNavLocations");
        if (tile != null && tile.IsChecked == true) tile.IsChecked = false;
    }

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
        if (option.Kind is "country" or "custom" or "auto")
        {
            ApplyCountryPick(window!, cfg, option);
            return;
        }
        if (option.Kind != "region") return;
        var selected = LocationOptions.Where(o => o.Kind == "region" && o.IsSelected && o.Tag != AllTag)
                                      .Select(o => o.Tag).ToList();
        if (option.Tag == AllTag) selected.Clear();
        else if (!selected.Remove(option.Tag)) selected.Add(option.Tag);
        bool allowEverything = selected.Count == 0;
        cfg.EnableExcludedContinents = !allowEverything;
        cfg.ExcludedContinents = allowEverything ? new List<string>() : ExcludedContinentsFor(selected);
        RefreshLocationOptions();
        CrimsonX.Pages.SettingsPage.Instance?.RefreshExitNodePane();
        window!.RequestConfigSave();
        if (window.State.IsEngineRunning)
            window.ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
    }

    private void LocModeRegions_Click(object? sender, RoutedEventArgs e) => SetLocationsMode("REGIONS");

    private void LocModeCountries_Click(object? sender, RoutedEventArgs e) => SetLocationsMode("COUNTRIES");

    private void SetLocationsMode(string mode)
    {
        var window = MainWindow.Instance;
        if (window?.Config != null) window.Config.LocationsMode = mode;
        _mode = mode;
        RefreshLocationOptions();
        window?.RequestConfigSave();
        if (CountriesMode) _ = RefreshCountryFeedAsync();
    }

    private async Task RefreshCountryFeedAsync()
    {
        var cfg = MainWindow.Instance?.Config;
        try
        {
            if (!CountryExitFeed.Loaded)
            {
                await CountryExitFeed.RefreshAsync(cfg);
                if (CountriesMode) RefreshLocationOptions();
            }
        }
        catch { }
    }

    private void ApplyCountryPick(MainWindow window, AppConfig cfg, LocationOption option)
    {
        bool changed;
        if (option.Kind == "custom")
        {
            if (!CountryExitFeed.CustomExitAvailable(cfg)) return;
            changed = CountryExitFeed.SelectCustom(cfg, arm: true);
        }
        else if (option.Kind == "auto")
        {
            changed = CountryExitFeed.SelectAuto(cfg);
        }
        else
        {
            var node = CountryExitFeed.NodeFor(option.Tag);
            if (node == null) return;
            changed = CountryExitFeed.SelectCountry(cfg, node);
        }
        RefreshLocationOptions();
        window.RequestConfigSave();
        if (changed && window.State.IsEngineRunning) window.SmartRestartXray();
        CrimsonX.Pages.SettingsPage.Instance?.RefreshExitNodePane();
        if (option.Kind is "country" or "auto") ClosePopups();
    }

    // Nav Selection

    public void SelectTab(string tag)
    {
        foreach (var radio in this.GetVisualDescendants().OfType<RadioButton>())
        {
            if (radio.Tag is string candidate && candidate == tag)
            {
                radio.IsChecked = true;
                return;
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

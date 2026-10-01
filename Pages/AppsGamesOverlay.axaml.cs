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
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using CrimsonX.Models;
using CrimsonX.Services;
using AS = CrimsonX.Localization.AppStrings;

namespace CrimsonX.Pages;

public class AppRuleViewModel
{
    private static readonly Dictionary<string, Bitmap?> DefaultIconCache = new();
    private static readonly Queue<string> DefaultIconOrder = new();
    private const int DefaultIconCacheLimit = 256;

    private static readonly Dictionary<string, Bitmap?> CustomIconCache = new(StringComparer.Ordinal);
    private static readonly Queue<string> CustomIconOrder = new();
    private const int CustomIconCacheLimit = 64;

    public string  RuleId     { get; }
    public bool    IsEnabled  { get; set; }
    public string  ExeName    { get; }
    public string  DisplayName { get; }
    public bool    HasIcon    { get; }
    public Bitmap? IconBitmap { get; }
    public bool    IsDefault  { get; }
    public bool    IsPinned   { get; }
    public bool    HasCountry { get; }
    public bool    HasRegion { get; }
    public string  RegionLabel { get; }
    public string  RegionTooltip { get; }
    public string[] CountryItems { get; }
    public string[] RegionItems { get; }
    public string ConfigLabel => CrimsonX.Localization.AppStrings.ConfigBadge;
    public string EditTooltip => CrimsonX.Localization.AppStrings.Edit;
    public string DeleteTooltip => CrimsonX.Localization.AppStrings.Delete;
    public string PinTooltip => CrimsonX.Localization.AppStrings.PinUnpin;
    public string EditAdaptersTooltip => CrimsonX.Localization.AppStrings.EditAdapters;
    public bool    IsLauncher { get; }
    public bool    IsBrowser  { get; }
    public bool    IsLeague   { get; }
    public bool    IsTekken   { get; }
    public bool    IsValorant { get; }
    public bool    ShowsRoutingPill => IsLauncher || IsBrowser;
    public bool    ShowsRoutingEditor => IsLeague || IsTekken || IsValorant;
    public bool    IsDirect   { get; }
    public bool    HasDirectRouting { get; }
    public bool    IsCustomRouting { get; }
    public string  CustomProxyLabel { get; }
    public string  CustomLabel => CrimsonX.Localization.AppStrings.RoutingCustomShort;
    public string  DirectLabel => CrimsonX.Localization.AppStrings.RoutingDirect.ToUpperInvariant();
    public string  ProxyLabel  => CrimsonX.Localization.AppStrings.RoutingProxy.ToUpperInvariant();

    public string  EditorTooltip => IsCustomRouting && !string.IsNullOrWhiteSpace(CustomProxyLabel)
        ? CustomProxyLabel
        : EditAdaptersTooltip;

    public bool    ShowAdapterEditor => HasCountry || HasRegion || IsLauncher || IsBrowser || ShowsRoutingEditor || HasDirectRouting;
    public int     CountryIndex { get; }
    public int     RegionIndex  { get; }

    public AppRuleViewModel(AppGameRule rule)
    {
        RuleId    = rule.Id;
        IsEnabled = rule.IsEnabled;
        ExeName   = rule.ExeName;
        DisplayName = string.IsNullOrWhiteSpace(rule.DisplayName) ? rule.ExeName : rule.DisplayName;
        IsDefault = !string.IsNullOrEmpty(rule.DefaultKey);
        IsPinned  = rule.IsPinned;
        HasCountry = !string.IsNullOrEmpty(rule.Country);
        HasRegion  = !string.IsNullOrEmpty(rule.Region);
        IsLauncher = string.Equals(rule.AppType, "Launcher", StringComparison.OrdinalIgnoreCase);
        IsBrowser  = AppsGamesOverlay.BrowserDefaultKeys.Contains(rule.DefaultKey ?? "", StringComparer.OrdinalIgnoreCase);
        IsLeague   = string.Equals(rule.DefaultKey, AppsGamesOverlay.LeagueDefaultKey, StringComparison.Ordinal);
        IsTekken   = string.Equals(rule.DefaultKey, AppsGamesOverlay.Tekken8DefaultKey, StringComparison.Ordinal);
        IsValorant = string.Equals(rule.DefaultKey, AppsGamesOverlay.ValorantDefaultKey, StringComparison.Ordinal);
        IsDirect   = string.Equals(rule.TcpRouting, "Direct", StringComparison.OrdinalIgnoreCase);
        IsCustomRouting = AppsGamesOverlay.IsCustomRouting(rule.TcpRouting)
                       || AppsGamesOverlay.IsCustomRouting(rule.UdpRouting);
        CustomProxyLabel = rule.CustomProxyLabel ?? "";
        HasDirectRouting = !AppsGamesOverlay.IsProxyRouting(rule.TcpRouting)
                        || !AppsGamesOverlay.IsProxyRouting(rule.UdpRouting);
        RegionLabel = HasCountry ? CrimsonX.Localization.AppStrings.ConnRegionShort : CrimsonX.Localization.AppStrings.MatchMakingRegion;
        RegionTooltip = HasCountry ? CrimsonX.Localization.AppStrings.ConnectionRegionLabel : CrimsonX.Localization.AppStrings.MatchMakingRegion;
        CountryIndex = rule.Country switch { "IRAN" => 1, "UAE" => 2, _ => 0 };
        RegionIndex  = AppsGamesOverlay.RegionIndexFor(rule.Region);
        CountryItems = AppsGamesOverlay.CountryDisplayOptions();
        RegionItems  = AppsGamesOverlay.RegionDisplayOptions();

        IconBitmap = RuleIcon(rule);
        HasIcon    = IconBitmap != null;
    }

    internal static Bitmap? RuleIcon(AppGameRule rule)
    {
        if (rule == null) return null;

        if (!string.IsNullOrEmpty(rule.IconAsset))
        {
            if (!DefaultIconCache.TryGetValue(rule.IconAsset, out var bmp))
            {
                bmp = LoadIconBitmap(rule.IconAsset);
                DefaultIconCache[rule.IconAsset] = bmp;
                DefaultIconOrder.Enqueue(rule.IconAsset);
                TrimIconCache(DefaultIconCache, DefaultIconOrder, DefaultIconCacheLimit);
            }
            return bmp;
        }

        if (!string.IsNullOrEmpty(rule.IconBase64))
            return CustomIcon(rule.IconBase64);

        return null;
    }

    private static Bitmap? CustomIcon(string base64)
    {
        if (string.IsNullOrEmpty(base64)) return null;
        if (CustomIconCache.TryGetValue(base64, out var cached)) return cached;

        Bitmap? bmp;
        try
        {
            var bytes = Convert.FromBase64String(base64);
            using var ms = new MemoryStream(bytes);
            bmp = new Bitmap(ms);
        }
        catch { bmp = null; }

        CustomIconCache[base64] = bmp;
        CustomIconOrder.Enqueue(base64);
        TrimIconCache(CustomIconCache, CustomIconOrder, CustomIconCacheLimit);
        return bmp;
    }

    private static void TrimIconCache(Dictionary<string, Bitmap?> cache, Queue<string> order, int limit)
    {
        while (cache.Count > limit && order.Count > 0)
            cache.Remove(order.Dequeue());
    }

    private static Bitmap? LoadIconBitmap(string assetName)
    {
        try
        {
            using var stream = global::Avalonia.Platform.AssetLoader.Open(new Uri("avares://CrimsonX/Assets/icons/" + assetName));
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return new Bitmap(new MemoryStream(ms.ToArray()));
        }
        catch
        {
            return null;
        }
    }
}

public partial class AppsGamesOverlay : UserControl
{
    

    private List<AppGameRule> _rules = new();
    private string _currentFilter = "ALL";
    private string _searchText = "";

    private readonly ObservableCollection<AppRuleViewModel> _ruleRows = new();

    private long _renderedSignature;
    private string _renderedFilterKey = "";

    private int _ruleStreamGeneration;

    private const int RuleRowBatchSize = 6;
    private global::Avalonia.Threading.DispatcherTimer? _searchDebounceTimer;
    private CrimsonX.Helpers.DragReorderHelper? _dragHelper;

    private string _editingRuleId = "";
    private string _iconBase64 = "";
    private string _exeName = "";
    private string _displayName = "";
    private bool _renamingName = false;

    private string _editingDefaultRuleId = "";
    private Avalonia.Controls.Panel? _defaultRuleEditorParent = null;
    private Avalonia.Controls.Border? _hiddenDefaultRuleView = null;
    private bool _isClosingDefaultEditor = false;
    private global::Avalonia.Threading.DispatcherTimer? _connectUiTimer;
    private bool _hasPendingRuleChanges = false;
    private global::Avalonia.Threading.DispatcherTimer? _overlayFillTimer;
    private double _overlayFillCurrent = 0;
    private double _overlayFillTarget = -1;
    private Avalonia.Controls.Border? _overlayFillBorder;
    private Avalonia.Controls.Border? _overlayBreathBorder;
    private readonly CrimsonX.Services.ConnectBreath _overlayBreath = new CrimsonX.Services.ConnectBreath();
    private Avalonia.Media.ScaleTransform? _overlayFillScale;
    private bool _isReady = false;

    internal static readonly string[] RegionOptions = { "ALL", "North America", "South America", "Europe", "Asia", "Africa", "Oceania" };

    internal static string[] CountryDisplayOptions()
    {
        EnsureDisplayOptions();
        return _countryDisplayOptions!;
    }

    internal static string[] RegionDisplayOptions()
    {
        EnsureDisplayOptions();
        return _regionDisplayOptions!;
    }

    private static bool _displayOptionsPersian;
    private static string[]? _countryDisplayOptions;
    private static string[]? _regionDisplayOptions;

    private static void EnsureDisplayOptions()
    {
        bool persian = AS.IsPersian;
        if (_countryDisplayOptions != null && _regionDisplayOptions != null && _displayOptionsPersian == persian)
            return;

        _displayOptionsPersian = persian;
        _countryDisplayOptions = new[]
        {
            AS.CountryEverywhere,
            AS.CountryIran,
            AS.CountryUae
        };
        _regionDisplayOptions = new[]
        {
            AS.FilterAll,
            AS.RegionNorthAmerica,
            AS.RegionSouthAmerica,
            AS.RegionEurope,
            AS.RegionAsia,
            AS.RegionAfrica,
            AS.RegionOceania
        };
    }

    internal static int RegionIndexFor(string region)
    {
        int idx = Array.IndexOf(RegionOptions, region);
        return idx >= 0 ? idx : 0;
    }

    internal static string RegionForIndex(int idx)
    {
        return idx >= 0 && idx < RegionOptions.Length ? RegionOptions[idx] : "ALL";
    }

    // ── Routing values: combo order is Proxy (0) / Custom (1) / Direct (2) ──

    internal static string RoutingName(int index) => index switch
    {
        1 => "Custom",
        2 => "Direct",
        _ => "Proxy"
    };

    internal static int RoutingIndex(string routing) => routing?.Trim().ToLowerInvariant() switch
    {
        "custom" => 1,
        "direct" => 2,
        _ => 0
    };

    // ── App type values: combo order is Game (0) / Launcher (1) / Other (2) ──

    internal static string AppTypeName(int index) => index switch
    {
        1 => "Launcher",
        2 => "Other",
        _ => "Game"
    };

    internal static int AppTypeIndex(string appType) => appType?.Trim().ToLowerInvariant() switch
    {
        "launcher" => 1,
        "other"    => 2,
        _          => 0
    };

    internal static bool IsCustomRouting(string routing)
        => string.Equals(routing, "Custom", StringComparison.OrdinalIgnoreCase);

    internal static bool IsProxyRouting(string routing)
        => string.IsNullOrEmpty(routing) || string.Equals(routing, "Proxy", StringComparison.OrdinalIgnoreCase);

    private AppConfig _cfg => MainWindow.Instance.Config;
    private AppState _state => MainWindow.Instance.State;

    private readonly List<string> _adapterNames = new();

    public AppsGamesOverlay()
    {
        InitializeComponent();
        ApplyLanguage();
        _isReady = true;

        ApplyMasterRulesVisual();
        UpdateOverlayConnectUI();

        _connectUiTimer = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _connectUiTimer.Tick += (s, e) => { if (IsVisible) UpdateOverlayConnectUI(); };
        _connectUiTimer.Start();

        this.AttachedToVisualTree += (s, e) =>
        {
            CrimsonX.Services.UiEventBus.Instance.ConnectionProgress += OnConnectionProgress;

            UpdateOverlayConnectUI();
            _connectUiTimer?.Start();
        };
        this.DetachedFromVisualTree += (s, e) =>
        {
            CrimsonX.Services.UiEventBus.Instance.ConnectionProgress -= OnConnectionProgress;
            StopOverlayActivity();
        };

        if (this.FindControl<ScrollViewer>("Scroller") is { } scroller)
            scroller.ScrollChanged += OnScrollerScrollChanged;
    }

    private void StopOverlayActivity()
    {
        _connectUiTimer?.Stop();

        _overlayFillTimer?.Stop();
        _overlayFillTimer = null;
        _overlayFillTarget = -1;
        _overlayFillCurrent = 0;
        _overlayFillBorder = null;
        _overlayBreathBorder = null;
        _overlayFillScale = null;

        _dragHelper?.Detach();
        _dragHelper = null;

        _searchDebounceTimer?.Stop();
    }

    private void OnScrollerScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var shadow = this.FindControl<Border>("panSearchBarShadow");
        if (shadow == null) return;
        var scroller = this.FindControl<ScrollViewer>("Scroller");
        shadow.Opacity = scroller != null && scroller.Offset.Y > 0.5 ? 1.0 : 0.0;
    }

    // ── Rules Load & Defaults Migration ──

    public void LoadRules()
    {
        using var _busy = CrimsonX.Services.UiBusy.Scope("apps & games rules");

        _rules = AppRulesService.Load();
        EnsureDefaultRules();
        _hasPendingRuleChanges = false;

        if (RulesSignature(_rules) != _renderedSignature || FilterKey() != _renderedFilterKey)
            RefreshList(stream: true);
        CloseEditor(); 
        CloseDefaultEditor(true);
        UpdateOverlaySplitUI();
        ApplyMasterRulesVisual();
        UpdateOverlayConnectUI();
    }


    // ── Rules List & Adapters UI ──

    private void RefreshList(bool stream = false)
    {
        using var _busy = CrimsonX.Services.UiBusy.Scope("apps & games list");

        var lst = this.FindControl<ItemsControl>("lstRules");
        if (lst == null) return;
        
        var filtered = _rules.AsEnumerable();
        if (_currentFilter == "GAMES") filtered = filtered.Where(r => r.AppType == "Game");
        else if (_currentFilter == "LAUNCHERS") filtered = filtered.Where(r => r.AppType == "Launcher");
        else if (_currentFilter == "OTHER") filtered = filtered.Where(r => r.AppType == "Other");

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            string st = _searchText.Trim();
            filtered = filtered.Where(r =>
                (r.ExeName ?? "").Contains(st, StringComparison.OrdinalIgnoreCase)
                || (r.DisplayName ?? "").Contains(st, StringComparison.OrdinalIgnoreCase));
        }

        var viewModels = filtered.Select(r => new AppRuleViewModel(r)).ToList();

        if (!ReferenceEquals(lst.ItemsSource, _ruleRows)) lst.ItemsSource = _ruleRows;

        _renderedSignature = RulesSignature(_rules);
        _renderedFilterKey = FilterKey();

        int generation = ++_ruleStreamGeneration;
        _ruleRows.Clear();

        if (!stream)
        {
            foreach (var viewModel in viewModels) _ruleRows.Add(viewModel);
            PostEditorRehosts();
            return;
        }

        for (int from = AppendRuleBatch(viewModels, 0); from < viewModels.Count; from += RuleRowBatchSize)
        {
            int start = from;
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => { if (generation == _ruleStreamGeneration) AppendRuleBatch(viewModels, start); },
                Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    private int AppendRuleBatch(List<AppRuleViewModel> viewModels, int from)
    {
        int to = System.Math.Min(from + RuleRowBatchSize, viewModels.Count);
        for (int i = from; i < to; i++) _ruleRows.Add(viewModels[i]);

        if (to >= viewModels.Count) PostEditorRehosts();

        return to;
    }

    private void PostEditorRehosts()
    {
        if (!string.IsNullOrEmpty(_editingRuleId) && !_isClosing)
            Avalonia.Threading.Dispatcher.UIThread.Post(RehostRuleEditor, Avalonia.Threading.DispatcherPriority.Loaded);

        if (!string.IsNullOrEmpty(_editingDefaultRuleId) && !_isClosingDefaultEditor)
            Avalonia.Threading.Dispatcher.UIThread.Post(RehostDefaultRuleEditor, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private static long RulesSignature(List<AppGameRule> rules)
    {
        var hash = new HashCode();
        hash.Add(rules.Count);

        foreach (var rule in rules)
        {
            hash.Add(rule.Id);
            hash.Add(rule.IsEnabled);
            hash.Add(rule.IsPinned);
            hash.Add(rule.AppType);
            hash.Add(rule.ExeName);
            hash.Add(rule.DisplayName);
            hash.Add(rule.DefaultKey);
            hash.Add(rule.Country);
            hash.Add(rule.Region);
            hash.Add(rule.TcpRouting);
            hash.Add(rule.UdpRouting);
            hash.Add(rule.TcpAdapter);
            hash.Add(rule.UdpAdapter);
            hash.Add(rule.CustomProxyLabel);
            hash.Add(rule.IconAsset);
            hash.Add(rule.IconBase64.Length);
            hash.Add(rule.ProcessNames.Count);
            hash.Add(rule.Domains.Count);
        }

        return hash.ToHashCode();
    }

    private string FilterKey() => _currentFilter + "\u0001" + _searchText;

    private void RehostDefaultRuleEditor()
    {
        if (string.IsNullOrEmpty(_editingDefaultRuleId) || _isClosingDefaultEditor) return;

        var panDefaultEditor = this.FindControl<Border>("panDefaultEditor");
        var lst              = this.FindControl<ItemsControl>("lstRules");
        if (panDefaultEditor == null || lst == null) return;

        if (panDefaultEditor.Opacity <= 0) return;

        if (panDefaultEditor.Parent is Visual currentHost
            && Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(currentHost).Contains(lst)) return;

        var viewModels = (lst.ItemsSource as IEnumerable<AppRuleViewModel>)?.ToList();
        if (viewModels == null) return;

        int index = viewModels.FindIndex(v => v.RuleId == _editingDefaultRuleId);
        if (index < 0) return;

        var row = lst.ContainerFromIndex(index);
        if (row == null) return;

        var targetHost = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(row)
            .OfType<ContentControl>().FirstOrDefault(c => c.Name == "EditContainer");
        if (targetHost == null) return;

        if (panDefaultEditor.Parent is Panel oldPanel) oldPanel.Children.Remove(panDefaultEditor);
        else if (panDefaultEditor.Parent is ContentControl oldHost) { oldHost.Content = null; oldHost.IsVisible = false; }

        targetHost.IsVisible = true;
        targetHost.Content = panDefaultEditor;

        var parentStack = targetHost.Parent as StackPanel;
        _hiddenDefaultRuleView = parentStack?.Children.OfType<Avalonia.Controls.Border>().FirstOrDefault(b => b.Name == "panDefaultRuleWrapper");
        if (_hiddenDefaultRuleView != null) { SetTransitionSpeed(_hiddenDefaultRuleView, 0); _hiddenDefaultRuleView.MaxHeight = 0; _hiddenDefaultRuleView.Opacity = 0; }

        panDefaultEditor.MaxHeight = 800;
        panDefaultEditor.Opacity = 1;
    }

    private void RehostRuleEditor()
    {
        if (string.IsNullOrEmpty(_editingRuleId) || _isClosing) return;

        var panEditor = this.FindControl<Border>("panEditor");
        var lst       = this.FindControl<ItemsControl>("lstRules");
        if (panEditor == null || lst == null) return;

        if (panEditor.Opacity <= 0) return;

        if (panEditor.Parent is Visual currentHost
            && Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(currentHost).Contains(lst)) return;

        var viewModels = (lst.ItemsSource as IEnumerable<AppRuleViewModel>)?.ToList();
        if (viewModels == null) return;

        int index = viewModels.FindIndex(v => v.RuleId == _editingRuleId);
        if (index < 0) return;   

        var row = lst.ContainerFromIndex(index);
        if (row == null) return;

        var targetHost = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(row)
            .OfType<ContentControl>().FirstOrDefault(c => c.Name == "EditContainer");
        if (targetHost == null) return;

        if (panEditor.Parent is Panel oldPanel) oldPanel.Children.Remove(panEditor);
        else if (panEditor.Parent is ContentControl oldHost) { oldHost.Content = null; oldHost.IsVisible = false; }

        targetHost.IsVisible = true;
        targetHost.Content = panEditor;
        panEditor.MaxHeight = 800;
        panEditor.Opacity = 1;

        var parentStack = targetHost.Parent as StackPanel;
        _hiddenRuleView = parentStack?.Children.OfType<Border>().FirstOrDefault(b => b.Name == "panRuleWrapper");
        if (_hiddenRuleView != null)
        {
            SetTransitionSpeed(_hiddenRuleView, 0);
            _hiddenRuleView.MaxHeight = 0;
            _hiddenRuleView.Opacity = 0;
        }
    }

    private void SaveRules(bool markDirty = true)
    {
        AppRulesService.Save(_rules);
        if (markDirty)
        {
            _hasPendingRuleChanges = true;
            UpdateOverlayConnectUI();
        }
    }

    private const int SearchDebounceMs = 200;

    private void Search_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var tb = sender as TextBox;
        string text = tb?.Text ?? "";
        if (text == _searchText) return;
        _searchText = text;

        if (text.Length == 0)
        {
            _searchDebounceTimer?.Stop();
            RefreshList();
            return;
        }

        if (_searchDebounceTimer == null)
        {
            _searchDebounceTimer = new global::Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(SearchDebounceMs)
            };
            _searchDebounceTimer.Tick += (s, e) => { _searchDebounceTimer?.Stop(); RefreshList(); };
        }

        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void RuleEnabled_Changed(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is AppRuleViewModel vm)
        {
            var rule = _rules.FirstOrDefault(r => r.Id == vm.RuleId);
            if (rule != null && rule.IsEnabled != (cb.IsChecked == true))
            {
                rule.IsEnabled = cb.IsChecked == true;
                SaveRules();
            }
        }
    }

    

    // INLINE EDITOR LOGIC

    private void PopulateAdapters()
    {
        var items = new List<string> { CrimsonX.Localization.AppStrings.AdapterDefault };
        _adapterNames.Clear();
        _adapterNames.Add("Default");

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var ipv4 = nic.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                if (ipv4 == null || string.IsNullOrWhiteSpace(ipv4.Address.ToString())) continue;

                string display = $"{nic.Name} - {ipv4.Address}";
                items.Add(display);
                _adapterNames.Add(nic.Name);
            }
        }
        catch { }

        var cbTcp = this.FindControl<ComboBox>("cbTcpAdapter");
        var cbUdp = this.FindControl<ComboBox>("cbUdpAdapter");
        if (cbTcp != null) { cbTcp.ItemsSource = items; cbTcp.SelectedIndex = 0; }
        if (cbUdp != null) { cbUdp.ItemsSource = items; cbUdp.SelectedIndex = 0; }

        var cbDefTcp = this.FindControl<ComboBox>("cbDefaultTcpAdapter");
        var cbDefUdp = this.FindControl<ComboBox>("cbDefaultUdpAdapter");
        if (cbDefTcp != null) { cbDefTcp.ItemsSource = items; cbDefTcp.SelectedIndex = 0; }
        if (cbDefUdp != null) { cbDefUdp.ItemsSource = items; cbDefUdp.SelectedIndex = 0; }
    }

    // ── Rule Editor (Add / Edit) ──

    private void AddToggle_Click(object? sender, PointerPressedEventArgs e)
    {
        var panEditor = this.FindControl<Border>("panEditor");
        if (panEditor == null) return;

        if (panEditor.Opacity > 0 && string.IsNullOrEmpty(_editingRuleId)) return;

        OpenEditor(null, null);
    }

    private Avalonia.Controls.Panel? _defaultEditorParent = null;
    private Avalonia.Controls.Border? _hiddenRuleView = null;

    private void EditRule_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.Button btn && btn.Tag is string ruleId)
        {
            var existing = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (existing != null)
            {
                var stackPanel = Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(btn).OfType<Avalonia.Controls.StackPanel>().FirstOrDefault();
                var container = stackPanel?.Children.OfType<Avalonia.Controls.ContentControl>().FirstOrDefault(c => c.Name == "EditContainer");
                OpenEditor(existing, container);
            }
        }
    }

    private bool _isClosing = false;
    private int _editorVersion = 0;
    private void OpenEditor(AppGameRule? ruleToEdit = null, Avalonia.Controls.ContentControl? targetContainer = null)
    {
        var panAddToggle = this.FindControl<Avalonia.Controls.Border>("panAddToggle");
var panAddToggleWrapper = this.FindControl<Avalonia.Controls.Border>("panAddToggleWrapper");
        var panEditor    = this.FindControl<Avalonia.Controls.Border>("panEditor");
        var btnSubmit    = this.FindControl<Avalonia.Controls.Button>("btnSubmit");

        CloseDefaultEditor(true);

        if (panEditor == null) return;
        
        if (panEditor.Opacity > 0 || _isClosing)
        {
            CloseEditor(true);
        }
        _editorVersion++;
        _isClosing = false;

        if (_defaultEditorParent == null)
            _defaultEditorParent = panEditor.Parent as Avalonia.Controls.Panel;

        if (panEditor.Parent is Avalonia.Controls.Panel p) p.Children.Remove(panEditor);
        else if (panEditor.Parent is Avalonia.Controls.ContentControl c) { c.Content = null; c.IsVisible = false; }

        if (targetContainer != null)
        {
            targetContainer.IsVisible = true;
            targetContainer.Content = panEditor;
            
            
            var parentStack = targetContainer.Parent as Avalonia.Controls.StackPanel;
            _hiddenRuleView = parentStack?.Children.OfType<Avalonia.Controls.Border>().FirstOrDefault(b => b.Name == "panRuleWrapper");
            if (_hiddenRuleView != null) { SetTransitionSpeed(_hiddenRuleView, 0); _hiddenRuleView.MaxHeight = 0; _hiddenRuleView.Opacity = 0; }
        }
        else
        {
            _defaultEditorParent?.Children.Add(panEditor);
            if (panAddToggleWrapper != null) { SetTransitionSpeed(panAddToggleWrapper, 0.3); panAddToggleWrapper.MaxHeight = 0; panAddToggleWrapper.Opacity = 0; }
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
          {
              panEditor.MaxHeight = 800;
              panEditor.Opacity = 1;
          });

        if (ruleToEdit == null)
            _ = RevealEditorAsync(panAddToggleWrapper, panEditor);

        if (ruleToEdit == null)
        {
            _editingRuleId = "";
            _exeName = "";
            _iconBase64 = "";
            if (btnSubmit != null) btnSubmit.Content = CrimsonX.Localization.AppStrings.Submit;
            ClearEditor();
        }
        else
        {
            _editingRuleId = ruleToEdit.Id;
            if (btnSubmit != null) btnSubmit.Content = CrimsonX.Localization.AppStrings.Update;
            PreFill(ruleToEdit);
        }

        SetRulesDimmed(true);
    }

    private async System.Threading.Tasks.Task RevealEditorAsync(Avalonia.Controls.Border addToggleWrapper, Avalonia.Controls.Control editor)
    {
        var scroller = this.FindControl<ScrollViewer>("Scroller");
        if (scroller == null || scroller.Content is not Avalonia.Visual content || addToggleWrapper == null) return;

        await System.Threading.Tasks.Task.Delay(16);
        if (editor.Opacity <= 0) return;

        var m = Avalonia.VisualExtensions.TransformToVisual(addToggleWrapper, content);
        if (!m.HasValue) return;
        double target = Math.Max(0, m.Value.Transform(new Avalonia.Point(0, 0)).Y);

        double start = scroller.Offset.Y;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            if (editor.Opacity <= 0) { timer.Stop(); return; }

            double t = Math.Min(1.0, sw.Elapsed.TotalMilliseconds / 300.0);
            double eased = 1.0 - Math.Pow(1.0 - t, 3.0);
            double maxOffset = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
            double y = start + (target - start) * eased;
            scroller.Offset = new Avalonia.Vector(scroller.Offset.X, Math.Max(0, Math.Min(y, maxOffset)));
            if (t >= 1.0) timer.Stop();
        };
        timer.Start();
    }

    private async void CloseEditor(bool instant = false)
{
try
{
var panAddToggle = this.FindControl<Avalonia.Controls.Border>("panAddToggle");
var panAddToggleWrapper = this.FindControl<Avalonia.Controls.Border>("panAddToggleWrapper");
var panEditor    = this.FindControl<Avalonia.Controls.Border>("panEditor");

if (panEditor == null) return;

_isClosing = true;
int editorVersion = ++_editorVersion;

if (panAddToggleWrapper != null) { 
    SetTransitionSpeed(panAddToggleWrapper, 0.3);
    panAddToggleWrapper.IsVisible = true; 
    panAddToggleWrapper.MaxHeight = 800; 
    panAddToggleWrapper.Opacity = 1; 
}
if (_hiddenRuleView != null) { 
    SetTransitionSpeed(_hiddenRuleView, 0.3);
    _hiddenRuleView.IsVisible = true; 
    _hiddenRuleView.MaxHeight = 800; 
    _hiddenRuleView.Opacity = 1; 
}

if (!instant)
{
panEditor.MaxHeight = 0;
panEditor.Opacity = 0;

SetRulesDimmed(false);
await System.Threading.Tasks.Task.Delay(300);
}

if (editorVersion != _editorVersion || !_isClosing) return; 

if (_defaultEditorParent != null)
{
if (panEditor.Parent is Avalonia.Controls.Panel p) p.Children.Remove(panEditor);
else if (panEditor.Parent is Avalonia.Controls.ContentControl c) { c.Content = null; c.IsVisible = false; }

_defaultEditorParent.Children.Add(panEditor);
}

if (instant)
{
panEditor.MaxHeight = 0;
panEditor.Opacity = 0;
}

_editingRuleId = null;

if (_hiddenRuleView != null) { _hiddenRuleView = null; }
_isClosing = false;
SetRulesDimmed(false);
}
catch (Exception ex)
{
    CrimsonX.Services.SimpleLogger.Log(ex);
}
}
    
    private void SetRulesDimmed(bool dimmed)
    {
        var list = this.FindControl<ItemsControl>("lstRules");
        if (list == null) return;

        if (dimmed) list.Classes.Add("editing");
        else list.Classes.Remove("editing");
    }

    private void CloseEditor() => CloseEditor(false);

    
        private void SetTransitionSpeed(Avalonia.Controls.Border? b, double seconds)
        {
            if (b == null || b.Transitions == null) return;
            foreach (var t in b.Transitions)
            {
                if (t is Avalonia.Animation.DoubleTransition dt)
                {
                    dt.Duration = TimeSpan.FromSeconds(seconds);
                }
            }
        }

        private void ClearEditor()
    {
        _displayName  = "";
        _renamingName = false;

        SetComboIndex("cbAppType", 0);

        SetComboIndex("cbTcpRouting", 0);
        SetComboIndex("cbUdpRouting", 2);
        SetComboIndex("cbTcpAdapter", 0);
        SetComboIndex("cbUdpAdapter", 0);
        SetRegionIndex(0);
        RefreshCustomProxyPool("cbCustomProxy");
        ShowCustomProxyInBox("cbCustomProxy", "");
        UpdateCustomAdapterAvailability();
        UpdateIconDisplay();
    }

    private void PreFill(AppGameRule rule)
    {
        SetComboIndex("cbAppType", AppTypeIndex(rule.AppType));

        _exeName    = rule.ExeName;
        _iconBase64 = rule.IconBase64;
        _displayName  = rule.DisplayName ?? "";
        _renamingName = false;
        UpdateIconDisplay();

        SetComboIndex("cbTcpRouting", RoutingIndex(rule.TcpRouting));
        SetComboIndex("cbUdpRouting", RoutingIndex(rule.UdpRouting));

        SetRegionIndex(AppsGamesOverlay.RegionIndexFor(rule.Region));

        SetAdapterByName("cbTcpAdapter", rule.TcpAdapter);
        SetAdapterByName("cbUdpAdapter", rule.UdpAdapter);

        RefreshCustomProxyPool("cbCustomProxy");
        AttachCustomProxyPaste();
        ShowCustomProxyInBox("cbCustomProxy", rule.CustomProxyRaw);

        UpdateCustomAdapterAvailability();
    }

    private void SetComboIndex(string name, int index)
    {
        var cb = this.FindControl<ComboBox>(name);
        if (cb != null && index < cb.ItemCount) cb.SelectedIndex = index;
    }

    private void SetAdapterByName(string comboName, string adapterName)
    {
        var cb = this.FindControl<ComboBox>(comboName);
        if (cb == null) return;
        int idx = _adapterNames.IndexOf(adapterName);
        cb.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void UpdateCustomAdapterAvailability()
    {
        if (!_isReady) return;

        var cbTcpR = this.FindControl<ComboBox>("cbTcpRouting");
        var cbUdpR = this.FindControl<ComboBox>("cbUdpRouting");
        var cbTcpA = this.FindControl<ComboBox>("cbTcpAdapter");
        var cbUdpA = this.FindControl<ComboBox>("cbUdpAdapter");

        bool tcpProxy = cbTcpR == null || AppsGamesOverlay.IsProxyRouting(AppsGamesOverlay.RoutingName(cbTcpR.SelectedIndex));
        bool udpProxy = cbUdpR == null || AppsGamesOverlay.IsProxyRouting(AppsGamesOverlay.RoutingName(cbUdpR.SelectedIndex));

        if (cbTcpA != null)
        {
            if (tcpProxy) cbTcpA.SelectedIndex = 0; 
            cbTcpA.IsEnabled = !tcpProxy;
        }

        if (cbUdpA != null)
        {
            if (udpProxy) cbUdpA.SelectedIndex = 0; 
            cbUdpA.IsEnabled = !udpProxy;
        }
    }

    private void Routing_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateCustomAdapterAvailability();
    }

    // ── Connection region ──

    private bool _suppressRegionToast;

    private void SetRegionIndex(int index)
    {
        bool wasSuppressed = _suppressRegionToast;
        _suppressRegionToast = true;
        try { SetComboIndex("cbRegion", index); }
        finally { _suppressRegionToast = wasSuppressed; }
    }

    private void Region_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isReady || _suppressRegionToast) return;

        var cbRegion = sender as ComboBox ?? this.FindControl<ComboBox>("cbRegion");
        if (cbRegion == null || cbRegion.SelectedIndex <= 0) return;

        MainWindow.Instance?.ShowToast(AS.ConnectionRegionWarning);
    }

    // ── Per-app CUSTOM PROXY (config + ping + save + saved pool) ──

    private bool _isCustomPinging;

    private bool _isValidatingCustomProxy;

    private (string Combo, string PingButton, string TcpAdapter, string UdpAdapter) CustomProxyControls()
        => string.IsNullOrEmpty(_editingDefaultRuleId)
            ? ("cbCustomProxy", "btnCustomProxyPing", "cbTcpAdapter", "cbUdpAdapter")
            : ("cbDefaultCustomProxy", "btnDefaultCustomProxyPing", "cbDefaultTcpAdapter", "cbDefaultUdpAdapter");

    private string ActiveCustomProxyText() => ActiveCustomProxyText(CustomProxyControls().Combo);

    private string ActiveCustomProxyText(string comboName)
    {
        string visible = this.FindControl<ComboBox>(comboName)?.Text?.Trim() ?? "";
        if (visible.Length == 0) return "";

        if (_customProxyRawByCombo.TryGetValue(comboName, out string raw) && raw.Length > 0)
        {
            string label = CrimsonX.Services.AppRulesSingboxBuilder.LabelOf(raw);
            if (string.Equals(visible, raw, StringComparison.Ordinal) || string.Equals(visible, label, StringComparison.Ordinal))
                return raw;
        }

        return visible;
    }

    private readonly Dictionary<string, string> _customProxyRawByCombo = new(StringComparer.Ordinal);
    private bool _customProxyPasteAttached;

    private ConfigBoxPresenter? _configBoxes;

    private ConfigBoxPresenter ConfigBoxes
        => _configBoxes ??= new ConfigBoxPresenter(ReadProxyRaw, WriteProxyRaw, CrimsonX.Services.ConfigConverter.LabelFor);

    private string ReadProxyRaw(string key)
        => _customProxyRawByCombo.TryGetValue(key, out string raw) ? raw : "";

    private void WriteProxyRaw(string key, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) _customProxyRawByCombo.Remove(key);
        else _customProxyRawByCombo[key] = raw.Trim();
    }

    private void ShowCustomProxyInBox(string comboName, string raw)
    {
        AttachCustomProxyPaste();
        WriteProxyRaw(comboName, raw);
        ConfigBoxes.Show(comboName, raw);
    }

    private async Task ShowPastedProxyAsync(string comboName, string text)
    {
        var verdict = await CrimsonX.Services.ConfigIntake.AcceptAsync(
            text, _cfg, CrimsonX.Services.ConfigTarget.Singbox, AS.TabAppsGames);

        if (!verdict.Accepted)
        {
            _customProxyRawByCombo.Remove(comboName);

            var box = this.FindControl<ComboBox>(comboName);
            if (box != null) box.Text = text;

            MainWindow.Instance?.ShowToast(verdict.Toast, ToastKind.Error);
            return;
        }

        if (verdict.Toast.Length > 0) MainWindow.Instance?.ShowToast(verdict.Toast, ToastKind.Error);

        ShowCustomProxyInBox(comboName, verdict.Raw);
    }

    private void AttachCustomProxyPaste()
    {
        if (_customProxyPasteAttached) return;

        var cb = this.FindControl<ComboBox>("cbCustomProxy");
        var def = this.FindControl<ComboBox>("cbDefaultCustomProxy");
        if (cb == null || def == null) return;

        ConfigBoxes.Attach("cbCustomProxy", cb);
        ConfigBoxes.Attach("cbDefaultCustomProxy", def);

        ConfigBoxes.AttachPaste("cbCustomProxy", text => ShowPastedProxyAsync("cbCustomProxy", text));
        ConfigBoxes.AttachPaste("cbDefaultCustomProxy", text => ShowPastedProxyAsync("cbDefaultCustomProxy", text));

        _customProxyPasteAttached = true;
    }

    private (string Name, string Ip) ActiveCustomProxyAdapter()
    {
        var boxes = CustomProxyControls();
        string tcp = AdapterNameFromCombo(boxes.TcpAdapter);
        string udp = AdapterNameFromCombo(boxes.UdpAdapter);

        string name = !string.Equals(tcp, "Default", StringComparison.OrdinalIgnoreCase) ? tcp
                    : !string.Equals(udp, "Default", StringComparison.OrdinalIgnoreCase) ? udp
                    : "Default";

        return (name, AdapterIpFor(name));
    }

    private string AdapterNameFromCombo(string comboName)
    {
        int i = this.FindControl<ComboBox>(comboName)?.SelectedIndex ?? 0;
        return i >= 0 && i < _adapterNames.Count ? _adapterNames[i] : "Default";
    }

    private static string AdapterIpFor(string adapterName)
    {
        if (string.IsNullOrWhiteSpace(adapterName) || string.Equals(adapterName, "Default", StringComparison.OrdinalIgnoreCase))
            return "";

        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.Name == adapterName);
            var ipv4 = nic?.GetIPProperties().UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
            return ipv4?.Address?.ToString() ?? "";
        }
        catch { return ""; }
    }

    private bool _suppressCustomPoolSync;

    private readonly Dictionary<string, List<AppCustomConfigEntry>> _customProxyEntries = new(StringComparer.Ordinal);

    private void RefreshCustomProxyPool(string comboName)
    {
        var cb = this.FindControl<ComboBox>(comboName);
        if (cb == null) return;

        string text = cb.Text ?? "";

        var entries = AppCustomConfigStore.Load(_cfg);

        bool wasSuppressed = _suppressCustomPoolSync;
        _suppressCustomPoolSync = true;
        try
        {
            _customProxyEntries[comboName] = entries;
            cb.ItemsSource = AppCustomConfigStore.DisplayOptions(entries);
            cb.SelectedIndex = -1;   
            cb.Text = text;
        }
        finally
        {
            _suppressCustomPoolSync = wasSuppressed;
        }
    }

    private async void CustomProxyPing_Click(object? sender, RoutedEventArgs e)
    {
        if (_isCustomPinging) return;

        var btn = sender as Button;
        try
        {
            string raw = ActiveCustomProxyText();
            if (raw.Length == 0)
            {
                MainWindow.Instance?.ShowToast(AS.CustomProxyEmpty, ToastKind.Error);
                return;
            }

            _isCustomPinging = true;
            string original = btn?.Content?.ToString() ?? AS.PingBtn;
            if (btn != null) { btn.Content = AS.ValidatingConfig; btn.IsEnabled = false; }

            var (adapter, adapterIp) = ActiveCustomProxyAdapter();

            using var cts = new System.Threading.CancellationTokenSource(15000);
            var res = await CrimsonX.Services.CustomConfigPinger.ProbeAsync(raw, _cfg, adapter, adapterIp, cts.Token);
            bool measured = res != null && res.Measured;
            bool timedOut = !measured && res != null && res.TimedOut;

            if (btn != null) { btn.Content = original; btn.IsEnabled = true; }

            string label = CrimsonX.Services.AppRulesSingboxBuilder.LabelOf(raw);
            string msg = measured ? res.Text(label)
                       : timedOut ? AS.CustomProxyNoResponse
                       : AS.InvalidConfig + (res != null && res.Reason.Length > 0 ? " " + AS.InvalidConfigReason + res.Reason : "");
            MainWindow.Instance?.ShowToast(msg, measured ? ToastKind.Success : ToastKind.Error);
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
            MainWindow.Instance?.ShowToast(AS.InvalidConfig, ToastKind.Error);
        }
        finally
        {
            _isCustomPinging = false;
            if (btn != null && !btn.IsEnabled) { btn.Content = AS.PingBtn; btn.IsEnabled = true; }
        }
    }

    private void CustomProxySave_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            string raw = ActiveCustomProxyText();
            if (raw.Length == 0)
            {
                MainWindow.Instance?.ShowToast(AS.CustomProxyEmpty, ToastKind.Error);
                return;
            }

            var result = AppCustomConfigStore.Store(_cfg, raw, out var label);
            switch (result)
            {
                case CustomConfigSaveResult.Saved:
                case CustomConfigSaveResult.Updated:
                    MainWindow.Instance?.ShowToast($"{AS.ToastCustomProxySaved}: {label}", ToastKind.Success);
                    RefreshCustomProxyPool(CustomProxyControls().Combo);
                    break;

                case CustomConfigSaveResult.PoolFull:
                    MainWindow.Instance?.ShowToast(AS.ToastCustomProxyPoolFull, ToastKind.Error);
                    break;

                default:
                    MainWindow.Instance?.ShowToast(AS.ToastCustomProxyInvalid, ToastKind.Error);
                    break;
            }
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
        }
    }
    private async void CustomProxyImport_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var page = CrimsonX.Pages.SettingsPage.Instance;
            if (page == null) return;

            string text = await page.PickConfigFileTextAsync();
            if (text.Length > 0) await ShowPastedProxyAsync(CustomProxyControls().Combo, text);
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
        }
    }
    // ── Custom proxy hint dialog (shared by the custom and default rule editors) ──

    private async void CustomProxyHint_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new CrimsonX.Dialogs.CustomProxyHintDialog();
            await dialog.ShowDialog<string>(MainWindow.Instance);
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
        }
    }

    private void ApplyCustomProxyToRule(AppGameRule rule, string comboName)
    {
        rule.CustomProxyRaw = ActiveCustomProxyText();
        rule.CustomProxyLabel = rule.CustomProxyRaw.Length > 0
            ? CrimsonX.Services.AppRulesSingboxBuilder.LabelOf(rule.CustomProxyRaw)
            : "";
    }
    private async Task<bool> ValidateCustomProxyBeforeSubmitAsync(string comboName, ComboBox? tcpRouting, ComboBox? udpRouting)
    {
        bool tcpCustom = AppsGamesOverlay.IsCustomRouting(AppsGamesOverlay.RoutingName(tcpRouting?.SelectedIndex ?? 0));
        bool udpCustom = AppsGamesOverlay.IsCustomRouting(AppsGamesOverlay.RoutingName(udpRouting?.SelectedIndex ?? 0));
        if (!tcpCustom && !udpCustom) return true;

        if (_isValidatingCustomProxy) return false;
        _isValidatingCustomProxy = true;

        var submit = this.FindControl<Button>(string.IsNullOrEmpty(_editingDefaultRuleId) ? "btnDefaultSubmit" : "btnSubmit");
        string submitLabel = submit?.Content?.ToString() ?? AS.Submit;
        if (submit != null) { submit.Content = AS.ValidatingConfig; submit.IsEnabled = false; }

        try
        {
            var cb = this.FindControl<ComboBox>(comboName);

            var (adapter, adapterIp) = ActiveCustomProxyAdapter();
            string sbDir = _cfg?.SbDir ?? "";
            string raw = ActiveCustomProxyText(comboName);

            string reason = "";
            var result = await Task.Run(() => SingboxConfigValidator.ValidateEditorConfig(sbDir, raw, out reason, adapter, adapterIp));

            switch (result)
            {
                case CustomProxyCheckResult.Ok:
                    return true;

                case CustomProxyCheckResult.Missing:
                    MainWindow.Instance?.ShowToast(AS.CustomProxyRequired, ToastKind.Error);
                    break;

                case CustomProxyCheckResult.Unparsable:
                    RefuseCustomProxy(AS.ToastCustomProxyInvalid, reason);
                    break;

                case CustomProxyCheckResult.NeedsCredentials:
                {
                    bool stored = false;
                    if (CrimsonX.Services.TunnelConfigParser.TryParse(raw, out var tunnel) && tunnel != null && tunnel.Success)
                    {
                        stored = await CrimsonX.Services.TunnelCredentialResolver.ApplyAsync(tunnel);
                    }

                    if (!stored)
                    {
                        MainWindow.Instance?.ShowToast(AS.ToastTunnelNeedsCredentials, ToastKind.Error);
                        FocusConfigBox(cb);
                        return false;
                    }

                    var retry = await Task.Run(() => SingboxConfigValidator.ValidateEditorConfig(sbDir, raw, out reason, adapter, adapterIp));
                    if (retry == CustomProxyCheckResult.Ok) return true;

                    RefuseCustomProxy(retry == CustomProxyCheckResult.Rejected
                        ? AS.ToastCustomProxyRejected
                        : AS.ToastCustomProxyInvalid, reason);
                    FocusConfigBox(cb);
                    return false;
                }

                default:
                    RefuseCustomProxy(AS.ToastCustomProxyRejected, reason);
                    break;
            }

            FocusConfigBox(cb);
            return false;
        }
        finally
        {
            if (submit != null) { submit.Content = submitLabel; submit.IsEnabled = true; }
            _isValidatingCustomProxy = false;
        }
    }

    private void RefuseCustomProxy(string headline, string reason)
    {
        string note = CrimsonX.Services.ConfigValidator.ShortReason(reason ?? "");

        MainWindow.Instance?.ShowToast(
            note.Length > 0 ? $"{headline} {AS.InvalidConfigReason}{note}" : headline, ToastKind.Error);

        if (_cfg?.DebugMode ?? false)
            CrimsonX.Services.SimpleLogger.Log($"[AppsGames] custom proxy refused: {reason}");
    }

    private static void FocusConfigBox(ComboBox? cb)
    {
        if (cb == null) return;

        var inner = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(cb)
            .OfType<TextBox>()
            .FirstOrDefault();

        if (inner != null)
        {
            inner.Focus();
            inner.SelectAll();
        }
        else
        {
            cb.Focus();
        }
    }
    private void CustomProxySaved_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isReady || _suppressCustomPoolSync) return;
        if (sender is not ComboBox cb) return;
        if (cb.SelectedIndex < 0) return;

        string boxName = ReferenceEquals(cb, this.FindControl<ComboBox>("cbCustomProxy"))
            ? "cbCustomProxy"
            : "cbDefaultCustomProxy";

        string raw = "";
        if (cb.SelectedIndex > 0)
        {
            int index = cb.SelectedIndex - 1;
            if (!_customProxyEntries.TryGetValue(boxName, out var listed) || index >= listed.Count) return;
            raw = listed[index].Raw;
        }

        ShowCustomProxyInBox(boxName, raw);
    }

    private void UpdateIconDisplay()
    {
        var imgIcon       = this.FindControl<Image>("imgAppIcon");
        var iconHolder    = this.FindControl<Border>("iconPlaceholder");
        var iconPanel     = this.FindControl<Panel>("panAppIcon");
        var lblExeName    = this.FindControl<TextBlock>("lblExeName");
        var lblHeader     = this.FindControl<TextBlock>("lblEditorHeader");

        bool hasExe  = !string.IsNullOrWhiteSpace(_exeName);
        bool hasIcon = hasExe && !string.IsNullOrEmpty(_iconBase64);

        if (iconPanel != null) iconPanel.IsVisible = hasExe;

        if (imgIcon != null)
        {
            imgIcon.IsVisible = hasIcon;
            if (hasIcon)
            {
                try
                {
                    var bytes = Convert.FromBase64String(_iconBase64);
                    imgIcon.Source = new Bitmap(new MemoryStream(bytes));
                }
                catch { imgIcon.IsVisible = false; }
            }
        }
        if (iconHolder != null) iconHolder.IsVisible = hasExe && !hasIcon;

        if (lblExeName != null)
        {
            lblExeName.Text      = DisplayLabel();
            lblExeName.IsVisible = hasExe && !_renamingName;
        }

        var nameBox = this.FindControl<TextBox>("txtDisplayName");
        if (nameBox != null) nameBox.IsVisible = hasExe && _renamingName;

        if (lblHeader != null) lblHeader.IsVisible = !hasExe;
    }

    private string DisplayLabel()
        => string.IsNullOrWhiteSpace(_displayName) ? _exeName : _displayName;


    private void NameLabel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_exeName)) return;
        if (sender is not Control source || !e.GetCurrentPoint(source).Properties.IsLeftButtonPressed) return;

        var box = this.FindControl<TextBox>("txtDisplayName");
        if (box == null) return;

        box.Text      = DisplayLabel();
        _renamingName = true;
        UpdateIconDisplay();
        e.Handled = true;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            box.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void NameEdit_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitDisplayNameRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _renamingName = false;
            UpdateIconDisplay();
            e.Handled = true;
        }
    }

    private void NameEdit_LostFocus(object? sender, RoutedEventArgs e) => CommitDisplayNameRename();

    private void CommitDisplayNameRename()
    {
        if (!_renamingName) return;
        _renamingName = false;

        string typed = (this.FindControl<TextBox>("txtDisplayName")?.Text ?? "").Trim();
        _displayName = string.Equals(typed, _exeName, StringComparison.OrdinalIgnoreCase) ? "" : typed;

        UpdateIconDisplay();
    }

    private async void BtnBrowse_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
        var mainWindow = MainWindow.Instance;
        if (mainWindow == null) return;

        var options = new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select Executable",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Executables") { Patterns = new[] { "*.exe" } },
                new Avalonia.Platform.Storage.FilePickerFileType("All Files")   { Patterns = new[] { "*.*"   } }
            }
        };

        var result = await mainWindow.StorageProvider.OpenFilePickerAsync(options);
        if (result == null || result.Count == 0) return;

        var file      = result[0];
        var localPath = file.Path.LocalPath;

        if (!string.Equals(file.Name, _exeName, StringComparison.OrdinalIgnoreCase))
            _displayName = "";

        _renamingName = false;
        _exeName      = file.Name;

        _iconBase64 = "";
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            try
            {
                using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(localPath);
                if (sysIcon != null)
                {
                    using var bmp = sysIcon.ToBitmap();
                    using var ms  = new MemoryStream();
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    _iconBase64 = Convert.ToBase64String(ms.ToArray());
                }
            }
            catch { }
        }

        UpdateIconDisplay();
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
        }
    }

    private async void BtnSubmit_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_exeName)) return;

        CommitDisplayNameRename();

        var cbTcpR = this.FindControl<ComboBox>("cbTcpRouting");
        var cbUdpR = this.FindControl<ComboBox>("cbUdpRouting");
        var cbTcpA = this.FindControl<ComboBox>("cbTcpAdapter");
        var cbUdpA = this.FindControl<ComboBox>("cbUdpAdapter");
        var cbAppType = this.FindControl<ComboBox>("cbAppType");

        string appType = AppTypeName(cbAppType?.SelectedIndex ?? 0);

        string GetRouting(ComboBox? cb) => AppsGamesOverlay.RoutingName(cb?.SelectedIndex ?? 0);
        string GetAdapter(ComboBox? cb)
        {
            int i = cb?.SelectedIndex ?? 0;
            return i < _adapterNames.Count ? _adapterNames[i] : "Default";
        }

        if (!await ValidateCustomProxyBeforeSubmitAsync("cbCustomProxy", cbTcpR, cbUdpR)) return;

        AppGameRule rule;
        if (!string.IsNullOrEmpty(_editingRuleId))
        {
            rule = _rules.FirstOrDefault(r => r.Id == _editingRuleId) ?? new AppGameRule();
        }
        else
        {
            rule = new AppGameRule();
            _rules.Add(rule);
            rule.IsEnabled = false;
        }
        rule.AppType    = appType;
        rule.ExeName    = _exeName;

        rule.DisplayName = _displayName ?? "";

        if (string.IsNullOrEmpty(rule.DefaultKey))
        {
            rule.ProcessNames = new List<string> { _exeName };
        }
        else if (rule.ProcessNames == null || rule.ProcessNames.Count == 0)
        {
            rule.ProcessNames = new List<string> { _exeName };
        }
        else if (!rule.ProcessNames.Any(n => string.Equals(n, _exeName, StringComparison.OrdinalIgnoreCase)))
        {
            rule.ProcessNames.Add(_exeName);
        }

        rule.IconBase64 = _iconBase64;
        var cbRegion = this.FindControl<ComboBox>("cbRegion");
        rule.Region = RegionForIndex(cbRegion?.SelectedIndex ?? 0);
        rule.TcpRouting = GetRouting(cbTcpR);
        rule.UdpRouting = GetRouting(cbUdpR);
        rule.TcpAdapter = GetAdapter(cbTcpA);
        rule.UdpAdapter = GetAdapter(cbUdpA);

        var cbCustomProxy = this.FindControl<ComboBox>("cbCustomProxy");
        if (cbCustomProxy != null) cbCustomProxy.Text = cbCustomProxy.Text?.Trim() ?? "";
        ApplyCustomProxyToRule(rule, "cbCustomProxy");

        SaveRules(rule.IsEnabled);
        RefreshList();
        CloseEditor();
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        CloseEditor();
    }

    // DEFAULT (CURATED) RULE LOGIC

    // ── Default Rule Editor (Region Rules) ──

    private void DefaultCountry_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.Tag is string ruleId)
        {
            var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule == null || string.IsNullOrEmpty(rule.Country)) return;

            string country = cb.SelectedIndex switch { 1 => "IRAN", 2 => "UAE", _ => "EVERYWHERE" };
            if (rule.Country == country) return;

            rule.Country = country;
            rule.TcpRouting = country == "UAE" ? "Direct" : "Proxy";
            rule.UdpRouting = country == "IRAN" ? "Direct" : "Proxy";

            if (rule.TcpRouting == "Proxy") rule.TcpAdapter = "Default";
            if (rule.UdpRouting == "Proxy") rule.UdpAdapter = "Default";

            SaveRules();
            RefreshList();
        }
    }

    private void DefaultRegion_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.Tag is string ruleId)
        {
            var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule == null || string.IsNullOrEmpty(rule.Region)) return;

            string region = RegionForIndex(cb.SelectedIndex);
            if (rule.Region == region) return;

            rule.Region = region;
            SaveRules();
            RefreshList();
        }
    }

    // ── Launcher DIRECT / PROXY Pills ──

    private void LauncherDirect_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.Button btn && btn.Tag is string ruleId)
            SetLauncherRouting(ruleId, "Direct");
    }

    private void LauncherProxy_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.Button btn && btn.Tag is string ruleId)
            SetLauncherRouting(ruleId, "Proxy");
    }

    private void SetLauncherRouting(string ruleId, string mode)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
        if (rule == null) return;
        if (string.Equals(rule.TcpRouting, mode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(rule.UdpRouting, mode, StringComparison.OrdinalIgnoreCase)) return;

        rule.TcpRouting = mode;
        rule.UdpRouting = mode;
        if (string.Equals(mode, "Proxy", StringComparison.OrdinalIgnoreCase))
        {
            rule.TcpAdapter = "Default";
            rule.UdpAdapter = "Default";
        }
        SaveRules();
        RefreshList();
    }

    private void EditDefaultRule_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.Button btn && btn.Tag is string ruleId)
        {
            var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule == null) return;

            var stackPanel = Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(btn)
                .OfType<Avalonia.Controls.StackPanel>()
                .FirstOrDefault(sp => sp.Children.OfType<Avalonia.Controls.ContentControl>().Any(c => c.Name == "EditContainer"));
            var container = stackPanel?.Children.OfType<Avalonia.Controls.ContentControl>().FirstOrDefault(c => c.Name == "EditContainer");
            OpenDefaultEditor(rule, container);
        }
    }

    private static bool SupportsRoutingEditor(AppGameRule rule) =>
        !string.IsNullOrEmpty(rule.DefaultKey);

    private static AppGameRule? CreateDefaultPreset(string key) => key switch
    {
        DiscordDefaultKey => CreateDiscordDefaultRule(),
        Cs2DefaultKey => CreateCs2DefaultRule(),
        ApexDefaultKey => CreateApexDefaultRule(),
        DeadlockDefaultKey => CreateDeadlockDefaultRule(),
        EfootballDefaultKey => CreateEfootballDefaultRule(),
        Tekken8DefaultKey => CreateTekken8DefaultRule(),
        RocketLeagueDefaultKey => CreateRocketLeagueDefaultRule(),
        Dota2DefaultKey => CreateDota2DefaultRule(),
        LeagueDefaultKey => CreateLeagueDefaultRule(),
        ValorantDefaultKey => CreateValorantDefaultRule(),
        Bf6DefaultKey => CreateBf6DefaultRule(),
        Titanfall2DefaultKey => CreateTitanfall2DefaultRule(),
        MarvelRivalsDefaultKey => CreateMarvelRivalsDefaultRule(),
        IRacingDefaultKey => CreateIRacingDefaultRule(),
        EaAppDefaultKey => CreateEaAppDefaultRule(),
        UbisoftDefaultKey => CreateUbisoftDefaultRule(),
        EpicDefaultKey => CreateEpicDefaultRule(),
        SteamDefaultKey => CreateSteamDefaultRule(),
        XboxDefaultKey => CreateXboxDefaultRule(),
        RiotDefaultKey => CreateRiotDefaultRule(),
        BattleNetDefaultKey => CreateBattleNetDefaultRule(),
        TelegramDefaultKey => CreateTelegramDefaultRule(),
        WhatsAppDefaultKey => CreateWhatsAppDefaultRule(),
        BraveDefaultKey => CreateBraveDefaultRule(),
        ChromeDefaultKey => CreateChromeDefaultRule(),
        EdgeDefaultKey => CreateEdgeDefaultRule(),
        FirefoxDefaultKey => CreateFirefoxDefaultRule(),
        _ => null
    };

    private void ApplyDefaultEditorValues(AppGameRule rule)
    {
        bool tcpProxy = AppsGamesOverlay.IsProxyRouting(rule.TcpRouting);
        bool udpProxy = AppsGamesOverlay.IsProxyRouting(rule.UdpRouting);

        SetAdapterByName("cbDefaultTcpAdapter", tcpProxy ? "Default" : rule.TcpAdapter);
        var cbTcpA = this.FindControl<ComboBox>("cbDefaultTcpAdapter");
        if (cbTcpA != null) cbTcpA.IsEnabled = !tcpProxy;

        SetAdapterByName("cbDefaultUdpAdapter", udpProxy ? "Default" : rule.UdpAdapter);
        var cbUdpA = this.FindControl<ComboBox>("cbDefaultUdpAdapter");
        if (cbUdpA != null) cbUdpA.IsEnabled = !udpProxy;

        var cbTcpR = this.FindControl<ComboBox>("cbDefaultTcpRouting");
        var cbUdpR = this.FindControl<ComboBox>("cbDefaultUdpRouting");
        if (cbTcpR != null && cbTcpR.ItemCount > 0)
            cbTcpR.SelectedIndex = RoutingIndex(rule.TcpRouting);
        if (cbUdpR != null && cbUdpR.ItemCount > 0)
            cbUdpR.SelectedIndex = RoutingIndex(rule.UdpRouting);

        RefreshCustomProxyPool("cbDefaultCustomProxy");
        AttachCustomProxyPaste();
        ShowCustomProxyInBox("cbDefaultCustomProxy", rule.CustomProxyRaw);
    }

    private void DefaultRestore_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_editingDefaultRuleId)) return;

        var rule = _rules.FirstOrDefault(r => r.Id == _editingDefaultRuleId);
        if (rule == null || string.IsNullOrEmpty(rule.DefaultKey)) return;

        var preset = CreateDefaultPreset(rule.DefaultKey);
        if (preset == null) return;

        rule.Country = preset.Country;
        rule.Region = preset.Region;
        rule.TcpRouting = preset.TcpRouting;
        rule.UdpRouting = preset.UdpRouting;
        rule.TcpAdapter = preset.TcpAdapter;
        rule.UdpAdapter = preset.UdpAdapter;

        SaveRules();
        RefreshList();
        ApplyDefaultEditorValues(rule);
    }

    private void UpdateDefaultEditorHeader(AppGameRule rule)
    {
        var hdr = this.FindControl<Avalonia.Controls.TextBlock>("lblDefaultEditorHeader");
        if (hdr != null) hdr.Text = string.IsNullOrEmpty(rule.ExeName) ? "APP" : rule.ExeName.ToUpperInvariant();

        var img    = this.FindControl<Avalonia.Controls.Image>("imgDefaultEditorIcon");
        var holder = this.FindControl<Avalonia.Controls.Border>("defaultEditorIconPlaceholder");
        var bmp    = AppRuleViewModel.RuleIcon(rule);

        if (img != null)
        {
            img.Source    = bmp;
            img.IsVisible = bmp != null;
        }
        if (holder != null) holder.IsVisible = bmp == null;
    }

    private void OpenDefaultEditor(AppGameRule rule, Avalonia.Controls.ContentControl? targetContainer)
    {
        CloseEditor(true);
        CloseDefaultEditor(true);

        var panDefaultEditor = this.FindControl<Avalonia.Controls.Border>("panDefaultEditor");
        if (panDefaultEditor == null) return;

        if (_defaultRuleEditorParent == null)
            _defaultRuleEditorParent = panDefaultEditor.Parent as Avalonia.Controls.Panel;

        if (panDefaultEditor.Parent is Avalonia.Controls.Panel p) p.Children.Remove(panDefaultEditor);
        else if (panDefaultEditor.Parent is Avalonia.Controls.ContentControl c) { c.Content = null; c.IsVisible = false; }

        if (targetContainer == null)
        {
            _defaultRuleEditorParent?.Children.Add(panDefaultEditor);
            return;
        }

        targetContainer.IsVisible = true;
        targetContainer.Content = panDefaultEditor;

        var parentStack = targetContainer.Parent as Avalonia.Controls.StackPanel;
        _hiddenDefaultRuleView = parentStack?.Children.OfType<Avalonia.Controls.Border>().FirstOrDefault(b => b.Name == "panDefaultRuleWrapper");
        if (_hiddenDefaultRuleView != null) { SetTransitionSpeed(_hiddenDefaultRuleView, 0); _hiddenDefaultRuleView.MaxHeight = 0; _hiddenDefaultRuleView.Opacity = 0; }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            panDefaultEditor.MaxHeight = 800;
            panDefaultEditor.Opacity = 1;
        });

        _editingDefaultRuleId = rule.Id;
        UpdateDefaultEditorHeader(rule);

        var panDefaultRouting = this.FindControl<Avalonia.Controls.Border>("panDefaultRouting");
        if (panDefaultRouting != null) panDefaultRouting.IsVisible = SupportsRoutingEditor(rule);

        ApplyDefaultEditorValues(rule);

        SetRulesDimmed(true);
    }

    private async void CloseDefaultEditor(bool instant = false)
    {
        try
        {
        var panDefaultEditor = this.FindControl<Avalonia.Controls.Border>("panDefaultEditor");
        if (panDefaultEditor == null) return;

        _isClosingDefaultEditor = true;

        if (_hiddenDefaultRuleView != null)
        {
            SetTransitionSpeed(_hiddenDefaultRuleView, 0.3);
            _hiddenDefaultRuleView.IsVisible = true;
            _hiddenDefaultRuleView.MaxHeight = 800;
            _hiddenDefaultRuleView.Opacity = 1;
        }

        if (!instant)
        {
            panDefaultEditor.MaxHeight = 0;
            panDefaultEditor.Opacity = 0;

            SetRulesDimmed(false);
            await System.Threading.Tasks.Task.Delay(300);
        }

        if (!_isClosingDefaultEditor) return;

        if (_defaultRuleEditorParent != null)
        {
            if (panDefaultEditor.Parent is Avalonia.Controls.Panel p) p.Children.Remove(panDefaultEditor);
            else if (panDefaultEditor.Parent is Avalonia.Controls.ContentControl c) { c.Content = null; c.IsVisible = false; }

            _defaultRuleEditorParent.Children.Add(panDefaultEditor);
        }

        if (instant)
        {
            panDefaultEditor.MaxHeight = 0;
            panDefaultEditor.Opacity = 0;
        }

        _editingDefaultRuleId = "";
        _hiddenDefaultRuleView = null;
        _isClosingDefaultEditor = false;
        SetRulesDimmed(false);
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
        }
    }

    private void DefaultRouting_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var cbTcpR = this.FindControl<ComboBox>("cbDefaultTcpRouting");
        var cbUdpR = this.FindControl<ComboBox>("cbDefaultUdpRouting");
        var cbTcpA = this.FindControl<ComboBox>("cbDefaultTcpAdapter");
        var cbUdpA = this.FindControl<ComboBox>("cbDefaultUdpAdapter");
        if (cbTcpA != null && cbTcpR != null) cbTcpA.IsEnabled = !AppsGamesOverlay.IsProxyRouting(AppsGamesOverlay.RoutingName(cbTcpR.SelectedIndex));
        if (cbUdpA != null && cbUdpR != null) cbUdpA.IsEnabled = !AppsGamesOverlay.IsProxyRouting(AppsGamesOverlay.RoutingName(cbUdpR.SelectedIndex));
    }

    private async void DefaultSubmit_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_editingDefaultRuleId)) { CloseDefaultEditor(); return; }

        var rule = _rules.FirstOrDefault(r => r.Id == _editingDefaultRuleId);
        if (rule != null)
        {
            if (SupportsRoutingEditor(rule))
            {
                var cbTcpR = this.FindControl<ComboBox>("cbDefaultTcpRouting");
                var cbUdpR = this.FindControl<ComboBox>("cbDefaultUdpRouting");

                if (!await ValidateCustomProxyBeforeSubmitAsync("cbDefaultCustomProxy", cbTcpR, cbUdpR)) return;

                rule.TcpRouting = AppsGamesOverlay.RoutingName(cbTcpR?.SelectedIndex ?? 0);
                rule.UdpRouting = AppsGamesOverlay.RoutingName(cbUdpR?.SelectedIndex ?? 0);
            }

            var cbTcpA = this.FindControl<ComboBox>("cbDefaultTcpAdapter");
            var cbUdpA = this.FindControl<ComboBox>("cbDefaultUdpAdapter");
            string GetAdapter(ComboBox? cb)
            {
                int i = cb?.SelectedIndex ?? 0;
                return i >= 0 && i < _adapterNames.Count ? _adapterNames[i] : "Default";
            }
            rule.TcpAdapter = GetAdapter(cbTcpA);
            rule.UdpAdapter = GetAdapter(cbUdpA);
            if (AppsGamesOverlay.IsProxyRouting(rule.TcpRouting)) rule.TcpAdapter = "Default";
            if (AppsGamesOverlay.IsProxyRouting(rule.UdpRouting)) rule.UdpAdapter = "Default";

            var cbCustomProxy = this.FindControl<ComboBox>("cbDefaultCustomProxy");
            if (cbCustomProxy != null) cbCustomProxy.Text = cbCustomProxy.Text?.Trim() ?? "";
            ApplyCustomProxyToRule(rule, "cbDefaultCustomProxy");

            SaveRules();
            RefreshList();
        }
        CloseDefaultEditor();
    }

    private void DefaultCancel_Click(object? sender, RoutedEventArgs e)
    {
        CloseDefaultEditor();
    }

        protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty && IsVisible)
            {
                UpdateOverlaySplitUI();
                ApplyMasterRulesVisual();
                UpdateOverlayConnectUI();
            }
        }

    // ── Overlay Split Rules & Master Toggle ──

        public void UpdateOverlaySplitUI()
        {
            this.FindControl<Avalonia.Controls.Button>("btnOverlaySplitRegular")?.Classes.Remove("activeOpt");
            this.FindControl<Avalonia.Controls.Button>("btnOverlaySplitInclusive")?.Classes.Remove("activeOpt");

            string mode = _cfg.SplitTunnelMode ?? "DISABLED";
            if (mode == "INCLUSIVE")
                this.FindControl<Avalonia.Controls.Button>("btnOverlaySplitInclusive")?.Classes.Add("activeOpt");
            else
                this.FindControl<Avalonia.Controls.Button>("btnOverlaySplitRegular")?.Classes.Add("activeOpt");
        }

        private void OverlaySplitTunnel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is Avalonia.Controls.Button clickedBtn)
            {
                string oldMode = _cfg.SplitTunnelMode ?? "DISABLED";
                
                if (clickedBtn.Name == "btnOverlaySplitRegular") _cfg.SplitTunnelMode = "DISABLED";
                else if (clickedBtn.Name == "btnOverlaySplitInclusive") _cfg.SplitTunnelMode = "INCLUSIVE";

                if (oldMode == _cfg.SplitTunnelMode) return;

                _cfg.EnableDirect = _cfg.SplitTunnelMode != "DISABLED";

                UpdateOverlaySplitUI();
                MainWindow.Instance.RequestSave();

                _hasPendingRuleChanges = true;
                UpdateOverlayConnectUI();

                if (_state.IsEngineRunning && !string.Equals(_cfg.LastXrayMode, "VPN Mode", StringComparison.OrdinalIgnoreCase))
                {
                    MainWindow.Instance.RestartXray();
                }
            }
        }

        // MASTER RULES BUTTON + OVERLAY CONNECT BUTTON

        private void MasterPill_Click(object? sender, RoutedEventArgs e)
        {
            if (!_isReady) return;

            bool on = sender is Avalonia.Controls.Button b && b.Name == "btnMasterOn";
            if (_cfg.EnableAppRules == on) return;

            _cfg.EnableAppRules = on;
            MainWindow.Instance.RequestSave();
            UpdateMasterRulesVisual();

            _hasPendingRuleChanges = true;
            UpdateOverlayConnectUI();
        }

        private void ApplyMasterRulesVisual()
        {
            UpdateMasterRulesVisual();
        }

        private void UpdateMasterRulesVisual()
        {
            bool on = _cfg.EnableAppRules;

            this.FindControl<Avalonia.Controls.Button>("btnMasterOn")?.Classes.Remove("activeOpt");
            this.FindControl<Avalonia.Controls.Button>("btnMasterOff")?.Classes.Remove("activeOpt");
            this.FindControl<Avalonia.Controls.Button>(on ? "btnMasterOn" : "btnMasterOff")?.Classes.Add("activeOpt");

            var box = this.FindControl<Border>("panRulesBox");
            if (box == null) return;
            box.Opacity = on ? 1.0 : 0.45;
            box.IsHitTestVisible = on;
        }

    // ── Overlay Connect & Progress Fill ──


        private bool _connectBoxPressed;

        private void ConnectBoxPress(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Control control) e.Pointer.Capture(control);
            _connectBoxPressed = true;
            SetConnectBoxPressed(true);
        }

        private void ConnectBoxRelease(object? sender, PointerReleasedEventArgs e)
        {
            bool wasPressed = _connectBoxPressed;
            _connectBoxPressed = false;
            e.Pointer.Capture(null);
            SetConnectBoxPressed(false);

            if (!wasPressed || sender is not Control control) return;

            var pos = e.GetPosition(control);
            bool stillOverBox = pos.X >= 0 && pos.Y >= 0
                                && pos.X <= control.Bounds.Width && pos.Y <= control.Bounds.Height;
            if (!stillOverBox) return;

            OverlayConnect_Click(sender, e);
        }

        private void ConnectBoxCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            _connectBoxPressed = false;
            SetConnectBoxPressed(false);
        }

        private void SetConnectBoxPressed(bool pressed)
        {
            var inner = this.FindControl<Panel>("panConnectBoxInner");
            if (inner != null)
            {
                double s = pressed ? 0.97 : 1.0;
                inner.RenderTransform = new Avalonia.Media.ScaleTransform(s, s);
            }

            var flash = this.FindControl<Border>("panConnectBoxPress");
            if (flash != null) flash.Opacity = pressed ? 1.0 : 0.0;
        }

        private async void OverlayConnect_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
            if (CrimsonX.Services.ConnectPhaseUi.ConnectClickMeansDisconnect(_state.IsConnected, _state.IsEngineRunning, _state.IsReconnecting))
            {
                MainWindow.Instance.ConnectDisconnect();
                UpdateOverlayConnectUI();
                return;
            }

            if (!_state.IsEngineRunning && !_state.IsConnected)
            {
                string mode = _cfg.LastXrayMode ?? "Proxy Mode";
                if (mode == "Clear Proxy" || mode == "Proxy Mode")
                {
                    var dialog = new CrimsonX.Dialogs.ConfirmDialog(
                        CrimsonX.Localization.AppStrings.AdvancedRulesVpnOnlyTitle,
                        CrimsonX.Localization.AppStrings.AdvancedRulesVpnOnlyMsg,
                        CrimsonX.Localization.AppStrings.Yes,
                        CrimsonX.Localization.AppStrings.No);

                    string? result = await dialog.ShowDialog<string>(MainWindow.Instance);
                    if (result != "Yes") return;

                    MainWindow.Instance.SwitchToVpnMode();
                }

                if (!CrimsonX.Services.ConnectivityService.HasUsableConnection())
                {
                    var noNetDlg = new CrimsonX.Dialogs.ConfirmDialog(
                        CrimsonX.Localization.AppStrings.NoInternetTitle,
                        CrimsonX.Localization.AppStrings.NoInternetMessage,
                        CrimsonX.Localization.AppStrings.Yes,
                        CrimsonX.Localization.AppStrings.No);

                    string? noNetResult = await noNetDlg.ShowDialog<string>(MainWindow.Instance);
                    if (noNetResult != "Yes") return;
                }
            }

            MainWindow.Instance.ConnectAfterCheck();
            _hasPendingRuleChanges = false;
            UpdateOverlayConnectUI();
            }
            catch (Exception ex)
            {
                CrimsonX.Services.SimpleLogger.Log(ex);
            }
        }

        private bool _isApplyingRules;

        private bool IsReapplying => _isApplyingRules || _state.IsReconnecting;

        private async void ApplyChanges_Click(object? sender, RoutedEventArgs e)
        {
            if (_isApplyingRules) return;

            var main = MainWindow.Instance;
            if (main == null) return;

            _isApplyingRules = true;

            UpdateOverlayConnectUI();

            try
            {
                bool ok = await main.RestartSingBoxOnlyAsync();

                if (ok)
                {
                    _hasPendingRuleChanges = false;
                    main.ShowToast(CrimsonX.Localization.AppStrings.ToastRulesApplied, kind: ToastKind.Success);
                }
                else if (_state.IsConnected)
                {
                    main.ShowToast(CrimsonX.Localization.AppStrings.ToastRulesApplyFailed, ToastKind.Error);
                }
                else
                {
                    CrimsonX.Services.SimpleLogger.Log(
                        "[AppRules] The apply was interrupted by a disconnect; the rule changes stay pending.");
                }
            }
            finally
            {
                _isApplyingRules = false;
                UpdateOverlayConnectUI();
            }
        }

        private void OnConnectionProgress(int percent)
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => SetOverlayConnectProgress(percent));
        }

        private void SetOverlayConnectProgress(int percent)
        {
            if (percent < 0)
            {
                _overlayFillTarget = -1;
                _overlayFillCurrent = 0;
                _overlayFillTimer?.Stop();
                ApplyOverlayFill(0, false);
                ApplyOverlayConnectBreath(false);
                return;
            }

            _overlayFillTarget = System.Math.Clamp(percent / 100.0, 0.0, 1.0);

            if (_overlayFillTimer == null)
            {
                _overlayFillTimer = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                _overlayFillTimer.Tick += (s, e) =>
                {
                    if (_overlayFillTarget < 0)
                    {
                        _overlayFillTimer.Stop();
                        return;
                    }

                    if (!IsEffectivelyVisible) return;

                    double diff = _overlayFillTarget - _overlayFillCurrent;
                    if (System.Math.Abs(diff) < 0.005) _overlayFillCurrent = _overlayFillTarget;
                    else _overlayFillCurrent += diff * 0.35;

                    bool comingUp = CrimsonX.Services.ConnectPhaseUi.IsComingUp(_state.IsConnected, _state.IsEngineRunning, IsReapplying);

                    bool showFill = comingUp && _overlayFillCurrent > 0.001;
                    ApplyOverlayFill(_overlayFillCurrent, showFill);
                    ApplyOverlayConnectBreath(comingUp);

                    if (!comingUp)
                    {
                        _overlayFillTarget = -1;
                        _overlayFillTimer.Stop();
                    }
                    else if (_overlayFillCurrent >= 0.999 && _overlayFillTarget >= 1.0)
                    {
                        _overlayFillTarget = -1;
                        _overlayFillTimer.Stop();
                    }
                };
            }

            if (!_overlayFillTimer.IsEnabled)
                _overlayFillTimer.Start();
        }

        private void ApplyOverlayConnectBreath(bool comingUp)
        {
            if (_overlayBreathBorder == null)
                _overlayBreathBorder = this.FindControl<Border>("panConnectBreath");
            if (_overlayBreathBorder == null) return;

            _overlayBreathBorder.Opacity = _overlayBreath.Next(DateTime.UtcNow, comingUp);
        }

        private void ApplyOverlayFill(double pct, bool show)
        {
            if (_overlayFillBorder == null)
            {
                _overlayFillBorder = this.FindControl<Border>("panOverlayConnectFill");
                if (_overlayFillBorder != null)
                    _overlayFillScale = _overlayFillBorder.RenderTransform as Avalonia.Media.ScaleTransform;
            }
            if (_overlayFillBorder == null || _overlayFillScale == null) return;

            _overlayFillScale.ScaleX = System.Math.Clamp(pct, 0.0, 1.0);
            _overlayFillBorder.Opacity = show ? 0.35 : 0.0;
        }

        public void UpdateOverlayConnectUI()
        {
            var txt = this.FindControl<TextBlock>("txtOverlayConnect");
            var txtOk = this.FindControl<TextBlock>("txtOverlayConnectConnected");
            if (txt == null) return;

            bool comingUp = CrimsonX.Services.ConnectPhaseUi.IsComingUp(_state.IsConnected, _state.IsEngineRunning, IsReapplying);

            if (_state.IsConnected && !comingUp)
            {
                if (txtOk != null)
                {
                    txtOk.Text = CrimsonX.Localization.AppStrings.StatusConnected;
                    txtOk.Opacity = 1;
                }
                txt.Text = "";

                _overlayFillTarget = -1;
                _overlayFillCurrent = 0;
                _overlayFillTimer?.Stop();
                ApplyOverlayFill(0, false);
            }
            else
            {
                if (txtOk != null) txtOk.Opacity = 0;

                string label = comingUp
                    ? CrimsonX.Localization.AppStrings.StatusConnecting
                    : CrimsonX.Localization.AppStrings.StatusConnect;

                if (txt.Text != label) txt.Text = label;

                if (!comingUp)
                {
                    _overlayFillTarget = -1;
                    _overlayFillCurrent = 0;
                    _overlayFillTimer?.Stop();
                    ApplyOverlayFill(0, false);
                }
            }
            ApplyOverlayConnectBreath(comingUp);

            bool showApply = CrimsonX.Services.ConnectPhaseUi.ShouldShowApplyChanges(
                _state.IsConnected, IsReapplying, _hasPendingRuleChanges, _cfg.LastXrayMode);

            var connectBox = this.FindControl<Border>("panConnectBox");
            if (connectBox != null)
            {
                if (_state.IsConnected && !comingUp)
                {
                    if (!connectBox.Classes.Contains("connected")) connectBox.Classes.Add("connected");
                }
                else
                {
                    connectBox.Classes.Remove("connected");
                }
            }

            SetApplyChangesBoxVisible(showApply);
        }

        private bool _applyChangesBoxVisible;
        private bool _bottomBarHooked;

        private void SetApplyChangesBoxVisible(bool show)
        {
            var box = this.FindControl<Border>("panApplyChangesBox");
            if (box == null) return;

            _applyChangesBoxVisible = show;

            if (!_bottomBarHooked)
            {
                _bottomBarHooked = true;
                box.SizeChanged += (_, _) => ApplyBottomBarLayout();
            }

            ApplyBottomBarLayout();
        }

        private void ApplyBottomBarLayout()
        {
            var bar = this.FindControl<StackPanel>("panOverlayBottomBar");
            var box = this.FindControl<Border>("panApplyChangesBox");
            if (bar == null || box == null) return;

            const double bottomMargin = 23;
            double nudge = box.Bounds.Width > 0 ? box.Bounds.Width + bar.Spacing : 0;

            bar.Margin = _applyChangesBoxVisible
                ? new Thickness(0, 0, 0, bottomMargin)
                : new Thickness(nudge, 0, 0, bottomMargin);

            box.Opacity = _applyChangesBoxVisible ? 1 : 0;

            box.IsHitTestVisible = CrimsonX.Services.ConnectPhaseUi.CanApplyChanges(_applyChangesBoxVisible, IsReapplying);
        }

    // ── List Filter & Localization ──

    private void Filter_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isReady) return;
        if (sender is ComboBox cb)
        {
            _currentFilter = cb.SelectedIndex switch { 1 => "GAMES", 2 => "LAUNCHERS", 3 => "OTHER", _ => "ALL" };
            RefreshList();
        }
    }
        protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            if (_dragHelper == null)
            {
                var lst = this.FindControl<global::Avalonia.Controls.ItemsControl>("lstRules");
                if (lst != null)
                {
                    _dragHelper = new CrimsonX.Helpers.DragReorderHelper(lst, new[] { "DragHandle", "DragHandleDefault" }, OnItemReordered);
                }
            }

            
        }

        public void ApplyLanguage()
        {
            bool fa = AS.IsPersian;

            TextBlock? F(string name) => this.FindControl<TextBlock>(name);

            void Apply(TextBlock? tb, string text, bool forceLtr = false)
                => CrimsonX.Localization.AppStrings.Apply(tb, text, forceLtr);

            void ApplyControl(string name, string text)
            {
                var c = this.FindControl<Avalonia.Controls.ContentControl>(name);
                if (c == null) return;
                c.Content = text;
                if (fa)
                {
                    c.FontFamily = new global::Avalonia.Media.FontFamily("Segoe UI");
                    c.FlowDirection = global::Avalonia.Media.FlowDirection.RightToLeft;
                }
                else
                {
                    c.FontFamily = global::Avalonia.Media.FontFamily.Default;
                    c.FlowDirection = global::Avalonia.Media.FlowDirection.LeftToRight;
                }
            }

            void SetComboItemText(string name, string text)
            {
                var item = this.FindControl<ComboBoxItem>(name);
                if (item != null) item.Content = text;
            }

            void FillCombo(string name, string[] items, int defaultIndex, int? saved)
            {
                var cb = this.FindControl<ComboBox>(name);
                if (cb == null) return;
                int idx = (saved.HasValue && saved.Value >= 0) ? saved.Value : defaultIndex;
                cb.ItemsSource = items;
                if (idx >= 0 && idx < items.Length) cb.SelectedIndex = idx;
            }

            void RestoreCombo(string name, int? saved)
            {
                if (saved == null || saved.Value < 0) return;
                var cb = this.FindControl<ComboBox>(name);
                if (cb != null && saved.Value < cb.Items.Count) cb.SelectedIndex = saved.Value;
            }

            void FillRegionCombo(string name, string[] items, int defaultIndex, int? saved)
            {
                bool wasSuppressed = _suppressRegionToast;
                _suppressRegionToast = true;
                try { FillCombo(name, items, defaultIndex, saved); }
                finally { _suppressRegionToast = wasSuppressed; }
            }

            // Top bar: search, filter, master rules, mode
            var btnScan = this.FindControl<global::Avalonia.Controls.Button>("btnScanAdapters");
            if (btnScan != null) btnScan.Content = AS.OverlayScanAdapters;

            var btnDefScan = this.FindControl<global::Avalonia.Controls.Button>("btnDefaultScanAdapters");
            if (btnDefScan != null) btnDefScan.Content = AS.OverlayScanAdapters;

            Apply(F("lblMasterOn"), AS.MasterRulesEnabled);
            Apply(F("lblMasterOff"), AS.MasterRulesDisabled);

            // Overlay split-mode buttons
            Apply(F("lblOverlayRegular"), AS.OverlaySplitRegular);
            Apply(F("lblOverlayInclusive"), AS.Inclusive);

            CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<Button>("btnOverlaySplitRegular"), AS.OverlaySplitRegularTooltip);
            CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<Button>("btnOverlaySplitInclusive"), AS.OverlaySplitInclusiveTooltip);
            CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<global::Avalonia.Controls.Button>("btnMasterOn"), AS.MasterRulesTooltip);
            CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<global::Avalonia.Controls.Button>("btnMasterOff"), AS.MasterRulesTooltip);

            // Filter dropdown items
            SetComboItemText("cbiFilterAll", AS.FilterAll);
            SetComboItemText("cbiFilterGames", AS.FilterGames);
            SetComboItemText("cbiFilterLaunchers", AS.FilterLaunchers);
            SetComboItemText("cbiFilterOther", AS.FilterOther);

            // Add/Edit rule editor
            Apply(F("lblAddToggle"), AS.AddToggle);
            bool adding = string.IsNullOrEmpty(_editingRuleId);
            Apply(F("lblEditorHeader"), AS.AddProgram);
            ApplyControl("btnSubmit", adding ? AS.Submit : AS.Update);
            ApplyControl("btnCancel", AS.Cancel);
            ApplyControl("btnBrowse", AS.Browse);
            Apply(F("lblType"), AS.TypeLabel);
            FillCombo("cbAppType", new[] { AS.Game, AS.Launcher, AS.Other }, 0, this.FindControl<ComboBox>("cbAppType")?.SelectedIndex);
            Apply(F("lblRouting"), AS.RoutingLabel);
            Apply(F("lblAdapter"), AS.AdapterLabel);

            // Routing combos (Proxy / Custom / Direct)
            var routingItems = new[] { AS.RoutingProxy, AS.RoutingCustom, AS.RoutingDirect };
            FillCombo("cbTcpRouting", routingItems, 0, this.FindControl<ComboBox>("cbTcpRouting")?.SelectedIndex);
            FillCombo("cbUdpRouting", routingItems, 2, this.FindControl<ComboBox>("cbUdpRouting")?.SelectedIndex);

            // Per-app custom proxy
            Apply(F("lblCustomProxy"), AS.CustomProxyLabel);
            ApplyControl("btnCustomProxyPing", AS.PingBtn);
            ApplyControl("btnCustomProxySave", AS.Save);
            ApplyControl("btnDefaultCustomProxyPing", AS.PingBtn);
            ApplyControl("btnDefaultCustomProxySave", AS.Save);
            CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<Button>("btnCustomProxyImport"), AS.ImportConfigTooltip);
            CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<Button>("btnDefaultCustomProxyImport"), AS.ImportConfigTooltip);
            Apply(F("lblDefaultCustomProxy"), AS.CustomProxyLabel);
            Apply(F("lblDefaultConfigRow"), AS.ConfigBadge);
            Apply(F("lblCustomProxyConfigRow"), AS.ConfigBadge);
            ApplyControl("btnCustomProxyHint", AS.HintBtn);

            CrimsonX.Localization.AppStrings.ApplyToolTip(F("lblCustomProxy"), AS.CustomProxyHint);
            CrimsonX.Localization.AppStrings.ApplyToolTip(F("lblDefaultCustomProxy"), AS.CustomProxyHint);
            foreach (var boxName in new[] { "cbCustomProxy", "cbDefaultCustomProxy" })
            {
                var box = this.FindControl<ComboBox>(boxName);
                if (box == null) continue;
                box.PlaceholderText = AS.CustomProxyPlaceholder;
                CrimsonX.Localization.AppStrings.ApplyToolTip(box, AS.CustomProxyTooltip);
            }

            RefreshCustomProxyPool("cbCustomProxy");
            RefreshCustomProxyPool("cbDefaultCustomProxy");

            ApplyControl("btnDefaultSubmit", AS.Submit);
            ApplyControl("btnDefaultCancel", AS.Cancel);
            ApplyControl("btnDefaultRestore", AS.RestoreDefaults);
            ApplyControl("btnDefaultHint", AS.HintBtn);
            CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<Button>("btnDefaultIconSubmit"), AS.Submit);
            CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<Button>("btnDefaultIconCancel"), AS.Cancel);
            CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<Button>("btnEditorIconSubmit"), AS.Submit);
            CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<Button>("btnEditorIconCancel"), AS.Cancel);
            CrimsonX.Localization.AppStrings.ApplyToolTip(F("lblExeName"), AS.RenameDisplayName);
            Apply(F("lblConnectionRegion"), AS.ConnectionRegionLabel);
            Apply(F("txtApplyChanges"), AS.ApplyChanges);
            Apply(F("lblDefaultRoutingHeader"), AS.RoutingLabel);
            Apply(F("lblDefaultAdapterHeader"), AS.AdapterLabel);
            FillCombo("cbDefaultTcpRouting", routingItems, 0, this.FindControl<ComboBox>("cbDefaultTcpRouting")?.SelectedIndex);
            FillCombo("cbDefaultUdpRouting", routingItems, 1, this.FindControl<ComboBox>("cbDefaultUdpRouting")?.SelectedIndex);
            FillRegionCombo("cbRegion", RegionDisplayOptions(), 0, this.FindControl<ComboBox>("cbRegion")?.SelectedIndex);

            int? tcpA = this.FindControl<ComboBox>("cbTcpAdapter")?.SelectedIndex;
            int? udpA = this.FindControl<ComboBox>("cbUdpAdapter")?.SelectedIndex;
            int? dTcpA = this.FindControl<ComboBox>("cbDefaultTcpAdapter")?.SelectedIndex;
            int? dUdpA = this.FindControl<ComboBox>("cbDefaultUdpAdapter")?.SelectedIndex;
            PopulateAdapters();
            RestoreCombo("cbTcpAdapter", tcpA);
            RestoreCombo("cbUdpAdapter", udpA);
            RestoreCombo("cbDefaultTcpAdapter", dTcpA);
            RestoreCombo("cbDefaultUdpAdapter", dUdpA);

            RefreshList();
            UpdateOverlayConnectUI();
        }

    // ── Delete / Pin / Reorder Rules ──

        private void DeleteRule_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is Avalonia.Controls.Button btn && btn.Tag is string ruleId)
            {
                var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
                if (rule == null || !string.IsNullOrEmpty(rule.DefaultKey)) return;
                bool wasEnabled = rule.IsEnabled;
                _rules.Remove(rule);
                SaveRules(wasEnabled);
                RefreshList();
            }
        }

        private void PinRule_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is Avalonia.Controls.Button btn && btn.Tag is string ruleId)
            {
                var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
                if (rule == null) return;

                rule.IsPinned = !rule.IsPinned;
                _rules.Remove(rule);

                if (rule.IsPinned)
                {
                    _rules.Insert(0, rule);
                }
                else
                {
                    int insertAt = _rules.Count(r => r.IsPinned);
                    _rules.Insert(insertAt, rule);
                }

                SaveRules(false);
                RefreshList();
            }
        }

        private void NormalizePinOrder()
        {
            _rules = _rules.OrderBy(r => !r.IsPinned).ToList();
        }

        
        private void OnItemReordered(int oldIndex, int newIndex)
        {
            var lst = this.FindControl<global::Avalonia.Controls.ItemsControl>("lstRules");
            if (lst == null || lst.ItemsSource == null) return;
            
            var viewModels = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Cast<AppRuleViewModel>(lst.ItemsSource));
            if (oldIndex < 0 || oldIndex >= viewModels.Count || newIndex < 0 || newIndex >= viewModels.Count) return;
            
            var movedRuleId = viewModels[oldIndex].RuleId;
            var targetRuleId = viewModels[newIndex].RuleId;
            
            int actualOld = _rules.FindIndex(r => r.Id == movedRuleId);
            int actualNew = _rules.FindIndex(r => r.Id == targetRuleId);
            
            if (actualOld >= 0 && actualNew >= 0)
            {
                var temp = _rules[actualOld];
                _rules.RemoveAt(actualOld);
                _rules.Insert(actualNew, temp);

                NormalizePinOrder();

                SaveRules(false);
                RefreshList();
            }
        }

        

        

    private void BtnScanAdapters_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        string? tcpA = this.FindControl<global::Avalonia.Controls.ComboBox>("cbTcpAdapter")?.SelectedItem as string;
        string? udpA = this.FindControl<global::Avalonia.Controls.ComboBox>("cbUdpAdapter")?.SelectedItem as string;
        string? dTcpA = this.FindControl<global::Avalonia.Controls.ComboBox>("cbDefaultTcpAdapter")?.SelectedItem as string;
        string? dUdpA = this.FindControl<global::Avalonia.Controls.ComboBox>("cbDefaultUdpAdapter")?.SelectedItem as string;

        PopulateAdapters();

        void RestoreCombo(string name, string? oldVal)
        {
            var cb = this.FindControl<global::Avalonia.Controls.ComboBox>(name);
            if (cb != null)
            {
                var list = cb.ItemsSource as System.Collections.Generic.List<string>;
                if (list != null && oldVal != null && list.Contains(oldVal))
                    cb.SelectedItem = oldVal;
                else
                    cb.SelectedIndex = 0;
            }
        }

        RestoreCombo("cbTcpAdapter", tcpA);
        RestoreCombo("cbUdpAdapter", udpA);
        RestoreCombo("cbDefaultTcpAdapter", dTcpA);
        RestoreCombo("cbDefaultUdpAdapter", dUdpA);
    }

}

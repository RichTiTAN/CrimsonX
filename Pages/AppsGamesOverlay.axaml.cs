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
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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

    public string  RuleId     { get; }
    public bool    IsEnabled  { get; set; }
    public string  ExeName    { get; }
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
    public bool    IsLeague   { get; }
    public bool    IsTekken   { get; }
    public bool    IsValorant { get; }
    public bool    ShowsRoutingPill => IsLauncher;
    public bool    ShowsRoutingEditor => IsLeague || IsTekken || IsValorant;
    public bool    IsDirect   { get; }
    public bool    HasDirectRouting { get; }
    public string  DirectLabel => CrimsonX.Localization.AppStrings.RoutingDirect.ToUpperInvariant();
    public string  ProxyLabel  => CrimsonX.Localization.AppStrings.RoutingProxy.ToUpperInvariant();
    public bool    ShowAdapterEditor => HasCountry || HasRegion || IsLauncher || ShowsRoutingEditor || HasDirectRouting;
    public int     CountryIndex { get; }
    public int     RegionIndex  { get; }

    public AppRuleViewModel(AppGameRule rule)
    {
        RuleId    = rule.Id;
        IsEnabled = rule.IsEnabled;
        ExeName   = rule.ExeName;
        IsDefault = !string.IsNullOrEmpty(rule.DefaultKey);
        IsPinned  = rule.IsPinned;
        HasCountry = !string.IsNullOrEmpty(rule.Country);
        HasRegion  = !string.IsNullOrEmpty(rule.Region);
        IsLauncher = string.Equals(rule.AppType, "Launcher", StringComparison.OrdinalIgnoreCase);
        IsLeague   = string.Equals(rule.DefaultKey, AppsGamesOverlay.LeagueDefaultKey, StringComparison.Ordinal);
        IsTekken   = string.Equals(rule.DefaultKey, AppsGamesOverlay.Tekken8DefaultKey, StringComparison.Ordinal);
        IsValorant = string.Equals(rule.DefaultKey, AppsGamesOverlay.ValorantDefaultKey, StringComparison.Ordinal);
        IsDirect   = string.Equals(rule.TcpRouting, "Direct", StringComparison.OrdinalIgnoreCase);
        HasDirectRouting = string.Equals(rule.TcpRouting, "Direct", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(rule.UdpRouting, "Direct", StringComparison.OrdinalIgnoreCase);
        RegionLabel = HasCountry ? CrimsonX.Localization.AppStrings.ConnRegionShort : CrimsonX.Localization.AppStrings.MatchMakingRegion;
        RegionTooltip = HasCountry ? CrimsonX.Localization.AppStrings.ConnectionRegionLabel : CrimsonX.Localization.AppStrings.MatchMakingRegion;
        CountryIndex = rule.Country switch { "IRAN" => 1, "UAE" => 2, _ => 0 };
        RegionIndex  = AppsGamesOverlay.RegionIndexFor(rule.Region);
        CountryItems = AppsGamesOverlay.CountryDisplayOptions();
        RegionItems  = AppsGamesOverlay.RegionDisplayOptions();

        if (!string.IsNullOrEmpty(rule.IconAsset))
        {
            if (!DefaultIconCache.TryGetValue(rule.IconAsset, out var bmp))
            {
                bmp = LoadIconBitmap(rule.IconAsset);
                DefaultIconCache[rule.IconAsset] = bmp;
            }
            IconBitmap = bmp;
            HasIcon = bmp != null;
        }
        else if (!string.IsNullOrEmpty(rule.IconBase64))
        {
            try
            {
                var bytes = Convert.FromBase64String(rule.IconBase64);
                IconBitmap = new Bitmap(new MemoryStream(bytes));
                HasIcon = true;
            }
            catch { HasIcon = false; }
        }
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
    private CrimsonX.Helpers.DragReorderHelper? _dragHelper;

    private string _editingRuleId = "";
    private string _iconBase64 = "";
    private string _exeName = "";

    private const string DiscordDefaultKey = "discord";
    private const string DiscordDefaultId = "d1a5c0de-0000-0000-0000-000000000001";
    private const string Cs2DefaultKey = "cs2";
    private const string Cs2DefaultId = "d1a5c0de-0000-0000-0000-000000000002";
    private const string ApexDefaultKey = "apex";
    private const string ApexDefaultId = "d1a5c0de-0000-0000-0000-000000000003";
    private const string DeadlockDefaultKey = "deadlock";
    private const string DeadlockDefaultId = "d1a5c0de-0000-0000-0000-000000000004";
    private const string EfootballDefaultKey = "efootball";
    private const string EfootballDefaultId = "d1a5c0de-0000-0000-0000-000000000005";
    internal const string Tekken8DefaultKey = "tekken8";
    private const string Tekken8DefaultId = "d1a5c0de-0000-0000-0000-000000000006";
    private const string RocketLeagueDefaultKey = "rocketleague";
    private const string RocketLeagueDefaultId = "d1a5c0de-0000-0000-0000-000000000007";
    internal const string LeagueDefaultKey = "league";
    private const string LeagueDefaultId = "d1a5c0de-0000-0000-0000-000000000014";
    internal const string ValorantDefaultKey = "valorant";
    private const string ValorantDefaultId = "d1a5c0de-0000-0000-0000-000000000017";
    private const string EaAppDefaultKey = "eaapp";
    private const string EaAppDefaultId = "d1a5c0de-0000-0000-0000-000000000008";
    private const string UbisoftDefaultKey = "ubisoft";
    private const string UbisoftDefaultId = "d1a5c0de-0000-0000-0000-000000000009";
    private const string EpicDefaultKey = "epicgames";
    private const string EpicDefaultId = "d1a5c0de-0000-0000-0000-000000000010";
    private const string SteamDefaultKey = "steam";
    private const string SteamDefaultId = "d1a5c0de-0000-0000-0000-000000000011";
    private const string XboxDefaultKey = "xbox";
    private const string XboxDefaultId = "d1a5c0de-0000-0000-0000-000000000012";
    private const string RiotDefaultKey = "riot";
    private const string RiotDefaultId = "d1a5c0de-0000-0000-0000-000000000013";
    private const string TelegramDefaultKey = "telegram";
    private const string TelegramDefaultId = "d1a5c0de-0000-0000-0000-000000000015";
    private const string WhatsAppDefaultKey = "whatsapp";
    private const string WhatsAppDefaultId = "d1a5c0de-0000-0000-0000-000000000016";
    private string _editingDefaultRuleId = "";
    private Avalonia.Controls.Panel? _defaultRuleEditorParent = null;
    private Avalonia.Controls.Border? _hiddenDefaultRuleView = null;
    private bool _isClosingDefaultEditor = false;
    private global::Avalonia.Threading.DispatcherTimer? _connectUiTimer;
    private bool _syncingMasterRules = false;
    private bool _hasPendingRuleChanges = false;
    private global::Avalonia.Threading.DispatcherTimer? _overlayFillTimer;
    private double _overlayFillCurrent = 0;
    private double _overlayFillTarget = -1;
    private Avalonia.Controls.Border? _overlayFillBorder;
    private Avalonia.Media.ScaleTransform? _overlayFillScale;
    private bool _isReady = false;

    internal static readonly string[] RegionOptions = { "ALL", "North America", "South America", "Europe", "Asia", "Africa", "Oceania" };

    internal static string[] CountryDisplayOptions() => new[]
    {
        CrimsonX.Localization.AppStrings.CountryEverywhere,
        CrimsonX.Localization.AppStrings.CountryIran,
        CrimsonX.Localization.AppStrings.CountryUae
    };

    internal static string[] RegionDisplayOptions() => new[]
    {
        CrimsonX.Localization.AppStrings.RegionAll,
        CrimsonX.Localization.AppStrings.RegionNorthAmerica,
        CrimsonX.Localization.AppStrings.RegionSouthAmerica,
        CrimsonX.Localization.AppStrings.RegionEurope,
        CrimsonX.Localization.AppStrings.RegionAsia,
        CrimsonX.Localization.AppStrings.RegionAfrica,
        CrimsonX.Localization.AppStrings.RegionOceania
    };

    internal static int RegionIndexFor(string region)
    {
        int idx = Array.IndexOf(RegionOptions, region);
        return idx >= 0 ? idx : 0;
    }

    internal static string RegionForIndex(int idx)
    {
        return idx >= 0 && idx < RegionOptions.Length ? RegionOptions[idx] : "ALL";
    }

    private AppConfig _cfg => MainWindow.Instance.Config;
    private AppState _state => MainWindow.Instance.State;

    private readonly List<string> _adapterNames = new();

    public AppsGamesOverlay()
    {
        InitializeComponent();
        PopulateAdapters();
        ApplyLanguage();
        _isReady = true;

        ApplyMasterRulesVisual();
        UpdateOverlayConnectUI();

        _connectUiTimer = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _connectUiTimer.Tick += (s, e) => { if (IsVisible) UpdateOverlayConnectUI(); };
        _connectUiTimer.Start();

        CrimsonX.Services.UiEventBus.Instance.ConnectionProgress += OnConnectionProgress;
    }

    // ── Rules Load & Defaults Migration ──

    public void LoadRules()
    {
        _rules = AppRulesService.Load();
        EnsureDefaultRules();
        _hasPendingRuleChanges = false;
        RefreshList();
        CloseEditor(); 
        CloseDefaultEditor(true);
        UpdateOverlaySplitUI();
        ApplyMasterRulesVisual();
        UpdateOverlayConnectUI();
    }

    private void EnsureDefaultRules()
    {
        bool changed = false;

        if (!_rules.Any(r => r.DefaultKey == DiscordDefaultKey))
        {
            _rules.Insert(0, CreateDiscordDefaultRule());
            changed = true;
        }

        var discordRule = _rules.FirstOrDefault(r => r.DefaultKey == DiscordDefaultKey);
        if (discordRule != null && !string.IsNullOrWhiteSpace(discordRule.Region))
        {
            discordRule.Region = "";
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == Cs2DefaultKey))
        {
            int discordIndex = _rules.FindIndex(r => r.DefaultKey == DiscordDefaultKey);
            _rules.Insert(discordIndex >= 0 ? discordIndex + 1 : 0, CreateCs2DefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == ApexDefaultKey))
        {
            int cs2Index = _rules.FindIndex(r => r.DefaultKey == Cs2DefaultKey);
            _rules.Insert(cs2Index >= 0 ? cs2Index + 1 : _rules.Count, CreateApexDefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == DeadlockDefaultKey))
        {
            int apexIndex = _rules.FindIndex(r => r.DefaultKey == ApexDefaultKey);
            _rules.Insert(apexIndex >= 0 ? apexIndex + 1 : _rules.Count, CreateDeadlockDefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == EfootballDefaultKey))
        {
            int deadlockIndex = _rules.FindIndex(r => r.DefaultKey == DeadlockDefaultKey);
            _rules.Insert(deadlockIndex >= 0 ? deadlockIndex + 1 : _rules.Count, CreateEfootballDefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == Tekken8DefaultKey))
        {
            int efootballIndex = _rules.FindIndex(r => r.DefaultKey == EfootballDefaultKey);
            _rules.Insert(efootballIndex >= 0 ? efootballIndex + 1 : _rules.Count, CreateTekken8DefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == RocketLeagueDefaultKey))
        {
            int tekkenIndex = _rules.FindIndex(r => r.DefaultKey == Tekken8DefaultKey);
            _rules.Insert(tekkenIndex >= 0 ? tekkenIndex + 1 : _rules.Count, CreateRocketLeagueDefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == LeagueDefaultKey))
        {
            int rlIndex = _rules.FindIndex(r => r.DefaultKey == RocketLeagueDefaultKey);
            _rules.Insert(rlIndex >= 0 ? rlIndex + 1 : _rules.Count, CreateLeagueDefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == ValorantDefaultKey))
        {
            int leagueIndex = _rules.FindIndex(r => r.DefaultKey == LeagueDefaultKey);
            _rules.Insert(leagueIndex >= 0 ? leagueIndex + 1 : _rules.Count, CreateValorantDefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == EaAppDefaultKey))
        {
            int valorantIndex = _rules.FindIndex(r => r.DefaultKey == ValorantDefaultKey);
            _rules.Insert(valorantIndex >= 0 ? valorantIndex + 1 : _rules.Count, CreateEaAppDefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == UbisoftDefaultKey))
        {
            int eaIndex = _rules.FindIndex(r => r.DefaultKey == EaAppDefaultKey);
            _rules.Insert(eaIndex >= 0 ? eaIndex + 1 : _rules.Count, CreateUbisoftDefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == EpicDefaultKey))
        {
            int ubisoftIndex = _rules.FindIndex(r => r.DefaultKey == UbisoftDefaultKey);
            _rules.Insert(ubisoftIndex >= 0 ? ubisoftIndex + 1 : _rules.Count, CreateEpicDefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == SteamDefaultKey))
        {
            int epicIndex = _rules.FindIndex(r => r.DefaultKey == EpicDefaultKey);
            _rules.Insert(epicIndex >= 0 ? epicIndex + 1 : _rules.Count, CreateSteamDefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == XboxDefaultKey))
        {
            int steamIndex = _rules.FindIndex(r => r.DefaultKey == SteamDefaultKey);
            _rules.Insert(steamIndex >= 0 ? steamIndex + 1 : _rules.Count, CreateXboxDefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == RiotDefaultKey))
        {
            int xboxIndex = _rules.FindIndex(r => r.DefaultKey == XboxDefaultKey);
            _rules.Insert(xboxIndex >= 0 ? xboxIndex + 1 : _rules.Count, CreateRiotDefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == TelegramDefaultKey))
        {
            _rules.Add(CreateTelegramDefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == WhatsAppDefaultKey))
        {
            _rules.Add(CreateWhatsAppDefaultRule());
            changed = true;
        }

        var regionOnlyKeys = new[] { Cs2DefaultKey, ApexDefaultKey, DeadlockDefaultKey, EfootballDefaultKey, RocketLeagueDefaultKey };
        foreach (var r in _rules.Where(r => regionOnlyKeys.Contains(r.DefaultKey)))
        {
            if (r.Country != "" || r.TcpRouting != "Proxy" || r.UdpRouting != "Direct" || r.TcpAdapter != "Default")
            {
                r.Country = "";
                r.TcpRouting = "Proxy";
                r.UdpRouting = "Direct";
                r.TcpAdapter = "Default";
                changed = true;
            }
        }

        var launcherKeys = new[] { EaAppDefaultKey, UbisoftDefaultKey, EpicDefaultKey, SteamDefaultKey, XboxDefaultKey, RiotDefaultKey };
        foreach (var r in _rules.Where(r => launcherKeys.Contains(r.DefaultKey)))
        {
            if (r.Country != "" || r.Region != "")
            {
                r.Country = "";
                r.Region = "";
                changed = true;
            }
            if (r.TcpAdapter != "Default" && string.Equals(r.TcpRouting, "Proxy", StringComparison.OrdinalIgnoreCase))
            {
                r.TcpAdapter = "Default";
                changed = true;
            }
            if (r.UdpAdapter != "Default" && string.Equals(r.UdpRouting, "Proxy", StringComparison.OrdinalIgnoreCase))
            {
                r.UdpAdapter = "Default";
                changed = true;
            }
        }

        var messagingKeys = new[] { TelegramDefaultKey, WhatsAppDefaultKey };
        foreach (var r in _rules.Where(r => messagingKeys.Contains(r.DefaultKey)))
        {
            if (r.Country != "" || r.Region != "" || r.TcpRouting != "Proxy" || r.UdpRouting != "Proxy" || r.TcpAdapter != "Default" || r.UdpAdapter != "Default")
            {
                r.Country = "";
                r.Region = "";
                r.TcpRouting = "Proxy";
                r.UdpRouting = "Proxy";
                r.TcpAdapter = "Default";
                r.UdpAdapter = "Default";
                changed = true;
            }
        }

        var telegramRule = _rules.FirstOrDefault(r => r.DefaultKey == TelegramDefaultKey);
        if (telegramRule != null && (telegramRule.ProcessNames == null || telegramRule.ProcessNames.Count != 1 || !string.Equals(telegramRule.ProcessNames[0], "Telegram.exe", StringComparison.Ordinal)))
        {
            telegramRule.ProcessNames = new List<string> { "Telegram.exe" };
            changed = true;
        }
        var whatsappRule = _rules.FirstOrDefault(r => r.DefaultKey == WhatsAppDefaultKey);
        if (whatsappRule != null && (whatsappRule.ProcessNames == null || whatsappRule.ProcessNames.Count != 1 || !string.Equals(whatsappRule.ProcessNames[0], "WhatsApp.Root.exe", StringComparison.Ordinal)))
        {
            whatsappRule.ProcessNames = new List<string> { "WhatsApp.Root.exe" };
            changed = true;
        }
        if (whatsappRule != null && (whatsappRule.Domains == null || whatsappRule.Domains.Count != 2
            || !whatsappRule.Domains.Contains("whatsapp.com", StringComparer.Ordinal)
            || !whatsappRule.Domains.Contains("whatsapp.net", StringComparer.Ordinal)))
        {
            whatsappRule.Domains = new List<string> { "whatsapp.com", "whatsapp.net" };
            changed = true;
        }
        var leagueRule = _rules.FirstOrDefault(r => r.DefaultKey == LeagueDefaultKey);
        if (leagueRule != null)
        {
            if (leagueRule.ProcessNames == null
                || !leagueRule.ProcessNames.Contains("League of Legends.exe", StringComparer.Ordinal)
                || !leagueRule.ProcessNames.Contains("LeagueClient.exe", StringComparer.Ordinal))
            {
                leagueRule.ProcessNames = new List<string> { "League of Legends.exe", "LeagueClient.exe" };
                changed = true;
            }
            if (leagueRule.Region != "")
            {
                leagueRule.Region = "";
                changed = true;
            }
        }
        var tekkenRule = _rules.FirstOrDefault(r => r.DefaultKey == Tekken8DefaultKey);
        if (tekkenRule != null)
        {
            if (tekkenRule.Region != "")
            {
                tekkenRule.Region = "";
                changed = true;
            }
        }

        foreach (var r in _rules.Where(r => !string.IsNullOrEmpty(r.DefaultKey)))
        {
            string? asset = DefaultIconAssetName(r.DefaultKey);
            if (asset == null) continue;
            if (r.IconAsset != asset || !string.IsNullOrEmpty(r.IconBase64))
            {
                r.IconAsset = asset;
                r.IconBase64 = "";
                changed = true;
            }
        }

        if (changed) SaveRules();
    }

    // ── Default Rule Presets (Apps & Games) ──

    private static AppGameRule CreateDiscordDefaultRule()
    {
        return new AppGameRule
        {
            Id = DiscordDefaultId,
            DefaultKey = DiscordDefaultKey,
            IsEnabled = false,
            AppType = "Other",
            ExeName = "Discord",
            ProcessNames = new List<string> { "Discord.exe", "Update.exe" },
            Country = "EVERYWHERE",
            Region = "",
            TcpRouting = "Proxy",
            UdpRouting = "Proxy",
            TcpAdapter = "Default",
            UdpAdapter = "Default",
            IconAsset = "discord.png"
        };
    }

    private static AppGameRule CreateCs2DefaultRule() =>
        CreateRegionOnlyDefaultRule(Cs2DefaultKey, Cs2DefaultId, "CS2", "cs2.exe", "cs2.png");

    private static AppGameRule CreateApexDefaultRule() =>
        CreateRegionOnlyDefaultRule(ApexDefaultKey, ApexDefaultId, "Apex Legends", "r5apex_dx12.exe", "apex.png");

    private static AppGameRule CreateDeadlockDefaultRule() =>
        CreateRegionOnlyDefaultRule(DeadlockDefaultKey, DeadlockDefaultId, "Deadlock", "deadlock.exe", "deadlock.png");

    private static AppGameRule CreateEfootballDefaultRule() =>
        CreateRegionOnlyDefaultRule(EfootballDefaultKey, EfootballDefaultId, "eFootball", "eFootball.exe", "efootball.png");

    private static AppGameRule CreateTekken8DefaultRule()
    {
        return new AppGameRule
        {
            Id = Tekken8DefaultId,
            DefaultKey = Tekken8DefaultKey,
            IsEnabled = false,
            AppType = "Game",
            ExeName = "Tekken 8",
            ProcessNames = new List<string> { "Polaris-Win64-Shipping.exe" },
            Country = "",
            Region = "",
            TcpRouting = "Proxy",
            UdpRouting = "Proxy",
            TcpAdapter = "Default",
            UdpAdapter = "Default",
            IconAsset = "tekken8.png"
        };
    }

    private static AppGameRule CreateRocketLeagueDefaultRule() =>
        CreateRegionOnlyDefaultRule(RocketLeagueDefaultKey, RocketLeagueDefaultId, "Rocket League", "RocketLeague.exe", "rl.png");

    private static AppGameRule CreateLeagueDefaultRule()
    {
        return new AppGameRule
        {
            Id = LeagueDefaultId,
            DefaultKey = LeagueDefaultKey,
            IsEnabled = false,
            AppType = "Game",
            ExeName = "League of Legends",
            ProcessNames = new List<string> { "League of Legends.exe", "LeagueClient.exe" },
            Country = "",
            Region = "",
            TcpRouting = "Direct",
            UdpRouting = "Direct",
            TcpAdapter = "Default",
            UdpAdapter = "Default",
            IconAsset = "lol.png"
        };
    }

    private static AppGameRule CreateValorantDefaultRule()
    {
        return new AppGameRule
        {
            Id = ValorantDefaultId,
            DefaultKey = ValorantDefaultKey,
            IsEnabled = false,
            AppType = "Game",
            ExeName = "Valorant",
            ProcessNames = new List<string> { "VALORANT-Win64-Shipping.exe" },
            Country = "",
            Region = "",
            TcpRouting = "Proxy",
            UdpRouting = "Direct",
            TcpAdapter = "Default",
            UdpAdapter = "Default",
            IconAsset = "valorant.png"
        };
    }

    private static AppGameRule CreateEaAppDefaultRule()
    {
        return new AppGameRule
        {
            Id = EaAppDefaultId,
            DefaultKey = EaAppDefaultKey,
            IsEnabled = false,
            AppType = "Launcher",
            ExeName = "EA App",
            ProcessNames = new List<string>
            {
                "EABackgroundService.exe", "EACefSubProcess.exe", "EAConnect_microsoft.exe",
                "EACrashReporter.exe", "EADesktop.exe", "EAEgsProxy.exe", "EAGEP.exe",
                "EALauncher.exe", "EALaunchHelper.exe", "EALocalHostSvc.exe",
                "EASteamAuthHelper.exe", "EASteamLauncher.exe", "EASteamProxy.exe",
                "EAUpdater.exe", "ErrorReporter.exe", "GetGameToken.exe",
                "IGOProxy32.exe", "Link2EA.exe", "OriginLegacyCompatibility.exe",
                "PolicyProxy32.exe", "PolicyProxy64.exe"
            },
            Country = "",
            Region = "",
            TcpRouting = "Proxy",
            UdpRouting = "Proxy",
            TcpAdapter = "Default",
            UdpAdapter = "Default",
            IconAsset = "ea.png"
        };
    }

    private static AppGameRule CreateUbisoftDefaultRule()
    {
        return new AppGameRule
        {
            Id = UbisoftDefaultId,
            DefaultKey = UbisoftDefaultKey,
            IsEnabled = false,
            AppType = "Launcher",
            ExeName = "Ubisoft Connect",
            ProcessNames = new List<string>
            {
                "UbisoftGameLauncher.exe", "UbisoftGameLauncher64.exe", "UbisoftConnect.exe",
                "UpcElevationService.exe", "UplayWebCore.exe", "upc.exe",
                "UplayService.exe", "UplayCrashReporter.exe", "UbisoftExtension.exe"
            },
            Country = "",
            Region = "",
            TcpRouting = "Proxy",
            UdpRouting = "Proxy",
            TcpAdapter = "Default",
            UdpAdapter = "Default",
            IconAsset = "ubisoft.png"
        };
    }

    private static AppGameRule CreateEpicDefaultRule()
    {
        return new AppGameRule
        {
            Id = EpicDefaultId,
            DefaultKey = EpicDefaultKey,
            IsEnabled = false,
            AppType = "Launcher",
            ExeName = "Epic Games",
            ProcessNames = new List<string>
            {
                "CrashReportClient.exe", "EOSOverlayRenderer-Win32-Shipping.exe", "EOSOverlayRenderer-Win64-Shipping.exe",
                "EpicOnlineServicesInstallHelper.exe", "EpicOnlineServicesUIHelper.exe", "EpicOnlineServicesUserHelper.exe",
                "EpicOnlineServicesHost.exe", "EpicGamesLauncher.exe", "EpicWebHelper.exe",
                "UnrealEngineLauncher.exe", "EpicGamesUpdater.exe", "EpicOnlineServicesInstaller.exe",
                "EOSBootStrapper.exe"
            },
            Country = "",
            Region = "",
            TcpRouting = "Proxy",
            UdpRouting = "Proxy",
            TcpAdapter = "Default",
            UdpAdapter = "Default",
            IconAsset = "epicgames.png"
        };
    }

    private static AppGameRule CreateSteamDefaultRule()
    {
        return new AppGameRule
        {
            Id = SteamDefaultId,
            DefaultKey = SteamDefaultKey,
            IsEnabled = false,
            AppType = "Launcher",
            ExeName = "Steam",
            ProcessNames = new List<string>
            {
                "steamwebhelper.exe", "SteamService.exe", "steam.exe"
            },
            Country = "",
            Region = "",
            TcpRouting = "Proxy",
            UdpRouting = "Proxy",
            TcpAdapter = "Default",
            UdpAdapter = "Default",
            IconAsset = "steam.png"
        };
    }

    private static AppGameRule CreateXboxDefaultRule()
    {
        return new AppGameRule
        {
            Id = XboxDefaultId,
            DefaultKey = XboxDefaultKey,
            IsEnabled = false,
            AppType = "Launcher",
            ExeName = "Xbox App",
            ProcessNames = new List<string>
            {
                "XboxPcTray.exe", "XboxPcAppFT.exe", "XboxPcAppCE.exe", "XboxPcApp.exe",
                "gamingservicesnet.exe", "gamingservices.exe", "gamingservicestcui.exe"
            },
            Country = "",
            Region = "",
            TcpRouting = "Proxy",
            UdpRouting = "Proxy",
            TcpAdapter = "Default",
            UdpAdapter = "Default",
            IconAsset = "xbox.png"
        };
    }

    private static AppGameRule CreateRiotDefaultRule()
    {
        return new AppGameRule
        {
            Id = RiotDefaultId,
            DefaultKey = RiotDefaultKey,
            IsEnabled = false,
            AppType = "Launcher",
            ExeName = "Riot Client",
            ProcessNames = new List<string>
            {
                "RiotClientServices.exe", "Riot Client.exe"
            },
            Country = "",
            Region = "",
            TcpRouting = "Proxy",
            UdpRouting = "Proxy",
            TcpAdapter = "Default",
            UdpAdapter = "Default",
            IconAsset = "riot.png"
        };
    }

    private static AppGameRule CreateTelegramDefaultRule()
    {
        return new AppGameRule
        {
            Id = TelegramDefaultId,
            DefaultKey = TelegramDefaultKey,
            IsEnabled = false,
            AppType = "Other",
            ExeName = "Telegram",
            ProcessNames = new List<string> { "Telegram.exe" },
            Country = "",
            Region = "",
            TcpRouting = "Proxy",
            UdpRouting = "Proxy",
            TcpAdapter = "Default",
            UdpAdapter = "Default",
            IconAsset = "telegram.png"
        };
    }

    private static AppGameRule CreateWhatsAppDefaultRule()
    {
        return new AppGameRule
        {
            Id = WhatsAppDefaultId,
            DefaultKey = WhatsAppDefaultKey,
            IsEnabled = false,
            AppType = "Other",
            ExeName = "WhatsApp",
            ProcessNames = new List<string> { "WhatsApp.Root.exe" },
            Domains = new List<string> { "whatsapp.com", "whatsapp.net" },
            Country = "",
            Region = "",
            TcpRouting = "Proxy",
            UdpRouting = "Proxy",
            TcpAdapter = "Default",
            UdpAdapter = "Default",
            IconAsset = "whatsapp.png"
        };
    }

    private static AppGameRule CreateRegionOnlyDefaultRule(string key, string id, string exeName, string processName, string iconAsset)
    {
        return new AppGameRule
        {
            Id = id,
            DefaultKey = key,
            IsEnabled = false,
            AppType = "Game",
            ExeName = exeName,
            ProcessNames = new List<string> { processName },
            Country = "",
            Region = "ALL",
            TcpRouting = "Proxy",
            UdpRouting = "Direct",
            TcpAdapter = "Default",
            UdpAdapter = "Default",
            IconAsset = iconAsset
        };
    }

    private static string? DefaultIconAssetName(string defaultKey) => defaultKey switch
    {
        DiscordDefaultKey => "discord.png",
        Cs2DefaultKey => "cs2.png",
        ApexDefaultKey => "apex.png",
        DeadlockDefaultKey => "deadlock.png",
        EfootballDefaultKey => "efootball.png",
        Tekken8DefaultKey => "tekken8.png",
        RocketLeagueDefaultKey => "rl.png",
        LeagueDefaultKey => "lol.png",
        ValorantDefaultKey => "valorant.png",
        EaAppDefaultKey => "ea.png",
        UbisoftDefaultKey => "ubisoft.png",
        EpicDefaultKey => "epicgames.png",
        SteamDefaultKey => "steam.png",
        XboxDefaultKey => "xbox.png",
        RiotDefaultKey => "riot.png",
        TelegramDefaultKey => "telegram.png",
        WhatsAppDefaultKey => "whatsapp.png",
        _ => null
    };

    // ── Rules List & Adapters UI ──

    private void RefreshList()
    {
        var lst = this.FindControl<ItemsControl>("lstRules");
        if (lst == null) return;
        
        var filtered = _rules.AsEnumerable();
        if (_currentFilter == "GAMES") filtered = filtered.Where(r => r.AppType == "Game");
        else if (_currentFilter == "LAUNCHERS") filtered = filtered.Where(r => r.AppType == "Launcher");
        else if (_currentFilter == "OTHER") filtered = filtered.Where(r => r.AppType == "Other");

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            string st = _searchText.Trim();
            filtered = filtered.Where(r => r.ExeName.Contains(st, StringComparison.OrdinalIgnoreCase));
        }

        lst.ItemsSource = filtered
            .Select(r => new AppRuleViewModel(r))
            .ToList();
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

    private void Search_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var tb = sender as TextBox;
        string text = tb?.Text ?? "";
        if (text == _searchText) return;
        _searchText = text;
        RefreshList();
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
        if (panEditor != null && panEditor.Opacity == 0)
        {
            OpenEditor(null, null);
        }
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
    private void OpenEditor(AppGameRule? ruleToEdit = null, Avalonia.Controls.ContentControl? targetContainer = null)
    {
        var panAddToggle = this.FindControl<Avalonia.Controls.Border>("panAddToggle");
var panAddToggleWrapper = this.FindControl<Avalonia.Controls.Border>("panAddToggleWrapper");
        var panEditor    = this.FindControl<Avalonia.Controls.Border>("panEditor");
        var btnSubmit    = this.FindControl<Avalonia.Controls.Button>("btnSubmit");
        var lblHeader    = this.FindControl<Avalonia.Controls.TextBlock>("lblEditorHeader");

        CloseDefaultEditor(true);

        if (panEditor == null) return;
        
        if (panEditor.Opacity > 0 || _isClosing)
        {
            CloseEditor(true);
        }
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
            if (panAddToggleWrapper != null) { SetTransitionSpeed(panAddToggleWrapper, 0); panAddToggleWrapper.MaxHeight = 0; panAddToggleWrapper.Opacity = 0; }
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
          {
              panEditor.MaxHeight = 800;
              panEditor.Opacity = 1;
          });

        if (ruleToEdit == null)
        {
            _editingRuleId = "";
            _exeName = "";
            _iconBase64 = "";
            if (btnSubmit != null) btnSubmit.Content = CrimsonX.Localization.AppStrings.Submit;
            if (lblHeader != null) lblHeader.Text = CrimsonX.Localization.AppStrings.AddProgram;
            ClearEditor();
        }
        else
        {
            _editingRuleId = ruleToEdit.Id;
            if (btnSubmit != null) btnSubmit.Content = CrimsonX.Localization.AppStrings.Update;
            if (lblHeader != null) lblHeader.Text = CrimsonX.Localization.AppStrings.EditProgram;
            PreFill(ruleToEdit);
        }
    }

    private async void CloseEditor(bool instant = false)
{
var panAddToggle = this.FindControl<Avalonia.Controls.Border>("panAddToggle");
var panAddToggleWrapper = this.FindControl<Avalonia.Controls.Border>("panAddToggleWrapper");
var panEditor    = this.FindControl<Avalonia.Controls.Border>("panEditor");

if (panEditor == null) return;

_isClosing = true;

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
await System.Threading.Tasks.Task.Delay(300);
}

if (!_isClosing) return; 

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
        var rbGame = this.FindControl<RadioButton>("rbGame");
        if (rbGame != null) rbGame.IsChecked = true;
        
        SetComboIndex("cbTcpRouting", 0);
        SetComboIndex("cbUdpRouting", 1);
        SetComboIndex("cbTcpAdapter", 0);
        SetComboIndex("cbUdpAdapter", 0);
        SetComboIndex("cbRegion", 0);
        UpdateCustomAdapterAvailability();
        UpdateIconDisplay();
    }

    private void PreFill(AppGameRule rule)
    {
        var rbGame   = this.FindControl<RadioButton>("rbGame");
        var rbLaunch = this.FindControl<RadioButton>("rbLaunch");
        var rbOther  = this.FindControl<RadioButton>("rbOther");
        if (rbGame   != null) rbGame.IsChecked   = rule.AppType == "Game";
        if (rbLaunch != null) rbLaunch.IsChecked = rule.AppType == "Launcher";
        if (rbOther  != null) rbOther.IsChecked  = rule.AppType == "Other";

        _exeName    = rule.ExeName;
        _iconBase64 = rule.IconBase64;
        UpdateIconDisplay();

        SetComboIndex("cbTcpRouting", string.Equals(rule.TcpRouting, "Direct", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        SetComboIndex("cbUdpRouting", string.Equals(rule.UdpRouting, "Direct", StringComparison.OrdinalIgnoreCase) ? 1 : 0);

        SetComboIndex("cbRegion", AppsGamesOverlay.RegionIndexFor(rule.Region));

        SetAdapterByName("cbTcpAdapter", rule.TcpAdapter);
        SetAdapterByName("cbUdpAdapter", rule.UdpAdapter);

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

        bool tcpProxy = cbTcpR == null || cbTcpR.SelectedIndex == 0;
        bool udpProxy = cbUdpR == null || cbUdpR.SelectedIndex == 0;

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

    private void UpdateIconDisplay()
    {
        var imgIcon       = this.FindControl<Image>("imgAppIcon");
        var iconHolder    = this.FindControl<Border>("iconPlaceholder");
        var lblExeName    = this.FindControl<TextBlock>("lblExeName");

        bool hasIcon = !string.IsNullOrEmpty(_iconBase64);
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
        if (iconHolder != null) iconHolder.IsVisible = !hasIcon;
        if (lblExeName != null) lblExeName.Text = _exeName;
    }

    private async void BtnBrowse_Click(object? sender, RoutedEventArgs e)
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

    private void BtnSubmit_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_exeName)) return;

        var cbTcpR = this.FindControl<ComboBox>("cbTcpRouting");
        var cbUdpR = this.FindControl<ComboBox>("cbUdpRouting");
        var cbTcpA = this.FindControl<ComboBox>("cbTcpAdapter");
        var cbUdpA = this.FindControl<ComboBox>("cbUdpAdapter");
        var rbGame   = this.FindControl<RadioButton>("rbGame");
        var rbLaunch = this.FindControl<RadioButton>("rbLaunch");

        string appType = rbGame?.IsChecked == true ? "Game"
                       : rbLaunch?.IsChecked == true ? "Launcher"
                       : "Other";

        string GetRouting(ComboBox? cb) => (cb?.SelectedIndex ?? 0) == 1 ? "Direct" : "Proxy";
        string GetAdapter(ComboBox? cb)
        {
            int i = cb?.SelectedIndex ?? 0;
            return i < _adapterNames.Count ? _adapterNames[i] : "Default";
        }

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

        if (rule.ProcessNames == null || rule.ProcessNames.Count == 0)
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
        string.Equals(rule.DefaultKey, LeagueDefaultKey, StringComparison.Ordinal)
        || string.Equals(rule.DefaultKey, Tekken8DefaultKey, StringComparison.Ordinal)
        || string.Equals(rule.DefaultKey, ValorantDefaultKey, StringComparison.Ordinal);

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
        var hdr = this.FindControl<Avalonia.Controls.TextBlock>("lblDefaultEditorHeader");
        if (hdr != null) hdr.Text = string.IsNullOrEmpty(rule.ExeName) ? "APP" : rule.ExeName.ToUpperInvariant();

        bool tcpDirect = rule.TcpRouting == "Direct";
        bool udpDirect = rule.UdpRouting == "Direct";

        SetAdapterByName("cbDefaultTcpAdapter", tcpDirect ? rule.TcpAdapter : "Default");
        var cbTcpA = this.FindControl<Avalonia.Controls.ComboBox>("cbDefaultTcpAdapter");
        if (cbTcpA != null) cbTcpA.IsEnabled = tcpDirect;

        SetAdapterByName("cbDefaultUdpAdapter", udpDirect ? rule.UdpAdapter : "Default");
        var cbUdpA = this.FindControl<Avalonia.Controls.ComboBox>("cbDefaultUdpAdapter");
        if (cbUdpA != null) cbUdpA.IsEnabled = udpDirect;

        bool showRouting = SupportsRoutingEditor(rule);
        var panDefaultRouting = this.FindControl<Avalonia.Controls.StackPanel>("panDefaultRouting");
        if (panDefaultRouting != null) panDefaultRouting.IsVisible = showRouting;

        var cbTcpR = this.FindControl<Avalonia.Controls.ComboBox>("cbDefaultTcpRouting");
        var cbUdpR = this.FindControl<Avalonia.Controls.ComboBox>("cbDefaultUdpRouting");
        if (cbTcpR != null && cbTcpR.ItemCount > 0)
            cbTcpR.SelectedIndex = string.Equals(rule.TcpRouting, "Direct", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (cbUdpR != null && cbUdpR.ItemCount > 0)
            cbUdpR.SelectedIndex = string.Equals(rule.UdpRouting, "Direct", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    private async void CloseDefaultEditor(bool instant = false)
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
    }

    private void DefaultRouting_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var cbTcpR = this.FindControl<ComboBox>("cbDefaultTcpRouting");
        var cbUdpR = this.FindControl<ComboBox>("cbDefaultUdpRouting");
        var cbTcpA = this.FindControl<ComboBox>("cbDefaultTcpAdapter");
        var cbUdpA = this.FindControl<ComboBox>("cbDefaultUdpAdapter");
        if (cbTcpA != null && cbTcpR != null) cbTcpA.IsEnabled = cbTcpR.SelectedIndex == 1;
        if (cbUdpA != null && cbUdpR != null) cbUdpA.IsEnabled = cbUdpR.SelectedIndex == 1;
    }

    private void DefaultSubmit_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_editingDefaultRuleId)) { CloseDefaultEditor(); return; }

        var rule = _rules.FirstOrDefault(r => r.Id == _editingDefaultRuleId);
        if (rule != null)
        {
            if (SupportsRoutingEditor(rule))
            {
                var cbTcpR = this.FindControl<ComboBox>("cbDefaultTcpRouting");
                var cbUdpR = this.FindControl<ComboBox>("cbDefaultUdpRouting");
                rule.TcpRouting = (cbTcpR?.SelectedIndex ?? 0) == 1 ? "Direct" : "Proxy";
                rule.UdpRouting = (cbUdpR?.SelectedIndex ?? 0) == 1 ? "Direct" : "Proxy";
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
            if (string.Equals(rule.TcpRouting, "Proxy", StringComparison.OrdinalIgnoreCase)) rule.TcpAdapter = "Default";
            if (string.Equals(rule.UdpRouting, "Proxy", StringComparison.OrdinalIgnoreCase)) rule.UdpAdapter = "Default";
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

                if (_state.IsEngineRunning)
                {
                    MainWindow.Instance.RestartXray();
                }
            }
        }

        // MASTER RULES SWITCH + OVERLAY CONNECT BUTTON

        private void MasterRules_Changed(object? sender, RoutedEventArgs e)
        {
            if (!_isReady) return;
            if (_syncingMasterRules) return;

            bool on = (sender as ToggleSwitch)?.IsChecked == true;
            if (_cfg.EnableAppRules == on) return;

            _cfg.EnableAppRules = on;
            MainWindow.Instance.RequestSave();
            UpdateMasterRulesVisual();
        }

        private void ApplyMasterRulesVisual()
        {
            bool on = _cfg.EnableAppRules;

            var tgl = this.FindControl<ToggleSwitch>("togMasterRules");
            if (tgl != null && tgl.IsChecked != on)
            {
                _syncingMasterRules = true;
                tgl.IsChecked = on;
                _syncingMasterRules = false;
            }

            UpdateMasterRulesVisual();
        }

        private void UpdateMasterRulesVisual()
        {
            bool on = _cfg.EnableAppRules;

            CrimsonX.Localization.AppStrings.Apply(
                this.FindControl<TextBlock>("lblRules"),
                on ? CrimsonX.Localization.AppStrings.MasterRulesEnabled
                   : CrimsonX.Localization.AppStrings.MasterRulesDisabled,
                forceLtr: true);

            var box = this.FindControl<Border>("panRulesBox");
            if (box == null) return;
            box.Opacity = on ? 1.0 : 0.45;
            box.IsHitTestVisible = on;
        }

    // ── Overlay Connect & Progress Fill ──

        private async void OverlayConnect_Click(object? sender, RoutedEventArgs e)
        {
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

                if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
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

            MainWindow.Instance.ConnectDisconnect();
            _hasPendingRuleChanges = false;
            UpdateOverlayConnectUI();
        }

        private void ApplyChanges_Click(object? sender, RoutedEventArgs e)
        {
            bool ok = MainWindow.Instance.RestartSingBoxOnly();
            if (ok)
            {
                _hasPendingRuleChanges = false;
                MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastRulesApplied, success: true);
            }
            else
            {
                MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastRulesApplyFailed);
            }
            UpdateOverlayConnectUI();
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

                    double diff = _overlayFillTarget - _overlayFillCurrent;
                    if (System.Math.Abs(diff) < 0.005) _overlayFillCurrent = _overlayFillTarget;
                    else _overlayFillCurrent += diff * 0.35;

                    bool showFill = _state.IsEngineRunning && !_state.IsConnected && _overlayFillCurrent > 0.001;
                    ApplyOverlayFill(_overlayFillCurrent, showFill);

                    if (!_state.IsEngineRunning || _state.IsConnected)
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

            if (_state.IsConnected)
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

                string label = _state.IsEngineRunning
                    ? CrimsonX.Localization.AppStrings.StatusConnecting
                    : CrimsonX.Localization.AppStrings.StatusConnect;

                if (txt.Text != label) txt.Text = label;

                if (!_state.IsEngineRunning)
                {
                    _overlayFillTarget = -1;
                    _overlayFillCurrent = 0;
                    _overlayFillTimer?.Stop();
                    ApplyOverlayFill(0, false);
                }
            }
            var btnApply = this.FindControl<Avalonia.Controls.Button>("btnApplyChanges");
            if (btnApply != null)
            {
                bool show = _state.IsConnected && _hasPendingRuleChanges
                    && string.Equals(_cfg.LastXrayMode, "VPN Mode", StringComparison.OrdinalIgnoreCase);
                btnApply.IsVisible = show;
            }
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

            // Top bar: search, filter, master rules, mode
            var txtSearch = this.FindControl<TextBox>("txtSearch");
            if (txtSearch != null) txtSearch.PlaceholderText = AS.SearchPlaceholder;
            Apply(F("lblFilter"), AS.FilterLabel, forceLtr: true);
            Apply(F("lblRules"), _cfg.EnableAppRules ? AS.MasterRulesEnabled : AS.MasterRulesDisabled, forceLtr: true);
            Apply(F("lblMode"), AS.OverlayMode, forceLtr: true);

            // Overlay split-mode buttons
            Apply(F("lblOverlayRegular"), AS.OverlaySplitRegular);
            Apply(F("lblOverlayInclusive"), AS.Inclusive);

            CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<Button>("btnOverlaySplitRegular"), AS.OverlaySplitRegularTooltip);
            CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<Button>("btnOverlaySplitInclusive"), AS.OverlaySplitInclusiveTooltip);
            CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togMasterRules"), AS.MasterRulesTooltip);

            // Filter dropdown items
            SetComboItemText("cbiFilterAll", AS.FilterAll);
            SetComboItemText("cbiFilterGames", AS.FilterGames);
            SetComboItemText("cbiFilterLaunchers", AS.FilterLaunchers);
            SetComboItemText("cbiFilterOther", AS.FilterOther);

            // Add/Edit rule editor
            Apply(F("lblAddToggle"), AS.AddToggle, forceLtr: true);
            bool adding = string.IsNullOrEmpty(_editingRuleId);
            Apply(F("lblEditorHeader"), adding ? AS.AddProgram : AS.EditProgram, forceLtr: true);
            ApplyControl("btnSubmit", adding ? AS.Submit : AS.Update);
            ApplyControl("btnCancel", AS.Cancel);
            ApplyControl("btnBrowse", AS.Browse);
            Apply(F("lblType"), AS.TypeLabel, forceLtr: true);
            ApplyControl("rbGame", AS.Game);
            ApplyControl("rbLaunch", AS.Launcher);
            ApplyControl("rbOther", AS.Other);
            Apply(F("lblApp"), AS.AppLabel, forceLtr: true);
            Apply(F("lblRouting"), AS.RoutingLabel, forceLtr: true);
            Apply(F("lblAdapter"), AS.AdapterLabel, forceLtr: true);

            // Routing combos (Proxy / Direct)
            var routingItems = new[] { AS.RoutingProxy, AS.RoutingDirect };
            FillCombo("cbTcpRouting", routingItems, 0, this.FindControl<ComboBox>("cbTcpRouting")?.SelectedIndex);
            FillCombo("cbUdpRouting", routingItems, 1, this.FindControl<ComboBox>("cbUdpRouting")?.SelectedIndex);

            ApplyControl("btnDefaultSubmit", AS.Submit);
            ApplyControl("btnDefaultCancel", AS.Cancel);
            Apply(F("lblConnectionRegion"), AS.ConnectionRegionLabel, forceLtr: true);
            Apply(F("lblConnectionRegionWarning"), AS.ConnectionRegionWarning);
            Apply(F("txtApplyChanges"), AS.ApplyChanges);
            Apply(F("lblTcpAdapter"), AS.TcpAdapterLabel);
            Apply(F("lblUdpAdapter"), AS.UdpAdapterLabel);
            Apply(F("lblTcpRouting"), AS.TcpRoutingLabel);
            Apply(F("lblUdpRouting"), AS.UdpRoutingLabel);
            FillCombo("cbDefaultTcpRouting", routingItems, 0, this.FindControl<ComboBox>("cbDefaultTcpRouting")?.SelectedIndex);
            FillCombo("cbDefaultUdpRouting", routingItems, 1, this.FindControl<ComboBox>("cbDefaultUdpRouting")?.SelectedIndex);
            FillCombo("cbRegion", RegionDisplayOptions(), 0, this.FindControl<ComboBox>("cbRegion")?.SelectedIndex);

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


        

        




}

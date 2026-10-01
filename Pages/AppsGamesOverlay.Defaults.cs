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

public partial class AppsGamesOverlay
{
    // -- Default rule presets ---------------------------------------------

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
    private const string Dota2DefaultKey = "dota2";
    private const string Dota2DefaultId = "d1a5c0de-0000-0000-0000-000000000018";
    internal const string LeagueDefaultKey = "league";
    private const string LeagueDefaultId = "d1a5c0de-0000-0000-0000-000000000014";
    internal const string ValorantDefaultKey = "valorant";
    private const string ValorantDefaultId = "d1a5c0de-0000-0000-0000-000000000017";
    private const string Bf6DefaultKey = "bf6";
    private const string Bf6DefaultId = "d1a5c0de-0000-0000-0000-000000000019";
    private const string Titanfall2DefaultKey = "titanfall2";
    private const string Titanfall2DefaultId = "d1a5c0de-0000-0000-0000-000000000020";
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
    private static readonly string[] RiotDomains = new[]
    {
        "riotgames.com", "pvp.net", "leagueoflegends.com", "lol.riotgames.com",
        "lol.pvp.net", "wr.pvp.net", "riotcdn.net", "riotcdn.com", "lolstatic.com",
        "rgpub.io", "riotdns.com", "dradis-prod.rdatasrv.net"
    };
    internal static readonly string[] LeagueProcessNames = new[]
    {
        "League of Legends.exe", "LeagueClient.exe",
        "LeagueClientUx.exe", "LeagueClientUxRender.exe"
    };
    private const string TelegramDefaultKey = "telegram";
    private const string TelegramDefaultId = "d1a5c0de-0000-0000-0000-000000000015";
    private const string WhatsAppDefaultKey = "whatsapp";
    private const string WhatsAppDefaultId = "d1a5c0de-0000-0000-0000-000000000016";
    private const string MarvelRivalsDefaultKey = "marvelrivals";
    private const string MarvelRivalsDefaultId = "d1a5c0de-0000-0000-0000-000000000021";
    private const string IRacingDefaultKey = "iracing";
    private const string IRacingDefaultId = "d1a5c0de-0000-0000-0000-000000000022";
    private const string BattleNetDefaultKey = "battlenet";
    private const string BattleNetDefaultId = "d1a5c0de-0000-0000-0000-000000000023";
    private const string BraveDefaultKey = "brave";
    private const string BraveDefaultId = "d1a5c0de-0000-0000-0000-000000000024";
    private const string ChromeDefaultKey = "chrome";
    private const string ChromeDefaultId = "d1a5c0de-0000-0000-0000-000000000025";
    private const string EdgeDefaultKey = "msedge";
    private const string EdgeDefaultId = "d1a5c0de-0000-0000-0000-000000000026";
    private const string FirefoxDefaultKey = "firefox";
    private const string FirefoxDefaultId = "d1a5c0de-0000-0000-0000-000000000027";
    internal static readonly string[] BrowserDefaultKeys = { BraveDefaultKey, ChromeDefaultKey, EdgeDefaultKey, FirefoxDefaultKey };

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

        if (!_rules.Any(r => r.DefaultKey == Dota2DefaultKey))
        {
            int rlIndex = _rules.FindIndex(r => r.DefaultKey == RocketLeagueDefaultKey);
            _rules.Insert(rlIndex >= 0 ? rlIndex + 1 : _rules.Count, CreateDota2DefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == LeagueDefaultKey))
        {
            int dota2Index = _rules.FindIndex(r => r.DefaultKey == Dota2DefaultKey);
            _rules.Insert(dota2Index >= 0 ? dota2Index + 1 : _rules.Count, CreateLeagueDefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == ValorantDefaultKey))
        {
            int leagueIndex = _rules.FindIndex(r => r.DefaultKey == LeagueDefaultKey);
            _rules.Insert(leagueIndex >= 0 ? leagueIndex + 1 : _rules.Count, CreateValorantDefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == Bf6DefaultKey))
        {
            int valorantIndex = _rules.FindIndex(r => r.DefaultKey == ValorantDefaultKey);
            _rules.Insert(valorantIndex >= 0 ? valorantIndex + 1 : _rules.Count, CreateBf6DefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == Titanfall2DefaultKey))
        {
            int bf6Index = _rules.FindIndex(r => r.DefaultKey == Bf6DefaultKey);
            _rules.Insert(bf6Index >= 0 ? bf6Index + 1 : _rules.Count, CreateTitanfall2DefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == MarvelRivalsDefaultKey))
        {
            int titanfall2Index = _rules.FindIndex(r => r.DefaultKey == Titanfall2DefaultKey);
            _rules.Insert(titanfall2Index >= 0 ? titanfall2Index + 1 : _rules.Count, CreateMarvelRivalsDefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == IRacingDefaultKey))
        {
            int marvelRivalsIndex = _rules.FindIndex(r => r.DefaultKey == MarvelRivalsDefaultKey);
            _rules.Insert(marvelRivalsIndex >= 0 ? marvelRivalsIndex + 1 : _rules.Count, CreateIRacingDefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == EaAppDefaultKey))
        {
            int iRacingIndex = _rules.FindIndex(r => r.DefaultKey == IRacingDefaultKey);
            _rules.Insert(iRacingIndex >= 0 ? iRacingIndex + 1 : _rules.Count, CreateEaAppDefaultRule());
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

        if (!_rules.Any(r => r.DefaultKey == BattleNetDefaultKey))
        {
            int riotIndex = _rules.FindIndex(r => r.DefaultKey == RiotDefaultKey);
            _rules.Insert(riotIndex >= 0 ? riotIndex + 1 : _rules.Count, CreateBattleNetDefaultRule());
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

        if (!_rules.Any(r => r.DefaultKey == BraveDefaultKey))
        {
            _rules.Add(CreateBraveDefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == ChromeDefaultKey))
        {
            _rules.Add(CreateChromeDefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == EdgeDefaultKey))
        {
            _rules.Add(CreateEdgeDefaultRule());
            changed = true;
        }

        if (!_rules.Any(r => r.DefaultKey == FirefoxDefaultKey))
        {
            _rules.Add(CreateFirefoxDefaultRule());
            changed = true;
        }

        var regionOnlyKeys = new[] { Cs2DefaultKey, ApexDefaultKey, DeadlockDefaultKey, EfootballDefaultKey, RocketLeagueDefaultKey, Dota2DefaultKey, Bf6DefaultKey, Titanfall2DefaultKey, MarvelRivalsDefaultKey, IRacingDefaultKey };
        foreach (var r in _rules.Where(r => regionOnlyKeys.Contains(r.DefaultKey)))
        {
            if (r.Country != "")
            {
                r.Country = "";
                changed = true;
            }
        }

        var launcherKeys = new[] { EaAppDefaultKey, UbisoftDefaultKey, EpicDefaultKey, SteamDefaultKey, XboxDefaultKey, RiotDefaultKey, BattleNetDefaultKey };
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

        var eaRule = _rules.FirstOrDefault(r => r.DefaultKey == EaAppDefaultKey);
        if (eaRule != null)
        {
            if (eaRule.ProcessNames == null) eaRule.ProcessNames = new List<string>();
            foreach (var exe in new[] { "EAAntiCheat.GameService.exe", "EABackgroundService.exe", "EAAntiCheat.GameServiceLauncher.exe" })
            {
                if (!eaRule.ProcessNames.Contains(exe, StringComparer.Ordinal))
                {
                    eaRule.ProcessNames.Add(exe);
                    changed = true;
                }
            }
        }

        var noRegionKeys = new[] { TelegramDefaultKey, WhatsAppDefaultKey, BraveDefaultKey, ChromeDefaultKey, EdgeDefaultKey, FirefoxDefaultKey };
        foreach (var r in _rules.Where(r => noRegionKeys.Contains(r.DefaultKey)))
        {
            if (r.Country != "" || r.Region != "")
            {
                r.Country = "";
                r.Region = "";
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
        var riotRule = _rules.FirstOrDefault(r => r.DefaultKey == RiotDefaultKey);
        if (riotRule != null && (riotRule.Domains == null || riotRule.Domains.Count != RiotDomains.Length
            || !RiotDomains.All(d => riotRule.Domains.Contains(d, StringComparer.OrdinalIgnoreCase))))
        {
            riotRule.Domains = new List<string>(RiotDomains);
            changed = true;
        }
        var leagueRule = _rules.FirstOrDefault(r => r.DefaultKey == LeagueDefaultKey);
        if (leagueRule != null)
        {
            if (leagueRule.ProcessNames == null) leagueRule.ProcessNames = new List<string>();
            foreach (var exe in LeagueProcessNames)
            {
                if (!leagueRule.ProcessNames.Any(n => string.Equals(n, exe, StringComparison.OrdinalIgnoreCase)))
                {
                    leagueRule.ProcessNames.Add(exe);
                    changed = true;
                }
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

    private static AppGameRule CreateDota2DefaultRule() =>
        CreateRegionOnlyDefaultRule(Dota2DefaultKey, Dota2DefaultId, "Dota 2", "dota2.exe", "dota2.png");

    private static AppGameRule CreateLeagueDefaultRule()
    {
        return new AppGameRule
        {
            Id = LeagueDefaultId,
            DefaultKey = LeagueDefaultKey,
            IsEnabled = false,
            AppType = "Game",
            ExeName = "League of Legends",
            ProcessNames = new List<string>(LeagueProcessNames),
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

    private static AppGameRule CreateBf6DefaultRule() =>
        CreateRegionOnlyDefaultRule(Bf6DefaultKey, Bf6DefaultId, "Battlefield 6", "bf6.exe", "bf6.png");

    private static AppGameRule CreateTitanfall2DefaultRule() =>
        CreateRegionOnlyDefaultRule(Titanfall2DefaultKey, Titanfall2DefaultId, "Titanfall 2", "Titanfall2.exe", "titanfall2.png");

    private static AppGameRule CreateMarvelRivalsDefaultRule() =>
        CreateRegionOnlyDefaultRule(MarvelRivalsDefaultKey, MarvelRivalsDefaultId, "Marvel Rivals",
            new[] { "MarvelRivals_Launcher.exe", "Marvel-Win64-Shipping.exe" }, "marvel.png");

    private static AppGameRule CreateIRacingDefaultRule() =>
        CreateRegionOnlyDefaultRule(IRacingDefaultKey, IRacingDefaultId, "iRacing",
            new[] { "iRacingSim64DX11.exe" }, "iracing.png");

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
                "EAAntiCheat.GameService.exe", "EAAntiCheat.GameServiceLauncher.exe",
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
            Domains = new List<string>(RiotDomains),
            Country = "",
            Region = "",
            TcpRouting = "Proxy",
            UdpRouting = "Proxy",
            TcpAdapter = "Default",
            UdpAdapter = "Default",
            IconAsset = "riot.png"
        };
    }

    private static AppGameRule CreateBattleNetDefaultRule()
    {
        return new AppGameRule
        {
            Id = BattleNetDefaultId,
            DefaultKey = BattleNetDefaultKey,
            IsEnabled = false,
            AppType = "Launcher",
            ExeName = "Battle.net",
            ProcessNames = new List<string> { "Battle.net.exe", "Battle.net Launcher.exe", "Agent.exe" },
            Country = "",
            Region = "",
            TcpRouting = "Proxy",
            UdpRouting = "Proxy",
            TcpAdapter = "Default",
            UdpAdapter = "Default",
            IconAsset = "battle.net.png"
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

    private static AppGameRule CreateRegionOnlyDefaultRule(string key, string id, string exeName, string[] processNames, string iconAsset)
    {
        return new AppGameRule
        {
            Id = id,
            DefaultKey = key,
            IsEnabled = false,
            AppType = "Game",
            ExeName = exeName,
            ProcessNames = new List<string>(processNames),
            Country = "",
            Region = "ALL",
            TcpRouting = "Proxy",
            UdpRouting = "Direct",
            TcpAdapter = "Default",
            UdpAdapter = "Default",
            IconAsset = iconAsset
        };
    }

    private static AppGameRule CreateBrowserDefaultRule(string key, string id, string exeName, string processName, string iconAsset)
    {
        return new AppGameRule
        {
            Id = id,
            DefaultKey = key,
            IsEnabled = false,
            AppType = "Other",
            ExeName = exeName,
            ProcessNames = new List<string> { processName },
            Country = "",
            Region = "",
            TcpRouting = "Proxy",
            UdpRouting = "Proxy",
            TcpAdapter = "Default",
            UdpAdapter = "Default",
            IconAsset = iconAsset
        };
    }

    private static AppGameRule CreateBraveDefaultRule() =>
        CreateBrowserDefaultRule(BraveDefaultKey, BraveDefaultId, "Brave", "brave.exe", "brave.png");

    private static AppGameRule CreateChromeDefaultRule() =>
        CreateBrowserDefaultRule(ChromeDefaultKey, ChromeDefaultId, "Chrome", "chrome.exe", "chrome.png");

    private static AppGameRule CreateEdgeDefaultRule() =>
        CreateBrowserDefaultRule(EdgeDefaultKey, EdgeDefaultId, "Edge", "msedge.exe", "msedge.png");

    private static AppGameRule CreateFirefoxDefaultRule() =>
        CreateBrowserDefaultRule(FirefoxDefaultKey, FirefoxDefaultId, "Firefox", "firefox.exe", "firefox.png");

    private static string? DefaultIconAssetName(string defaultKey) => defaultKey switch
    {
        DiscordDefaultKey => "discord.png",
        Cs2DefaultKey => "cs2.png",
        ApexDefaultKey => "apex.png",
        DeadlockDefaultKey => "deadlock.png",
        EfootballDefaultKey => "efootball.png",
        Tekken8DefaultKey => "tekken8.png",
        RocketLeagueDefaultKey => "rl.png",
        Dota2DefaultKey => "dota2.png",
        LeagueDefaultKey => "lol.png",
        ValorantDefaultKey => "valorant.png",
        Bf6DefaultKey => "bf6.png",
        Titanfall2DefaultKey => "titanfall2.png",
        EaAppDefaultKey => "ea.png",
        UbisoftDefaultKey => "ubisoft.png",
        EpicDefaultKey => "epicgames.png",
        SteamDefaultKey => "steam.png",
        XboxDefaultKey => "xbox.png",
        RiotDefaultKey => "riot.png",
        TelegramDefaultKey => "telegram.png",
        WhatsAppDefaultKey => "whatsapp.png",
        MarvelRivalsDefaultKey => "marvel.png",
        IRacingDefaultKey => "iracing.png",
        BattleNetDefaultKey => "battle.net.png",
        BraveDefaultKey => "brave.png",
        ChromeDefaultKey => "chrome.png",
        EdgeDefaultKey => "msedge.png",
        FirefoxDefaultKey => "firefox.png",
        _ => null
    };
}

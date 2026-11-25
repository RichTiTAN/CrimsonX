/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System.IO;
using Newtonsoft.Json;
using CrimsonX.Models;

namespace CrimsonX.Services
{
    public static class ConfigService
    {
        private static readonly object SaveLock = new object();
        private static string LastSavedPath = "";
        private static string LastSavedJson = "";

        public static bool ConfigLoaded { get; private set; }

        public static void Save(AppConfig config, AppState state, string cfgFile)
        {
            var data = new
            {
                EnableCustomConfigs = config.EnableCustomConfigs,
                StartupTab = config.StartupTab,
                EnableAppRules = config.EnableAppRules,
                PauseGlows = config.PauseGlows,
                DisableGlows = config.DisableGlows,
                CustomConfig1 = config.CustomConfig1,
                CustomConfig2 = config.CustomConfig2,
                CustomConfig1Enabled = config.CustomConfig1Enabled,
                CustomConfig2Enabled = config.CustomConfig2Enabled,
                FetchConfigWhenOneProvided = config.FetchConfigWhenOneProvided,
                EnableCustomConfigObfuscation = config.EnableCustomConfigObfuscation,
                ShowIpsecProfileButton = config.ShowIpsecProfileButton,
                EnablePerConfigAdapters = config.EnablePerConfigAdapters,
                AdapterBindingBeforePerConfig = config.AdapterBindingBeforePerConfig,
                CustomConfig1Adapter = config.CustomConfig1Adapter,
                CustomConfig2Adapter = config.CustomConfig2Adapter,
                DisableBackgroundChecks = config.DisableBackgroundChecks,
                DisableRefreshTimer = config.DisableRefreshTimer,
                AutoStart = config.AutoStart,
                LaunchOnBoot = config.LaunchOnBoot,
                StartMinimized = config.StartMinimized,
                WindowLeft = config.WindowLeft,
                EnableAdBlock = config.EnableAdBlock,
                AllowLanConnections = config.AllowLanConnections,
                EnableLanAuth = config.EnableLanAuth,
                LanAuthUsername = config.LanAuthUsername,
                LanAuthPassword = config.LanAuthPassword,
                Language = config.Language,
                DebugMode = config.DebugMode,
                ThemeColor = config.ThemeColor,
                WindowTop = config.WindowTop,
                XrayMode = config.LastXrayMode,
                SplitTunnelMode = config.SplitTunnelMode,
                ManualSplit = config.LastManualSplit,
                AppSplit = config.LastAppSplit,
                BlockSplit = config.LastBlockSplit,
                EnableDirect = config.EnableDirect,
                EnableDirectUDP = config.EnableDirectUDP,
                DirectUdpAdapterName = config.DirectUdpAdapterName,
                DirectUdpAdapterIp = config.DirectUdpAdapterIp,
                EnableV2rayChain = config.EnableV2rayChain,
                V2rayChainJson = config.V2rayChainJson,
                LocationsMode = config.LocationsMode,
                ExitNodeSource = config.ExitNodeSource,
                ExitNodeCountry = config.ExitNodeCountry,
                ExitNodeCustomJson = config.ExitNodeCustomJson,
                ExitNodeCustomEnabled = config.ExitNodeCustomEnabled,
                ExitNodeSuspended = config.ExitNodeSuspended,
                ExitNodeAuto = config.ExitNodeAuto,
                EnableAdapterBinding = config.EnableAdapterBinding,
                SelectedAdapterName = config.SelectedAdapterName,
                QuickSetting1 = config.QuickSetting1,
                QuickSetting2 = config.QuickSetting2,
                LastAppVersion = config.LastAppVersion,
                SelectedAdapterIp = config.SelectedAdapterIp,
                EnableWarp = config.EnableWarp,
                WarpProtocol = config.WarpProtocol,
                EnableUpstreamDoh = config.EnableUpstreamDoh,
                UpstreamDohUrl = config.UpstreamDohUrl,
                EnableSystemDns = config.EnableSystemDns,
                SystemDnsPrimary = config.SystemDnsPrimary,
                SystemDnsSecondary = config.SystemDnsSecondary,
                MinimizeToTray = config.MinimizeToTray,
                XrayBalancePolicy = config.XrayBalancePolicy,
                AdapterBalancePolicy = config.AdapterBalancePolicy,
                EnableLoadBalanceAdapters = config.EnableLoadBalanceAdapters,
                EnableUdpCapableNodes = config.EnableUdpCapableNodes,
                LoadBalanceAdapters = new System.Collections.Generic.List<string>(config.LoadBalanceAdapters ?? new()),
                EnableExcludedContinents = config.EnableExcludedContinents,
                ExcludedContinents = config.ExcludedContinents,
                UdpScanAmount = config.UdpScanAmount,
                UdpScanConcurrency = config.UdpScanConcurrency,
                UdpScanDiscardMs = config.UdpScanDiscardMs,
                UdpScanAdapterName = config.UdpScanAdapterName,
                UdpScanAdapterIp = config.UdpScanAdapterIp,
                UdpScanProtocol = config.UdpScanProtocol,
                DnsRestore = config.DnsRestore,
                PinTopBar = config.PinTopBar,
                HideStarPrompt = config.HideStarPrompt,
            };
            try
            {
                var dir = Path.GetDirectoryName(cfgFile);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                string json = JsonConvert.SerializeObject(data, Formatting.None);
                lock (SaveLock)
                {
                    if (!string.Equals(LastSavedPath, cfgFile, System.StringComparison.OrdinalIgnoreCase)
                        || LastSavedJson != json
                        || !File.Exists(cfgFile))
                    {
                        WriteSettings(cfgFile, json);
                        LastSavedPath = cfgFile;
                        LastSavedJson = json;
                    }
                }
                TunnelCredentialPruner.Sweep(config, AppRulesService.Load());
                TunnelHostPruner.Sweep(config, AppRulesService.Load(), force: true);
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
        }

        private static void WriteSettings(string cfgFile, string json)
        {
            if (cfgFile.EndsWith(".bin"))
            {
                using (var fs = new FileStream(cfgFile, FileMode.Create, FileAccess.Write))
                using (var bw = new BinaryWriter(fs))
                {
                    bw.Write(json);
                }
            }
            else
            {
                File.WriteAllText(cfgFile, json);
            }
        }

                public static void Load(AppConfig config, AppState state, string cfgFile)
        {
            string json = null;
            if (File.Exists(cfgFile))
            {
                state.IsFirstLaunch = false;
                if (cfgFile.EndsWith(".bin"))
                {
                    try
                    {
                        using (var fs = new FileStream(cfgFile, FileMode.Open, FileAccess.Read))
                        using (var br = new BinaryReader(fs))
                        {
                            json = br.ReadString();
                        }
                    }
                    catch
                    {
                        json = null;
                    }
                }
                else
                {
                    json = File.ReadAllText(cfgFile);
                }
            }
            string oldJsonPath = Path.ChangeExtension(cfgFile, ".json");
            if (json == null && File.Exists(oldJsonPath))
            {
                state.IsFirstLaunch = false;
                json = File.ReadAllText(oldJsonPath);
            }
            if (string.IsNullOrEmpty(json))
            {
                config.LastAppVersion = UpdateService.AppVersion;
                ConfigLoaded = false;
                return;
            }
            try
            {
                var jobj = Newtonsoft.Json.Linq.JObject.Parse(json);
                using var jReader = jobj.CreateReader();
                Newtonsoft.Json.JsonSerializer.CreateDefault().Populate(jReader, config);
                if (jobj["XrayBalancePolicy"] == null && jobj["HaProxyBalancePolicy"] != null)
                    config.XrayBalancePolicy = jobj.Value<string>("HaProxyBalancePolicy") ?? config.XrayBalancePolicy;
                ConfigLoaded = true;
            }
            catch (Exception ex)
            {
                ConfigLoaded = false;
                SimpleLogger.Log(ex);
            }
            if (string.IsNullOrWhiteSpace(config.LastAppVersion)) Migrate(config, state, cfgFile);
            else config.LastAppVersion = UpdateService.AppVersion;
        }

        private static void Migrate(AppConfig config, AppState state, string cfgFile)
        {
            config.QuickSetting1  = "CUSTOM CONFIGS";
            config.LastAppVersion = UpdateService.AppVersion;
            Save(config, state, cfgFile);
            SimpleLogger.Log(
                $"[Config] Profile carried over to the current settings (v{UpdateService.AppVersion}): Quick Settings slot 1 is CUSTOM CONFIGS now.");
        }
    }
}

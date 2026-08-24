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

using System.IO;
using Newtonsoft.Json;
using CrimsonX.Models;

namespace CrimsonX.Services
{
    public static class ConfigService
    {
        public static void Save(AppConfig config, AppState state, string cfgFile)
        {
            var data = new
            {
                EnableCustomConfigs = config.EnableCustomConfigs,
                CustomConfig1 = config.CustomConfig1,
                CustomConfig2 = config.CustomConfig2,
                AllowOneCustomConfig = config.AllowOneCustomConfig,
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
                IsLogsOpen = state.IsLogsOpen,
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
                EnableV2rayChain = config.EnableV2rayChain,
                V2rayChainJson = config.V2rayChainJson,

                EnableAdapterBinding = config.EnableAdapterBinding,
                SelectedAdapterName = config.SelectedAdapterName,
                QuickSetting1 = config.QuickSetting1,
                QuickSetting2 = config.QuickSetting2,
                SelectedAdapterIp = config.SelectedAdapterIp,
                EnableUpstreamDoh = config.EnableUpstreamDoh,
                UpstreamDohUrl = config.UpstreamDohUrl,
                EnableSystemDns = config.EnableSystemDns,
                SystemDnsPrimary = config.SystemDnsPrimary,
                SystemDnsSecondary = config.SystemDnsSecondary,

                MinimizeToTray = config.MinimizeToTray,
                XrayBalancePolicy = config.XrayBalancePolicy,
                EnableExcludedContinents = config.EnableExcludedContinents,
                ExcludedContinents = config.ExcludedContinents,
            };

            try
            {
                var dir = Path.GetDirectoryName(cfgFile);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                
                string json = JsonConvert.SerializeObject(data, Formatting.None);
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
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
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

            string oldJsonPath = cfgFile.Replace(".bin", ".json");
            if (json == null && File.Exists(oldJsonPath))
            {
                state.IsFirstLaunch = false;
                json = File.ReadAllText(oldJsonPath);
            }

            if (string.IsNullOrEmpty(json)) return;

            try
            {

                var jobj = Newtonsoft.Json.Linq.JObject.Parse(json);
                using var jReader = jobj.CreateReader();
                Newtonsoft.Json.JsonSerializer.CreateDefault().Populate(jReader, config);

                if (jobj["IsLogsOpen"] != null)
                    state.IsLogsOpen = jobj.Value<bool>("IsLogsOpen");


            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
        }
    }
}

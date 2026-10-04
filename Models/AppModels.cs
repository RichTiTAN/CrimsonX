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

using Newtonsoft.Json;

namespace CrimsonX.Models
{
    public class AppConfig
    {
        [JsonIgnore] public string BaseDir { get; set; } = "";
        [JsonIgnore] public string CfgFile { get; set; } = "";
        [JsonIgnore] public string XrayDir { get; set; } = "";
        [JsonIgnore] public string SbDir { get; set; } = "";

        public bool EnableCustomConfigs { get; set; } = false;
        public string CustomConfig1 { get; set; } = "";
        public string CustomConfig2 { get; set; } = "";
        public bool AllowOneCustomConfig { get; set; } = false;

        public bool DisableBackgroundChecks { get; set; } = false;
        public bool DisableRefreshTimer { get; set; } = false;

        public bool PauseGlows { get; set; } = false;
        public bool DisableGlows { get; set; } = false;

        public bool PinTopBar { get; set; } = true;

        public bool AutoStart { get; set; } = true;
        public string StartupTab { get; set; } = "Home";
        public bool LaunchOnBoot { get; set; } = false;
        public bool DebugMode { get; set; } = false;
        public bool StartMinimized { get; set; } = false;
        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
        [JsonProperty("XrayMode")]
        public string LastXrayMode { get; set; } = "VPN Mode";
        public string SplitTunnelMode { get; set; } = "DISABLED"; 
        [JsonProperty("ManualSplit")]
        public string LastManualSplit { get; set; } = "";
        [JsonProperty("AppSplit")]
        public string LastAppSplit { get; set; } = "";
        public bool EnableAppRules { get; set; } = true;
        [JsonProperty("BlockSplit")]
        public string LastBlockSplit { get; set; } = "";
        public bool EnableDirect { get; set; } = false;
        public bool EnableDirectUDP { get; set; } = false;
        public string DirectUdpAdapterName { get; set; } = "";
        public string DirectUdpAdapterIp { get; set; } = "";
        public bool ShowAdvancedRouting { get; set; } = false;
        public bool EnableExcludedContinents { get; set; } = false;
        public List<string> ExcludedContinents { get; set; } = new List<string>();
        public string V2rayChainJson { get; set; } = "";
        public bool EnableV2rayChain { get; set; } = false;

        public string QuickSetting1 { get; set; } = "CUSTOM CONFIGS";
        public string QuickSetting2 { get; set; } = "AUTO-CONNECT";

        public string LastAppVersion { get; set; } = "";

        public bool EnableAdapterBinding { get; set; } = false;
        public string SelectedAdapterName { get; set; } = "";
        public string SelectedAdapterIp { get; set; } = "";

        public bool EnableLoadBalanceAdapters { get; set; } = false;

        public bool EnableUdpCapableNodes { get; set; } = true;

        public System.Collections.Generic.List<string> LoadBalanceAdapters { get; set; } = new();

        public bool EnableUpstreamDoh { get; set; } = false;
        public string UpstreamDohUrl { get; set; } = "https://cloudflare-dns.com/dns-query";
        public bool EnableSystemDns { get; set; } = false;
        public string SystemDnsPrimary { get; set; } = "";
        public string SystemDnsSecondary { get; set; } = "";
        public bool MinimizeToTray { get; set; } = false;
        public bool EnableAdBlock { get; set; } = false;
        public bool AllowLanConnections { get; set; } = false;
        public bool EnableLanAuth       { get; set; } = false;
        public string LanAuthUsername   { get; set; } = "";
        public string LanAuthPassword   { get; set; } = "";
        public string Language { get; set; } = "ENGLISH";
        public string ThemeColor { get; set; } = "Crimson";
        [JsonProperty("XrayBalancePolicy")]
        public string XrayBalancePolicy { get; set; } = "leastping";

        [JsonProperty("AdapterBalancePolicy")]
        public string AdapterBalancePolicy { get; set; } = "roundrobin";

        // UDP Scanner page options
        public int UdpScanAmount { get; set; } = 10;
        public int UdpScanConcurrency { get; set; } = 5;
        public int UdpScanDiscardMs { get; set; } = 500;
        public string UdpScanAdapterName { get; set; } = "";
        public string UdpScanAdapterIp { get; set; } = "";

        public List<DnsRestoreEntry> DnsRestore { get; set; } = new List<DnsRestoreEntry>();
    }

    public class DnsRestoreEntry
    {
        public string Adapter { get; set; } = "";
        public bool WasDhcp { get; set; }
        public string[] Servers { get; set; } = new string[0];
    }

    public class AppState
    {
        public bool IsFirstLaunch { get; set; } = true;
        public bool IsConnected { get; set; } = false;
        public bool IsEngineRunning { get; set; } = false;

        public bool IsReconnecting { get; set; } = false;

        public bool AbortBoot { get; set; } = false;
        public bool IsGeoTracing { get; set; } = false;
        public bool IsAdvancedOpen { get; set; } = false;
        public bool IgnoreComboChange { get; set; } = false;
        public bool AppInitialized { get; set; } = false;
        public string PreviousBridge { get; set; } = "Direct";
        public string PreviousConfig { get; set; } = "Optimized";
        public string LanIp { get; set; } = "UNKNOWN";
        public DateTime? SessionStartTime { get; set; } = null;
        public long LastTotalBytes { get; set; } = 0;
        public long SessionDataBytes { get; set; } = 0;
        public double[] SpeedSamples { get; set; } = new double[5];
    }
}

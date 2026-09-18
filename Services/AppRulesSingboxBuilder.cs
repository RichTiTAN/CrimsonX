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
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using CrimsonX.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public sealed class AppRulesSingboxResult
    {
        public List<object> RouteRules { get; } = new List<object>();
        public List<object> Outbounds  { get; } = new List<object>();
        public List<object> DnsRules   { get; } = new List<object>();
        public List<object> RuleSets   { get; } = new List<object>();

        public List<object> DnsServers { get; } = new List<object>();

        public List<CustomOutboundProbe> CustomProxies { get; } = new List<CustomOutboundProbe>();

        internal Dictionary<string, string> CustomTags { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public Dictionary<string, string> CustomLabels { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public static class AppRulesSingboxBuilder
    {
        private static readonly Dictionary<string, string> RegionToRuleSet = new Dictionary<string, string>
        {
            ["North America"] = "north-america",
            ["South America"] = "south-america",
            ["Europe"]        = "europe",
            ["Asia"]          = "asia",
            ["Africa"]        = "africa",
            ["Oceania"]       = "oceania",
        };

        public static AppRulesSingboxResult Build(AppConfig config, ISet<string> skipCustomKeys = null)
        {
            var result = new AppRulesSingboxResult();

            if (config == null || !config.EnableAppRules) return result;

            List<AppGameRule> rules;
            try { rules = AppRulesService.Load(); }
            catch { return result; }
            if (rules == null) return result;

            var enabled = rules
                .Where(r => r.IsEnabled
                    && ((r.ProcessNames != null && r.ProcessNames.Count > 0) || !string.IsNullOrWhiteSpace(r.ExeName)))
                .ToList();
            if (enabled.Count == 0) return result;

            var adapterTags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var adapterIps  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int adapterIndex = 1;
            var usedRuleSets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rule in enabled)
            {
                var nameSource = rule.ProcessNames != null && rule.ProcessNames.Count > 0
                    ? rule.ProcessNames
                    : new List<string> { rule.ExeName };
                var names = BuildProcessNames(nameSource);
                if (names.Count == 0) continue;

                var namesArray = names.ToArray();

                if (!string.IsNullOrWhiteSpace(rule.Region)
                    && rule.Region != "ALL"
                    && RegionToRuleSet.TryGetValue(rule.Region.Trim(), out var ruleSetFile))
                {
                    string ruleSetTag = "region-" + ruleSetFile;
                    usedRuleSets.Add(ruleSetFile);

                    result.RouteRules.Add(new
                    {
                        type = "logical",
                        mode = "and",
                        rules = new object[]
                        {
                            new { process_name = namesArray },
                            new { network = "udp" },
                            new { port = new[] { 3478, 5349 }, invert = true },
                            new { ip_is_private = true, invert = true },
                            new { rule_set = ruleSetTag, invert = true }
                        },
                        action = "reject"
                    });
                }

                string tcpOutbound = ResolveOutbound(rule, rule.TcpRouting, rule.TcpAdapter, adapterTags, adapterIps, ref adapterIndex, result, skipCustomKeys);
                string udpOutbound = ResolveOutbound(rule, rule.UdpRouting, rule.UdpAdapter, adapterTags, adapterIps, ref adapterIndex, result, skipCustomKeys);

                result.RouteRules.Add(new
                {
                    process_name = namesArray,
                    network = "tcp",
                    action = "route",
                    outbound = tcpOutbound
                });
                result.RouteRules.Add(new
                {
                    process_name = namesArray,
                    network = "udp",
                    action = "route",
                    outbound = udpOutbound
                });

                bool tcpCustom = IsCustomRouting(rule.TcpRouting);
                bool udpCustom = IsCustomRouting(rule.UdpRouting);
                bool tcpCustomTag = tcpCustom && tcpOutbound.StartsWith("custom-", StringComparison.Ordinal);
                bool udpCustomTag = udpCustom && udpOutbound.StartsWith("custom-", StringComparison.Ordinal);
                bool fullyCustom = tcpCustomTag && udpCustomTag && tcpOutbound == udpOutbound;
                bool fullyProxied = IsProxyRouting(rule.TcpRouting) && IsProxyRouting(rule.UdpRouting);

                string dnsServer = fullyCustom ? "dns-" + tcpOutbound
                                 : tcpCustomTag ? "dns-" + tcpOutbound
                                 : udpCustomTag ? "dns-" + udpOutbound
                                 : fullyProxied ? "dns_proxy"
                                 : "dns_direct";

                result.DnsRules.Add(new
                {
                    process_name = namesArray,
                    action = "route",
                    server = dnsServer
                });

                var domains = rule.Domains != null
                    ? rule.Domains.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()).ToArray()
                    : Array.Empty<string>();
                if (domains.Length > 0)
                {
                    result.RouteRules.Add(new
                    {
                        domain_suffix = domains,
                        action = "route",
                        outbound = tcpOutbound
                    });

                    result.DnsRules.Add(new
                    {
                        domain_suffix = domains,
                        action = "route",
                        server = dnsServer
                    });
                }
            }

            foreach (var file in usedRuleSets)
            {
                result.RuleSets.Add(new
                {
                    tag = "region-" + file,
                    type = "local",
                    format = "binary",
                    path = "rule_sets/" + file + ".srs"
                });
            }

            return result;
        }

        private static string ResolveOutbound(AppGameRule rule, string routing, string adapter,
            Dictionary<string, string> adapterTags, Dictionary<string, string> adapterIps, ref int adapterIndex,
            AppRulesSingboxResult result, ISet<string> skipCustomKeys)
        {
            if (IsCustomRouting(routing))
            {
                string customTag = ResolveCustomOutbound(rule, adapter, adapterIps, result, skipCustomKeys);
                return customTag.Length > 0 ? customTag : "proxy";
            }

            bool direct = string.Equals(routing, "Direct", StringComparison.OrdinalIgnoreCase);
            bool customAdapter = !string.IsNullOrWhiteSpace(adapter)
                && !string.Equals(adapter, "Default", StringComparison.OrdinalIgnoreCase);

            if (!direct) return "proxy";

            if (customAdapter)
            {
                if (adapterTags.TryGetValue(adapter, out var existing)) return existing;

                string tag = "direct-adapter-" + adapterIndex++;
                adapterTags[adapter] = tag;

                var ob = new Dictionary<string, object>
                {
                    ["type"] = "direct",
                    ["tag"] = tag,
                    ["bind_interface"] = adapter
                };
                string ip = CachedAdapterIp(adapterIps, adapter);
                if (!string.IsNullOrWhiteSpace(ip)) ob["inet4_bind_address"] = ip;

                result.Outbounds.Add(ob);
                return tag;
            }

            return "direct";
        }

        private static bool IsCustomRouting(string routing)
            => string.Equals(routing, "Custom", StringComparison.OrdinalIgnoreCase);

        private static bool IsProxyRouting(string routing)
            => string.IsNullOrEmpty(routing)
            || string.Equals(routing, "Proxy", StringComparison.OrdinalIgnoreCase)
            || IsCustomRouting(routing);

        private static string ResolveCustomOutbound(AppGameRule rule, string adapter,
            Dictionary<string, string> adapterIps, AppRulesSingboxResult result, ISet<string> skipCustomKeys)
        {
            string raw = rule?.CustomProxyRaw ?? "";
            if (string.IsNullOrWhiteSpace(raw)) return "";

            string key = SingboxLinkParser.KeyOf(raw, adapter);
            if (skipCustomKeys != null && skipCustomKeys.Contains(key)) return "";
            if (result.CustomTags.TryGetValue(key, out var existing)) return existing;

            if (!SingboxLinkParser.TryParseLink(raw, out var outboundJson, out var label)) return "";

            JObject outbound;
            try { outbound = JObject.Parse(outboundJson); }
            catch { return ""; }

            string tag = "custom-" + result.CustomProxies.Count;
            SingboxLinkParser.WithTagAndDial(outbound, tag, adapter, CachedAdapterIp(adapterIps, adapter));

            string normalized = outbound.ToString(Formatting.None);

            result.Outbounds.Add(outbound);
            result.CustomProxies.Add(new CustomOutboundProbe { Key = key, OutboundJson = normalized });
            result.CustomTags[key] = tag;
            result.CustomLabels[tag] = label;

            result.DnsServers.Add(new
            {
                tag     = "dns-" + tag,
                type    = "https",
                server  = "dns.google",
                path    = "/dns-query",
                detour  = tag
            });

            return tag;
        }

        internal static List<string> BuildProcessNames(IEnumerable<string> processNames)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            void AddUnique(string value)
            {
                if (value.Length > 0 && seen.Add(value)) result.Add(value);
            }

            foreach (var raw in processNames)
            {
                if (raw == null) continue;

                string exe = raw.Trim();
                if (exe.Length == 0) continue;

                AddUnique(exe);
                AddUnique(exe.ToLowerInvariant());

                if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    string baseName = exe.Substring(0, exe.Length - 4);
                    AddUnique(baseName);
                    AddUnique(baseName.ToLowerInvariant());
                }
            }

            return result;
        }
        private static string CachedAdapterIp(Dictionary<string, string> cache, string adapterName)
        {
            if (cache.TryGetValue(adapterName, out var cached)) return cached;

            string ip = ResolveAdapterIp(adapterName);
            cache[adapterName] = ip;
            return ip;
        }

        private static string ResolveAdapterIp(string adapterName)
        {
            try
            {
                var nic = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => n.Name == adapterName && n.OperationalStatus == OperationalStatus.Up);
                if (nic == null) return "";

                return nic.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?
                    .Address?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }
    }
}

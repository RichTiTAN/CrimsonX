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

namespace CrimsonX.Services
{
    public sealed class AppRulesSingboxResult
    {
        public List<object> RouteRules { get; } = new List<object>();
        public List<object> Outbounds  { get; } = new List<object>();
        public List<object> DnsRules   { get; } = new List<object>();
        public List<object> RuleSets   { get; } = new List<object>();
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

        public static AppRulesSingboxResult Build(AppConfig config)
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
            int adapterIndex = 1;
            var usedRuleSets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rule in enabled)
            {
                var nameSource = rule.ProcessNames != null && rule.ProcessNames.Count > 0
                    ? rule.ProcessNames
                    : new List<string> { rule.ExeName };
                var names = BuildProcessNames(nameSource);
                if (names.Count == 0) continue;

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
                            new { process_name = names.ToArray() },
                            new { network = "udp" },
                            new { port = new[] { 3478, 5349 }, invert = true },
                            new { ip_is_private = true, invert = true },
                            new { rule_set = ruleSetTag, invert = true }
                        },
                        action = "reject"
                    });
                }

                result.RouteRules.Add(new
                {
                    process_name = names.ToArray(),
                    network = "tcp",
                    action = "route",
                    outbound = ResolveOutbound(rule.TcpRouting, rule.TcpAdapter, adapterTags, ref adapterIndex, result)
                });
                result.RouteRules.Add(new
                {
                    process_name = names.ToArray(),
                    network = "udp",
                    action = "route",
                    outbound = ResolveOutbound(rule.UdpRouting, rule.UdpAdapter, adapterTags, ref adapterIndex, result)
                });

                bool fullyProxied = rule.TcpRouting == "Proxy" && rule.UdpRouting == "Proxy";
                result.DnsRules.Add(new
                {
                    process_name = names.ToArray(),
                    action = "route",
                    server = fullyProxied ? "dns_proxy" : "dns_direct"
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
                        outbound = "proxy"
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

        private static string ResolveOutbound(string routing, string adapter,
            Dictionary<string, string> adapterTags, ref int adapterIndex, AppRulesSingboxResult result)
        {
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
                string ip = ResolveAdapterIp(adapter);
                if (!string.IsNullOrWhiteSpace(ip)) ob["inet4_bind_address"] = ip;

                result.Outbounds.Add(ob);
                return tag;
            }

            return "direct";
        }

        private static List<string> BuildProcessNames(IEnumerable<string> processNames)
        {
            var list = new List<string>();
            foreach (var raw in processNames)
            {
                string exe = raw.Trim();
                if (exe.Length == 0) continue;

                if (!list.Contains(exe, StringComparer.OrdinalIgnoreCase)) list.Add(exe);
                string exeLower = exe.ToLowerInvariant();
                if (!list.Contains(exeLower, StringComparer.OrdinalIgnoreCase)) list.Add(exeLower);

                if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    string baseName = exe.Substring(0, exe.Length - 4);
                    if (!list.Contains(baseName, StringComparer.OrdinalIgnoreCase)) list.Add(baseName);
                    string baseLower = baseName.ToLowerInvariant();
                    if (!list.Contains(baseLower, StringComparer.OrdinalIgnoreCase)) list.Add(baseLower);
                }
            }
            return list;
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

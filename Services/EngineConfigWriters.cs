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
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CrimsonX.Models;

namespace CrimsonX.Services
{
    public static class XrayConfigWriter
    {
        public static bool Write(AppConfig config, string xrayDir, System.Collections.Generic.List<JObject>? activeOutbounds = null, Func<string, bool>? isAvailable = null)
        {
            var chains = new System.Collections.Generic.List<System.Collections.Generic.List<JObject>>();
            if (activeOutbounds != null)
            {
                foreach (var outbound in activeOutbounds)
                {
                    chains.Add(new System.Collections.Generic.List<JObject> { outbound });
                }
            }
            return Write(config, xrayDir, chains, isAvailable);
        }

        public static bool Write(AppConfig config, string xrayDir, System.Collections.Generic.List<System.Collections.Generic.List<JObject>> activeChains, Func<string, bool>? isAvailable = null)
        {
            int nodeCount = 1;
            if (activeChains != null && activeChains.Count > 0) nodeCount = activeChains.Count;
            bool exitIsSingboxExit = ExitNodeChain.IsSingboxExit(config);
            bool useCustomChain = config.EnableV2rayChain
                && !string.IsNullOrWhiteSpace(config.V2rayChainJson)
                && !exitIsSingboxExit;
            bool singBoxOwnsLanPort = ExitNodeChain.ShouldChain(config);
            bool preferDirectDefault = config.EnableDirect && config.SplitTunnelMode == "INCLUSIVE" && config.LastXrayMode != "VPN Mode";

            // ── the adapters the traffic is spread over ──────────────────────────────────────────────────
            bool perConfigAdapters = UsesPerConfigAdapters(config);
            var adapterCheck = isAvailable ?? IsAdapterAvailable;
            int storedAdapterCount = AdapterIps(config.LoadBalanceAdapters).Count;
            var adapterBindings = !perConfigAdapters && config.EnableLoadBalanceAdapters
                ? AdapterBindings.ResolveAvailable(config.LoadBalanceAdapters, adapterCheck)
                : new System.Collections.Generic.List<AdapterBinding>();
            if (!perConfigAdapters && config.EnableLoadBalanceAdapters && storedAdapterCount > adapterBindings.Count)
            {
                SimpleLogger.LogFirst("adapter-write-unavailable",
                    $"[Adapter] Ignoring {storedAdapterCount - adapterBindings.Count} load-balance adapter(s) that are no longer available.");
            }
            if (!perConfigAdapters && config.EnableLoadBalanceAdapters && storedAdapterCount > 0 && adapterBindings.Count == 0)
            {
                SimpleLogger.LogFirst("adapter-write-no-lb",
                    "[Adapter] No load-balance adapter is available; using the default route.");
            }
            var loadBalanceBindings = adapterBindings.Count > 1
                ? adapterBindings
                : new System.Collections.Generic.List<AdapterBinding>();
            string selectedIp = (config.SelectedAdapterIp ?? "").Trim();
            AdapterBinding pinnedBinding = adapterBindings.Count == 1
                ? adapterBindings[0]
                : (!perConfigAdapters && config.EnableAdapterBinding && adapterCheck(selectedIp)
                    ? AdapterBindings.Resolve(config.SelectedAdapterName, selectedIp)
                    : AdapterBinding.Empty);
            if (!perConfigAdapters && config.EnableAdapterBinding && selectedIp.Length > 0 && !adapterCheck(selectedIp))
            {
                SimpleLogger.LogFirst("adapter-write-no-bind",
                    "[Adapter] The selected adapter is no longer available; using the default route.");
            }
            string xrayBalancePolicy = GetXrayBalancePolicy(
                loadBalanceBindings.Count > 0 ? config.AdapterBalancePolicy : config.XrayBalancePolicy);
            object strategy = GetXrayBalancerStrategy(xrayDir, xrayBalancePolicy);
            var rules = new List<object>
            {
                new { type = "field", ip = new[] { "127.0.0.0/8", "::1", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16" }, outboundTag = "direct" },
                new { type = "field", domain = new[] { "domain:get.geojs.io" }, balancerTag = "proxy" }
            };
            var blockDomains = new List<string>();
            var blockIps = new List<string>();
            var blockPorts = new List<string>();
            if (config.EnableAdBlock)
            {
                blockDomains.AddRange(new[] { "geosite:category-ads-all", "domain:analytics.google.com", "domain:google-analytics.com" });
            }
            if (config.EnableDirect && !string.IsNullOrWhiteSpace(config.LastBlockSplit))
            {
                foreach (var d in config.LastBlockSplit.Split(','))
                {
                    var t = d.Trim();
                    if (!string.IsNullOrEmpty(t)) 
                    {
                        if (t.All(c => char.IsDigit(c) || c == '-')) blockPorts.Add(t);
                        else if (t.Any(char.IsLetter)) blockDomains.Add($"domain:{t}");
                        else blockIps.Add(t);
                    }
                }
            }
            if (blockDomains.Count > 0)
                rules.Add(new { type = "field", domain = blockDomains.ToArray(), outboundTag = "block" });
            if (blockIps.Count > 0)
                rules.Add(new { type = "field", ip = blockIps.ToArray(), outboundTag = "block" });
            if (blockPorts.Count > 0)
                rules.Add(new { type = "field", port = string.Join(",", blockPorts), outboundTag = "block" });
            if (config.EnableDirect && config.LastXrayMode != "VPN Mode" && !string.IsNullOrWhiteSpace(config.LastManualSplit))
            {
                var domains = new List<string>();
                var ips = new List<string>();
                var ports = new List<string>();
                foreach (var item in config.LastManualSplit.Split(','))
                {
                    var t = item.Trim();
                    if (string.IsNullOrEmpty(t)) continue;
                    if (t.All(c => char.IsDigit(c) || c == '-')) ports.Add(t);
                    else if (t.Any(char.IsLetter)) domains.Add($"domain:{t}");
                    else ips.Add(t);
                }
                string targetTag = config.SplitTunnelMode == "INCLUSIVE" ? "proxy" : "direct";
                if (domains.Count > 0) rules.Add(new { type = "field", domain = domains.ToArray(), outboundTag = targetTag });
                if (ips.Count > 0) rules.Add(new { type = "field", ip = ips.ToArray(), outboundTag = targetTag });
                if (ports.Count > 0) rules.Add(new { type = "field", port = string.Join(",", ports), outboundTag = targetTag });
            }
            rules.Insert(0, new { type = "field", inboundTag = new[] { "api" }, outboundTag = "api" });
            rules.Insert(1, new { type = "field", domain = new[] { $"keyword:{AppSecrets.WorkerDomainKeyword}" }, outboundTag = "direct" });
            if (preferDirectDefault)
            {
                rules.Add(new { type = "field", network = "tcp,udp", outboundTag = "direct" });
            }
            else
            {
                rules.Add(new { type = "field", network = "tcp,udp", balancerTag = "proxy" });
            }
            bool lanAuth = config.AllowLanConnections
                        && config.EnableLanAuth
                        && !string.IsNullOrWhiteSpace(config.LanAuthUsername)
                        && !string.IsNullOrWhiteSpace(config.LanAuthPassword);
            object mixedSettings = lanAuth
                ? (object)new { udp = true, accounts = new[] { new { user = config.LanAuthUsername, pass = config.LanAuthPassword } } }
                : new { udp = true };
            var inbounds = new object[]
            {
                new { listen = config.AllowLanConnections && !singBoxOwnsLanPort ? "0.0.0.0" : "127.0.0.1", port = 10919, protocol = "mixed", tag = "mixed-in",
                      settings = mixedSettings,
                      sniffing = new { enabled = true, destOverride = new[] { "http", "tls", "quic", "fakedns" }, routeOnly = true } },
                new { listen = "127.0.0.1", port = 10999, protocol = "dokodemo-door", tag = "api",
                      settings = new { address = "127.0.0.1" } }
            };
            var outbounds = new List<object>();
            var proxyCloneTags = new List<string>();
            string sessionSuffix = Guid.NewGuid().ToString("N").Substring(0, 6);
            if (config.EnableV2rayChain && !string.IsNullOrWhiteSpace(config.V2rayChainJson))
            {
                try
                {
                    var exitHops = ResolveExitNodeHops(config.V2rayChainJson);
                    if (exitHops.Count > 0)
                    {
                        int chainCopies = loadBalanceBindings.Count > 0 ? loadBalanceBindings.Count : 1;
                        for (int slot = 0; slot < nodeCount * chainCopies; slot++)
                        {
                            int node = slot / chainCopies + 1;
                            int copy = slot % chainCopies + 1;
                            bool localEntry = activeChains != null && node - 1 < activeChains.Count
                                && activeChains[node - 1] != null && activeChains[node - 1].Count > 0
                                && XrayLinkParser.IsLocalOutbound(activeChains[node - 1][0]);
                            if (localEntry && copy > 1) continue;
                            string nodeTag = NodeTag(sessionSuffix, node, copy, chainCopies);
                            string baseTag = CloneTag(sessionSuffix, node, copy, chainCopies);
                            for (int h = 0; h < exitHops.Count; h++)
                            {
                                var clone = (JObject)exitHops[h].DeepClone();
                                clone["tag"] = ExitHopTag(baseTag, h);
                                ChainThroughDialer(clone, h + 1 < exitHops.Count ? ExitHopTag(baseTag, h + 1) : nodeTag);
                                if (h == 0) proxyCloneTags.Add(clone["tag"]!.ToString());
                                outbounds.Add(clone);
                            }
                        }
                    }
                    else
                    {
                        useCustomChain = false;
                    }
                }
                catch
                {
                    useCustomChain = false;
                }
            }
            var nodeOutboundTags = new List<string>();
            if (activeChains != null && activeChains.Count > 0)
            {
                int nodeCopies = loadBalanceBindings.Count > 0 ? loadBalanceBindings.Count : 1;
                for (int slot = 0; slot < activeChains.Count * nodeCopies; slot++)
                {
                    int node = slot / nodeCopies;
                    int copy = slot % nodeCopies + 1;
                    var chain = activeChains[node];
                    if (chain == null || chain.Count == 0) continue;
                    bool local = XrayLinkParser.IsLocalOutbound(chain[0]);
                    if (local && copy > 1) continue;
                    string tag = NodeTag(sessionSuffix, node + 1, copy, nodeCopies);
                    nodeOutboundTags.Add(tag);
                    var binding = loadBalanceBindings.Count > 0 ? loadBalanceBindings[copy - 1] : pinnedBinding;
                    for (int h = 0; h < chain.Count; h++)
                    {
                        var ob = (JObject)chain[h].DeepClone();
                        ob["tag"] = h == 0 ? tag : ExitHopTag(tag, h);
                        if (h == chain.Count - 1 && !local)
                        {
                            AdapterBindings.ApplyXrayBinding(ob, binding);
                        }
                        if (h + 1 < chain.Count) ChainThroughDialer(ob, ExitHopTag(tag, h + 1));
                        else
                        {
                            ob.Remove("proxySettings");
                            if (ob["streamSettings"] is JObject lastStream && lastStream["sockopt"] is JObject lastSockopt)
                            {
                                lastSockopt.Remove("dialerProxy");
                                if (lastSockopt.Count == 0) lastStream.Remove("sockopt");
                                if (lastStream.Count == 0) ob.Remove("streamSettings");
                            }
                        }
                        outbounds.Add(ob);
                    }
                }
            }
            if (config.EnableAdBlock || (config.EnableDirect && !string.IsNullOrWhiteSpace(config.LastBlockSplit)))
                outbounds.Add(new { tag = "block", protocol = "blackhole", settings = new { } });
            outbounds.Add(new { tag = "direct", protocol = "freedom", settings = new { } });
            if (config.EnableDirectUDP && !string.IsNullOrWhiteSpace(config.DirectUdpAdapterIp))
                outbounds.Add(new { tag = "direct-udp", protocol = "freedom", settings = new { }, sendThrough = config.DirectUdpAdapterIp });
            var allRules = new List<object>();
            if (config.EnableDirectUDP)
            {
                allRules.Add(new { type = "field", network = "udp", outboundTag = !string.IsNullOrWhiteSpace(config.DirectUdpAdapterIp) ? "direct-udp" : "direct" });
            }
            allRules.AddRange(rules);
            var balancerSelector = useCustomChain && proxyCloneTags.Count > 0
                ? new[] { "proxy-clone-" }
                : new[] { "proxy-node-" };
            var balancers = new List<object>
            {
                new { selector = balancerSelector, strategy = strategy, tag = "proxy" }
            };
            var cfg = new Dictionary<string, object>
            {
                ["log"] = new { loglevel = "info", access = Path.Combine(xrayDir, "access.log").Replace("\\", "/"), error = Path.Combine(xrayDir, "error.log").Replace("\\", "/") },
                ["stats"] = new { },
                ["api"] = new { tag = "api", services = new[] { "StatsService", "HandlerService" } },
                ["policy"] = new
                {
                    system = new
                    {
                        statsInboundUplink = true,
                        statsInboundDownlink = true,
                        statsOutboundUplink = true,
                        statsOutboundDownlink = true
                    }
                },
                ["inbounds"] = inbounds,
                ["outbounds"] = outbounds.ToArray(),
                ["routing"] = new { domainStrategy = "AsIs", rules = allRules.ToArray(), balancers = balancers.ToArray() }
            };
            if (xrayBalancePolicy == "leastPing" || xrayBalancePolicy == "leastLoad")
            {
                cfg["observatory"] = new
                {
                    subjectSelector = balancerSelector,
                    probeUrl = "https://www.google.com/generate_204",
                    probeInterval = "5s"
                };
            }
            if (config.EnableUpstreamDoh && !string.IsNullOrWhiteSpace(config.UpstreamDohUrl))
            {
                if (config.UpstreamDohUrl == "8.8.8.8" || config.UpstreamDohUrl == "8.8.4.4") 
                {
                    cfg["dns"] = new { servers = new[] { "https://dns.google/dns-query" } };
                }
                else if (config.UpstreamDohUrl == "1.1.1.1" || config.UpstreamDohUrl == "1.0.0.1") 
                {
                    cfg["dns"] = new { servers = new[] { "https://cloudflare-dns.com/dns-query" } };
                }
                else if (config.UpstreamDohUrl == "9.9.9.9")
                {
                    cfg["dns"] = new { servers = new[] { "https://dns.quad9.net/dns-query" } };
                }
                else 
                {
                    cfg["dns"] = new { servers = new[] { config.UpstreamDohUrl } };
                }
            }
            try
            {
                var json = JsonConvert.SerializeObject(cfg, Formatting.Indented);
                File.WriteAllText(Path.Combine(xrayDir, "config.json"), json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write Xray config. Check disk space and permissions.\n\n{ex.Message}", "Config Error");
                return false;
            }
        }

        private static string GetXrayBalancePolicy(string? policy)
        {
            return (policy ?? string.Empty).ToLowerInvariant() switch
            {
                "leastload" => "leastLoad",
                "leastping" => "leastPing",
                "roundrobin" => "roundRobin",
                "random" => "random",
                "leastconn" => "roundRobin",
                "first" => "roundRobin",
                _ => "roundRobin"
            };
        }

        private static readonly Dictionary<string, bool> XrayBalancerSupportCache = new();

        private static object GetXrayBalancerStrategy(string xrayDir, string xrayBalancePolicy)
        {
            if (xrayBalancePolicy == "leastLoad" || xrayBalancePolicy == "leastPing")
            {
                if (IsXrayBalancerStrategySupported(xrayDir, xrayBalancePolicy))
                {
                    return new { type = xrayBalancePolicy, settings = new { expected = 1 } };
                }
                return new { type = "roundRobin" };
            }
            return new { type = xrayBalancePolicy };
        }

        private static bool IsXrayBalancerStrategySupported(string xrayDir, string strategyType)
        {
            try
            {
                string xrayExeStamp = "";
                try
                {
                    var xrayExePath = Path.Combine(xrayDir, "xray.exe");
                    if (File.Exists(xrayExePath))
                        xrayExeStamp = File.GetLastWriteTimeUtc(xrayExePath).Ticks.ToString("X");
                }
                catch { }
                string cacheKey = $"{xrayDir}|{xrayExeStamp}|{strategyType}";
                if (XrayBalancerSupportCache.TryGetValue(cacheKey, out bool supported))
                {
                    return supported;
                }
                var xrayExe = Path.Combine(xrayDir, "xray.exe");
                if (!File.Exists(xrayExe))
                {
                    XrayBalancerSupportCache[cacheKey] = false;
                    return false;
                }
                var tempConfig = Path.Combine(xrayDir, $"xray_strategy_check_{strategyType}.json");
                var config = new Dictionary<string, object>
                {
                    ["log"] = new { loglevel = "none" },
                    ["inbounds"] = new[] { new { listen = "127.0.0.1", port = 10919, protocol = "socks", settings = new { auth = "noauth" } } },
                    ["outbounds"] = new[]
                    {
                        new { tag = "a", protocol = "freedom", settings = new { } },
                        new { tag = "b", protocol = "freedom", settings = new { } }
                    },
                    ["routing"] = new
                    {
                        domainStrategy = "AsIs",
                        rules = new[] { new { type = "field", network = "tcp,udp", balancerTag = "test-balancer" } },
                        balancers = new[] { new { selector = new[] { "a", "b" }, strategy = new { type = strategyType, settings = new { expected = 1 } }, tag = "test-balancer" } }
                    }
                };
                if (strategyType == "leastPing" || strategyType == "leastLoad")
                {
                    config["observatory"] = new
                    {
                        subjectSelector = new[] { "a", "b" },
                        probeUrl = "https://www.google.com/generate_204",
                        probeInterval = "5s"
                    };
                }
                File.WriteAllText(tempConfig, JsonConvert.SerializeObject(config, Formatting.Indented));
                var startInfo = new ProcessStartInfo
                {
                    FileName = xrayExe,
                    Arguments = $"-test -c \"{tempConfig}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    XrayBalancerSupportCache[cacheKey] = false;
                    File.Delete(tempConfig);
                    return false;
                }
                var outTask = process.StandardOutput.ReadToEndAsync();
                var errTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(); } catch { }
                    process.WaitForExit();
                }
                Task.WaitAll(outTask, errTask);
                bool result = process.ExitCode == 0;
                File.Delete(tempConfig);
                XrayBalancerSupportCache[cacheKey] = result;
                return result;
            }
            catch
            {
                return false;
            }
        }

        public static void ChainThroughDialer(JObject outbound, string dialerTag)
        {
            if (outbound == null || string.IsNullOrWhiteSpace(dialerTag)) return;
            if (outbound["streamSettings"] is not JObject stream)
            {
                stream = new JObject();
                outbound["streamSettings"] = stream;
            }
            if (stream["sockopt"] is not JObject sockopt)
            {
                sockopt = new JObject();
                stream["sockopt"] = sockopt;
            }
            sockopt["dialerProxy"] = dialerTag.Trim();
        }

        private static List<JObject> ResolveExitNodeHops(string raw)
        {
            var hops = new List<JObject>();
            if (string.IsNullOrWhiteSpace(raw)) return hops;
            if (ExitNodeChain.IsPlainTcpVless(raw)) return hops;
            if (TunnelConfigParser.LooksLikeTunnel(raw)) return hops;
            try
            {
                if (!ConfigConverter.TryXrayChain(raw, out var chain, out _, out _) || chain.Count == 0)
                {
                    CrimsonX.Services.SimpleLogger.Log("[ExitNode] The custom exit node could not be used by xray.");
                    return hops;
                }
                foreach (var hop in chain)
                {
                    string protocol = hop["protocol"]?.ToString() ?? "";
                    if (protocol == "freedom" || protocol == "blackhole") continue;
                    hops.Add(hop);
                }
            }
            catch (Exception ex)
            {
                CrimsonX.Services.SimpleLogger.Log(ex);
            }
            return hops;
        }

        public static string NodeTag(string session, int node, int copy, int copies) =>
            copies > 1 ? $"proxy-node-{session}-{node}-{copy}" : $"proxy-node-{session}-{node}";

        public static string CloneTag(string session, int node, int copy, int copies) =>
            copies > 1 ? $"proxy-clone-{session}-{node}-{copy}" : $"proxy-clone-{session}-{node}";

    public static string ExitHopTag(string baseTag, int index) =>
        index == 0
            ? baseTag
            : baseTag.Replace("proxy-clone-", "proxy-hop-").Replace("proxy-node-", "proxy-hop-") + "-h" + (index + 1);

        public static string ProbeSendThrough(AppConfig config, Func<string, bool>? isAvailable = null)
            => ProbeBinding(config, isAvailable).Primary;

        public static AdapterBinding ProbeBinding(AppConfig config, Func<string, bool>? isAvailable = null)
        {
            if (config == null) return AdapterBinding.Empty;
            if (UsesPerConfigAdapters(config)) return AdapterBinding.Empty;
            var adapterCheck = isAvailable ?? IsAdapterAvailable;
            if (config.EnableLoadBalanceAdapters)
            {
                int stored = AdapterIps(config.LoadBalanceAdapters).Count;
                var bindings = AdapterBindings.ResolveAvailable(config.LoadBalanceAdapters, adapterCheck);
                if (bindings.Count > 0)
                {
                    if (stored > bindings.Count)
                    {
                        SimpleLogger.LogFirst("adapter-probe-unavailable",
                            $"[Adapter] Ignoring {stored - bindings.Count} load-balance adapter(s) that are no longer available.");
                    }
                    return bindings[0];
                }
                if (stored > 0)
                {
                    SimpleLogger.LogFirst("adapter-probe-no-lb",
                        "[Adapter] No load-balance adapter is available; using the default route.");
                }
            }
            string selected = (config.SelectedAdapterIp ?? "").Trim();
            if (config.EnableAdapterBinding && selected.Length > 0 && !adapterCheck(selected))
            {
                SimpleLogger.LogFirst("adapter-probe-no-bind",
                    "[Adapter] The selected adapter is no longer available; using the default route.");
            }
            return config.EnableAdapterBinding && adapterCheck(selected)
                ? AdapterBindings.Resolve(config.SelectedAdapterName, selected)
                : AdapterBinding.Empty;
        }
        public static bool UsesPerConfigAdapters(AppConfig? config)
            => config != null && config.EnableCustomConfigs && config.EnablePerConfigAdapters;

        public static (string Name, string Ip) CustomConfigAdapter(AppConfig? config, int slot)
            => UsesPerConfigAdapters(config)
                ? SplitAdapter(slot == 2 ? config!.CustomConfig2Adapter : config!.CustomConfig1Adapter)
                : ("", "");

        public static (string Name, string Ip) SplitAdapter(string? pick)
        {
            string text = (pick ?? "").Trim();
            if (text.Length == 0) return ("", "");
            int at = text.LastIndexOf(" - ", StringComparison.Ordinal);
            if (at < 0) return ("", text);
            return (text.Substring(0, at).Trim(), text.Substring(at + 3).Trim());
        }

        public static string ApplySendThrough(string xrayDoc, string adapterIp)
        {
            if (string.IsNullOrWhiteSpace(xrayDoc) || string.IsNullOrWhiteSpace(adapterIp)) return xrayDoc ?? "";
            return ApplySendThrough(xrayDoc, AdapterBindings.ResolveFromIp(adapterIp));
        }

        public static string ApplySendThrough(string xrayDoc, AdapterBinding binding)
        {
            if (string.IsNullOrWhiteSpace(xrayDoc) || binding == null || !binding.HasAny) return xrayDoc ?? "";
            try
            {
                var root = JObject.Parse(xrayDoc);
                if (root["outbounds"] is not JArray outbounds) return xrayDoc;
                JObject outbound = null;
                foreach (var entry in outbounds)
                {
                    if (entry is not JObject candidate) continue;
                    if (XrayLinkParser.IsLocalOutbound(candidate)) continue;
                    outbound = candidate;
                }
                if (outbound == null) return xrayDoc;
                AdapterBindings.ApplyXrayBinding(outbound, binding);
                return root.ToString(Formatting.None);
            }
            catch
            {
                return xrayDoc;
            }
        }
        public static System.Collections.Generic.List<string> AdapterIps(System.Collections.Generic.IEnumerable<string>? adapters)
        {
            var ips = new System.Collections.Generic.List<string>();
            if (adapters == null) return ips;
            foreach (string item in adapters)
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                int at = item.LastIndexOf(" - ", StringComparison.Ordinal);
                string ip = (at >= 0 ? item.Substring(at + 3) : item).Trim();
                if (ip.Length == 0 || ips.Contains(ip)) continue;
                ips.Add(ip);
            }
            return ips;
        }

        public static System.Collections.Generic.List<string> AvailableAdapterIps(
            System.Collections.Generic.IEnumerable<string>? adapters, Func<string, bool>? isAvailable = null)
        {
            var check = isAvailable ?? IsAdapterAvailable;
            var available = new System.Collections.Generic.List<string>();
            foreach (string ip in AdapterIps(adapters))
            {
                if (check(ip)) available.Add(ip);
            }
            return available;
        }

        public static bool IsAdapterAvailable(string ip) => AdapterBindings.IsAddressAvailable(ip);

    }

    public static class SingboxConfigWriter
    {
        internal const string TunInboundTag = "tun-in";

        internal static bool SplitListIsProxied(AppConfig config)
            => string.Equals(config?.SplitTunnelMode, "INCLUSIVE", StringComparison.Ordinal);

        internal static object BuildTunInbound()
            => BuildTunInbound(false);

        internal static object BuildTunInbound(bool ipv6)
        {
            return new
            {
                type = "tun", tag = TunInboundTag,
                interface_name = TunProfile.InterfaceName,
                address = ipv6
                    ? new[] { TunProfile.AddressPrefix, TunProfile.Ipv6AddressPrefix }
                    : new[] { TunProfile.AddressPrefix },
                mtu = 9000, auto_route = true, strict_route = true,
                stack = "mixed", endpoint_independent_nat = true
            };
        }

        private static object BuildDirectUdpOutbound(AppConfig config)
        {
            var binding = AdapterBindings.Resolve(config.DirectUdpAdapterName, config.DirectUdpAdapterIp);
            var outbound = new System.Collections.Generic.Dictionary<string, object>
            {
                ["type"] = "direct",
                ["tag"] = "direct-udp"
            };
            if (binding.Name.Length > 0) outbound["bind_interface"] = binding.Name;
            if (binding.HasV4) outbound["inet4_bind_address"] = binding.Ipv4;
            if (binding.HasV6) outbound["inet6_bind_address"] = binding.Ipv6;
            return outbound;
        }

        public static bool Write(AppConfig config, string sbDir, bool skipExitNode = false)
        {
            var currentExe = Path.GetFileName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "");
            var systemBypassApps = new List<string>
            {
                currentExe, "conjure-client.exe", "dnstt-client.exe",
                "xray.exe", "xray",
                "sing-box.exe", "sing-box"
            };
            if (currentExe.Length > 0 && !systemBypassApps.Contains(currentExe.ToLowerInvariant()))
                systemBypassApps.Add(currentExe.ToLowerInvariant());
            var userApps = new List<string>();
            if (config.EnableDirect && !string.IsNullOrWhiteSpace(config.LastAppSplit))
            {
                foreach (var app in config.LastAppSplit.Split(','))
                {
                    var a = app.Trim();
                    if (string.IsNullOrEmpty(a)) continue;
                    var appExe = a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? a : a + ".exe";
                    var appBase = Path.GetFileNameWithoutExtension(appExe);
                    if (!userApps.Contains(appExe)) userApps.Add(appExe);
                    if (!userApps.Contains(appBase)) userApps.Add(appBase);
                    if (!userApps.Contains(appExe.ToLower())) userApps.Add(appExe.ToLower());
                    if (!userApps.Contains(appBase.ToLower())) userApps.Add(appBase.ToLower());
                }
            }

            var endpointScan = AdapterBindings.ScanEndpoints(config);
            bool tunnelIpv6 = endpointScan.UsesIpv6;
            string dnsStrategy = endpointScan.NeedsDomainIpv6 ? "prefer_ipv4" : "ipv4_only";
            // ── Exit node ────────────────────────────────────────────────────────────────────────
            bool   exitChained  = false;
            string exitError    = "";
            ExitNodeChain.ExitNodePlane exitPlane = ExitNodeChain.ExitNodePlane.None;
            System.Collections.Generic.List<JObject> exitHops = null;
            if (!skipExitNode)
                exitHops = ExitNodeChain.BuildExitChain(config, out exitPlane, out exitChained, out exitError);
            if (exitChained) ExitNodeChain.AllocateProxyPort();
            bool tunInbound = string.Equals(config.LastXrayMode, "VPN Mode", StringComparison.OrdinalIgnoreCase);
            if (exitError.Length > 0) CrimsonX.Services.SimpleLogger.Log($"[ExitNode] {exitError}");
            string proxyTag = exitChained ? ExitNodeChain.EndpointTag : ExitNodeChain.TransportTag;
            var appRules = AppRulesSingboxBuilder.Build(config, null, proxyTag);
            string configPath = Path.Combine(sbDir, "config.json");
            object BuildSbConfig(AppRulesSingboxResult rules)
            {
                var sbRules = new List<object>
                {
                    new { action = "sniff" },
                    new { protocol = "dns", action = "hijack-dns" },
                    new { port = new[] { 53 }, network = "udp", action = "hijack-dns" },
                    new { port = new[] { 53 }, network = "tcp", action = "hijack-dns" }
                };
                if (exitChained)
                {
                    sbRules.Add(new { inbound = new[] { ExitNodeChain.InboundTag }, ip_is_private = true, action = "route", outbound = "direct" });
                    sbRules.Add(new { inbound = new[] { ExitNodeChain.InboundTag }, action = "route", outbound = proxyTag });
                }
                if (tunInbound)
                {
                    if (tunnelIpv6)
                    {
                        var engineApps = new List<string> { "xray.exe", "xray", "sing-box.exe", "sing-box", "amnezia-box.exe", "amnezia-box", "conjure-client.exe", "dnstt-client.exe" };
                        sbRules.Add(new { inbound = new[] { TunInboundTag }, process_name = engineApps.ToArray(), ip_cidr = new[] { "::/0" }, action = "route", outbound = "direct" });
                        sbRules.Add(new { ip_is_private = true, action = "route", outbound = "direct" });
                        sbRules.Add(new { ip_cidr = new[] { "::/0" }, action = "reject" });
                    }
                    sbRules.Add(new { inbound = new[] { TunInboundTag }, process_name = systemBypassApps.ToArray(), action = "route", outbound = "direct" });
                }
                if (!tunnelIpv6)
                    sbRules.Add(new { ip_is_private = true, action = "route", outbound = "direct" });
                if (rules.RouteRules.Count > 0)
                    sbRules.AddRange(rules.RouteRules);
                bool splitListProxied  = SplitListIsProxied(config);
                bool proxyIsTheDefault = !(config.EnableDirect && splitListProxied);
                if (proxyIsTheDefault)
                {
                    if (!splitListProxied && userApps.Count > 0)
                        sbRules.Add(new { process_name = userApps.ToArray(), protocol = "quic", action = "route", outbound = "direct" });
                    sbRules.Add(new { protocol = "quic", action = "reject", method = "default" });
                }
                sbRules.Add(new { domain_keyword = new[] { AppSecrets.WorkerDomainKeyword }, action = "route", outbound = "direct" });
                if (userApps.Count > 0)
                {
                    string targetOutbound = SplitListIsProxied(config) ? proxyTag : "direct";
                    if (splitListProxied)
                        sbRules.Add(new { process_name = userApps.ToArray(), protocol = "quic", action = "reject", method = "default" });
                    sbRules.Add(new { process_name = userApps.ToArray(), action = "route", outbound = targetOutbound });
                }
                if (config.EnableDirectUDP)
                {
                    sbRules.Add(new { network = "udp", action = "route", outbound = !string.IsNullOrWhiteSpace(config.DirectUdpAdapterIp) ? "direct-udp" : "direct" });
                }

                // ── Upstream resolvers ───────────────────────────────────────────────────────────────
                var dnsServers = new List<object>
                {
                    new { tag = "dns_direct", type = "udp", server = "8.8.8.8" }
                };
                if (rules.DnsServers.Count > 0)
                    dnsServers.AddRange(rules.DnsServers);
                if (config.EnableUpstreamDoh && !string.IsNullOrWhiteSpace(config.UpstreamDohUrl))
                {
                    if (config.UpstreamDohUrl.StartsWith("https://"))
                    {
                        try
                        {
                            var u = new Uri(config.UpstreamDohUrl);
                            var dPath = u.AbsolutePath == "/" ? "/dns-query" : u.PathAndQuery;
                            dnsServers.Add(new { tag = "dns_proxy", type = "https", server = u.Host, path = dPath, detour = "proxy" });
                        }
                        catch
                        {
                            dnsServers.Add(new { tag = "dns_proxy", type = "tcp", server = "1.1.1.1", detour = "proxy" });
                        }
                    }
                    else
                    {
                        if (config.UpstreamDohUrl == "8.8.8.8" || config.UpstreamDohUrl == "8.8.4.4") 
                        {
                            dnsServers.Add(new { tag = "dns_proxy", type = "https", server = "dns.google", path = "/dns-query", detour = "proxy" });
                        }
                        else if (config.UpstreamDohUrl == "1.1.1.1" || config.UpstreamDohUrl == "1.0.0.1") 
                        {
                            dnsServers.Add(new { tag = "dns_proxy", type = "https", server = "cloudflare-dns.com", path = "/dns-query", detour = "proxy" });
                        }
                        else if (config.UpstreamDohUrl == "9.9.9.9")
                        {
                            dnsServers.Add(new { tag = "dns_proxy", type = "https", server = "dns.quad9.net", path = "/dns-query", detour = "proxy" });
                        }
                        else 
                        {
                            dnsServers.Add(new { tag = "dns_proxy", type = "tcp", server = config.UpstreamDohUrl, detour = "proxy" });
                        }
                    }
                }
                else
                {
                    dnsServers.Add(new { tag = "dns_proxy", type = "https", server = "dns.google", path = "/dns-query", detour = "proxy" });
                }
                var dnsRules = new List<object>
                {
                    new { domain_keyword = new[] { "stun", "cdn77", "datapacket" }, action = "route", server = "dns_direct" }
                };
                if (rules.DnsRules.Count > 0)
                    dnsRules.AddRange(rules.DnsRules);

                // ── Catch-all: DNS dependency follows traffic dependency ─────────────────────────────
                if (config.EnableDirect && config.SplitTunnelMode == "INCLUSIVE")
                {
                    if (userApps.Count > 0)
                    {
                        dnsRules.Add(new { process_name = userApps.ToArray(), action = "route", server = "dns_proxy" });
                    }
                    dnsRules.Add(new { action = "route", server = "dns_direct" });
                }
                else if (config.EnableDirect && config.SplitTunnelMode == "EXCLUSIVE")
                {
                    if (userApps.Count > 0)
                    {
                        dnsRules.Add(new { process_name = userApps.ToArray(), action = "route", server = "dns_direct" });
                    }
                    dnsRules.Add(new { action = "route", server = "dns_proxy" });
                }
                else
                {
                    dnsRules.Add(new { action = "route", server = "dns_proxy" });
                }
                string clashController = SingboxClashApi.EnsureController();
                string clashSecret     = SingboxClashApi.EnsureSecret();
                var sbConfig = new
                {
                    log = new { level = "fatal" },
                    dns = new
                    {
                        servers = dnsServers.ToArray(),
                        rules = dnsRules.ToArray(),
                        strategy = dnsStrategy
                    },
                    inbounds = tunInbound ? new object[] { BuildTunInbound(tunnelIpv6) } : new object[0],
                    outbounds = new System.Collections.Generic.List<object> {
                        new { type = "socks", tag = "proxy", server = "127.0.0.1", server_port = 10919 },
                        new { type = "direct", tag = "direct" }
                    }.Concat(
                        (config.EnableDirectUDP && !string.IsNullOrWhiteSpace(config.DirectUdpAdapterIp))
                            ? new object[] { BuildDirectUdpOutbound(config) }
                            : new object[0]
                    ).Concat(rules.Outbounds).ToArray(),
                    route = new
                    {
                        rules = sbRules.ToArray(),
                        rule_set = rules.RuleSets.ToArray(),
                        final = config.EnableDirect && config.SplitTunnelMode == "INCLUSIVE" ? "direct" : proxyTag,
                        default_domain_resolver = new { server = "dns_direct" },
                        auto_detect_interface = true,
                        find_process = true
                    },
                    experimental = new
                    {
                        clash_api = new
                        {
                            external_controller = clashController,
                            secret              = clashSecret
                        }
                    }
                };
                if (!exitChained) return sbConfig;
                var document = JObject.FromObject(sbConfig);
                ExitNodeChain.PlaceInto(document, exitHops, exitPlane);
                ExitNodeChain.AddProxyInbound(document, config);
                return document;
            }
            try
            {
                var json = JsonConvert.SerializeObject(BuildSbConfig(appRules), Formatting.Indented);
                File.WriteAllText(configPath, json);
                if (appRules.CustomProxies.Count > 0 && !SingboxConfigValidator.Check(sbDir, configPath))
                {
                    var invalidKeys = SingboxConfigValidator.FindInvalidOutbounds(sbDir, appRules.CustomProxies);
                    if (invalidKeys.Count > 0)
                    {
                        var safeRules = AppRulesSingboxBuilder.Build(config, invalidKeys);
                        var safeJson = JsonConvert.SerializeObject(BuildSbConfig(safeRules), Formatting.Indented);
                        File.WriteAllText(configPath, safeJson);
                        CrimsonX.Services.SimpleLogger.Log(
                            $"[SingBox] Dropped {invalidKeys.Count} invalid custom proxy outbound(s).");
                        MainWindow.Instance?.ShowToast(CrimsonX.Localization.AppStrings.ToastCustomProxyDropped, ToastKind.Error);
                        if (safeRules.CustomProxies.Count > 0 && !SingboxConfigValidator.Check(sbDir, configPath))
                        {
                            var allKeys = new HashSet<string>(StringComparer.Ordinal);
                            foreach (var probe in appRules.CustomProxies) allKeys.Add(probe.Key);
                            var bareRules = AppRulesSingboxBuilder.Build(config, allKeys);
                            File.WriteAllText(configPath,
                                JsonConvert.SerializeObject(BuildSbConfig(bareRules), Formatting.Indented));
                            CrimsonX.Services.SimpleLogger.Log(
                                $"[SingBox] Config was still rejected; dropped all {allKeys.Count} custom proxy outbound(s).");
                        }
                    }
                    else
                    {
                        CrimsonX.Services.SimpleLogger.Log(
                            "[SingBox] Config rejected but no single custom proxy outbound is at fault; keeping it.");
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write Sing-Box config.\n\n{ex.Message}", "Config Error");
                return false;
            }
        }
    }
}

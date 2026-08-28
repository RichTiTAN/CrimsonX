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

using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CrimsonX.Models;

namespace CrimsonX.Services
{
    public static class XrayConfigWriter
    {
        public static bool Write(AppConfig config, string xrayDir, System.Collections.Generic.List<JObject>? activeOutbounds = null)
        {
            int nodeCount = 1;
            if (activeOutbounds != null && activeOutbounds.Count > 0) nodeCount = activeOutbounds.Count;
            else {
                nodeCount = 1;
                if (nodeCount > 8) nodeCount = 8;
            }

            bool useCustomChain = config.EnableV2rayChain && !string.IsNullOrWhiteSpace(config.V2rayChainJson);
            bool preferDirectDefault = config.EnableDirect && config.SplitTunnelMode == "INCLUSIVE" && config.LastXrayMode != "VPN Mode";

            string xrayBalancePolicy = GetXrayBalancePolicy(config.XrayBalancePolicy);
            object strategy = GetXrayBalancerStrategy(xrayDir, xrayBalancePolicy);

            var rules = new List<object>
            {
                new { type = "field", ip = new[] { "127.0.0.0/8", "::1", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16" }, outboundTag = "direct" },
                preferDirectDefault
                    ? new { type = "field", domain = new[] { "domain:get.geojs.io" }, outboundTag = "direct" }
                    : new { type = "field", domain = new[] { "domain:get.geojs.io" }, balancerTag = "proxy" }
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
            rules.Insert(1, new { type = "field", domain = AppSecrets.WorkerDomains.Select(d => $"domain:{d}").ToArray(), outboundTag = "direct" });
            
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
                new { listen = config.AllowLanConnections ? "0.0.0.0" : "127.0.0.1", port = 10919, protocol = "mixed", tag = "mixed-in",
                      settings = mixedSettings,
                      sniffing = new { enabled = true, destOverride = new[] { "http", "tls", "quic", "fakedns" } } },
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
                    var v2p = JObject.Parse(config.V2rayChainJson);
                    JObject? v2ob;

                    if (v2p["outbounds"] is JArray obArr)
                    {
                        v2ob = obArr.OfType<JObject>()
                            .FirstOrDefault(o => o["protocol"]?.ToString() != "freedom" && o["protocol"]?.ToString() != "blackhole");
                    }
                    else
                    {
                        v2ob = v2p;
                    }

                    if (v2ob != null)
                    {
                        for (int i = 1; i <= nodeCount; i++)
                        {
                            string cloneTag = $"proxy-clone-{sessionSuffix}-{i}";
                            proxyCloneTags.Add(cloneTag);

                            var clone = (JObject)v2ob.DeepClone();
                            clone["tag"] = cloneTag;
                            clone["proxySettings"] = JObject.FromObject(new { tag = $"proxy-node-{sessionSuffix}-{i}" });

                            outbounds.Add(clone);
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
            if (activeOutbounds != null && activeOutbounds.Count > 0)
            {
                for (int i = 0; i < activeOutbounds.Count; i++)
                {
                    string tag = $"proxy-node-{sessionSuffix}-{i + 1}";
                    nodeOutboundTags.Add(tag);
                    var ob = (JObject)activeOutbounds[i].DeepClone();
                    ob["tag"] = tag;
                    
                    if (config.EnableAdapterBinding && !string.IsNullOrWhiteSpace(config.SelectedAdapterIp))
                    {
                        ob["sendThrough"] = config.SelectedAdapterIp;
                    }
                    
                    outbounds.Add(ob);
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
                string cacheKey = $"{xrayDir}|{strategyType}";
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

                process.WaitForExit(5000);
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
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
    }

    public static class SingboxConfigWriter
    {
        public static bool Write(AppConfig config, string sbDir)
        {
            var currentExe = Path.GetFileName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "").ToLower();

            var systemBypassApps = new List<string>
            {
                currentExe, "conjure-client.exe", "dnstt-client.exe",
                "xray.exe", "xray",
                "sing-box.exe", "sing-box", "cmd.exe", "conhost.exe",
                "powershell.exe", "pwsh.exe"
            };

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

            var sbRules = new List<object>
            {
                new { action = "sniff" },
                new { protocol = "quic", action = "reject", method = "default" },
                new { protocol = "dns", action = "hijack-dns" },
                new { port = new[] { 53 }, network = "udp", action = "hijack-dns" },
                new { port = new[] { 53 }, network = "tcp", action = "hijack-dns" },
                new { domain = AppSecrets.WorkerDomains, action = "route", outbound = "direct" },
                new { process_name = systemBypassApps.ToArray(), action = "route", outbound = "direct" }
            };

            if (config.EnableDirectUDP)
            {
                sbRules.Add(new { network = "udp", action = "route", outbound = !string.IsNullOrWhiteSpace(config.DirectUdpAdapterIp) ? "direct-udp" : "direct" });
            }

            if (userApps.Count > 0)
            {
                string targetOutbound = config.SplitTunnelMode == "INCLUSIVE" ? "proxy" : "direct";
                sbRules.Add(new { process_name = userApps.ToArray(), action = "route", outbound = targetOutbound });
            }

            sbRules.Add(new { network = "udp", port = new[] { 3478, 5349 }, action = "route", outbound = "direct" });
            sbRules.Add(new { ip_is_private = true, action = "route", outbound = "direct" });

            var dnsServers = new List<object>
            {
                new { tag = "dns_direct", type = "udp", server = "8.8.8.8" }
            };

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

            if (config.EnableDirect && config.SplitTunnelMode == "INCLUSIVE")
            {
                if (userApps.Count > 0)
                {
                    dnsRules.Add(new { process_name = userApps.ToArray(), action = "route", server = "dns_proxy" });
                }
                dnsRules.Add(new { action = "route", server = "dns_proxy" }); 
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

            var sbConfig = new
            {
                log = new { level = "fatal" },
                dns = new
                {
                    servers = dnsServers.ToArray(),
                    rules = dnsRules.ToArray(),
                    strategy = "ipv4_only"
                },
                inbounds = new object[]
                {
                    new
                    {
                        type = "tun", tag = "tun-in",
                        interface_name = "singbox_tun",
                        address = new[] { "172.18.0.1/30" },
                        mtu = 9000, auto_route = true, strict_route = true,
                        stack = "mixed", endpoint_independent_nat = true
                    }
                },
                outbounds = (new System.Collections.Generic.List<object> {
                    new { type = "socks", tag = "proxy", server = "127.0.0.1", server_port = 10919 },
                    new { type = "direct", tag = "direct" }
                }.Concat(
                    (config.EnableDirectUDP && !string.IsNullOrWhiteSpace(config.DirectUdpAdapterIp))
                        ? new object[] { new { type = "direct", tag = "direct-udp", bind_interface = config.DirectUdpAdapterName, inet4_bind_address = config.DirectUdpAdapterIp } }
                        : new object[0]
                )).ToArray(),
                route = new
                {
                    rules = sbRules.ToArray(),
                    final = config.EnableDirect && config.SplitTunnelMode == "INCLUSIVE" ? "direct" : "proxy",
                    default_domain_resolver = new { server = "dns_direct" },
                    auto_detect_interface = true,
                    find_process = true
                }
            };

            try
            {
                var json = JsonConvert.SerializeObject(sbConfig, Formatting.Indented);
                File.WriteAllText(Path.Combine(sbDir, "config.json"), json);
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


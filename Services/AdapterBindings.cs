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
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public enum EndpointFamily
    {
        Unknown,
        Ipv4,
        Ipv6
    }

    public sealed class AdapterBinding
    {
        public string Name { get; init; } = "";
        public string Ipv4 { get; init; } = "";
        public string Ipv6 { get; init; } = "";

        public bool HasV4 => Ipv4.Length > 0;
        public bool HasV6 => Ipv6.Length > 0;
        public bool HasAny => HasV4 || HasV6;
        public string Primary => HasV4 ? Ipv4 : Ipv6;

        public static AdapterBinding Empty { get; } = new AdapterBinding();
    }

    public sealed class EndpointScanResult
    {
        public bool UsesIpv6 { get; set; }
        public bool NeedsDomainIpv6 { get; set; }
    }
    public static class AdapterBindings
    {
        private sealed class CachedBinding
        {
            public AdapterBinding Binding = AdapterBinding.Empty;
            public DateTime Stamp;
        }

        private sealed class CachedFamily
        {
            public EndpointFamily Family;
            public DateTime Stamp;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, CachedBinding> Bindings = new Dictionary<string, CachedBinding>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, CachedFamily> Domains = new Dictionary<string, CachedFamily>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan BindingTtl = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan DomainTtl = TimeSpan.FromMinutes(10);
        private static System.Collections.Generic.HashSet<string> LiveAddressesCache =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan LiveAddressesTtl = TimeSpan.FromSeconds(2);
        private static DateTime LiveAddressesStamp = DateTime.MinValue;
        private const int DomainBudgetMs = 400;

        internal static Func<string, EndpointFamily> DomainResolver { get; set; }

        public static AdapterBinding Resolve(string adapterName, string preferredIp = "")
        {
            string name = (adapterName ?? "").Trim();
            string ipText = (preferredIp ?? "").Trim();
            if (name.Length == 0 && ipText.Length == 0) return AdapterBinding.Empty;
            string key = name + "|" + ipText;
            lock (Sync)
            {
                if (Bindings.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.Stamp < BindingTtl) return hit.Binding;
            }

            IPAddress preferred = null;
            IPAddress.TryParse(StripBrackets(ipText), out preferred);
            NetworkInterface nic = null;
            try
            {
                foreach (var candidate in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (candidate.OperationalStatus != OperationalStatus.Up) continue;
                    if (candidate.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (name.Length > 0 && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        nic = candidate;
                        break;
                    }
                    if (preferred != null && Useful(candidate).Any(i => i.Address.Equals(preferred)))
                    {
                        nic = candidate;
                        break;
                    }
                }
            }
            catch (Exception ex) { SimpleLogger.Log(ex); }

            var binding = BuildBinding(nic, name, preferred);
            lock (Sync) { Bindings[key] = new CachedBinding { Binding = binding, Stamp = DateTime.UtcNow }; }
            return binding;
        }

        public static AdapterBinding ResolveFromIp(string ip)
        {
            string text = (ip ?? "").Trim();
            return text.Length == 0 ? AdapterBinding.Empty : Resolve("", text);
        }

        public static AdapterBinding ResolveSelected(Models.AppConfig config)
        {
            if (config == null) return AdapterBinding.Empty;
            if (config.EnableLoadBalanceAdapters)
            {
                var available = ResolveAvailable(config.LoadBalanceAdapters, null);
                if (available.Count > 0) return available[0];
            }
            if (config.EnableAdapterBinding && !string.IsNullOrWhiteSpace(config.SelectedAdapterIp))
                return Resolve(config.SelectedAdapterName, config.SelectedAdapterIp);
            return AdapterBinding.Empty;
        }

        public static List<AdapterBinding> ResolveAvailable(IEnumerable<string> picks, Func<string, bool> isAvailable = null)
        {
            var result = new List<AdapterBinding>();
            var check = isAvailable ?? IsAddressAvailable;
            foreach (string pick in picks ?? Enumerable.Empty<string>())
            {
                string text = (pick ?? "").Trim();
                if (text.Length == 0) continue;
                int at = text.LastIndexOf(" - ", StringComparison.Ordinal);
                string name = at > 0 ? text.Substring(0, at).Trim() : "";
                string ip = at >= 0 ? text.Substring(at + 3).Trim() : text;
                if (ip.Length == 0 || !check(ip)) continue;
                var binding = Resolve(name, ip);
                if (!binding.HasAny) continue;
                if (result.Any(b => string.Equals(b.Name, binding.Name, StringComparison.Ordinal)
                                 && string.Equals(b.Ipv4, binding.Ipv4, StringComparison.Ordinal)
                                 && string.Equals(b.Ipv6, binding.Ipv6, StringComparison.Ordinal))) continue;
                result.Add(binding);
            }
            return result;
        }

        public static bool IsAddressAvailable(string ip)
        {
            string text = StripBrackets(ip);
            if (!IPAddress.TryParse(text, out var wanted)) return false;
            return LiveAddresses().Contains(wanted.ToString());
        }

        private static System.Collections.Generic.HashSet<string> LiveAddresses()
        {
            lock (Sync)
            {
                var now = DateTime.UtcNow;
                if (now - LiveAddressesStamp < LiveAddressesTtl) return LiveAddressesCache;
                var set = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (nic.OperationalStatus != OperationalStatus.Up) continue;
                        foreach (var info in Useful(nic)) set.Add(info.Address.ToString());
                    }
                }
                catch (Exception ex) { SimpleLogger.Log(ex); }
                LiveAddressesCache = set;
                LiveAddressesStamp = now;
                return set;
            }
        }
        public static string SourceFor(AdapterBinding binding, EndpointFamily family)
        {
            if (binding == null || !binding.HasAny) return "";
            switch (family)
            {
                case EndpointFamily.Ipv6: return binding.Ipv6;
                case EndpointFamily.Ipv4: return binding.Ipv4;
                default: return binding.Ipv4.Length > 0 ? binding.Ipv4 : "";
            }
        }

        public static void ApplyXrayBinding(JObject outbound, AdapterBinding binding)
        {
            if (outbound == null || binding == null || !binding.HasAny) return;
            if (XrayLinkParser.IsLocalOutbound(outbound)) return;
            string source = SourceFor(binding, EndpointFamilyOf(outbound));
            if (source.Length > 0) outbound["sendThrough"] = source;
        }

        public static void ApplySingboxBinding(JObject target, AdapterBinding binding)
        {
            if (target == null || binding == null || !binding.HasAny) return;
            string name = (binding.Name ?? "").Trim();
            if (name.Length > 0 && !string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase))
                target["bind_interface"] = name;
            if (binding.HasV4) target["inet4_bind_address"] = binding.Ipv4;
            else target.Remove("inet4_bind_address");
            if (binding.HasV6) target["inet6_bind_address"] = binding.Ipv6;
            else target.Remove("inet6_bind_address");
        }

        public static bool TunnelUsesIpv6(TunnelParseResult tunnel)
            => tunnel != null && tunnel.Endpoint != null && EndpointFamilyOf(tunnel.Endpoint) == EndpointFamily.Ipv6;

        private static List<UnicastIPAddressInformation> Useful(NetworkInterface nic)
        {
            var list = new List<UnicastIPAddressInformation>();
            try
            {
                foreach (var info in nic.GetIPProperties().UnicastAddresses)
                {
                    var address = info.Address;
                    if (address == null) continue;
                    if (IPAddress.IsLoopback(address)) continue;
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        byte[] bytes = address.GetAddressBytes();
                        if (bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254) continue;
                        if (address.Equals(IPAddress.Any)) continue;
                        list.Add(info);
                    }
                    else if (address.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal) continue;
                        if (info.AddressPreferredLifetime <= 0) continue;
                        list.Add(info);
                    }
                }
            }
            catch (Exception ex) { SimpleLogger.Log(ex); }
            return list;
        }

        private static AdapterBinding BuildBinding(NetworkInterface nic, string name, IPAddress preferred)
        {
            string v4 = "";
            string v6 = "";
            if (nic != null)
            {
                var infos = Useful(nic);
                if (preferred != null && preferred.AddressFamily == AddressFamily.InterNetwork) v4 = preferred.ToString();
                if (v4.Length == 0)
                    v4 = infos.Where(i => i.Address.AddressFamily == AddressFamily.InterNetwork)
                              .Select(i => i.Address.ToString()).FirstOrDefault() ?? "";
                if (preferred != null && preferred.AddressFamily == AddressFamily.InterNetworkV6) v6 = preferred.ToString();
                if (v6.Length == 0)
                    v6 = infos.Where(i => i.Address.AddressFamily == AddressFamily.InterNetworkV6)
                              .OrderBy(i => i.SuffixOrigin == SuffixOrigin.Random ? 1 : 0)
                              .Select(i => i.Address.ToString()).FirstOrDefault() ?? "";
                if (name.Length == 0) name = nic.Name ?? "";
            }
            else if (preferred != null)
            {
                if (preferred.AddressFamily == AddressFamily.InterNetwork) v4 = preferred.ToString();
                else v6 = preferred.ToString();
            }
            return new AdapterBinding { Name = name, Ipv4 = v4, Ipv6 = v6 };
        }

        public static string StripBrackets(string value)
        {
            string text = (value ?? "").Trim();
            if (text.StartsWith("[", StringComparison.Ordinal))
            {
                int close = text.IndexOf(']');
                return close > 0 ? text.Substring(1, close - 1) : text.TrimStart('[');
            }
            return text;
        }

        private static string CleanHost(string value)
        {
            string text = StripBrackets(value);
            if (text.Length == 0) return "";
            if (IPAddress.TryParse(text, out _)) return text;
            int colon = text.LastIndexOf(':');
            if (colon > 0 && int.TryParse(text.Substring(colon + 1), out _)) return text.Substring(0, colon);
            return text;
        }

        private static string CleanEndpoint(string value)
        {
            string text = (value ?? "").Trim();
            if (text.StartsWith("[", StringComparison.Ordinal))
            {
                int close = text.IndexOf(']');
                return close > 0 ? text.Substring(1, close - 1) : text.TrimStart('[');
            }
            if (text.Length == 0) return "";
            int colon = text.LastIndexOf(':');
            if (colon > 0 && int.TryParse(text.Substring(colon + 1), out _)) return text.Substring(0, colon);
            return text;
        }

        public static string ServerHost(JObject outbound)
        {
            if (outbound == null) return "";
            var settings = outbound["settings"] as JObject;
            if (settings != null)
            {
                if (settings["vnext"] is JArray vnext && vnext.Count > 0 && vnext[0] is JObject v)
                    return CleanHost(v["address"] == null ? "" : v["address"].ToString());
                if (settings["servers"] is JArray servers && servers.Count > 0 && servers[0] is JObject s)
                    return CleanHost(s["address"] == null ? "" : s["address"].ToString());
                if (settings["peers"] is JArray peers && peers.Count > 0 && peers[0] is JObject p)
                {
                    string direct = p["address"] == null ? "" : p["address"].ToString();
                    if (direct.Length > 0) return CleanHost(direct);
                    return CleanEndpoint(p["endpoint"] == null ? "" : p["endpoint"].ToString());
                }
                string settingsHost = CleanHost(settings["address"] == null ? "" : settings["address"].ToString());
                if (settingsHost.Length == 0) settingsHost = CleanHost(settings["server"] == null ? "" : settings["server"].ToString());
                if (settingsHost.Length > 0) return settingsHost;
            }
            if (outbound["peers"] is JArray sbPeers && sbPeers.Count > 0 && sbPeers[0] is JObject sp)
            {
                string direct = sp["address"] == null ? "" : sp["address"].ToString();
                if (direct.Length > 0) return CleanHost(direct);
                return CleanEndpoint(sp["server"] == null ? "" : sp["server"].ToString());
            }
            if (outbound["servers"] is JArray topServers && topServers.Count > 0 && topServers[0] is JObject ts)
            {
                string address = ts["server"] == null ? "" : ts["server"].ToString();
                if (address.Length == 0) address = ts["address"] == null ? "" : ts["address"].ToString();
                return CleanHost(address);
            }
            string fallback = outbound["server"] == null ? "" : outbound["server"].ToString();
            if (fallback.Length == 0) fallback = outbound["address"] == null ? "" : outbound["address"].ToString();
            return CleanHost(fallback);
        }

        public static EndpointFamily FamilyOf(string host)
        {
            string text = StripBrackets(host);
            if (text.Length == 0) return EndpointFamily.Unknown;
            if (IPAddress.TryParse(text, out var address))
                return address.AddressFamily == AddressFamily.InterNetworkV6 ? EndpointFamily.Ipv6 : EndpointFamily.Ipv4;
            return EndpointFamily.Unknown;
        }

        public static EndpointFamily ResolveDomain(string host)
        {
            string name = CleanHost(host);
            if (name.Length == 0) return EndpointFamily.Unknown;
            var known = FamilyOf(name);
            if (known != EndpointFamily.Unknown) return known;
            var resolver = DomainResolver;
            if (resolver != null) return resolver(name);
            lock (Sync)
            {
                if (Domains.TryGetValue(name, out var hit) && DateTime.UtcNow - hit.Stamp < DomainTtl) return hit.Family;
            }
            EndpointFamily family = EndpointFamily.Unknown;
            try
            {
                var task = Dns.GetHostAddressesAsync(name);
                if (!task.Wait(DomainBudgetMs))
                {
                    _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    family = EndpointFamily.Unknown;
                }
                else
                {
                    var addresses = task.Result ?? new IPAddress[0];
                    bool v4 = addresses.Any(a => a.AddressFamily == AddressFamily.InterNetwork);
                    bool v6 = addresses.Any(a => a.AddressFamily == AddressFamily.InterNetworkV6);
                    family = v4 ? EndpointFamily.Ipv4 : v6 ? EndpointFamily.Ipv6 : EndpointFamily.Unknown;
                }
            }
            catch (Exception ex) { SimpleLogger.LogOnce($"adapter-dns|{name}", $"[Adapter] Host lookup failed for {name}: {ex.Message}"); family = EndpointFamily.Unknown; }
            lock (Sync) { Domains[name] = new CachedFamily { Family = family, Stamp = DateTime.UtcNow }; }
            return family;
        }

        public static EndpointFamily EndpointFamilyOf(JObject outbound)
        {
            string host = ServerHost(outbound);
            var family = FamilyOf(host);
            if (family != EndpointFamily.Unknown || host.Length == 0) return family;
            return ResolveDomain(host);
        }
        public static EndpointScanResult ScanEndpoints(Models.AppConfig config)
        {
            var scan = new EndpointScanResult();
            if (config == null) return scan;
            try
            {
                foreach (string raw in XrayPipelineManager.ActiveOutbounds)
                    ScanRaw(raw, scan);
            }
            catch (Exception ex) { SimpleLogger.Log(ex); }
            ScanRaw(config.CustomConfig1, scan);
            ScanRaw(config.CustomConfig2, scan);
            ScanRaw(config.V2rayChainJson, scan);
            if (MainWindow.Instance != null)
            {
                try
                {
                    foreach (var target in AppRulesSingboxBuilder.CollectTunnelTargets(config))
                        ScanEndpointObject(target == null || target.Parsed == null ? null : target.Parsed.Endpoint, scan);
                }
                catch (Exception ex) { SimpleLogger.Log(ex); }
            }
            return scan;
        }

        private static void ScanRaw(string raw, EndpointScanResult scan)
        {
            string text = (raw ?? "").Trim();
            if (text.Length == 0) return;
            try
            {
                if (TunnelConfigParser.TryParse(text, out var tunnel) && tunnel != null && tunnel.Success)
                {
                    ScanEndpointObject(tunnel.Endpoint, scan);
                    return;
                }
                if (ConfigConverter.TryXrayChain(text, out var hops, out _, out _) && hops != null && hops.Count > 0)
                {
                    foreach (var hop in hops) ScanEndpointObject(hop, scan);
                    return;
                }
                if (XrayLinkParser.TryParseCustomConfig(text, out string document))
                {
                    ScanJObject(JObject.Parse(document), scan);
                    return;
                }
                if (SingboxLinkParser.TryParseLink(text, out string outbound, out _))
                {
                    ScanJObject(JObject.Parse(outbound), scan);
                }
            }
            catch (Exception ex) { SimpleLogger.Log(ex); }
        }

        private static void ScanJObject(JToken token, EndpointScanResult scan)
        {
            if (token is JArray array)
            {
                foreach (var item in array) ScanJObject(item, scan);
                return;
            }
            if (token is not JObject obj) return;
            if (obj["outbounds"] is JArray outbounds)
            {
                foreach (var item in outbounds) ScanEndpointObject(item as JObject, scan);
                return;
            }
            ScanEndpointObject(obj, scan);
        }

        private static void ScanEndpointObject(JObject endpoint, EndpointScanResult scan)
        {
            if (endpoint == null) return;
            if (XrayLinkParser.IsLocalOutbound(endpoint)) return;
            string host = ServerHost(endpoint);
            if (host.Length == 0) return;
            var family = FamilyOf(host);
            if (family == EndpointFamily.Ipv6)
            {
                scan.UsesIpv6 = true;
                return;
            }
            if (family != EndpointFamily.Unknown) return;
            if (ResolveDomain(host) == EndpointFamily.Ipv6)
            {
                scan.UsesIpv6 = true;
                scan.NeedsDomainIpv6 = true;
            }
        }
    }
}

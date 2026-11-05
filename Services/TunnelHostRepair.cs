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
using System.Net.Sockets;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public static class TunnelHostRepair
    {
        internal static Func<string, IReadOnlyList<string>> SystemResolver = DefaultSystem;

        internal static Func<string, IReadOnlyList<string>> DohResolver = BootstrapDns.Resolve;

        public static void Apply(JObject endpoint)
        {
            if (endpoint == null) return;
            string type = endpoint["type"]?.ToString() ?? "";
            if (string.Equals(type, "openvpn-client", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "openvpn", StringComparison.OrdinalIgnoreCase))
            {
                RepairOpenVpn(endpoint);
                return;
            }
            if (string.Equals(type, "wireguard", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "awg", StringComparison.OrdinalIgnoreCase))
            {
                RepairPeers(endpoint);
            }
        }

        private static void RepairOpenVpn(JObject endpoint)
        {
            if (string.Equals(endpoint["mode"]?.ToString(), "static_key", StringComparison.OrdinalIgnoreCase)) return;
            if (endpoint["servers"] is not JArray servers || servers.Count == 0) return;
            var rebuilt = new JArray();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var server in servers.OfType<JObject>())
            {
                string host = server["server"]?.ToString() ?? "";
                IReadOnlyList<string> addresses = Array.Empty<string>();
                string localAnswer = "";
                if (host.Length > 0 && !IPAddress.TryParse(host, out _))
                    addresses = DohAddresses(host, out localAnswer);
                if (addresses.Count == 0)
                {
                    AddServer(rebuilt, seen, server, null);
                    continue;
                }
                SimpleLogger.Log($"[DoH] {host} answers {localAnswer} locally; the tunnel will try {string.Join(", ", addresses)}");
                KeepServerName(endpoint, host);
                foreach (string address in addresses) AddServer(rebuilt, seen, server, address);
            }
            endpoint["servers"] = rebuilt;
        }

        private static void RepairPeers(JObject endpoint)
        {
            if (endpoint["peers"] is not JArray peers) return;
            foreach (var peer in peers.OfType<JObject>())
            {
                string host = peer["address"]?.ToString() ?? "";
                if (host.Length == 0 || IPAddress.TryParse(host, out _)) continue;
                var addresses = DohAddresses(host, out string localAnswer);
                if (addresses.Count == 0) continue;
                peer["address"] = addresses[0];
                KeepServerName(endpoint, host);
                SimpleLogger.Log($"[DoH] {host} answers {localAnswer} locally; dialing {addresses[0]} instead");
            }
        }

        private static void AddServer(JArray target, HashSet<string> seen, JObject source, string? address)
        {
            string server = address ?? source["server"]?.ToString() ?? "";
            if (server.Length == 0) return;
            string network = source["network"]?.ToString() ?? "";
            string key = server + "|" + source["server_port"]?.ToString() + "|" + network;
            if (!seen.Add(key)) return;
            var entry = (JObject)source.DeepClone();
            entry["server"] = server;
            target.Add(entry);
        }

        private static void KeepServerName(JObject endpoint, string host)
        {
            if (endpoint["tls"] is JObject tls && (tls["server_name"]?.ToString() ?? "").Length == 0)
                tls["server_name"] = host;
        }

        private static IReadOnlyList<string> DohAddresses(string host, out string localAnswer)
        {
            localAnswer = "";
            var system = SafeResolve(SystemResolver, host);
            if (system.Count == 0) return Array.Empty<string>();
            if (system.Any(address => !BootstrapDns.IsPrivateAddress(address))) return Array.Empty<string>();
            localAnswer = system[0];
            var doh = SafeResolve(DohResolver, host);
            if (doh.Count == 0) return Array.Empty<string>();
            return doh.Where(address => !string.IsNullOrWhiteSpace(address))
                      .Select(address => address.Trim())
                      .Distinct(StringComparer.Ordinal)
                      .ToArray();
        }

        private static IReadOnlyList<string> SafeResolve(Func<string, IReadOnlyList<string>> resolver, string host)
        {
            try { return resolver(host) ?? Array.Empty<string>(); }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                return Array.Empty<string>();
            }
        }

        private static IReadOnlyList<string> DefaultSystem(string host)
        {
            try
            {
                return Dns.GetHostAddresses(host)
                    .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => address.ToString())
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}

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
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public static class TunnelTcpProbe
    {
        public const int DefaultTimeoutMs = 1500;

        private const int MaxRemotes = 8;

        private const int IpUnicastIf = 31;

        public sealed class TResult
        {
            public bool Ok { get; set; }

            public long Ms { get; set; } = -1;

            public string Target { get; set; } = "";

            public bool TimedOut { get; set; }

            public string Error { get; set; } = "";
        }

        private sealed class Candidate
        {
            public IPAddress Address { get; set; }

            public string Host { get; set; } = "";

            public int Port { get; set; }

            public string Network { get; set; } = "";

            public string Write() => $"{Host}:{Port}";
        }

        private sealed class Route
        {
            public IPAddress Source { get; set; }

            public int InterfaceIndex { get; set; }

            public string Name { get; set; } = "";
        }

        public static bool IsOpenVpn(TunnelParseResult tunnel)
            => tunnel != null && tunnel.Success && tunnel.Kind == TunnelKind.OpenVpn;

        public static bool IsWireGuard(TunnelParseResult tunnel)
            => tunnel != null && tunnel.Success && tunnel.Kind == TunnelKind.WireGuard;

        public static string ServerText(TunnelParseResult tunnel)
        {
            try { return tunnel?.ServerText() ?? ""; }
            catch { return ""; }
        }

        public static TResult Probe(TunnelParseResult tunnel, string adapterIp, int attemptTimeoutMs, int totalBudgetMs = 5000)
        {
            var result = new TResult();
            var remotes = Remotes(tunnel);
            if (remotes.Count == 0)
            {
                result.Error = "no remote server";
                return result;
            }
            int timeout = attemptTimeoutMs > 0 ? attemptTimeoutMs : DefaultTimeoutMs;
            int budget = totalBudgetMs > 0 ? totalBudgetMs : 5000;
            var route = ResolveRoute(adapterIp);
            var candidates = new List<Candidate>();
            foreach (var remote in remotes.Take(MaxRemotes))
            {
                foreach (var address in Resolve(remote.Item1))
                {
                    candidates.Add(new Candidate
                    {
                        Address = address,
                        Host = remote.Item1,
                        Port = remote.Item2,
                        Network = remote.Item3
                    });
                }
            }
            if (candidates.Count == 0)
            {
                result.Error = "the remote host could not be resolved";
                return result;
            }
            candidates = candidates
                .OrderBy(c => string.Equals(c.Network, "tcp", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ToList();
            var gate = new object();
            var finished = new ManualResetEventSlim(false);
            string failure = "";
            bool sawTimeout = false;
            int pending = candidates.Count;
            foreach (var candidate in candidates)
            {
                var item = candidate;
                Task.Run(() =>
                {
                    try
                    {
                        if (TryConnect(item, route, timeout, out long ms, out bool timedOut, out string error))
                        {
                            lock (gate)
                            {
                                if (!result.Ok || ms < result.Ms)
                                {
                                    result.Ok = true;
                                    result.Ms = ms;
                                    result.Target = item.Write();
                                }
                            }
                            finished.Set();
                            return;
                        }
                        lock (gate)
                        {
                            if (timedOut) sawTimeout = true;
                            if (failure.Length == 0) failure = error;
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (gate) { if (failure.Length == 0) failure = ex.Message; }
                    }
                    finally
                    {
                        if (Interlocked.Decrement(ref pending) == 0) finished.Set();
                    }
                });
            }
            finished.Wait(budget + timeout + 500);
            if (!result.Ok)
            {
                result.TimedOut = sawTimeout;
                result.Error = failure.Length > 0 ? failure : "no remote answered";
            }
            return result;
        }

        public static string RouteText(string adapterIp)
        {
            var route = ResolveRoute(adapterIp);
            if (route == null) return "default route";
            return route.Name.Length > 0
                ? $"{route.Name} ({route.Source}, ifIndex {route.InterfaceIndex})"
                : route.Source.ToString();
        }

        private static List<Tuple<string, int, string>> Remotes(TunnelParseResult tunnel)
        {
            var list = new List<Tuple<string, int, string>>();
            try
            {
                if (tunnel?.Endpoint?["servers"] is JArray servers)
                {
                    foreach (var item in servers.OfType<JObject>())
                    {
                        string host = item["server"]?.ToString() ?? "";
                        int port = 0;
                        int.TryParse(item["server_port"]?.ToString() ?? "", out port);
                        string network = item["network"]?.ToString() ?? "";
                        if (host.Length == 0 || port <= 0 || port > 65535) continue;
                        if (list.Any(r => r.Item1 == host && r.Item2 == port)) continue;
                        list.Add(Tuple.Create(host, port, network));
                    }
                }
                if (list.Count == 0)
                {
                    string host = tunnel?.Endpoint?["server"]?.ToString() ?? "";
                    int port = 0;
                    int.TryParse(tunnel?.Endpoint?["server_port"]?.ToString() ?? "", out port);
                    if (host.Length > 0 && port > 0 && port <= 65535) list.Add(Tuple.Create(host, port, ""));
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
            return list;
        }

        private static Route ResolveRoute(string adapterIp)
        {
            try
            {
                var nics = NetworkInterface.GetAllNetworkInterfaces();
                if (IPAddress.TryParse((adapterIp ?? "").Trim(), out var wanted) && wanted.AddressFamily == AddressFamily.InterNetwork)
                {
                    foreach (var nic in nics)
                    {
                        if (!nic.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(wanted))) continue;
                        return new Route { Source = wanted, InterfaceIndex = Index(nic), Name = nic.Name };
                    }
                }
                foreach (var nic in nics)
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    if (IsVirtual(nic)) continue;
                    var address = nic.GetIPProperties().UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
                    if (address == null) continue;
                    return new Route { Source = address, InterfaceIndex = Index(nic), Name = nic.Name };
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
            return null;
        }

        private static bool IsVirtual(NetworkInterface nic)
        {
            string text = (nic.Name ?? "") + " " + (nic.Description ?? "");
            string[] blocked =
            {
                "singbox", "sing-box", "tun", "wintun", "tap", "vpn", "wireguard", "virtual",
                "vmware", "virtualbox", "hyper-v", "hyperv", "loopback", "bluetooth", "warp"
            };
            foreach (var word in blocked)
            {
                if (text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static int Index(NetworkInterface nic)
        {
            try { return nic.GetIPProperties().GetIPv4Properties()?.Index ?? 0; }
            catch { return 0; }
        }

        private static List<IPAddress> Resolve(string host)
        {
            var list = new List<IPAddress>();
            try
            {
                if (IPAddress.TryParse(host, out var literal))
                {
                    list.Add(literal);
                    return list;
                }
                var found = Dns.GetHostAddresses(host) ?? new IPAddress[0];
                list.AddRange(found.Where(a => a.AddressFamily == AddressFamily.InterNetwork));
                list.AddRange(found.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6));
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
            return list;
        }

        private static bool TryConnect(Candidate candidate, Route route, int timeoutMs, out long ms, out bool timedOut, out string error)
        {
            if (TryConnectOnce(candidate, route, timeoutMs, out ms, out timedOut, out error)) return true;
            string pinnedError = error;
            bool pinnedTimeout = timedOut;
            if (route == null) return false;
            if (TryConnectOnce(candidate, null, timeoutMs, out ms, out timedOut, out error)) return true;
            error = $"{error} (pinned to {route.Name}: {pinnedError}{(pinnedTimeout ? ", timed out" : "")})";
            if (pinnedTimeout) timedOut = true;
            return false;
        }

        private static string RouteName(Route route)
            => route == null ? "the default route" : $"{route.Name} ({route.Source}, ifIndex {route.InterfaceIndex})";

        private static bool TryConnectOnce(Candidate candidate, Route route, int timeoutMs, out long ms, out bool timedOut, out string error)
        {
            ms = -1;
            timedOut = false;
            error = "";
            TcpClient client = null;
            try
            {
                client = new TcpClient(candidate.Address.AddressFamily);
                if (route != null)
                {
                    try { client.Client.Bind(new IPEndPoint(route.Source, 0)); }
                    catch (Exception ex) { error = "bind to " + route.Source + " failed: " + ex.Message; }
                    if (route.InterfaceIndex > 0)
                    {
                        try { client.Client.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)IpUnicastIf, route.InterfaceIndex); }
                        catch (Exception ex) { error = "pinning to interface " + route.InterfaceIndex + " failed: " + ex.Message; }
                    }
                }
                var watch = Stopwatch.StartNew();
                var connect = client.ConnectAsync(candidate.Address, candidate.Port);
                if (!connect.Wait(timeoutMs))
                {
                    timedOut = true;
                    error = $"{candidate.Write()} did not answer within {timeoutMs} ms";
                    return false;
                }
                if (connect.IsFaulted)
                {
                    var inner = connect.Exception?.InnerException;
                    error = $"{candidate.Write()} refused: {inner?.Message ?? "connect failed"}";
                    return false;
                }
                watch.Stop();
                ms = watch.ElapsedMilliseconds;
                return true;
            }
            catch (Exception ex)
            {
                error = $"{candidate.Write()} refused: {ex.Message}";
                return false;
            }
            finally
            {
                try { client?.Close(); } catch { }
            }
        }
    }
}

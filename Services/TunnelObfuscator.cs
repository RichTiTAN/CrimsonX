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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CrimsonX.Models;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public sealed class TunnelObfuscator : IDisposable
    {
        private const int DecoyCount = 6;
        private const int DecoyMinBytes = 64;
        private const int DecoyMaxBytes = 768;
        private const int BufferSize = 1600;

        private readonly Socket _local;
        private readonly Socket _upstream;
        private readonly TunnelKind _kind;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private volatile EndPoint _client;
        private bool _seenClient;
        private int _decoys;
        private bool _disposed;

        public int Port { get; }

        public string Target { get; }

        public string Bound { get; }

        public int Decoys => Volatile.Read(ref _decoys);

        private TunnelObfuscator(Socket local, Socket upstream, TunnelKind kind, string target, string bound)
        {
            _local = local;
            _upstream = upstream;
            _kind = kind;
            Target = target;
            Bound = bound;
            Port = ((IPEndPoint)local.LocalEndPoint).Port;
            Task.Run(() => PumpClientAsync(_cts.Token));
            Task.Run(() => PumpServerAsync(_cts.Token));
        }

        public static TunnelObfuscator TryStart(TunnelKind kind, string host, int port, AdapterBinding binding, out string error)
        {
            error = "";
            if (port <= 0 || port > 65535)
            {
                error = "the port is not usable";
                return null;
            }
            IPAddress address = ResolveHost(host, binding);
            if (address == null)
            {
                error = $"'{host}' could not be resolved";
                return null;
            }
            var family = address.AddressFamily == AddressFamily.InterNetworkV6 ? EndpointFamily.Ipv6 : EndpointFamily.Ipv4;
            string source = AdapterBindings.SourceFor(binding, family);
            string adapter = (binding?.Name ?? "").Trim();
            bool pinned = adapter.Length > 0 && !string.Equals(adapter, "Default", StringComparison.OrdinalIgnoreCase);
            if (pinned && source.Length == 0)
            {
                error = $"the adapter '{adapter}' has no {(family == EndpointFamily.Ipv6 ? "IPv6" : "IPv4")} address for this server";
                return null;
            }
            Socket upstream = null;
            Socket local = null;
            try
            {
                upstream = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                upstream.Bind(source.Length > 0
                    ? new IPEndPoint(IPAddress.Parse(source), 0)
                    : new IPEndPoint(address.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0));
                upstream.Connect(new IPEndPoint(address, port));
                local = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                local.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                string bound = source.Length > 0 ? (pinned ? $"{source} ({adapter})" : source) : "the default route";
                var relay = new TunnelObfuscator(local, upstream, kind, $"{host}:{port}", bound);
                local = null;
                upstream = null;
                return relay;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
            finally
            {
                try { local?.Dispose(); } catch { }
                try { upstream?.Dispose(); } catch { }
            }
        }

        public static bool Apply(AppConfig cfg, TunnelTarget target, JObject endpoint, List<TunnelObfuscator> relays, out string error)
        {
            error = "";
            if (cfg == null || endpoint == null || relays == null) return false;
            if (!cfg.EnableCustomConfigObfuscation) return false;
            if (endpoint["detour"] != null) return false;
            if (AwgConfig.IsAwgEndpoint(endpoint)) return false;
            string type = endpoint["type"]?.ToString() ?? "";
            var binding = AdapterBindings.Resolve(target?.AdapterName ?? "", target?.AdapterIp ?? "");
            var created = new List<TunnelObfuscator>();
            bool applied;
            if (type.Equals("wireguard", StringComparison.OrdinalIgnoreCase))
                applied = ApplyWireGuard(endpoint, binding, created, out error);
            else if (type.Equals("openvpn-client", StringComparison.OrdinalIgnoreCase))
                applied = ApplyOpenVpn(endpoint, binding, created, out error);
            else
                return false;
            if (!applied)
            {
                DisposeAll(created);
                return false;
            }
            endpoint.Remove("bind_interface");
            endpoint.Remove("inet4_bind_address");
            endpoint.Remove("inet6_bind_address");
            relays.AddRange(created);
            SimpleLogger.Log($"[Obfuscation] {target?.Parsed?.KindText} {string.Join(", ", created.Select(relay => relay.Target))} " +
                             $"→ {created.Count} relay(s) on 127.0.0.1:{created[0].Port} via {created[0].Bound}");
            return true;
        }

        private static bool ApplyWireGuard(JObject endpoint, AdapterBinding binding, List<TunnelObfuscator> created, out string error)
        {
            error = "";
            if (endpoint["peers"] is not JArray peers || peers.Count == 0)
            {
                error = "the config has no WireGuard peer";
                return false;
            }
            var planned = new List<(JObject Peer, string Host, int Port)>();
            foreach (var peer in peers.OfType<JObject>())
            {
                string host = peer["address"]?.ToString() ?? "";
                if (host.Length == 0)
                {
                    error = "a WireGuard peer has no address";
                    return false;
                }
                planned.Add((peer, host, ReadPort(peer["port"], 51820)));
            }
            foreach (var item in planned)
            {
                var relay = TryStart(TunnelKind.WireGuard, item.Host, item.Port, binding, out error);
                if (relay == null) return false;
                created.Add(relay);
            }
            for (int i = 0; i < planned.Count; i++)
            {
                planned[i].Peer["address"] = "127.0.0.1";
                planned[i].Peer["port"] = created[i].Port;
            }
            return true;
        }

        private static bool ApplyOpenVpn(JObject endpoint, AdapterBinding binding, List<TunnelObfuscator> created, out string error)
        {
            error = "";
            if (IsTcp(endpoint["network"])) return false;
            bool nested = endpoint["servers"] is JArray servers && servers.Count > 0;
            var planned = new List<(JObject Server, string Host, int Port)>();
            if (nested)
            {
                foreach (var server in ((JArray)endpoint["servers"]).OfType<JObject>())
                {
                    if (IsTcp(server["network"])) return false;
                    string host = server["server"]?.ToString() ?? "";
                    if (host.Length == 0)
                    {
                        error = "an OpenVPN server has no host";
                        return false;
                    }
                    planned.Add((server, host, ReadPort(server["server_port"], 1194)));
                }
            }
            else
            {
                string host = endpoint["server"]?.ToString() ?? "";
                if (host.Length == 0) return false;
                planned.Add((endpoint, host, ReadPort(endpoint["server_port"], 1194)));
            }
            foreach (var item in planned)
            {
                var relay = TryStart(TunnelKind.OpenVpn, item.Host, item.Port, binding, out error);
                if (relay == null) return false;
                created.Add(relay);
            }
            for (int i = 0; i < planned.Count; i++)
            {
                var owner = planned[i].Server;
                owner["server"] = "127.0.0.1";
                owner["server_port"] = created[i].Port;
            }
            return true;
        }

        private static void DisposeAll(List<TunnelObfuscator> relays)
        {
            foreach (var relay in relays)
            {
                try { relay.Dispose(); } catch { }
            }
            relays.Clear();
        }

        private static bool IsTcp(JToken token)
            => string.Equals(token?.ToString() ?? "", "tcp", StringComparison.OrdinalIgnoreCase);

        private static int ReadPort(JToken token, int fallback)
        {
            if (token == null) return fallback;
            return int.TryParse(token.ToString(), out int port) && port > 0 && port <= 65535 ? port : fallback;
        }

        private static IPAddress ResolveHost(string host, AdapterBinding binding)
        {
            if (string.IsNullOrWhiteSpace(host)) return null;
            if (IPAddress.TryParse(host.Trim().Trim('[', ']'), out var literal)) return literal;
            IPAddress[] addresses;
            try { addresses = Dns.GetHostAddresses(host.Trim()); }
            catch { return null; }
            bool preferIpv6 = binding != null && binding.HasV6 && !binding.HasV4;
            IPAddress fallback = null;
            foreach (var address in addresses)
            {
                if (address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    if (preferIpv6) return address;
                    fallback ??= address;
                }
                else if (address.AddressFamily == AddressFamily.InterNetwork)
                {
                    if (!preferIpv6) return address;
                    fallback ??= address;
                }
            }
            return fallback;
        }

        private async Task PumpClientAsync(CancellationToken ct)
        {
            var buffer = new byte[BufferSize];
            var decoy = new byte[DecoyMaxBytes];
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            while (!ct.IsCancellationRequested)
            {
                SocketReceiveFromResult result;
                try { result = await _local.ReceiveFromAsync(buffer.AsMemory(), SocketFlags.None, from, ct).ConfigureAwait(false); }
                catch { return; }
                if (result.ReceivedBytes <= 0) continue;
                _client = result.RemoteEndPoint;
                var data = buffer.AsMemory(0, result.ReceivedBytes);
                bool first = !_seenClient;
                _seenClient = true;
                if (first || IsHandshake(data.Span)) SendDecoys(decoy);
                try { await _upstream.SendAsync(data, SocketFlags.None, ct).ConfigureAwait(false); } catch { }
            }
        }

        private async Task PumpServerAsync(CancellationToken ct)
        {
            var buffer = new byte[BufferSize];
            while (!ct.IsCancellationRequested)
            {
                int read;
                try { read = await _upstream.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, ct).ConfigureAwait(false); }
                catch { return; }
                if (read <= 0) continue;
                var client = _client;
                if (client == null) continue;
                try { await _local.SendToAsync(buffer.AsMemory(0, read), SocketFlags.None, client, ct).ConfigureAwait(false); } catch { }
            }
        }

        private void SendDecoys(byte[] decoy)
        {
            for (int i = 0; i < DecoyCount; i++)
            {
                int size = Random.Shared.Next(DecoyMinBytes, DecoyMaxBytes + 1);
                RandomNumberGenerator.Fill(decoy.AsSpan(0, size));
                try { _upstream.Send(decoy, 0, size, SocketFlags.None); }
                catch { return; }
                Interlocked.Increment(ref _decoys);
            }
        }

        private bool IsHandshake(ReadOnlySpan<byte> data)
        {
            if (_kind == TunnelKind.WireGuard)
                return data.Length == 148 && data[0] == 0x01 && data[1] == 0x00 && data[2] == 0x00 && data[3] == 0x00;
            if (_kind == TunnelKind.OpenVpn)
                return data.Length >= 2 && ((data[0] & 0x1F) == 7 || (data[0] & 0x1F) == 10);
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _cts.Cancel(); } catch { }
            try { _local.Dispose(); } catch { }
            try { _upstream.Dispose(); } catch { }
            try { _cts.Dispose(); } catch { }
        }
    }
}

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
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public static class BootstrapDns
    {
        private sealed class Entry
        {
            public IReadOnlyList<string> Addresses;

            public DateTime ExpiresUtc;
        }

        private sealed class Endpoint
        {
            public string Url = "";

            public string Host = "";
        }

        private static readonly Endpoint[] Endpoints =
        {
            new Endpoint { Url = "https://1.1.1.1/dns-query" },
            new Endpoint { Url = "https://1.0.0.1/dns-query" },
            new Endpoint { Url = "https://8.8.8.8/resolve", Host = "dns.google" },
            new Endpoint { Url = "https://8.8.4.4/resolve", Host = "dns.google" },
            new Endpoint { Url = "https://9.9.9.9/dns-query", Host = "dns.quad9.net" },
            new Endpoint { Url = "https://94.140.14.14/dns-query", Host = "dns.adguard-dns.com" },
            new Endpoint { Url = "https://208.67.222.222/dns-query", Host = "doh.opendns.com" }
        };

        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

        private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(3);

        private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

        private static readonly Dictionary<string, Entry> Cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, DateTime> FailedUntil = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private static readonly object Sync = new object();

        private static HttpClient _client;

        private static readonly Dictionary<string, HttpClient> BoundClients = new Dictionary<string, HttpClient>(StringComparer.OrdinalIgnoreCase);

        internal static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Sender { get; set; }

        public static IReadOnlyList<string> Resolve(string host) => Resolve(host, "");

        public static IReadOnlyList<string> Resolve(string host, string sourceAddress)
        {
            if (string.IsNullOrWhiteSpace(host)) return Array.Empty<string>();
            host = host.Trim();
            if (IPAddress.TryParse(host, out _)) return new[] { host };
            string source = (sourceAddress ?? "").Trim();
            string key = source.Length > 0 ? host + "@" + source : host;
            lock (Sync)
            {
                if (Cache.TryGetValue(key, out var cached) && cached.ExpiresUtc > DateTime.UtcNow) return cached.Addresses;
                if (FailedUntil.TryGetValue(key, out var failedUntil) && failedUntil > DateTime.UtcNow) return Array.Empty<string>();
            }
            var addresses = ResolveCore(host, source, key);
            if (addresses.Count == 0) return addresses;
            lock (Sync)
            {
                Cache[key] = new Entry { Addresses = addresses, ExpiresUtc = DateTime.UtcNow + Ttl };
                FailedUntil.Remove(key);
            }
            return addresses;
        }

        internal static void Reset()
        {
            lock (Sync)
            {
                Cache.Clear();
                FailedUntil.Clear();
            }
        }

        private static IReadOnlyList<string> ResolveCore(string host, string sourceAddress, string key)
        {
            var client = ClientFor(sourceAddress);
            foreach (var endpoint in Endpoints)
            {
                try
                {
                    var found = QueryEndpoint(host, endpoint, client);
                    if (found.Count > 0)
                    {
                        SimpleLogger.Log($"[DoH] {host} -> {string.Join(", ", found)} via {new Uri(endpoint.Url).Host}");
                        return found;
                    }
                }
                catch (Exception ex)
                {
                    SimpleLogger.Log($"[DoH] {endpoint.Url} could not answer for {host}: {ex.Message}");
                }
            }
            lock (Sync) FailedUntil[key] = DateTime.UtcNow + Cooldown;
            SimpleLogger.Log($"[DoH] every resolver failed for {host}; skipping DoH for that name for {(int)Cooldown.TotalMinutes} minutes");
            return Array.Empty<string>();
        }

        private static IReadOnlyList<string> QueryEndpoint(string host, Endpoint endpoint, HttpClient client)
        {
            string url = endpoint.Url + "?name=" + Uri.EscapeDataString(host) + "&type=A";
            using var cts = new CancellationTokenSource(QueryTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("accept", "application/dns-json");
            if (endpoint.Host.Length > 0) request.Headers.TryAddWithoutValidation("host", endpoint.Host);

            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sender = Sender;
            Task<HttpResponseMessage> pending = sender != null ? sender(request, cts.Token) : client.SendAsync(request, cts.Token);
            using var response = pending.GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) return Array.Empty<string>();
            string body = response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
            return ParseAnswers(body);
        }

        private static HttpClient ClientFor(string sourceAddress)
        {
            if (sourceAddress.Length == 0) return Client;
            lock (Sync)
            {
                if (BoundClients.TryGetValue(sourceAddress, out var existing)) return existing;
            }
            IPAddress source;
            if (!IPAddress.TryParse(sourceAddress, out source)) return Client;
            var handler = new SocketsHttpHandler
            {
                ConnectTimeout = QueryTimeout,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                UseProxy = false,
                ConnectCallback = async (context, token) =>
                {
                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        socket.Bind(new IPEndPoint(source, 0));
                        await socket.ConnectAsync(context.DnsEndPoint, token).ConfigureAwait(false);
                        return new NetworkStream(socket, true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            };
            var bound = new HttpClient(handler) { Timeout = QueryTimeout };
            lock (Sync)
            {
                if (BoundClients.Count >= 4) BoundClients.Clear();
                BoundClients[sourceAddress] = bound;
            }
            return bound;
        }

        public static bool IsPrivateAddress(string value)
        {
            if (!IPAddress.TryParse((value ?? "").Trim(), out var address)) return false;
            if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return true;
            if (address.AddressFamily == AddressFamily.InterNetworkV6 && !address.IsIPv4MappedToIPv6) return false;
            byte[] bytes = address.MapToIPv4().GetAddressBytes();
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254)
                || bytes[0] == 0
                || bytes[0] >= 224;
        }

        internal static IReadOnlyList<string> ParseAnswers(string body)
        {
            var result = new List<string>();
            try
            {
                var document = JObject.Parse(body);
                if (document["Answer"] is not JArray answers) return result;
                foreach (var answer in answers.OfType<JObject>())
                {
                    if ((int?)answer["type"] != 1) continue;
                    string data = answer["data"]?.ToString() ?? "";
                    if (!IPAddress.TryParse(data, out var address)) continue;
                    string text = address.ToString();
                    if (!result.Contains(text)) result.Add(text);
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
            return result;
        }

        private static HttpClient Client
        {
            get
            {
                lock (Sync)
                {
                    if (_client != null) return _client;
                    var handler = new SocketsHttpHandler
                    {
                        ConnectTimeout = QueryTimeout,
                        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                        UseProxy = false
                    };
                    _client = new HttpClient(handler) { Timeout = QueryTimeout };
                    return _client;
                }
            }
        }
    }
}

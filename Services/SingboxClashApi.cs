/*
  CrimsonX - A GUI VPN client that fetches, tests and load-balances multiple xray configs suited for your network.
  Copyright (C) 2026 RichTiTAN

  This program is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    internal static class SingboxClashApi
    {
        private static readonly object Sync = new();

        private static string _controller = "";
        private static string _secret = "";

        internal static string Controller
        {
            get { lock (Sync) { return _controller; } }
        }

        internal static string Secret
        {
            get { lock (Sync) { return _secret; } }
        }

        internal static bool IsConfigured
        {
            get { lock (Sync) { return _controller.Length > 0 && _secret.Length > 0; } }
        }

        internal static string EnsureController()
        {
            lock (Sync)
            {
                if (_controller.Length == 0)
                {
                    int port = PickPort();
                    _controller = "127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture);
                }
                return _controller;
            }
        }

        internal static string EnsureSecret()
        {
            lock (Sync)
            {
                if (_secret.Length == 0)
                {
                    var bytes = new byte[16];
                    System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
                    _secret = Convert.ToHexString(bytes).ToLowerInvariant();
                }
                return _secret;
            }
        }

        private static int PickPort()
        {
            for (int attempt = 0; attempt < 16; attempt++)
            {
                int candidate = Random.Shared.Next(19090, 19290);
                if (IsPortFree(candidate)) return candidate;
            }

            return 19091;
        }

        private static bool IsPortFree(int port)
        {
            TcpListener? listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return true;
            }
            catch { return false; }
            finally
            {
                try { listener?.Stop(); } catch { }
            }
        }
    }

    internal sealed class RuleProcessTraffic
    {
        internal string ProcessName { get; set; } = "";
        internal int ActiveConnections { get; set; }
        internal long DownloadBytes { get; set; }
        internal long UploadBytes { get; set; }
    }

    internal sealed class RuleConnectionsSnapshot
    {
        internal bool Available { get; set; }

        internal int ActiveConnections { get; set; }

        internal long DownloadBytes { get; set; }
        internal long UploadBytes { get; set; }

        internal long SessionDownloadBytes { get; set; }
        internal long SessionUploadBytes { get; set; }

        internal List<RuleProcessTraffic> Processes { get; set; } = new();
    }

    internal sealed class SingboxConnectionsClient : IDisposable
    {
        private readonly HttpClient _http;

        internal SingboxConnectionsClient()
        {
            _http = new HttpClient(new HttpClientHandler { UseProxy = false })
            {
                Timeout = TimeSpan.FromSeconds(2)
            };
        }

        internal async Task<RuleConnectionsSnapshot?> GetAsync(ISet<string> ruleProcessNames, CancellationToken token)
        {
            if (!SingboxClashApi.IsConfigured) return null;

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get, "http://" + SingboxClashApi.Controller + "/connections");
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + SingboxClashApi.Secret);

                using var cts    = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, token);

                using var response = await _http.SendAsync(request, linked.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                string body = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
                return Parse(body, ruleProcessNames);
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex) { SimpleLogger.Log(ex); return null; }
        }

        internal static RuleConnectionsSnapshot Parse(string json, ISet<string> ruleProcessNames)
        {
            var snapshot = new RuleConnectionsSnapshot
            {
                Available = ruleProcessNames.Count > 0
            };

            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                snapshot.Available = false;
                return snapshot;
            }

            snapshot.SessionDownloadBytes = root.Value<long?>("downloadTotal") ?? 0;
            snapshot.SessionUploadBytes   = root.Value<long?>("uploadTotal") ?? 0;

            if (root["connections"] is not JArray connections) return snapshot;

            var buckets = new Dictionary<string, RuleProcessTraffic>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in connections)
            {
                string exe = ProcessKey(item["metadata"]?["processPath"]?.ToString());
                if (exe.Length == 0) continue;
                if (!ruleProcessNames.Contains(exe)) continue;

                if (!buckets.TryGetValue(exe, out var bucket))
                {
                    bucket = new RuleProcessTraffic { ProcessName = exe };
                    buckets[exe] = bucket;
                }

                bucket.ActiveConnections++;
                bucket.DownloadBytes += item.Value<long?>("download") ?? 0;
                bucket.UploadBytes   += item.Value<long?>("upload") ?? 0;
            }

            foreach (var bucket in buckets.Values)
            {
                snapshot.ActiveConnections += bucket.ActiveConnections;
                snapshot.DownloadBytes     += bucket.DownloadBytes;
                snapshot.UploadBytes       += bucket.UploadBytes;
            }

            snapshot.Processes = buckets.Values
                .OrderByDescending(p => p.DownloadBytes + p.UploadBytes)
                .ThenByDescending(p => p.ActiveConnections)
                .ToList();

            return snapshot;
        }

        internal static string ProcessKey(string? processPath)
        {
            if (string.IsNullOrWhiteSpace(processPath)) return "";

            string name = processPath.Trim();
            int slash = name.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0) name = name.Substring(slash + 1);
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);

            return name.ToLowerInvariant();
        }

        public void Dispose() => _http.Dispose();
    }
}

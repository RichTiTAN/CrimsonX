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
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public sealed class NetworkDiagnosticsService : IDisposable
    {
        private HttpClient? _geoPingClient;
        private string _geoClientKey = "";

        private HttpClient GeoClient()
        {
            var cfg = MainWindow.Instance?.Config;
            int port = ExitNodeChain.ActivePort;
            bool auth = cfg != null
                && cfg.AllowLanConnections
                && cfg.EnableLanAuth
                && !string.IsNullOrWhiteSpace(cfg.LanAuthUsername)
                && !string.IsNullOrWhiteSpace(cfg.LanAuthPassword);
            string key = port + "|" + (auth ? cfg!.LanAuthUsername + ":" + cfg.LanAuthPassword : "");
            if (_geoPingClient != null && _geoClientKey == key) return _geoPingClient;
            try { _geoPingClient?.Dispose(); } catch { }
            var proxy = new System.Net.WebProxy("http://127.0.0.1:" + port);
            if (auth) proxy.Credentials = new System.Net.NetworkCredential(cfg!.LanAuthUsername, cfg.LanAuthPassword);
            _geoPingClient = new HttpClient(new SocketsHttpHandler
            {
                Proxy          = proxy,
                UseProxy       = true,
                ConnectTimeout = TimeSpan.FromSeconds(8)
            })
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _geoClientKey = key;
            return _geoPingClient;
        }

        private readonly HttpClient _grpcClient = new HttpClient(new HttpClientHandler())
        {
            DefaultRequestVersion = new Version(2, 0),
            DefaultVersionPolicy  = HttpVersionPolicy.RequestVersionExact
        };

        private static readonly byte[] GrpcQueryBody =
            { 0x00, 0x00, 0x00, 0x00, 0x02, 0x0A, 0x00 };

        private CancellationTokenSource? _geoCts;

        private CancellationTokenSource? _statsCts;
        private int _isFetching = 0; 
        public const int HistorySamples = 14;

        private readonly Queue<double> _upHistory = new();
        private readonly Queue<double> _dnHistory = new();
        private double _upSum;
        private double _dnSum;
        private long   _lastUpBytes;
        private long   _lastDnBytes;
        private DateTime _lastPollTime = DateTime.MinValue;

        private readonly Dictionary<string, long> _endpointUpSeen = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _endpointDnSeen = new(StringComparer.Ordinal);
        private long _endpointUpTotal;
        private long _endpointDnTotal;
        private SingboxConnectionsClient? _chainClient;

        private static readonly Dictionary<string, string> ContinentNames = new()
        {
            ["NA"] = "NORTH AMERICA", ["EU"] = "EUROPE",  ["AS"] = "ASIA",
            ["SA"] = "SOUTH AMERICA", ["AF"] = "AFRICA",  ["OC"] = "OCEANIA",
            ["AN"] = "ANTARCTICA"
        };

        private bool _disposed;

        public event Action<GeoTraceResult>? GeoTraceCompleted;

        public event Action<StatsSnapshot>? StatsUpdated;

        // Geo-trace

        private int _geoRun;

        public void StartGeoTrace()
        {
            int run = ++_geoRun;
            if (_geoCts != null)
            {
                try { _geoCts.Cancel(); _geoCts.Dispose(); } catch { }
            }
            var cts = new CancellationTokenSource();
            _geoCts = cts;
            var token = cts.Token;
            CrimsonX.Services.BackgroundTask.Run("geo trace", async () =>
            {
                try
                {
                    int port = ExitNodeChain.ActivePort;
                    if (!await ProxyListeningAsync(port, token).ConfigureAwait(false))
                    {
                        if (run != _geoRun) return;
                        string down = $"the local proxy on 127.0.0.1:{port} is not listening";
                        SimpleLogger.Log($"[Geo] run {run}: {down}.");
                        GeoTraceCompleted?.Invoke(new GeoTraceResult { Error = down });
                        return;
                    }
                    var hit = await TraceGeoAsync(token).ConfigureAwait(false);
                    if (run != _geoRun) return;
                    if (hit.Ip.Length == 0)
                    {
                        SimpleLogger.Log($"[Geo] run {run}: no source answered through 127.0.0.1:{port} ({hit.Error}).");
                        GeoTraceCompleted?.Invoke(new GeoTraceResult { Error = hit.Error });
                        return;
                    }
                    SimpleLogger.LogFirst(
                        $"geo-trace|{hit.CountryCode}|{port}",
                        $"[Geo] traced through 127.0.0.1:{port} -> {hit.Country} ({hit.CountryCode}) in {hit.Ms} ms via {hit.Source}" +
                        (ExitNodeChain.ChainActive ? " - that is the exit node's location" : ""));
                    GeoTraceCompleted?.Invoke(new GeoTraceResult
                    {
                        Country       = hit.Country,
                        Continent     = hit.Continent,
                        CountryCode   = hit.CountryCode,
                        ContinentCode = hit.ContinentCode,
                        Ip            = hit.Ip,
                        PingMs        = hit.Ms
                    });
                }
                catch (Exception ex)
                {
                    if (run != _geoRun) return;
                    if (token.IsCancellationRequested)
                    {
                        SimpleLogger.Log($"[Geo] run {run}: timeout, the request was cancelled.");
                        GeoTraceCompleted?.Invoke(new GeoTraceResult { Error = "the trace was cancelled" });
                        return;
                    }
                    if (CrimsonX.Services.BackgroundTask.IsTeardownAbort(ex)) return;
                    SimpleLogger.Log(ex);
                    GeoTraceCompleted?.Invoke(new GeoTraceResult { Error = ex.Message });
                }
            });
        }

        public void StopGeoTrace()
        {
            _geoRun++;
            if (_geoCts == null) return;
            try { _geoCts.Cancel(); _geoCts.Dispose(); } catch { }
            _geoCts = null;
        }

        private const int SourceBudgetMs = 6000;
        private const int ProxyCheckTimeoutMs = 2000;

        private sealed class TraceHit
        {
            public string Ip = "";
            public string CountryCode = "";
            public string Country = "";
            public string ContinentCode = "";
            public string Continent = "";
            public long Ms;
            public string Source = "";
            public string Error = "";
        }

        private static readonly Dictionary<string, string> ContinentCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AFRICA"] = "AF", ["ASIA"] = "AS", ["EUROPE"] = "EU", ["NORTH AMERICA"] = "NA", ["SOUTH AMERICA"] = "SA", ["OCEANIA"] = "OC", ["ANTARCTICA"] = "AN"
        };

        private static async Task<bool> ProxyListeningAsync(int port, CancellationToken token)
        {
            if (port <= 0) return false;
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(ProxyCheckTimeoutMs);
                await client.ConnectAsync(System.Net.IPAddress.Loopback, port, cts.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch { return false; }
        }

        private async Task<TraceHit> TraceGeoAsync(CancellationToken token)
        {
            var failures = new List<string>();
            var client = GeoClient();
            foreach (string source in new[] { "cloudflare", "geojs", "myip", "ipify" })
            {
                var attempt = new TraceHit();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(SourceBudgetMs);
                var sw = Stopwatch.StartNew();
                try
                {
                    if (source == "cloudflare") await FillFromCloudflareAsync(client, attempt, cts.Token).ConfigureAwait(false);
                    else if (source == "geojs") await FillFromGeojsAsync(client, attempt, cts.Token).ConfigureAwait(false);
                    else if (source == "myip") await FillFromMyIpAsync(client, attempt, cts.Token).ConfigureAwait(false);
                    else await FillFromIpifyAsync(client, attempt, cts.Token).ConfigureAwait(false);
                    sw.Stop();
                    if (attempt.Ip.Length > 0)
                    {
                        attempt.Ms = sw.ElapsedMilliseconds;
                        attempt.Source = source;
                        return attempt;
                    }
                    failures.Add($"{source}: no ip in the reply ({sw.ElapsedMilliseconds} ms)");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    sw.Stop();
                    failures.Add($"{source}: {ex.GetType().Name}: {ex.Message} ({sw.ElapsedMilliseconds} ms)");
                }
            }
            return new TraceHit { Error = failures.Count == 0 ? "no source answered" : string.Join("; ", failures) };
        }

        private static void ApplyCountry(TraceHit hit, string code)
        {
            hit.CountryCode = (code ?? "").Trim().ToUpperInvariant();
            if (hit.Country.Length == 0 && hit.CountryCode.Length > 0)
                hit.Country = CrimsonX.Localization.GeoTranslation.GetCountryEn(hit.CountryCode, hit.CountryCode);
            if (hit.CountryCode.Length > 0
                && CrimsonX.Localization.CountryContinents.Map.TryGetValue(hit.CountryCode, out string? continent))
                hit.Continent = continent;
            if (hit.ContinentCode.Length == 0 && hit.Continent.Length > 0
                && ContinentCodes.TryGetValue(hit.Continent, out string? continentCode))
                hit.ContinentCode = continentCode;
        }

        private static async Task FillFromCloudflareAsync(HttpClient client, TraceHit hit, CancellationToken token)
        {
            string text = await client.GetStringAsync("https://1.1.1.1/cdn-cgi/trace", token).ConfigureAwait(false);
            string code = "";
            foreach (string line in text.Split('\n'))
            {
                string row = line.Trim();
                if (row.StartsWith("ip=", StringComparison.Ordinal)) hit.Ip = row.Substring(3).Trim();
                else if (row.StartsWith("loc=", StringComparison.Ordinal)) code = row.Substring(4).Trim();
            }
            ApplyCountry(hit, code);
        }

        private static async Task FillFromGeojsAsync(HttpClient client, TraceHit hit, CancellationToken token)
        {
            string json = await client.GetStringAsync("https://get.geojs.io/v1/ip/geo.json", token).ConfigureAwait(false);
            var data = JObject.Parse(json);
            hit.Ip = data["ip"]?.ToString() ?? "";
            hit.Country = data["country"]?.ToString() ?? "";
            string continentCode = (data["continent_code"]?.ToString() ?? "").Trim().ToUpperInvariant();
            ApplyCountry(hit, data["country_code"]?.ToString() ?? "");
            if (hit.ContinentCode.Length == 0 && continentCode.Length > 0) hit.ContinentCode = continentCode;
            if (hit.Continent.Length == 0 && hit.ContinentCode.Length > 0
                && ContinentNames.TryGetValue(hit.ContinentCode, out string? continent))
                hit.Continent = continent;
        }

        private static async Task FillFromMyIpAsync(HttpClient client, TraceHit hit, CancellationToken token)
        {
            string json = await client.GetStringAsync("https://api.myip.com", token).ConfigureAwait(false);
            var data = JObject.Parse(json);
            hit.Ip = data["ip"]?.ToString() ?? "";
            hit.Country = data["country"]?.ToString() ?? "";
            ApplyCountry(hit, data["cc"]?.ToString() ?? "");
        }

        private static async Task FillFromIpifyAsync(HttpClient client, TraceHit hit, CancellationToken token)
        {
            string json = await client.GetStringAsync("https://api.ipify.org?format=json", token).ConfigureAwait(false);
            try { hit.Ip = JObject.Parse(json)["ip"]?.ToString() ?? ""; }
            catch { hit.Ip = ""; }
        }

        // Stats polling

        public void StartStatsPolling(Func<bool> isConnected)
        {
            StopStatsPolling();
            _statsCts = new CancellationTokenSource();
            var token = _statsCts.Token;
            CrimsonX.Services.BackgroundTask.Run("stats polling", async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try { await Task.Delay(1500, token).ConfigureAwait(false); }
                    catch { break; }
                    if (token.IsCancellationRequested) break;
                    if (!isConnected()) break;
                    try { await PollStatsTick(token).ConfigureAwait(false); } catch { }
                }
            });
        }

        public void StopStatsPolling()
        {
            if (_statsCts == null) return;
            try { _statsCts.Cancel(); _statsCts.Dispose(); } catch { }
            _statsCts = null;
            _upHistory.Clear();
            _dnHistory.Clear();
            _upSum = 0; _dnSum = 0;
            _lastUpBytes = 0; _lastDnBytes = 0;
            _lastPollTime = DateTime.MinValue;
            _endpointUpSeen.Clear();
            _endpointDnSeen.Clear();
            _endpointUpTotal = 0;
            _endpointDnTotal = 0;
        }

        // Private polling implementation 

        private async Task PollStatsTick(CancellationToken token)
        {
            if (Interlocked.CompareExchange(ref _isFetching, 1, 0) != 0) return;
            try
            {
                var endpoint = await ReadExitEndpointTotalsAsync(token).ConfigureAwait(false);
                long curUp;
                long curDn;
                if (endpoint.HasValue)
                {
                    curUp = endpoint.Value.Up;
                    curDn = endpoint.Value.Dn;
                }
                else
                {
                    using var request = new HttpRequestMessage(
                        HttpMethod.Post,
                        "http://127.0.0.1:10999/xray.app.stats.command.StatsService/QueryStats")
                    {
                        Version       = new Version(2, 0),
                        VersionPolicy = HttpVersionPolicy.RequestVersionExact
                    };
                    request.Content = new ByteArrayContent(GrpcQueryBody);
                    request.Content.Headers.ContentType =
                        new System.Net.Http.Headers.MediaTypeHeaderValue("application/grpc");
                    request.Headers.Add("TE", "trailers");
                    using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
                    using var combined = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, token);
                    using var response = await _grpcClient.SendAsync(request, combined.Token)
                        .ConfigureAwait(false);
                    var bytes = await response.Content.ReadAsByteArrayAsync(combined.Token)
                        .ConfigureAwait(false);
                    long upVal = 0, dnVal = 0;
                    ParseGrpcStatsBytes(bytes, ref upVal, ref dnVal);
                    curUp = upVal;
                    curDn = dnVal;
                }
                if (curUp > 0 && _lastUpBytes > 0)
                {
                    var diffUp = Math.Max(0, curUp - _lastUpBytes);
                    var diffDn = Math.Max(0, curDn - _lastDnBytes);
                    var now     = DateTime.UtcNow;
                    double elapsed = (_lastPollTime == DateTime.MinValue)
                        ? 1.5
                        : Math.Max(0.1, (now - _lastPollTime).TotalSeconds);
                    _upSum += diffUp;
                    _upHistory.Enqueue(diffUp);
                    if (_upHistory.Count > HistorySamples) _upSum -= _upHistory.Dequeue();
                    _dnSum += diffDn;
                    _dnHistory.Enqueue(diffDn);
                    if (_dnHistory.Count > HistorySamples) _dnSum -= _dnHistory.Dequeue();
                    double spdUpRaw = diffUp / elapsed;
                    double spdDnRaw = diffDn / elapsed;
                    StatsUpdated?.Invoke(new StatsSnapshot
                    {
                        SpeedUp     = FormatSpeed(spdUpRaw),
                        SpeedDn     = FormatSpeed(spdDnRaw),
                        DiffUpBytes = diffUp,
                        DiffDnBytes = diffDn,
                        UpHistory   = _upHistory.ToArray(),
                        DnHistory   = _dnHistory.ToArray()
                    });
                }
                if (curUp > 0) _lastUpBytes = curUp;
                if (curDn > 0) _lastDnBytes = curDn;
                _lastPollTime = DateTime.UtcNow;
            }
            catch (Exception ex) { SimpleLogger.Log(ex); }
            finally { Interlocked.Exchange(ref _isFetching, 0); }
        }

        private async Task<(long Up, long Dn)?> ReadExitEndpointTotalsAsync(CancellationToken token)
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return null;
            if (!ExitNodeChain.ChainActive || !ExitNodeChain.IsTunnelExit(cfg)) return null;
            if (!SingboxClashApi.IsConfigured) return null;
            _chainClient ??= new SingboxConnectionsClient();
            var snapshot = await _chainClient.GetChainAsync(ExitNodeChain.EndpointTag, token)
                .ConfigureAwait(false);
            if (snapshot == null) return null;
            if (!snapshot.HasChains)
                return (snapshot.SessionUploadBytes, snapshot.SessionDownloadBytes);
            var live = new HashSet<string>(StringComparer.Ordinal);
            foreach (var connection in snapshot.Connections)
            {
                live.Add(connection.Id);
                long seenUp = _endpointUpSeen.TryGetValue(connection.Id, out long prevUp) ? prevUp : 0;
                long seenDn = _endpointDnSeen.TryGetValue(connection.Id, out long prevDn) ? prevDn : 0;
                _endpointUpTotal += Math.Max(0, connection.UploadBytes   - seenUp);
                _endpointDnTotal += Math.Max(0, connection.DownloadBytes - seenDn);
                _endpointUpSeen[connection.Id] = Math.Max(seenUp, connection.UploadBytes);
                _endpointDnSeen[connection.Id] = Math.Max(seenDn, connection.DownloadBytes);
            }
            foreach (var id in _endpointUpSeen.Keys.Where(k => !live.Contains(k)).ToList())
            {
                _endpointUpSeen.Remove(id);
                _endpointDnSeen.Remove(id);
            }
            return (_endpointUpTotal, _endpointDnTotal);
        }

        // Protobuf varint decoder

        private static void ParseGrpcStatsBytes(byte[] bytes, ref long upVal, ref long dnVal)
        {
            int pos = 5; 
            while (pos < bytes.Length)
            {
                if (bytes[pos] != 0x0A) break;
                pos++;
                int  statLen  = ReadVarint32(bytes, ref pos);
                int  statEnd  = pos + statLen;
                bool isUplink = false, isDownlink = false, isSocks = false;
                long value    = 0;
                while (pos < statEnd)
                {
                    int tag = ReadVarint32(bytes, ref pos);
                    if (tag == 0x0A)
                    {
                        int nameLen = ReadVarint32(bytes, ref pos);
                        var span    = new ReadOnlySpan<byte>(bytes, pos, nameLen);
                        if (span.IndexOf("uplink"u8)              >= 0) isUplink   = true;
                        if (span.IndexOf("downlink"u8)            >= 0) isDownlink = true;
                        if (span.IndexOf("inbound>>>mixed-in"u8)  >= 0) isSocks    = true;
                        pos += nameLen;
                    }
                    else if (tag == 0x10)
                    {
                        value = ReadVarint64(bytes, ref pos);
                    }
                    else
                    {
                        int wireType = tag & 7;
                        if      (wireType == 0) ReadVarint64(bytes, ref pos);
                        else if (wireType == 1) pos += 8;
                        else if (wireType == 2) pos += ReadVarint32(bytes, ref pos);
                        else if (wireType == 5) pos += 4;
                    }
                }
                if (isSocks)
                {
                    if (isUplink)   upVal += value;
                    if (isDownlink) dnVal += value;
                }
            }
        }

        private static int ReadVarint32(byte[] data, ref int p)
        {
            int result = 0, shift = 0;
            while (p < data.Length)
            {
                byte b = data[p++];
                if (shift < 32) result |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
            }
            return result;
        }

        private static long ReadVarint64(byte[] data, ref int p)
        {
            long result = 0; int shift = 0;
            while (p < data.Length)
            {
                byte b = data[p++];
                result |= (long)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
            }
            return result;
        }

        // Speed formatting 

        internal static string FormatSpeed(double bytesPerSec) =>
            bytesPerSec >= 1_073_741_824 ? (bytesPerSec / 1_073_741_824.0).ToString("0.##", CultureInfo.InvariantCulture) + " GB/s" :
            bytesPerSec >= 1_048_576     ? (bytesPerSec / 1_048_576.0).ToString("0.#",  CultureInfo.InvariantCulture) + " MB/s" :
            bytesPerSec >= 1_024         ? (bytesPerSec / 1_024.0).ToString("0",      CultureInfo.InvariantCulture) + " KB/s" :
                                           ((int)bytesPerSec).ToString(CultureInfo.InvariantCulture) + " B/s";

        // IDisposable 

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopGeoTrace();
            StopStatsPolling();
            _chainClient?.Dispose();
            _geoPingClient?.Dispose();
            _grpcClient.Dispose();
        }
    }
}

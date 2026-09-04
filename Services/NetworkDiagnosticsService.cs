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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public sealed class NetworkDiagnosticsService : IDisposable
    {
        private readonly HttpClient _geoPingClient = new HttpClient(
            new HttpClientHandler
            {
                Proxy    = new System.Net.WebProxy("http://127.0.0.1:10919"),
                UseProxy = true
            })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

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
        private readonly Queue<double> _upHistory = new();
        private readonly Queue<double> _dnHistory = new();
        private double _upSum;
        private double _dnSum;
        private long   _lastUpBytes;
        private long   _lastDnBytes;
        private DateTime _lastPollTime = DateTime.MinValue;

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

        public void StartGeoTrace()
        {
            if (_geoCts != null)
            {
                try { _geoCts.Cancel(); _geoCts.Dispose(); } catch { }
            }
            _geoCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var token = _geoCts.Token;
            var sw    = Stopwatch.StartNew();

            _ = Task.Run(async () =>
            {
                try
                {
                    var json = await _geoPingClient
                        .GetStringAsync("https://get.geojs.io/v1/ip/geo.json", token)
                        .ConfigureAwait(false);
                    sw.Stop();

                    var data          = JObject.Parse(json);
                    var continentCode = data["continent_code"]?.ToString() ?? "";
                    var countryCode   = data["country_code"]?.ToString()   ?? "";
                    var country       = data["country"]?.ToString()        ?? "";
                    ContinentNames.TryGetValue(continentCode, out var continent);
                    continent ??= continentCode;

                    GeoTraceCompleted?.Invoke(new GeoTraceResult
                    {
                        Country       = country,
                        Continent     = continent,
                        CountryCode   = countryCode,
                        ContinentCode = continentCode,
                        PingMs        = sw.ElapsedMilliseconds
                    });
                }
                catch (Exception ex)
                {
                    if (token != _geoCts?.Token) return; 
                    SimpleLogger.Log(ex);
                    GeoTraceCompleted?.Invoke(new GeoTraceResult());
                }
            }, token);
        }

        /// <summary>Cancels any in-flight geo-trace.</summary>
        public void StopGeoTrace()
        {
            if (_geoCts == null) return;
            try { _geoCts.Cancel(); _geoCts.Dispose(); } catch { }
            _geoCts = null;
        }

        // Stats polling

        public void StartStatsPolling(Func<bool> isConnected)
        {
            StopStatsPolling();

            _statsCts = new CancellationTokenSource();
            var token = _statsCts.Token;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try { await Task.Delay(1500, token).ConfigureAwait(false); }
                    catch { break; }

                    if (token.IsCancellationRequested) break;
                    if (!isConnected()) break;

                    try { await PollStatsTick(token).ConfigureAwait(false); } catch { }
                }
            }, token);
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
        }

        // ─── Private polling implementation 

        private async Task PollStatsTick(CancellationToken token)
        {
            if (Interlocked.CompareExchange(ref _isFetching, 1, 0) != 0) return;
            try
            {
                var request = new HttpRequestMessage(
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

                long curUp = upVal;
                long curDn = dnVal;

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
                    if (_upHistory.Count > 40) _upSum -= _upHistory.Dequeue();

                    _dnSum += diffDn;
                    _dnHistory.Enqueue(diffDn);
                    if (_dnHistory.Count > 40) _dnSum -= _dnHistory.Dequeue();

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

        // ─── Protobuf varint decoder

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

        // ─── Speed formatting ─────────────────────────────────────────────

        private static string FormatSpeed(double bytesPerSec) =>
            bytesPerSec >= 1_048_576 ? $"{Math.Round(bytesPerSec / 1_048_576.0, 2)} MB/s" :
            bytesPerSec >= 1_024     ? $"{Math.Round(bytesPerSec / 1_024.0,     1)} KB/s" :
                                       $"{(int)bytesPerSec} B/s";

        // ─── IDisposable ──────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopGeoTrace();
            StopStatsPolling();
            _geoPingClient.Dispose();
            _grpcClient.Dispose();
        }
    }
}

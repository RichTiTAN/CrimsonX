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
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using CrimsonX.Models;

namespace CrimsonX.Services
{
    public enum ConfigPingKind
    {
        None,

        Ok,

        Slow,

        Rejected,

        TimedOut
    }

    public class ConfigTestResult
    {
        public bool Success { get; set; }
        public bool TimedOut { get; set; }

        public ConfigPingKind Kind { get; set; } = ConfigPingKind.None;

        public string Reason { get; set; } = "";

        public string EngineLog { get; set; } = "";

        public bool IsTcpPing { get; set; }

        public bool Measured => Success || Kind == ConfigPingKind.Slow;

        public string Text(string label)
        {
            if (Ping <= 0) return "";
            string number = IsTcpPing
                ? CrimsonX.Localization.AppStrings.TcpPingPrefix + Ping + "ms"
                : Ping + "ms";
            if (Kind == ConfigPingKind.Slow) number += CrimsonX.Localization.AppStrings.PingSlowSuffix;
            return string.IsNullOrEmpty(label) ? number : $"{label}: {number}";
        }

        public bool UdpOk { get; set; }
        public long Ping { get; set; }
        public long UdpPing { get; set; }
        public double Speed { get; set; }
        public double SpeedStability { get; set; }
        public bool SpeedStable { get; set; }
        public double SpeedMinMbps { get; set; }
        public double SpeedMedianMbps { get; set; }
        public int SpeedStalls { get; set; }
        public bool SpeedDegraded { get; set; }
        public string Link { get; set; } = "";
        public string OutboundJson { get; set; } = "";
        public string Continent { get; set; } = "";
        public string CountryCode { get; set; } = "";
        public string Country { get; set; } = "";
    }
    public class UdpStabilityResult
    {
        public int Sent { get; set; }
        public int Ok { get; set; }
        public long TotalPingMs { get; set; }
        public long MinPingMs { get; set; } = long.MaxValue;
        public long MaxPingMs { get; set; }

        public bool HasSamples => Sent > 0;
        public bool AnySuccess => Ok > 0;
        public double SuccessRate => Sent > 0 ? (double)Ok / Sent : 0;
        public double LossPercent => Sent > 0 ? 100.0 * (Sent - Ok) / Sent : 0;
        public long AvgPingMs => Ok > 0 ? TotalPingMs / Ok : 0;
        public long MinPingOrZero => Ok > 0 ? MinPingMs : 0;
    }

    public class SpeedStabilityResult
    {
        public double Mbps { get; set; }
        public double MedianMbps { get; set; }
        public double MinMbps { get; set; }
        public double PeakMbps { get; set; }
        public double FirstHalfMbps { get; set; }
        public double SecondHalfMbps { get; set; }
        public double JitterCv { get; set; }

        public int Windows { get; set; }
        public int StalledWindows { get; set; }
        public bool Degraded { get; set; }

        public double Stability { get; set; }
        public bool Stable { get; set; }

        public bool HasSamples => Windows > 0;
        public double StallRatio => Windows > 0 ? (double)StalledWindows / Windows : 0;
        public int StabilityPercent => (int)Math.Round(Stability * 100);
    }

    public static class ConfigTester
    {
        internal static readonly string[] TestTargets = {
            "https://www.gstatic.com/generate_204"
        };
        private const int TimeoutMs = 3000;
        private const int SpeedTestDurationMs = 7000;
        private const int SpeedSampleWindowMs = 250;
        private const int SpeedWarmupMs = 1000;
        private const int SpeedMinWindows = 4;
        private const int SpeedSampleBufferSize = 64 * 1024;
        private const double SpeedStallRatio = 0.10;
        private const double SpeedStallLimitRatio = 0.15;
        private const double SpeedJitterLimitCv = 0.35;
        private const double SpeedDegradeRatio = 0.50;
        private const double SpeedMinStableMbps = 1.0;
        private const string SpeedTestUrl = "https://proof.ovh.net/files/100Mb.dat";
        private const int NtpPort = 123;
        private const int ProbeAttempts = 3;
        private const string ScanNtpServerIp = "162.159.200.1";
        private const int StunPort = 19302;
        private static readonly Lazy<string> StunServerIp = new Lazy<string>(() =>
        {
            try
            {
                foreach (var addr in Dns.GetHostAddresses("stun.l.google.com"))
                    if (addr.AddressFamily == AddressFamily.InterNetwork) return addr.ToString();
            }
            catch { }
            return "74.125.250.129";
        });
        private const int GeoTimeoutMs = 6000;
        private static readonly string[] GeoEndpoints =
        {
            "https://get.geojs.io/v1/ip/geo.json",
            "https://ipwho.is/",
            "http://ip-api.com/json/?fields=status,country,countryCode,continentCode"
        };
        private const long MaxAcceptedPingMs = 1500;
        private const int StabilityDurationMs = 10000;
        private const int StabilityIntervalMs = 500;
        private const int StabilitySessionAttempts = 2;
        private const int WarmupBudgetMs = 3000;
        private static readonly Lazy<string> NtpServerIp = new Lazy<string>(() =>
        {
            try
            {
                foreach (var addr in Dns.GetHostAddresses("pool.ntp.org"))
                    if (addr.AddressFamily == AddressFamily.InterNetwork) return addr.ToString();
            }
            catch { }
            return "162.159.200.123";
        });

        public static List<ConfigTestResult> RankForConnection(IEnumerable<ConfigTestResult> configs)
        {
            var all = configs?.ToList() ?? new List<ConfigTestResult>();
            var stable = all.Where(c => c.SpeedStable).OrderByDescending(c => c.Speed).ToList();
            var unstable = all.Where(c => !c.SpeedStable).OrderByDescending(c => c.Speed).ToList();
            if (stable.Count == 0) return unstable;
            var ranked = new List<ConfigTestResult>(all.Count);
            ranked.AddRange(stable);
            ranked.AddRange(unstable);
            return ranked;
        }

        public static void ApplySpeedResult(ConfigTestResult target, SpeedStabilityResult speed)
        {
            target.Speed = speed.Mbps;
            target.SpeedStability = speed.Stability;
            target.SpeedStable = speed.Stable;
            target.SpeedMinMbps = speed.MinMbps;
            target.SpeedMedianMbps = speed.MedianMbps;
            target.SpeedStalls = speed.StalledWindows;
            target.SpeedDegraded = speed.Degraded;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            string trend = speed.Mbps <= 0
                ? "no throughput"
                : speed.Degraded
                    ? $"dropped {speed.FirstHalfMbps.ToString("F1", culture)} -> {speed.SecondHalfMbps.ToString("F1", culture)} Mbps"
                    : "no drop";
            SimpleLogger.Log($"[Speed] {target.CountryCode} {XrayLinkParser.ExtractServerAddress(target.OutboundJson)}: " +
                             $"{speed.Mbps.ToString("F1", culture)} Mbps, stability {speed.StabilityPercent}% " +
                             $"({speed.Windows} windows, {speed.StalledWindows} stalls, {trend})" +
                             $"{(speed.Stable ? "" : " - unstable")}");
        }

        private static (string Name, string Ip) TestAdapter(AppConfig cfg)
        {
            if (cfg.EnableAdapterBinding)
                return (cfg.SelectedAdapterName ?? "", (cfg.SelectedAdapterIp ?? "").Trim());
            string ip = XrayConfigWriter.ProbeSendThrough(cfg);
            foreach (string item in cfg.LoadBalanceAdapters ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                int at = item.LastIndexOf(" - ", StringComparison.Ordinal);
                if (at > 0 && string.Equals(item[(at + 3)..].Trim(), ip, StringComparison.Ordinal))
                    return (item[..at].Trim(), ip);
            }
            return ("", ip);
        }

        public static async Task<ConfigTestResult> TestConfigAsync(string link, AppConfig cfg, CancellationToken ct, bool isWatchdog = false, bool fetchGeo = false, bool isActiveWatchdog = false)
        {
            var res = new ConfigTestResult { Link = link };
            string outboundJsonStr = string.Empty;
            var adapter = TestAdapter(cfg);
            string testAdapterName = adapter.Name;
            string testAdapterIp   = adapter.Ip;
            if (TunnelConfigParser.TryParse(link, out var openVpn) && TunnelTcpProbe.IsOpenVpn(openVpn))
            {
                var tcp = await Task.Run(() => TunnelTcpProbe.Probe(openVpn, testAdapterIp, TunnelTcpProbe.DefaultTimeoutMs));
                string route = TunnelTcpProbe.RouteText(testAdapterIp);
                if (tcp.Ok)
                {
                    return new ConfigTestResult
                    {
                        Link = link, Success = true, Ping = tcp.Ms,
                        Kind = ConfigPingKind.Ok, IsTcpPing = true
                    };
                }
                return new ConfigTestResult
                {
                    Link = link, Success = false, TimedOut = tcp.TimedOut,
                    Kind = tcp.TimedOut ? ConfigPingKind.TimedOut : ConfigPingKind.Rejected,
                    Reason = tcp.Error, IsTcpPing = true
                };
            }
            IDisposable tunnelLease = null;
            var tunnelPrep = await TunnelEngine.PrepareTestAsync(link, cfg, testAdapterName, testAdapterIp);
            if (tunnelPrep.State == TunnelTestState.Failed)
            {
                try { tunnelPrep.Lease?.Dispose(); } catch { }
                res.Kind = ConfigPingKind.Rejected;
                res.Reason = "the tunnel behind this config could not be prepared";
                return res;
            }
            if (tunnelPrep.State == TunnelTestState.Ready)
            {
                outboundJsonStr = tunnelPrep.OutboundJson;
                tunnelLease = tunnelPrep.Lease;
            }
            else if (!XrayLinkParser.TryParseCustomConfig(link, out outboundJsonStr))
            {
                res.Kind = ConfigPingKind.Rejected;
                res.Reason = "this config could not be read as an xray outbound";
                return res;
            }
            res.OutboundJson = outboundJsonStr;
            int port = GetFreePort();
            int udpPort = GetFreeUdpPort();
            string tempId = Guid.NewGuid().ToString("N");
            string cfgPath = Path.Combine(cfg.XrayDir, $"test_{tempId}.json");
            Process testProc = null;
            var engineLog = new System.Text.StringBuilder();
            try
            {
                var outboundJson = JObject.Parse(outboundJsonStr);
                if (outboundJson["outbounds"] is JArray arr && arr.Count > 0)
                {
                    var outb = (JObject)arr[0];
                    outb["tag"] = "proxy";
                    string sendThroughIp = XrayConfigWriter.ProbeSendThrough(cfg);
                    if (sendThroughIp.Length > 0 && !XrayLinkParser.IsLocalOutbound(outb))
                    {
                        outb["sendThrough"] = sendThroughIp;
                    }
                }
                var fullConfig = new JObject
                {
                    ["log"] = new JObject { ["loglevel"] = "error" },
                    ["inbounds"] = new JArray
                    {
                        new JObject
                        {
                            ["port"] = port,
                            ["listen"] = "127.0.0.1",
                            ["protocol"] = "http",
                            ["tag"] = "in"
                        },
                        new JObject
                        {
                            ["port"] = udpPort,
                            ["listen"] = "127.0.0.1",
                            ["protocol"] = "dokodemo-door",
                            ["tag"] = "udp-in",
                            ["settings"] = new JObject
                            {
                                ["address"] = NtpServerIp.Value,
                                ["port"] = NtpPort,
                                ["network"] = "udp"
                            }
                        }
                    },
                    ["outbounds"] = BuildSessionOutbounds(outboundJson),
                    ["routing"] = new JObject
                    {
                        ["rules"] = new JArray
                        {
                            new JObject { ["type"] = "field", ["inboundTag"] = new JArray("in"), ["outboundTag"] = "proxy" },
                            new JObject { ["type"] = "field", ["inboundTag"] = new JArray("udp-in"), ["outboundTag"] = "proxy" }
                        }
                    }
                };
                File.WriteAllText(cfgPath, fullConfig.ToString());
                testProc = new Process();
                testProc.StartInfo.FileName = Path.Combine(cfg.XrayDir, "xray.exe");
                testProc.StartInfo.Arguments = $"run -c \"{cfgPath}\"";
                testProc.StartInfo.UseShellExecute = false;
                testProc.StartInfo.CreateNoWindow = true;
                testProc.StartInfo.RedirectStandardError = true;
                testProc.StartInfo.RedirectStandardOutput = true;
                testProc.ErrorDataReceived  += (s, ev) => { if (!string.IsNullOrWhiteSpace(ev.Data)) engineLog.AppendLine(ev.Data); };
                testProc.OutputDataReceived += (s, ev) => { if (!string.IsNullOrWhiteSpace(ev.Data)) engineLog.AppendLine(ev.Data); };
                await Task.Run(() => {
                    testProc.Start();
                    testProc.BeginErrorReadLine();
                    testProc.BeginOutputReadLine();
                });
                JobManager.AddProcess(testProc);
                await Task.Delay(300, ct);
                if (testProc.HasExited)
                {
                    res.Kind = ConfigPingKind.Rejected;
                    res.Reason = "the engine refused this config";
                    res.EngineLog = Tail(engineLog);
                    return res;
                }
                var handler = new HttpClientHandler
                {
                    Proxy = new WebProxy($"http://127.0.0.1:{port}"),
                    UseProxy = true,
                    ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
                };
                using var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromMilliseconds(TimeoutMs);
                long totalPing = 0;
                var targetsToTest = TestTargets;
                foreach (var target in targetsToTest)
                {
                    ct.ThrowIfCancellationRequested();
                    var sw = Stopwatch.StartNew();
                    using var req = new HttpRequestMessage(HttpMethod.Get, target);
                    req.Headers.ConnectionClose = true;
                    using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.NoContent && resp.StatusCode != HttpStatusCode.Found)
                    {
                        throw new Exception("the tunnel answered HTTP " + (int)resp.StatusCode);
                    }
                    sw.Stop();
                    long ping = sw.ElapsedMilliseconds;
                    totalPing += ping;
                    if (!isWatchdog && ping > MaxAcceptedPingMs)
                    {
                        res.Success = false;
                        res.Kind = ConfigPingKind.Slow;
                        res.Ping = ping;
                        return res;
                    }
                }
                res.Success = true;
                res.Kind = ConfigPingKind.Ok;
                res.Ping = totalPing / targetsToTest.Length;
                res.UdpOk = cfg.EnableUdpCapableNodes ? await TestUdpAsync(udpPort, ct) : true;
                if (fetchGeo)
                {
                    try
                    {
                        using var geoReq = new HttpRequestMessage(HttpMethod.Get, "https://get.geojs.io/v1/ip/geo.json");
                        geoReq.Headers.ConnectionClose = true;
                        using var geoResp = await client.SendAsync(geoReq, ct);
                        if (geoResp.IsSuccessStatusCode)
                        {
                            var geoJson = Newtonsoft.Json.Linq.JObject.Parse(await geoResp.Content.ReadAsStringAsync(ct));
                            res.CountryCode = geoJson["country_code"]?.ToString() ?? "";
                            res.Continent = geoJson["continent_code"]?.ToString() ?? "";
                            res.Continent = res.Continent switch
                            {
                                "AS" => "Asia",
                                "EU" => "Europe",
                                "NA" => "North America",
                                "SA" => "South America",
                                "AF" => "Africa",
                                "OC" => "Oceania",
                                "AN" => "Antarctica",
                                _ => res.Continent
                            };
                        }
                    }
                    catch { } 
                }
            }
            catch (OperationCanceledException)
            {
                res.Success = false;
                res.TimedOut = true;
                res.Kind = ConfigPingKind.TimedOut;
                res.Reason = "the probe ran out of time";
            }
            catch (Exception ex)
            {
                res.Success = false;
                res.TimedOut = ex is HttpRequestException;
                res.Kind = res.TimedOut ? ConfigPingKind.TimedOut : ConfigPingKind.Rejected;
                res.Reason = res.TimedOut ? "nothing answered through the tunnel" : ex.Message;
                res.EngineLog = Tail(engineLog);
            }
            finally
            {
                if (testProc != null)
                {
                    await Task.Run(() => {
                        try { testProc.Kill(); } catch { }
                        try { testProc.Dispose(); } catch { }
                    });
                }
                try { if (File.Exists(cfgPath)) File.Delete(cfgPath); } catch { }
                try { tunnelLease?.Dispose(); } catch { }
            }
            return res;
        }

        public static async Task<SpeedStabilityResult> TestSpeedStabilityAsync(string outboundJsonStr, AppConfig cfg, CancellationToken ct)
        {
            var res = new SpeedStabilityResult();
            int port = GetFreePort();
            string tempId = Guid.NewGuid().ToString("N");
            string cfgPath = Path.Combine(cfg.XrayDir, $"test_{tempId}.json");
            Process testProc = null;
            try
            {
                var outboundJson = JObject.Parse(outboundJsonStr);
                if (outboundJson["outbounds"] is JArray arr && arr.Count > 0)
                {
                    var outb = (JObject)arr[0];
                    outb["tag"] = "proxy";
                    string sendThroughIp = XrayConfigWriter.ProbeSendThrough(cfg);
                    if (sendThroughIp.Length > 0 && !XrayLinkParser.IsLocalOutbound(outb))
                    {
                        outb["sendThrough"] = sendThroughIp;
                    }
                }
                var fullConfig = new JObject
                {
                    ["log"] = new JObject { ["loglevel"] = "none" },
                    ["inbounds"] = new JArray
                    {
                        new JObject
                        {
                            ["port"] = port,
                            ["listen"] = "127.0.0.1",
                            ["protocol"] = "http",
                            ["tag"] = "in"
                        }
                    },
                    ["outbounds"] = BuildSessionOutbounds(outboundJson),
                    ["routing"] = new JObject
                    {
                        ["rules"] = new JArray
                        {
                            new JObject { ["type"] = "field", ["inboundTag"] = new JArray("in"), ["outboundTag"] = "proxy" }
                        }
                    }
                };
                File.WriteAllText(cfgPath, fullConfig.ToString());
                testProc = new Process();
                testProc.StartInfo.FileName = Path.Combine(cfg.XrayDir, "xray.exe");
                testProc.StartInfo.Arguments = $"run -c \"{cfgPath}\"";
                testProc.StartInfo.UseShellExecute = false;
                testProc.StartInfo.CreateNoWindow = true;
                await Task.Run(() => {
                    testProc.Start();
                });
                JobManager.AddProcess(testProc);
                await Task.Delay(300, ct); 
                var handler = new HttpClientHandler
                {
                    Proxy = new WebProxy($"http://127.0.0.1:{port}"),
                    UseProxy = true,
                    ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
                };
                using var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromMilliseconds(10000); 
                ct.ThrowIfCancellationRequested();
                var samples = await SampleSpeedWindowsAsync(client, ct);
                FillSpeedResult(samples, res);
                return res;
            }
            catch
            {
                return res;
            }
            finally
            {
                if (testProc != null)
                {
                    try { testProc.Kill(); } catch { }
                    try { testProc.Dispose(); } catch { }
                }
                try { if (File.Exists(cfgPath)) File.Delete(cfgPath); } catch { }
            }
        }

        private static async Task<List<(long Bytes, bool IsGap)>> SampleSpeedWindowsAsync(HttpClient client, CancellationToken ct)
        {
            var samples = new List<(long Bytes, bool IsGap)>();
            using var timeoutCts = new CancellationTokenSource(SpeedTestDurationMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            var sw = Stopwatch.StartNew();
            long[] pending = new long[1];
            long[] everRead = new long[1];
            long[] gapStart = new long[1];
            long[] noData = new long[1];
            long[] readerAlive = new long[1];
            Volatile.Write(ref gapStart[0], -1);
            async Task ReadLoopAsync()
            {
                byte[] buffer = new byte[SpeedSampleBufferSize];
                while (!linkedCts.IsCancellationRequested)
                {
                    try
                    {
                        Volatile.Write(ref gapStart[0], sw.ElapsedMilliseconds);
                        using var req = new HttpRequestMessage(HttpMethod.Get, SpeedTestUrl);
                        req.Headers.ConnectionClose = true;
                        req.Headers.UserAgent.ParseAdd("Mozilla/5.0");
                        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
                        if (!resp.IsSuccessStatusCode)
                        {
                            Volatile.Write(ref noData[0], Volatile.Read(ref everRead[0]) == 0 ? 1 : 0);
                            return;
                        }
                        using var stream = await resp.Content.ReadAsStreamAsync(linkedCts.Token);
                        int read;
                        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, linkedCts.Token)) > 0)
                        {
                            Interlocked.Add(ref pending[0], read);
                            Interlocked.Add(ref everRead[0], read);
                            Volatile.Write(ref gapStart[0], -1);
                        }
                    }
                    catch
                    {
                        Volatile.Write(ref noData[0], Volatile.Read(ref everRead[0]) == 0 ? 1 : 0);
                        return;
                    }
                }
            }
            var reader = Task.Run(ReadLoopAsync);
            Volatile.Write(ref readerAlive[0], 1);
            _ = reader.ContinueWith(_ => Volatile.Write(ref readerAlive[0], 0), TaskScheduler.Default);
            try
            {
                long nextTick = SpeedSampleWindowMs;
                while (!linkedCts.IsCancellationRequested && Volatile.Read(ref noData[0]) == 0)
                {
                    int wait = (int)(nextTick - sw.ElapsedMilliseconds);
                    if (wait > 0) await Task.Delay(wait, linkedCts.Token);
                    if (linkedCts.IsCancellationRequested) break;
                    bool isGap = Volatile.Read(ref readerAlive[0]) == 1 && Volatile.Read(ref gapStart[0]) >= 0;
                    samples.Add((Interlocked.Exchange(ref pending[0], 0), isGap));
                    nextTick += SpeedSampleWindowMs;
                }
            }
            catch (OperationCanceledException)
            {
            }
            try { await reader; } catch { }
            return samples;
        }

        private static void FillSpeedResult(List<(long Bytes, bool IsGap)> samples, SpeedStabilityResult res)
        {
            const int warmupWindows = SpeedWarmupMs / SpeedSampleWindowMs;
            const double windowSeconds = SpeedSampleWindowMs / 1000.0;
            var measured = new List<long>();
            foreach (var sample in samples)
            {
                if (!sample.IsGap) measured.Add(sample.Bytes);
            }
            if (measured.Count > warmupWindows) measured.RemoveRange(0, warmupWindows);
            res.Windows = measured.Count;
            if (measured.Count == 0) return;
            var speeds = new List<double>(measured.Count);
            long totalBytes = 0;
            foreach (long bytes in measured)
            {
                totalBytes += bytes;
                speeds.Add(bytes * 8.0 / windowSeconds / 1000000.0);
            }
            res.Mbps = totalBytes * 8.0 / (measured.Count * windowSeconds) / 1000000.0;
            res.PeakMbps = speeds.Max();
            res.MinMbps = speeds.Min();
            var sorted = speeds.OrderBy(s => s).ToList();
            res.MedianMbps = sorted.Count % 2 == 1
                ? sorted[sorted.Count / 2]
                : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0;
            double reference = sorted[(int)(sorted.Count * 0.75)];
            res.StalledWindows = reference <= 0 ? speeds.Count : speeds.Count(s => s < reference * SpeedStallRatio);
            int half = speeds.Count / 2;
            if (half > 0)
            {
                res.FirstHalfMbps = speeds.Take(half).Average();
                res.SecondHalfMbps = speeds.Skip(half).Average();
            }
            else
            {
                res.FirstHalfMbps = res.Mbps;
                res.SecondHalfMbps = res.Mbps;
            }
            res.Degraded = res.FirstHalfMbps > 0 && res.SecondHalfMbps < res.FirstHalfMbps * SpeedDegradeRatio;
            if (res.Mbps > 0 && speeds.Count > 1)
            {
                double variance = 0;
                foreach (double s in speeds) variance += (s - res.Mbps) * (s - res.Mbps);
                variance /= speeds.Count;
                res.JitterCv = Math.Sqrt(variance) / res.Mbps;
            }
            res.Stability = res.Mbps < SpeedMinStableMbps
                ? 0
                : Math.Clamp(1.0 - (0.6 * res.JitterCv + 1.5 * res.StallRatio + (res.Degraded ? 0.3 : 0)), 0, 1);
            res.Stable = res.Windows >= SpeedMinWindows
                         && res.Mbps >= SpeedMinStableMbps
                         && !res.Degraded
                         && res.StallRatio <= SpeedStallLimitRatio
                         && res.JitterCv <= SpeedJitterLimitCv;
        }

        private static int GetFreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        private static int GetFreeUdpPort()
        {
            var l = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            int port = ((IPEndPoint)l.Client.LocalEndPoint).Port;
            l.Close();
            return port;
        }

        private static async Task<bool> TestUdpAsync(int udpPort, CancellationToken ct)
        {
            var (ok, _) = await ProbeUdpAsync(udpPort, ct);
            return ok;
        }
        private static string Tail(System.Text.StringBuilder log, int max = 400)
        {
            string text = (log?.ToString() ?? "").Trim();
            return text.Length <= max ? text : text[^max..];
        }

        private static string LinkLabel(string link)
        {
            string text = (link ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= 60 ? text : text[..60] + "...";
        }

        private static async Task<(bool Ok, long RttMs)> ProbeUdpAsync(int udpPort, CancellationToken ct, int timeoutMs = TimeoutMs, UdpClient? client = null, bool requireNtpReply = false, bool stun = false)
        {
            UdpClient udp = client;
            bool ownsClient = false;
            try
            {
                if (udp == null)
                {
                    udp = new UdpClient();
                    ownsClient = true;
                }
                udp.Connect(IPAddress.Loopback, udpPort);
                try
                {
                    var stale = new byte[512];
                    while (udp.Client.Available > 0)
                    {
                        udp.Client.Receive(stale);
                    }
                }
                catch { }
                byte[] request;
                byte[] transactionId = null;
                if (stun)
                {
                    request = BuildStunRequest(out transactionId);
                }
                else
                {
                    request = new byte[48];
                    request[0] = 0x23;
                }
                var sw = Stopwatch.StartNew();
                await udp.SendAsync(request.AsMemory(), ct);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(timeoutMs);
                var resp = await udp.ReceiveAsync(timeoutCts.Token);
                sw.Stop();
                bool ok = resp.Buffer != null && resp.Buffer.Length > 0;
                if (ok && stun)
                {
                    ok = IsStunResponse(resp.Buffer!, transactionId);
                }
                else if (ok && requireNtpReply)
                {
                    ok = resp.Buffer!.Length >= 48 && (resp.Buffer[0] & 0x07) == 4;
                }
                return (ok, ok ? sw.ElapsedMilliseconds : 0);
            }
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested) throw;
                return (false, 0);
            }
            catch
            {
                return (false, 0);
            }
            finally
            {
                if (ownsClient)
                {
                    try { udp?.Dispose(); } catch { }
                }
            }
        }

        internal static byte[] BuildStunRequest(out byte[] transactionId)
        {
            transactionId = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
            var request = new byte[20];
            request[0] = 0x00;
            request[1] = 0x01;
            request[2] = 0x00;
            request[3] = 0x00;
            request[4] = 0x21;
            request[5] = 0x12;
            request[6] = 0xA4;
            request[7] = 0x42;
            Array.Copy(transactionId, 0, request, 8, 12);
            return request;
        }

        internal static bool IsStunResponse(byte[] buffer, byte[] transactionId)
        {
            if (buffer == null || transactionId == null) return false;
            if (buffer.Length < 20) return false;
            if (buffer[0] != 0x01 || buffer[1] != 0x01) return false;
            if (buffer[4] != 0x21 || buffer[5] != 0x12 || buffer[6] != 0xA4 || buffer[7] != 0x42) return false;
            for (int i = 0; i < 12; i++)
            {
                if (buffer[8 + i] != transactionId[i]) return false;
            }
            return true;
        }

        // ── UDP-Only Scan & Stability (UDP Scanner) ──

        private sealed class UdpTestSession : IDisposable
        {
            public Process Proc { get; set; }
            public string CfgPath { get; set; } = "";
            public int HttpPort { get; set; }
            public int UdpPort { get; set; }

            public void Dispose()
            {
                var proc = Proc;
                Proc = null;
                if (proc != null)
                {
                    try { proc.Kill(); } catch { }
                    try { proc.Dispose(); } catch { }
                }
                try { if (File.Exists(CfgPath)) File.Delete(CfgPath); } catch { }
            }
        }

        internal static JArray BuildSessionOutbounds(JObject outboundJson)
        {
            var list = new JArray();
            var hops = new List<JObject>();
            string text = outboundJson?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
            if (ConfigConverter.TryXrayChain(text, out var chain, out _, out _) && chain.Count > 0)
            {
                hops.AddRange(chain);
            }
            else if (outboundJson != null) hops.Add((JObject)outboundJson.DeepClone());
            for (int i = 0; i < hops.Count; i++)
            {
                var hop = hops[i];
                hop["tag"] = i == 0 ? "proxy" : "proxy-" + (i + 1);
                if (i + 1 < hops.Count)
                {
                    XrayConfigWriter.ChainThroughDialer(hop, "proxy-" + (i + 2));
                    list.Add(hop);
                    continue;
                }
                hop.Remove("proxySettings");
                if (hop["streamSettings"] is JObject stream && stream["sockopt"] is JObject sockopt)
                {
                    sockopt.Remove("dialerProxy");
                    if (sockopt.Count == 0) stream.Remove("sockopt");
                    if (stream.Count == 0) hop.Remove("streamSettings");
                }
                list.Add(hop);
            }
            return list;
        }

        private static void LogBindNotice(string message) => SimpleLogger.LogFirst($"bind|{message}", message);
        private static async Task<UdpTestSession> StartUdpTestSessionAsync(string outboundJsonStr, AppConfig cfg, CancellationToken ct, string? sendThroughIp = null, bool stun = false)
        {
            var session = new UdpTestSession
            {
                HttpPort = GetFreePort(),
                UdpPort = GetFreeUdpPort()
            };
            session.CfgPath = Path.Combine(cfg.XrayDir, $"test_{Guid.NewGuid().ToString("N")}.json");
            Process testProc = null;
            try
            {
                var outboundJson = JObject.Parse(outboundJsonStr);
                if (outboundJson["outbounds"] is JArray arr && arr.Count > 0)
                {
                    var outb = (JObject)arr[0];
                    outb["tag"] = "proxy";
                    string adapterIp = !string.IsNullOrWhiteSpace(sendThroughIp)
                        ? sendThroughIp!
                        : XrayConfigWriter.ProbeSendThrough(cfg);
                    if (!string.IsNullOrWhiteSpace(adapterIp))
                    {
                        if (!IsAvailableAdapterAddress(adapterIp))
                        {
                            LogBindNotice($"[UdpScanner] Adapter {adapterIp} is not up right now - testing without binding");
                            adapterIp = "";
                        }
                        else if (XrayLinkParser.IsLocalOutbound(outb))
                        {
                            LogBindNotice("[UdpScanner] Adapter binding skipped: the node itself is a local address");
                            adapterIp = "";
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(adapterIp))
                    {
                        outb["sendThrough"] = adapterIp;
                        SimpleLogger.LogFirst($"bind-perconfig|{adapterIp}", $"[UdpScanner] Test traffic bound to adapter address {adapterIp}");
                    }
                }
                var fullConfig = new JObject
                {
                    ["log"] = new JObject { ["loglevel"] = "none" },
                    ["inbounds"] = new JArray
                    {
                        new JObject
                        {
                            ["port"] = session.HttpPort,
                            ["listen"] = "127.0.0.1",
                            ["protocol"] = "http",
                            ["tag"] = "in"
                        },
                        new JObject
                        {
                            ["port"] = session.UdpPort,
                            ["listen"] = "127.0.0.1",
                            ["protocol"] = "dokodemo-door",
                            ["tag"] = "udp-in",
                            ["settings"] = new JObject
                            {
                                ["address"] = stun ? StunServerIp.Value : ScanNtpServerIp,
                                ["port"] = stun ? StunPort : NtpPort,
                                ["network"] = "udp"
                            }
                        }
                    },
                    ["outbounds"] = BuildSessionOutbounds(outboundJson),
                    ["routing"] = new JObject
                    {
                        ["rules"] = new JArray
                        {
                            new JObject { ["type"] = "field", ["inboundTag"] = new JArray("in"), ["outboundTag"] = "proxy" },
                            new JObject { ["type"] = "field", ["inboundTag"] = new JArray("udp-in"), ["outboundTag"] = "proxy" }
                        }
                    }
                };
                File.WriteAllText(session.CfgPath, fullConfig.ToString());
                testProc = new Process();
                testProc.StartInfo.FileName = Path.Combine(cfg.XrayDir, "xray.exe");
                testProc.StartInfo.Arguments = $"run -c \"{session.CfgPath}\"";
                testProc.StartInfo.UseShellExecute = false;
                testProc.StartInfo.CreateNoWindow = true;
                var proc = testProc;
                await Task.Run(() => { proc.Start(); });
                JobManager.AddProcess(testProc);
                await Task.Delay(300, ct);
                if (testProc.HasExited)
                {
                    session.Dispose();
                    return null;
                }
                session.Proc = testProc;
                return session;
            }
            catch (OperationCanceledException)
            {
                if (testProc != null) { try { testProc.Kill(); } catch { } try { testProc.Dispose(); } catch { } }
                try { if (File.Exists(session.CfgPath)) File.Delete(session.CfgPath); } catch { }
                if (ct.IsCancellationRequested) throw;
                return null;
            }
            catch
            {
                if (testProc != null) { try { testProc.Kill(); } catch { } try { testProc.Dispose(); } catch { } }
                try { if (File.Exists(session.CfgPath)) File.Delete(session.CfgPath); } catch { }
                return null;
            }
        }
        public static Task<ConfigTestResult> TestUdpOnlyAsync(string link, AppConfig cfg, CancellationToken ct, string? sendThroughIp = null, bool measureHttpPing = true, bool stun = false, bool warmUpFirst = false)
            => TestRelayOnlyAsync(link, cfg, ct, sendThroughIp, measureHttpPing, stun, warmUpFirst, tcp: false);

        public static Task<ConfigTestResult> TestTcpOnlyAsync(string link, AppConfig cfg, CancellationToken ct, string? sendThroughIp = null, bool measureHttpPing = true, bool stun = false, bool warmUpFirst = false)
            => TestRelayOnlyAsync(link, cfg, ct, sendThroughIp, measureHttpPing, stun, warmUpFirst, tcp: true);

        private static async Task<ConfigTestResult> TestRelayOnlyAsync(string link, AppConfig cfg, CancellationToken ct, string? sendThroughIp, bool measureHttpPing, bool stun, bool warmUpFirst, bool tcp)
        {
            var res = new ConfigTestResult { Link = link };
            string outboundJsonStr;
            var adapter = TestAdapter(cfg);
            string testAdapterName = adapter.Name;
            string testAdapterIp   = adapter.Ip;
            if (!string.IsNullOrWhiteSpace(sendThroughIp) && IsAvailableAdapterAddress(sendThroughIp!))
            {
                testAdapterIp = sendThroughIp!;
                string pickedName = AdapterNameForIp(sendThroughIp!);
                if (pickedName.Length > 0) testAdapterName = pickedName;
            }
            IDisposable tunnelLease = null;
            var tunnelPrep = await TunnelEngine.PrepareTestAsync(link, cfg, testAdapterName, testAdapterIp);
            if (tunnelPrep.State == TunnelTestState.Failed)
            {
                try { tunnelPrep.Lease?.Dispose(); } catch { }
                return res;
            }
            if (tunnelPrep.State == TunnelTestState.Ready)
            {
                outboundJsonStr = tunnelPrep.OutboundJson;
                tunnelLease = tunnelPrep.Lease;
            }
            else if (!ConfigConverter.TryXrayChain(link, out var linkHops, out _, out _) || linkHops.Count == 0)
            {
                return res;
            }
            else outboundJsonStr = ConfigConverter.BuildXrayChain(linkHops);
            res.OutboundJson = outboundJsonStr;
            UdpTestSession session = null;
            try
            {
                session = await StartUdpTestSessionAsync(outboundJsonStr, cfg, ct, sendThroughIp, stun);
                if (session == null) return res;
                if (tcp) await ProbeOverTcpAsync(res, session.HttpPort, ct);
                else await ProbeOverUdpAsync(res, session.HttpPort, session.UdpPort, warmUpFirst, measureHttpPing, stun, ct);
                return res;
            }
            catch (OperationCanceledException)
            {
                res.Success = false;
                res.UdpOk = false;
                if (ct.IsCancellationRequested) throw;
                return res;
            }
            catch
            {
                res.Success = false;
                res.UdpOk = false;
                return res;
            }
            finally
            {
                session?.Dispose();
                try { tunnelLease?.Dispose(); } catch { }
            }
        }
        private static async Task ProbeOverUdpAsync(ConfigTestResult res, int httpPort, int udpPort, bool warmUpFirst, bool measureHttpPing, bool stun, CancellationToken ct)
        {
            long bestPing = long.MaxValue;
            int okCount = 0;
            using (var probeSocket = new UdpClient())
            {
                if (warmUpFirst)
                {
                    if (await WarmUpAsync(udpPort, probeSocket, ct, stun))
                    {
                        var (ok, rtt) = await ProbeUdpAsync(udpPort, ct, TimeoutMs, probeSocket, requireNtpReply: !stun, stun: stun);
                        if (ok)
                        {
                            okCount++;
                            bestPing = rtt;
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < ProbeAttempts; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var (ok, rtt) = await ProbeUdpAsync(udpPort, ct, TimeoutMs, probeSocket, requireNtpReply: !stun, stun: stun);
                        if (ok)
                        {
                            okCount++;
                            if (rtt < bestPing) bestPing = rtt;
                        }
                    }
                }
            }
            res.UdpOk = okCount > 0;
            res.Success = res.UdpOk;
            res.UdpPing = okCount > 0 ? bestPing : 0;
            res.Ping = res.UdpPing;
            if (!res.Success) return;
            await FetchGeoAsync(res, httpPort, ct);
            if (string.IsNullOrWhiteSpace(res.CountryCode) && string.IsNullOrWhiteSpace(res.Country))
                await FetchGeoForServerAsync(res, ct);
            if (measureHttpPing)
            {
                long realPing = await MeasureHttpPingAsync(httpPort, ct);
                if (realPing > 0) res.Ping = realPing;
            }
        }

        private static async Task ProbeOverTcpAsync(ConfigTestResult res, int httpPort, CancellationToken ct)
        {
            res.UdpOk = false;
            res.UdpPing = 0;
            long bestPing = 0;
            using (var client = RelayDelayClient(httpPort))
            {
                await MeasureRelayDelaySampleAsync(client, ct);
                for (int i = 0; i < ProbeAttempts; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var (ok, rtt) = await MeasureRelayDelaySampleAsync(client, ct);
                    if (!ok) continue;
                    if (bestPing == 0 || rtt < bestPing) bestPing = rtt;
                }
            }
            res.Success = bestPing > 0;
            res.Ping = bestPing;
            res.Kind = bestPing > 0 ? ConfigPingKind.Ok : ConfigPingKind.TimedOut;
            if (!res.Success) return;
            await FetchGeoAsync(res, httpPort, ct);
            if (string.IsNullOrWhiteSpace(res.CountryCode) && string.IsNullOrWhiteSpace(res.Country))
                await FetchGeoForServerAsync(res, ct);
        }

        private static HttpClient RelayDelayClient(int httpPort)
        {
            var handler = new HttpClientHandler
            {
                Proxy = new WebProxy($"http://127.0.0.1:{httpPort}"),
                UseProxy = true,
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };
            return new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(TimeoutMs) };
        }

        private static async Task<(bool Ok, long RttMs)> MeasureRelayDelaySampleAsync(HttpClient client, CancellationToken ct, bool capPing = true)
        {
            try
            {
                long total = 0;
                foreach (var target in TestTargets)
                {
                    ct.ThrowIfCancellationRequested();
                    var sw = Stopwatch.StartNew();
                    using var req = new HttpRequestMessage(HttpMethod.Get, target);
                    using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.NoContent && resp.StatusCode != HttpStatusCode.Found)
                        return (false, 0);
                    sw.Stop();
                    long ping = sw.ElapsedMilliseconds;
                    if (capPing && ping > MaxAcceptedPingMs) return (false, 0);
                    total += ping;
                }
                long avg = total / TestTargets.Length;
                return (avg > 0, avg);
            }
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested) throw;
                return (false, 0);
            }
            catch
            {
                return (false, 0);
            }
        }

        private const int BatchReadyBudgetMs = 3000;
        private const int BatchReadyPollMs = 50;
        private const int BatchReadySettleMs = 200;

        public static string BuildUdpBatchConfig(IReadOnlyList<string> outboundsJson, IReadOnlyList<(int HttpPort, int UdpPort)> slots, bool stun, string adapterIp)
        {
            if (outboundsJson == null || slots == null || outboundsJson.Count == 0 || outboundsJson.Count != slots.Count)
                return "";
            var inbounds = new JArray();
            var outbounds = new JArray();
            var rules = new JArray();
            for (int i = 0; i < outboundsJson.Count; i++)
            {
                JArray nodes;
                try { nodes = JObject.Parse(outboundsJson[i])["outbounds"] as JArray; }
                catch { return ""; }
                if (nodes == null || nodes.Count == 0 || nodes[0] is not JObject node) return "";
                string tag = $"proxy-{i}";
                var outbound = (JObject)node.DeepClone();
                outbound["tag"] = tag;
                if (!string.IsNullOrWhiteSpace(adapterIp) && !XrayLinkParser.IsLocalOutbound(outbound))
                    outbound["sendThrough"] = adapterIp;
                outbounds.Add(outbound);
                string httpTag = $"in-{i}";
                string udpTag  = $"udp-in-{i}";
                inbounds.Add(new JObject
                {
                    ["port"] = slots[i].HttpPort,
                    ["listen"] = "127.0.0.1",
                    ["protocol"] = "http",
                    ["tag"] = httpTag
                });
                inbounds.Add(new JObject
                {
                    ["port"] = slots[i].UdpPort,
                    ["listen"] = "127.0.0.1",
                    ["protocol"] = "dokodemo-door",
                    ["tag"] = udpTag,
                    ["settings"] = new JObject
                    {
                        ["address"] = stun ? StunServerIp.Value : ScanNtpServerIp,
                        ["port"] = stun ? StunPort : NtpPort,
                        ["network"] = "udp"
                    }
                });
                rules.Add(new JObject { ["type"] = "field", ["inboundTag"] = new JArray(httpTag), ["outboundTag"] = tag });
                rules.Add(new JObject { ["type"] = "field", ["inboundTag"] = new JArray(udpTag),  ["outboundTag"] = tag });
            }
            return new JObject
            {
                ["log"] = new JObject { ["loglevel"] = "none" },
                ["inbounds"] = inbounds,
                ["outbounds"] = outbounds,
                ["routing"] = new JObject { ["rules"] = rules }
            }.ToString();
        }

        private sealed class UdpBatchSession : IDisposable
        {
            public Process Proc { get; set; }
            public string CfgPath { get; set; } = "";
            public List<int> HttpPorts { get; } = new List<int>();
            public List<int> UdpPorts { get; } = new List<int>();
            public int Count => HttpPorts.Count;

            public void Dispose()
            {
                var proc = Proc;
                Proc = null;
                if (proc != null)
                {
                    try { proc.Kill(); } catch { }
                    try { proc.Dispose(); } catch { }
                }
                try { if (File.Exists(CfgPath)) File.Delete(CfgPath); } catch { }
            }
        }

        private static int UniquePort(List<int> taken)
        {
            for (int attempt = 0; attempt < 64; attempt++)
            {
                int port = GetFreePort();
                if (!taken.Contains(port)) return port;
            }
            return GetFreePort();
        }

        private static int UniqueUdpPort(List<int> taken)
        {
            for (int attempt = 0; attempt < 64; attempt++)
            {
                int port = GetFreeUdpPort();
                if (!taken.Contains(port)) return port;
            }
            return GetFreeUdpPort();
        }

        private sealed class UdpBatchStart
        {
            public UdpBatchSession Session { get; set; }
            public string Error { get; set; } = "";
        }

        private static async Task<UdpBatchStart> StartUdpBatchSessionAsync(IReadOnlyList<string> outboundsJson, AppConfig cfg, CancellationToken ct, string? sendThroughIp, bool stun)
        {
            var session = new UdpBatchSession();
            var slots = new List<(int HttpPort, int UdpPort)>();
            for (int i = 0; i < outboundsJson.Count; i++)
            {
                int httpPort = UniquePort(session.HttpPorts);
                int udpPort  = UniqueUdpPort(session.UdpPorts);
                session.HttpPorts.Add(httpPort);
                session.UdpPorts.Add(udpPort);
                slots.Add((httpPort, udpPort));
            }
            string adapterIp = !string.IsNullOrWhiteSpace(sendThroughIp) ? sendThroughIp! : XrayConfigWriter.ProbeSendThrough(cfg);
            if (!string.IsNullOrWhiteSpace(adapterIp) && !IsAvailableAdapterAddress(adapterIp))
            {
                LogBindNotice($"[UdpScanner] Adapter {adapterIp} is not up right now - testing without binding");
                adapterIp = "";
            }
            string config = BuildUdpBatchConfig(outboundsJson, slots, stun, adapterIp);
            if (config.Length == 0)
                return new UdpBatchStart { Error = "the batch cannot be expressed as one xray config" };
            session.CfgPath = Path.Combine(cfg.XrayDir, $"test_{Guid.NewGuid().ToString("N")}.json");
            Process proc = null;
            try
            {
                File.WriteAllText(session.CfgPath, config);
                proc = new Process();
                proc.StartInfo.FileName = Path.Combine(cfg.XrayDir, "xray.exe");
                proc.StartInfo.Arguments = $"run -c \"{session.CfgPath}\"";
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.CreateNoWindow = true;
                var started = proc;
                await Task.Run(() => { started.Start(); }, ct);
                JobManager.AddProcess(proc);
                var watch = Stopwatch.StartNew();
                if (!await WaitForUdpBatchReadyAsync(proc, session.HttpPorts, ct))
                {
                    string reason = proc.HasExited
                        ? "xray exited while the batch was starting"
                        : $"xray did not open the batch inbounds within {BatchReadyBudgetMs} ms";
                    try { proc.Kill(); } catch { }
                    try { proc.Dispose(); } catch { }
                    try { if (File.Exists(session.CfgPath)) File.Delete(session.CfgPath); } catch { }
                    return new UdpBatchStart { Error = reason };
                }
                session.Proc = proc;
                string via = adapterIp.Length > 0 ? $" via {adapterIp}" : "";
                SimpleLogger.LogOnce($"batch-ready|{session.Count}|{adapterIp}", $"[Batch] {session.Count} configs{via}: one xray instance ready in {watch.ElapsedMilliseconds} ms");
                return new UdpBatchStart { Session = session };
            }
            catch (OperationCanceledException)
            {
                if (proc != null) { try { proc.Kill(); } catch { } try { proc.Dispose(); } catch { } }
                try { if (File.Exists(session.CfgPath)) File.Delete(session.CfgPath); } catch { }
                throw;
            }
            catch (Exception ex)
            {
                if (proc != null) { try { proc.Kill(); } catch { } try { proc.Dispose(); } catch { } }
                try { if (File.Exists(session.CfgPath)) File.Delete(session.CfgPath); } catch { }
                return new UdpBatchStart { Error = ex.Message };
            }
        }

        private static async Task<bool> WaitForUdpBatchReadyAsync(Process proc, List<int> httpPorts, CancellationToken ct)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < BatchReadyBudgetMs)
            {
                ct.ThrowIfCancellationRequested();
                try { if (proc.HasExited) return false; } catch { return false; }
                bool allOpen = true;
                foreach (int port in httpPorts)
                {
                    if (await IsTcpPortOpenAsync(port)) continue;
                    allOpen = false;
                    break;
                }
                if (allOpen && watch.ElapsedMilliseconds >= BatchReadySettleMs) return true;
                await Task.Delay(BatchReadyPollMs, ct);
            }
            return false;
        }

        private static async Task<bool> IsTcpPortOpenAsync(int port)
        {
            try
            {
                using var client = new TcpClient();
                using var timeout = new CancellationTokenSource(200);
                await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
                return true;
            }
            catch { return false; }
        }

        public static bool BatchCarriesThisLink(string link)
        {
            if (string.IsNullOrWhiteSpace(link)) return false;
            if (ConfigConverter.TryTunnel(link, out _, out _)) return false;
            return XrayLinkParser.TryParseCustomConfig(link, out _);
        }

        public static Task<ConfigTestResult[]> TestUdpBatchAsync(IReadOnlyList<string> links, AppConfig cfg, CancellationToken ct, string? sendThroughIp = null, bool measureHttpPing = true, bool stun = false, bool warmUpFirst = false)
            => TestRelayBatchAsync(links, cfg, ct, sendThroughIp, measureHttpPing, stun, warmUpFirst, tcp: false);

        public static Task<ConfigTestResult[]> TestTcpBatchAsync(IReadOnlyList<string> links, AppConfig cfg, CancellationToken ct, string? sendThroughIp = null, bool measureHttpPing = true, bool stun = false, bool warmUpFirst = false)
            => TestRelayBatchAsync(links, cfg, ct, sendThroughIp, measureHttpPing, stun, warmUpFirst, tcp: true);

        private static async Task<ConfigTestResult[]> TestRelayBatchAsync(IReadOnlyList<string> links, AppConfig cfg, CancellationToken ct, string? sendThroughIp, bool measureHttpPing, bool stun, bool warmUpFirst, bool tcp)
        {
            var results = new ConfigTestResult[links.Count];
            var outbounds = new List<string>();
            var indexes = new List<int>();
            var stragglers = new List<int>();
            for (int i = 0; i < links.Count; i++)
            {
                results[i] = new ConfigTestResult { Link = links[i] };
                if (!BatchCarriesThisLink(links[i]))
                {
                    stragglers.Add(i);
                    continue;
                }
                XrayLinkParser.TryParseCustomConfig(links[i], out string outboundJson);
                results[i].OutboundJson = outboundJson;
                outbounds.Add(outboundJson);
                indexes.Add(i);
            }

            Task RunSingleAsync(int index)
                => Task.Run(async () => results[index] = await TestRelayOnlyAsync(links[index], cfg, ct, sendThroughIp, measureHttpPing, stun, warmUpFirst, tcp), ct);

            var running = new List<Task>();
            foreach (int index in stragglers) running.Add(RunSingleAsync(index));

            if (outbounds.Count == 0)
            {
                await AwaitAllAsync(running, ct);
                return results;
            }

            UdpBatchStart start = await StartUdpBatchSessionAsync(outbounds, cfg, ct, sendThroughIp, stun);
            UdpBatchSession session = start.Session;
            if (session == null)
            {
                SimpleLogger.LogFirst("batch-fallback", $"[Batch] {outbounds.Count} configs: one shared instance failed ({start.Error}) - testing one instance per config instead");
                foreach (int index in indexes) running.Add(RunSingleAsync(index));
                await AwaitAllAsync(running, ct);
                return results;
            }
            try
            {
                for (int slot = 0; slot < indexes.Count; slot++)
                {
                    var result = results[indexes[slot]];
                    int httpPort = session.HttpPorts[slot];
                    int udpPort  = session.UdpPorts[slot];
                    running.Add(Task.Run(async () =>
                    {
                        try
                        {
                            if (tcp) await ProbeOverTcpAsync(result, httpPort, ct);
                            else await ProbeOverUdpAsync(result, httpPort, udpPort, warmUpFirst, measureHttpPing, stun, ct);
                        }
                        catch (OperationCanceledException) { result.Success = false; result.UdpOk = false; }
                        catch { result.Success = false; result.UdpOk = false; }
                    }, ct));
                }
                await AwaitAllAsync(running, ct);
                return results;
            }
            finally
            {
                session.Dispose();
            }
        }

        private static async Task AwaitAllAsync(List<Task> running, CancellationToken ct)
        {
            if (running.Count == 0) return;
            try { await Task.WhenAll(running); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) { }
            catch { }
        }

        public static Task<UdpStabilityResult> TestUdpStabilityAsync(string outboundJsonStr, AppConfig cfg, CancellationToken ct, int durationMs = StabilityDurationMs, int intervalMs = StabilityIntervalMs, Action<bool, long>? onSample = null, string? sendThroughIp = null, bool stun = false)
            => TestRelayStabilityAsync(outboundJsonStr, cfg, ct, durationMs, intervalMs, onSample, sendThroughIp, stun, tcp: false);

        public static Task<UdpStabilityResult> TestTcpStabilityAsync(string outboundJsonStr, AppConfig cfg, CancellationToken ct, int durationMs = StabilityDurationMs, int intervalMs = StabilityIntervalMs, Action<bool, long>? onSample = null, string? sendThroughIp = null, bool stun = false)
            => TestRelayStabilityAsync(outboundJsonStr, cfg, ct, durationMs, intervalMs, onSample, sendThroughIp, stun, tcp: true);

        private static async Task<UdpStabilityResult> TestRelayStabilityAsync(string outboundJsonStr, AppConfig cfg, CancellationToken ct, int durationMs, int intervalMs, Action<bool, long>? onSample, string? sendThroughIp, bool stun, bool tcp)
        {
            var result = new UdpStabilityResult();
            UdpTestSession session = null;
            HttpClient relayClient = null;
            try
            {
                using var probeSocket = new UdpClient();
                for (int attempt = 0; attempt < StabilitySessionAttempts && session == null; attempt++)
                {
                    var candidate = await StartUdpTestSessionAsync(outboundJsonStr, cfg, ct, sendThroughIp, stun);
                    if (candidate == null) continue;
                    bool ready;
                    if (tcp)
                    {
                        relayClient?.Dispose();
                        relayClient = RelayDelayClient(candidate.HttpPort);
                        ready = await WarmUpRelayAsync(relayClient, ct);
                    }
                    else ready = await WarmUpAsync(candidate.UdpPort, probeSocket, ct, stun);
                    if (ready) session = candidate;
                    else candidate.Dispose();
                }
                if (session == null) return result;
                var elapsed = Stopwatch.StartNew();
                while (elapsed.ElapsedMilliseconds < durationMs)
                {
                    ct.ThrowIfCancellationRequested();
                    (bool Ok, long RttMs) sample;
                    if (tcp && relayClient != null) sample = await MeasureRelayDelaySampleAsync(relayClient, ct, capPing: false);
                    else if (tcp) sample = (false, 0);
                    else sample = await ProbeUdpAsync(session.UdpPort, ct, TimeoutMs, probeSocket, requireNtpReply: !stun, stun: stun);
                    var (ok, rtt) = sample;
                    result.Sent++;
                    if (ok)
                    {
                        result.Ok++;
                        result.TotalPingMs += rtt;
                        if (rtt < result.MinPingMs) result.MinPingMs = rtt;
                        if (rtt > result.MaxPingMs) result.MaxPingMs = rtt;
                    }
                    try { onSample?.Invoke(ok, rtt); } catch { }
                    long nextTick = (long)result.Sent * intervalMs;
                    int wait = (int)(nextTick - elapsed.ElapsedMilliseconds);
                    if (wait > 0) await Task.Delay(wait, ct);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
            finally
            {
                session?.Dispose();
                relayClient?.Dispose();
            }
            return result;
        }
        private static async Task<long> MeasureHttpPingAsync(int httpPort, CancellationToken ct)
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    Proxy = new WebProxy($"http://127.0.0.1:{httpPort}"),
                    UseProxy = true,
                    ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
                };
                using var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromMilliseconds(TimeoutMs);
                long totalPing = 0;
                foreach (var target in TestTargets)
                {
                    ct.ThrowIfCancellationRequested();
                    var sw = Stopwatch.StartNew();
                    using var req = new HttpRequestMessage(HttpMethod.Get, target);
                    req.Headers.ConnectionClose = true;
                    using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.NoContent && resp.StatusCode != HttpStatusCode.Found)
                        return 0;
                    sw.Stop();
                    long ping = sw.ElapsedMilliseconds;
                    if (ping > MaxAcceptedPingMs) return 0;
                    totalPing += ping;
                }
                return totalPing / TestTargets.Length;
            }
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested) throw;
                return 0;
            }
            catch
            {
                return 0;
            }
        }
        internal static bool IsAvailableAdapterAddress(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;
            if (!IPAddress.TryParse(ip, out _)) return false;
            try
            {
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                    foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && unicast.Address.ToString() == ip)
                            return true;
                    }
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Log($"[UdpScanner] Adapter check failed: {ex.Message}");
            }
            return false;
        }

        private static string AdapterNameForIp(string ip)
        {
            try
            {
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                    foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && unicast.Address.ToString() == ip)
                            return adapter.Name;
                    }
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Log($"[UdpScanner] Adapter name lookup failed: {ex.Message}");
            }
            return "";
        }
        private static async Task<bool> WarmUpAsync(int udpPort, UdpClient socket, CancellationToken ct, bool stun = false)
        {
            var budget = Stopwatch.StartNew();
            while (budget.ElapsedMilliseconds < WarmupBudgetMs)
            {
                ct.ThrowIfCancellationRequested();
                var (ok, _) = await ProbeUdpAsync(udpPort, ct, TimeoutMs, socket, requireNtpReply: !stun, stun: stun);
                if (ok) return true;
            }
            return false;
        }
        private static async Task<bool> WarmUpRelayAsync(HttpClient client, CancellationToken ct)
        {
            var budget = Stopwatch.StartNew();
            while (budget.ElapsedMilliseconds < WarmupBudgetMs)
            {
                ct.ThrowIfCancellationRequested();
                var (ok, _) = await MeasureRelayDelaySampleAsync(client, ct, capPing: false);
                if (ok) return true;
            }
            return false;
        }
        private static async Task FetchGeoAsync(ConfigTestResult res, int httpPort, CancellationToken ct)
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    Proxy = new WebProxy($"http://127.0.0.1:{httpPort}"),
                    UseProxy = true,
                    ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
                };
                using var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromMilliseconds(GeoTimeoutMs);
                foreach (var endpoint in GeoEndpoints)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using var geoReq = new HttpRequestMessage(HttpMethod.Get, endpoint);
                        geoReq.Headers.ConnectionClose = true;
                        geoReq.Headers.UserAgent.ParseAdd("Mozilla/5.0");
                        using var geoResp = await client.SendAsync(geoReq, ct);
                        if (!geoResp.IsSuccessStatusCode) continue;
                        var geoJson = JObject.Parse(await geoResp.Content.ReadAsStringAsync(ct));
                        string code = geoJson["country_code"]?.ToString() ?? geoJson["countryCode"]?.ToString() ?? "";
                        string country = geoJson["country"]?.ToString() ?? "";
                        string continent = geoJson["continent_code"]?.ToString() ?? geoJson["continentCode"]?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(country)) continue;
                        res.CountryCode = code;
                        res.Country = country;
                        res.Continent = continent switch
                        {
                            "AS" => "Asia",
                            "EU" => "Europe",
                            "NA" => "North America",
                            "SA" => "South America",
                            "AF" => "Africa",
                            "OC" => "Oceania",
                            "AN" => "Antarctica",
                            _ => continent
                        };
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        if (ct.IsCancellationRequested) throw;
                    }
                    catch
                    {
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested) throw;
            }
            catch
            {
            }
        }
        private static async Task FetchGeoForServerAsync(ConfigTestResult res, CancellationToken ct)
        {
            try
            {
                string target = XrayLinkParser.ExtractServerAddress(res.OutboundJson).Trim();
                if (string.IsNullOrWhiteSpace(target)) return;
                if (!System.Net.IPAddress.TryParse(target, out _))
                {
                    var addresses = await Dns.GetHostAddressesAsync(target, ct);
                    foreach (var address in addresses)
                    {
                        if (address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            target = address.ToString();
                            break;
                        }
                    }
                }
                if (!System.Net.IPAddress.TryParse(target, out _) || XrayLinkParser.IsLocalAddress(target)) return;
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(4000) };
                using var resp = await client.GetAsync($"https://get.geojs.io/v1/ip/geo/{target}.json", ct);
                if (!resp.IsSuccessStatusCode) return;
                var geoJson = JObject.Parse(await resp.Content.ReadAsStringAsync(ct));
                string code = geoJson["country_code"]?.ToString() ?? "";
                string country = geoJson["country"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(country)) return;
                res.CountryCode = code;
                res.Country = country;
                res.Continent = (geoJson["continent_code"]?.ToString() ?? "") switch
                {
                    "AS" => "Asia",
                    "EU" => "Europe",
                    "NA" => "North America",
                    "SA" => "South America",
                    "AF" => "Africa",
                    "OC" => "Oceania",
                    "AN" => "Antarctica",
                    _ => res.Continent
                };
            }
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested) throw;
            }
            catch
            {
            }
        }
    }
}

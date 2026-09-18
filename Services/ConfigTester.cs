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
    public class ConfigTestResult
    {
        public bool Success { get; set; }
        public bool TimedOut { get; set; }

        public bool UdpOk { get; set; }
        public long Ping { get; set; }
        public long UdpPing { get; set; }
        public double Speed { get; set; }
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

    public static class ConfigTester
    {
        internal static readonly string[] TestTargets = {
            "http://clients3.google.com/generate_204",
            "http://cp.cloudflare.com",
            "http://detectportal.firefox.com"
        };
        private const int TimeoutMs = 3000;
        private const int SpeedTestDurationMs = 5000;
        private const int NtpPort = 123;
        private const int UdpProbeAttempts = 3;
        private const string ScanNtpServerIp = "162.159.200.1";
        private const int GeoTimeoutMs = 6000;
        private static readonly string[] GeoEndpoints =
        {
            "https://get.geojs.io/v1/ip/geo.json",
            "https://ipwho.is/",
            "http://ip-api.com/json/?fields=status,country,countryCode,continentCode"
        };
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

        public static async Task<ConfigTestResult> TestConfigAsync(string link, AppConfig cfg, CancellationToken ct, bool isWatchdog = false, bool fetchGeo = false, bool isActiveWatchdog = false)
        {
            var res = new ConfigTestResult { Link = link };
            string outboundJsonStr = string.Empty;

            if (link.TrimStart().StartsWith("{"))
            {
                outboundJsonStr = link;
                res.OutboundJson = outboundJsonStr;
            }
            else
            {
                if (!XrayLinkParser.TryParseLink(link, out outboundJsonStr))
                    return res;
                res.OutboundJson = outboundJsonStr;
            }

            int port = GetFreePort();
            int udpPort = GetFreeUdpPort();
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
                    if (cfg.EnableAdapterBinding && !string.IsNullOrWhiteSpace(cfg.SelectedAdapterIp) && !XrayLinkParser.IsLocalOutbound(outb))
                    {
                        outb["sendThrough"] = cfg.SelectedAdapterIp;
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
                    ["outbounds"] = new JArray
                    {
                        outboundJson["outbounds"][0]
                    },
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
                
                await Task.Run(() => {
                    testProc.Start();
                });
                
                JobManager.AddProcess(testProc);

                await Task.Delay(300, ct);

                if (testProc.HasExited)
                    return res;

                var handler = new HttpClientHandler
                {
                    Proxy = new WebProxy($"http://127.0.0.1:{port}"),
                    UseProxy = true,
                    ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
                };
                
                using var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromMilliseconds(TimeoutMs);

                long totalPing = 0;
                var targetsToTest = isActiveWatchdog ? new[] { "http://clients3.google.com/generate_204" } : TestTargets;
                foreach (var target in targetsToTest)
                {
                    ct.ThrowIfCancellationRequested();
                    var sw = Stopwatch.StartNew();
                    using var req = new HttpRequestMessage(HttpMethod.Get, target);
                    req.Headers.ConnectionClose = true;
                    using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    
                    if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.NoContent && resp.StatusCode != HttpStatusCode.Found)
                    {
                        throw new Exception("Bad status");
                    }
                    sw.Stop();
                    long ping = sw.ElapsedMilliseconds;
                    totalPing += ping;

                    if (!isWatchdog && ping > 1200)
                    {
                        res.Success = false;
                        res.Ping = ping;
                        return res;
                    }
                }

                res.Success = true;
                res.Ping = totalPing / targetsToTest.Length;

                res.UdpOk = await TestUdpAsync(udpPort, ct);

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
            catch
            {
                res.Success = false;
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
            }

            return res;
        }

        public static async Task<double> TestSpeedAsync(string outboundJsonStr, AppConfig cfg, CancellationToken ct)
        {
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
                    if (cfg.EnableAdapterBinding && !string.IsNullOrWhiteSpace(cfg.SelectedAdapterIp) && !XrayLinkParser.IsLocalOutbound(outb))
                    {
                        outb["sendThrough"] = cfg.SelectedAdapterIp;
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
                    ["outbounds"] = new JArray
                    {
                        outboundJson["outbounds"][0]
                    },
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
                var sw = Stopwatch.StartNew();
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://proof.ovh.net/files/100Mb.dat");
                req.Headers.ConnectionClose = true;
                req.Headers.UserAgent.ParseAdd("Mozilla/5.0");
                using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                
                if (!resp.IsSuccessStatusCode)
                {
                    return 0;
                }
                
                using var stream = await resp.Content.ReadAsStreamAsync(ct);
                byte[] buffer = new byte[8192];
                long totalBytes = 0;
                
                using var timeoutCts = new CancellationTokenSource(SpeedTestDurationMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                
                try
                {
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, linkedCts.Token)) > 0)
                    {
                        totalBytes += read;
                    }
                }
                catch (OperationCanceledException)
                {
                }
                
                sw.Stop();
                
                double seconds = sw.Elapsed.TotalSeconds;
                if (seconds == 0) seconds = 0.001;
                
                double bytesPerSec = totalBytes / seconds;
                double mbps = (bytesPerSec * 8) / 1000000.0;
                
                return mbps;
            }
            catch
            {
                return 0;
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
        private static async Task<(bool Ok, long RttMs)> ProbeUdpAsync(int udpPort, CancellationToken ct, int timeoutMs = TimeoutMs, UdpClient? client = null, bool requireNtpReply = false)
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

                var ntpRequest = new byte[48];
                ntpRequest[0] = 0x23;

                var sw = Stopwatch.StartNew();
                await udp.SendAsync(ntpRequest.AsMemory(), ct);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(timeoutMs);
                var resp = await udp.ReceiveAsync(timeoutCts.Token);
                sw.Stop();

                bool ok = resp.Buffer != null && resp.Buffer.Length > 0;
                if (ok && requireNtpReply)
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

        private static readonly HashSet<string> _bindNotices = new HashSet<string>();
        private static readonly object _bindNoticesLock = new object();

        private static void LogBindNoticeOnce(string message)
        {
            lock (_bindNoticesLock)
            {
                if (!_bindNotices.Add(message)) return;
            }

            SimpleLogger.Log(message);
        }
        private static async Task<UdpTestSession> StartUdpTestSessionAsync(string outboundJsonStr, AppConfig cfg, CancellationToken ct, string? sendThroughIp = null)
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
                        : (cfg.EnableAdapterBinding ? cfg.SelectedAdapterIp : "");

                    if (!string.IsNullOrWhiteSpace(adapterIp))
                    {
                        if (!IsAvailableAdapterAddress(adapterIp))
                        {
                            LogBindNoticeOnce($"[UdpScanner] Adapter {adapterIp} is not up right now - testing without binding");
                            adapterIp = "";
                        }
                        else if (XrayLinkParser.IsLocalOutbound(outb))
                        {
                            LogBindNoticeOnce("[UdpScanner] Adapter binding skipped: the node itself is a local address");
                            adapterIp = "";
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(adapterIp))
                    {
                        outb["sendThrough"] = adapterIp;
                        LogBindNoticeOnce($"[UdpScanner] Test traffic bound to adapter address {adapterIp}");
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
                                ["address"] = ScanNtpServerIp,
                                ["port"] = NtpPort,
                                ["network"] = "udp"
                            }
                        }
                    },
                    ["outbounds"] = new JArray
                    {
                        outboundJson["outbounds"][0]
                    },
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
        public static async Task<ConfigTestResult> TestUdpOnlyAsync(string link, AppConfig cfg, CancellationToken ct, string? sendThroughIp = null)
        {
            var res = new ConfigTestResult { Link = link };
            string outboundJsonStr;

            if (link.TrimStart().StartsWith("{"))
            {
                outboundJsonStr = link;
                res.OutboundJson = outboundJsonStr;
            }
            else
            {
                if (!XrayLinkParser.TryParseLink(link, out outboundJsonStr))
                    return res;
                res.OutboundJson = outboundJsonStr;
            }

            UdpTestSession session = null;
            try
            {
                session = await StartUdpTestSessionAsync(outboundJsonStr, cfg, ct, sendThroughIp);
                if (session == null) return res;

                long bestPing = long.MaxValue;
                int okCount = 0;

                using (var probeSocket = new UdpClient())
                {
                    for (int i = 0; i < UdpProbeAttempts; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var (ok, rtt) = await ProbeUdpAsync(session.UdpPort, ct, TimeoutMs, probeSocket, requireNtpReply: true);
                        if (ok)
                        {
                            okCount++;
                            if (rtt < bestPing) bestPing = rtt;
                        }
                    }
                }

                res.UdpOk = okCount > 0;
                res.Success = res.UdpOk;
                res.UdpPing = okCount > 0 ? bestPing : 0;
                res.Ping = res.UdpPing;

                if (res.Success)
                {
                    await FetchGeoAsync(res, session.HttpPort, ct);

                    if (string.IsNullOrWhiteSpace(res.CountryCode) && string.IsNullOrWhiteSpace(res.Country))
                        await FetchGeoForServerAsync(res, ct);

                    long realPing = await MeasureHttpPingAsync(session.HttpPort, ct);
                    if (realPing > 0) res.Ping = realPing;
                }

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
            }
        }
        public static async Task<UdpStabilityResult> TestUdpStabilityAsync(string outboundJsonStr, AppConfig cfg, CancellationToken ct, int durationMs = StabilityDurationMs, int intervalMs = StabilityIntervalMs, Action<bool, long>? onSample = null, string? sendThroughIp = null)
        {
            var result = new UdpStabilityResult();
            UdpTestSession session = null;
            try
            {
                using var probeSocket = new UdpClient();

                for (int attempt = 0; attempt < StabilitySessionAttempts && session == null; attempt++)
                {
                    var candidate = await StartUdpTestSessionAsync(outboundJsonStr, cfg, ct, sendThroughIp);
                    if (candidate == null) continue;

                    if (await WarmUpAsync(candidate.UdpPort, probeSocket, ct)) session = candidate;
                    else candidate.Dispose();
                }

                if (session == null) return result;

                var elapsed = Stopwatch.StartNew();

                while (elapsed.ElapsedMilliseconds < durationMs)
                {
                    ct.ThrowIfCancellationRequested();

                    var (ok, rtt) = await ProbeUdpAsync(session.UdpPort, ct, TimeoutMs, probeSocket, requireNtpReply: true);
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
                    if (ping > 1200) return 0;

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
        private static bool IsAvailableAdapterAddress(string ip)
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
        private static async Task<bool> WarmUpAsync(int udpPort, UdpClient socket, CancellationToken ct)
        {
            var budget = Stopwatch.StartNew();

            while (budget.ElapsedMilliseconds < WarmupBudgetMs)
            {
                ct.ThrowIfCancellationRequested();
                var (ok, _) = await ProbeUdpAsync(udpPort, ct, TimeoutMs, socket, requireNtpReply: true);
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

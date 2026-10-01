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
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using CrimsonX.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public static class SingboxConfigTester
    {
        private const int StartupDelayMs = 400;
        private const int TimeoutMs = 6000;
        private const long SlowTargetMs = 1500;

        public static async Task<ConfigTestResult> TestAsync(string raw, AppConfig cfg, string adapterName, string adapterIp, CancellationToken ct)
        {
            var res = new ConfigTestResult { Link = raw };
            if (string.IsNullOrWhiteSpace(raw)) { res.Kind = ConfigPingKind.Rejected; res.Reason = "no config to probe"; return res; }

            string sbDir = cfg?.SbDir ?? "";
            if (sbDir.Length == 0) { res.Kind = ConfigPingKind.Rejected; res.Reason = "sing-box's folder is not set"; return res; }

            CleanupStaleProbeDirs(sbDir);

            if (TunnelConfigParser.TryParse(raw, out var openVpn) && TunnelTcpProbe.IsOpenVpn(openVpn))
            {
                var tcp = await Task.Run(() => TunnelTcpProbe.Probe(openVpn, adapterIp, TunnelTcpProbe.DefaultTimeoutMs));
                string route = TunnelTcpProbe.RouteText(adapterIp);
                bool tunnelActive = XrayPipelineManager.ActiveOutbounds.Count > 0;

                if (tcp.Ok)
                {
                    return new ConfigTestResult
                    {
                        Link = raw, Success = true, Ping = tcp.Ms,
                        Kind = ConfigPingKind.Ok, IsTcpPing = true
                    };
                }

                string hint = tunnelActive ? " (a session is up, so the probe may be captured by the active tunnel)" : "";
                return new ConfigTestResult
                {
                    Link = raw, Success = false, TimedOut = tcp.TimedOut,
                    Kind = tcp.TimedOut ? ConfigPingKind.TimedOut : ConfigPingKind.Rejected,
                    Reason = tcp.Error, IsTcpPing = true
                };
            }

            if (!SingboxLinkParser.TryParseLink(raw, out string outboundJson, out _))
            {
                res.Kind = ConfigPingKind.Rejected;
                res.Reason = "this link could not be read as a sing-box outbound";
                return res;
            }

            res.OutboundJson = outboundJson;

            return await RunOutboundProbeAsync(outboundJson, raw, sbDir, adapterName, adapterIp, ct).ConfigureAwait(false);
        }

        public static async Task<ConfigTestResult> TestOutboundAsync(string singboxOutboundJson, AppConfig cfg, string adapterName, string adapterIp, CancellationToken ct)
        {
            var res = new ConfigTestResult { Link = singboxOutboundJson };
            if (string.IsNullOrWhiteSpace(singboxOutboundJson))
            {
                res.Kind = ConfigPingKind.Rejected;
                res.Reason = "no outbound to probe";
                return res;
            }

            string sbDir = cfg?.SbDir ?? "";
            if (sbDir.Length == 0)
            {
                res.Kind = ConfigPingKind.Rejected;
                res.Reason = "sing-box's folder is not set";
                return res;
            }

            CleanupStaleProbeDirs(sbDir);
            return await RunOutboundProbeAsync(singboxOutboundJson, singboxOutboundJson, sbDir, adapterName, adapterIp, ct).ConfigureAwait(false);
        }

        private static async Task<ConfigTestResult> RunOutboundProbeAsync(string outboundJson, string link, string sbDir, string adapterName, string adapterIp, CancellationToken ct)
        {
            var res = new ConfigTestResult { Link = link, OutboundJson = outboundJson };
            string tempDir = Path.Combine(sbDir, "probe_" + Guid.NewGuid().ToString("N"));
            string cfgPath = Path.Combine(tempDir, "config.json");
            int port = GetFreePort();
            Process proc = null;

            try
            {
                Directory.CreateDirectory(tempDir);

                var outbound = SingboxLinkParser.WithTagAndDial(
                    JObject.Parse(outboundJson), "proxy", adapterName, adapterIp);

                var probe = new JObject
                {
                    ["log"] = new JObject { ["level"] = "fatal" },
                    ["dns"] = new JObject
                    {
                        ["servers"] = new JArray
                        {
                            new JObject { ["tag"] = "dns_direct", ["type"] = "udp", ["server"] = "8.8.8.8" }
                        },
                        ["strategy"] = "ipv4_only"
                    },
                    ["inbounds"] = new JArray
                    {
                        new JObject { ["type"] = "mixed", ["tag"] = "in", ["listen"] = "127.0.0.1", ["listen_port"] = port }
                    },
                    ["outbounds"] = new JArray
                    {
                        outbound,
                        new JObject { ["type"] = "direct", ["tag"] = "direct" }
                    },
                    ["route"] = new JObject
                    {
                        ["rules"] = new JArray { new JObject { ["action"] = "sniff" } },
                        ["final"] = "proxy",
                        ["default_domain_resolver"] = new JObject { ["server"] = "dns_direct" }
                    }
                };

                File.WriteAllText(cfgPath, probe.ToString(Formatting.Indented));

                string exe = MainWindow.Instance?.GetAppPath(@"Data\sing_box\sing-box.exe")
                          ?? Path.Combine(sbDir, "sing-box.exe");

                proc = ProcessService.StartProcessDirect(
                    exe, $"run -D \"{tempDir}\" -c \"{cfgPath}\"", tempDir);
                if (proc == null)
                {
                    res.Kind = ConfigPingKind.Rejected;
                    res.Reason = "the sing-box probe could not be started";
                    return res;
                }

                await Task.Delay(StartupDelayMs, ct);
                if (proc.HasExited)
                {
                    res.Kind = ConfigPingKind.Rejected;
                    res.Reason = "sing-box refused this config (its ERROR lines are in the log)";
                    return res;
                }

                res.Success = await PingAsync(port, res, ct);
                if (!res.Success)
                {
                    res.Kind = res.TimedOut ? ConfigPingKind.TimedOut : ConfigPingKind.Rejected;
                    if (res.Reason.Length == 0)
                        res.Reason = res.TimedOut ? "nothing answered in time" : "the probe got no usable answer";
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
                res.Kind = ConfigPingKind.Rejected;
                res.Reason = ex.Message;
            }
            finally
            {
                if (proc != null)
                {
                    await Task.Run(() =>
                    {
                        try { proc.Kill(); } catch { }
                        try { proc.Dispose(); } catch { }
                    });
                }
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }

            return res;
        }

        private static async Task<bool> PingAsync(int port, ConfigTestResult res, CancellationToken ct)
        {
            var handler = new HttpClientHandler
            {
                Proxy = new WebProxy($"http://127.0.0.1:{port}"),
                UseProxy = true,
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };

            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromMilliseconds(TimeoutMs);

            long total = 0;
            int count = 0;

            foreach (var target in ConfigTester.TestTargets)
            {
                ct.ThrowIfCancellationRequested();

                var sw = Stopwatch.StartNew();
                using var req = new HttpRequestMessage(HttpMethod.Get, target);
                req.Headers.ConnectionClose = true;

                HttpResponseMessage resp;
                try
                {
                    resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                }
                catch (Exception ex) when (ex is HttpRequestException
                                        || (ex is OperationCanceledException && !ct.IsCancellationRequested))
                {
                    res.TimedOut = true;
                    return false;
                }

                using (resp)
                {
                    if (!resp.IsSuccessStatusCode
                        && resp.StatusCode != HttpStatusCode.NoContent
                        && resp.StatusCode != HttpStatusCode.Found)
                    {
                        return false;
                    }
                }

                sw.Stop();
                total += sw.ElapsedMilliseconds;
                count++;

                if (sw.ElapsedMilliseconds > SlowTargetMs)
                {
                    res.Kind = ConfigPingKind.Slow;
                    break;
                }
            }

            if (count == 0) return false;

            if (res.Kind != ConfigPingKind.Slow) res.Kind = ConfigPingKind.Ok;
            res.Ping = total / count;
            return true;
        }

        private static int GetFreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        private static int _probeSweepDone;
        private static void CleanupStaleProbeDirs(string sbDir)
        {
            if (Interlocked.Exchange(ref _probeSweepDone, 1) != 0) return;

            try
            {
                if (!Directory.Exists(sbDir)) return;

                var cutoff = DateTime.UtcNow.AddMinutes(-30);
                foreach (var dir in Directory.GetDirectories(sbDir, "probe_*"))
                {
                    try { if (Directory.GetLastWriteTimeUtc(dir) < cutoff) Directory.Delete(dir, true); } catch { }
                }
            }
            catch { }
        }
    }
}

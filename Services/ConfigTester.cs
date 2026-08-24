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
using Newtonsoft.Json.Linq;
using CrimsonX.Models;

namespace CrimsonX.Services
{
    public class ConfigTestResult
    {
        public bool Success { get; set; }
        public long Ping { get; set; }
        public double Speed { get; set; }
        public string Link { get; set; } = "";
        public string OutboundJson { get; set; } = "";
        public string Continent { get; set; } = "";
    }

    public static class ConfigTester
    {
        private static readonly string[] TestTargets = {
            "http://clients3.google.com/generate_204",
            "http://cp.cloudflare.com",
            "http://detectportal.firefox.com"
        };
        private const int TimeoutMs = 3000;

        public static async Task<ConfigTestResult> TestConfigAsync(string link, AppConfig cfg, CancellationToken ct, bool isWatchdog = false, bool fetchGeo = false)
        {
            var res = new ConfigTestResult { Link = link };
            if (!XrayLinkParser.TryParseLink(link, out string outboundJsonStr))
                return res;

            res.OutboundJson = outboundJsonStr;

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
                    if (cfg.EnableAdapterBinding && !string.IsNullOrWhiteSpace(cfg.SelectedAdapterIp))
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
                foreach (var target in TestTargets)
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
                    totalPing += sw.ElapsedMilliseconds;
                }

                long avgPing = totalPing / TestTargets.Length;
                if (!isWatchdog && avgPing > 1000)
                {
                    res.Success = false;
                    res.Ping = avgPing;
                    return res;
                }

                res.Success = true;
                res.Ping = avgPing;

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
                    if (cfg.EnableAdapterBinding && !string.IsNullOrWhiteSpace(cfg.SelectedAdapterIp))
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
                
                using var timeoutCts = new CancellationTokenSource(3000);
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
    }
}

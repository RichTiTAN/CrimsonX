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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CrimsonX.Services;

namespace CrimsonX
{
    public partial class MainWindow
    {
        private CancellationTokenSource _pipelineCts;
        private ConcurrentQueue<string> _untestedConfigs = new ConcurrentQueue<string>();
        private List<string> _reservePool = new List<string>();
        private HashSet<string> _customOutboundJsons = new HashSet<string>();
        private static readonly HttpClient _workerClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private async Task RunDynamicPipelineAsyncCore()
        {
            _pipelineCts?.Cancel();
            _pipelineCts?.Dispose();
            _pipelineCts = new CancellationTokenSource();
            var ct = _pipelineCts.Token;

            _seenLogs.Clear();
            _state.IsEngineRunning = true;
            _state.AbortBoot = false;
            _state.IsConnected = false;

            CrimsonX.Services.SimpleLogger.Log($"[Connect] Starting connection sequence in {_cfg.LastXrayMode}...");

            Dispatcher.UIThread.Post(() =>
            {
                if (txtConnectBtn != null)
                {
                    txtConnectBtn.Text = CrimsonX.Localization.AppStrings.IsPersian ? "در حال اتصال..." : "CONNECTING";
                    txtConnectBtn.Foreground = BrWhite;
                }
                SetConnectButtonProgress(5);
                UpdateRingAnimation("Connecting");
            });

            await UpdateLanIpAsync();
            await ApplySystemDnsAsync();
            ProxyService.SetSystemProxy(false);

            TryDeleteFile(GetAppPath(@"Data\Xray\access.log"));
            Dispatcher.UIThread.Post(() =>
            {
                var txtXrayLogs = this.FindControl<Avalonia.Controls.TextBox>("txtXrayLogs");
                if (txtXrayLogs != null) txtXrayLogs.Text = "";
            });
            Interlocked.Exchange(ref _lastXrayLogPos, 0);
            _xrayLogLines.Clear();

            bool customConfigApplied = false;
            List<string> customTopConfigs = new List<string>();
            _customOutboundJsons.Clear();

            if (_cfg.EnableCustomConfigs)
            {
                var c1 = _cfg.CustomConfig1;
                var c2 = _cfg.CustomConfig2;
                bool hasC1 = !string.IsNullOrWhiteSpace(c1);
                bool hasC2 = !string.IsNullOrWhiteSpace(c2);

                if (hasC1 || hasC2)
                {
                    Dispatcher.UIThread.Post(() => SetConnectButtonProgress(50));
                    var t1 = hasC1 ? await CrimsonX.Services.ConfigTester.TestConfigAsync(c1, _cfg, ct, false) : null;
                    var t2 = hasC2 ? await CrimsonX.Services.ConfigTester.TestConfigAsync(c2, _cfg, ct, false) : null;

                    bool t1Ok = t1 != null && t1.Success;
                    bool t2Ok = t2 != null && t2.Success;

                    if (t1Ok) _customOutboundJsons.Add(t1.OutboundJson);
                    if (t2Ok) _customOutboundJsons.Add(t2.OutboundJson);

                    if (t1Ok && t2Ok)
                    {
                        customTopConfigs = new List<string> { t1.OutboundJson, t2.OutboundJson };
                        customConfigApplied = true;
                    }
                    else if ((t1Ok || t2Ok) && _cfg.AllowOneCustomConfig)
                    {
                        customTopConfigs = new List<string> { t1Ok ? t1.OutboundJson : t2.OutboundJson };
                        customConfigApplied = true;
                    }
                    else if (t1Ok || t2Ok)
                    {
                        List<CrimsonX.Services.ConfigTestResult> passedScraped = new List<CrimsonX.Services.ConfigTestResult>();
                        int si = -1;
                        while (si < 4 && passedScraped.Count < 4)
                        {
                            ct.ThrowIfCancellationRequested();
                            var configs = si == -1 ? CrimsonX.Services.ConfigCache.LoadCache(GetAppPath(@"Data\cache\cache.bin")) : await FetchConfigsFromWorker(si, ct);
                            if (configs.Count == 0) { si++; continue; }
                            
                            var q = new System.Collections.Concurrent.ConcurrentQueue<string>(configs);
                            var tasks = new List<Task<CrimsonX.Services.ConfigTestResult>>();
                            while (passedScraped.Count < 4 && q.TryDequeue(out string cfg))
                            {
                                tasks.Add(CrimsonX.Services.ConfigTester.TestConfigAsync(cfg, _cfg, ct, false));
                                if (tasks.Count >= 5 || q.IsEmpty)
                                {
                                    var results = await Task.WhenAll(tasks);
                                    tasks.Clear();
                                    foreach(var r in results) { if(r.Success && passedScraped.Count < 4) passedScraped.Add(r); }
                                }
                            }
                            si++;
                        }
                        
                        Dispatcher.UIThread.Post(() => {
                            SetConnectButtonProgress(60);
                        });
                        
                        var customSpeedTasks = passedScraped.Select(async r => {
                            r.Speed = await CrimsonX.Services.ConfigTester.TestSpeedAsync(r.OutboundJson, _cfg, ct);
                            return r;
                        }).ToList();
                        
                        var speedResults = await Task.WhenAll(customSpeedTasks);
                        var fastest = speedResults.OrderByDescending(x => x.Speed).FirstOrDefault();
                        
                        customTopConfigs = new List<string> { t1Ok ? t1.OutboundJson : t2.OutboundJson };
                        if (fastest != null) customTopConfigs.Add(fastest.OutboundJson);
                        
                        customConfigApplied = true;
                    }
                }
            }

            if (customConfigApplied)
            {
                if (!await Task.Run(() => XrayPipelineManager.StartXray(customTopConfigs, _cfg, _cfg.XrayDir)))
                {
                    throw new Exception("Xray process failed to start.");
                }

                if (_cfg.LastXrayMode == "VPN Mode")
                {
                    if (!SingboxConfigWriter.Write(_cfg, _cfg.SbDir)) throw new Exception("Singbox config failed");
                    var sbProc = ProcessService.StartProcessDirect(GetAppPath(@"Data\sing_box\sing-box.exe"), "run -c config.json", _cfg.SbDir);
                    _sbPid = sbProc?.Id;
                    if (sbProc == null) throw new Exception("Singbox failed to start");
                }
                else
                {
                    ProxyService.SetSystemProxy(_cfg.LastXrayMode == "Proxy Mode");
                }

                _state.IsConnected = true;
                _state.SessionStartTime = DateTime.Now;
                CrimsonX.Services.SimpleLogger.Log("[Connect] Connected successfully with Custom Configs.");
                StartGeoPing();

                Dispatcher.UIThread.Post(() => {
                    SetConnectButtonProgress(100);
                    UpdateLocalPortUI();
                    UpdateLanPortUI();
                    UpdateRingAnimation("Connected");
                });

                StartSessionClock();
                StartStatsPolling();
                _ = StartBackgroundTestingLoop(ct);
                _ = StartRefreshTimer(ct);
                return;
            }

            int sourceIndex = -1;
            List<ConfigTestResult> passedConfigs = new List<ConfigTestResult>();

            while (sourceIndex < 4 && passedConfigs.Count < 4)
            {
                ct.ThrowIfCancellationRequested();
                Dispatcher.UIThread.Post(() => SetConnectButtonProgress(10 + (Math.Max(0, sourceIndex) * 5)));

                List<string> configs;
                if (sourceIndex == -1)
                {
                    configs = CrimsonX.Services.ConfigCache.LoadCache(GetAppPath(@"Data\cache\cache.bin"));
                }
                else
                {
                    configs = await FetchConfigsFromWorker(sourceIndex, ct);
                }
                
                if (configs.Count == 0)
                {
                    sourceIndex++;
                    continue;
                }

                Dispatcher.UIThread.Post(() => SetConnectButtonProgress(25));
                _untestedConfigs = new ConcurrentQueue<string>(configs);
                
                var seenSubnets = new HashSet<string>();
                var duplicates = new List<ConfigTestResult>();
                var testingTasks = new List<Task<ConfigTestResult>>();
                bool checkGeo = _cfg.EnableExcludedContinents && _cfg.ExcludedContinents != null && _cfg.ExcludedContinents.Count > 0;
                while (passedConfigs.Count < 8 && _untestedConfigs.TryDequeue(out string cfg))
                {
                    ct.ThrowIfCancellationRequested();
                    testingTasks.Add(ConfigTester.TestConfigAsync(cfg, _cfg, ct, fetchGeo: checkGeo));
                    
                    if (testingTasks.Count >= 5 || _untestedConfigs.IsEmpty)
                    {
                        var results = await Task.WhenAll(testingTasks);
                        testingTasks.Clear();

                        foreach (var r in results)
                        {
                            if (r.Success)
                            {
                                if (checkGeo && _cfg.ExcludedContinents!.Contains(r.Continent))
                                {
                                    continue; 
                                }

                                string addr = XrayLinkParser.ExtractServerAddress(r.OutboundJson);
                                string subnet = XrayLinkParser.GetSubnetOrDomain(addr);
                                if (!string.IsNullOrEmpty(subnet) && !seenSubnets.Contains(subnet))
                                {
                                    seenSubnets.Add(subnet);
                                    passedConfigs.Add(r);
                                }
                                else if (string.IsNullOrEmpty(subnet))
                                {
                                    passedConfigs.Add(r);
                                }
                                else
                                {
                                    duplicates.Add(r);
                                }
                            }
                        }

                        Dispatcher.UIThread.Post(() => {
                            int prog = 25 + (passedConfigs.Count * 5);
                            if (prog > 85) prog = 85;
                            SetConnectButtonProgress(prog);
                        });

                        if (passedConfigs.Count >= 6) break;
                    }
                }

                if (passedConfigs.Count < 6)
                {
                    foreach (var dup in duplicates)
                    {
                        if (passedConfigs.Count >= 6) break;
                        passedConfigs.Add(dup);
                    }
                }

                if (passedConfigs.Count < 2)
                {
                    sourceIndex++;
                }
            }

            if (passedConfigs.Count < 2)
            {
                throw new Exception("Failed to find enough working configs across all sources.");
            }

            Dispatcher.UIThread.Post(() => SetConnectButtonProgress(90));

            var speedTasks = passedConfigs.Select(async cfgTest =>
            {
                ct.ThrowIfCancellationRequested();
                cfgTest.Speed = await ConfigTester.TestSpeedAsync(cfgTest.OutboundJson, _cfg, ct);
                return cfgTest;
            }).ToList();

            var speedTestedConfigsList = await Task.WhenAll(speedTasks);
            var speedTestedConfigs = speedTestedConfigsList.ToList();

            var finalConfigs = speedTestedConfigs.OrderByDescending(x => x.Speed).ToList();
            var workingJson = finalConfigs.Select(x => x.OutboundJson).ToList();
            var topConfigs = workingJson.Take(2).ToList();
            
            _reservePool = workingJson.Skip(2).ToList();

            CrimsonX.Services.ConfigCache.SaveCache(GetAppPath(@"Data\cache\cache.bin"), workingJson);

            if (!await Task.Run(() => XrayPipelineManager.StartXray(topConfigs, _cfg, _cfg.XrayDir)))
            {
                throw new Exception("Xray process failed to start.");
            }

            if (_cfg.LastXrayMode == "VPN Mode")
            {
                if (!SingboxConfigWriter.Write(_cfg, _cfg.SbDir)) throw new Exception("Singbox config failed");
                var sbProc = ProcessService.StartProcessDirect(GetAppPath(@"Data\sing_box\sing-box.exe"), "run -c config.json", _cfg.SbDir);
                _sbPid = sbProc?.Id;
                if (sbProc == null) throw new Exception("Singbox failed to start");
            }
            else
            {
                ProxyService.SetSystemProxy(_cfg.LastXrayMode == "Proxy Mode");
            }

            _state.IsConnected = true;
            _state.SessionStartTime = DateTime.Now;
            CrimsonX.Services.SimpleLogger.Log("[Connect] Connected successfully with Dynamic Configs.");
            StartGeoPing();

            Dispatcher.UIThread.Post(() => {
                SetConnectButtonProgress(100);
                UpdateLocalPortUI();
                UpdateLanPortUI();
                UpdateRingAnimation("Connected");
            });

            StartSessionClock();
            StartStatsPolling();
            _ = StartBackgroundTestingLoop(ct);
            _ = StartRefreshTimer(ct);
        }

        private async Task<List<string>> FetchConfigsFromWorker(int index, CancellationToken ct)
        {
            string[] workers = CrimsonX.Services.AppSecrets.WorkerUrls;
            foreach (var worker in workers)
            {
                try
                {
                    string apiUrl = $"{worker}/api/{index}";
                    string newSha = null;
                    try
                    {
                        using var apiReq = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                        apiReq.Headers.UserAgent.ParseAdd("CrimsonX-App/1.0");
                        using var apiResp = await _workerClient.SendAsync(apiReq, ct);
                        if (apiResp.IsSuccessStatusCode)
                        {
                            var data = Newtonsoft.Json.Linq.JObject.Parse(await apiResp.Content.ReadAsStringAsync(ct));
                            newSha = data["sha"]?.ToString();
                        }
                        else
                        {
                            CrimsonX.Services.SimpleLogger.Log($"[Fetch] API {worker}/api/{index} returned {(int)apiResp.StatusCode}");
                        }
                    }
                    catch (Exception ex) 
                    {
                        CrimsonX.Services.SimpleLogger.Log($"[Fetch] API {worker}/api/{index} error: {ex.Message}");
                    }

                    string shaPath = GetAppPath($@"Data\cache\worker_sha_{index}.bin");
                    string dataPath = GetAppPath($@"Data\cache\worker_data_{index}.bin");

                    if (!string.IsNullOrEmpty(newSha) && File.Exists(shaPath) && File.Exists(dataPath))
                    {
                        string oldSha = CrimsonX.Services.ConfigCache.LoadString(shaPath);
                        if (oldSha == newSha)
                        {
                            string cachedContent = CrimsonX.Services.ConfigCache.LoadString(dataPath);
                            if (!string.IsNullOrEmpty(cachedContent))
                            {
                                var cachedConfigs = XrayLinkParser.ExtractVlessConfigs(cachedContent);
                                if (cachedConfigs.Count > 0) return cachedConfigs;
                            }
                        }
                    }

                    string url = $"{worker}/{index}";
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.UserAgent.ParseAdd("CrimsonX-App/1.0");
                    
                    using var resp = await _workerClient.SendAsync(req, ct);
                    if (resp.IsSuccessStatusCode)
                    {
                        string content = await resp.Content.ReadAsStringAsync(ct);
                        if (!string.IsNullOrEmpty(newSha))
                        {
                            CrimsonX.Services.ConfigCache.SaveString(shaPath, newSha);
                            CrimsonX.Services.ConfigCache.SaveString(dataPath, content);
                        }
                        var configs = XrayLinkParser.ExtractVlessConfigs(content);
                        if (configs.Count > 0) return configs;
                    }
                    else
                    {
                        CrimsonX.Services.SimpleLogger.Log($"[Fetch] Data {worker}/{index} returned {(int)resp.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    CrimsonX.Services.SimpleLogger.Log($"[Fetch] Data {worker}/{index} error: {ex.Message}");
                }
            }
            return new List<string>();
        }

        private async Task StartBackgroundTestingLoop(CancellationToken ct)
        {
            try
            {
                if (!_cfg.DisableBackgroundChecks)
                {
                    try
                    {
                        var newConfigs = await FetchConfigsFromWorker(0, ct);
                        if (newConfigs.Count > 0)
                        {
                            foreach (var c in newConfigs)
                            {
                                _untestedConfigs.Enqueue(c);
                            }
                        }
                    }
                    catch { }
                }

                while (!ct.IsCancellationRequested && _state.IsConnected)
                {
                    if (_cfg.DisableBackgroundChecks)
                    {
                        await Task.Delay(5000, ct);
                        continue;
                    }

                    if (_untestedConfigs.TryDequeue(out string cfg))
                    {
                        var res = await ConfigTester.TestConfigAsync(cfg, _cfg, ct);
                        if (res.Success)
                        {
                            lock (_reservePool)
                            {
                                if (!_reservePool.Contains(res.OutboundJson))
                                {
                                    _reservePool.Add(res.OutboundJson);
                                    
                                    var allWorking = new List<string>(XrayPipelineManager.ActiveOutbounds);
                                    allWorking.AddRange(_reservePool);
                                    CrimsonX.Services.ConfigCache.SaveCache(GetAppPath(@"Data\cache\cache.bin"), allWorking);
                                }
                            }
                        }
                    }
                    await Task.Delay(5000, ct);
                }
            }
            catch { }
        }


        private async Task StartRefreshTimer(CancellationToken ct)
        {
            try
            {
                string[] lastShas = new string[5];
                string[] workers = CrimsonX.Services.AppSecrets.WorkerUrls;

                while (!ct.IsCancellationRequested && _state.IsConnected)
                {
                    await Task.Delay(TimeSpan.FromHours(1), ct);
                    
                    if (_cfg.DisableRefreshTimer) continue;

                    bool apiSuccess = false;

                    try
                    {
                        for (int i = 0; i <= 4; i++)
                        {
                            ct.ThrowIfCancellationRequested();
                            string newSha = null;
                            
                            foreach (var workerUrl in workers)
                            {
                                try
                                {
                                    string url = $"{workerUrl}/api/{i}";
                                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                                    req.Headers.UserAgent.ParseAdd("CrimsonX-App/1.0");
                                    using var resp = await _workerClient.SendAsync(req, ct);
                                    if (resp.IsSuccessStatusCode)
                                    {
                                        string json = await resp.Content.ReadAsStringAsync(ct);
                                        var data = Newtonsoft.Json.Linq.JObject.Parse(json);
                                        newSha = data["sha"]?.ToString();
                                        apiSuccess = true;
                                        break; 
                                    }
                                }
                                catch (Exception ex)
                                {
                                    CrimsonX.Services.SimpleLogger.Log($"[RefreshTimer] SHA check failed for {workerUrl}/api/{i}: {ex.Message}");
                                }
                            }

                            if (newSha != null && newSha != lastShas[i])
                            {
                                lastShas[i] = newSha;
                                var newConfigs = await FetchConfigsFromWorker(i, ct);
                                foreach (var c in newConfigs) _untestedConfigs.Enqueue(c);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        CrimsonX.Services.SimpleLogger.Log($"[RefreshTimer] API fetch loop error: {ex.Message}");
                    }

                    if (!apiSuccess)
                    {
                        CrimsonX.Services.SimpleLogger.Log("[RefreshTimer] All workers failed to respond. Skipping active config watchdog test.");
                        continue;
                    }

                    try
                    {
                        CrimsonX.Services.SimpleLogger.Log("[RefreshTimer] Starting watchdog ping test for active configs...");
                        var activeConfigs = new List<string>(XrayPipelineManager.ActiveOutbounds);
                        var activeTasks = activeConfigs.Select(async cfgStr =>
                        {
                            ct.ThrowIfCancellationRequested();
                            var res = await ConfigTester.TestConfigAsync(cfgStr, _cfg, ct, isWatchdog: true);
                            if (!res.Success)
                            {
                                if (_customOutboundJsons.Contains(cfgStr))
                                {
                                    if (_cfg.DebugMode)
                                        CrimsonX.Services.SimpleLogger.Log("[RefreshTimer] Custom config failed the watchdog ping and will not be replaced.");
                                    return cfgStr;
                                }
                                CrimsonX.Services.ConfigCache.RemoveFromCache(GetAppPath(@"Data\cache\cache.bin"), cfgStr);
                                lock (_reservePool) { _reservePool.Remove(cfgStr); }
                                return null;
                            }
                            return cfgStr;
                        });

                        var activeResults = await Task.WhenAll(activeTasks);
                        var workingActive = activeResults.Where(x => x != null).ToList();

                        int needed = Math.Max(2 - workingActive.Count, 0);
                        CrimsonX.Services.SimpleLogger.Log($"[RefreshTimer] Watchdog finished. {workingActive.Count} passed. Replacements needed: {needed}");
                        
                        if (needed > 0)
                        {
                            CrimsonX.Services.SimpleLogger.Log($"[RefreshTimer] Initiating 5-by-5 batch test to find {needed} replacements...");
                            int targetPassedCount = (needed == 1) ? 6 : 4;
                            var configsToTest = new Queue<string>();
                            
                            lock (_reservePool)
                            {
                                foreach (var c in _reservePool.Where(x => !workingActive.Contains(x)))
                                    configsToTest.Enqueue(c);
                            }
                            
                            while (_untestedConfigs.TryDequeue(out string c))
                            {
                                configsToTest.Enqueue(c);
                            }

                            var passedConfigs = new List<ConfigTestResult>();
                            var testingTasks = new List<Task<ConfigTestResult>>();
                            bool checkGeo = _cfg.EnableExcludedContinents && _cfg.ExcludedContinents != null && _cfg.ExcludedContinents.Count > 0;

                            while (passedConfigs.Count < targetPassedCount && configsToTest.TryDequeue(out string cfg))
                            {
                                ct.ThrowIfCancellationRequested();
                                testingTasks.Add(ConfigTester.TestConfigAsync(cfg, _cfg, ct, isWatchdog: true, fetchGeo: checkGeo));
                                
                                if (testingTasks.Count >= 5 || configsToTest.Count == 0)
                                {
                                    var results = await Task.WhenAll(testingTasks);
                                    testingTasks.Clear();
                                    
                                    foreach (var r in results)
                                    {
                                        if (r.Success)
                                        {
                                            if (checkGeo && _cfg.ExcludedContinents!.Contains(r.Continent)) continue;
                                            passedConfigs.Add(r);
                                        }
                                        else
                                        {
                                            string badLink = r.Link ?? r.OutboundJson;
                                            if (badLink != null)
                                            {
                                                CrimsonX.Services.ConfigCache.RemoveFromCache(GetAppPath(@"Data\cache\cache.bin"), badLink);
                                                lock (_reservePool) { _reservePool.Remove(badLink); }
                                            }
                                        }
                                    }
                                    if (passedConfigs.Count >= targetPassedCount) break;
                                }
                            }

                            while (configsToTest.TryDequeue(out string c))
                            {
                                _untestedConfigs.Enqueue(c);
                            }

                            if (passedConfigs.Count > 0)
                            {
                                var speedTasks = passedConfigs.Select(async cfgTest =>
                                {
                                    ct.ThrowIfCancellationRequested();
                                    cfgTest.Speed = await ConfigTester.TestSpeedAsync(cfgTest.OutboundJson, _cfg, ct);
                                    return cfgTest;
                                });

                                var speedTestedConfigs = (await Task.WhenAll(speedTasks)).OrderByDescending(x => x.Speed).ToList();
                                var replacements = speedTestedConfigs.Take(needed).Select(x => x.OutboundJson).ToList();
                                
                                var finalNewOutbounds = new List<string>(workingActive);
                                finalNewOutbounds.AddRange(replacements);

                                await XrayPipelineManager.SwapOutboundsAsync(finalNewOutbounds, _cfg, _cfg.XrayDir);

                                lock (_reservePool)
                                {
                                    foreach (var unused in speedTestedConfigs.Skip(needed))
                                    {
                                        if (!_reservePool.Contains(unused.OutboundJson))
                                            _reservePool.Add(unused.OutboundJson);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        CrimsonX.Services.SimpleLogger.Log($"[RefreshTimer] Watchdog swap error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                CrimsonX.Services.SimpleLogger.Log($"[RefreshTimer] Fatal error: {ex.Message}");
            }
        }
    }
}

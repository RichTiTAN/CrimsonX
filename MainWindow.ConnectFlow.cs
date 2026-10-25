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
        private CancellationTokenSource? _pipelineCts;
        private readonly object _pipelineCtsLock = new object();
        private ConcurrentQueue<string> _untestedConfigs = new ConcurrentQueue<string>();
        private List<string> _reservePool = new List<string>();
        private HashSet<string> _customOutboundJsons = new HashSet<string>();
        private const int WorkerSourceCount = 6;
        private int _backgroundSeedSourceIndex = 0;
        private static readonly HttpClient _workerClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private bool IsConfigAllowed(ConfigTestResult r)
        {
            if (CrimsonX.Services.CountryFilter.IsBlocked(r.CountryCode))
            {
                CrimsonX.Services.SimpleLogger.LogOnce($"blocked-country-{r.CountryCode}",
                    $"[Filter] a config exiting in {r.CountryCode.ToUpperInvariant()} was skipped (RU and BY are blocked).");
                return false;
            }
            bool checkContinents = _cfg.EnableExcludedContinents && _cfg.ExcludedContinents != null && _cfg.ExcludedContinents.Count > 0;
            return !(checkContinents && _cfg.ExcludedContinents!.Contains(r.Continent));
        }

    // ── Dynamic Connect Pipeline ──

        private async Task RunDynamicPipelineAsyncCore()
        {
            lock (_pipelineCtsLock)
            {
                _pipelineCts?.Cancel();
                _pipelineCts?.Dispose();
                _pipelineCts = new CancellationTokenSource();
            }
            var ct = _pipelineCts.Token;
            await Task.Yield();
            _seenLogs.Clear();
            _lastEngineChangeUtc = DateTime.UtcNow;
            _state.IsEngineRunning = true;
            _state.AbortBoot = false;
            _state.IsConnected = false;
            _backgroundSeedSourceIndex = 0;
            CrimsonX.Services.SimpleLogger.Log(
                $"[Connect] exit node: source={_cfg.ExitNodeSource}, enabled={_cfg.EnableV2rayChain}, auto={_cfg.ExitNodeAuto}, country={_cfg.ExitNodeCountry}, chain={(_cfg.V2rayChainJson ?? "").Length} chars, custom={(_cfg.ExitNodeCustomJson ?? "").Length} chars");
            CrimsonX.Services.SimpleLogger.Log($"[Connect] Starting connection sequence in {_cfg.LastXrayMode}...");
            if (_cfg.EnableLoadBalanceAdapters)
            {
                var storedAdapters = XrayConfigWriter.AdapterIps(_cfg.LoadBalanceAdapters);
                var availableAdapters = XrayConfigWriter.AvailableAdapterIps(_cfg.LoadBalanceAdapters);
                if (storedAdapters.Count > 0 && availableAdapters.Count == 0)
                {
                    Dispatcher.UIThread.Post(() => ShowToast(
                        CrimsonX.Localization.AppStrings.ToastLoadBalanceAdaptersMissing, ToastKind.Error));
                }
                else if (storedAdapters.Count > availableAdapters.Count)
                {
                    CrimsonX.Services.SimpleLogger.Log(
                        $"[Adapter] Ignoring {storedAdapters.Count - availableAdapters.Count} load-balance adapter(s) that are no longer available.");
                }
            }
            if (_cfg.EnableAdapterBinding
                && !string.IsNullOrWhiteSpace(_cfg.SelectedAdapterIp)
                && !XrayConfigWriter.IsAdapterAvailable(_cfg.SelectedAdapterIp))
            {
                Dispatcher.UIThread.Post(() => ShowToast(
                    CrimsonX.Localization.AppStrings.ToastAdapterNoLongerAvail, ToastKind.Error));
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (txtConnectBtn != null)
                {
                    txtConnectBtn.Text = CrimsonX.Localization.AppStrings.StatusConnecting;
                    txtConnectBtn.Foreground = BrWhite;
                }
                ResetConnectProgressBaseline();
                SetConnectButtonProgress(ConnectProgress.Starting);
            });
            await UpdateLanIpAsync();
            await CrimsonX.Services.SystemDnsService.ApplyAsync(_cfg);
            Dispatcher.UIThread.Post(() => SetConnectButtonProgress(ConnectProgress.Preparing));
            ProxyService.SetSystemProxy(false);
            TryDeleteFile(GetAppPath(@"Data\Xray\access.log"));
            bool customConfigApplied = false;
            List<string> customTopConfigs = new List<string>();
            var customTunnels = new List<(int Slot, CrimsonX.Services.TunnelParseResult Tunnel)>();
            _customOutboundJsons.Clear();
            if (_cfg.EnableCustomConfigs)
            {
                void ApplyCustomSlot(int slot, string? raw)
                {
                    if (string.IsNullOrWhiteSpace(raw)) return;
                    var (_, slotAdapterIp) = CrimsonX.Services.XrayConfigWriter.CustomConfigAdapter(_cfg, slot);
                    if (CrimsonX.Services.TunnelConfigParser.TryParse(raw, out var tunnel))
                    {
                        if (tunnel == null || !tunnel.Success)
                        {
                            string reason = tunnel?.Error is { Length: > 0 } e ? e : "unsupported tunnel config";
                            CrimsonX.Services.SimpleLogger.Log($"[Connect] Custom tunnel config rejected: {reason}");
                            Dispatcher.UIThread.Post(() => ShowToast(reason));
                            return;
                        }
                        customTunnels.Add((slot, tunnel));
                        return;
                    }
                    if (CrimsonX.Services.ConfigConverter.TryXrayChain(raw, out var chainHops, out string chainLabel, out _) && chainHops.Count > 1)
                    {
                        string chainDoc = CrimsonX.Services.ConfigConverter.BuildXrayChain(chainHops);
                        chainDoc = CrimsonX.Services.XrayConfigWriter.ApplySendThrough(chainDoc, slotAdapterIp);
                        customTopConfigs.Add(chainDoc);
                        _customOutboundJsons.Add(chainDoc);
                        CrimsonX.Services.SimpleLogger.Log($"[Connect] Custom config '{chainLabel}' is dialed by xray as a chain of {chainHops.Count} hops.");
                        return;
                    }
                    if (CrimsonX.Services.ConfigConverter.TryXrayOutbound(raw, out string xrayDoc, out string configLabel, out _))
                    {
                        xrayDoc = CrimsonX.Services.XrayConfigWriter.ApplySendThrough(xrayDoc, slotAdapterIp);
                        customTopConfigs.Add(xrayDoc);
                        _customOutboundJsons.Add(xrayDoc);
                        CrimsonX.Services.SimpleLogger.Log($"[Connect] Custom config '{configLabel}' is dialed by xray.");
                        return;
                    }
                    string preview = raw.Length <= 40 ? raw : raw.Substring(0, 40) + "…";
                    CrimsonX.Services.SimpleLogger.Log($"[Connect] Custom config could not be read: \"{preview.Replace("\r", " ").Replace("\n", " ")}\"");
                    Dispatcher.UIThread.Post(() => ShowToast(CrimsonX.Localization.AppStrings.ToastConfigUnreadable, ToastKind.Error));
                }
                ApplyCustomSlot(1, _cfg.CustomConfig1);
                ApplyCustomSlot(2, _cfg.CustomConfig2);
                if (customTunnels.Count > 0)
                {
                    Dispatcher.UIThread.Post(() => SetConnectButtonProgress(40));
                    var readyTunnels = new List<(int Slot, CrimsonX.Services.TunnelParseResult Tunnel, string AdapterName, string AdapterIp)>();
                    foreach (var (slot, tunnel) in customTunnels)
                    {
                        if (await CrimsonX.Services.TunnelCredentialResolver.ApplyAsync(tunnel))
                        {
                            var (adapterName, adapterIp) = CrimsonX.Services.XrayConfigWriter.CustomConfigAdapter(_cfg, slot);
                            if (!CrimsonX.Services.XrayConfigWriter.UsesPerConfigAdapters(_cfg)
                                && adapterIp.Length == 0
                                && _cfg.EnableAdapterBinding
                                && !string.IsNullOrWhiteSpace(_cfg.SelectedAdapterIp))
                            {
                                adapterName = _cfg.SelectedAdapterName ?? "";
                                adapterIp   = (_cfg.SelectedAdapterIp ?? "").Trim();
                            }
                            readyTunnels.Add((slot, tunnel, adapterName, adapterIp));
                            continue;
                        }
                        CrimsonX.Services.SimpleLogger.Log($"[Connect] '{tunnel.Label}' skipped: no credentials were provided.");
                        Dispatcher.UIThread.Post(() => ShowToast(CrimsonX.Localization.AppStrings.ToastTunnelNeedsCredentials, ToastKind.Error));
                    }
                    if (readyTunnels.Count > 0)
                    {
                        var targets = new List<CrimsonX.Services.TunnelTarget>();
                        foreach (var item in readyTunnels)
                        {
                            targets.Add(new CrimsonX.Services.TunnelTarget
                            {
                                Key = CrimsonX.Services.TunnelConfigParser.KeyOf(item.Tunnel.Raw, item.AdapterName),
                                Parsed = item.Tunnel,
                                Tag = "tunnel-" + targets.Count,
                                AdapterName = item.AdapterName,
                                AdapterIp = item.AdapterIp
                            });
                        }
                        string tunnelError = "";
                        bool tunnelStarted = await Task.Run(() => CrimsonX.Services.TunnelEngine.EnsureTargetsStarted(
                            _cfg, CrimsonX.Services.TunnelEngine.GroupCustom, targets, "", "", out tunnelError));
                        if (tunnelStarted)
                        {
                            foreach (var item in readyTunnels)
                            {
                                string tunnelKey = CrimsonX.Services.TunnelConfigParser.KeyOf(item.Tunnel.Raw, item.AdapterName);
                                int? port = CrimsonX.Services.TunnelEngine.PortFor(CrimsonX.Services.TunnelEngine.GroupCustom, tunnelKey);
                                if (port == null)
                                {
                                    CrimsonX.Services.SimpleLogger.Log($"[Connect] Tunnel '{item.Tunnel.Label}' has no socks port in the engine; it cannot be added to the xray config.");
                                    Dispatcher.UIThread.Post(() => ShowToast(CrimsonX.Localization.AppStrings.ToastTunnelFailed, ToastKind.Error));
                                    continue;
                                }
                                string socksJson = CrimsonX.Services.TunnelEngine.BuildXraySocksOutbound(port.Value)
                                    .ToString(Newtonsoft.Json.Formatting.None);
                                customTopConfigs.Add(socksJson);
                                _customOutboundJsons.Add(socksJson);
                                CrimsonX.Services.SimpleLogger.Log($"[Connect] Tunnel '{item.Tunnel.Label}' is dialed by xray as a socks outbound on 127.0.0.1:{port.Value}.");
                            }
                            await AwaitTunnelEstablishedAsync(readyTunnels, ct);
                        }
                        else
                        {
                            CrimsonX.Services.SimpleLogger.Log($"[Connect] Tunnel engine failed: {tunnelError}");
                            Dispatcher.UIThread.Post(() => ShowToast(CrimsonX.Localization.AppStrings.ToastTunnelFailed, ToastKind.Error));
                        }
                    }
                }
                if (customTopConfigs.Count > 0)
                {
                    Dispatcher.UIThread.Post(() => SetConnectButtonProgress(50));
                    if (!_cfg.FetchConfigWhenOneProvided || customTopConfigs.Count >= 2)
                    {
                        customConfigApplied = true;
                    }
                    else
                    {
                        var fastest = await ScrapeFastestConfigAsync(ct);
                        if (fastest != null) customTopConfigs.Add(fastest);
                        customConfigApplied = true;
                    }
                }
            }
            if (_cfg.EnableAppRules && string.Equals(_cfg.LastXrayMode, "VPN Mode", StringComparison.OrdinalIgnoreCase))
            {
                string rulesError = "";
                bool rulesStarted = await Task.Run(() => CrimsonX.Services.AppRulesSingboxBuilder.EnsureRuleTunnels(_cfg, out rulesError));
                if (!rulesStarted)
                {
                    CrimsonX.Services.SimpleLogger.Log($"[Connect] App-rule tunnel group failed: {rulesError}");
                    Dispatcher.UIThread.Post(() => ShowToast(CrimsonX.Localization.AppStrings.ToastTunnelFailed, ToastKind.Error));
                }
            }
            else
            {
                CrimsonX.Services.TunnelEngine.Stop(CrimsonX.Services.TunnelEngine.GroupRules);
            }
            string engineLayout =
                $"[Tunnel] engines: custom({CrimsonX.Services.TunnelEngine.Describe(CrimsonX.Services.TunnelEngine.GroupCustom)}) " +
                $"rules({CrimsonX.Services.TunnelEngine.Describe(CrimsonX.Services.TunnelEngine.GroupRules)})";
            CrimsonX.Services.SimpleLogger.LogFirst(engineLayout, engineLayout);
            ct.ThrowIfCancellationRequested();
            if (customConfigApplied)
            {
                Dispatcher.UIThread.Post(() => SetConnectButtonProgress(ConnectProgress.EngineStart));
                if (!await Task.Run(() => XrayPipelineManager.StartXray(customTopConfigs, _cfg, _cfg.XrayDir)))
                {
                    throw new Exception("Xray process failed to start.");
                }
                bool customChainWanted = CrimsonX.Services.ExitNodeChain.ShouldChain(_cfg);
                if (_cfg.LastXrayMode == "VPN Mode" || customChainWanted)
                {
                    Dispatcher.UIThread.Post(() => SetConnectButtonProgress(ConnectProgress.ExitNode));
                    await PrepareExitNodeCredentialsAsync();
                    var sbPid = await StartSingBoxAsync(ct);
                    if (sbPid == null)
                    {
                        if (_cfg.LastXrayMode == "VPN Mode") throw new Exception("Singbox failed to start");
                        CrimsonX.Services.SimpleLogger.Log("[ExitNode] The chain did not start here; the system proxy stays on xray.");
                    }
                    else
                    {
                        _sbPid = sbPid;
                    }
                }
                if (_cfg.LastXrayMode != "VPN Mode") ApplySystemProxyForMode();
                _state.IsConnected = true;
                _state.SessionStartTime = DateTime.Now;
                CrimsonX.Services.SimpleLogger.Log("[Connect] Connected successfully with Custom Configs.");
                StartGeoPing();
                Dispatcher.UIThread.Post(() => {
                    SetConnectButtonProgress(ConnectProgress.Connected);
                    UpdateLocalPortUI();
                    UpdateLanPortUI();
                });
                StartSessionClock();
                StartStatsPolling();
                CrimsonX.Services.BackgroundTask.Run("connect testing", () => StartBackgroundTestingLoop(ct));
                CrimsonX.Services.BackgroundTask.Run("connect refresh", () => StartRefreshTimer(ct));
                return;
            }
            int sourceIndex = -1;
            bool sourcesExhausted = false;
            var seenConfigs = new HashSet<string>(StringComparer.Ordinal);
            var acceptedConfigs = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Queue<string>();
            var flowPassed = new List<ConfigTestResult>();
            var measured = new List<ConfigTestResult>();
            var fallback = new List<ConfigTestResult>();
            var overflowCandidates = new List<ConfigTestResult>();
            var droppedForCache = new HashSet<string>(StringComparer.Ordinal);
            _untestedConfigs = new ConcurrentQueue<string>();

            void DropPoolConfig(ConfigTestResult r)
            {
                if (!string.IsNullOrWhiteSpace(r.OutboundJson) && !_customOutboundJsons.Contains(r.OutboundJson))
                {
                    droppedForCache.Add(r.OutboundJson);
                    lock (_reservePool) { _reservePool.Remove(r.OutboundJson); }
                }
            }

            void FlushDroppedCache()
            {
                if (droppedForCache.Count == 0) return;
                try
                {
                    string cachePath = GetAppPath(@"Data\cache\cache.bin");
                    var existing = CrimsonX.Services.ConfigCache.LoadCache(cachePath);
                    int removed = existing.RemoveAll(droppedForCache.Contains);
                    if (removed > 0)
                    {
                        CrimsonX.Services.ConfigCache.SaveCache(cachePath, existing, true);
                        CrimsonX.Services.SimpleLogger.Log($"[Connect] Removed {removed} no-flow config(s) from the working cache.");
                    }
                }
                catch (Exception ex)
                {
                    CrimsonX.Services.SimpleLogger.Log($"[Connect] Cache cleanup failed: {ex.Message}");
                }
                droppedForCache.Clear();
            }

            int FillFlowFromOverflow()
            {
                int filled = 0;
                for (int i = overflowCandidates.Count - 1;
                     i >= 0 && flowPassed.Count < ConfigSelectionPolicy.PreferredConnectedConfigs;
                     i--)
                {
                    flowPassed.Add(overflowCandidates[i]);
                    overflowCandidates.RemoveAt(i);
                    filled++;
                }
                return filled;
            }

            async Task<(int Measured, int Fallback, int Dropped)> SpeedTestVerifiedAsync(
                List<ConfigTestResult> candidates, string phase)
            {
                if (candidates.Count == 0) return (0, 0, 0);
                Dispatcher.UIThread.Post(() => SetConnectButtonProgress(ConnectProgress.SpeedStart));
                int completed = 0;
                var speedTasks = candidates.Select(async r =>
                {
                    var speed = await ConfigTester.TestSpeedStabilityAsync(r.OutboundJson, _cfg, ct, r.FlowTarget);
                    ConfigTester.ApplySpeedResult(r, speed);
                    int done = Interlocked.Increment(ref completed);
                    int progress = ConnectProgress.Speed(done, candidates.Count);
                    Dispatcher.UIThread.Post(() => SetConnectButtonProgress(progress));
                    return r;
                }).ToList();
                var speedResults = await Task.WhenAll(speedTasks);
                int m = 0, f = 0, d = 0;
                foreach (var r in speedResults)
                {
                    if (r.SpeedResponded && r.Speed > 0) { measured.Add(r); m++; }
                    else if (r.SpeedResponded) { fallback.Add(r); f++; }
                    else
                    {
                        d++;
                        string server = string.IsNullOrWhiteSpace(r.OutboundJson)
                            ? "unreadable config"
                            : XrayLinkParser.ExtractServerAddress(r.OutboundJson);
                        CrimsonX.Services.SimpleLogger.Log(
                            $"[Connect] Dropped {server} after the final speed test: no payload");
                        DropPoolConfig(r);
                    }
                }
                Dispatcher.UIThread.Post(() => SetConnectButtonProgress(ConnectProgress.SpeedComplete));
                CrimsonX.Services.SimpleLogger.Log(
                    $"[Connect] Speed round ({phase}): {candidates.Count} flowed -> {m} measured, {f} fallback, {d} dropped.");
                return (m, f, d);
            }

            while (true)
            {
                while (ConfigSelectionPolicy.NeedsMoreFlow(flowPassed.Count)
                       && (pending.Count > 0 || !sourcesExhausted))
                {
                    ct.ThrowIfCancellationRequested();
                    var sourceWatch = System.Diagnostics.Stopwatch.StartNew();
                    if (pending.Count == 0)
                    {
                        if (sourceIndex >= WorkerSourceCount) { sourcesExhausted = true; break; }
                        int sourceForFetch = sourceIndex;
                        string sourceLabel = sourceForFetch == -1 ? "-1 (cache)" : sourceForFetch.ToString();
                        Dispatcher.UIThread.Post(() => SetConnectButtonProgress(ConnectProgress.Discovery));
                        CrimsonX.Services.SimpleLogger.Log(sourceForFetch == -1
                            ? "[Connect] Source -1: the locally saved working configs."
                            : $"[Connect] Source {sourceForFetch}: fetching from the worker sources.");
                        List<string> configs;
                        if (sourceForFetch == -1)
                        {
                            configs = CrimsonX.Services.ConfigCache.LoadCache(GetAppPath(@"Data\cache\cache.bin"));
                            configs = configs.Where(c => !CrimsonX.Services.XrayLinkParser.IsGrpcOutbound(c)).ToList();
                        }
                        else
                        {
                            try { configs = await FetchConfigsFromWorker(sourceForFetch, ct); }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex)
                            {
                                CrimsonX.Services.SimpleLogger.Log($"[Connect] Source {sourceForFetch} could not be fetched: {ex.Message}");
                                configs = null;
                            }
                        }
                        if (configs != null)
                        {
                            if (!string.IsNullOrWhiteSpace(_cfg.CustomConfig1) && CrimsonX.Services.XrayLinkParser.TryParseCustomConfig(_cfg.CustomConfig1, out string c1Json))
                                configs.RemoveAll(c => c == c1Json || c.Contains(c1Json));
                            if (!string.IsNullOrWhiteSpace(_cfg.CustomConfig2) && CrimsonX.Services.XrayLinkParser.TryParseCustomConfig(_cfg.CustomConfig2, out string c2Json))
                                configs.RemoveAll(c => c == c2Json || c.Contains(c2Json));
                        }
                        if (configs == null || configs.Count == 0)
                        {
                            CrimsonX.Services.SimpleLogger.Log($"[Connect] Source {sourceLabel} done: no candidates.");
                            sourceIndex++;
                            if (sourceIndex >= WorkerSourceCount) sourcesExhausted = true;
                            continue;
                        }
                        var distinct = ShuffledDistinctConfigs(configs, seenConfigs);
                        if (distinct.Count == 0)
                        {
                            CrimsonX.Services.SimpleLogger.Log($"[Connect] Source {sourceLabel} done: every candidate was already tested.");
                            sourceIndex++;
                            if (sourceIndex >= WorkerSourceCount) sourcesExhausted = true;
                            continue;
                        }
                        pending = new Queue<string>(distinct);
                    }

                    int sourceRun = sourceIndex;
                    string sourceRunLabel = sourceRun == -1 ? "-1 (cache)" : sourceRun.ToString();
                    int tested = 0, ping = 0, flowed = 0, flowPassedHere = 0;
                    int noFlow = 0, udpBlocked = 0, blocked = 0, duplicatesHere = 0;

                    void HandleResult(ConfigTestResult r)
                    {
                        if (r.PingOk) ping++;
                        if (!r.Success || !r.Flowed)
                        {
                            if (r.PingOk && !r.Success && r.Kind == ConfigPingKind.NoData)
                            {
                                noFlow++;
                                DropPoolConfig(r);
                            }
                            else if (!r.Success)
                            {
                                DropPoolConfig(r);
                            }
                            else
                            {
                                noFlow++;
                                DropPoolConfig(r);
                            }
                            return;
                        }
                        flowed++;
                        if (!r.UdpOk)
                        {
                            udpBlocked++;
                            DropPoolConfig(r);
                            return;
                        }
                        if (!IsConfigAllowed(r))
                        {
                            blocked++;
                            DropPoolConfig(r);
                            return;
                        }
                        string identity = XrayLinkParser.ConfigIdentity(r.OutboundJson);
                        if (identity.Length > 0 && !acceptedConfigs.Add(identity))
                        {
                            duplicatesHere++;
                            return;
                        }
                        if (flowPassed.Count < ConfigSelectionPolicy.PreferredConnectedConfigs)
                        {
                            flowPassed.Add(r);
                            flowPassedHere++;
                        }
                        else
                        {
                            overflowCandidates.Add(r);
                        }
                        int progress = ConnectProgress.Flow(flowPassed.Count);
                        Dispatcher.UIThread.Post(() => SetConnectButtonProgress(progress));
                    }

                    await ConfigProbeRunner.RunAsync(
                        pending,
                        5,
                        (link, token) =>
                        {
                            tested++;
                            return ConfigTester.TestConfigAsync(link, _cfg, token, fetchGeo: true, verifyFlow: true);
                        },
                        HandleResult,
                        () => ConfigSelectionPolicy.NeedsMoreFlow(flowPassed.Count),
                        ct);

                    int overflowFill = FillFlowFromOverflow();
                    flowPassedHere += overflowFill;
                    if (overflowFill > 0)
                    {
                        CrimsonX.Services.SimpleLogger.Log(
                            $"[Connect] Filled {overflowFill} flow-verified overflow config(s) into the pool.");
                    }
                    bool accepted = sourceRun == -1
                        ? flowPassed.Count >= ConfigSelectionPolicy.PreferredConnectedConfigs
                        : flowPassed.Count >= ConfigSelectionPolicy.MinimumConnectedConfigs;
                    int nextSource = sourceIndex + 1;
                    string sourceStatus = accepted
                        ? (sourceRun == -1 ? "cache target reached" : "source target reached")
                        : nextSource >= WorkerSourceCount
                            ? "all sources exhausted"
                            : sourceRun == -1
                                ? $"need {ConfigSelectionPolicy.PreferredConnectedConfigs - flowPassed.Count} more flowing - moving to source {nextSource}"
                                : $"need {ConfigSelectionPolicy.MinimumConnectedConfigs - flowPassed.Count} more flowing - moving to next source";
                    CrimsonX.Services.SimpleLogger.Log(
                        $"[Connect] Source {sourceRunLabel} done in {sourceWatch.Elapsed.TotalSeconds:0.0}s: " +
                        $"tested {tested}, ping {ping}, flow-passed {flowed}, selected {flowPassedHere}, " +
                        $"dropped {noFlow} no-flow, {udpBlocked} UDP-blocked, {blocked} blocked, {duplicatesHere} duplicate-config. {sourceStatus}");
                    if (pending.Count == 0)
                    {
                        sourceIndex++;
                        if (sourceIndex >= WorkerSourceCount) sourcesExhausted = true;
                    }
                    if (accepted) break;
                }

                if (flowPassed.Count < ConfigSelectionPolicy.MinimumConnectedConfigs)
                {
                    CrimsonX.Services.SimpleLogger.Log(
                        $"[Connect] Flow discovery finished: only {flowPassed.Count} config(s) carried payload - " +
                        $"need {ConfigSelectionPolicy.MinimumConnectedConfigs}. Failing.");
                    FlushDroppedCache();
                    throw new NoFlowingConfigException(CrimsonX.Localization.AppStrings.ToastNoFlowingConfig);
                }

                var speedCandidates = flowPassed.Where(r => !r.SpeedTested).ToList();
                if (speedCandidates.Count > 0)
                {
                    CrimsonX.Services.SimpleLogger.Log(
                        $"[Connect] Flow discovery finished: {flowPassed.Count} config(s) passed ping and flow - running the speed test.");
                    await SpeedTestVerifiedAsync(speedCandidates, "final speed test");
                }

                flowPassed.RemoveAll(r => r.SpeedTested && !r.SpeedResponded);
                if (ConfigSelectionPolicy.CanConnect(measured.Count, fallback.Count))
                    break;

                int retryOverflowFill = FillFlowFromOverflow();
                if (retryOverflowFill > 0)
                {
                    CrimsonX.Services.SimpleLogger.Log(
                        $"[Connect] Filled {retryOverflowFill} overflow config(s) into the retry pool.");
                }
                if (pending.Count == 0
                    && sourcesExhausted
                    && overflowCandidates.Count == 0
                    && flowPassed.All(r => r.SpeedTested))
                {
                    CrimsonX.Services.SimpleLogger.Log(
                        $"[Connect] Speed test finished: only {measured.Count} measured + {fallback.Count} fallback - " +
                        $"need {ConfigSelectionPolicy.MinimumConnectedConfigs}. Failing.");
                    FlushDroppedCache();
                    throw new NoFlowingConfigException(CrimsonX.Localization.AppStrings.ToastNoFlowingConfig);
                }
            }

            var finalConfigs = ConfigTester.RankVerifiedConfigs(measured.Concat(fallback));
            var workingJson = finalConfigs.Select(x => x.OutboundJson)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (workingJson.Count < ConfigSelectionPolicy.MinimumConnectedConfigs)
            {
                CrimsonX.Services.SimpleLogger.Log(
                    $"[Connect] Only {workingJson.Count} distinct payload-carrying outbound(s) remained - failing.");
                FlushDroppedCache();
                throw new NoFlowingConfigException(CrimsonX.Localization.AppStrings.ToastNoFlowingConfig);
            }

            var topConfigs = workingJson.Take(2).ToList();
            int topFallback = finalConfigs.Take(2).Count(x => x.Speed <= 0);
            if (topFallback > 0)
            {
                CrimsonX.Services.SimpleLogger.Log(
                    $"[Connect] WARNING: {topFallback} of the top 2 has no measured speed; " +
                    "using a flow-verified fallback as last resort.");
            }
            CrimsonX.Services.SimpleLogger.Log(
                $"[Connect] Using {finalConfigs.Count} verified config(s): {measured.Count} measured / {fallback.Count} fallback. " +
                $"Top 2 selected; reserve {Math.Max(0, workingJson.Count - 2)}.");

            var leftover = new List<string>();
            while (pending.Count > 0) leftover.Add(pending.Dequeue());
            foreach (var r in overflowCandidates)
            {
                if (!string.IsNullOrWhiteSpace(r.Link)) leftover.Add(r.Link);
            }
            _untestedConfigs = new ConcurrentQueue<string>(leftover.Distinct(StringComparer.Ordinal));
            _backgroundSeedSourceIndex = Math.Min(Math.Max(0, sourceIndex), WorkerSourceCount);
            Dispatcher.UIThread.Post(() => SetConnectButtonProgress(ConnectProgress.EngineStart));
            lock (_reservePool)
            {
                _reservePool.Clear();
                _reservePool.AddRange(workingJson.Skip(2));
            }
            FlushDroppedCache();
            var cleanWorkingJson = workingJson.Where(c => !_customOutboundJsons.Contains(c)).ToList();
            CrimsonX.Services.ConfigCache.SaveCache(GetAppPath(@"Data\cache\cache.bin"), cleanWorkingJson);
            if (!await Task.Run(() => XrayPipelineManager.StartXray(topConfigs, _cfg, _cfg.XrayDir)))
            {
                throw new Exception("Xray process failed to start.");
            }
            bool chainWanted = CrimsonX.Services.ExitNodeChain.ShouldChain(_cfg);
            if (_cfg.LastXrayMode == "VPN Mode" || chainWanted)
            {
                Dispatcher.UIThread.Post(() => SetConnectButtonProgress(ConnectProgress.ExitNode));
                await PrepareExitNodeCredentialsAsync();
                var sbPid = await StartSingBoxAsync(ct);
                if (sbPid == null)
                {
                    if (_cfg.LastXrayMode == "VPN Mode") throw new Exception("Singbox failed to start");
                    CrimsonX.Services.SimpleLogger.Log("[ExitNode] The chain did not start here; the system proxy stays on xray.");
                }
                else
                {
                    _sbPid = sbPid;
                }
            }
            if (_cfg.LastXrayMode != "VPN Mode") ApplySystemProxyForMode();
            _state.IsConnected = true;
            _state.SessionStartTime = DateTime.Now;
            CrimsonX.Services.SimpleLogger.Log("[Connect] Connected successfully with Dynamic Configs.");
            StartGeoPing();
            Dispatcher.UIThread.Post(() => {
                SetConnectButtonProgress(ConnectProgress.Connected);
                UpdateLocalPortUI();
                UpdateLanPortUI();
            });
            StartSessionClock();
            StartStatsPolling();
            CrimsonX.Services.BackgroundTask.Run("connect testing", () => StartBackgroundTestingLoop(ct));
            CrimsonX.Services.BackgroundTask.Run("connect refresh", () => StartRefreshTimer(ct));
        }

    // ── Worker Config Fetching ──

        private async Task<string?> ScrapeFastestConfigAsync(CancellationToken ct)
        {
            List<CrimsonX.Services.ConfigTestResult> passedScraped = new List<CrimsonX.Services.ConfigTestResult>();
            int si = -1;
            while (si < WorkerSourceCount && passedScraped.Count < 4)
            {
                ct.ThrowIfCancellationRequested();
                var configs = si == -1 ? CrimsonX.Services.ConfigCache.LoadCache(GetAppPath(@"Data\cache\cache.bin")) : await FetchConfigsFromWorker(si, ct);
                if (si == -1 && configs != null)
                {
                    configs = configs.Where(c => !CrimsonX.Services.XrayLinkParser.IsGrpcOutbound(c)).ToList();
                }
                if (configs == null || configs.Count == 0) { si++; continue; }
                if (!string.IsNullOrWhiteSpace(_cfg.CustomConfig1) && CrimsonX.Services.XrayLinkParser.TryParseCustomConfig(_cfg.CustomConfig1, out string c1Json))
                {
                    configs.RemoveAll(c => c == c1Json || c.Contains(c1Json));
                }
                if (!string.IsNullOrWhiteSpace(_cfg.CustomConfig2) && CrimsonX.Services.XrayLinkParser.TryParseCustomConfig(_cfg.CustomConfig2, out string c2Json))
                {
                    configs.RemoveAll(c => c == c2Json || c.Contains(c2Json));
                }
                if (configs.Count == 0) { si++; continue; }
                var q = new System.Collections.Concurrent.ConcurrentQueue<string>(configs);
                var tasks = new List<Task<CrimsonX.Services.ConfigTestResult>>();
                while (passedScraped.Count < 4 && q.TryDequeue(out string cfg))
                {
                    tasks.Add(CrimsonX.Services.ConfigTester.TestConfigAsync(cfg, _cfg, ct, fetchGeo: true));
                    if (tasks.Count >= 5 || q.IsEmpty)
                    {
                        var results = await Task.WhenAll(tasks);
                        tasks.Clear();
                        foreach (var r in results)
                        {
                            if (r.Success && r.UdpOk)
                            {
                                if (!IsConfigAllowed(r)) continue;
                                if (passedScraped.Count < 4) passedScraped.Add(r);
                            }
                        }
                    }
                }
                si++;
            }
            if (passedScraped.Count == 0) return null;
            Dispatcher.UIThread.Post(() => SetConnectButtonProgress(60));
            var customSpeedTasks = passedScraped.Select(async r =>
            {
                var speed = await CrimsonX.Services.ConfigTester.TestSpeedStabilityAsync(
                    r.OutboundJson, _cfg, ct, preferredTarget: null, verifyFlow: false);
                CrimsonX.Services.ConfigTester.ApplySpeedResult(r, speed);
                return r;
            }).ToList();
            var speedResults = await Task.WhenAll(customSpeedTasks);
            var fastest = CrimsonX.Services.ConfigTester.RankForConnection(speedResults).FirstOrDefault();
            return fastest?.OutboundJson;
        }

        private static List<string> ShuffledDistinct(IEnumerable<string> items)
        {
            var list = items.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.Ordinal).ToList();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }

        private static List<string> ShuffledDistinctConfigs(IEnumerable<string> items, HashSet<string> seen)
        {
            var list = new List<string>();
            foreach (string item in items)
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                string identity = XrayLinkParser.ConfigIdentity(item);
                if (identity.Length == 0) identity = item.Trim();
                if (!seen.Add(identity)) continue;
                list.Add(item);
            }
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }

        private async Task<List<string>> FetchConfigsFromWorker(int index, CancellationToken ct)
        {
            string[] workers = CrimsonX.Services.AppSecrets.WorkerUrls;
            for (int wi = 0; wi < workers.Length; wi++)
            {
                string worker = workers[wi];
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
                            CrimsonX.Services.SimpleLogger.Log($"[Fetch] API worker#{wi}/api/{index} returned {(int)apiResp.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        CrimsonX.Services.SimpleLogger.Log($"[Fetch] API worker#{wi}/api/{index} error: {ex.Message}");
                    }
                    string shaPath = GetAppPath($@"Data\cache\worker_sha_{index}.bin");
                    string dataPath = GetAppPath($@"Data\cache\worker_data_{index}.bin");
                    if (!string.IsNullOrEmpty(newSha) && File.Exists(shaPath) && File.Exists(dataPath))
                    {
                        string? oldSha = CrimsonX.Services.ConfigCache.LoadString(shaPath);
                        if (oldSha == newSha)
                        {
                            string? cachedContent = CrimsonX.Services.ConfigCache.LoadString(dataPath);
                            if (!string.IsNullOrEmpty(cachedContent))
                            {
                                var cachedConfigs = XrayLinkParser.ExtractConfigs(cachedContent);
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
                        CrimsonX.Services.SimpleLogger.Log($"[Fetch] worker#{wi}/{index} schemes: {XrayLinkParser.DescribeSchemes(content)}");
                        var configs = XrayLinkParser.ExtractConfigs(content);
                        if (configs.Count > 0) return configs;
                    }
                    else
                    {
                        CrimsonX.Services.SimpleLogger.Log($"[Fetch] Data worker#{wi}/{index} returned {(int)resp.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    CrimsonX.Services.SimpleLogger.LogFirst($"fetch-error|{ex.Message}", $"[Fetch] Data worker#{wi}/{index} error: {ex.Message}");
                }
            }
            return new List<string>();
        }

    // ── Background Testing Loop ──

        private async Task StartBackgroundTestingLoop(CancellationToken ct)
        {
            try
            {
                int sourceIndex = _backgroundSeedSourceIndex;
                while (!ct.IsCancellationRequested && _state.IsConnected)
                {
                    if (_cfg.DisableBackgroundChecks)
                    {
                        await Task.Delay(5000, ct);
                        continue;
                    }
                    if (_untestedConfigs.IsEmpty && sourceIndex < WorkerSourceCount)
                    {
                        try
                        {
                            var newConfigs = await Task.Run(() => FetchConfigsFromWorker(sourceIndex, ct), ct);
                            foreach (var c in ShuffledDistinct(newConfigs))
                            {
                                _untestedConfigs.Enqueue(c);
                            }
                        }
                        catch { }
                        sourceIndex++;
                    }
                    if (_untestedConfigs.TryDequeue(out string cfg))
                    {
                        var res = await Task.Run(
                            () => ConfigTester.TestConfigAsync(cfg, _cfg, ct, fetchGeo: true, verifyFlow: true), ct);
                        if (res.Success && res.UdpOk && res.Flowed && IsConfigAllowed(res))
                        {
                            lock (_reservePool)
                            {
                                if (!_reservePool.Contains(res.OutboundJson))
                                {
                                    _reservePool.Add(res.OutboundJson);
                                    var allWorking = new List<string>(XrayPipelineManager.ActiveOutbounds);
                                    allWorking.AddRange(_reservePool);
                                    var cleanWorking = allWorking.Where(c => !_customOutboundJsons.Contains(c)).ToList();
                                    CrimsonX.Services.ConfigCache.SaveCache(GetAppPath(@"Data\cache\cache.bin"), cleanWorking);
                                }
                            }
                        }
                        else if (!res.Flowed && !string.IsNullOrWhiteSpace(res.OutboundJson))
                        {
                            CrimsonX.Services.ConfigCache.RemoveFromCache(GetAppPath(@"Data\cache\cache.bin"), res.OutboundJson);
                            lock (_reservePool) { _reservePool.Remove(res.OutboundJson); }
                        }
                        await Task.Delay(5000, ct);
                    }
                    else
                    {
                        await Task.Delay(5000, ct);
                    }
                }
            }
            catch { }
        }

    // ── Periodic Refresh & Seamless Swap ──

        private static bool HasExited(global::System.Diagnostics.Process process)
        {
            try { return process.HasExited; } catch { return true; }
        }

        private static int ExitCodeOf(global::System.Diagnostics.Process process)
        {
            try { return process.ExitCode; } catch { return -1; }
        }

        // ── Exit node (an OpenVPN / WireGuard config chained inside the sing-box instance) ──

        private const int ExitNodeTimeoutSeconds = 25;

        private async Task PrepareExitNodeCredentialsAsync()
        {
            if (!CrimsonX.Services.ExitNodeChain.ShouldChain(_cfg)) return;
            if (!CrimsonX.Services.TunnelConfigParser.TryParse(_cfg.V2rayChainJson, out var tunnel) || tunnel?.Endpoint == null)
                return;
            if (await CrimsonX.Services.TunnelCredentialResolver.ApplyAsync(tunnel)) return;
            CrimsonX.Services.SimpleLogger.Log("[ExitNode] No OpenVPN credentials were provided; the exit node is skipped.");
            Dispatcher.UIThread.Post(() => ShowToast(CrimsonX.Localization.AppStrings.ToastTunnelNeedsCredentials, ToastKind.Error));
        }

        private async Task<bool> AwaitExitNodeEstablishedAsync(global::System.Diagnostics.Process? singBox, CancellationToken ct, int timeoutSeconds = ExitNodeTimeoutSeconds)
        {
            if (!CrimsonX.Services.ExitNodeChain.ShouldChain(_cfg)) return true;
            bool auth = _cfg.AllowLanConnections
                && _cfg.EnableLanAuth
                && !string.IsNullOrWhiteSpace(_cfg.LanAuthUsername)
                && !string.IsNullOrWhiteSpace(_cfg.LanAuthPassword);
            var started   = DateTime.UtcNow;
            var lastToast = started;
            bool loggedFailure = false;
            while (!ct.IsCancellationRequested)
            {
                var probe = await CrimsonX.Services.TunnelHopProbe.ProbeAsync(
                    CrimsonX.Services.ExitNodeChain.ProxyPort, 4000, ct,
                    auth ? _cfg.LanAuthUsername : "", auth ? _cfg.LanAuthPassword : "");
                if (probe.Ok)
                {
                    CrimsonX.Services.SimpleLogger.Log(
                        $"[ExitNode] established after {(int)(DateTime.UtcNow - started).TotalMilliseconds} ms (hop reply in {probe.Ms} ms)");
                    return true;
                }
                if (!loggedFailure)
                {
                    loggedFailure = true;
                    CrimsonX.Services.SimpleLogger.Log($"[ExitNode] the first probe through the chain failed: {probe.Error}");
                }
                if (ct.IsCancellationRequested)
                {
                    CrimsonX.Services.SimpleLogger.Log("[ExitNode] the connect was cancelled while the exit node was starting.");
                    return false;
                }
                if (singBox != null && HasExited(singBox))
                {
                    CrimsonX.Services.SimpleLogger.Log(
                        $"[ExitNode] the sing-box instance exited (code {ExitCodeOf(singBox)}) while the exit node was chained - a disconnect or a newer connect may have replaced it; see the [sing-box.exe] lines above.");
                    return false;
                }
                if ((DateTime.UtcNow - started).TotalSeconds >= timeoutSeconds)
                {
                    CrimsonX.Services.SimpleLogger.Log($"[ExitNode] the exit node did not come up: {probe.Error}");
                    return false;
                }
                if ((DateTime.UtcNow - lastToast).TotalSeconds >= 15)
                {
                    lastToast = DateTime.UtcNow;
                    Dispatcher.UIThread.Post(() => ShowToast(CrimsonX.Localization.AppStrings.ToastStillConnecting));
                }
                try { await Task.Delay(750, ct); } catch { break; }
            }
            return false;
        }

        private async Task<int?> StartSingBoxAsync(CancellationToken ct)
            => await StartOrRestartSingBoxVerifiedAsync(killRunning: false, ExitNodeTimeoutSeconds, ct) ? _sbPid : null;

        private async Task AwaitTunnelEstablishedAsync(
            List<(int Slot, CrimsonX.Services.TunnelParseResult Tunnel, string AdapterName, string AdapterIp)> tunnels,
            CancellationToken ct)
    {
        var pending = new List<(CrimsonX.Services.TunnelParseResult Tunnel, string AdapterName)>();
        foreach (var item in tunnels)
        {
            int? port = CrimsonX.Services.TunnelEngine.PortFor(CrimsonX.Services.TunnelEngine.GroupCustom,
                CrimsonX.Services.TunnelConfigParser.KeyOf(item.Tunnel.Raw, item.AdapterName));
            if (port != null) pending.Add((item.Tunnel, item.AdapterName));
        }
        if (pending.Count == 0) return;
        var started = DateTime.UtcNow;
        var lastToast = started;
        while (pending.Count > 0 && !ct.IsCancellationRequested)
        {
            foreach (var item in pending.ToList())
            {
                int? port = CrimsonX.Services.TunnelEngine.PortFor(CrimsonX.Services.TunnelEngine.GroupCustom,
                    CrimsonX.Services.TunnelConfigParser.KeyOf(item.Tunnel.Raw, item.AdapterName));
                if (port == null) continue;
                var probe = await CrimsonX.Services.TunnelHopProbe.ProbeAsync(port.Value, 4000, ct);
                if (!probe.Ok) continue;
                CrimsonX.Services.SimpleLogger.Log($"[Tunnel] established '{item.Tunnel.Label}' via 127.0.0.1:{port.Value} after {(int)(DateTime.UtcNow - started).TotalMilliseconds} ms (hop reply in {probe.Ms} ms)");
                pending.Remove(item);
            }
            if (pending.Count == 0) break;
            if ((DateTime.UtcNow - lastToast).TotalSeconds >= 15)
            {
                lastToast = DateTime.UtcNow;
                Dispatcher.UIThread.Post(() => ShowToast(CrimsonX.Localization.AppStrings.ToastStillConnecting));
            }
            try { await Task.Delay(750, ct); }
            catch { break; }
        }
    }

    private async Task StartRefreshTimer(CancellationToken ct)
    {
            try
            {
                string[] lastShas = new string[WorkerSourceCount];
                string[] workers = CrimsonX.Services.AppSecrets.WorkerUrls;
                while (!ct.IsCancellationRequested && _state.IsConnected)
                {
                    await Task.Delay(TimeSpan.FromHours(1), ct);
                    if (_cfg.DisableRefreshTimer) continue;
                    bool apiSuccess = false;
                    try
                    {
                        for (int i = 0; i < WorkerSourceCount; i++)
                        {
                            ct.ThrowIfCancellationRequested();
                            string newSha = null;
                            for (int wi = 0; wi < workers.Length; wi++)
                            {
                                string workerUrl = workers[wi];
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
                                    CrimsonX.Services.SimpleLogger.Log($"[RefreshTimer] SHA check failed for worker#{wi}/api/{i}: {ex.Message}");
                                }
                            }
                            if (newSha != null && newSha != lastShas[i])
                            {
                                lastShas[i] = newSha;
                                var newConfigs = await Task.Run(() => FetchConfigsFromWorker(i, ct), ct);
                                foreach (var c in ShuffledDistinct(newConfigs)) _untestedConfigs.Enqueue(c);
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
                            var res = await Task.Run(
                                () => ConfigTester.TestConfigAsync(cfgStr, _cfg, ct, isWatchdog: true, fetchGeo: true, isActiveWatchdog: true, verifyFlow: true), ct);
                            bool isBlocked = !IsConfigAllowed(res);
                            if (!res.Success || !res.UdpOk || !res.Flowed || isBlocked)
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
                        if (!_cfg.FetchConfigWhenOneProvided && _customOutboundJsons.Count > 0)
                            needed = 0;
                        CrimsonX.Services.SimpleLogger.Log($"[RefreshTimer] Watchdog finished. {workingActive.Count} passed. Replacements needed: {needed}");
                        if (needed > 0)
                        {
                            CrimsonX.Services.SimpleLogger.Log($"[RefreshTimer] Initiating 5-by-5 batch test to find {needed} replacements...");
                            int targetPassedCount = (needed == 1) ? 4 : 5;
                            var candidatesToTest = new List<string>();
                            lock (_reservePool)
                            {
                                foreach (var c in _reservePool.Where(x => !workingActive.Contains(x)))
                                    candidatesToTest.Add(c);
                            }
                            while (_untestedConfigs.TryDequeue(out string c))
                            {
                                candidatesToTest.Add(c);
                            }
                            var configsToTest = new Queue<string>(ShuffledDistinct(candidatesToTest));
                            var passedConfigs = new List<ConfigTestResult>();
                            var testingTasks = new List<Task<ConfigTestResult>>();
                            while (passedConfigs.Count < targetPassedCount && configsToTest.TryDequeue(out string cfg))
                            {
                                ct.ThrowIfCancellationRequested();
                                testingTasks.Add(Task.Run(
                                    () => ConfigTester.TestConfigAsync(cfg, _cfg, ct, isWatchdog: true, fetchGeo: true, verifyFlow: true), ct));
                                if (testingTasks.Count >= 5 || configsToTest.Count == 0)
                                {
                                    var results = await Task.WhenAll(testingTasks);
                                    testingTasks.Clear();
                                    foreach (var r in results)
                                    {
                                        if (r.Success && r.UdpOk && r.Flowed)
                                        {
                                            if (!IsConfigAllowed(r)) continue;
                                            passedConfigs.Add(r);
                                        }
                                        else
                                        {
                                            string badLink = !string.IsNullOrWhiteSpace(r.OutboundJson) ? r.OutboundJson : (r.Link ?? "");
                                            if (badLink.Length > 0)
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
                                    var speed = await Task.Run(
                                        () => ConfigTester.TestSpeedStabilityAsync(cfgTest.OutboundJson, _cfg, ct), ct);
                                    ConfigTester.ApplySpeedResult(cfgTest, speed);
                                    return cfgTest;
                                });
                                var speedTestedConfigs = ConfigTester.RankVerifiedConfigs(await Task.WhenAll(speedTasks));
                                var replacements = speedTestedConfigs.Take(needed).Select(x => x.OutboundJson).ToList();
                                if (replacements.Count == 0)
                                {
                                    CrimsonX.Services.SimpleLogger.Log(
                                        $"[RefreshTimer] No flow-verified replacement was found; keeping the current {workingActive.Count} active config(s).");
                                }
                                else
                                {
                                    if (replacements.Count < needed)
                                    {
                                        CrimsonX.Services.SimpleLogger.Log(
                                            $"[RefreshTimer] Only {replacements.Count} of {needed} flow-verified replacement(s) were found; " +
                                            "swapping the available ones.");
                                    }
                                    var finalNewOutbounds = new List<string>(workingActive);
                                    finalNewOutbounds.AddRange(replacements);
                                    await XrayPipelineManager.SwapOutboundsAsync(finalNewOutbounds, _cfg, _cfg.XrayDir);
                                    lock (_reservePool)
                                    {
                                        foreach (var unused in speedTestedConfigs.Skip(replacements.Count))
                                        {
                                            if (!_reservePool.Contains(unused.OutboundJson))
                                                _reservePool.Add(unused.OutboundJson);
                                        }
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

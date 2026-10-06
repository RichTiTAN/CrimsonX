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
using System.Threading;
using System.Threading.Tasks;
using CrimsonX.Models;

namespace CrimsonX.Services
{
    public sealed class AppOptimizeResult
    {
        public string Raw { get; set; } = "";
        public string Label { get; set; } = "";
        public string OutboundJson { get; set; } = "";
        public long PingMs { get; set; }
        public int StabilityPercent { get; set; }
    }

    public static class AppOptimizerService
    {
        public const int WorkingGoal = 10;
        public const int MinimumUsable = 4;
        public const int PatientSources = 3;
        public const int ConnectingConcurrency = 5;
        public const int ConnectedConcurrency = 10;

        private const long PingToleranceMs = 20;
        private const int MinStabilityPercent = 90;
        private const int StabilityDurationMs = 10000;
        private const int StabilityIntervalMs = 500;

        public static async Task<AppOptimizeResult> RunAsync(AppConfig cfg, string adapterIp, int concurrency, IProgress<double> progress, CancellationToken ct)
        {
            int batch = Math.Max(1, concurrency);
            var main = MainWindow.Instance;
            if (main == null || cfg == null) return null;

            var collected = new List<ConfigTestResult>();
            var tried = new HashSet<string>(StringComparer.Ordinal);
            int sourceCount = Math.Max(1, main.ScanWorkerSourceCount + 1);
            int sourceIndex = -1;
            int sourcesRead = 0;
            bool exhausted = false;
            bool foundEnough = false;
            double last = 0;

            void Report(double value)
            {
                value = Math.Clamp(value, 0.0, 1.0);
                if (value <= last) return;
                last = value;
                try { progress?.Report(value); } catch { }
            }

            async Task<bool> FetchFromSourceAsync()
            {
                List<string> configs;
                try
                {
                    configs = await main.FetchScanConfigsAsync(sourceIndex, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    SimpleLogger.Log($"[Optimize] Source {sourceIndex} failed: {ex.Message}");
                    return false;
                }
                if (configs == null || configs.Count == 0) return false;
                configs = Shuffled(configs.Where(c => !SingboxCannotDial(c)));
                if (configs.Count == 0) return false;

                var queue = new ConcurrentQueue<string>(configs);
                var tasks = new List<Task<ConfigTestResult>>();
                int added = 0;
                while (added < WorkingGoal && queue.TryDequeue(out string link))
                {
                    ct.ThrowIfCancellationRequested();
                    tasks.Add(ConfigTester.TestUdpOnlyAsync(link, cfg, ct, adapterIp, measureHttpPing: false, stun: true, warmUpFirst: true));
                    if (tasks.Count < batch && !queue.IsEmpty) continue;
                    var results = await Task.WhenAll(tasks);
                    tasks.Clear();
                    foreach (var r in results)
                    {
                        if (r == null || !r.Success || !r.UdpOk) continue;
                        if (string.IsNullOrWhiteSpace(r.OutboundJson)) continue;
                        if (!main.IsConfigAllowedForScan(r)) continue;
                        if (SingboxCannotDial(r.Link) || SingboxCannotDial(r.OutboundJson)) continue;
                        if (collected.Any(f => string.Equals(f.OutboundJson, r.OutboundJson, StringComparison.Ordinal))) continue;
                        collected.Add(r);
                        added++;
                    }
                    Report(0.6 * Math.Min(1.0, (sourcesRead + Math.Min(1.0, added / (double)WorkingGoal)) / sourceCount));
                }
                return true;
            }

            while (!ct.IsCancellationRequested)
            {
                bool gathered = false;
                while (!ct.IsCancellationRequested)
                {
                    if (exhausted) break;
                    if (foundEnough && gathered) break;
                    if (!foundEnough && EnoughToTest(collected.Count, sourcesRead))
                    {
                        foundEnough = true;
                        break;
                    }
                    if (await FetchFromSourceAsync())
                    {
                        sourcesRead++;
                        gathered = true;
                    }
                    sourceIndex++;
                    if (sourceIndex >= main.ScanWorkerSourceCount) exhausted = true;
                }

                foreach (var candidate in collected.OrderBy(SortPing).ToList())
                {
                    if (ct.IsCancellationRequested) return null;
                    string key = candidate.OutboundJson;
                    if (string.IsNullOrEmpty(key) || !tried.Add(key)) continue;
                    var stability = await ConfigTester.TestUdpStabilityAsync(
                        key, cfg, ct, StabilityDurationMs, StabilityIntervalMs, null, adapterIp, stun: true);
                    Report(Math.Min(0.95, 0.6 + 0.35 * tried.Count / (double)Math.Max(1, collected.Count)));
                    if (!PassesStability(candidate, stability)) continue;
                    string raw = string.IsNullOrWhiteSpace(candidate.Link) ? candidate.OutboundJson : candidate.Link;
                    if (!AppRulesCanCarry(cfg, raw)) continue;
                    Report(1.0);
                    return BuildResult(candidate, stability);
                }

                if (exhausted) return null;
            }
            return null;
        }

        internal static bool SingboxCannotDial(string config)
        {
            if (string.IsNullOrWhiteSpace(config)) return false;
            string text = config.Trim();
            if (text.IndexOf("type=xhttp", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("type=splithttp", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!ConfigConverter.TrySingboxOutbound(text, out string json, out _, out _)) return false;
            try
            {
                var transport = (Newtonsoft.Json.Linq.JObject.Parse(json)["transport"] as Newtonsoft.Json.Linq.JObject)?["type"]?.ToString();
                return string.Equals(transport, "xhttp", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(transport, "splithttp", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static bool EnoughToTest(int collectedCount, int sourcesRead)
            => collectedCount >= WorkingGoal
               || (collectedCount >= MinimumUsable && sourcesRead >= PatientSources);

        internal static List<string> Shuffled(IEnumerable<string> items)
        {
            var list = (items ?? Enumerable.Empty<string>()).ToList();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }

        public static long SortPing(ConfigTestResult result)
            => result == null ? long.MaxValue : (result.UdpPing > 0 ? result.UdpPing : result.Ping);

        public static bool PassesStability(ConfigTestResult candidate, UdpStabilityResult stability)
        {
            if (candidate == null || stability == null || !stability.HasSamples || stability.Ok == 0) return false;
            int rate = (int)Math.Round(stability.SuccessRate * 100);
            if (rate < MinStabilityPercent) return false;
            long basePing = candidate.UdpPing > 0 ? candidate.UdpPing : candidate.Ping;
            if (basePing <= 0 || stability.AvgPingMs <= 0) return false;
            return stability.AvgPingMs <= basePing + PingToleranceMs;
        }

        internal static bool AppRulesCanCarry(AppConfig cfg, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            if (SingboxCannotDial(raw)) return false;
            if (!ConfigConverter.TrySingboxOutbound(raw, out string singboxJson, out _, out _)) return false;
            return SingboxConfigValidator.CheckOutbound(cfg?.SbDir ?? "", singboxJson);
        }

        public static AppOptimizeResult BuildResult(ConfigTestResult candidate, UdpStabilityResult stability)
        {
            string raw = string.IsNullOrWhiteSpace(candidate.Link) ? candidate.OutboundJson : candidate.Link;
            return new AppOptimizeResult
            {
                Raw = raw,
                Label = AppRulesSingboxBuilder.LabelOf(raw),
                OutboundJson = candidate.OutboundJson,
                PingMs = candidate.UdpPing > 0 ? candidate.UdpPing : candidate.Ping,
                StabilityPercent = (int)Math.Round(stability.SuccessRate * 100)
            };
        }
    }
}

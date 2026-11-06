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
using System.Threading;
using System.Threading.Tasks;

namespace CrimsonX.Services
{
    public static class TunnelHealthMonitor
    {
        internal const int HopTimeoutMs = 1500;

        internal const int WarmupRounds = 2;

        internal const int WarmupIntervalMs = 10000;

        internal const int SteadyIntervalMs = 30000;

        internal const int ConfirmAttempts = 5;

        internal const int ConfirmWindowMs = 10000;

        internal const int ConfirmTimeoutMs = 1500;

        internal const int RecoveringIntervalMs = 10000;

        internal static int ConfirmGapMs => ConfirmWindowMs / ConfirmAttempts;

        internal enum HopKind
        {
            Stock,

            Awg,

            Ipsec
        }

        internal sealed class Hop
        {
            public string Group = "";

            public string Key = "";

            public string Label = "";

            public int Port;

            public HopKind Kind = HopKind.Stock;

            public string Id => Group + "|" + Key + "|" + Port;
        }

        internal static int DelayFor(int warmupRoundsDone, bool recovering)
            => recovering                      ? RecoveringIntervalMs
             : warmupRoundsDone < WarmupRounds ? WarmupIntervalMs
                                               : SteadyIntervalMs;

        internal static List<Hop> Hops()
        {
            var hops = new List<Hop>();
            foreach (string group in new[] { TunnelEngine.GroupCustom, TunnelEngine.GroupRules })
            {
                foreach (var target in TunnelEngine.PlannedTargets(group))
                {
                    if (target == null || target.Port <= 0) continue;
                    string key = target.Key ?? "";
                    if (hops.Any(h => h.Group == group && h.Key == key && h.Port == target.Port)) continue;
                    hops.Add(new Hop
                    {
                        Group = group,
                        Key = key,
                        Label = target.Parsed?.Label ?? key,
                        Port = target.Port,
                        Kind = target.Parsed?.RequiresIpsec == true ? HopKind.Ipsec
                             : target.Parsed?.RequiresAwg == true   ? HopKind.Awg
                                                                    : HopKind.Stock
                    });
                }
            }
            return hops;
        }

        internal static async Task<bool> ProbeHopAsync(Hop hop, int timeoutMs, CancellationToken token)
        {
            if (hop == null || hop.Port <= 0) return false;
            var probe = await TunnelHopProbe.ProbeAsync(hop.Port, timeoutMs, token).ConfigureAwait(false);
            return probe.Ok;
        }

        internal static async Task<List<Hop>> ProbeAsync(IEnumerable<Hop> hops, CancellationToken token)
        {
            var list = (hops ?? Enumerable.Empty<Hop>()).ToList();
            if (list.Count == 0) return new List<Hop>();
            var results = await Task.WhenAll(list.Select(hop => ProbeHopAsync(hop, HopTimeoutMs, token))).ConfigureAwait(false);
            var failed = new List<Hop>();
            for (int i = 0; i < list.Count; i++)
            {
                if (!results[i]) failed.Add(list[i]);
            }
            return failed;
        }

        internal static async Task<bool> ConfirmAsync(
            Func<CancellationToken, Task<bool>> probe, CancellationToken token,
            int attempts = ConfirmAttempts, int gapMs = -1, int windowMs = ConfirmWindowMs)
        {
            if (probe == null) return false;
            if (gapMs < 0) gapMs = ConfirmGapMs;
            int count = Math.Max(1, attempts);
            var clock = Stopwatch.StartNew();
            for (int attempt = 0; attempt < count; attempt++)
            {
                if (token.IsCancellationRequested) return false;
                if (attempt > 0)
                {
                    int untilNext = attempt * gapMs - (int)clock.ElapsedMilliseconds;
                    if (untilNext > 0 && !await WaitAsync(untilNext, token).ConfigureAwait(false)) return false;
                }
                bool answered;
                try { answered = await probe(token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return false; }
                if (answered) return true;
                if (clock.ElapsedMilliseconds >= windowMs) break;
            }
            return false;
        }

        internal static async Task RunAsync(Func<bool> isConnected, Func<bool> isBusy,
            Action<Hop> onHopLost, Action onTunnelsAnswered, CancellationToken token)
        {
            var down = new Dictionary<string, Hop>(StringComparer.Ordinal);
            int warmupRounds = 0;
            bool recovering = false;
            while (!token.IsCancellationRequested)
            {
                if (!await WaitAsync(DelayFor(warmupRounds, recovering), token).ConfigureAwait(false)) return;
                if (!isConnected())
                {
                    down.Clear();
                    recovering = false;
                    warmupRounds = 0;
                    continue;
                }
                if (isBusy != null && isBusy()) continue;
                var hops = Hops();
                if (hops.Count == 0)
                {
                    if (down.Count == 0) continue;
                    down.Clear();
                    recovering = false;
                    onTunnelsAnswered?.Invoke();
                    continue;
                }
                if (!recovering) warmupRounds++;
                var failed = await ProbeAsync(hops, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;

                if (recovering)
                {
                    var failedIds = new HashSet<string>(failed.Select(h => h.Id), StringComparer.Ordinal);
                    foreach (var hop in hops)
                    {
                        if (!failedIds.Contains(hop.Id))
                        {
                            down.Remove(hop.Id);
                            continue;
                        }
                        if (down.ContainsKey(hop.Id)) continue;
                        down[hop.Id] = hop;
                        SimpleLogger.Log(
                            $"[Tunnel] ({hop.Group}) the hop '{hop.Label}' on 127.0.0.1:{hop.Port} is silent as well.");
                        onHopLost?.Invoke(hop);
                    }
                    if (down.Count > 0) continue;
                    recovering = false;
                    warmupRounds = WarmupRounds;
                    onTunnelsAnswered?.Invoke();
                    continue;
                }

                if (failed.Count == 0) continue;

                var confirmed = new List<Hop>();
                foreach (var hop in failed)
                {
                    if (await ConfirmAsync(ct => ProbeHopAsync(hop, ConfirmTimeoutMs, ct), token).ConfigureAwait(false))
                        continue;
                    confirmed.Add(hop);
                }
                if (confirmed.Count == 0)
                {
                    SimpleLogger.Log(
                        $"[Tunnel] ({failed[0].Group}) a probe round came back empty but the confirm window answered; the session is kept.");
                    continue;
                }
                recovering = true;
                foreach (var hop in confirmed)
                {
                    down[hop.Id] = hop;
                    SimpleLogger.Log(
                        $"[Tunnel] ({hop.Group}) the hop '{hop.Label}' on 127.0.0.1:{hop.Port} stayed silent through the confirm window.");
                    onHopLost?.Invoke(hop);
                }
            }
        }

        private static async Task<bool> WaitAsync(int milliseconds, CancellationToken token)
        {
            if (milliseconds <= 0) return !token.IsCancellationRequested;
            try { await Task.Delay(milliseconds, token).ConfigureAwait(false); }
            catch { return false; }
            return !token.IsCancellationRequested;
        }
    }
}
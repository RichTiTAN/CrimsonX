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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CrimsonX.Services
{
    public static class TunnelHealthMonitor
    {
        internal const int ProbeIntervalMs = 10000;

        internal const int FailureRetryMs = 2000;

        internal const int FailureThreshold = 2;

        internal const int HopTimeoutMs = 1500;

        internal sealed class Hop
        {
            public string Group = "";

            public string Key = "";

            public string Label = "";

            public int Port;

            public int Failures;

            public string Id => Group + "|" + Key + "|" + Port;
        }

        internal static bool ShouldRecover(int failures) => failures >= FailureThreshold;

        internal static List<Hop> Hops()
        {
            var hops = new List<Hop>();
            foreach (string group in new[] { TunnelEngine.GroupCustom, TunnelEngine.GroupRules })
            {
                foreach (var target in TunnelEngine.ActiveTargets(group))
                {
                    if (target == null || target.Port <= 0) continue;
                    string key = target.Key ?? "";
                    if (hops.Any(h => h.Group == group && h.Key == key && h.Port == target.Port)) continue;
                    hops.Add(new Hop { Group = group, Key = key, Label = target.Parsed?.Label ?? key, Port = target.Port });
                }
            }
            return hops;
        }

        internal static async Task<List<Hop>> ProbeAsync(IEnumerable<Hop> hops, CancellationToken token)
        {
            var list = (hops ?? Enumerable.Empty<Hop>()).ToList();
            if (list.Count == 0) return new List<Hop>();
            var results = await Task.WhenAll(list.Select(hop => TunnelHopProbe.ProbeAsync(hop.Port, HopTimeoutMs, token))).ConfigureAwait(false);
            var failed = new List<Hop>();
            for (int i = 0; i < list.Count; i++)
            {
                if (!results[i].Ok) failed.Add(list[i]);
            }
            return failed;
        }

        internal static Hop FirstToRecover(IEnumerable<Hop> hops, Func<string, int> failuresOf)
        {
            if (hops == null) return null;
            foreach (var hop in hops)
            {
                if (ShouldRecover(failuresOf?.Invoke(hop.Id) ?? 0)) return hop;
            }
            return null;
        }

        public static async Task RunAsync(Func<bool> isConnected, Func<bool> isBusy, Action<string> onHopLost, CancellationToken token)
        {
            var failures = new Dictionary<string, int>(StringComparer.Ordinal);
            int delay = ProbeIntervalMs;
            while (!token.IsCancellationRequested)
            {
                if (!await WaitAsync(delay, token).ConfigureAwait(false)) return;
                if (!isConnected() || isBusy())
                {
                    failures.Clear();
                    delay = ProbeIntervalMs;
                    continue;
                }
                var hops = Hops();
                if (hops.Count == 0)
                {
                    failures.Clear();
                    delay = ProbeIntervalMs;
                    continue;
                }
                var failed = await ProbeAsync(hops, token).ConfigureAwait(false);
                foreach (var hop in hops)
                {
                    if (failed.Contains(hop)) failures[hop.Id] = failures.TryGetValue(hop.Id, out int count) ? count + 1 : 1;
                    else failures.Remove(hop.Id);
                }
                delay = failed.Count > 0 ? FailureRetryMs : ProbeIntervalMs;
                var lost = FirstToRecover(hops, id => failures.TryGetValue(id, out int count) ? count : 0);
                if (lost == null) continue;
                failures.Remove(lost.Id);
                SimpleLogger.Log($"[Tunnel] ({lost.Group}) the hop '{lost.Label}' on 127.0.0.1:{lost.Port} stopped answering; recovering the group.");
                onHopLost?.Invoke(lost.Group);
            }
        }

        private static async Task<bool> WaitAsync(int milliseconds, CancellationToken token)
        {
            try { await Task.Delay(milliseconds, token).ConfigureAwait(false); }
            catch { return false; }
            return !token.IsCancellationRequested;
        }
    }
}
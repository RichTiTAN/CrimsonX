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
using System.Threading;
using System.Threading.Tasks;

namespace CrimsonX.Services
{
    public static class ExitNodeVerifier
    {
        internal const int BudgetMs = 10000;

        internal const int GapMs = 5000;

        internal const int ProbeTimeoutMs = 4000;

        internal static async Task<bool> TwoProbesAsync(
            Func<CancellationToken, Task<bool>> probe, int budgetMs, int gapMs, CancellationToken ct)
        {
            if (probe == null) return false;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(budgetMs);
            var clock = Stopwatch.StartNew();
            try { await probe(deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return false; }
            int untilGap = gapMs - (int)clock.ElapsedMilliseconds;
            if (untilGap > 0)
            {
                try { await Task.Delay(untilGap, deadline.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return false; }
            }
            try { return await probe(deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return false; }
        }

        public static Task<bool> VerifyAsync(int port, CancellationToken ct)
            => TwoProbesAsync(async token =>
            {
                var probe = await TunnelHopProbe.ProbeAsync(port, ProbeTimeoutMs, token).ConfigureAwait(false);
                return probe.Ok;
            }, BudgetMs, GapMs, ct);
    }
}

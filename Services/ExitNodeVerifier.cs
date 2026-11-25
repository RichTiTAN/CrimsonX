/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
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

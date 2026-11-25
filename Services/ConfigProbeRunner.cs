/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CrimsonX.Services
{
    public static class ConfigProbeRunner
    {
        public static async Task RunAsync(
            Queue<string> pending,
            int concurrency,
            Func<string, CancellationToken, Task<ConfigTestResult>> probe,
            Action<ConfigTestResult> onResult,
            Func<bool> shouldKeepScheduling,
            CancellationToken ct)
        {
            if (pending == null) throw new ArgumentNullException(nameof(pending));
            if (probe == null) throw new ArgumentNullException(nameof(probe));
            if (onResult == null) throw new ArgumentNullException(nameof(onResult));

            int limit = Math.Max(1, concurrency);
            var inFlight = new List<Task<ConfigTestResult>>();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                while (inFlight.Count < limit
                       && pending.Count > 0
                       && (shouldKeepScheduling == null || shouldKeepScheduling()))
                {
                    string item = pending.Dequeue();
                    inFlight.Add(probe(item, ct));
                }

                if (inFlight.Count == 0) break;

                var completed = await Task.WhenAny(inFlight).ConfigureAwait(false);
                inFlight.Remove(completed);

                ConfigTestResult result;
                try
                {
                    result = await completed.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    continue;
                }

                onResult(result);
            }
        }
    }
}

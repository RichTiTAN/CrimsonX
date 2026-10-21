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

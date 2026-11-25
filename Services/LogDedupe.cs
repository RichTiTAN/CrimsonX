/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;

namespace CrimsonX.Services
{
    public static class LogDedupe
    {
        public const int SummaryEvery = 25;

        public const int MaxKeys = 256;

        public static bool ShouldLog(string key, IDictionary<string, int> counters, out bool summary, out int repeats)
        {
            summary = false;
            repeats = 0;
            if (counters == null) return true;
            key = key ?? "";
            if (counters.Count >= MaxKeys && !counters.ContainsKey(key)) counters.Clear();
            if (!counters.TryGetValue(key, out int seen))
            {
                counters[key] = 1;
                return true;
            }
            seen++;
            counters[key] = seen;
            repeats = seen;
            if (seen % SummaryEvery != 0) return false;
            summary = true;
            return true;
        }
    }
}

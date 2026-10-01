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

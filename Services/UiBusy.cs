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

namespace CrimsonX.Services
{
    public static class UiBusy
    {
        public static string Current => _current;

        private const int SlowScopeMs = 200;

        private static string _current = "";

        public static IDisposable Scope(string name)
        {
            string previous = _current;
            _current = name;
            return new ScopeHandle(name, previous);
        }

        private sealed class ScopeHandle : IDisposable
        {
            private readonly string _name;
            private readonly string _previous;
            private readonly long _startedAt;
            private bool _done;

            public ScopeHandle(string name, string previous)
            {
                _name = name;
                _previous = previous;
                _startedAt = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                if (_done) return;
                _done = true;
                _current = _previous;
                if (!(MainWindow.Instance?.Config?.DebugMode ?? false)) return;
                long ms = (long)Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds;
                if (ms >= SlowScopeMs)
                    SimpleLogger.Log($"[UI] {_name} took {ms} ms on the interface thread");
            }
        }
    }
}

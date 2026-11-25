/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
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

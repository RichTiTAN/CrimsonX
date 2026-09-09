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
using System.Threading;
using System.Threading.Tasks;

namespace CrimsonX.Services
{
    public sealed class SessionManager : IDisposable
    {
        // ── State 
        private DateTime?                _startTime;
        private readonly object         _startLock = new object();
        private CancellationTokenSource? _cts;
        private bool                     _disposed;

        // ── Events 
        public event Action<string>? ElapsedTimeUpdated;

        // ── Public API 

        public void Start()
        {
            Stop(); // 
            lock (_startLock) { _startTime = DateTime.Now; }
            _cts       = new CancellationTokenSource();
            var token  = _cts.Token;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try { await Task.Delay(1000, token).ConfigureAwait(false); }
                    catch { break; }

                    if (token.IsCancellationRequested) break;

                    DateTime? start;
                    lock (_startLock) { start = _startTime; }
                    if (start == null) break;

                    var elapsed = DateTime.Now - start.Value;
                    ElapsedTimeUpdated?.Invoke(elapsed.ToString(@"hh\:mm\:ss"));
                }
            }, token);
        }

        public void Stop()
        {
            if (_cts == null) return;
            try { _cts.Cancel(); _cts.Dispose(); } catch { }
            _cts = null;
            lock (_startLock) { _startTime = null; }
        }

        public TimeSpan? GetElapsed()
        {
            lock (_startLock)
            {
                return _startTime.HasValue ? (DateTime.Now - _startTime.Value) : null;
            }
        }

        // ── IDisposable 
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}

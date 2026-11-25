/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
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
            CrimsonX.Services.BackgroundTask.Run("session clock", async () =>
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
            });
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

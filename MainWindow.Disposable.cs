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

namespace CrimsonX;

public partial class MainWindow : IDisposable
{
    private bool _disposed = false;

    // ── App Shutdown Cleanup ──

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (!disposing) return;

        // ── DispatcherTimers 
        StopTimer(ref _saveDebounceTimer);
        StopTimer(ref _xrayRestartTimer);
        StopTimer(ref _toastTimer);
        StopTimer(ref _logTimer);
        StopTimer(ref _logClearTimer);
        StopTimer(ref _autoBootTimer);

        StopTimer(ref _fillAnimTimer);
        StopTimer(ref _colorTimer);
 
        CancelAndDispose(ref _graphAnimCts);

        CancelAndDispose(ref _updateCts);

        // ── Services 
        _session.Dispose();

        _netDiag.Dispose();

        DisposeTrayIcon();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    // ── Cleanup Helpers ──

    private static void StopTimer(ref global::Avalonia.Threading.DispatcherTimer? timer)
    {
        timer?.Stop();
        timer = null;
    }

    private static void CancelAndDispose(ref System.Threading.CancellationTokenSource? cts)
    {
        if (cts == null) return;
        try { cts.Cancel(); } catch (Exception ex) { CrimsonX.Services.SimpleLogger.Log(ex); }
        try { cts.Dispose(); } catch (Exception ex) { CrimsonX.Services.SimpleLogger.Log(ex); }
        cts = null;
    }
}

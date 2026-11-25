/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
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
        StopTimer(ref _logClearTimer);
        StopTimer(ref _autoBootTimer);
        StopTimer(ref _fillAnimTimer);
        StopTimer(ref _colorTimer);
        StopTimer(ref _uiStallTimer);
        StopTimer(ref _graphAnimTimer);
        StopTimer(ref _starPromptTimer);
        CancelAndDispose(ref _updateCts);
        CancelAndDispose(ref _pipelineCts);

        // ── Tray widget window 
        try { _trayWidget?.Close(); } catch (Exception ex) { CrimsonX.Services.SimpleLogger.Log(ex); }
        _trayWidget = null;

        // ── Services 
        try { CrimsonX.Services.IpsecTunnelEngine.KillNow(); } catch (Exception ex) { CrimsonX.Services.SimpleLogger.Log(ex); }
        try { CrimsonX.Services.ProcessService.KillOrphanEngines(); } catch (Exception ex) { CrimsonX.Services.SimpleLogger.Log(ex); }
        _session.Dispose();
        _netDiag.Dispose();
        DisposeTrayIcon();

        // ── Release page/control singletons 
        Pages.SettingsPage.ClearInstance();
        Pages.SplitTunnelPage.ClearInstance();
        Pages.ThemesPage.ClearInstance();
        Pages.AboutPage.ClearInstance();
        Controls.AnimatedBackground.ClearInstance();
        Controls.QuickSettingsPanel.ClearInstance();
    }

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

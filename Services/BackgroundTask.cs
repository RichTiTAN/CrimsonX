/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace CrimsonX.Services
{
    public static class BackgroundTask
    {
        private const int OperationAborted = unchecked((int)0x800703E3);
        private const int Cancelled = unchecked((int)0x800704C7);
        private const int BrokenPipe = unchecked((int)0x8007006D);
        private const int NoData = unchecked((int)0x800700E8);

        private static readonly ConcurrentDictionary<string, byte> _reported = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, byte> _running = new(StringComparer.Ordinal);

        public static void Run(string site, Func<Task> work)
        {
            _ = Observe(site, work);
        }

        public static void RunOnUi(string site, Func<Task> work)
        {
            _ = ObserveOnUi(site, work);
        }

        public static void RunOnceOnUi(string site, Func<Task> work)
        {
            string key = site ?? "";
            if (!_running.TryAdd(key, 0)) return;
            _ = ObserveOnceOnUi(key, work);
        }

        public static void Report(string site, Exception ex)
        {
            if (!IsTeardownAbort(ex))
            {
                SimpleLogger.Log($"[Background] {site} failed: {ex}");
                return;
            }
            if (!(MainWindow.Instance?.Config?.DebugMode ?? false)) return;
            if (!_reported.TryAdd(site, 0)) return;
            SimpleLogger.Log($"[Background] {site}: an I/O read was aborted while the connection was being torn down ({Root(ex).Message})");
        }

        public static bool IsTeardownAbort(Exception ex)
        {
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                if (e is OperationCanceledException) return true;
                if (e is IOException io && (io.HResult == OperationAborted || io.HResult == Cancelled
                                         || io.HResult == BrokenPipe || io.HResult == NoData)) return true;
                if (e is SocketException se && se.SocketErrorCode is SocketError.OperationAborted
                                                              or SocketError.Interrupted
                                                              or SocketError.Shutdown) return true;
            }
            return false;
        }

        private static async Task Observe(string site, Func<Task> work)
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Report(site, ex);
            }
        }

        private static async Task ObserveOnUi(string site, Func<Task> work)
        {
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                Report(site, ex);
            }
        }

        private static async Task ObserveOnceOnUi(string site, Func<Task> work)
        {
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                Report(site, ex);
            }
            finally
            {
                _running.TryRemove(site, out _);
            }
        }

        private static Exception Root(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }
    }
}

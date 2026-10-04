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

        public static void Run(string site, Func<Task> work)
        {
            _ = Observe(site, work);
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

        private static Exception Root(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }
    }
}

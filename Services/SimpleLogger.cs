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
using System.IO;
using System.Threading.Tasks;
using System.Threading.Channels;

namespace CrimsonX.Services
{
    public static class SimpleLogger
    {
        private static readonly string LogFile = Path.Combine(AppContext.BaseDirectory, "Logs", "error.log");
        private static bool _dirCreated = false;
        
        private static readonly Channel<string> _logChannel = Channel.CreateUnbounded<string>();

        static SimpleLogger()
        {
            Task.Run(async () =>
            {
                try
                {
                    var reader = _logChannel.Reader;
                    while (await reader.WaitToReadAsync())
                    {
                        while (reader.TryRead(out var msg))
                        {
                            try
                            {
                                if (!_dirCreated)
                                {
                                    var dir = Path.GetDirectoryName(LogFile);
                                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                                    {
                                        Directory.CreateDirectory(dir);
                                    }
                                    _dirCreated = true;
                                    TrimLogFile();
                                }
                                File.AppendAllText(LogFile, msg);
                                
                                if (++_writeCount >= 50)
                                {
                                    _writeCount = 0;
                                    TrimLogFile();
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            });
        }

        private static int _writeCount = 0;

        private static void TrimLogFile()
        {
            try
            {
                if (!File.Exists(LogFile)) return;
                var lines = File.ReadAllLines(LogFile);
                if (lines.Length > 1000)
                {
                    var newLines = new string[1000];
                    Array.Copy(lines, lines.Length - 1000, newLines, 0, 1000);
                    File.WriteAllLines(LogFile, newLines);
                }
            }
            catch { }
        }

        public static bool EnableLogging { get; set; } = true;

        public static void Log(Exception ex)
        {
            if (!EnableLogging) return;
            try
            {
                string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n";
                _logChannel.Writer.TryWrite(msg);
            }
            catch { }
        }
        
        public static void Log(string message)
        {
            if (!EnableLogging) return;
            try
            {
                string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";
                _logChannel.Writer.TryWrite(msg);
            }
            catch { }
        }
    }
}

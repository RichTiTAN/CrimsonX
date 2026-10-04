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

namespace CrimsonX.Services
{
    public static class GeneratedArtifacts
    {
        private static readonly string[] Files =
        {
            @"Data\Xray\config.json",
            @"Data\Xray\temp_outbounds.json",
            @"Data\Xray\access.log",
            @"Data\Xray\error.log",
            @"Data\Xray\access.log.tmp",
            @"Data\sing_box\config.json"
        };

        private static readonly string[] Patterns =
        {
            @"Data\Xray\test_*.json",
            @"Data\sing_box\probe_*",
            @"Data\sing_box\tunnel_probe_*"
        };

        private static readonly string[] Trees =
        {
            @"Data\sing_box\tunnel"
        };

        public static void RunStartupCleanup()
        {
            Cleanup();
        }

        public static void Cleanup()
        {
            var owner = MainWindow.Instance;
            foreach (var relative in Files)
            {
                try
                {
                    string path = owner?.GetAppPath(relative) ?? relative;
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception ex) { SimpleLogger.Log(ex); }
            }
            foreach (var relative in Patterns)
            {
                try
                {
                    string path = owner?.GetAppPath(relative) ?? relative;
                    string? dir = Path.GetDirectoryName(path);
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                    foreach (var file in Directory.GetFiles(dir, Path.GetFileName(path)))
                    {
                        try { File.Delete(file); } catch (Exception ex) { SimpleLogger.Log(ex); }
                    }
                    foreach (var sub in Directory.GetDirectories(dir, Path.GetFileName(path)))
                    {
                        TryDeleteTree(sub);
                    }
                }
                catch (Exception ex) { SimpleLogger.Log(ex); }
            }
            foreach (var relative in Trees)
            {
                try
                {
                    string path = owner?.GetAppPath(relative) ?? relative;
                    if (Directory.Exists(path)) TryDeleteTree(path);
                }
                catch (Exception ex) { SimpleLogger.Log(ex); }
            }
        }

        private static void TryDeleteTree(string path)
        {
            try
            {
                Directory.Delete(path, true);
            }
            catch (Exception ex)
            {
                SimpleLogger.Log($"[Cleanup] could not remove {path}: {ex.Message}");
            }
        }
    }
}

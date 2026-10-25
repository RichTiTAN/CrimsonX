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

        private static readonly string[] StartupOnlyFiles =
        {
            "updater.bat"
        };

        private static readonly string[] StartupOnlyPatterns =
        {
            "update_temp*.zip",
            "update_extracted*"
        };

        public static void RunStartupCleanup()
        {
            Cleanup();
            if (PendingUpdateHandOff()) return;
            Purge(StartupOnlyFiles, StartupOnlyPatterns);
        }

        private static bool PendingUpdateHandOff()
        {
            try
            {
                var owner = MainWindow.Instance;
                string path = owner?.GetAppPath("updater.bat") ?? "updater.bat";
                if (!File.Exists(path)) return false;
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
                return age >= TimeSpan.Zero && age < TimeSpan.FromMinutes(5);
            }
            catch (Exception ex) { SimpleLogger.Log(ex); return false; }
        }

        public static void Cleanup()
        {
            Purge(Files, Patterns);
            var owner = MainWindow.Instance;
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

        public static bool TookFailedUpdateHandOff()
        {
            try
            {
                var owner = MainWindow.Instance;
                string path = owner?.GetAppPath("updater.log") ?? "updater.log";
                if (!File.Exists(path)) return false;
                bool finished = false;
                bool failed = false;
                foreach (string line in File.ReadAllLines(path))
                {
                    if (line.Contains("copy complete", StringComparison.OrdinalIgnoreCase)) finished = true;
                    else if (line.Contains("gave up", StringComparison.OrdinalIgnoreCase)) { finished = true; failed = true; }
                }
                if (!finished)
                {
                    if (PendingUpdateHandOff()) return false;
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromMinutes(5)) return false;
                }
                try { File.Delete(path); } catch (Exception ex) { SimpleLogger.Log(ex); }
                return finished && failed;
            }
            catch (Exception ex) { SimpleLogger.Log(ex); return false; }
        }

        private static void Purge(string[] files, string[] patterns)
        {
            var owner = MainWindow.Instance;
            foreach (var relative in files)
            {
                try
                {
                    string path = owner?.GetAppPath(relative) ?? relative;
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception ex) { SimpleLogger.Log(ex); }
            }
            foreach (var relative in patterns)
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

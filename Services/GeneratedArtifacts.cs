/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
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
            "updater.bat",
            @"Data\xray\LICENSE",
            @"Data\sing_box\LICENSE",
            @"Data\awg\LICENSE",
            @"Data\ipsec\THIRD-PARTY-NOTICES.txt",
            @"Data\xray\SOURCE.txt",
            @"Data\sing_box\SOURCE.txt",
            @"Data\awg\SOURCE.txt"
        };

        private static readonly string[] StartupOnlyPatterns =
        {
            "update_temp*.zip",
            "update_extracted*",
            @"Data\aether\LICENSES"
        };

        private static readonly string[] SymbolPatterns =
        {
            "*.pdb"
        };

        public static void RunStartupCleanup()
        {
            Cleanup();
            if (PendingUpdateHandOff()) return;
            Purge(StartupOnlyFiles, StartupOnlyPatterns);
            if (!InsideBuildOutput()) Purge(Array.Empty<string>(), SymbolPatterns);
        }

        private static bool InsideBuildOutput()
        {
            if (System.Diagnostics.Debugger.IsAttached) return true;
            string dir = AppContext.BaseDirectory ?? "";
            string marker = Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar;
            return dir.Contains(marker, StringComparison.OrdinalIgnoreCase);
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

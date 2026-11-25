/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using System.IO;
using Newtonsoft.Json;

namespace CrimsonX.Services
{
    public static class SecureJsonStore
    {
        public const string Folder = @"Data\cache";

        public static string PathFor(string fileName)
        {
            var dir = MainWindow.Instance?.GetAppPath(Folder) ?? Folder;
            return System.IO.Path.Combine(dir, fileName);
        }

        public static T Load<T>(string path) where T : class
        {
            try
            {
                string json = ConfigCache.LoadString(path);
                if (string.IsNullOrEmpty(json)) return null;
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                return null;
            }
        }

        public static bool Save<T>(string path, T value)
        {
            try
            {
                ConfigCache.SaveString(path, JsonConvert.SerializeObject(value));
                return true;
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                return false;
            }
        }

        public static void MigrateLegacyStores()
        {
            var appsDir = MainWindow.Instance?.GetAppPath(@"Data\Apps") ?? @"Data\Apps";
            AdoptPlaintextFile(System.IO.Path.Combine(appsDir, "custom_configs.json"),    PathFor("app_custom_configs.bin"));
            AdoptPlaintextFile(System.IO.Path.Combine(appsDir, "tunnel_credentials.json"), PathFor("tunnel_credentials.bin"));
            AdoptPlaintextFile(System.IO.Path.Combine(appsDir, "rules.json"),              PathFor("app_rules.bin"));
            TryRemoveEmptyFolder(appsDir);
        }

        public static void AdoptPlaintextFile(string legacyPath, string securePath)
        {
            try
            {
                if (string.IsNullOrEmpty(legacyPath) || !File.Exists(legacyPath)) return;
                string json = File.ReadAllText(legacyPath);
                if (!File.Exists(securePath) && !string.IsNullOrWhiteSpace(json))
                {
                    ConfigCache.SaveString(securePath, json);
                    SimpleLogger.Log($"[SecureStore] moved {System.IO.Path.GetFileName(legacyPath)} into {System.IO.Path.GetFileName(securePath)}");
                }
                File.Delete(legacyPath);
                SimpleLogger.Log($"[SecureStore] removed the plain text store {legacyPath}");
                TryRemoveEmptyFolder(System.IO.Path.GetDirectoryName(legacyPath));
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
        }

        private static void TryRemoveEmptyFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            try
            {
                if (!Directory.Exists(folder)) return;
                if (Directory.GetFileSystemEntries(folder).Length > 0) return;
                Directory.Delete(folder);
                SimpleLogger.Log($"[SecureStore] removed the empty folder {folder}");
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
        }
    }
}

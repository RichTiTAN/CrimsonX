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

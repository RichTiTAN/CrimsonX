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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace CrimsonX.Services
{
    public static class ConfigCache
    {
        private static readonly byte[] Key = Encoding.UTF8.GetBytes(AppSecrets.CacheKey); // 32 bytes
        private static readonly byte[] Iv = Encoding.UTF8.GetBytes(AppSecrets.CacheIv); // legacy fixed IV (decrypting older cache files)

        private static byte[] Encrypt(byte[] plainBytes)
        {
            using var aes = Aes.Create();
            aes.Key = Key;
            aes.GenerateIV();

            using var ms = new MemoryStream();
            ms.Write(aes.IV, 0, aes.IV.Length); 

            using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            {
                cs.Write(plainBytes, 0, plainBytes.Length);
                cs.FlushFinalBlock();
            }
            return ms.ToArray();
        }

        private static string? DecryptToString(byte[] data)
        {
            if (data.Length > 16)
            {
                try
                {
                    var iv = new byte[16];
                    Array.Copy(data, 0, iv, 0, iv.Length);
                    using var aes = Aes.Create();
                    aes.Key = Key;
                    aes.IV = iv;
                    using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                    using var ms = new MemoryStream(data, 16, data.Length - 16, false);
                    using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                    using var sr = new StreamReader(cs);
                    string json = sr.ReadToEnd();
                    if (!string.IsNullOrEmpty(json)) return json;
                }
                catch
                {
                }
            }

            try
            {
                using var aes = Aes.Create();
                aes.Key = Key;
                aes.IV = Iv;
                using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                using var ms = new MemoryStream(data);
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var sr = new StreamReader(cs);
                return sr.ReadToEnd();
            }
            catch { return null; }
        }

        public static List<string> LoadCache(string path)
        {
            if (!File.Exists(path)) return new List<string>();

            try
            {
                byte[] encrypted = File.ReadAllBytes(path);
                string? json = DecryptToString(encrypted);
                if (string.IsNullOrEmpty(json)) return new List<string>();

                var list = JsonConvert.DeserializeObject<List<string>>(json);
                return list ?? new List<string>();
            }
            catch (Exception ex)
            {
                SimpleLogger.Log($"Failed to load config cache: {ex.Message}");
                return new List<string>();
            }
        }

        public static void SaveCache(string path, List<string> newConfigs, bool overwrite = false)
        {
            try
            {
                var finalConfigs = newConfigs;
                
                if (!overwrite)
                {
                    var existing = LoadCache(path);
                    var merged = new List<string>();
                    merged.AddRange(newConfigs);
                    
                    foreach (var old in existing)
                    {
                        if (!merged.Contains(old))
                        {
                            merged.Add(old);
                        }
                    }
                    finalConfigs = merged.Take(20).ToList();
                }

                string json = JsonConvert.SerializeObject(finalConfigs);
                byte[] plainBytes = Encoding.UTF8.GetBytes(json);

                File.WriteAllBytes(path, Encrypt(plainBytes));
            }
            catch (Exception ex)
            {
                SimpleLogger.Log($"Failed to save config cache: {ex.Message}");
            }
        }

        public static void RemoveFromCache(string path, string configJson)
        {
            try
            {
                var existing = LoadCache(path);
                if (existing.Remove(configJson))
                {
                    SaveCache(path, existing, true);
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Log($"Failed to remove from config cache: {ex.Message}");
            }
        }

        public static string? LoadString(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                return DecryptToString(File.ReadAllBytes(path));
            }
            catch { return null; }
        }

        public static void SaveString(string path, string content)
        {
            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(content ?? "");

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllBytes(path, Encrypt(plainBytes));
            }
            catch { }
        }
        
        public static Dictionary<string, string> LoadIconCache(string path)
        {
            string? json = LoadString(path);
            if (string.IsNullOrEmpty(json)) return new Dictionary<string, string>();
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }

        public static void SaveIconCache(string path, Dictionary<string, string> cache)
        {
            SaveString(path, JsonConvert.SerializeObject(cache));
        }
    }
}

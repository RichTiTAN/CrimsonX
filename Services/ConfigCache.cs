/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
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
        public const int WorkingLimit = 50;

        private static readonly object Sync = new();

        // The connect flow reads the same encrypted cache several times; keep the
        // decrypted text/list with the file stamp so repeated loads avoid AES + JSON.
        private sealed class TextCacheEntry
        {
            public DateTime StampUtc;
            public long Length;
            public string Value = "";
        }

        private sealed class ListCacheEntry
        {
            public DateTime StampUtc;
            public long Length;
            public List<string> Value = new();
        }

        private const int TextCacheLimit = 32;
        private const int ListCacheLimit = 8;

        private static readonly Dictionary<string, TextCacheEntry> TextCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, ListCacheEntry> ListCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Queue<string> TextOrder = new();
        private static readonly Queue<string> ListOrder = new();

        private static bool IsCurrent(FileInfo info, DateTime stampUtc, long length)
            => info.Exists && info.LastWriteTimeUtc == stampUtc && info.Length == length;

        private static void RememberText(string path, FileInfo info, string value)
        {
            if (TextCache.ContainsKey(path))
            {
                TextCache[path] = new TextCacheEntry { StampUtc = info.LastWriteTimeUtc, Length = info.Length, Value = value };
                return;
            }
            TextCache[path] = new TextCacheEntry { StampUtc = info.LastWriteTimeUtc, Length = info.Length, Value = value };
            TextOrder.Enqueue(path);
            while (TextCache.Count > TextCacheLimit && TextOrder.Count > 0)
                TextCache.Remove(TextOrder.Dequeue());
        }

        private static void ForgetText(string path)
        {
            TextCache.Remove(path);
            if (TextOrder.Count > TextCacheLimit * 4)
            {
                TextOrder.Clear();
                foreach (string key in TextCache.Keys) TextOrder.Enqueue(key);
            }
        }

        private static void RememberList(string path, FileInfo info, List<string> value)
        {
            if (ListCache.ContainsKey(path))
            {
                ListCache[path] = new ListCacheEntry { StampUtc = info.LastWriteTimeUtc, Length = info.Length, Value = new List<string>(value) };
                return;
            }
            ListCache[path] = new ListCacheEntry { StampUtc = info.LastWriteTimeUtc, Length = info.Length, Value = new List<string>(value) };
            ListOrder.Enqueue(path);
            while (ListCache.Count > ListCacheLimit && ListOrder.Count > 0)
                ListCache.Remove(ListOrder.Dequeue());
        }

        private static void ForgetList(string path)
        {
            ListCache.Remove(path);
            if (ListOrder.Count > ListCacheLimit * 4)
            {
                ListOrder.Clear();
                foreach (string key in ListCache.Keys) ListOrder.Enqueue(key);
            }
        }

        private static byte[] Encrypt(byte[] plainBytes)
        {
            using var aes = Aes.Create();
            aes.Key = SecureDataKey.Current();
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

        private static string? TryDecrypt(byte[] data, byte[] key, bool perFileIv)
        {
            try
            {
                int offset = 0;
                byte[] iv = SecureDataKey.LegacyIv;
                if (perFileIv)
                {
                    if (data.Length <= 16) return null;
                    iv = new byte[16];
                    Array.Copy(data, 0, iv, 0, iv.Length);
                    offset = 16;
                }
                using var aes = Aes.Create();
                aes.Key = key;
                aes.IV = iv;
                using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                using var ms = new MemoryStream(data, offset, data.Length - offset, false);
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var sr = new StreamReader(cs);
                string json = sr.ReadToEnd();
                return string.IsNullOrEmpty(json) ? null : json;
            }
            catch
            {
                return null;
            }
        }

        private static string? DecryptBytes(string path, byte[] data)
        {
            string? json = TryDecrypt(data, SecureDataKey.Current(), true);
            if (json != null) return json;
            json = TryDecrypt(data, SecureDataKey.LegacyKey, true);
            if (json == null) json = TryDecrypt(data, SecureDataKey.LegacyKey, false);
            if (json != null) UpgradeFile(path, json);
            return json;
        }

        private static void UpgradeFile(string path, string json)
        {
            if (string.IsNullOrEmpty(path) || SecureDataKey.UsingLegacy()) return;
            try
            {
                WriteAtomic(path, Encrypt(Encoding.UTF8.GetBytes(json)));
                SimpleLogger.Log($"[Cache] {Path.GetFileName(path)} moved to the machine key.");
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
        }

        private static void WriteAtomic(string path, byte[] data)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, data);
                File.Move(temp, path, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                throw;
            }
        }

        public static List<string> LoadCache(string path)
        {
            lock (Sync)
            {
                if (!File.Exists(path))
                {
                    ForgetList(path);
                    ForgetText(path);
                    return new List<string>();
                }
                try
                {
                    var info = new FileInfo(path);
                    if (ListCache.TryGetValue(path, out var cached)
                        && IsCurrent(info, cached.StampUtc, cached.Length))
                    {
                        return new List<string>(cached.Value);
                    }

                    string? json = LoadString(path);
                    if (string.IsNullOrEmpty(json)) return new List<string>();
                    var list = JsonConvert.DeserializeObject<List<string>>(json);
                    var result = list ?? new List<string>();
                    RememberList(path, new FileInfo(path), result);
                    return new List<string>(result);
                }
                catch (Exception ex)
                {
                    SimpleLogger.Log($"Failed to load config cache: {ex.Message}");
                    return new List<string>();
                }
            }
        }

        public static int Count(string path)
        {
            lock (Sync)
            {
                return LoadCache(path).Count;
            }
        }

        public static void SaveCache(string path, List<string> newConfigs, bool overwrite = false)
        {
            lock (Sync)
            {
                try
                {
                    var finalConfigs = newConfigs;
                    if (!overwrite)
                    {
                        var existing = LoadCache(path);
                        var seen = new HashSet<string>(StringComparer.Ordinal);
                        var merged = new List<string>(newConfigs.Count + existing.Count);
                        foreach (var item in newConfigs)
                        {
                            if (seen.Add(item)) merged.Add(item);
                        }
                        foreach (var old in existing)
                        {
                            if (seen.Add(old)) merged.Add(old);
                        }
                        finalConfigs = merged.Take(WorkingLimit).ToList();
                    }
                    string json = JsonConvert.SerializeObject(finalConfigs);
                    byte[] plainBytes = Encoding.UTF8.GetBytes(json);
                    WriteAtomic(path, Encrypt(plainBytes));
                    var info = new FileInfo(path);
                    RememberText(path, info, json);
                    RememberList(path, info, finalConfigs);
                }
                catch (Exception ex)
                {
                    SimpleLogger.Log($"Failed to save config cache: {ex.Message}");
                }
            }
        }

        public static void RemoveFromCache(string path, string configJson)
        {
            lock (Sync)
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
        }

        public static string? LoadString(string path)
        {
            lock (Sync)
            {
                if (!File.Exists(path))
                {
                    ForgetText(path);
                    return null;
                }
                try
                {
                    var info = new FileInfo(path);
                    if (TextCache.TryGetValue(path, out var cached)
                        && IsCurrent(info, cached.StampUtc, cached.Length))
                    {
                        return cached.Value;
                    }
                    string? json = DecryptBytes(path, File.ReadAllBytes(path));
                    if (!string.IsNullOrEmpty(json))
                        RememberText(path, new FileInfo(path), json);
                    return json;
                }
                catch { return null; }
            }
        }

        public static void SaveString(string path, string content)
        {
            lock (Sync)
            {
                try
                {
                    byte[] plainBytes = Encoding.UTF8.GetBytes(content ?? "");
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    WriteAtomic(path, Encrypt(plainBytes));
                    var info = new FileInfo(path);
                    if (!string.IsNullOrEmpty(content))
                        RememberText(path, info, content);
                    else
                        ForgetText(path);
                    ForgetList(path);
                }
                catch { }
            }
        }
        
        public static Dictionary<string, string> LoadIconCache(string path)
        {
            string? json = LoadString(path);
            if (string.IsNullOrEmpty(json)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            return loaded == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
        }

        public static void SaveIconCache(string path, Dictionary<string, string> cache)
        {
            SaveString(path, JsonConvert.SerializeObject(cache));
        }
    }
}

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
using CrimsonX.Models;
using Newtonsoft.Json;

namespace CrimsonX.Services
{
    public sealed class AppCustomConfigEntry
    {
        public string Raw { get; set; } = "";
        public string Label { get; set; } = "";
        public bool LabelEdited { get; set; }
        public DateTime SavedUtc { get; set; }
    }

    public enum CustomConfigSaveResult
    {
        Saved,
        Updated,
        Invalid,
        PoolFull
    }

    public static class AppCustomConfigStore
    {
        public const int MaxEntries = 30;

        private static readonly object _lock = new object();
        private static List<AppCustomConfigEntry> _cache;
        private static DateTime _cacheStampUtc;
        private static long _cacheLength;

        private static string StorePath()
        {
            return SecureJsonStore.PathFor("app_custom_configs.bin");
        }

        private static string LegacyStorePath()
        {
            var baseDir = MainWindow.Instance?.GetAppPath("Data\\Apps") ?? "Data\\Apps";
            return Path.Combine(baseDir, "custom_configs.json");
        }
        public static List<AppCustomConfigEntry> Load(AppConfig cfg)
        {
            lock (_lock)
            {
                return LoadCore(cfg);
            }
        }

        private static List<AppCustomConfigEntry> LoadCore(AppConfig cfg)
        {
            var path = StorePath();
            SecureJsonStore.AdoptPlaintextFile(LegacyStorePath(), path);
            if (!File.Exists(path))
            {
                InvalidateCache();
                var seeded = SeedFromSettings(cfg);
                if (seeded.Count > 0) SaveCore(seeded);
                return seeded;
            }
            try
            {
                var info = new FileInfo(path);
                if (_cache != null && info.LastWriteTimeUtc == _cacheStampUtc && info.Length == _cacheLength)
                    return Clone(_cache);
                var json = ConfigCache.LoadString(path);
                if (string.IsNullOrEmpty(json))
                    throw new InvalidDataException($"The encrypted config pool at {Path.GetFileName(path)} could not be read.");
                var list = JsonConvert.DeserializeObject<List<AppCustomConfigEntry>>(json) ?? new List<AppCustomConfigEntry>();
                var loaded = list.Where(e => e != null && !string.IsNullOrWhiteSpace(e.Raw))
                                 .OrderByDescending(e => e.SavedUtc)
                                 .ToList();
                if (NormalizeLabels(loaded))
                {
                    SaveCore(loaded);
                    info = new FileInfo(path);
                }
                _cache         = loaded;
                _cacheStampUtc = info.LastWriteTimeUtc;
                _cacheLength   = info.Length;
                return Clone(loaded);
            }
            catch (Exception ex)
            {
                CrimsonX.Services.SimpleLogger.Log(ex);
                BackupCorruptStore(path);
                InvalidateCache();
                return new List<AppCustomConfigEntry>();
            }
        }

        private static bool NormalizeLabels(List<AppCustomConfigEntry> entries)
        {
            bool changed = false;
            foreach (var entry in entries)
            {
                if (entry == null || entry.LabelEdited) continue;
                string raw = entry.Raw ?? "";
                if (!LooksGeneratedLabel(entry.Label, raw))
                {
                    entry.LabelEdited = true;
                    changed = true;
                    continue;
                }
                string fresh = ConfigConverter.LabelFor(raw);
                if (fresh.Length == 0 || string.Equals(fresh, entry.Label, StringComparison.Ordinal)) continue;
                entry.Label = fresh;
                changed = true;
            }
            return changed;
        }

        private static bool LooksGeneratedLabel(string label, string raw)
        {
            if (string.IsNullOrWhiteSpace(label)) return true;
            if (string.Equals(label, SingboxLinkParser.LabelOf(raw), StringComparison.Ordinal)) return true;
            if (string.Equals(label, TunnelConfigParser.LabelOf(raw), StringComparison.Ordinal)) return true;
            if (label.StartsWith("xray", StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(label, "vless", StringComparison.OrdinalIgnoreCase)
                || string.Equals(label, "vmess", StringComparison.OrdinalIgnoreCase)
                || string.Equals(label, "trojan", StringComparison.OrdinalIgnoreCase)
                || string.Equals(label, "shadowsocks", StringComparison.OrdinalIgnoreCase)
                || string.Equals(label, "hysteria2", StringComparison.OrdinalIgnoreCase)
                || string.Equals(label, "tuic", StringComparison.OrdinalIgnoreCase)
                || string.Equals(label, "wireguard", StringComparison.OrdinalIgnoreCase)
                || string.Equals(label, "openvpn", StringComparison.OrdinalIgnoreCase);
        }

        internal static List<AppCustomConfigEntry> Snapshot(AppConfig cfg)
        {
            lock (_lock)
            {
                try
                {
                    var path = StorePath();
                    if (!File.Exists(path)) return new List<AppCustomConfigEntry>();
                    var info = new FileInfo(path);
                    if (_cache != null && info.LastWriteTimeUtc == _cacheStampUtc && info.Length == _cacheLength)
                        return Clone(_cache);
                    var json = ConfigCache.LoadString(path);
                    if (string.IsNullOrEmpty(json)) return new List<AppCustomConfigEntry>();
                    var list = JsonConvert.DeserializeObject<List<AppCustomConfigEntry>>(json) ?? new List<AppCustomConfigEntry>();
                    return list.Where(e => e != null && !string.IsNullOrWhiteSpace(e.Raw)).ToList();
                }
                catch (Exception ex)
                {
                    CrimsonX.Services.SimpleLogger.Log(ex);
                    return new List<AppCustomConfigEntry>();
                }
            }
        }

        public static void Save(List<AppCustomConfigEntry> entries)
        {
            lock (_lock)
            {
                SaveCore(entries);
            }
        }

        private static void SaveCore(List<AppCustomConfigEntry> entries)
        {
            try
            {
                var path = StorePath();
                if (!SecureJsonStore.Save(path, entries)) throw new IOException($"Could not write {Path.GetFileName(path)}");
                RefreshCache(entries, path);
            }
            catch (Exception ex)
            {
                CrimsonX.Services.SimpleLogger.Log(ex);
                InvalidateCache();
            }
        }

        public static bool CanStore(string raw)
            => !string.IsNullOrWhiteSpace(raw)
               && TunnelConfigParser.TryParse(raw.Trim(), out var tunnel) && tunnel.Success;

        public static CustomConfigSaveResult Store(AppConfig cfg, string raw, out string label)
            => Store(cfg, raw, out label, out _);

        public static CustomConfigSaveResult Store(AppConfig cfg, string raw, out string label, out string reason)
        {
            label  = "";
            reason = "";
            if (string.IsNullOrWhiteSpace(raw))
            {
                reason = "empty config";
                return CustomConfigSaveResult.Invalid;
            }
            raw = raw.Trim();
            bool usable = TunnelConfigParser.TryParse(raw, out var tunnel) && tunnel.Success;
            string xrayError = "";
            if (!usable)
            {
                usable = SingboxLinkParser.TryParseLink(raw, out _, out _);
                if (!usable) usable = ConfigConverter.TryDetectChain(raw, out _, out var chainHops, out _, out _) && chainHops.Count > 0;
                if (!usable) usable = ConfigConverter.TryXrayOutbound(raw, out _, out _, out xrayError);
            }
            if (!usable)
            {
                label  = "";
                reason = xrayError.Length > 0 ? xrayError : "this config is neither an xray outbound, a share link nor a tunnel";
                SimpleLogger.Log($"[Store] Refused a config: {reason}");
                return CustomConfigSaveResult.Invalid;
            }
            label = ConfigConverter.LabelFor(raw);
            CustomConfigSaveResult result;
            lock (_lock)
            {
                var entries = LoadCore(cfg);
                string key = NormalizeKey(raw);
                var existing = entries.FirstOrDefault(e => NormalizeKey(e.Raw) == key);
                if (existing != null)
                {
                    existing.Raw   = raw;
                    if (!existing.LabelEdited) existing.Label = label;
                    existing.SavedUtc = DateTime.UtcNow;
                    SaveCore(entries.OrderByDescending(e => e.SavedUtc).ToList());
                    result = CustomConfigSaveResult.Updated;
                }
                else if (entries.Count >= MaxEntries)
                {
                    result = CustomConfigSaveResult.PoolFull;
                }
                else
                {
                    entries.Insert(0, new AppCustomConfigEntry { Raw = raw, Label = label, SavedUtc = DateTime.UtcNow });
                    SaveCore(entries);
                    result = CustomConfigSaveResult.Saved;
                }
            }
            if (result != CustomConfigSaveResult.PoolFull)
                TunnelCredentialPruner.Sweep(cfg, AppRulesService.Load());
            return result;
        }

        public static int Delete(AppConfig cfg, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0;
            int removed;
            lock (_lock)
            {
                var entries = LoadCore(cfg);
                string key = NormalizeKey(raw.Trim());
                removed = entries.RemoveAll(e => NormalizeKey(e.Raw) == key);
                if (removed > 0) SaveCore(entries);
            }
            if (removed > 0)
                TunnelCredentialPruner.Sweep(cfg, AppRulesService.Load());
            return removed;
        }

        public static bool SameConfig(string first, string second)
            => !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second)
            && NormalizeKey(first.Trim()) == NormalizeKey(second.Trim());

        private static string NormalizeKey(string raw)
        {
            return ConfigConverter.KeyFor(raw, "");
        }

        public static string LabelFor(AppConfig cfg, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string trimmed = raw.Trim();
            try
            {
                var entries = Load(cfg);
                string key = NormalizeKey(trimmed);
                var existing = entries.FirstOrDefault(e => e != null && !string.IsNullOrWhiteSpace(e.Raw) && NormalizeKey(e.Raw) == key);
                if (existing != null && !string.IsNullOrWhiteSpace(existing.Label)) return existing.Label;
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
            string label = ConfigConverter.LabelFor(trimmed);
            return label.Length > 0 ? label : (trimmed.Length <= 40 ? trimmed : trimmed.Substring(0, 40) + "…");
        }
        public static List<string> DisplayOptions(IReadOnlyList<AppCustomConfigEntry> entries)
        {
            var items = new List<string> { CrimsonX.Localization.AppStrings.CustomProxyNone };
            if (entries == null) return items;
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                string text = entry == null ? "" : (string.IsNullOrWhiteSpace(entry.Label) ? ConfigConverter.LabelFor(entry.Raw ?? "") : entry.Label);
                if (seen.TryGetValue(text, out int count))
                {
                    count++;
                    seen[text] = count;
                    text = text + " (#" + count + ")";
                }
                else
                {
                    seen[text] = 1;
                }
                items.Add(text);
            }
            return items;
        }

        private static List<AppCustomConfigEntry> Clone(List<AppCustomConfigEntry> entries)
        {
            var copy = new List<AppCustomConfigEntry>(entries.Count);
            foreach (var entry in entries)
            {
                copy.Add(new AppCustomConfigEntry
                {
                    Raw      = entry.Raw,
                    Label    = entry.Label,
                    SavedUtc = entry.SavedUtc
                });
            }
            return copy;
        }

        private static void RefreshCache(List<AppCustomConfigEntry> entries, string path)
        {
            try
            {
                var info = new FileInfo(path);
                _cache         = Clone(entries.OrderByDescending(e => e.SavedUtc).ToList());
                _cacheStampUtc = info.LastWriteTimeUtc;
                _cacheLength   = info.Length;
            }
            catch
            {
                InvalidateCache();
            }
        }

        private static void InvalidateCache()
        {
            _cache         = null;
            _cacheStampUtc = default;
            _cacheLength   = 0;
        }
        private static void BackupCorruptStore(string path)
        {
            try
            {
                string backup = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
                File.Move(path, backup, overwrite: true);
                CrimsonX.Services.SimpleLogger.Log(
                    $"[CustomConfigs] Unreadable saved-config pool moved to {Path.GetFileName(backup)}.");
            }
            catch (Exception ex)
            {
                CrimsonX.Services.SimpleLogger.Log(ex);
            }
        }

        private static List<AppCustomConfigEntry> SeedFromSettings(AppConfig cfg)
        {
            var list = new List<AppCustomConfigEntry>();
            if (cfg == null) return list;
            foreach (var candidate in new[] { cfg.CustomConfig1, cfg.CustomConfig2 })
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                string label;
                if (!SingboxLinkParser.TryParseLink(candidate.Trim(), out _, out label))
                {
                    if (!TunnelConfigParser.TryParse(candidate.Trim(), out var tunnel) || !tunnel.Success) continue;
                    label = tunnel.Label;
                }
                if (list.Any(e => NormalizeKey(e.Raw) == NormalizeKey(candidate))) continue;
                list.Add(new AppCustomConfigEntry { Raw = candidate.Trim(), Label = label, SavedUtc = DateTime.UtcNow });
            }
            return list;
        }
    }
}

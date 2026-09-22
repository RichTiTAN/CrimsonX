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
using CrimsonX.Models;
using Newtonsoft.Json;

namespace CrimsonX.Services
{
    public static class AppRulesService
    {
        private static string RulesPath()
        {
            var baseDir = MainWindow.Instance?.GetAppPath("Data\\Apps") ?? "Data\\Apps";
            return Path.Combine(baseDir, "rules.json");
        }

        private static List<AppGameRule>? _cached;
        private static DateTime _cachedStampUtc;
        private static long _cachedLength = -1;

        private static bool CacheIsCurrent(string path)
        {
            if (_cached == null) return false;
            try
            {
                var info = new FileInfo(path);
                return info.Exists && info.LastWriteTimeUtc == _cachedStampUtc && info.Length == _cachedLength;
            }
            catch { return false; }
        }

        public static List<AppGameRule> Load()
        {
            try
            {
                var path = RulesPath();
                if (CacheIsCurrent(path)) return Clone(_cached!);
                if (!File.Exists(path)) return new List<AppGameRule>();

                var json = File.ReadAllText(path);
                var rules = JsonConvert.DeserializeObject<List<AppGameRule>>(json) ?? new List<AppGameRule>();
                Remember(path, rules);
                return rules;
            }
            catch
            {
                return new List<AppGameRule>();
            }
        }

        public static void Save(List<AppGameRule> rules)
        {
            try
            {
                var path = RulesPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonConvert.SerializeObject(rules, Formatting.Indented));
                Remember(path, rules);
            }
            catch { }
        }

        private static void Remember(string path, List<AppGameRule> rules)
        {
            try
            {
                var info = new FileInfo(path);
                _cached         = Clone(rules);
                _cachedStampUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
                _cachedLength   = info.Exists ? info.Length : -1;
            }
            catch { _cached = null; }
        }

        private static List<AppGameRule> Clone(List<AppGameRule> rules)
        {
            var copy = new List<AppGameRule>(rules.Count);
            foreach (var rule in rules)
                if (rule != null) copy.Add(Clone(rule));   
            return copy;
        }

        private static AppGameRule Clone(AppGameRule rule) => new AppGameRule
        {
            Id           = rule.Id,
            IsEnabled    = rule.IsEnabled,
            IsPinned     = rule.IsPinned,
            AppType      = rule.AppType,
            ExeName      = rule.ExeName,
            DisplayName  = rule.DisplayName,
            IconBase64   = rule.IconBase64,
            IconAsset    = rule.IconAsset,
            DefaultKey   = rule.DefaultKey,
            ProcessNames = rule.ProcessNames != null ? new List<string>(rule.ProcessNames) : new List<string>(),
            Domains      = rule.Domains != null ? new List<string>(rule.Domains) : new List<string>(),
            Country      = rule.Country,
            Region       = rule.Region,
            TcpRouting   = rule.TcpRouting,
            UdpRouting   = rule.UdpRouting,
            TcpAdapter   = rule.TcpAdapter,
            UdpAdapter   = rule.UdpAdapter,
            CustomProxyRaw   = rule.CustomProxyRaw,
            CustomProxyLabel = rule.CustomProxyLabel
        };
    }
}

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
using CrimsonX.Models;
using Newtonsoft.Json;

namespace CrimsonX.Services
{
    public static class AppRulesService
    {
        private static string RulesPath()
        {
            return SecureJsonStore.PathFor("app_rules.bin");
        }

        private static string LegacyRulesPath()
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
                SecureJsonStore.AdoptPlaintextFile(LegacyRulesPath(), path);
                if (CacheIsCurrent(path)) return Clone(_cached!);
                if (!File.Exists(path)) return new List<AppGameRule>();
                var rules = SecureJsonStore.Load<List<AppGameRule>>(path) ?? new List<AppGameRule>();
                rules = rules.Where(r => r != null).ToList();
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
                SecureJsonStore.Save(path, rules);
                Remember(path, rules);
                TunnelCredentialPruner.Sweep(MainWindow.Instance?.Config, rules);
                TunnelHostPruner.Sweep(MainWindow.Instance?.Config, rules, force: true);
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

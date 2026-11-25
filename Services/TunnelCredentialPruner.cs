/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using System.Collections.Generic;
using CrimsonX.Models;

namespace CrimsonX.Services
{
    internal static class TunnelCredentialPruner
    {
        public static int Sweep(AppConfig cfg, IEnumerable<AppGameRule> rules)
        {
            if (cfg == null || !ConfigService.ConfigLoaded) return 0;
            int removed = 0;
            try
            {
                var keys = LiveKeys(cfg, rules);
                try
                {
                    foreach (var choice in AppOptimizeStore.Load().Choices)
                    {
                        if (choice == null) continue;
                        Add(keys, choice.PrevCustomProxyRaw);
                    }
                }
                catch (Exception ex)
                {
                    SimpleLogger.Log(ex);
                }
                removed = TunnelCredentialStore.PruneTo(keys);
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
            if (removed > 0)
                SimpleLogger.Log($"[Credentials] Forgot {removed} stored credential(s) whose config is gone.");
            return removed;
        }

        internal static HashSet<string> LiveKeys(AppConfig cfg, IEnumerable<AppGameRule> rules)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (cfg != null)
            {
                Add(keys, cfg.CustomConfig1);
                Add(keys, cfg.CustomConfig2);
                Add(keys, cfg.V2rayChainJson);
                try
                {
                    foreach (var entry in AppCustomConfigStore.Snapshot(cfg))
                    {
                        if (entry != null) Add(keys, entry.Raw);
                    }
                }
                catch (Exception ex)
                {
                    SimpleLogger.Log(ex);
                }
            }
            if (rules != null)
            {
                foreach (var rule in rules)
                {
                    if (rule == null) continue;
                    Add(keys, rule.CustomProxyRaw);
                }
            }
            return keys;
        }

        private static void Add(HashSet<string> keys, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            if (!TunnelConfigParser.TryParse(raw.Trim(), out var tunnel) || tunnel == null || !tunnel.Success) return;
            string key = TunnelConfigParser.Normalize(tunnel.Raw);
            if (key.Length > 0) keys.Add(key);
        }
    }
}

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
                removed = TunnelCredentialStore.PruneTo(LiveKeys(cfg, rules));
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

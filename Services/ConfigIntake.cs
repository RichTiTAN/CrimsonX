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
using System.Threading.Tasks;
using CrimsonX.Localization;
using CrimsonX.Models;

namespace CrimsonX.Services
{
    public sealed class ConfigIntakeResult
    {
        public bool Accepted { get; set; }

        public bool Unreadable { get; set; }

        public bool NeedsCredentials { get; set; }

        public bool IsTunnel { get; set; }

        public string Raw { get; set; } = "";

        public string Label { get; set; } = "";

        public string EngineJson { get; set; } = "";

        public string Reason { get; set; } = "";

        public string Toast { get; set; } = "";

        public string MaskNote { get; set; } = "";
    }

    public static class ConfigIntake
    {
        private const int CacheLimit = 16;

        private static readonly Dictionary<string, ConfigIntakeResult> Cache = new(StringComparer.Ordinal);

        public static async Task<ConfigIntakeResult> AcceptAsync(string raw, AppConfig cfg, ConfigTarget target, string paneName,
            ConfigCheckLevel level = ConfigCheckLevel.Offline, string adapterName = "", string adapterIp = "")
        {
            string text = (raw ?? "").Trim();
            var result = new ConfigIntakeResult { Raw = text };
            if (text.Length == 0)
            {
                result.Unreadable = true;
                result.Reason = "empty config";
                return result;
            }
            string key = $"{target}|{level}|{paneName}|{adapterName}|{ConfigConverter.KeyFor(text, adapterIp)}";
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var verdict = await ConfigValidator.CheckAsync(text, cfg, target, level, adapterName, adapterIp).ConfigureAwait(false);
            result.Accepted         = verdict.Ok;
            result.Unreadable       = verdict.Unreadable;
            result.NeedsCredentials = verdict.NeedsCredentials;
            result.IsTunnel         = verdict.IsTunnel;
            result.Label            = verdict.Label.Length > 0 ? verdict.Label : ConfigConverter.LabelFor(text);
            result.EngineJson       = verdict.EngineJson;
            result.Reason           = verdict.Reason;
            result.MaskNote = verdict.MaskNote;
            if (verdict.WithoutMask.Length > 0) result.Raw = verdict.WithoutMask;
            if (!result.Accepted)
            {
                if (result.Reason.Length > 0)
                    SimpleLogger.Log($"[Intake] {paneName}: {result.Reason}");
                result.Toast = result.NeedsCredentials
                    ? AppStrings.ToastTunnelNeedsCredentials
                    : AppStrings.ToastNotSupportedPrefix + paneName;
            }
            else if (result.MaskNote.Length > 0)
            {
                SimpleLogger.Log($"[Intake] {paneName}: the final mask was refused by xray: {result.MaskNote}");
                result.Toast = AppStrings.ToastFinalMaskDropped + ConfigValidator.ShortReason(result.MaskNote);
            }
            Remember(key, result);
            return result;
        }

        public static void Invalidate() => Cache.Clear();

        private static void Remember(string key, ConfigIntakeResult result)
        {
            if (Cache.Count >= CacheLimit) Cache.Clear();
            Cache[key] = result;
        }
    }
}

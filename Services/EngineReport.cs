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
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CrimsonX.Models;

namespace CrimsonX.Services
{
    public static class EngineReport
    {
        private static readonly Regex NamedVersion = new(
            @"(?:xray|sing-box)\D{0,20}?(\d+)\.(\d+)\.(\d+)(?!\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex AnyVersion = new(
            @"(?<!\d)(\d+)\.(\d+)\.(\d+)(?!\d)", RegexOptions.Compiled);

        public static Version? XrayVersion { get; private set; }

        public static Version? SingboxVersion { get; private set; }

        public static async Task AnnounceAsync(AppConfig cfg)
        {
            if (cfg == null) return;
            XrayVersion    = await AnnounceAsync("xray", Path.Combine(cfg.XrayDir ?? "", "xray.exe")).ConfigureAwait(false);
            SingboxVersion = await AnnounceAsync("sing-box", Path.Combine(cfg.SbDir ?? "", "sing-box.exe")).ConfigureAwait(false);
        }

        public static string XraySummary() => XrayVersion == null ? "" : "xray " + XrayVersion;

        public static Version? ParseVersion(string? output)
        {
            if (string.IsNullOrWhiteSpace(output)) return null;
            var match = NamedVersion.Match(output);
            if (!match.Success) match = AnyVersion.Match(output);
            if (!match.Success) return null;
            return Version.TryParse(
                $"{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}", out var version)
                ? version
                : null;
        }

        private static async Task<Version?> AnnounceAsync(string name, string exePath)
        {
            if (!File.Exists(exePath))
            {
                SimpleLogger.Log($"[Engine] {name}: not found at {exePath}");
                return null;
            }
            var version = ParseVersion(await RunVersionAsync(exePath).ConfigureAwait(false));
            if (version == null)
            {
                SimpleLogger.Log($"[Engine] {name}: version unknown — {exePath}");
                return null;
            }
            SimpleLogger.Log($"[Engine] {name} {version} — {exePath}");
            return version;
        }

        private static async Task<string> RunVersionAsync(string exePath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = exePath,
                    Arguments              = "version",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    WorkingDirectory       = Path.GetDirectoryName(exePath) ?? ""
                };
                using var process = Process.Start(psi);
                if (process == null) return "";
                var outTask = process.StandardOutput.ReadToEndAsync();
                var errTask = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(true); } catch { }
                    return "";
                }
                return (await outTask.ConfigureAwait(false)) + " " + (await errTask.ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                return "";
            }
        }
    }
}

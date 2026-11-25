/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
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

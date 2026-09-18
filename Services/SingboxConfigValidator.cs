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
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public sealed class CustomOutboundProbe
    {
        public string Key { get; set; } = "";

        public string OutboundJson { get; set; } = "";
    }

    public enum CustomProxyCheckResult
    {
        Ok,

        Missing,

        Unparsable,

        Rejected
    }

    public static class SingboxConfigValidator
    {
        private const int CheckTimeoutMs = 8000;

        private const string SingleProbeKey = "custom-editor";
        private static int _tempSweepDone;

        private static string SbExePath(string sbDir)
        {
            var appPath = MainWindow.Instance?.GetAppPath(@"Data\sing_box\sing-box.exe");
            if (!string.IsNullOrWhiteSpace(appPath)) return appPath;
            return Path.Combine(sbDir ?? "", "sing-box.exe");
        }
        public static bool Check(string sbDir, string configPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sbDir) || string.IsNullOrWhiteSpace(configPath)) return true;
                return RunCheck(sbDir, configPath);
            }
            catch
            {
                return true;
            }
        }

        private static bool RunCheck(string sbDir, string configPath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = SbExePath(sbDir),
                    Arguments              = $"check -D \"{sbDir}\" -c \"{configPath}\"",
                    WorkingDirectory       = sbDir,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
                };

                using var process = Process.Start(psi);
                if (process == null) return true;

                JobManager.AddProcess(process);

                var outTask = process.StandardOutput.ReadToEndAsync();
                var errTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(CheckTimeoutMs))
                {
                    try { process.Kill(); } catch { }
                    SimpleLogger.Log($"[SingBox:check] check timed out after {CheckTimeoutMs}ms; assuming the config is valid.");
                    return true;
                }

                Task.WaitAll(outTask, errTask);
                bool ok = process.ExitCode == 0;
                if (!ok)
                {
                    string detail = (errTask.Result + " " + outTask.Result).Trim();
                    if (detail.Length > 400) detail = detail.Substring(0, 400);
                    SimpleLogger.Log($"[SingBox:check] rejected config: {detail}");
                }
                return ok;
            }
            catch
            {
                return true;
            }
        }
        public static HashSet<string> FindInvalidOutbounds(string sbDir, IReadOnlyList<CustomOutboundProbe> probes)
        {
            var invalid = new HashSet<string>(StringComparer.Ordinal);
            if (probes == null || probes.Count == 0) return invalid;
            if (string.IsNullOrWhiteSpace(sbDir) || !Directory.Exists(sbDir)) return invalid;

            SweepStaleProbeDirs();

            var usable = new List<JObject>();
            var keys   = new List<string>();
            foreach (var probe in probes)
            {
                if (probe == null || string.IsNullOrWhiteSpace(probe.OutboundJson)) continue;
                try
                {
                    usable.Add(JObject.Parse(probe.OutboundJson));
                    keys.Add(probe.Key);
                }
                catch
                {
                    invalid.Add(probe.Key);
                }
            }

            if (usable.Count == 0) return invalid;

            string probeDir = Path.Combine(Path.GetTempPath(), "CrimsonX_sbcheck_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(probeDir);
                string probePath = Path.Combine(probeDir, "probe.json");

                if (!WriteAndCheck(sbDir, probePath, usable, batch: true))
                {
                    for (int i = 0; i < usable.Count; i++)
                    {
                        if (!WriteAndCheck(sbDir, probePath, new List<JObject> { usable[i] }, batch: false))
                            invalid.Add(keys[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
            finally
            {
                try { if (Directory.Exists(probeDir)) Directory.Delete(probeDir, true); } catch { }
            }

            return invalid;
        }
        private static bool WriteAndCheck(string sbDir, string probePath, List<JObject> outbounds, bool batch)
        {
            var list = new JArray();
            for (int i = 0; i < outbounds.Count; i++)
            {
                outbounds[i]["tag"] = batch ? "probe-" + i : "probe";
                list.Add(outbounds[i]);
            }

            list.Add(new JObject { ["type"] = "direct", ["tag"] = "direct" });

            var config = new JObject
            {
                ["log"] = new JObject { ["level"] = "fatal" },
                ["outbounds"] = list
            };

            File.WriteAllText(probePath, config.ToString(Formatting.Indented));
            return Check(sbDir, probePath);
        }
        private static void SweepStaleProbeDirs()
        {
            if (Interlocked.Exchange(ref _tempSweepDone, 1) != 0) return;

            try
            {
                var cutoff = DateTime.UtcNow.AddMinutes(-30);
                foreach (var dir in Directory.GetDirectories(Path.GetTempPath(), "CrimsonX_sbcheck_*"))
                {
                    try { if (Directory.GetLastWriteTimeUtc(dir) < cutoff) Directory.Delete(dir, true); } catch { }
                }
            }
            catch { }
        }

        public static bool CheckOutbound(string sbDir, string outboundJson)
        {
            if (string.IsNullOrWhiteSpace(outboundJson)) return false;

            try { JObject.Parse(outboundJson); }
            catch { return false; }

            var probe = new CustomOutboundProbe { Key = SingleProbeKey, OutboundJson = outboundJson };
            var invalid = FindInvalidOutbounds(sbDir, new[] { probe });
            return !invalid.Contains(SingleProbeKey);
        }

        public static CustomProxyCheckResult ValidateEditorConfig(string sbDir, string raw, string adapterName = "", string adapterIp = "")
        {
            string text = (raw ?? "").Trim();
            if (text.Length == 0) return CustomProxyCheckResult.Missing;

            if (!SingboxLinkParser.TryParseLink(text, out var outboundJson, out _)) return CustomProxyCheckResult.Unparsable;

            try
            {
                var outbound = JObject.Parse(outboundJson);
                var tagged   = SingboxLinkParser.WithTagAndDial(outbound, SingleProbeKey, adapterName, adapterIp);
                if (!CheckOutbound(sbDir, tagged.ToString(Formatting.None))) return CustomProxyCheckResult.Rejected;
            }
            catch
            {
                return CustomProxyCheckResult.Unparsable;
            }

            return CustomProxyCheckResult.Ok;
        }
    }
}

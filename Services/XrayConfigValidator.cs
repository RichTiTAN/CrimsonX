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
using System.Linq;
using System.Threading.Tasks;
using CrimsonX.Models;

namespace CrimsonX.Services
{
    public static class XrayConfigValidator
    {
        public sealed class XrayCheckResult
        {
            public string Error { get; set; } = "";

            public string MaskNote { get; set; } = "";

            public string WithoutMask { get; set; } = "";

            public bool Ok => Error.Length == 0;
        }

        public static async Task<string> CheckAsync(AppConfig cfg, string raw)
            => (await CheckDetailedAsync(cfg, raw).ConfigureAwait(false)).Error;

        public static async Task<XrayCheckResult> CheckDetailedAsync(AppConfig cfg, string raw)
        {
            var result = new XrayCheckResult();
            if (cfg == null) return result;
            if (!ConfigConverter.TryXrayOutbound(raw, out string document, out _, out string convertError))
            {
                result.Error = convertError;
                return result;
            }
            string error = await TestAsync(cfg, document).ConfigureAwait(false);
            if (error.Length == 0) return result;
            if (FinalMask.FromStream(MaskOwner(document)) != null && FinalMask.LooksLikeMaskFailure(error))
            {
                string withoutMask = FinalMask.StripDocument(document);
                string retry = withoutMask == document ? error : await TestAsync(cfg, withoutMask).ConfigureAwait(false);
                if (retry.Length == 0)
                {
                    result.MaskNote    = error;
                    result.WithoutMask = FinalMask.StripFromText(raw);
                    string core = EngineReport.XraySummary();
                    SimpleLogger.Log($"[FinalMask] {core}{(core.Length > 0 ? " " : "")}refused the final mask of this config, so it is used without it: {error}");
                    return result;
                }
            }
            result.Error = error;
            return result;
        }

        private static async Task<string> TestAsync(AppConfig cfg, string document)
        {
            string wrapped = WrapDocument(document);
            if (wrapped.Length == 0) return "this config could not be wrapped for xray";
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(tempFile, wrapped);
                string xrayExe = Path.Combine(cfg.BaseDir ?? "", "Data", "xray", "xray.exe");
                if (!File.Exists(xrayExe)) return "";
                var psi = new ProcessStartInfo
                {
                    FileName               = xrayExe,
                    Arguments              = $"-test -config \"{tempFile}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true
                };
                using var process = Process.Start(psi);
                if (process == null) return "";
                var outTask = process.StandardOutput.ReadToEndAsync();
                var errTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode == 0) return "";
                string err = await errTask;
                string outStr = await outTask;
                string message = string.IsNullOrWhiteSpace(err) ? outStr : err;
                var lines = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Where(l => !l.Contains("Xray, Penetrates Everything") && !l.Contains("unified platform"));
                message = string.Join(" ", lines).Trim();
                return message.Length > 0 ? message : "xray rejected this config";
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                return "";
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        private static Newtonsoft.Json.Linq.JToken MaskOwner(string document)
        {
            try
            {
                var root = Newtonsoft.Json.Linq.JObject.Parse(document);
                if (root["outbounds"] is Newtonsoft.Json.Linq.JArray outbounds)
                    return outbounds.OfType<Newtonsoft.Json.Linq.JObject>().FirstOrDefault();
                return root;
            }
            catch
            {
                return null;
            }
        }

        public static string TestFile(string xrayDir, string configPath)
        {
            try
            {
                string xrayExe = Path.Combine(xrayDir ?? "", "xray.exe");
                if (!File.Exists(xrayExe) || !File.Exists(configPath)) return "";
                var psi = new ProcessStartInfo
                {
                    FileName               = xrayExe,
                    Arguments              = $"-test -config \"{configPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true
                };
                using var process = Process.Start(psi);
                if (process == null) return "";
                string err = process.StandardError.ReadToEnd();
                string outStr = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode == 0) return "";
                string message = string.IsNullOrWhiteSpace(err) ? outStr : err;
                var lines = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Where(l => !l.Contains("Xray, Penetrates Everything") && !l.Contains("unified platform"));
                message = string.Join(" ", lines).Trim();
                return message.Length > 0 ? message : "xray rejected this config";
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                return "";
            }
        }

        public static string WrapDocument(string document)
        {
            try
            {
                var root = Newtonsoft.Json.Linq.JObject.Parse(document);
                var outbounds = root["outbounds"] as Newtonsoft.Json.Linq.JArray
                                ?? new Newtonsoft.Json.Linq.JArray { root };
                return new Newtonsoft.Json.Linq.JObject
                {
                    ["log"]       = new Newtonsoft.Json.Linq.JObject { ["loglevel"] = "none" },
                    ["inbounds"]  = new Newtonsoft.Json.Linq.JArray(),
                    ["outbounds"] = outbounds
                }.ToString();
            }
            catch
            {
                return "";
            }
        }
    }
}

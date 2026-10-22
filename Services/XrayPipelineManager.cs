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
using Newtonsoft.Json.Linq;
using CrimsonX.Models;

namespace CrimsonX.Services
{
    public static class XrayPipelineManager
    {
        private static Process? _xrayProcess;
        private static readonly object _lock = new object();
        private static readonly SemaphoreSlim SwapGate = new(1, 1);
        private static int _generation;
        public static List<string> ActiveOutbounds { get; private set; } = new List<string>();

        public static bool StartXray(List<string> outbounds, AppConfig config, string xrayDir)
        {
            lock (_lock)
            {
                _generation++;
                ActiveOutbounds = new List<string>(outbounds);
                var chains = ParseOutbounds(outbounds, "StartXray");
                if (!XrayConfigWriter.Write(config, xrayDir, chains))
                {
                    return false;
                }
                ApplyFinalMaskFallback(config, xrayDir, chains);
                if (_xrayProcess != null)
                {
                    try 
                    {
                        if (!_xrayProcess.HasExited)
                            _xrayProcess.Kill(); 
                    } 
                    catch { }
                    try { _xrayProcess.Dispose(); } catch { }
                }
                try
                {
                    _xrayProcess = new Process();
                    _xrayProcess.StartInfo.FileName = Path.Combine(xrayDir, "xray.exe");
                    _xrayProcess.StartInfo.Arguments = $"run -c \"{Path.Combine(xrayDir, "config.json")}\"";
                    _xrayProcess.StartInfo.UseShellExecute = false;
                    _xrayProcess.StartInfo.CreateNoWindow = true;
                    _xrayProcess.StartInfo.RedirectStandardError = true;
                    _xrayProcess.StartInfo.RedirectStandardOutput = true;
                    _xrayProcess.Start();
                    _xrayProcess.ErrorDataReceived += (s, e) => {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                            CrimsonX.Services.SimpleLogger.Log($"[Xray Core Error] {e.Data}");
                    };
                    _xrayProcess.OutputDataReceived += (s, e) => {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                            CrimsonX.Services.SimpleLogger.Log($"[Xray Core] {e.Data}");
                    };
                    _xrayProcess.BeginErrorReadLine();
                    _xrayProcess.BeginOutputReadLine();
                    JobManager.AddProcess(_xrayProcess);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static void ApplyFinalMaskFallback(AppConfig config, string xrayDir, List<List<JObject>> chains)
        {
            try
            {
                bool hasMask = false;
                foreach (var chain in chains)
                {
                    foreach (var outbound in chain)
                    {
                        if (FinalMask.FromStream(outbound) == null) continue;
                        hasMask = true;
                        break;
                    }
                    if (hasMask) break;
                }
                if (!hasMask) return;
                string configPath = Path.Combine(xrayDir, "config.json");
                string error = XrayConfigValidator.TestFile(xrayDir, configPath);
                if (error.Length == 0) return;
                if (!FinalMask.LooksLikeMaskFailure(error))
                {
                    CrimsonX.Services.SimpleLogger.Log($"[FinalMask] xray refused this session for another reason: {error}");
                    return;
                }
                bool removed = false;
                foreach (var chain in chains)
                {
                    foreach (var outbound in chain) removed |= FinalMask.Strip(outbound);
                }
                if (!removed || !XrayConfigWriter.Write(config, xrayDir, chains)) return;
                string retry = XrayConfigValidator.TestFile(xrayDir, configPath);
                if (retry.Length == 0)
                {
                    CrimsonX.Services.SimpleLogger.Log($"[FinalMask] xray refused a final mask of this session, so it was dropped: {error}");
                    UiEventBus.Instance.PublishToast(
                        CrimsonX.Localization.AppStrings.ToastFinalMaskDropped + ConfigValidator.ShortReason(error));
                }
                else
                {
                    CrimsonX.Services.SimpleLogger.Log($"[FinalMask] the session is still invalid without its mask: {retry}");
                }
            }
            catch (Exception ex)
            {
                CrimsonX.Services.SimpleLogger.Log(ex);
            }
        }

        public static async Task<bool> SwapOutboundsAsync(List<string> newOutbounds, AppConfig config, string xrayDir, bool forceRestart = false)
        {
            return await Task.Run(async () =>
            {
                await SwapGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    int generation = 0;
                    JArray proxyOutbounds = new JArray();
                    List<string> tagsToRemove = new List<string>();
                    string tempObPath = "";
                    lock (_lock)
                    {
                        generation = _generation;
                        ActiveOutbounds = new List<string>(newOutbounds);
                        var chains = ParseOutbounds(newOutbounds, "SwapOutboundsAsync");
                        var nodesToRemove = new List<string>();
                        var clonesToRemove = new List<string>();
                        string configJsonPath = Path.Combine(xrayDir, "config.json");
                        try
                        {
                            if (File.Exists(configJsonPath))
                            {
                                var oldRoot = JObject.Parse(File.ReadAllText(configJsonPath));
                                if (oldRoot["outbounds"] is JArray oldObs)
                                {
                                    foreach (JObject ob in oldObs)
                                    {
                                        string tag = ob["tag"]?.ToString() ?? "";
                                        if (tag.StartsWith("proxy-node"))
                                            nodesToRemove.Add(tag);
                                        else if (tag.StartsWith("proxy-clone") || tag.StartsWith("proxy-hop"))
                                            clonesToRemove.Add(tag);
                                    }
                                }
                            }
                        }
                        catch { }
                        tagsToRemove.AddRange(clonesToRemove);
                        tagsToRemove.AddRange(nodesToRemove);
                        if (!XrayConfigWriter.Write(config, xrayDir, chains))
                        {
                            return false;
                        }
                        if (forceRestart || _xrayProcess == null || _xrayProcess.HasExited)
                        {
                            return StartXray(newOutbounds, config, xrayDir);
                        }
                        try
                        {
                            if (!File.Exists(configJsonPath)) return false;
                            var root = JObject.Parse(File.ReadAllText(configJsonPath));
                            var outboundsArray = root["outbounds"] as JArray;
                            if (outboundsArray == null) return false;
                            var nodesToAdd = new JArray();
                            var clonesToAdd = new JArray();
                            foreach (JObject ob in outboundsArray)
                            {
                                string tag = ob["tag"]?.ToString() ?? "";
                                if (tag.StartsWith("proxy-node"))
                                    nodesToAdd.Add(ob);
                                else if (tag.StartsWith("proxy-clone") || tag.StartsWith("proxy-hop"))
                                    clonesToAdd.Add(ob);
                            }
                            foreach (var n in nodesToAdd) proxyOutbounds.Add(n);
                            foreach (var c in clonesToAdd) proxyOutbounds.Add(c);
                            tempObPath = Path.Combine(xrayDir, "temp_outbounds.json");
                        }
                        catch
                        {
                            return false;
                        }
                    }
                    try
                    {
                        foreach (JObject ob in proxyOutbounds)
                        {
                            var wrapper = new JObject();
                            wrapper["outbounds"] = new JArray(ob);
                            await File.WriteAllTextAsync(tempObPath, wrapper.ToString()).ConfigureAwait(false);
                            await RunXrayApiCliAsync(xrayDir, "ado", $"api ado --server=127.0.0.1:10999 \"{tempObPath}\"", 1500).ConfigureAwait(false);
                        }
                        foreach (string tag in tagsToRemove)
                        {
                            await RunXrayApiCliAsync(xrayDir, "rmo", $"api rmo --server=127.0.0.1:10999 \"{tag}\"", 1500).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        CrimsonX.Services.SimpleLogger.Log($"[XrayPipelineManager] Seamless swap failed, falling back to restart: {ex.Message}");
                        lock (_lock)
                        {
                            if (generation != _generation || _xrayProcess == null || _xrayProcess.HasExited) return false;
                            return StartXray(newOutbounds, config, xrayDir);
                        }
                    }
                    finally
                    {
                        try { if (File.Exists(tempObPath)) File.Delete(tempObPath); } catch { }
                    }
                    lock (_lock)
                    {
                        if (generation != _generation || _xrayProcess == null || _xrayProcess.HasExited) return false;
                    }
                    CrimsonX.Services.SimpleLogger.Log("[XrayPipelineManager] Seamless hot-swap executed successfully via CLI API.");
                    return true;
                }
                finally
                {
                    SwapGate.Release();
                }
            });
        }

        private static async Task RunXrayApiCliAsync(string xrayDir, string operation, string arguments, int timeoutMs)
        {
            using var proc = new Process();
            proc.StartInfo.FileName = Path.Combine(xrayDir, "xray.exe");
            proc.StartInfo.Arguments = arguments;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.Start();
            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(); } catch { }
                using var killCts = new CancellationTokenSource(1000);
                try { await proc.WaitForExitAsync(killCts.Token).ConfigureAwait(false); } catch { }
            }
            string output = (await errTask.ConfigureAwait(false)) + " " + (await outTask.ConfigureAwait(false));
            if (proc.ExitCode != 0)
            {
                throw new Exception($"xray api {operation} failed with exit code {proc.ExitCode}: {output.Trim()}");
            }
        }

        public static void StopXray()
        {
            lock (_lock)
            {
                _generation++;
                if (_xrayProcess != null)
                {
                    try 
                    {
                        if (!_xrayProcess.HasExited)
                            _xrayProcess.Kill(); 
                    } 
                    catch { }
                    try { _xrayProcess.Dispose(); } catch { }
                    _xrayProcess = null;
                }
                ActiveOutbounds = new List<string>();
            }
        }

        private static List<List<JObject>> ParseOutbounds(List<string> entries, string caller)
        {
            var result = new List<List<JObject>>();
            int requested = 0;
            foreach (var outbStr in entries ?? new List<string>())
            {
                requested++;
                try
                {
                    if (ConfigConverter.TryXrayChain(outbStr, out var hops, out _, out string chainError) && hops.Count > 0)
                    {
                        result.Add(hops);
                    }
                    else
                    {
                        CrimsonX.Services.SimpleLogger.Log($"[Xray] {caller}: outbound entry skipped ({chainError}): {Preview(outbStr)}");
                    }
                }
                catch (Exception ex)
                {
                    CrimsonX.Services.SimpleLogger.Log($"[Xray] {caller}: outbound entry skipped ({ex.GetType().Name}): {Preview(outbStr)}");
                }
            }
            CrimsonX.Services.SimpleLogger.Log($"[Xray] {caller}: outbounds requested={requested} accepted={result.Count}");
            if (requested > 0 && result.Count == 0)
                CrimsonX.Services.SimpleLogger.Log($"[Xray] {caller}: no usable outbound was handed to xray.");
            return result;
        }

        private static string Preview(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "(empty)";
            string flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length <= 90 ? flat : flat.Substring(0, 90) + "…";
        }
    }
}

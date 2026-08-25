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
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using CrimsonX.Models;

namespace CrimsonX.Services
{
    public static class XrayPipelineManager
    {
        private static Process _xrayProcess;
        private static readonly object _lock = new object();
        public static List<string> ActiveOutbounds { get; private set; } = new List<string>();

        public static bool StartXray(List<string> outbounds, AppConfig config, string xrayDir)
        {
            lock (_lock)
            {
                ActiveOutbounds = new List<string>(outbounds);

                var jOutbounds = new List<JObject>();
                foreach (var outbStr in outbounds)
                {
                    try
                    {
                        var jOut = JObject.Parse(outbStr);
                        if (jOut["outbounds"] is JArray arr && arr.Count > 0)
                        {
                            jOutbounds.Add((JObject)arr[0]);
                        }
                    }
                    catch { }
                }

                if (!XrayConfigWriter.Write(config, xrayDir, jOutbounds))
                {
                    return false;
                }

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

        public static async Task<bool> SwapOutboundsAsync(List<string> newOutbounds, AppConfig config, string xrayDir, bool forceRestart = false)
        {
            return await Task.Run(() =>
            {
                lock (_lock)
                {
                    int oldOutboundsCount = ActiveOutbounds.Count;
                    ActiveOutbounds = new List<string>(newOutbounds);

                    var jOutbounds = new List<JObject>();
                    foreach (var outbStr in newOutbounds)
                    {
                        try
                        {
                            var jOut = JObject.Parse(outbStr);
                            if (jOut["outbounds"] is JArray arr && arr.Count > 0)
                                jOutbounds.Add((JObject)arr[0]);
                        }
                        catch { }
                    }

                    // 1. Read existing config.json to get old tags before we overwrite it
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
                                    else if (tag.StartsWith("proxy-clone"))
                                        clonesToRemove.Add(tag);
                                }
                            }
                        }
                    } 
                    catch { }

                    var tagsToRemove = new List<string>();
                    tagsToRemove.AddRange(clonesToRemove);
                    tagsToRemove.AddRange(nodesToRemove);

                    // 2. Write new config.json (generates new unique tags for proxy-nodes)
                    if (!XrayConfigWriter.Write(config, xrayDir, jOutbounds))
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

                        // 3. Read the newly generated tags
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
                            else if (tag.StartsWith("proxy-clone"))
                                clonesToAdd.Add(ob);
                        }

                        // Merge them: nodes first, then clones
                        var proxyOutbounds = new JArray();
                        foreach (var n in nodesToAdd) proxyOutbounds.Add(n);
                        foreach (var c in clonesToAdd) proxyOutbounds.Add(c);

                        // 4. ADD the new outbounds to Xray FIRST (so it never has 0 outbounds)
                        string tempObPath = Path.Combine(xrayDir, "temp_outbounds.json");
                        foreach (var ob in proxyOutbounds)
                        {
                            var wrapper = new JObject();
                            wrapper["outbounds"] = new JArray(ob);
                            File.WriteAllText(tempObPath, wrapper.ToString());

                            var adoProc = new Process();
                            adoProc.StartInfo.FileName = Path.Combine(xrayDir, "xray.exe");
                            adoProc.StartInfo.Arguments = $"api ado --server=127.0.0.1:10999 \"{tempObPath}\"";
                            adoProc.StartInfo.UseShellExecute = false;
                            adoProc.StartInfo.CreateNoWindow = true;
                            adoProc.StartInfo.RedirectStandardError = true;
                            adoProc.StartInfo.RedirectStandardOutput = true;
                            adoProc.Start();
                            
                            string errOut = adoProc.StandardError.ReadToEnd() + " " + adoProc.StandardOutput.ReadToEnd();
                            adoProc.WaitForExit(1500);
                            
                            if (adoProc.ExitCode != 0)
                            {
                                throw new Exception($"xray api ado failed with exit code {adoProc.ExitCode}: {errOut.Trim()}");
                            }
                        }
                        try { if (File.Exists(tempObPath)) File.Delete(tempObPath); } catch { }

                        // 5. REMOVE the old outbounds from Xray SECOND
                        foreach (string tag in tagsToRemove)
                        {
                            var rmoProc = new Process();
                            rmoProc.StartInfo.FileName = Path.Combine(xrayDir, "xray.exe");
                            rmoProc.StartInfo.Arguments = $"api rmo --server=127.0.0.1:10999 \"{tag}\"";
                            rmoProc.StartInfo.UseShellExecute = false;
                            rmoProc.StartInfo.CreateNoWindow = true;
                            rmoProc.StartInfo.RedirectStandardError = true;
                            rmoProc.StartInfo.RedirectStandardOutput = true;
                            rmoProc.Start();
                            
                            string errOut = rmoProc.StandardError.ReadToEnd() + " " + rmoProc.StandardOutput.ReadToEnd();
                            rmoProc.WaitForExit(1500);
                            
                            if (rmoProc.ExitCode != 0)
                            {
                                throw new Exception($"xray api rmo failed with exit code {rmoProc.ExitCode}: {errOut.Trim()}");
                            }
                        }

                        CrimsonX.Services.SimpleLogger.Log("[XrayPipelineManager] Seamless hot-swap executed successfully via CLI API.");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        CrimsonX.Services.SimpleLogger.Log($"[XrayPipelineManager] Seamless swap failed, falling back to restart: {ex.Message}");
                        return StartXray(newOutbounds, config, xrayDir);
                    }
                    finally
                    {
                        string tempObPath = Path.Combine(xrayDir, "temp_outbounds.json");
                        try { if (File.Exists(tempObPath)) File.Delete(tempObPath); } catch { }
                    }
                }
            });
        }

        public static void StopXray()
        {
            lock (_lock)
            {
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
                ActiveOutbounds.Clear();
            }
        }
    }
}

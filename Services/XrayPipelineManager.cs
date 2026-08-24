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
                    _xrayProcess.Start();
                    JobManager.AddProcess(_xrayProcess);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static async Task<bool> SwapOutboundsAsync(List<string> newOutbounds, AppConfig config, string xrayDir)
        {
            return await Task.Run(() =>
            {
                return StartXray(newOutbounds, config, xrayDir);
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

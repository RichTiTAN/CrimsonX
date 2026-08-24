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

namespace CrimsonX.Services
{
    public static class ProcessService
    {
        public static Process? StartProcessDirect(string filePath, string arguments = "", string workingDirectory = "", bool hidden = true)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = filePath,
                    Arguments = arguments,
                    WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? "" : workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = hidden,
                    WindowStyle = hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
                };
                var process = new Process { StartInfo = psi };
                process.Start();
                try { JobManager.AddProcess(process); } catch (Exception ex) { CrimsonX.Services.SimpleLogger.Log(ex); }
                try { if (!process.HasExited) process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception ex) { CrimsonX.Services.SimpleLogger.Log(ex); }
                return process;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Process launch failed for '{filePath}': {ex.Message}");
                return null;
            }
        }

        public static void KillProcess(int? pid)
        {
            if (pid == null) return;
            try
            {
                using var p = Process.GetProcessById(pid.Value);
                p.Kill();
            }
            catch (Exception ex) { CrimsonX.Services.SimpleLogger.Log(ex); }
        }

        public static void KillAppProcesses(string[] names, string[] appPaths)
        {
            foreach (var name in names)
            {
                try
                {
                    var procs = Process.GetProcessesByName(name);
                    foreach (var p in procs)
                    {
                        using (p)
                        {
                            try
                            {
                                if (appPaths.Any(path => string.Equals(path, p.MainModule?.FileName, StringComparison.OrdinalIgnoreCase)))
                                    p.Kill();
                            }
                            catch (Exception ex) { CrimsonX.Services.SimpleLogger.Log(ex); }
                        }
                    }
                }
                catch (Exception ex) { CrimsonX.Services.SimpleLogger.Log(ex); }
            }
        }

        public static async Task UpdateBootScheduledTask(bool enable, string exePath)
        {
            const string taskName = "CrimsonX_AutoStart";
            try
            {
                if (enable)
                {
                    var args = $"/Create /F /RL HIGHEST /SC ONLOGON /TN \"{taskName}\" /TR \"\\\"{exePath}\\\"\" /DELAY 0000:05";
                    var psi = new ProcessStartInfo("schtasks.exe", args)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var p = Process.Start(psi);
                    if (p != null)
                    {
                    var stderrTask = p.StandardError.ReadToEndAsync();
                    await p.WaitForExitAsync();
                    string err = await stderrTask;
                        if (p.ExitCode != 0)
                            throw new Exception("schtasks exit code " + p.ExitCode + " " + err);
                    }
                }
                else
                {
                    var args = $"/Delete /F /TN \"{taskName}\"";
                    var psi = new ProcessStartInfo("schtasks.exe", args)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var p = Process.Start(psi);
                    if (p != null)
                    {
                    var stderrTask = p.StandardError.ReadToEndAsync();
                    await p.WaitForExitAsync();
                    string err = await stderrTask;
                        if (p.ExitCode != 0)
                            throw new Exception("schtasks exit code " + p.ExitCode + " " + err);
                    }
                }

                var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                var oldLnk = Path.Combine(startupFolder, "CrimsonX.lnk");
                if (File.Exists(oldLnk)) File.Delete(oldLnk);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Boot task error: {ex.Message}");
                throw;
            }
        }
    }
}



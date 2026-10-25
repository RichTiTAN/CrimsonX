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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public static class UpdateService
    {
        public const string AppVersion = "3.1.7";
        
        private static HttpClient BuildClient(TimeSpan timeout, bool throughApp)
        {
            var handler = new System.Net.Http.SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(8) };
            if (throughApp)
            {
                handler.Proxy = new System.Net.WebProxy("http://127.0.0.1:" + ExitNodeChain.XrayProxyPort);
                handler.UseProxy = true;
            }
            else
            {
                handler.UseProxy = false;
            }
            return new HttpClient(handler) { Timeout = timeout };
        }

        public static async Task<bool> UseAppProxyAsync(bool engineRunning, string? mode)
        {
            if (!engineRunning) return false;
            if (string.Equals(mode, "Clear Proxy", StringComparison.OrdinalIgnoreCase)) return false;
            return await AppProxyListeningAsync().ConfigureAwait(false);
        }

        private static async Task<bool> AppProxyListeningAsync()
        {
            try
            {
                using var socket = new System.Net.Sockets.TcpClient();
                using var timeout = new CancellationTokenSource(300);
                await socket.ConnectAsync(System.Net.IPAddress.Loopback, ExitNodeChain.XrayProxyPort, timeout.Token).ConfigureAwait(false);
                return socket.Connected;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsNewerVersion(string? remoteVersion)
        {
            return Version.TryParse(remoteVersion, out var remote)
                && Version.TryParse(AppVersion, out var local)
                && remote > local;
        }

        public static async Task<(string? remoteVer, string? remoteMin)> CheckForUpdatesAsync(CancellationToken token = default, bool engineRunning = false, string? mode = null)
        {
            bool throughApp = await UseAppProxyAsync(engineRunning, mode).ConfigureAwait(false);
            using var client = BuildClient(TimeSpan.FromSeconds(15), throughApp);
            var url = $"https://raw.githubusercontent.com/RichTiTAN/CrimsonX/main/version.json?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var response = await client.GetAsync(url, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var raw = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var json = JObject.Parse(raw);
            var remoteVer = json["version"]?.ToString() ?? "0.0.0";
            var remoteMin = json["minAutoUpdateVersion"]?.ToString() ?? "0.0.0";
            if (IsNewerVersion(remoteVer))
            {
                return (remoteVer, remoteMin);
            }
            return (null, null);
        }

        public static async Task DownloadAndInstallUpdateAsync(string remoteVersion, string baseDir, Action<string> progressCallback, CancellationToken token, Action<int>? percentCallback = null, bool engineRunning = false, string? mode = null)
        {
            bool throughApp = await UseAppProxyAsync(engineRunning, mode).ConfigureAwait(false);
            using var headClient = BuildClient(TimeSpan.FromSeconds(15), throughApp);
            using var fileClient = BuildClient(Timeout.InfiniteTimeSpan, throughApp);
            var zipUrl = "https://github.com/RichTiTAN/CrimsonX/releases/latest/download/CrimsonX.zip";
            string version = VersionToken(remoteVersion);
            var zipPath = Path.Combine(baseDir, $"update_temp_{version}.zip");
            var extPath = Path.Combine(baseDir, $"update_extracted_{version}");
            PurgeOtherVersions(baseDir, version);
            try
            {
                long existingLen = 0;
                if (File.Exists(zipPath))
                    existingLen = new FileInfo(zipPath).Length;
                using var headReq = new HttpRequestMessage(HttpMethod.Head, zipUrl);
                using var headRes = await headClient.SendAsync(headReq, token).ConfigureAwait(false);
                headRes.EnsureSuccessStatusCode();
                var total = headRes.Content.Headers.ContentLength ?? -1L;
                if (total > 0 && existingLen == total)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        progressCallback($"UPDATE ALREADY DOWNLOADED... EXTRACTING");
                        percentCallback?.Invoke(100);
                    });
                }
                else
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, zipUrl);
                    if (existingLen > 0 && existingLen < total)
                    {
                        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingLen, null);
                        Dispatcher.UIThread.Post(() => progressCallback($"RESUMING DOWNLOAD..."));
                    }
                    else
                    {
                        existingLen = 0;
                        if (File.Exists(zipPath)) File.Delete(zipPath);
                        Dispatcher.UIThread.Post(() =>
                        {
                            progressCallback($"DOWNLOADING UPDATE... 0% (CLICK TO CANCEL)");
                            percentCallback?.Invoke(0);
                        });
                    }
                    using var dlResponse = await fileClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    dlResponse.EnsureSuccessStatusCode();
                    var streamTotal = total;
                    using var fs = new FileStream(zipPath, existingLen > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                    if (existingLen > 0 && dlResponse.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        existingLen = 0;
                        fs.SetLength(0);
                    }
                    using var stream = await dlResponse.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                    var buffer = new byte[81920];
                    long downloaded = existingLen;
                    int read;
                    int lastPct = -1;
                    while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                        downloaded += read;
                        if (streamTotal > 0)
                        {
                            int pct = (int)(downloaded * 100 / streamTotal);
                            if (pct != lastPct)
                            {
                                lastPct = pct;
                                Dispatcher.UIThread.Post(() =>
                                {
                                    progressCallback($"DOWNLOADING UPDATE... {pct}% (CLICK TO CANCEL)");
                                    percentCallback?.Invoke(pct);
                                });
                            }
                        }
                    }
                }
                if (Directory.Exists(extPath)) Directory.Delete(extPath, true);
                Dispatcher.UIThread.Post(() =>
                {
                    progressCallback("EXTRACTING UPDATE...");
                    percentCallback?.Invoke(100);
                });
                try
                {
                    await Task.Run(() => {
                        token.ThrowIfCancellationRequested();
                        System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extPath, true);
                    }, token).ConfigureAwait(false);
                }
                catch (System.IO.InvalidDataException)
                {
                    throw new Exception("The downloaded update file is corrupt. It will be re-downloaded next time.");
                }
                var exeFile = Directory.GetFiles(extPath, "CrimsonX.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (exeFile == null) throw new Exception("CrimsonX.exe not found in the downloaded ZIP!");
                var sourceDir = Path.GetDirectoryName(exeFile)!;
                var currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                static void ValidateBatPath(string path, string label)
                {
                    if (path.IndexOfAny(new[] { '%', '&', '|', '<', '>', '(', ')', '^', '!' }) >= 0)
                        throw new Exception($"{label} contains characters not supported by the update script: {path}");
                }
                ValidateBatPath(sourceDir, "Update source directory");
                ValidateBatPath(baseDir, "Application directory");
                ValidateBatPath(extPath, "Update extraction directory");
                ValidateBatPath(zipPath, "Update archive path");
                ValidateBatPath(currentExe, "Application executable");
                var logPath = Path.Combine(baseDir, "updater.log");
                var batContent = "@echo off\n" +
"setlocal enabledelayedexpansion\n" +
"echo [%DATE% %TIME%] update hand-off started>>\"" + logPath + "\"\n" +
"set /a waits=0\n" +
":waitloop\n" +
"tasklist | find /i \"CrimsonX.exe\" > nul\n" +
"if not errorlevel 1 (\n" +
"    timeout /t 1 > nul\n" +
"    set /a waits+=1\n" +
"    if !waits! lss 120 goto waitloop\n" +
"    echo [%DATE% %TIME%] the app never exited>>\"" + logPath + "\"\n" +
")\n" +
"set /a tries=0\n" +
":copyloop\n" +
"xcopy /Y /E /H /C /I \"" + sourceDir + "\\*\" \"" + baseDir + "\" >>\"" + logPath + "\" 2>&1\n" +
"if errorlevel 1 (\n" +
"    set /a tries+=1\n" +
"    echo [%DATE% %TIME%] copy attempt !tries! failed>>\"" + logPath + "\"\n" +
"    if !tries! lss 20 (\n" +
"        timeout /t 1 > nul\n" +
"        goto copyloop\n" +
"    )\n" +
"    echo [%DATE% %TIME%] gave up after !tries! attempts>>\"" + logPath + "\"\n" +
") else (\n" +
"    echo [%DATE% %TIME%] copy complete>>\"" + logPath + "\"\n" +
"    rmdir /S /Q \"" + extPath + "\"\n" +
"    del /Q \"" + zipPath + "\"\n" +
")\n" +
"start \"\" \"" + currentExe + "\"\n" +
"del \"%~f0\"\n";
                var batPath = Path.Combine(baseDir, "updater.bat");
                File.WriteAllText(batPath, batContent);
                using (Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{batPath}\"") { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true }))
                {
                }
            }
            catch (Exception ex)
            {
                bool isCorrupt = ex.Message.Contains("corrupt") || ex is System.IO.InvalidDataException;
                if (isCorrupt)
                {
                    try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
                }
                try { if (Directory.Exists(extPath)) Directory.Delete(extPath, true); } catch { }
                throw;
            }
        }

        private static string VersionToken(string? remoteVersion)
        {
            string raw = (remoteVersion ?? "").Trim();
            var token = new System.Text.StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (token.Length >= 24) break;
                token.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_');
            }
            return token.Length == 0 ? "unknown" : token.ToString();
        }

        private static void PurgeOtherVersions(string baseDir, string current)
        {
            try
            {
                foreach (string file in Directory.GetFiles(baseDir, "update_temp*.zip"))
                {
                    if (string.Equals(Path.GetFileName(file), "update_temp_" + current + ".zip", StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Delete(file); } catch { }
                }
                foreach (string dir in Directory.GetDirectories(baseDir, "update_extracted*"))
                {
                    if (string.Equals(Path.GetFileName(dir), "update_extracted_" + current, StringComparison.OrdinalIgnoreCase)) continue;
                    try { Directory.Delete(dir, true); } catch { }
                }
            }
            catch { }
        }
    }
}

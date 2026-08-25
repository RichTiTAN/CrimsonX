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
        public const string AppVersion = "1.0.1";
        
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly HttpClient _dlClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        public static async Task<(string? remoteVer, string? remoteMin)> CheckForUpdatesAsync(CancellationToken token = default)
        {
            var url = $"https://raw.githubusercontent.com/RichTiTAN/CrimsonX/main/version.json?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var response = await _httpClient.GetAsync(url, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            
            var raw = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var json = JObject.Parse(raw);
            var remoteVer = json["version"]?.ToString() ?? "0.0.0";
            var remoteMin = json["minAutoUpdateVersion"]?.ToString() ?? "0.0.0";

            if (Version.Parse(remoteVer) > Version.Parse(AppVersion))
            {
                return (remoteVer, remoteMin);
            }
            
            return (null, null);
        }

        public static async Task DownloadAndInstallUpdateAsync(string remoteVersion, string baseDir, Action<string> progressCallback, CancellationToken token)
        {
            var zipUrl = "https://github.com/RichTiTAN/CrimsonX/releases/latest/download/CrimsonX.zip";
            var zipPath = Path.Combine(baseDir, "update_temp.zip");
            var extPath = Path.Combine(baseDir, "update_extracted");
            
            try
            {
                long existingLen = 0;
                if (File.Exists(zipPath))
                    existingLen = new FileInfo(zipPath).Length;

                using var headReq = new HttpRequestMessage(HttpMethod.Head, zipUrl);
                using var headRes = await _httpClient.SendAsync(headReq, token).ConfigureAwait(false);
                headRes.EnsureSuccessStatusCode();
                
                var total = headRes.Content.Headers.ContentLength ?? -1L;
                
                if (total > 0 && existingLen == total)
                {
                    Dispatcher.UIThread.Post(() => progressCallback($"UPDATE ALREADY DOWNLOADED... EXTRACTING"));
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
                        Dispatcher.UIThread.Post(() => progressCallback($"DOWNLOADING UPDATE... 0% (CLICK TO CANCEL)"));
                    }

                    using var dlResponse = await _dlClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
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
                                Dispatcher.UIThread.Post(() => progressCallback($"DOWNLOADING UPDATE... {pct}% (CLICK TO CANCEL)"));
                            }
                        }
                    }
                
                }

                if (Directory.Exists(extPath)) Directory.Delete(extPath, true);
                Dispatcher.UIThread.Post(() => progressCallback("EXTRACTING UPDATE..."));
                
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
                
                var batContent = "@echo off\n" +
":waitloop\n" +
"tasklist | find /i \"CrimsonX.exe\" > nul\n" +
"if not errorlevel 1 (\n" +
"    timeout /t 1 > nul\n" +
"    goto waitloop\n" +
")\n" +
":copyloop\n" +
"xcopy /Y /E /H /C /I \"" + sourceDir + "\\*\" \"" + baseDir + "\"\n" +
"if errorlevel 1 (\n" +
"    timeout /t 1 > nul\n" +
"    goto copyloop\n" +
")\n" +
"rmdir /S /Q \"" + extPath + "\"\n" +
"del /Q \"" + zipPath + "\"\n" +
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
    }
}

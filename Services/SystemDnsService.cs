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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrimsonX.Models;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public static class SystemDnsService
    {
        private const int ApplyBudgetMs   = 8000;
        private const int RestoreBudgetMs = 1500;
        private const int HealBudgetMs    = 6000;

        private static readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        private static readonly List<DnsState> _pending = new List<DnsState>();

        private static int _epoch;

        private static readonly string BackupPath =
            Path.Combine(AppContext.BaseDirectory, "Data", "dns_backup.json");

        public static bool HasPendingRestore => _pending.Count > 0;

        // ── Apply system DNS at connect time ──

        public static async Task ApplyAsync(AppConfig cfg)
        {
            if (!cfg.EnableSystemDns) return;
            if (string.IsNullOrWhiteSpace(cfg.SystemDnsPrimary)) return;

            int myEpoch = Volatile.Read(ref _epoch);

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (myEpoch != Volatile.Read(ref _epoch))
                {
                    SimpleLogger.Log("[DnsService] Skipped DNS apply: a newer DNS operation replaced it.");
                    return;
                }

                string primary   = cfg.SystemDnsPrimary.Trim();
                string secondary = cfg.SystemDnsSecondary?.Trim() ?? "";
                string wanted    = string.IsNullOrEmpty(secondary) ? primary : $"{primary}, {secondary}";

                await Task.Run(() =>
                {
                    try
                    {
                        AdoptDiskBackup();

                        var nic = ResolveAdapter(cfg);
                        if (nic == null)
                        {
                            SimpleLogger.Log("[DnsService] No valid adapter found for DNS.");
                            return;
                        }

                        if (!_pending.Any(s => string.Equals(s.AdapterName, nic.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            _pending.Add(DnsService.CaptureState(nic));
                            SavePending();
                        }

                        bool ok = DnsService.SetDns(nic.Name, primary, secondary, ApplyBudgetMs, out string error);
                        string actual = string.Join(", ", DnsService.GetCurrentDns(nic.Name));

                        if (ok)
                            SimpleLogger.Log($"[DnsService] Applied DNS {actual} on {nic.Name} (requested {wanted})");
                        else
                            SimpleLogger.Log($"[DnsService] DNS only partly applied on {nic.Name} — {error} (requested {wanted})");
                    }
                    catch (Exception ex)
                    {
                        SimpleLogger.Log(ex);
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        // ── Restore system DNS at disconnect / app close ──

        public static Task RestoreAsync(int budgetMs = RestoreBudgetMs) =>
            RestoreCoreAsync(budgetMs, adoptDisk: false);

        public static Task HealFromDiskAsync() =>
            RestoreCoreAsync(HealBudgetMs, adoptDisk: true);

        private static async Task RestoreCoreAsync(int budgetMs, bool adoptDisk)
        {
            Interlocked.Increment(ref _epoch);

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (adoptDisk) AdoptDiskBackup();
                if (_pending.Count == 0) return;

                await Task.Run(() =>
                {
                    try
                    {
                        foreach (var state in _pending.ToArray())
                        {
                            string wanted = state.WasDhcp
                                ? "DHCP (automatic)"
                                : string.Join(", ", state.Servers);

                            if (DnsService.RestoreState(state, budgetMs, out string error))
                            {
                                _pending.Remove(state);
                                SavePending();
                                SimpleLogger.Log($"[DnsService] Restored DNS on {state.AdapterName} to {wanted}");
                            }
                            else
                            {
                                SimpleLogger.Log($"[DnsService] DNS restore on {state.AdapterName} to {wanted} is incomplete — {error}. It stays queued for the next launch.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        SimpleLogger.Log(ex);
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private static void SavePending()
        {
            try
            {
                if (_pending.Count == 0)
                {
                    if (File.Exists(BackupPath)) File.Delete(BackupPath);
                    return;
                }

                var array = new JArray();
                foreach (var state in _pending)
                {
                    array.Add(new JObject
                    {
                        ["adapter"] = state.AdapterName,
                        ["dhcp"]    = state.WasDhcp,
                        ["servers"] = new JArray(state.Servers)
                    });
                }

                var dir = Path.GetDirectoryName(BackupPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(BackupPath, array.ToString());
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
        }

        private static void AdoptDiskBackup()
        {
            try
            {
                if (!File.Exists(BackupPath)) return;

                var array = JArray.Parse(File.ReadAllText(BackupPath));
                foreach (var entry in array.OfType<JObject>())
                {
                    string adapterName = entry["adapter"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(adapterName)) continue;
                    if (_pending.Any(s => string.Equals(s.AdapterName, adapterName, StringComparison.OrdinalIgnoreCase))) continue;

                    _pending.Add(new DnsState
                    {
                        AdapterName = adapterName,
                        WasDhcp     = entry["dhcp"]?.ToObject<bool>() ?? false,
                        Servers     = entry["servers"]?.ToObject<string[]>() ?? Array.Empty<string>()
                    });

                    SimpleLogger.Log($"[DnsService] adopted the pending DNS backup for {adapterName} from an earlier run");
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
        }

        private static System.Net.NetworkInformation.NetworkInterface? ResolveAdapter(AppConfig cfg)
        {
            if (cfg.EnableAdapterBinding && !string.IsNullOrWhiteSpace(cfg.SelectedAdapterName))
            {
                var bound = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(a => a.Name == cfg.SelectedAdapterName
                                      && a.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up);

                if (bound != null) return bound;
            }

            return DnsService.GetMainPhysicalAdapter();
        }
    }
}

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
using System.Linq;
using System.Threading.Tasks;
using CrimsonX.Models;

namespace CrimsonX.Services
{
    public static class SystemDnsService
    {
        private static string?   _savedDnsAdapterName;
        private static string[]? _savedDnsServers;

        // ── Apply system DNS at connect time ──

        public static async Task ApplyAsync(AppConfig cfg)
        {
            if (!cfg.EnableSystemDns) return;
            if (string.IsNullOrWhiteSpace(cfg.SystemDnsPrimary)) return;

            await Task.Run(() =>
            {
                try
                {
                    System.Net.NetworkInformation.NetworkInterface? nic = null;
                    if (cfg.EnableAdapterBinding && !string.IsNullOrWhiteSpace(cfg.SelectedAdapterName))
                    {
                        nic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                            .FirstOrDefault(a => a.Name == cfg.SelectedAdapterName && a.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up);
                    }

                    if (nic == null)
                    {
                        nic = DnsService.GetMainPhysicalAdapter();
                    }

                    if (nic == null)
                    {
                        SimpleLogger.Log("[DnsService] No valid adapter found for DNS.");
                        return;
                    }
                    _savedDnsAdapterName = nic.Name;
                    _savedDnsServers     = DnsService.GetCurrentDns(nic);

                    DnsService.SetDns(nic.Name, cfg.SystemDnsPrimary, cfg.SystemDnsSecondary);
                    SimpleLogger.Log($"[DnsService] Applied DNS {cfg.SystemDnsPrimary}/{cfg.SystemDnsSecondary} to {nic.Name}");
                }
                catch (Exception ex)
                {
                    SimpleLogger.Log(ex);
                }
            });
        }

        // ── Restore system DNS at disconnect / app close ──

        public static async Task RestoreAsync()
        {
            if (_savedDnsAdapterName == null) return;

            await Task.Run(() =>
            {
                try
                {
                    DnsService.RestoreDns(_savedDnsAdapterName, _savedDnsServers ?? Array.Empty<string>());
                    SimpleLogger.Log($"[DnsService] Restored DNS on {_savedDnsAdapterName}");
                }
                catch (Exception ex)
                {
                    SimpleLogger.Log(ex);
                }
                finally
                {
                    _savedDnsAdapterName = null;
                    _savedDnsServers     = null;
                }
            });
        }
    }
}

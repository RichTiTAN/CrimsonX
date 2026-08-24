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
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace CrimsonX.Services
{
    public static class DnsService
    {
        private static readonly Regex _ipv4Regex = new Regex(
            @"^((25[0-5]|2[0-4]\d|1\d{2}|[1-9]\d|\d)\.){3}(25[0-5]|2[0-4]\d|1\d{2}|[1-9]\d|\d)$",
            RegexOptions.Compiled);

        public static bool IsValidIpv4(string? address)
        {
            if (string.IsNullOrWhiteSpace(address)) return false;
            return _ipv4Regex.IsMatch(address.Trim());
        }

        public static NetworkInterface? GetMainPhysicalAdapter()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic =>
                    nic.OperationalStatus == OperationalStatus.Up &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                    !nic.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                    !nic.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase) &&
                    !nic.Description.Contains("TAP", StringComparison.OrdinalIgnoreCase) &&
                    !nic.Description.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) &&
                    nic.GetIPProperties().GatewayAddresses.Any(g =>
                        g.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !g.Address.Equals(IPAddress.Any)))
                .OrderBy(nic =>
                    nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0 : 1)
                .FirstOrDefault();
        }

        public static string[] GetCurrentDns(NetworkInterface nic)
        {
            try
            {
                var props = nic.GetIPProperties();
                var addresses = props.DnsAddresses
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.ToString())
                    .ToArray();
                return addresses;
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public static bool IsDhcpDns(NetworkInterface nic)
        {
            try
            {
                var props = nic.GetIPProperties().GetIPv4Properties();
                return props?.IsDhcpEnabled == true &&
                       !nic.GetIPProperties().DnsAddresses
                           .Any(a => a.AddressFamily == AddressFamily.InterNetwork);
            }
            catch
            {
                return false;
            }
        }

        public static bool SetDns(string adapterName, string primary, string? secondary = null)
        {
            try
            {
                RunNetsh($"interface ip set dns name=\"{adapterName}\" static {primary} primary");

                if (!string.IsNullOrWhiteSpace(secondary))
                    RunNetsh($"interface ip add dns name=\"{adapterName}\" {secondary} index=2");

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DnsService] SetDns failed: {ex.Message}");
                return false;
            }
        }

        public static bool RestoreDns(string adapterName, string[] previousServers)
        {
            try
            {
                if (previousServers == null || previousServers.Length == 0)
                {
                    RunNetsh($"interface ip set dns name=\"{adapterName}\" dhcp");
                }
                else
                {
                    RunNetsh($"interface ip set dns name=\"{adapterName}\" static {previousServers[0]} primary");
                    for (int i = 1; i < previousServers.Length; i++)
                        RunNetsh($"interface ip add dns name=\"{adapterName}\" {previousServers[i]} index={i + 1}");
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DnsService] RestoreDns failed: {ex.Message}");
                return false;
            }
        }

        private static void RunNetsh(string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            };
            using var proc = Process.Start(psi);
            bool exited = proc?.WaitForExit(5000) ?? true;
            if (!exited)
            {
                try { proc?.Kill(); } catch { }
                SimpleLogger.Log("netsh timed out");
            }
        }
    }
}

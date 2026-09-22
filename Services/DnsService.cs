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
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace CrimsonX.Services
{
    public static class DnsService
    {
        private const int NetshTimeoutMs    = 5000;
        private const int ReadBackTimeoutMs = 1500;
        private const int ReadBackPollMs    = 250;
        private const int FastFailRetryMs   = 1500;
        private const int MaxAttempts       = 2;

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

        public static NetworkInterface? FindAdapter(string adapterName)
        {
            if (string.IsNullOrWhiteSpace(adapterName)) return null;

            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => string.Equals(n.Name, adapterName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                return null;
            }
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

        public static string[] GetCurrentDns(string adapterName)
        {
            var nic = FindAdapter(adapterName);
            return nic == null ? Array.Empty<string>() : GetCurrentDns(nic);
        }

        // ── Capturing and putting back the pre-connect DNS state ──

        public static bool TryReadStaticDnsFromRegistry(NetworkInterface nic, out string[] servers)
        {
            servers = Array.Empty<string>();

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{nic.Id}");

                if (key == null) return false;

                servers = NormalizeList(((key.GetValue("NameServer") as string) ?? "").Split(','));
                return true;
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                return false;
            }
        }

        public static DnsState CaptureState(NetworkInterface nic)
        {
            bool readable = TryReadStaticDnsFromRegistry(nic, out var staticServers);

            bool wasDhcp = readable && staticServers.Length == 0;

            string[] servers = staticServers.Length > 0 ? staticServers : GetCurrentDns(nic);

            SimpleLogger.Log($"[DnsService] Captured {nic.Name}: dhcp={wasDhcp}, servers=[{string.Join(", ", servers)}], registryReadable={readable}");

            return new DnsState
            {
                AdapterName = nic.Name,
                WasDhcp     = wasDhcp,
                Servers     = servers
            };
        }

        public static bool RestoreState(DnsState state, int budgetMs, out string error)
        {
            var servers = NormalizeList(state.Servers);

            if (state.WasDhcp || servers.Length == 0)
                return ResetToDhcp(state.AdapterName, budgetMs, out error);

            return ApplyList(state.AdapterName, servers, budgetMs, out error);
        }

        public static string[] DescribeRestore(DnsState state)
        {
            var servers = NormalizeList(state.Servers);

            if (state.WasDhcp || servers.Length == 0)
                return new[] { BuildDhcpCommand(state.AdapterName) };

            return BuildApplyCommands(state.AdapterName, servers);
        }

        public static bool SetDns(string adapterName, string primary, string? secondary, int budgetMs, out string error)
        {
            var servers = string.IsNullOrWhiteSpace(secondary)
                ? new[] { primary }
                : new[] { primary, secondary! };

            return ApplyList(adapterName, NormalizeList(servers), budgetMs, out error);
        }

        public static string[] BuildApplyCommands(string adapterName, string[] servers)
        {
            var commands = new string[servers.Length];

            for (int i = 0; i < servers.Length; i++)
            {
                commands[i] = i == 0
                    ? $"interface ip set dns name=\"{adapterName}\" static {servers[i]} primary"
                    : $"interface ip add dns name=\"{adapterName}\" {servers[i]} index={i + 1}";
            }

            return commands;
        }

        public static string BuildDhcpCommand(string adapterName) =>
            $"interface ip set dns name=\"{adapterName}\" dhcp";

        // ── Internals ──

        private static bool ApplyList(string adapterName, string[] servers, int budgetMs, out string error)
        {
            error = "";

            if (servers.Length == 0)
            {
                error = "no DNS servers to apply";
                return false;
            }

            var sw = Stopwatch.StartNew();
            string[] current = GetCurrentDns(adapterName);

            if (ListMatches(current, servers))
            {
                SimpleLogger.Log($"[DnsService] {adapterName} already lists {string.Join(", ", servers)}");
                return true;
            }

            string[] commands = BuildApplyCommands(adapterName, servers);
            var missing = new List<string>();

            for (int i = 0; i < servers.Length; i++)
            {
                string server = servers[i];

                if (current.Any(s => string.Equals(s, server, StringComparison.OrdinalIgnoreCase)))
                {
                    SimpleLogger.Log($"[DnsService] {server} is already on {adapterName}, skipping");
                    continue;
                }

                if (RemainingBudget(sw, budgetMs) <= 0)
                {
                    SimpleLogger.Log($"[DnsService] the {budgetMs} ms budget for {adapterName} ran out before {server}");
                    missing.Add(server);
                    continue;
                }

                if (WriteStep(adapterName, server, commands[i], sw, budgetMs))
                    current = GetCurrentDns(adapterName);
                else
                    missing.Add(server);
            }

            if (missing.Count > 0)
            {
                error = $"{string.Join(", ", missing)} not applied (adapter now: {string.Join(", ", GetCurrentDns(adapterName))})";
                return false;
            }

            return true;
        }

        private static bool WriteStep(string adapterName, string server, string command, Stopwatch sw, int budgetMs)
        {
            if (RunStep(adapterName, server, command, sw, budgetMs, out bool failedFast)) return true;

            if (!failedFast)
            {
                SimpleLogger.Log($"[DnsService] not retrying {server} on {adapterName}: the first attempt was slow");
                return false;
            }

            if (RemainingBudget(sw, budgetMs) <= 0)
            {
                SimpleLogger.Log($"[DnsService] no budget left to retry {server} on {adapterName}");
                return false;
            }

            SimpleLogger.Log($"[DnsService] retrying {server} on {adapterName}");
            return RunStep(adapterName, server, command, sw, budgetMs, out _);
        }

        private static bool RunStep(string adapterName, string server, string command, Stopwatch sw, int budgetMs, out bool failedFast)
        {
            failedFast = false;

            var (ok, detail) = RunNetsh(command, Math.Min(NetshTimeoutMs, RemainingBudget(sw, budgetMs)));
            bool applied = WaitFor(() => IsConfigured(adapterName, server), ClosingWaitMs(sw, budgetMs));

            if (applied)
            {
                if (!ok)
                    SimpleLogger.Log($"[DnsService] {server} is on {adapterName} even though netsh did not exit cleanly ({detail})");
                return true;
            }

            failedFast = ok || sw.ElapsedMilliseconds < FastFailRetryMs;

            SimpleLogger.Log($"[DnsService] {server} is not on {adapterName} yet ({detail})");
            return false;
        }

        private static int RemainingBudget(Stopwatch sw, int budgetMs) =>
            (int)(budgetMs - sw.ElapsedMilliseconds);

        private static int ClosingWaitMs(Stopwatch sw, int budgetMs) =>
            Math.Clamp(Math.Min(ReadBackTimeoutMs, RemainingBudget(sw, budgetMs)), ReadBackPollMs, ReadBackTimeoutMs);

        private static bool ListMatches(string[] current, string[] servers) =>
            current.Length == servers.Length
            && servers.All(s => current.Any(c => string.Equals(c, s, StringComparison.OrdinalIgnoreCase)));

        private static bool ResetToDhcp(string adapterName, int budgetMs, out string error)
        {
            error = "";

            if (IsOnDhcpDns(adapterName))
            {
                SimpleLogger.Log($"[DnsService] {adapterName} is already on DHCP DNS");
                return true;
            }

            var sw = Stopwatch.StartNew();
            string command = BuildDhcpCommand(adapterName);
            string detail = "";

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                if (RemainingBudget(sw, budgetMs) <= 0)
                {
                    SimpleLogger.Log($"[DnsService] the {budgetMs} ms budget for {adapterName} ran out during the DHCP reset");
                    break;
                }

                var (ok, runDetail) = RunNetsh(command, Math.Min(NetshTimeoutMs, RemainingBudget(sw, budgetMs)));
                detail = runDetail;

                if (WaitFor(() => IsOnDhcpDns(adapterName), ClosingWaitMs(sw, budgetMs)))
                {
                    if (!ok)
                        SimpleLogger.Log($"[DnsService] {adapterName} is back on DHCP DNS even though netsh did not exit cleanly ({detail})");
                    return true;
                }

                if (!ok && sw.ElapsedMilliseconds >= FastFailRetryMs)
                {
                    SimpleLogger.Log($"[DnsService] not retrying the DHCP reset on {adapterName}: the first attempt was slow");
                    break;
                }
            }

            error = string.IsNullOrWhiteSpace(detail)
                ? "adapter is still on static DNS"
                : $"adapter is still on static DNS ({detail})";
            return false;
        }

        private static bool IsConfigured(string adapterName, string server) =>
            GetCurrentDns(adapterName).Any(s => string.Equals(s, server, StringComparison.OrdinalIgnoreCase));

        private static bool IsOnDhcpDns(string adapterName)
        {
            var nic = FindAdapter(adapterName);
            if (nic == null) return false;

            return TryReadStaticDnsFromRegistry(nic, out var staticServers) && staticServers.Length == 0;
        }

        private static bool WaitFor(Func<bool> check, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();

            while (true)
            {
                try
                {
                    if (check()) return true;
                }
                catch { }

                if (sw.ElapsedMilliseconds >= timeoutMs) return false;
                Thread.Sleep(ReadBackPollMs);
            }
        }

        private static string[] NormalizeList(IEnumerable<string> servers)
        {
            var list = new List<string>();

            foreach (var server in servers)
            {
                string value = (server ?? "").Trim();

                if (value.Length == 0) continue;
                if (value == "0.0.0.0") continue;
                if (!IsValidIpv4(value)) continue;
                if (list.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase))) continue;

                list.Add(value);
            }

            return list.ToArray();
        }

        private static (bool Ok, string Detail) RunNetsh(string args, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) return (false, "netsh could not be started");

                var outTask = proc.StandardOutput.ReadToEndAsync();
                var errTask = proc.StandardError.ReadToEndAsync();

                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(); } catch { }
                    proc.WaitForExit();
                    try { Task.WaitAll(new Task[] { outTask, errTask }, 1000); } catch { }

                    SimpleLogger.Log($"[DnsService] netsh timed out after {sw.ElapsedMilliseconds} ms: {args}");
                    return (false, $"netsh timed out after {timeoutMs} ms");
                }

                Task.WaitAll(outTask, errTask);

                if (proc.ExitCode != 0)
                {
                    string detail = string.IsNullOrWhiteSpace(errTask.Result) ? outTask.Result : errTask.Result;
                    detail = detail.Trim();
                    SimpleLogger.Log($"[DnsService] netsh failed (exit {proc.ExitCode}, {sw.ElapsedMilliseconds} ms): {detail}");
                    return (false, detail);
                }

                SimpleLogger.Log($"[DnsService] netsh ok ({sw.ElapsedMilliseconds} ms): {args}");
                return (true, "");
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                return (false, ex.Message);
            }
        }
    }
}

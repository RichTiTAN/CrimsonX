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
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CrimsonX.Services
{
    public readonly struct AdapterSnapshot
    {
        public AdapterSnapshot(
            string name,
            string description,
            NetworkInterfaceType type,
            bool isUp,
            IEnumerable<string>? ipv4Addresses = null,
            IEnumerable<string>? ipv4Gateways = null)
        {
            Name          = name ?? "";
            Description   = description ?? "";
            Type          = type;
            IsUp          = isUp;
            Ipv4Addresses = ipv4Addresses?.ToArray() ?? Array.Empty<string>();
            Ipv4Gateways  = ipv4Gateways?.ToArray()  ?? Array.Empty<string>();
        }

        public string                  Name          { get; }
        public string                  Description   { get; }
        public NetworkInterfaceType    Type          { get; }
        public bool                    IsUp          { get; }
        public string[]                Ipv4Addresses { get; }
        public string[]                Ipv4Gateways  { get; }
    }

    public static class ConnectivityService
    {
        private static readonly string[] VirtualMarkers =
        {
            "virtual", "vethernet", "hyper-v", "wsl", "docker", "vmware", "virtualbox",
            "loopback", "pseudo", "wintun", "tap-", "tun ", "tunnel", "vpn", "wireguard", "bluetooth"
        };

        public static bool HasUsableConnection() => HasUsableConnection(Snapshot());

        public static IEnumerable<AdapterSnapshot> Snapshot()
        {
            NetworkInterface[] nics;
            try
            {
                nics = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                return Array.Empty<AdapterSnapshot>();
            }

            var list = new List<AdapterSnapshot>(nics.Length);
            foreach (var nic in nics)
            {
                try
                {
                    var props = nic.GetIPProperties();

                    var addresses = props.UnicastAddresses
                        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(a => a.Address.ToString());

                    var gateways = props.GatewayAddresses
                        .Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(g => g.Address.ToString());

                    list.Add(new AdapterSnapshot(
                        nic.Name,
                        nic.Description,
                        nic.NetworkInterfaceType,
                        nic.OperationalStatus == OperationalStatus.Up,
                        addresses,
                        gateways));
                }
                catch (Exception ex)
                {
                    SimpleLogger.Log(ex);
                }
            }

            return list;
        }

        public static bool HasUsableConnection(IEnumerable<AdapterSnapshot> adapters)
        {
            if (adapters == null) return false;

            foreach (var adapter in adapters)
            {
                if (!adapter.IsUp) continue;
                if (adapter.Type == NetworkInterfaceType.Loopback ||
                    adapter.Type == NetworkInterfaceType.Tunnel) continue;
                if (IsVirtual(adapter)) continue;
                if (!HasRoutableIpv4(adapter)) continue;
                if (!HasIpv4Gateway(adapter)) continue;

                return true;
            }

            return false;
        }

        private static bool IsVirtual(AdapterSnapshot adapter) =>
            VirtualMarkers.Any(marker =>
                adapter.Name.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
                adapter.Description.Contains(marker, StringComparison.OrdinalIgnoreCase));

        private static bool HasRoutableIpv4(AdapterSnapshot adapter) =>
            adapter.Ipv4Addresses.Any(ip =>
                !ip.StartsWith("127.", StringComparison.Ordinal) &&
                !ip.StartsWith("169.254.", StringComparison.Ordinal) &&
                ip != "0.0.0.0");

        private static bool HasIpv4Gateway(AdapterSnapshot adapter) =>
            adapter.Ipv4Gateways.Any(gw => !string.IsNullOrWhiteSpace(gw) && gw != "0.0.0.0");
    }
}

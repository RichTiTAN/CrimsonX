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

using System.Net;
using System.Net.Sockets;

namespace CrimsonX.Services
{
    public static class IpDisplay
    {
        private const int TileBudget = 20;

        public static bool IsIpv6(string? ip)
            => !string.IsNullOrWhiteSpace(ip)
               && ip.Contains(':')
               && IPAddress.TryParse(ip.Trim(), out var parsed)
               && parsed.AddressFamily == AddressFamily.InterNetworkV6;

        public static string ForTile(string? ip)
        {
            string text = (ip ?? "").Trim();
            if (text.Length <= TileBudget || !IsIpv6(text)) return text;
            string[] groups = text.Split(':');
            if (groups.Length < 4) return text;
            return $"{groups[0]}:{groups[1]}:{groups[2]}…:{groups[groups.Length - 1]}";
        }
    }
}

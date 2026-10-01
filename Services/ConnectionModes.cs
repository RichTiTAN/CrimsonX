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

namespace CrimsonX.Services
{
    public static class ConnectionModes
    {
        public const string Vpn   = "VPN Mode";
        public const string Proxy = "Proxy Mode";
        public const string Clear = "Clear Proxy";

        public static readonly string[] All = { Proxy, Vpn, Clear };

        public static string Normalise(string? mode)
        {
            string value = (mode ?? "").Trim();

            foreach (string known in All)
            {
                if (string.Equals(known, value, StringComparison.OrdinalIgnoreCase)) return known;
            }

            return Proxy;
        }
    }
}

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

namespace CrimsonX.Services
{
    public static class VpnAdapterGuard
    {
        private static readonly string[] NameMarkers = { "singbox", "sing-box", "wintun" };

        private static readonly string[] DescriptionMarkers =
        {
            TunProfile.DeviceDescriptionMarker, "singbox", "sing-box", "wintun"
        };

        public static bool IsInUse(IEnumerable<AdapterSnapshot> adapters)
        {
            if (adapters == null) return false;
            foreach (var adapter in adapters)
            {
                if (!adapter.IsUp) continue;
                if (Matches(adapter.Name, adapter.Description)) return true;
            }
            return false;
        }

        public static bool Matches(string name, string description)
        {
            name ??= "";
            description ??= "";
            if (name.Equals(TunProfile.InterfaceName, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var marker in NameMarkers)
            {
                if (name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            foreach (var marker in DescriptionMarkers)
            {
                if (description.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }
}

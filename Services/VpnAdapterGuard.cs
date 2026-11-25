/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
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

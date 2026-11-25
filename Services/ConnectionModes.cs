/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
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

        public static bool SwitchesToOrFromVpn(string? currentMode, string? nextMode)
        {
            string current = Normalise(currentMode);
            string next = Normalise(nextMode);
            return current != next && (current == Vpn || next == Vpn);
        }
    }
}

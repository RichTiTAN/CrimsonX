/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
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

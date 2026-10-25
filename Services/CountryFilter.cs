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

namespace CrimsonX.Services
{
    internal static class CountryFilter
    {
        internal static readonly string[] BlockedCodes = { "RU", "BY" };

        internal static bool IsBlocked(string? countryCode) =>
            !string.IsNullOrWhiteSpace(countryCode) &&
            BlockedCodes.Contains(countryCode.Trim(), StringComparer.OrdinalIgnoreCase);
    }
}

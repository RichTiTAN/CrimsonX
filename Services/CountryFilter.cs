/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
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

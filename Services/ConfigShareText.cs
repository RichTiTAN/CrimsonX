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
    public static class ConfigShareText
    {
        public static string Build(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string trimmed = raw.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal)) return raw;
            if (XrayLinkParser.TryBuildShareLink(trimmed, out string link, "CrimsonX") && !string.IsNullOrWhiteSpace(link))
                return link;
            return raw;
        }
    }
}

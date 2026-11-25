/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using CrimsonX.Localization;

namespace CrimsonX.Services
{
    public static class TunnelFailureText
    {
        public static string Friendly(string reason)
        {
            string text = reason ?? "";
            if (Has(text, "authentication_failed") || Has(text, "auth_failed") || Has(text, "authentication failed"))
                return AppStrings.ToastTunnelLoginRejected;
            if (Has(text, "not installed") || Has(text, "not found"))
                return AppStrings.ToastTunnelEngineMissing;
            if (Has(text, "no free loopback port") || Has(text, "no socks port"))
                return AppStrings.ToastTunnelNoPort;
            if (Has(text, "no usable") || Has(text, "not an ikev2 config") || Has(text, "could not be read")
                || Has(text, "could not be written") || Has(text, "unsupported") || Has(text, "is not set"))
                return AppStrings.ToastTunnelConfigInvalid;
            if (Has(text, "did not bring the tunnel up") || Has(text, "exited right after")
                || Has(text, "did not answer") || Has(text, "timed out") || Has(text, "timeout"))
                return AppStrings.ToastTunnelServerSilent;
            return AppStrings.ToastTunnelFailed;
        }

        private static bool Has(string text, string needle)
            => text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

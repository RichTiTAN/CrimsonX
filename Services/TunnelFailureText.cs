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

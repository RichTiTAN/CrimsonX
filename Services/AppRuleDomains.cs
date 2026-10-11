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
    internal static class AppRuleDomains
    {
        internal const int MaxDomains = 16;

        private static readonly char[] Separators = { ',', '\u060C', ';', ' ', '\t', '\r', '\n' };
        private static readonly char[] PathSeparators = { '/', '?', '#' };

        public static List<string> Parse(string text)
        {
            var domains = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return domains;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                string domain = Normalize(part);
                if (domain.Length == 0 || !seen.Add(domain)) continue;
                domains.Add(domain);
                if (domains.Count >= MaxDomains) break;
            }
            return domains;
        }

        public static string Format(IEnumerable<string> domains)
        {
            if (domains == null) return "";
            var text = new List<string>();
            foreach (string value in domains)
            {
                string domain = Normalize(value);
                if (domain.Length > 0 && !text.Contains(domain)) text.Add(domain);
            }
            return string.Join(", ", text);
        }

        internal static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string domain = value.Trim().ToLowerInvariant();
            int scheme = domain.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) domain = domain.Substring(scheme + 3);
            int path = domain.IndexOfAny(PathSeparators);
            if (path >= 0) domain = domain.Substring(0, path);
            int port = domain.IndexOf(':');
            if (port >= 0) domain = domain.Substring(0, port);
            int user = domain.LastIndexOf('@');
            if (user >= 0) domain = domain.Substring(user + 1);
            if (domain.StartsWith("www.", StringComparison.Ordinal)) domain = domain.Substring(4);
            domain = domain.Trim('.');
            if (domain.IndexOf('.') < 0) return "";
            foreach (char c in domain)
            {
                if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_') return "";
            }
            return domain;
        }
    }
}

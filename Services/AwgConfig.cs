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
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public static class AwgConfig
    {
        public const string EnginePath = @"Data\awg\amnezia-box.exe";

        public sealed class DnsServer
        {
            public string Host { get; init; } = "";
            public int Port { get; init; } = 53;
        }

        private static readonly string[] KnownKeys =
        {
            "jc", "jmin", "jmax", "s1", "s2", "s3", "s4",
            "h1", "h2", "h3", "h4", "i1", "i2", "i3", "i4", "i5",
            "hpk", "headerprotectionkey", "cpa", "contentpaddingaddition",
            "rekeyaftertime", "rekeytimeout", "rejectaftertime", "keepalivetimeout",
            "maxhandshakeattempts", "randomtrailers", "disablecookies"
        };

        public static bool IsAwgEndpoint(JObject endpoint)
            => string.Equals(endpoint?["type"]?.ToString() ?? "", "awg", StringComparison.OrdinalIgnoreCase);

        public static List<DnsServer> ReadDnsServers(string value, out string warning)
        {
            warning = "";
            var servers = new List<DnsServer>();
            if (string.IsNullOrWhiteSpace(value)) return servers;
            var ignored = new List<string>();
            foreach (string raw in value.Split(new[] { ',', ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string text = raw.Trim();
                if (text.Length == 0) continue;
                int port = 53;
                string host = text;
                if (host.StartsWith("[", StringComparison.Ordinal) && host.IndexOf(']') > 0)
                {
                    int close = host.IndexOf(']');
                    string rest = host.Substring(close + 1).Trim();
                    host = host.Substring(1, close - 1);
                    if (rest.StartsWith(":", StringComparison.Ordinal)
                        && int.TryParse(rest.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int bracketPort)
                        && bracketPort > 0 && bracketPort <= 65535)
                        port = bracketPort;
                }
                else if (host.Count(c => c == ':') == 1)
                {
                    int colon = host.IndexOf(':');
                    if (int.TryParse(host.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPort)
                        && parsedPort > 0 && parsedPort <= 65535)
                    {
                        port = parsedPort;
                        host = host.Substring(0, colon).Trim();
                    }
                }
                if (!System.Net.IPAddress.TryParse(host, out _))
                {
                    ignored.Add(raw.Trim());
                    continue;
                }
                if (servers.Any(s => s.Host == host && s.Port == port)) continue;
                servers.Add(new DnsServer { Host = host, Port = port });
                if (servers.Count >= 4) break;
            }
            if (ignored.Count > 0) warning = $"The AmneziaWG DNS line lists {string.Join(", ", ignored)}, which is not a plain IP address and was ignored.";
            return servers;
        }

        public static bool Apply(JObject endpoint, IReadOnlyDictionary<string, string> values, out string error)
        {
            error = "";
            if (endpoint == null || values == null) return false;
            if (!KnownKeys.Any(values.ContainsKey)) return false;
            if (!HasAwgValue(values)) return false;
            int jc = ReadInt(values, "jc");
            int jmin = ReadInt(values, "jmin");
            int jmax = ReadInt(values, "jmax");
            if (jmin > 0 && jmax > 0 && jmin > jmax)
            {
                error = "This AmneziaWG config has Jmin larger than Jmax.";
                return false;
            }
            endpoint["type"] = "awg";
            endpoint["useIntegratedTun"] = false;
            SetInt(endpoint, "jc", jc);
            SetInt(endpoint, "jmin", jmin);
            SetInt(endpoint, "jmax", jmax);
            SetInt(endpoint, "s1", ReadInt(values, "s1"));
            SetInt(endpoint, "s2", ReadInt(values, "s2"));
            SetInt(endpoint, "s3", ReadInt(values, "s3"));
            SetInt(endpoint, "s4", ReadInt(values, "s4"));
            Copy(values, endpoint, "h1", "h1");
            Copy(values, endpoint, "h2", "h2");
            Copy(values, endpoint, "h3", "h3");
            Copy(values, endpoint, "h4", "h4");
            Copy(values, endpoint, "i1", "i1");
            Copy(values, endpoint, "i2", "i2");
            Copy(values, endpoint, "i3", "i3");
            Copy(values, endpoint, "i4", "i4");
            Copy(values, endpoint, "i5", "i5");
            Copy(values, endpoint, "header_protection_key", "hpk", "headerprotectionkey");
            Copy(values, endpoint, "content_padding_addition", "cpa", "contentpaddingaddition");
            Copy(values, endpoint, "rekey_after_time", "rekeyaftertime");
            Copy(values, endpoint, "rekey_timeout", "rekeytimeout");
            Copy(values, endpoint, "reject_after_time", "rejectaftertime");
            Copy(values, endpoint, "keepalive_timeout", "keepalivetimeout");
            Copy(values, endpoint, "max_handshake_attempts", "maxhandshakeattempts");
            if (ReadBool(values, "randomtrailers")) endpoint["random_trailers"] = true;
            if (ReadBool(values, "disablecookies")) endpoint["disable_cookies"] = true;
            if (endpoint["peers"] is JArray peers)
            {
                foreach (var peer in peers.OfType<JObject>())
                {
                    peer.Remove("reserved");
                    if (peer["pre_shared_key"] is JToken shared)
                    {
                        peer.Remove("pre_shared_key");
                        peer["preshared_key"] = shared;
                    }
                }
            }
            return true;
        }

        private static bool HasAwgValue(IReadOnlyDictionary<string, string> values)
            => ReadInt(values, "jc") > 0 || ReadInt(values, "jmin") > 0 || ReadInt(values, "jmax") > 0
               || ReadInt(values, "s1") > 0 || ReadInt(values, "s2") > 0
               || ReadInt(values, "s3") > 0 || ReadInt(values, "s4") > 0
               || Read(values, "h1", "h2", "h3", "h4").Length > 0
               || Read(values, "i1", "i2", "i3", "i4", "i5").Length > 0
               || Read(values, "hpk", "headerprotectionkey").Length > 0
               || Read(values, "cpa", "contentpaddingaddition").Length > 0
               || Read(values, "rekeyaftertime", "rekeytimeout", "rejectaftertime",
                       "keepalivetimeout", "maxhandshakeattempts").Length > 0
               || ReadBool(values, "randomtrailers") || ReadBool(values, "disablecookies");

        private static void SetInt(JObject endpoint, string field, int value)
        {
            if (value > 0) endpoint[field] = value;
        }

        private static void Copy(IReadOnlyDictionary<string, string> values, JObject endpoint, string field, params string[] keys)
        {
            string value = Read(values, keys);
            if (value.Length > 0) endpoint[field] = value;
        }

        private static string Read(IReadOnlyDictionary<string, string> values, params string[] keys)
        {
            foreach (string key in keys)
            {
                if (values.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return "";
        }

        private static int ReadInt(IReadOnlyDictionary<string, string> values, params string[] keys)
            => int.TryParse(Read(values, keys), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;

        private static bool ReadBool(IReadOnlyDictionary<string, string> values, params string[] keys)
        {
            string text = Read(values, keys);
            return text.Equals("true", StringComparison.OrdinalIgnoreCase)
                || text.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || text == "1";
        }
    }
}

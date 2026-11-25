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
    public static class ConfigBuilderLayout
    {
        private const int FullRow = 3;
        private const int WideCell = 2;
        private const int NarrowCell = 1;

        private static readonly HashSet<string> AloneKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "psk", "ca", "client_cert", "client_key", "tls_auth_key", "static_key",
            "peer_fingerprint", "data_ciphers", "data_ciphers_fallback", "extra", "fm",
            "label", "allowed_ips", "address"
        };

        private static readonly HashSet<string> NarrowTextKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "flow", "packet_encoding", "fp", "alpn", "ech", "vcn", "pcs", "pqv",
            "reality_sid", "service_name", "mode", "username", "dns", "remote_id",
            "local_id", "static_address", "static_peer", "cipher",
            "h1", "h2", "h3", "h4", "i1", "i2", "i3", "i4", "i5",
            "rekey_after", "rekey_timeout", "reject_after", "keepalive_timeout"
        };

        private static readonly HashSet<string> PairRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "server|port", "uuid|encryption", "flow|packet_encoding", "security|sni",
            "fp|alpn", "insecure|ech", "vcn|pcs", "reality_sid|reality_spx",
            "transport|header_type", "service_name|authority",
            "kcp_mtu|kcp_tti", "kcp_up|kcp_down", "kcp_cwnd|kcp_window",
            "uuid|alter_id", "alter_id|vmess_security", "vmess_security|packet_encoding",
            "alpn|fp", "insecure|vcn", "method|password", "plugin|plugin_opts",
            "version|username", "username|password", "tls|sni", "sni|insecure",
            "alpn|obfs", "obfs|obfs_password", "sni|alpn", "fp|rdns",
            "keepalive|mtu", "listen_port|workers", "jc|jmin", "s1|s2", "s3|s4",
            "h1|h2", "h3|h4", "i1|i2", "i3|i4", "rekey_after|rekey_timeout",
            "reject_after|keepalive_timeout", "random_trailers|disable_cookies",
            "remote_id|local_id", "dns|mtu", "tls_auth_type|tls_auth_direction",
            "tls_version_min|tls_version_max", "tun_mtu|mssfix", "reneg_sec|ping",
            "ping_restart|comp_lzo", "compress|fragment", "redirect_gateway|block_ipv6",
            "fragment|explicit_exit_notify", "static_address|static_peer",
            "congestion|relay_mode", "uuid|password"
        };

        public static int Units(ConfigField field)
        {
            if (field.Multiline || AloneKeys.Contains(field.Key)) return FullRow;
            return field.Kind switch
            {
                ConfigFieldKind.Check => NarrowCell,
                ConfigFieldKind.Number => NarrowCell,
                ConfigFieldKind.Password => NarrowCell,
                ConfigFieldKind.Combo => NarrowCell,
                _ => NarrowTextKeys.Contains(field.Key) ? NarrowCell : WideCell
            };
        }

        public static List<ConfigField[]> Rows(IReadOnlyList<ConfigField> fields)
        {
            var rows = new List<ConfigField[]>();
            int index = 0;
            while (index < fields.Count)
            {
                var first = fields[index];
                int units = Units(first);
                if (units < FullRow && index + 1 < fields.Count)
                {
                    var second = fields[index + 1];
                    int next = Units(second);
                    if (string.Equals(first.Group, second.Group, StringComparison.Ordinal)
                        && PairRules.Contains(first.Key + "|" + second.Key)
                        && units + next <= FullRow)
                    {
                        rows.Add(new[] { first, second });
                        index += 2;
                        continue;
                    }
                }
                rows.Add(new[] { first });
                index++;
            }
            return rows;
        }

        public static string ColumnDefinitions(ConfigField[] row)
        {
            if (row.Length < 2) return "*";
            string left = Units(row[0]) == WideCell ? "2*" : "*";
            string right = Units(row[1]) == WideCell ? "2*" : "*";
            return left + ",10," + right;
        }
    }
}

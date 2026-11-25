/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using System.Collections.Generic;
using System.Linq;

namespace CrimsonX.Services
{
    public enum ConfigBuilderScope
    {
        CustomConfig,
        SavedPool
    }

    public enum ConfigFieldKind
    {
        Text,
        Password,
        Number,
        Check,
        Combo
    }

    public sealed class ConfigField
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
        public ConfigFieldKind Kind { get; init; }
        public string[] Options { get; init; } = Array.Empty<string>();
        public string Placeholder { get; init; } = "";
        public string Default { get; init; } = "";
        public bool Required { get; init; }
        public bool Multiline { get; init; }
        public string Group { get; init; } = "";
        public string EnableKey { get; init; } = "";
        public string[] EnableValues { get; init; } = Array.Empty<string>();
    }

    public sealed class ConfigProtocol
    {
        public string Id { get; init; } = "";
        public string Label { get; init; } = "";
        public bool InSlot { get; init; } = true;
        public bool InPool { get; init; } = true;
        public List<ConfigField> Fields { get; init; } = new List<ConfigField>();
        public List<string[]> ExclusiveSets { get; init; } = new List<string[]>();
    }

    public static class ConfigBuilderSchema
    {
        private const string Conn = "CONNECTION";
        private const string Auth = "AUTHENTICATION";
        private const string Tls = "TLS";
        private const string Transport = "TRANSPORT";
        private const string Advanced = "ADVANCED";
        private const string Awg = "AMNEZIAWG";

        private static ConfigField T(string key, string label, string group = Conn, string placeholder = "", string def = "", bool required = false, bool multiline = false, string enableKey = "", params string[] enableValues)
            => new ConfigField { Key = key, Label = label, Kind = ConfigFieldKind.Text, Group = group, Placeholder = placeholder, Default = def, Required = required, Multiline = multiline, EnableKey = enableKey, EnableValues = enableValues };

        private static ConfigField Pw(string key, string label, string group = Auth, string enableKey = "", params string[] enableValues)
            => new ConfigField { Key = key, Label = label, Kind = ConfigFieldKind.Password, Group = group, EnableKey = enableKey, EnableValues = enableValues };

        private static ConfigField Num(string key, string label, string def, string group = Conn, string enableKey = "", params string[] enableValues)
            => new ConfigField { Key = key, Label = label, Kind = ConfigFieldKind.Number, Group = group, Default = def, EnableKey = enableKey, EnableValues = enableValues };

        private static ConfigField Chk(string key, string label, string group = Advanced, string enableKey = "", params string[] enableValues)
            => new ConfigField { Key = key, Label = label, Kind = ConfigFieldKind.Check, Group = group, EnableKey = enableKey, EnableValues = enableValues };

        private static ConfigField Cmb(string key, string label, string[] options, string group = Conn, string def = "", string enableKey = "", params string[] enableValues)
            => new ConfigField { Key = key, Label = label, Kind = ConfigFieldKind.Combo, Options = options, Group = group, Default = def, EnableKey = enableKey, EnableValues = enableValues };

        private static ConfigField Server() => T("server", "SERVER", Conn, "example.com", "", required: true);
        private static ConfigField Port(string def) => Num("port", "PORT", def);
        private static ConfigField Label() => T("label", "LABEL (OPTIONAL)", Advanced, "shown in the config lists");

        private static ConfigField[] TlsFields(bool reality = true)
        {
            string[] security = reality ? new[] { "tls", "reality", "none" } : new[] { "tls", "none" };
            var list = new List<ConfigField>
            {
                Cmb("security", "SECURITY", security, Tls, "tls"),
                T("sni", "SNI", Tls, "example.com", "", false, false, "security", "tls", "reality"),
                T("fp", "FINGERPRINT", Tls, "chrome", "", false, false, "security", "tls", "reality"),
                T("alpn", "ALPN", Tls, "h2,http/1.1", "", false, false, "security", "tls", "reality"),
                Chk("insecure", "ALLOW INSECURE", Tls, "security", "tls", "reality"),
                T("ech", "ECH", Tls, "", "", false, false, "security", "tls", "reality"),
                T("vcn", "VERIFY CERT NAME", Tls, "", "", false, false, "security", "tls", "reality"),
                T("pcs", "PINNED CERT SHA256", Tls, "", "", false, false, "security", "tls", "reality"),
                T("pqv", "MLDSA65 VERIFY", Tls, "", "", false, false, "security", "tls", "reality")
            };
            if (reality)
            {
                list.Add(T("reality_pbk", "REALITY PUBLIC KEY", Tls, "", "", false, false, "security", "reality"));
                list.Add(T("reality_sid", "REALITY SHORT ID", Tls, "", "", false, false, "security", "reality"));
                list.Add(T("reality_spx", "REALITY SPIDER X", Tls, "/", "", false, false, "security", "reality"));
            }
            return list.ToArray();
        }

        private static readonly Lazy<List<ConfigProtocol>> AllProtocols = new Lazy<List<ConfigProtocol>>(CreateProtocols);

        public static IReadOnlyList<ConfigProtocol> Protocols(ConfigBuilderScope scope)
        {
            bool slot = scope == ConfigBuilderScope.CustomConfig;
            return AllProtocols.Value.Where(p => slot ? p.InSlot : p.InPool).ToList();
        }

        public static ConfigProtocol? Find(string id)
            => AllProtocols.Value.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

        private static List<ConfigProtocol> CreateProtocols()
        {
            List<ConfigField> F(List<ConfigField> seed, params ConfigField[][] blocks)
            {
                foreach (var block in blocks) seed.AddRange(block);
                return seed;
            }

            var protocols = new List<ConfigProtocol>();

            protocols.Add(new ConfigProtocol
            {
                Id = "vless",
                Label = "VLESS",
                Fields = F(new List<ConfigField>
                {
                    Server(), Port("443"),
                    T("uuid", "UUID", Auth, "", "", required: true),
                    T("encryption", "ENCRYPTION", Auth, "none", "none"),
                    T("flow", "FLOW", Auth, "xtls-rprx-vision"),
                    T("packet_encoding", "PACKET ENCODING", Auth, "xudp")
                }, TlsFields(), TransportFields(), new[]
                {
                    T("fm", "FINAL MASK (JSON)", Advanced, "", "", false, true),
                    Label()
                })
            });

            protocols.Add(new ConfigProtocol
            {
                Id = "vmess",
                Label = "VMess",
                Fields = F(new List<ConfigField>
                {
                    Server(), Port("443"),
                    T("uuid", "UUID", Auth, "", "", required: true),
                    Num("alter_id", "ALTER ID", "0", Auth),
                    Cmb("vmess_security", "ENCRYPTION", new[] { "auto", "none", "aes-128-gcm", "chacha20-poly1305" }, Auth, "auto"),
                    T("packet_encoding", "PACKET ENCODING", Auth, "xudp")
                }, new[]
                {
                    Cmb("security", "TLS", new[] { "tls", "none" }, Tls, "tls"),
                    T("sni", "SNI", Tls, "example.com", "", false, false, "security", "tls"),
                    T("alpn", "ALPN", Tls, "h2,http/1.1", "", false, false, "security", "tls"),
                    T("fp", "FINGERPRINT", Tls, "chrome", "", false, false, "security", "tls"),
                    Chk("insecure", "ALLOW INSECURE", Tls, "security", "tls"),
                    T("vcn", "VERIFY CERT NAME", Tls, "", "", false, false, "security", "tls"),
                    T("pcs", "PINNED CERT SHA256", Tls, "", "", false, false, "security", "tls")
                }, new[]
                {
                    Cmb("transport", "TRANSPORT", new[] { "raw", "ws", "grpc", "httpupgrade" }, Transport, "raw"),
                    Cmb("header_type", "HEADER TYPE", new[] { "none", "http" }, Transport, "none", "transport", "raw"),
                    T("path", "PATH", Transport, "/", "", false, false, "transport", "raw", "ws", "httpupgrade"),
                    T("host", "HOST", Transport, "example.com", "", false, false, "transport", "raw", "ws", "httpupgrade"),
                    T("service_name", "SERVICE NAME", Transport, "grpc", "", false, false, "transport", "grpc")
                }, new[]
                {
                    T("fm", "FINAL MASK (JSON)", Advanced, "", "", false, true),
                    Label()
                })
            });

            protocols.Add(new ConfigProtocol
            {
                Id = "trojan",
                Label = "Trojan",
                Fields = F(new List<ConfigField>
                {
                    Server(), Port("443"),
                    Pw("password", "PASSWORD", Auth)
                }, TlsFields(), TransportFields(), new[]
                {
                    T("fm", "FINAL MASK (JSON)", Advanced, "", "", false, true),
                    Label()
                })
            });

            protocols.Add(new ConfigProtocol
            {
                Id = "shadowsocks",
                Label = "Shadowsocks",
                Fields = new List<ConfigField>
                {
                    Server(), Port("8388"),
                    Cmb("method", "METHOD", new[] { "2022-blake3-aes-128-gcm", "2022-blake3-aes-256-gcm", "2022-blake3-chacha20-poly1305", "aes-128-gcm", "aes-256-gcm", "chacha20-ietf-poly1305", "xchacha20-ietf-poly1305" }, Auth, "aes-256-gcm"),
                    Pw("password", "PASSWORD", Auth),
                    Cmb("plugin", "PLUGIN", new[] { "none", "obfs-local", "v2ray-plugin" }, Advanced, "none"),
                    T("plugin_opts", "PLUGIN OPTIONS", Advanced, "obfs=tls;obfs-host=example.com", "", false, false, "plugin", "obfs-local", "v2ray-plugin"),
                    Label()
                }
            });

            protocols.Add(new ConfigProtocol
            {
                Id = "socks",
                Label = "SOCKS",
                Fields = F(new List<ConfigField>
                {
                    Server(), Port("1080"),
                    Cmb("version", "VERSION", new[] { "5", "4" }, Auth, "5"),
                    T("username", "USERNAME", Auth, "", "", false, false, "version", "5"),
                    Pw("password", "PASSWORD", Auth, "version", "5")
                }, new[]
                {
                    Cmb("security", "SECURITY", new[] { "none", "tls" }, Tls, "none"),
                    T("sni", "SNI", Tls, "example.com", "", false, false, "security", "tls"),
                    T("fp", "FINGERPRINT", Tls, "chrome", "", false, false, "security", "tls"),
                    T("alpn", "ALPN", Tls, "h2", "", false, false, "security", "tls"),
                    Chk("insecure", "ALLOW INSECURE", Tls, "security", "tls")
                }, new[] { Label() })
            });

            protocols.Add(new ConfigProtocol
            {
                Id = "http",
                Label = "HTTP(S)",
                Fields = new List<ConfigField>
                {
                    Server(), Port("8080"),
                    T("username", "USERNAME", Auth),
                    Pw("password", "PASSWORD", Auth),
                    Chk("tls", "USE TLS (HTTPS)", Tls),
                    T("sni", "SNI", Tls, "example.com", "", false, false, "tls", "1"),
                    T("fp", "FINGERPRINT", Tls, "chrome", "", false, false, "tls", "1"),
                    T("alpn", "ALPN", Tls, "h2", "", false, false, "tls", "1"),
                    Chk("insecure", "ALLOW INSECURE", Tls, "tls", "1"),
                    Label()
                }
            });

            protocols.Add(new ConfigProtocol
            {
                Id = "hysteria2",
                Label = "Hysteria2",
                Fields = new List<ConfigField>
                {
                    Server(), Port("443"),
                    Pw("password", "PASSWORD", Auth),
                    T("sni", "SNI", Tls, "example.com"),
                    Chk("insecure", "ALLOW INSECURE", Tls),
                    T("alpn", "ALPN", Tls, "h3"),
                    Cmb("obfs", "OBFS", new[] { "none", "salamander" }, Advanced, "none"),
                    T("obfs_password", "OBFS PASSWORD", Advanced, "", "", false, false, "obfs", "salamander"),
                    Label()
                }
            });

            protocols.Add(new ConfigProtocol
            {
                Id = "masque",
                Label = "MASQUE",
                Fields = new List<ConfigField>
                {
                    Server(), Port("443"),
                    T("username", "USERNAME", Auth),
                    Pw("password", "PASSWORD", Auth),
                    T("authority", "AUTHORITY", Advanced, "example.com"),
                    T("path", "PATH", Advanced, "/.well-known/masque/ip/{target}/{ipproto}/"),
                    T("sni", "SNI", Tls, "example.com"),
                    Cmb("alpn", "ALPN", new[] { "h2", "h3" }, Tls, "h2"),
                    T("fp", "FINGERPRINT", Tls, "chrome"),
                    T("rdns", "REMOTE DNS", Advanced, "1.1.1.1,2606:4700:4700::1111"),
                    Label()
                }
            });

            protocols.Add(new ConfigProtocol
            {
                Id = "wireguard",
                Label = "WireGuard",
                Fields = new List<ConfigField>
                {
                    Server(), Port("51820"),
                    Pw("private_key", "PRIVATE KEY", Auth),
                    T("public_key", "PEER PUBLIC KEY", Auth),
                    Pw("preshared_key", "PRESHARED KEY", Auth),
                    T("address", "TUNNEL ADDRESS", Conn, "10.0.0.2/32", "", required: true),
                    T("allowed_ips", "ALLOWED IPS", Advanced, "0.0.0.0/0,::/0", "0.0.0.0/0,::/0"),
                    Num("keepalive", "PERSISTENT KEEPALIVE", "25", Advanced),
                    Num("mtu", "MTU", "", Advanced),
                    Num("listen_port", "LISTEN PORT", "", Advanced),
                    Num("workers", "WORKERS", "", Advanced),
                    T("reserved", "RESERVED (a+b+c)", Advanced, "0+0+0", "", false, false, "awg", "0"),
                    Chk("awg", "AMNEZIAWG", Awg),
                    T("awg_dns", "DNS", Awg, "1.1.1.1", "", false, false, "awg", "1"),
                    Num("jc", "JC", "", Awg, "awg", "1"),
                    Num("jmin", "JMIN", "", Awg, "awg", "1"),
                    Num("jmax", "JMAX", "", Awg, "awg", "1"),
                    Num("s1", "S1", "", Awg, "awg", "1"),
                    Num("s2", "S2", "", Awg, "awg", "1"),
                    Num("s3", "S3", "", Awg, "awg", "1"),
                    Num("s4", "S4", "", Awg, "awg", "1"),
                    T("h1", "H1", Awg, "", "", false, false, "awg", "1"),
                    T("h2", "H2", Awg, "", "", false, false, "awg", "1"),
                    T("h3", "H3", Awg, "", "", false, false, "awg", "1"),
                    T("h4", "H4", Awg, "", "", false, false, "awg", "1"),
                    T("i1", "I1", Awg, "", "", false, false, "awg", "1"),
                    T("i2", "I2", Awg, "", "", false, false, "awg", "1"),
                    T("i3", "I3", Awg, "", "", false, false, "awg", "1"),
                    T("i4", "I4", Awg, "", "", false, false, "awg", "1"),
                    T("i5", "I5", Awg, "", "", false, false, "awg", "1"),
                    T("hpk", "HEADER PROTECTION KEY", Awg, "", "", false, false, "awg", "1"),
                    T("cpa", "CONTENT PADDING ADDITION", Awg, "", "", false, false, "awg", "1"),
                    T("rekey_after", "REKEY AFTER TIME", Awg, "", "", false, false, "awg", "1"),
                    T("rekey_timeout", "REKEY TIMEOUT", Awg, "", "", false, false, "awg", "1"),
                    T("reject_after", "REJECT AFTER TIME", Awg, "", "", false, false, "awg", "1"),
                    T("keepalive_timeout", "KEEPALIVE TIMEOUT", Awg, "", "", false, false, "awg", "1"),
                    Num("max_handshake", "MAX HANDSHAKE ATTEMPTS", "", Awg, "awg", "1"),
                    Chk("random_trailers", "RANDOM TRAILERS", Awg, "awg", "1"),
                    Chk("disable_cookies", "DISABLE COOKIES", Awg, "awg", "1"),
                    Label()
                }
            });

            protocols.Add(new ConfigProtocol
            {
                Id = "ikev2",
                Label = "IKEv2",
                Fields = new List<ConfigField>
                {
                    Server(), Port("500"),
                    T("username", "USERNAME (EAP)", Auth),
                    Pw("password", "PASSWORD (EAP)", Auth),
                    Pw("psk", "PRE-SHARED KEY", Auth),
                    T("remote_id", "REMOTE ID", Advanced, "same as the server"),
                    T("local_id", "LOCAL ID", Advanced, "same as the username"),
                    T("ca", "SERVER CA (PEM)", Advanced, "", "", false, true),
                    T("dns", "DNS", Advanced, "1.1.1.1"),
                    Num("mtu", "MTU", "1400", Advanced),
                    Label()
                },
                ExclusiveSets = new List<string[]>
                {
                    new[] { "username", "password" },
                    new[] { "psk" }
                }
            });

            protocols.Add(new ConfigProtocol
            {
                Id = "openvpn",
                Label = "OpenVPN",
                Fields = new List<ConfigField>
                {
                    Server(), Port("1194"),
                    Cmb("proto", "PROTOCOL", new[] { "udp", "tcp" }, Conn, "udp"),
                    T("remotes", "EXTRA REMOTES (HOST PORT [UDP|TCP] PER LINE)", Conn, "vpn2.example.com 1194", "", false, true),
                    Chk("remote_random", "REMOTE-RANDOM", Conn),
                    T("ca", "SERVER CA (PEM)", Auth, "", "", false, true),
                    T("client_cert", "CLIENT CERTIFICATE (PEM)", Auth, "", "", false, true),
                    T("client_key", "CLIENT KEY (PEM)", Auth, "", "", false, true),
                    Cmb("tls_auth_type", "TLS KEY TYPE", new[] { "none", "tls_auth", "tls_crypt", "tls_crypt_v2" }, Auth, "none"),
                    T("tls_auth_key", "TLS KEY (PEM)", Auth, "", "", false, true, "tls_auth_type", "tls_auth", "tls_crypt", "tls_crypt_v2"),
                    Cmb("tls_auth_direction", "TLS KEY DIRECTION", new[] { "client", "server" }, Auth, "client", "tls_auth_type", "tls_auth"),
                    Cmb("remote_cert_tls", "REMOTE CERT TLS", new[] { "none", "server", "client" }, Advanced, "none"),
                    T("peer_fingerprint", "PEER FINGERPRINT (SHA256)", Advanced),
                    Cmb("tls_version_min", "TLS VERSION MIN", new[] { "none", "1.2", "1.3" }, Advanced, "none"),
                    Cmb("tls_version_max", "TLS VERSION MAX", new[] { "none", "1.2", "1.3" }, Advanced, "none"),
                    T("cipher", "CIPHER", Advanced, "AES-256-GCM", "AES-256-GCM"),
                    T("data_ciphers", "DATA CIPHERS", Advanced, "AES-256-GCM:AES-128-GCM"),
                    T("data_ciphers_fallback", "DATA CIPHERS FALLBACK", Advanced, "AES-256-CBC"),
                    Cmb("auth_alg", "AUTH", new[] { "none", "SHA256", "SHA1", "MD5" }, Advanced, "none"),
                    Num("tun_mtu", "MTU", "", Advanced),
                    Num("mssfix", "MSSFIX", "", Advanced),
                    Num("reneg_sec", "RENEGOTIATE SECONDS", "", Advanced),
                    Num("ping", "PING", "", Advanced),
                    Num("ping_restart", "PING RESTART", "", Advanced),
                    Cmb("comp_lzo", "COMP-LZO", new[] { "none", "yes", "no", "adaptive" }, Advanced, "none"),
                    Cmb("compress", "COMPRESS", new[] { "none", "stub", "no" }, Advanced, "none"),
                    Chk("redirect_gateway", "REDIRECT GATEWAY", Advanced),
                    Chk("block_ipv6", "BLOCK IPV6", Advanced),
                    Chk("route_nopull", "ROUTE NOPULL", Advanced),
                    Num("fragment", "FRAGMENT", "", Advanced),
                    Num("explicit_exit_notify", "EXPLICIT EXIT NOTIFY", "", Advanced),
                    T("static_key", "STATIC KEY (PEM)", Advanced, "", "", false, true),
                    T("static_address", "STATIC ADDRESS", Advanced, "10.8.0.2/32"),
                    T("static_peer", "STATIC PEER ADDRESS", Advanced, "10.8.0.1"),
                    Cmb("key_direction", "STATIC KEY DIRECTION", new[] { "", "client", "server" }, Advanced, ""),
                    Label()
                },
                ExclusiveSets = new List<string[]>
                {
                    new[] { "ca", "client_cert", "client_key", "tls_auth_key", "remote_cert_tls", "peer_fingerprint", "tls_version_min", "tls_version_max", "data_ciphers", "data_ciphers_fallback" },
                    new[] { "static_key", "static_address", "static_peer", "key_direction" }
                }
            });

            protocols.Add(new ConfigProtocol
            {
                Id = "tuic",
                Label = "TUIC",
                InSlot = false,
                Fields = new List<ConfigField>
                {
                    Server(), Port("443"),
                    T("uuid", "UUID", Auth, "", "", required: true),
                    Pw("password", "PASSWORD", Auth),
                    Cmb("congestion", "CONGESTION CONTROL", new[] { "cubic", "new_reno", "bbr" }, Advanced, "bbr"),
                    Cmb("relay_mode", "UDP RELAY MODE", new[] { "native", "quic" }, Advanced, "native"),
                    T("sni", "SNI", Tls, "example.com"),
                    T("alpn", "ALPN", Tls, "h3"),
                    Chk("insecure", "ALLOW INSECURE", Tls),
                    Label()
                }
            });

            protocols.Add(new ConfigProtocol
            {
                Id = "anytls",
                Label = "AnyTLS",
                InSlot = false,
                Fields = new List<ConfigField>
                {
                    Server(), Port("443"),
                    Pw("password", "PASSWORD", Auth),
                    T("sni", "SNI", Tls, "example.com"),
                    T("alpn", "ALPN", Tls, "h2"),
                    Chk("insecure", "ALLOW INSECURE", Tls),
                    Label()
                }
            });

            return protocols;
        }

        private static ConfigField[] TransportFields()
        {
            return new[]
            {

                Cmb("transport", "TRANSPORT", new[] { "raw", "ws", "grpc", "httpupgrade", "xhttp", "kcp" }, Transport, "raw"),
                Cmb("header_type", "HEADER TYPE", new[] { "none", "http" }, Transport, "none", "transport", "raw"),
                T("path", "PATH", Transport, "/", "", false, false, "transport", "raw", "ws", "httpupgrade", "xhttp"),
                T("host", "HOST", Transport, "example.com", "", false, false, "transport", "raw", "ws", "httpupgrade", "xhttp"),
                T("service_name", "SERVICE NAME", Transport, "grpc", "", false, false, "transport", "grpc"),
                T("authority", "AUTHORITY", Transport, "", "", false, false, "transport", "grpc"),
                T("mode", "MODE", Transport, "gun / multi / auto", "", false, false, "transport", "grpc", "xhttp"),
                T("extra", "EXTRA (JSON)", Transport, "", "", false, true, "transport", "xhttp"),
                Num("kcp_mtu", "KCP MTU", "", Transport, "transport", "kcp"),
                Num("kcp_tti", "KCP TTI", "", Transport, "transport", "kcp"),
                Num("kcp_up", "KCP UPLINK", "", Transport, "transport", "kcp"),
                Num("kcp_down", "KCP DOWNLINK", "", Transport, "transport", "kcp"),
                Num("kcp_cwnd", "KCP CWND MULTIPLIER", "", Transport, "transport", "kcp"),
                Num("kcp_window", "KCP MAX SENDING WINDOW", "", Transport, "transport", "kcp")
            };
        }
    }
}

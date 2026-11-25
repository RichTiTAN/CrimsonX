/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public static class ConfigBuilder
    {
        public static (string Raw, string Error) Build(string protocolId, IReadOnlyDictionary<string, string> values)
        {
            var v = values ?? new Dictionary<string, string>();
            try
            {
                switch ((protocolId ?? "").Trim().ToLowerInvariant())
                {
                    case "vless": return BuildVless(v);
                    case "vmess": return BuildVmess(v);
                    case "trojan": return BuildTrojan(v);
                    case "shadowsocks": return BuildShadowsocks(v);
                    case "socks": return BuildSocks(v);
                    case "http": return BuildHttp(v);
                    case "hysteria2": return BuildHysteria2(v);
                    case "masque": return BuildMasque(v);
                    case "wireguard": return BuildWireGuard(v);
                    case "ikev2": return BuildIkev2(v);
                    case "openvpn": return BuildOpenVpn(v);
                    case "tuic": return BuildTuic(v);
                    case "anytls": return BuildAnyTls(v);
                    default: return ("", "Pick a protocol.");
                }
            }
            catch (Exception ex)
            {
                return ("", ex.Message);
            }
        }

        public static bool Validate(string raw, ConfigBuilderScope scope, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "the config is empty";
                return false;
            }
            if (TunnelConfigParser.TryParse(raw, out var tunnel) && tunnel != null)
            {
                if (!tunnel.Success)
                {
                    error = tunnel.Error.Length > 0 ? tunnel.Error : "this tunnel config could not be read";
                    return false;
                }
                return true;
            }
            var target = scope == ConfigBuilderScope.CustomConfig ? ConfigTarget.Xray : ConfigTarget.Pool;
            return ConfigConverter.TryAcceptFor(raw, target, out error);
        }

        private static string V(IReadOnlyDictionary<string, string> v, string key)
            => v.TryGetValue(key, out string value) ? (value ?? "").Trim() : "";

        private static bool B(IReadOnlyDictionary<string, string> v, string key) => V(v, key) == "1";

        private static int Int(IReadOnlyDictionary<string, string> v, string key, int fallback)
            => int.TryParse(V(v, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0 ? parsed : fallback;

        private static string Esc(string value) => Uri.EscapeDataString((value ?? "").Trim());

        private static string Host(string server)
            => server.Contains(':') && !server.StartsWith("[", StringComparison.Ordinal) ? "[" + server + "]" : server;

        private static string Fragment(string label)
            => string.IsNullOrWhiteSpace(label) ? "" : "#" + Uri.EscapeDataString(label.Trim());

        private static string Query(IEnumerable<(string Key, string Value)> pairs)
        {
            var parts = pairs
                .Where(p => !string.IsNullOrWhiteSpace(p.Value))
                .Select(p => p.Key + "=" + Uri.EscapeDataString(p.Value.Trim()))
                .ToList();
            return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
        }

        private static string Type(IReadOnlyDictionary<string, string> v)
        {
            string type = V(v, "transport").ToLowerInvariant();
            return type.Length == 0 ? "raw" : type;
        }

        private static List<(string, string)> TransportPairs(IReadOnlyDictionary<string, string> v)
        {
            string type = Type(v);
            var pairs = new List<(string, string)> { ("type", type) };
            bool pathHost = type == "raw" || type == "ws" || type == "httpupgrade" || type == "xhttp";
            if (type == "raw" && V(v, "header_type") == "http") pairs.Add(("headerType", "http"));
            if (pathHost)
            {
                pairs.Add(("path", V(v, "path")));
                pairs.Add(("host", V(v, "host")));
            }
            if (type == "grpc")
            {
                pairs.Add(("serviceName", V(v, "service_name")));
                pairs.Add(("authority", V(v, "authority")));
                pairs.Add(("mode", V(v, "mode")));
            }
            if (type == "xhttp") pairs.Add(("extra", V(v, "extra")));
            if (type == "kcp")
            {
                pairs.Add(("mtu", V(v, "kcp_mtu")));
                pairs.Add(("tti", V(v, "kcp_tti")));
                pairs.Add(("uplinkCapacity", V(v, "kcp_up")));
                pairs.Add(("downlinkCapacity", V(v, "kcp_down")));
                pairs.Add(("cwndMultiplier", V(v, "kcp_cwnd")));
                pairs.Add(("maxSendingWindow", V(v, "kcp_window")));
            }
            return pairs;
        }

        private static List<(string, string)> TlsPairs(IReadOnlyDictionary<string, string> v, bool reality)
        {
            string security = V(v, "security");
            var pairs = new List<(string, string)> { ("security", security == "none" ? "" : security) };
            if (security == "tls" || security == "reality")
            {
                pairs.Add(("sni", V(v, "sni")));
                pairs.Add(("fp", V(v, "fp")));
                pairs.Add(("alpn", V(v, "alpn")));
                if (B(v, "insecure")) pairs.Add(("allowInsecure", "1"));
                pairs.Add(("ech", V(v, "ech")));
                pairs.Add(("vcn", V(v, "vcn")));
                pairs.Add(("pcs", V(v, "pcs")));
                pairs.Add(("pqv", V(v, "pqv")));
            }
            if (reality && security == "reality")
            {
                pairs.Add(("pbk", V(v, "reality_pbk")));
                pairs.Add(("sid", V(v, "reality_sid")));
                pairs.Add(("spx", V(v, "reality_spx")));
            }
            return pairs;
        }

        private static (string Raw, string Error) BuildVless(IReadOnlyDictionary<string, string> v)
        {
            string server = V(v, "server"), uuid = V(v, "uuid");
            if (server.Length == 0 || uuid.Length == 0) return ("", "Enter the server and the UUID.");
            int port = Int(v, "port", 443);
            var pairs = new List<(string, string)>
            {
                ("encryption", V(v, "encryption").Length > 0 ? V(v, "encryption") : "none"),
                ("flow", V(v, "flow")),
                ("packetEncoding", V(v, "packet_encoding"))
            };
            pairs.AddRange(TlsPairs(v, reality: true));
            pairs.AddRange(TransportPairs(v));
            pairs.Add(("fm", V(v, "fm")));
            string raw = $"vless://{Esc(uuid)}@{Host(server)}:{port}{Query(pairs)}{Fragment(V(v, "label"))}";
            return (raw, "");
        }

        private static (string Raw, string Error) BuildVmess(IReadOnlyDictionary<string, string> v)
        {
            string server = V(v, "server"), uuid = V(v, "uuid");
            if (server.Length == 0 || uuid.Length == 0) return ("", "Enter the server and the UUID.");
            string type = Type(v);
            var json = new JObject
            {
                ["v"] = "2",
                ["ps"] = V(v, "label"),
                ["add"] = server,
                ["port"] = Int(v, "port", 443).ToString(CultureInfo.InvariantCulture),
                ["id"] = uuid,
                ["aid"] = V(v, "alter_id").Length > 0 ? V(v, "alter_id") : "0",
                ["scy"] = V(v, "vmess_security").Length > 0 ? V(v, "vmess_security") : "auto",
                ["net"] = type,
                ["type"] = V(v, "header_type") == "http" ? "http" : "none",
                ["host"] = V(v, "host"),
                ["path"] = V(v, "path"),
                ["tls"] = V(v, "security") == "tls" ? "tls" : "",
                ["sni"] = V(v, "sni"),
                ["alpn"] = V(v, "alpn"),
                ["fp"] = V(v, "fp"),
                ["allowInsecure"] = B(v, "insecure") ? "1" : "",
                ["packetEncoding"] = V(v, "packet_encoding")
            };
            string body = Convert.ToBase64String(Encoding.UTF8.GetBytes(json.ToString(Formatting.None)));
            return ($"vmess://{body}", "");
        }

        private static (string Raw, string Error) BuildTrojan(IReadOnlyDictionary<string, string> v)
        {
            string server = V(v, "server"), password = V(v, "password");
            if (server.Length == 0 || password.Length == 0) return ("", "Enter the server and the password.");
            int port = Int(v, "port", 443);
            var pairs = new List<(string, string)>();
            pairs.AddRange(TlsPairs(v, reality: true));
            pairs.AddRange(TransportPairs(v));
            pairs.Add(("fm", V(v, "fm")));
            string raw = $"trojan://{Esc(password)}@{Host(server)}:{port}{Query(pairs)}{Fragment(V(v, "label"))}";
            return (raw, "");
        }

        private static (string Raw, string Error) BuildShadowsocks(IReadOnlyDictionary<string, string> v)
        {
            string server = V(v, "server"), method = V(v, "method"), password = V(v, "password");
            if (server.Length == 0 || method.Length == 0 || password.Length == 0)
                return ("", "Enter the server, the method and the password.");
            int port = Int(v, "port", 8388);
            string plugin = V(v, "plugin");
            var pairs = new List<(string, string)>();
            if (plugin.Length > 0 && plugin != "none")
            {
                string opts = V(v, "plugin_opts");
                pairs.Add(("plugin", opts.Length > 0 ? plugin + ";" + opts : plugin));
            }
            string creds = Convert.ToBase64String(Encoding.UTF8.GetBytes(method + ":" + password));
            return ($"ss://{creds}@{Host(server)}:{port}{Query(pairs)}{Fragment(V(v, "label"))}", "");
        }

        private static (string Raw, string Error) BuildSocks(IReadOnlyDictionary<string, string> v)
        {
            string server = V(v, "server");
            if (server.Length == 0) return ("", "Enter the server.");
            int port = Int(v, "port", 1080);
            string version = V(v, "version").Length > 0 ? V(v, "version") : "5";
            if (version == "4")
                return ($"socks4://{Host(server)}:{port}{Fragment(V(v, "label"))}", "");
            string user = V(v, "username"), pass = V(v, "password");
            string userInfo = user.Length > 0 ? Esc(user) + ":" + Esc(pass) + "@" : "";
            var pairs = new List<(string, string)>();
            if (V(v, "security") == "tls")
            {
                pairs.Add(("security", "tls"));
                pairs.Add(("sni", V(v, "sni")));
                pairs.Add(("fp", V(v, "fp")));
                pairs.Add(("alpn", V(v, "alpn")));
                if (B(v, "insecure")) pairs.Add(("allowInsecure", "1"));
            }
            return ($"socks5://{userInfo}{Host(server)}:{port}{Query(pairs)}{Fragment(V(v, "label"))}", "");
        }

        private static (string Raw, string Error) BuildHttp(IReadOnlyDictionary<string, string> v)
        {
            string server = V(v, "server");
            if (server.Length == 0) return ("", "Enter the server.");
            bool tls = B(v, "tls");
            int port = Int(v, "port", tls ? 443 : 8080);
            string user = V(v, "username"), pass = V(v, "password");
            string userInfo = user.Length > 0 ? Esc(user) + ":" + Esc(pass) + "@" : "";
            var pairs = new List<(string, string)>();
            if (tls)
            {
                pairs.Add(("security", "tls"));
                pairs.Add(("sni", V(v, "sni")));
                pairs.Add(("fp", V(v, "fp")));
                pairs.Add(("alpn", V(v, "alpn")));
                if (B(v, "insecure")) pairs.Add(("allowInsecure", "1"));
            }
            string scheme = tls ? "https" : "http";
            return ($"{scheme}://{userInfo}{Host(server)}:{port}{Query(pairs)}{Fragment(V(v, "label"))}", "");
        }

        private static (string Raw, string Error) BuildHysteria2(IReadOnlyDictionary<string, string> v)
        {
            string server = V(v, "server"), password = V(v, "password");
            if (server.Length == 0 || password.Length == 0) return ("", "Enter the server and the password.");
            int port = Int(v, "port", 443);
            var pairs = new List<(string, string)>
            {
                ("sni", V(v, "sni")),
                ("alpn", V(v, "alpn"))
            };
            if (B(v, "insecure")) pairs.Add(("insecure", "1"));
            if (V(v, "obfs") == "salamander")
            {
                pairs.Add(("obfs", "salamander"));
                pairs.Add(("obfs-password", V(v, "obfs_password")));
            }
            return ($"hysteria2://{Esc(password)}@{Host(server)}:{port}{Query(pairs)}{Fragment(V(v, "label"))}", "");
        }

        private static (string Raw, string Error) BuildMasque(IReadOnlyDictionary<string, string> v)
        {
            string server = V(v, "server");
            if (server.Length == 0) return ("", "Enter the server.");
            int port = Int(v, "port", 443);
            string user = V(v, "username"), pass = V(v, "password");
            string userInfo = user.Length > 0 ? Esc(user) + ":" + Esc(pass) + "@" : "";
            var pairs = new List<(string, string)>
            {
                ("path", V(v, "path")),
                ("host", V(v, "authority")),
                ("sni", V(v, "sni")),
                ("alpn", V(v, "alpn")),
                ("fp", V(v, "fp")),
                ("rdns", V(v, "rdns"))
            };
            return ($"masque://{userInfo}{Host(server)}:{port}{Query(pairs)}{Fragment(V(v, "label"))}", "");
        }

        private static string AwgValue(IReadOnlyDictionary<string, string> v, string key) => key switch
        {
            "rekeyaftertime" => V(v, "rekey_after"),
            "rekeytimeout" => V(v, "rekey_timeout"),
            "rejectaftertime" => V(v, "reject_after"),
            "keepalivetimeout" => V(v, "keepalive_timeout"),
            "maxhandshakeattempts" => V(v, "max_handshake"),
            _ => V(v, key)
        };

        private static (string Raw, string Error) BuildWireGuard(IReadOnlyDictionary<string, string> v)
        {
            string server = V(v, "server"), priv = V(v, "private_key"), pub = V(v, "public_key"), address = V(v, "address");
            if (server.Length == 0 || priv.Length == 0 || pub.Length == 0 || address.Length == 0)
                return ("", "Enter the server, the private key, the peer public key and the tunnel address.");
            int port = Int(v, "port", 51820);
            bool awg = B(v, "awg");
            var pairs = new List<(string, string)>
            {
                ("publickey", pub),
                ("address", address),
                ("allowedips", V(v, "allowed_ips")),
                ("psk", V(v, "preshared_key")),
                ("keepalive", V(v, "keepalive")),
                ("mtu", V(v, "mtu")),
                ("listenport", V(v, "listen_port")),
                ("workers", V(v, "workers")),
                ("reserved", awg ? "" : V(v, "reserved"))
            };
            if (awg)
            {
                pairs.Add(("dns", V(v, "awg_dns")));
                foreach (string key in new[] { "jc", "jmin", "jmax", "s1", "s2", "s3", "s4", "h1", "h2", "h3", "h4", "i1", "i2", "i3", "i4", "i5", "hpk", "cpa", "rekeyaftertime", "rekeytimeout", "rejectaftertime", "keepalivetimeout", "maxhandshakeattempts" })
                    pairs.Add((key, AwgValue(v, key)));
                if (B(v, "random_trailers")) pairs.Add(("randomtrailers", "1"));
                if (B(v, "disable_cookies")) pairs.Add(("disablecookies", "1"));
            }
            return ($"wireguard://{Esc(priv)}@{Host(server)}:{port}{Query(pairs)}{Fragment(V(v, "label"))}", "");
        }

        private static (string Raw, string Error) BuildIkev2(IReadOnlyDictionary<string, string> v)
        {
            string server = V(v, "server");
            if (server.Length == 0) return ("", "Enter the server address.");
            bool psk = V(v, "psk").Length > 0;
            string username = V(v, "username"), password = V(v, "password");
            if (!psk && (username.Length == 0 || password.Length == 0))
                return ("", "Enter a username and password, or a pre-shared key.");
            var profile = new JObject
            {
                ["type"] = "ipsec",
                ["server"] = server,
                ["server_port"] = Int(v, "port", 500),
                ["auth"] = psk ? "psk" : "eap"
            };
            if (psk)
            {
                profile["psk"] = V(v, "psk");
            }
            else
            {
                profile["username"] = username;
                profile["password"] = password;
            }
            foreach (var (key, field) in new[] { ("remote_id", "remote_id"), ("local_id", "local_id"), ("ca", "ca"), ("dns", "dns") })
            {
                string value = V(v, key);
                if (value.Length > 0) profile[field] = value;
            }
            int mtu = Int(v, "mtu", 0);
            if (mtu > 0) profile["mtu"] = mtu;
            string label = V(v, "label");
            if (label.Length > 0) profile["name"] = label;
            return (profile.ToString(Formatting.Indented), "");
        }

        private static void AppendIf(StringBuilder sb, string directive, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine(directive + " " + value.Trim());
        }

        private static void AppendInline(StringBuilder sb, string tag, string pem)
        {
            if (string.IsNullOrWhiteSpace(pem)) return;
            string text = pem.Trim();
            if (text.Contains("<" + tag + ">", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine(text);
                return;
            }
            sb.AppendLine($"<{tag}>");
            sb.AppendLine(text);
            sb.AppendLine($"</{tag}>");
        }

        private static bool TryParseExtraRemotes(string text, out List<(string Host, int Port, string Proto)> remotes, out string error)
        {
            remotes = new List<(string Host, int Port, string Proto)>();
            error = "";
            foreach (string rawLine in (text ?? "").Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    error = $"Extra remote \"{line}\" needs a host and a port.";
                    return false;
                }
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int remotePort)
                    || remotePort < 1 || remotePort > 65535)
                {
                    error = $"Extra remote \"{line}\" has an invalid port.";
                    return false;
                }
                string remoteProto = parts.Length > 2 ? parts[2].ToLowerInvariant() : "";
                if (remoteProto.Length > 0 && remoteProto != "udp" && remoteProto != "tcp")
                {
                    error = $"Extra remote \"{line}\" names an unknown protocol; use udp or tcp.";
                    return false;
                }
                remotes.Add((parts[0], remotePort, remoteProto));
            }
            return true;
        }

        private static (string Raw, string Error) BuildOpenVpn(IReadOnlyDictionary<string, string> v)
        {
            string server = V(v, "server");
            if (server.Length == 0) return ("", "Enter the server address.");
            string ca = V(v, "ca"), cert = V(v, "client_cert"), key = V(v, "client_key");
            string staticKey = V(v, "static_key"), staticAddress = V(v, "static_address");
            bool staticMode = staticKey.Length > 0 || staticAddress.Length > 0;
            if (!staticMode && ca.Length == 0 && V(v, "peer_fingerprint").Length == 0)
                return ("", "Enter the server CA (or a peer fingerprint).");
            if (staticMode && (staticKey.Length == 0 || staticAddress.Length == 0))
                return ("", "A static-key profile needs both the key and the tunnel address.");
            if ((cert.Length > 0) != (key.Length > 0))
                return ("", "The client certificate and its key go together.");
            if (!TryParseExtraRemotes(V(v, "remotes"), out var extraRemotes, out string remotesError))
                return ("", remotesError);
            if (staticMode && extraRemotes.Count > 0)
                return ("", "Static-key mode uses a single remote; remove the extra remotes.");
            string proto = V(v, "proto").Length > 0 ? V(v, "proto") : "udp";
            int port = Int(v, "port", 1194);
            string label = V(v, "label");
            var sb = new StringBuilder();
            if (label.Length > 0) sb.AppendLine("# name: " + label);
            sb.AppendLine("dev tun");
            sb.AppendLine("proto " + proto);
            sb.AppendLine($"remote {server} {port}");
            foreach (var remote in extraRemotes)
            {
                sb.AppendLine(remote.Proto.Length > 0
                    ? $"remote {remote.Host} {remote.Port} {remote.Proto}"
                    : $"remote {remote.Host} {remote.Port}");
            }
            if (!staticMode && B(v, "remote_random")) sb.AppendLine("remote-random");
            if (staticMode)
            {
                AppendInline(sb, "secret", staticKey);
                sb.AppendLine($"ifconfig {staticAddress} {(V(v, "static_peer").Length > 0 ? V(v, "static_peer") : "10.8.0.1")}");
                AppendIf(sb, "cipher", V(v, "cipher"));
                AppendIf(sb, "auth", V(v, "auth_alg") == "none" ? "" : V(v, "auth_alg"));
                AppendIf(sb, "key-direction", V(v, "key_direction") == "server" ? "0" : "1");
            }
            else
            {
                sb.AppendLine("client");
                sb.AppendLine("resolv-retry infinite");
                sb.AppendLine("nobind");
                sb.AppendLine("persist-key");
                sb.AppendLine("persist-tun");
                AppendIf(sb, "remote-cert-tls", V(v, "remote_cert_tls") == "none" ? "" : V(v, "remote_cert_tls"));
                AppendIf(sb, "tls-version-min", V(v, "tls_version_min") == "none" ? "" : V(v, "tls_version_min"));
                AppendIf(sb, "tls-version-max", V(v, "tls_version_max") == "none" ? "" : V(v, "tls_version_max"));
                AppendIf(sb, "peer-fingerprint", V(v, "peer_fingerprint"));
                AppendIf(sb, "cipher", V(v, "cipher"));
                AppendIf(sb, "data-ciphers", V(v, "data_ciphers"));
                AppendIf(sb, "data-ciphers-fallback", V(v, "data_ciphers_fallback"));
                AppendIf(sb, "auth", V(v, "auth_alg") == "none" ? "" : V(v, "auth_alg"));
                AppendIf(sb, "tun-mtu", V(v, "tun_mtu"));
                AppendIf(sb, "mssfix", V(v, "mssfix"));
                AppendIf(sb, "reneg-sec", V(v, "reneg_sec"));
                AppendIf(sb, "ping", V(v, "ping"));
                AppendIf(sb, "ping-restart", V(v, "ping_restart"));
                AppendIf(sb, "comp-lzo", V(v, "comp_lzo") == "none" ? "" : V(v, "comp_lzo"));
                AppendIf(sb, "compress", V(v, "compress") == "none" ? "" : V(v, "compress"));
                if (B(v, "redirect_gateway")) sb.AppendLine("redirect-gateway def1");
                if (B(v, "block_ipv6")) sb.AppendLine("block-ipv6");
                if (B(v, "route_nopull")) sb.AppendLine("route-nopull");
                AppendIf(sb, "fragment", V(v, "fragment"));
                AppendIf(sb, "explicit-exit-notify", V(v, "explicit_exit_notify"));
                sb.AppendLine("auth-user-pass");
                AppendInline(sb, "ca", ca);
                AppendInline(sb, "cert", cert);
                AppendInline(sb, "key", key);
                string tlsType = V(v, "tls_auth_type");
                if (tlsType != "none" && V(v, "tls_auth_key").Length > 0)
                {
                    string tag = tlsType == "tls_auth" ? "tls-auth" : tlsType == "tls_crypt" ? "tls-crypt" : "tls-crypt-v2";
                    AppendInline(sb, tag, V(v, "tls_auth_key"));
                    if (tlsType == "tls_auth") AppendIf(sb, "key-direction", V(v, "tls_auth_direction") == "server" ? "0" : "1");
                }
            }
            return (sb.ToString().TrimEnd(), "");
        }

        private static (string Raw, string Error) BuildTuic(IReadOnlyDictionary<string, string> v)
        {
            string server = V(v, "server"), uuid = V(v, "uuid"), password = V(v, "password");
            if (server.Length == 0 || uuid.Length == 0 || password.Length == 0)
                return ("", "Enter the server, the UUID and the password.");
            int port = Int(v, "port", 443);
            var pairs = new List<(string, string)>
            {
                ("congestion_control", V(v, "congestion")),
                ("udp_relay_mode", V(v, "relay_mode")),
                ("sni", V(v, "sni")),
                ("alpn", V(v, "alpn"))
            };
            if (B(v, "insecure")) pairs.Add(("insecure", "1"));
            string userInfo = Esc(uuid) + ":" + Esc(password) + "@";
            return ($"tuic://{userInfo}{Host(server)}:{port}{Query(pairs)}{Fragment(V(v, "label"))}", "");
        }

        private static (string Raw, string Error) BuildAnyTls(IReadOnlyDictionary<string, string> v)
        {
            string server = V(v, "server"), password = V(v, "password");
            if (server.Length == 0 || password.Length == 0) return ("", "Enter the server and the password.");
            int port = Int(v, "port", 443);
            var pairs = new List<(string, string)>
            {
                ("sni", V(v, "sni")),
                ("alpn", V(v, "alpn"))
            };
            if (B(v, "insecure")) pairs.Add(("insecure", "1"));
            return ($"anytls://{Esc(password)}@{Host(server)}:{port}{Query(pairs)}{Fragment(V(v, "label"))}", "");
        }

        public static bool TryRead(string raw, ConfigBuilderScope scope, out string protocolId, out Dictionary<string, string> values, out string error)
        {
            protocolId = "";
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = "";
            string text = (raw ?? "").Trim();
            if (text.Length == 0)
            {
                error = "the config is empty";
                return false;
            }
            bool ok = TunnelConfigParser.TryParse(text, out var tunnel) && tunnel?.Endpoint != null
                ? ReadTunnel(tunnel, out protocolId, out values, out error)
                : ReadLink(text, out protocolId, out values, out error);
            if (!ok) return false;
            string explicitLabel = ConfigConverter.ExplicitLabelFor(text);
            if (explicitLabel.Length > 0) values["label"] = explicitLabel;
            var protocol = ConfigBuilderSchema.Find(protocolId);
            if (protocol == null)
            {
                error = "this config cannot be opened in the editor";
                return false;
            }
            if (scope == ConfigBuilderScope.CustomConfig && !protocol.InSlot)
            {
                error = "this config is only supported in the saved configs";
                return false;
            }
            return true;
        }

        private static string Str(JToken token)
            => token == null || token.Type == JTokenType.Null ? "" : token.Type == JTokenType.String ? token.ToString() : token.ToString(Formatting.None);

        private static string Join(JToken token, string separator)
            => token is JArray array ? string.Join(separator, array.Select(x => x?.ToString() ?? "")) : Str(token);

        private static string Lines(JToken token)
            => token is JArray array ? string.Join("\n", array.Select(x => x?.ToString() ?? "")) : Str(token);

        private static bool Truthy(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return false;
            if (token.Type == JTokenType.Boolean) return token.Value<bool>();
            string text = token.ToString();
            return text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1" || text.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string Get(Dictionary<string, string> query, string key)
            => query.TryGetValue(key, out string value) ? (value ?? "").Trim() : "";

        private static bool TryBase64(string text, out string decoded)
        {
            decoded = "";
            try
            {
                string body = (text ?? "").Trim().Replace('-', '+').Replace('_', '/');
                switch (body.Length % 4)
                {
                    case 2: body += "=="; break;
                    case 3: body += "="; break;
                }
                decoded = Encoding.UTF8.GetString(Convert.FromBase64String(body));
                return decoded.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static Dictionary<string, string> ReadQuery(Uri uri)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string query = uri.Query;
            if (query.StartsWith("?", StringComparison.Ordinal)) query = query.Substring(1);
            foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string key = eq < 0 ? pair : pair.Substring(0, eq);
                string value = eq < 0 ? "" : pair.Substring(eq + 1);
                result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value);
            }
            return result;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            return "";
        }

        private static bool ReadTunnel(TunnelParseResult tunnel, out string protocolId, out Dictionary<string, string> values, out string error)
        {
            protocolId = "";
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = "";
            var ep = tunnel.Endpoint;
            string type = Str(ep["type"]);
            if (type.Equals("ipsec", StringComparison.OrdinalIgnoreCase) || type.Equals("ikev2", StringComparison.OrdinalIgnoreCase))
            {
                protocolId = "ikev2";
                values["server"] = Str(ep["server"]);
                values["port"] = Str(ep["server_port"]);
                if (Str(ep["auth"]).Equals("psk", StringComparison.OrdinalIgnoreCase))
                {
                    values["psk"] = Str(ep["psk"]);
                }
                else
                {
                    values["username"] = Str(ep["username"]);
                    values["password"] = Str(ep["password"]);
                }
                values["remote_id"] = Str(ep["remote_id"]);
                values["local_id"] = Str(ep["local_id"]);
                values["ca"] = Str(ep["ca"]);
                values["dns"] = Str(ep["dns"]);
                values["mtu"] = Str(ep["mtu"]);
                return true;
            }
            if (type.StartsWith("openvpn", StringComparison.OrdinalIgnoreCase))
                return ReadOpenVpnTunnel(ep, out protocolId, out values, out error);
            if (type.Equals("wireguard", StringComparison.OrdinalIgnoreCase) || type.Equals("awg", StringComparison.OrdinalIgnoreCase))
                return ReadWireGuardTunnel(ep, out protocolId, out values, out error);
            error = "this tunnel config cannot be opened in the editor";
            return false;
        }

        private static bool ReadOpenVpnTunnel(JObject ep, out string protocolId, out Dictionary<string, string> values, out string error)
        {
            protocolId = "openvpn";
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = "";
            var servers = (ep["servers"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
            var server = servers.FirstOrDefault();
            values["server"] = Str(server?["server"]);
            values["port"] = Str(server?["server_port"]);
            string network = Str(ep["network"]);
            values["proto"] = network;
            if (servers.Count > 1)
            {
                var extras = new List<string>();
                for (int index = 1; index < servers.Count; index++)
                {
                    string host = Str(servers[index]["server"]);
                    if (host.Length == 0) continue;
                    string line = host + " " + Str(servers[index]["server_port"]);
                    string remoteNetwork = Str(servers[index]["network"]);
                    if (remoteNetwork.Length > 0 && !remoteNetwork.Equals(network, StringComparison.OrdinalIgnoreCase))
                        line += " " + remoteNetwork;
                    extras.Add(line);
                }
                Set(values, "remotes", string.Join("\n", extras));
            }
            if (Truthy(ep["remote_random"])) values["remote_random"] = "1";
            var tls = ep["tls"] as JObject;
            values["ca"] = Lines(tls?["certificate"]);
            values["client_cert"] = Lines(tls?["client_certificate"]);
            values["client_key"] = Lines(tls?["client_key"]);
            var wrap = tls?["control_wrap"] as JObject;
            values["tls_auth_type"] = Str(wrap?["type"]);
            values["tls_auth_key"] = Lines(wrap?["key"]);
            values["tls_auth_direction"] = Str(wrap?["direction"]);
            values["remote_cert_tls"] = Str(tls?["remote_certificate_tls"]);
            values["peer_fingerprint"] = Join(tls?["peer_fingerprint"], ",");
            values["tls_version_min"] = Str(tls?["version_min"]);
            values["tls_version_max"] = Str(tls?["version_max"]);
            values["cipher"] = Str(ep["cipher"]);
            values["data_ciphers"] = Join(ep["data_ciphers"], ":");
            values["data_ciphers_fallback"] = Str(ep["data_ciphers_fallback"]);
            values["auth_alg"] = Str(ep["auth"]);
            values["tun_mtu"] = Str(ep["mtu"]);
            values["mssfix"] = Str(ep["mss_fix"]);
            values["reneg_sec"] = Str(ep["renegotiate_interval"]);
            values["ping"] = Str(ep["ping_interval"]);
            values["ping_restart"] = Str(ep["ping_restart"]);
            values["comp_lzo"] = Str(ep["compression_lzo"]);
            values["compress"] = Str(ep["compression"]);
            if (Truthy(ep["redirect_gateway"])) values["redirect_gateway"] = "1";
            if (Truthy(ep["block_ipv6"])) values["block_ipv6"] = "1";
            if (Truthy(ep["route_nopull"])) values["route_nopull"] = "1";
            values["fragment"] = Str(ep["fragment"]);
            values["explicit_exit_notify"] = Str(ep["explicit_exit_notify"]);
            values["static_key"] = Lines(ep["static_key"]);
            values["static_address"] = Join(ep["address"], ",");
            values["static_peer"] = Str(ep["peer_address"]);
            values["key_direction"] = Str(ep["key_direction"]);
            return true;
        }

        private static bool ReadWireGuardTunnel(JObject ep, out string protocolId, out Dictionary<string, string> values, out string error)
        {
            protocolId = "wireguard";
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = "";
            var peer = (ep["peers"] as JArray)?.OfType<JObject>().FirstOrDefault();
            values["server"] = Str(peer?["address"]);
            values["port"] = Str(peer?["port"]);
            values["private_key"] = Str(ep["private_key"]);
            values["public_key"] = Str(peer?["public_key"]);
            values["preshared_key"] = FirstNonEmpty(Str(peer?["pre_shared_key"]), Str(peer?["preshared_key"]));
            values["address"] = Join(ep["address"], ",");
            values["allowed_ips"] = Join(peer?["allowed_ips"], ",");
            values["keepalive"] = Str(peer?["persistent_keepalive_interval"]);
            values["mtu"] = Str(ep["mtu"]);
            values["listen_port"] = Str(ep["listen_port"]);
            values["workers"] = Str(ep["workers"]);
            values["reserved"] = Join(peer?["reserved"], "+");
            if (Str(ep["type"]).Equals("awg", StringComparison.OrdinalIgnoreCase))
            {
                values["awg"] = "1";
                foreach (string key in new[] { "jc", "jmin", "jmax", "s1", "s2", "s3", "s4", "h1", "h2", "h3", "h4", "i1", "i2", "i3", "i4", "i5" })
                    values[key] = Str(ep[key]);
                values["hpk"] = Str(ep["header_protection_key"]);
                values["cpa"] = Str(ep["content_padding_addition"]);
                values["rekey_after"] = Str(ep["rekey_after_time"]);
                values["rekey_timeout"] = Str(ep["rekey_timeout"]);
                values["reject_after"] = Str(ep["reject_after_time"]);
                values["keepalive_timeout"] = Str(ep["keepalive_timeout"]);
                values["max_handshake"] = Str(ep["max_handshake_attempts"]);
                if (Truthy(ep["random_trailers"])) values["random_trailers"] = "1";
                if (Truthy(ep["disable_cookies"])) values["disable_cookies"] = "1";
            }
            return true;
        }

        private static void Set(Dictionary<string, string> values, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) values[key] = value.Trim();
        }

        private static bool ReadLink(string text, out string protocolId, out Dictionary<string, string> values, out string error)
        {
            protocolId = "";
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = "";
            int idx = text.IndexOf("://", StringComparison.Ordinal);
            if (idx <= 0)
            {
                error = "this config cannot be read";
                return false;
            }
            string scheme = text.Substring(0, idx).ToLowerInvariant();
            if (scheme == "vmess") return ReadVmessLink(text, out protocolId, out values, out error);
            if (scheme == "ss") return ReadShadowsocksLink(text, out protocolId, out values, out error);
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
            {
                error = "this config cannot be read";
                return false;
            }
            var query = ReadQuery(uri);
            switch (scheme)
            {
                case "vless": return ReadVlessLink(uri, query, out protocolId, out values, out error);
                case "trojan": return ReadTrojanLink(uri, query, out protocolId, out values, out error);
                case "socks":
                case "socks4":
                case "socks5":
                case "socks5h": return ReadSocksLink(scheme, uri, query, out protocolId, out values, out error);
                case "http":
                case "https": return ReadHttpLink(scheme, uri, query, out protocolId, out values, out error);
                case "hysteria2":
                case "hy2": return ReadHysteria2Link(uri, query, out protocolId, out values, out error);
                case "tuic": return ReadTuicLink(uri, query, out protocolId, out values, out error);
                case "anytls": return ReadAnyTlsLink(uri, query, out protocolId, out values, out error);
                case "masque": return ReadMasqueLink(uri, query, out protocolId, out values, out error);
                default:
                    error = "this config cannot be opened in the editor";
                    return false;
            }
        }

        private static void ReadXrayCommon(Uri uri, Dictionary<string, string> query, Dictionary<string, string> values)
        {
            Set(values, "server", uri.IdnHost);
            if (uri.Port > 0) values["port"] = uri.Port.ToString(CultureInfo.InvariantCulture);
            Set(values, "security", Get(query, "security"));
            Set(values, "sni", Get(query, "sni"));
            Set(values, "fp", Get(query, "fp"));
            Set(values, "alpn", Get(query, "alpn"));
            if (Get(query, "allowInsecure") == "1" || Get(query, "insecure") == "1") values["insecure"] = "1";
            Set(values, "ech", Get(query, "ech"));
            Set(values, "vcn", Get(query, "vcn"));
            Set(values, "pcs", Get(query, "pcs"));
            Set(values, "pqv", Get(query, "pqv"));
            Set(values, "reality_pbk", Get(query, "pbk"));
            Set(values, "reality_sid", Get(query, "sid"));
            Set(values, "reality_spx", Get(query, "spx"));
            string type = Get(query, "type");
            if (type == "tcp") type = "raw";
            if (type is "raw" or "ws" or "grpc" or "httpupgrade" or "xhttp" or "kcp") Set(values, "transport", type);
            if (Get(query, "headerType") == "http") values["header_type"] = "http";
            Set(values, "path", Get(query, "path"));
            Set(values, "host", Get(query, "host"));
            Set(values, "service_name", Get(query, "serviceName"));
            Set(values, "authority", Get(query, "authority"));
            Set(values, "mode", Get(query, "mode"));
            Set(values, "extra", Get(query, "extra"));
            Set(values, "kcp_mtu", Get(query, "mtu"));
            Set(values, "kcp_tti", Get(query, "tti"));
            Set(values, "kcp_up", Get(query, "uplinkCapacity"));
            Set(values, "kcp_down", Get(query, "downlinkCapacity"));
            Set(values, "kcp_cwnd", Get(query, "cwndMultiplier"));
            Set(values, "kcp_window", Get(query, "maxSendingWindow"));
            Set(values, "fm", Get(query, "fm"));
            Set(values, "label", Uri.UnescapeDataString(uri.Fragment.TrimStart('#')));
        }

        private static bool ReadVlessLink(Uri uri, Dictionary<string, string> query, out string protocolId, out Dictionary<string, string> values, out string error)
        {
            protocolId = "vless";
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = "";
            ReadXrayCommon(uri, query, values);
            Set(values, "uuid", Uri.UnescapeDataString(uri.UserInfo));
            Set(values, "encryption", Get(query, "encryption"));
            Set(values, "flow", Get(query, "flow"));
            Set(values, "packet_encoding", Get(query, "packetEncoding"));
            return true;
        }

        private static bool ReadTrojanLink(Uri uri, Dictionary<string, string> query, out string protocolId, out Dictionary<string, string> values, out string error)
        {
            protocolId = "trojan";
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = "";
            ReadXrayCommon(uri, query, values);
            Set(values, "password", Uri.UnescapeDataString(uri.UserInfo));
            return true;
        }

        private static void ReadUserInfo(Uri uri, Dictionary<string, string> values)
        {
            string raw = Uri.UnescapeDataString(uri.UserInfo);
            if (raw.Length == 0) return;
            int colon = raw.IndexOf(':');
            if (colon > 0)
            {
                Set(values, "username", raw.Substring(0, colon));
                Set(values, "password", raw.Substring(colon + 1));
            }
            else
            {
                Set(values, "username", raw);
            }
        }

        private static bool ReadSocksLink(string scheme, Uri uri, Dictionary<string, string> query, out string protocolId, out Dictionary<string, string> values, out string error)
        {
            protocolId = "socks";
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = "";
            ReadXrayCommon(uri, query, values);
            values["version"] = scheme == "socks4" ? "4" : "5";
            ReadUserInfo(uri, values);
            return true;
        }

        private static bool ReadHttpLink(string scheme, Uri uri, Dictionary<string, string> query, out string protocolId, out Dictionary<string, string> values, out string error)
        {
            protocolId = "http";
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = "";
            values["tls"] = scheme == "https" || Get(query, "security") == "tls" ? "1" : "0";
            Set(values, "server", uri.IdnHost);
            if (uri.Port > 0) values["port"] = uri.Port.ToString(CultureInfo.InvariantCulture);
            ReadUserInfo(uri, values);
            Set(values, "sni", Get(query, "sni"));
            Set(values, "fp", Get(query, "fp"));
            Set(values, "alpn", Get(query, "alpn"));
            if (Get(query, "allowInsecure") == "1") values["insecure"] = "1";
            Set(values, "label", Uri.UnescapeDataString(uri.Fragment.TrimStart('#')));
            return true;
        }

        private static bool ReadHysteria2Link(Uri uri, Dictionary<string, string> query, out string protocolId, out Dictionary<string, string> values, out string error)
        {
            protocolId = "hysteria2";
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = "";
            Set(values, "server", uri.IdnHost);
            if (uri.Port > 0) values["port"] = uri.Port.ToString(CultureInfo.InvariantCulture);
            string raw = Uri.UnescapeDataString(uri.UserInfo);
            int colon = raw.IndexOf(':');
            Set(values, "password", colon > 0 ? raw.Substring(colon + 1) : raw);
            Set(values, "sni", Get(query, "sni"));
            if (Get(query, "insecure") == "1" || Get(query, "allowInsecure") == "1") values["insecure"] = "1";
            Set(values, "alpn", Get(query, "alpn"));
            Set(values, "obfs", Get(query, "obfs"));
            Set(values, "obfs_password", FirstNonEmpty(Get(query, "obfs-password"), Get(query, "obfs_password"), Get(query, "obfsPassword")));
            Set(values, "label", Uri.UnescapeDataString(uri.Fragment.TrimStart('#')));
            return true;
        }

        private static bool ReadTuicLink(Uri uri, Dictionary<string, string> query, out string protocolId, out Dictionary<string, string> values, out string error)
        {
            protocolId = "tuic";
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = "";
            Set(values, "server", uri.IdnHost);
            if (uri.Port > 0) values["port"] = uri.Port.ToString(CultureInfo.InvariantCulture);
            string raw = Uri.UnescapeDataString(uri.UserInfo);
            int colon = raw.IndexOf(':');
            if (colon > 0)
            {
                Set(values, "uuid", raw.Substring(0, colon));
                Set(values, "password", raw.Substring(colon + 1));
            }
            else
            {
                Set(values, "password", raw);
            }
            Set(values, "congestion", FirstNonEmpty(Get(query, "congestion_control"), Get(query, "congestionControl")));
            Set(values, "relay_mode", FirstNonEmpty(Get(query, "udp_relay_mode"), Get(query, "udpRelayMode")));
            Set(values, "sni", Get(query, "sni"));
            Set(values, "alpn", Get(query, "alpn"));
            if (Get(query, "insecure") == "1" || Get(query, "allowInsecure") == "1") values["insecure"] = "1";
            Set(values, "label", Uri.UnescapeDataString(uri.Fragment.TrimStart('#')));
            return true;
        }

        private static bool ReadAnyTlsLink(Uri uri, Dictionary<string, string> query, out string protocolId, out Dictionary<string, string> values, out string error)
        {
            protocolId = "anytls";
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = "";
            Set(values, "server", uri.IdnHost);
            if (uri.Port > 0) values["port"] = uri.Port.ToString(CultureInfo.InvariantCulture);
            string raw = Uri.UnescapeDataString(uri.UserInfo);
            int colon = raw.IndexOf(':');
            Set(values, "password", colon > 0 ? raw.Substring(colon + 1) : raw);
            Set(values, "sni", Get(query, "sni"));
            Set(values, "alpn", Get(query, "alpn"));
            if (Get(query, "insecure") == "1" || Get(query, "allowInsecure") == "1") values["insecure"] = "1";
            Set(values, "label", Uri.UnescapeDataString(uri.Fragment.TrimStart('#')));
            return true;
        }

        private static bool ReadMasqueLink(Uri uri, Dictionary<string, string> query, out string protocolId, out Dictionary<string, string> values, out string error)
        {
            protocolId = "masque";
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = "";
            Set(values, "server", uri.IdnHost);
            if (uri.Port > 0) values["port"] = uri.Port.ToString(CultureInfo.InvariantCulture);
            ReadUserInfo(uri, values);
            Set(values, "authority", Get(query, "host"));
            Set(values, "path", Get(query, "path"));
            Set(values, "sni", Get(query, "sni"));
            Set(values, "alpn", Get(query, "alpn"));
            Set(values, "fp", Get(query, "fp"));
            Set(values, "rdns", FirstNonEmpty(Get(query, "rdns"), Get(query, "remoteDNS")));
            Set(values, "label", Uri.UnescapeDataString(uri.Fragment.TrimStart('#')));
            return true;
        }

        private static bool SplitHostPort(string value, out string host, out int port)
        {
            host = "";
            port = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim();
            int slash = value.IndexOf('/');
            if (slash >= 0) value = value.Substring(0, slash);
            if (value.StartsWith("[", StringComparison.Ordinal))
            {
                int close = value.IndexOf(']');
                if (close < 0) return false;
                host = value.Substring(1, close - 1);
                string tail = value.Substring(close + 1);
                if (tail.StartsWith(":", StringComparison.Ordinal)) int.TryParse(tail.Substring(1), out port);
            }
            else
            {
                int colon = value.LastIndexOf(':');
                if (colon > 0)
                {
                    host = value.Substring(0, colon);
                    int.TryParse(value.Substring(colon + 1), out port);
                }
                else
                {
                    host = value;
                }
            }
            return host.Length > 0 && port > 0 && port <= 65535;
        }

        private static bool ReadVmessLink(string text, out string protocolId, out Dictionary<string, string> values, out string error)
        {
            protocolId = "";
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = "";
            string body = text.Substring("vmess://".Length);
            string label = "";
            int hash = body.IndexOf('#');
            if (hash >= 0)
            {
                label = Uri.UnescapeDataString(body.Substring(hash + 1));
                body = body.Substring(0, hash);
            }
            if (!TryBase64(body, out string json))
            {
                error = "this vmess link cannot be read";
                return false;
            }
            JObject root;
            try { root = JObject.Parse(json); }
            catch
            {
                error = "this vmess link cannot be read";
                return false;
            }
            protocolId = "vmess";
            Set(values, "server", root["add"]?.ToString() ?? "");
            Set(values, "port", root["port"]?.ToString() ?? "");
            Set(values, "uuid", root["id"]?.ToString() ?? "");
            Set(values, "alter_id", root["aid"]?.ToString() ?? "");
            Set(values, "vmess_security", root["scy"]?.ToString() ?? "");
            Set(values, "packet_encoding", FirstNonEmpty(root["packetEncoding"]?.ToString() ?? "", root["packet_encoding"]?.ToString() ?? ""));
            string net = root["net"]?.ToString() ?? "";
            if (net == "tcp") net = "raw";
            if (net is "raw" or "ws" or "grpc" or "httpupgrade") Set(values, "transport", net);
            if ((root["type"]?.ToString() ?? "") == "http") values["header_type"] = "http";
            Set(values, "host", root["host"]?.ToString() ?? "");
            Set(values, "path", root["path"]?.ToString() ?? "");
            values["security"] = (root["tls"]?.ToString() ?? "") == "tls" ? "tls" : "none";
            Set(values, "sni", root["sni"]?.ToString() ?? "");
            Set(values, "alpn", root["alpn"]?.ToString() ?? "");
            Set(values, "fp", root["fp"]?.ToString() ?? "");
            if ((root["allowInsecure"]?.ToString() ?? "") == "1") values["insecure"] = "1";
            Set(values, "label", FirstNonEmpty(root["ps"]?.ToString() ?? "", label));
            return true;
        }

        private static bool ReadShadowsocksLink(string text, out string protocolId, out Dictionary<string, string> values, out string error)
        {
            protocolId = "";
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = "";
            string body = text.Substring("ss://".Length);
            string label = "";
            int hash = body.IndexOf('#');
            if (hash >= 0)
            {
                label = Uri.UnescapeDataString(body.Substring(hash + 1));
                body = body.Substring(0, hash);
            }
            string query = "";
            int q = body.IndexOf('?');
            if (q >= 0)
            {
                query = body.Substring(q + 1);
                body = body.Substring(0, q);
            }
            int at = body.LastIndexOf('@');
            string userInfo, hostPort;
            if (at >= 0)
            {
                userInfo = body.Substring(0, at);
                hostPort = body.Substring(at + 1);
            }
            else
            {
                if (!TryBase64(body, out string decoded))
                {
                    error = "this shadowsocks link cannot be read";
                    return false;
                }
                at = decoded.LastIndexOf('@');
                if (at < 0)
                {
                    error = "this shadowsocks link cannot be read";
                    return false;
                }
                userInfo = decoded.Substring(0, at);
                hostPort = decoded.Substring(at + 1);
            }
            string creds = userInfo.Contains(':')
                ? Uri.UnescapeDataString(userInfo)
                : TryBase64(userInfo, out string decodedCreds) ? decodedCreds : Uri.UnescapeDataString(userInfo);
            int colon = creds.IndexOf(':');
            if (colon <= 0)
            {
                error = "this shadowsocks link cannot be read";
                return false;
            }
            if (!SplitHostPort(hostPort, out string host, out int port))
            {
                error = "this shadowsocks link has no usable server";
                return false;
            }
            protocolId = "shadowsocks";
            values["method"] = creds.Substring(0, colon);
            values["password"] = creds.Substring(colon + 1);
            values["server"] = host;
            values["port"] = port.ToString(CultureInfo.InvariantCulture);
            foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string key = eq < 0 ? pair : pair.Substring(0, eq);
                if (!key.Equals("plugin", StringComparison.OrdinalIgnoreCase)) continue;
                string plugin = Uri.UnescapeDataString(eq < 0 ? "" : pair.Substring(eq + 1));
                int semi = plugin.IndexOf(';');
                Set(values, "plugin", semi < 0 ? plugin : plugin.Substring(0, semi));
                Set(values, "plugin_opts", semi < 0 ? "" : plugin.Substring(semi + 1));
            }
            Set(values, "label", label);
            return true;
        }
    }
}

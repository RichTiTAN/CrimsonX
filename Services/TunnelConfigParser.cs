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

#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public enum TunnelKind
    {
        None,

        OpenVpn,

        WireGuard
    }

    public sealed class TunnelParseResult
    {
        public bool Success { get; set; }

        public TunnelKind Kind { get; set; } = TunnelKind.None;

        public string Label { get; set; } = "";

        public JObject Endpoint { get; set; }

        public string Raw { get; set; } = "";

        public string Error { get; set; } = "";

        public string User { get; set; } = "";

        public string Password { get; set; } = "";

        public bool NeedsCredentials { get; set; }

        public string KindText => Kind == TunnelKind.OpenVpn ? "openvpn" : Kind == TunnelKind.WireGuard ? "wireguard" : "";

        public string ServerText()
        {
            try
            {
                if (Kind == TunnelKind.WireGuard)
                {
                    var peer = (Endpoint?["peers"] as JArray)?.FirstOrDefault() as JObject;
                    if (peer == null) return "";
                    return $"{peer["address"]?.ToString()}:{peer["port"]?.ToString()}";
                }

                var server = (Endpoint?["servers"] as JArray)?.FirstOrDefault() as JObject;
                if (server != null) return $"{server["server"]?.ToString()}:{server["server_port"]?.ToString()}";

                string host = Endpoint?["server"]?.ToString() ?? "";
                return host.Length == 0 ? "" : $"{host}:{Endpoint?["server_port"]?.ToString()}";
            }
            catch { return ""; }
        }
    }

    public static class TunnelConfigParser
    {
        private static readonly string[] DefaultAllowedIps = { "0.0.0.0/0", "::/0" };

        private static readonly HashSet<string> BlockTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ca", "cert", "key", "tls-crypt", "tls-crypt-v2", "tls-auth", "auth-user-pass",
            "secret", "extra-certs", "connection", "pkcs12", "crl-verify", "dh"
        };

        private static readonly Regex InlineBlockRegex =
            new Regex(@"^<(?<tag>[A-Za-z0-9_\-]+)>(?<body>.*)</\k<tag>>\s*$", RegexOptions.Compiled);

        // ── Public API ──────────────────────────────────────────────────────────────────────

        public const int DefaultTunnelMtu = 1280;

        private static void ApplyDefaultMtu(JObject endpoint)
        {
            if (endpoint == null) return;
            if (ToInt(endpoint["mtu"]) > 0) return;

            endpoint["mtu"] = DefaultTunnelMtu;
        }

        public static bool TryParse(string raw, out TunnelParseResult result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string text = raw.Trim();
            TunnelParseResult parsed = null;

            try
            {
                if (text.StartsWith("{")) parsed = ParseJson(text);
                else if (text.StartsWith("wireguard://", StringComparison.OrdinalIgnoreCase)
                      || text.StartsWith("wg://", StringComparison.OrdinalIgnoreCase)) parsed = ParseWireGuardLink(text);
                else if (LooksLikeWireGuardIni(text)) parsed = ParseWireGuardIni(text);
                else if (LooksLikeOpenVpn(text)) parsed = ParseOpenVpn(text);
            }
            catch (Exception ex)
            {
                SimpleLogger.Log($"[Tunnel] Could not parse the custom config: {ex.Message}");
                parsed = null;
            }

            if (parsed == null) return false;

            if (parsed.Endpoint != null)
            {
                ApplyDefaultMtu(parsed.Endpoint);
                parsed.Success = true;
                if (string.IsNullOrWhiteSpace(parsed.Label)) parsed.Label = DescribeEndpoint(parsed.Endpoint);
                parsed.Label = CleanLabel(parsed.Label);
                if (parsed.Label.Length == 0) parsed.Label = parsed.Endpoint["type"]?.ToString() ?? "";
            }
            else if (parsed.Error.Length == 0)
            {
                parsed.Error = "Unsupported tunnel config.";
            }

            parsed.Raw = text;
            result = parsed;
            return true;
        }

        public static bool LooksLikeTunnel(string raw) => TryParse(raw, out var parsed) && parsed.Endpoint != null;

        public static string LabelOf(string raw) => TryParse(raw, out var parsed) ? parsed.Label : "";

        public static string KeyOf(string raw, string adapter)
            => Normalize(raw) + "|" + (string.IsNullOrWhiteSpace(adapter) ? "Default" : adapter.Trim());

        public static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            string text = raw.Trim();
            if (!TryParse(text, out var parsed) || parsed.Endpoint == null) return StripWhitespace(text);

            try { return SortKeys(parsed.Endpoint).ToString(Formatting.None); }
            catch { return StripWhitespace(text); }
        }

        public static JObject WithTag(JObject endpoint, string tag, string adapterName, string adapterIp)
        {
            endpoint["tag"] = tag;
            if (!string.IsNullOrWhiteSpace(adapterName) && !string.Equals(adapterName, "Default", StringComparison.OrdinalIgnoreCase))
            {
                endpoint["bind_interface"] = adapterName.Trim();
                if (!string.IsNullOrWhiteSpace(adapterIp)) endpoint["inet4_bind_address"] = adapterIp.Trim();
            }
            return endpoint;
        }

        public static JObject WithCredentials(JObject endpoint, string username, string password)
        {
            if (endpoint == null) return endpoint;
            if (!string.IsNullOrWhiteSpace(username)) endpoint["username"] = username.Trim();
            if (!string.IsNullOrWhiteSpace(password)) endpoint["password"] = password;
            return endpoint;
        }

        public static string DescribeEndpoint(JObject endpoint)
        {
            try
            {
                string type = endpoint?["type"]?.ToString() ?? "";
                if (type.Length == 0) return "";

                if (type == "wireguard")
                {
                    var peer = (endpoint["peers"] as JArray)?.FirstOrDefault() as JObject;
                    if (peer != null) return $"wireguard · {peer["address"]}:{peer["port"]}";
                    return "wireguard";
                }

                if (type == "openvpn-client")
                {
                    var server = (endpoint["servers"] as JArray)?.FirstOrDefault() as JObject;
                    if (server != null) return $"openvpn · {server["server"]}:{server["server_port"]}";
                    if (endpoint["server"] != null) return $"openvpn · {endpoint["server"]}:{endpoint["server_port"]}";
                    return "openvpn";
                }

                return type;
            }
            catch { return ""; }
        }

        // ── JSON inputs (sing-box endpoints, xray wireguard, sing-box outbounds) ────────────

        private static TunnelParseResult ParseJson(string text)
        {
            var root = JObject.Parse(text);

            if (root["endpoints"] is JArray endpoints)
            {
                TunnelParseResult first = null;
                int supported = 0;

                foreach (var item in endpoints.OfType<JObject>())
                {
                    var found = FromSingboxEndpoint(item);
                    if (found == null) continue;

                    supported++;
                    if (first == null) first = found;
                }

                if (first == null)
                    return new TunnelParseResult { Error = "No wireguard / openvpn-client endpoint found in this JSON." };

                if (supported > 1)
                    SimpleLogger.Log($"[Tunnel] This JSON holds {supported} endpoints; only the first one is used.");

                return first;
            }

            var single = FromSingboxEndpoint(root);
            if (single != null) return single;

            if (root["outbounds"] is JArray outbounds)
            {
                foreach (var item in outbounds.OfType<JObject>())
                {
                    var found = FromOutbound(item);
                    if (found != null) return found;
                }
                return null;
            }

            return FromOutbound(root);
        }

        private static TunnelParseResult FromOutbound(JObject outbound)
        {
            if (outbound == null) return null;

            string type = outbound["type"]?.ToString() ?? "";
            if (type.Equals("wireguard", StringComparison.OrdinalIgnoreCase)) return FromSingboxEndpoint(outbound);

            if ((outbound["protocol"]?.ToString() ?? "").Equals("wireguard", StringComparison.OrdinalIgnoreCase))
                return FromXrayWireGuard(outbound);

            return null;
        }

        private static TunnelParseResult FromSingboxEndpoint(JObject source)
        {
            if (source == null) return null;

            string type = source["type"]?.ToString() ?? "";
            bool isWireGuard = type.Equals("wireguard", StringComparison.OrdinalIgnoreCase);
            bool isOpenVpn = type.Equals("openvpn-client", StringComparison.OrdinalIgnoreCase)
                          || type.Equals("openvpn", StringComparison.OrdinalIgnoreCase);
            if (!isWireGuard && !isOpenVpn) return null;

            var endpoint = (JObject)source.DeepClone();
            endpoint.Remove("tag");
            endpoint.Remove("label");
            StripEmpty(endpoint);

            if (isWireGuard) return BuildFromWireGuardObject(endpoint);
            return BuildFromOpenVpnObject(endpoint);
        }

        private static void StripEmpty(JToken token)
        {
            if (token is JObject obj)
            {
                foreach (var property in obj.Properties().ToList())
                {
                    if (property.Value is JValue value && value.Type == JTokenType.String
                        && string.IsNullOrWhiteSpace(value.ToString()))
                    {
                        property.Remove();
                        continue;
                    }

                    StripEmpty(property.Value);
                }
                return;
            }

            if (token is JArray array)
            {
                foreach (var item in array.ToList())
                {
                    if (item is JValue itemValue && itemValue.Type == JTokenType.String
                        && string.IsNullOrWhiteSpace(itemValue.ToString()))
                    {
                        item.Remove();
                        continue;
                    }

                    StripEmpty(item);
                }
            }
        }

        private static TunnelParseResult BuildFromWireGuardObject(JObject endpoint)
        {
            string privateKey = FirstNonEmpty(endpoint["private_key"]?.ToString(), endpoint["privateKey"]?.ToString(),
                                              endpoint["secretKey"]?.ToString());

            bool legacyMarkers = endpoint["peers"] == null &&
                                 (endpoint["peer_public_key"] != null || endpoint["peerPublicKey"] != null || endpoint["server"] != null);

            if (legacyMarkers)
            {
                var legacy = FromLegacyWireGuardOutbound(endpoint);
                if (legacy != null) return legacy;
            }

            if (privateKey.Length == 0)
                return new TunnelParseResult { Error = "This WireGuard config has no private key." };

            var peers = new JArray();
            if (endpoint["peers"] is JArray source)
            {
                foreach (var item in source.OfType<JObject>())
                {
                    var peer = (JObject)item.DeepClone();
                    NormalizePeer(peer);
                    if ((peer["public_key"]?.ToString() ?? "").Length == 0) continue;
                    if ((peer["address"]?.ToString() ?? "").Length == 0) continue;
                    peers.Add(peer);
                }
            }

            if (peers.Count == 0)
                return new TunnelParseResult { Error = "This WireGuard config has no usable peer (server + public key)." };

            var addresses = ReadList(FirstToken(endpoint, "address", "local_address"));
            if (addresses.Length == 0)
                return new TunnelParseResult { Error = "This WireGuard config has no tunnel address." };

            endpoint["type"] = "wireguard";
            endpoint["private_key"] = privateKey;
            endpoint["address"] = new JArray(addresses);
            endpoint["peers"] = peers;
            endpoint.Remove("privateKey");
            endpoint.Remove("secretKey");
            endpoint.Remove("local_address");

            return new TunnelParseResult { Kind = TunnelKind.WireGuard, Endpoint = endpoint };
        }

        private static TunnelParseResult FromLegacyWireGuardOutbound(JObject source)
        {
            string privateKey = FirstNonEmpty(source["private_key"]?.ToString(), source["privateKey"]?.ToString());
            string peerKey = FirstNonEmpty(source["peer_public_key"]?.ToString(), source["peerPublicKey"]?.ToString());
            string server = FirstNonEmpty(source["server"]?.ToString(), source["address"]?.ToString());
            if (privateKey.Length == 0 || peerKey.Length == 0 || server.Length == 0) return null;

            int port = ToInt(FirstToken(source, "server_port", "port"));
            if (port <= 0) port = 51820;

            var peer = new JObject
            {
                ["address"] = server,
                ["port"] = port,
                ["public_key"] = peerKey,
                ["allowed_ips"] = new JArray(DefaultAllowedIps)
            };

            string preShared = FirstNonEmpty(source["pre_shared_key"]?.ToString(), source["preSharedKey"]?.ToString());
            if (preShared.Length > 0) peer["pre_shared_key"] = preShared;

            int keepAlive = ToInt(FirstToken(source, "persistent_keepalive_interval", "persistentKeepalive", "keepAlive"));
            if (keepAlive > 0) peer["persistent_keepalive_interval"] = keepAlive;

            var reserved = ReadList(FirstToken(source, "reserved"));
            if (reserved.Length > 0) peer["reserved"] = ToIntArray(reserved);

            var addresses = ReadList(FirstToken(source, "local_address", "address"));
            if (addresses.Length == 0)
                return new TunnelParseResult { Error = "This WireGuard config has no tunnel address." };

            var endpoint = new JObject
            {
                ["type"] = "wireguard",
                ["private_key"] = privateKey,
                ["address"] = new JArray(addresses),
                ["peers"] = new JArray { peer }
            };

            int mtu = ToInt(source["mtu"]);
            if (mtu > 0) endpoint["mtu"] = mtu;

            int listenPort = ToInt(FirstToken(source, "listen_port", "listenPort"));
            if (listenPort > 0) endpoint["listen_port"] = listenPort;

            int workers = ToInt(source["workers"]);
            if (workers > 0) endpoint["workers"] = workers;

            return new TunnelParseResult { Kind = TunnelKind.WireGuard, Endpoint = endpoint };
        }

        private static TunnelParseResult FromXrayWireGuard(JObject outbound)
        {
            var settings = outbound["settings"] as JObject;
            if (settings == null) return null;

            string privateKey = FirstNonEmpty(settings["secretKey"]?.ToString(), settings["privateKey"]?.ToString(),
                                              settings["private_key"]?.ToString());
            if (privateKey.Length == 0)
                return new TunnelParseResult { Error = "Xray wireguard outbound has no secretKey." };

            var addresses = ReadList(FirstToken(settings, "address", "local_address"));
            if (addresses.Length == 0)
                return new TunnelParseResult { Error = "Xray wireguard outbound has no address." };

            var endpoint = new JObject
            {
                ["type"] = "wireguard",
                ["private_key"] = privateKey,
                ["address"] = new JArray(addresses)
            };

            int mtu = ToInt(settings["mtu"]);
            if (mtu > 0) endpoint["mtu"] = mtu;

            int listenPort = ToInt(FirstToken(settings, "listenPort", "listen_port"));
            if (listenPort > 0) endpoint["listen_port"] = listenPort;

            int workers = ToInt(settings["workers"]);
            if (workers > 0) endpoint["workers"] = workers;

            var reserved = ReadList(FirstToken(settings, "reserved"));

            var peers = new JArray();
            if (settings["peers"] is JArray source)
            {
                foreach (var item in source.OfType<JObject>())
                {
                    var peer = (JObject)item.DeepClone();
                    NormalizePeer(peer);

                    if ((peer["public_key"]?.ToString() ?? "").Length == 0) continue;
                    if ((peer["address"]?.ToString() ?? "").Length == 0 || ToInt(peer["port"]) <= 0) continue;
                    if (reserved.Length > 0 && peer["reserved"] == null) peer["reserved"] = ToIntArray(reserved);

                    peers.Add(peer);
                }
            }

            if (peers.Count == 0)
                return new TunnelParseResult { Error = "Xray wireguard outbound has no usable peer." };

            endpoint["peers"] = peers;
            return new TunnelParseResult { Kind = TunnelKind.WireGuard, Endpoint = endpoint };
        }

        private static void NormalizePeer(JObject peer)
        {
            string combined = peer["endpoint"]?.ToString() ?? "";
            if (combined.Length > 0)
            {
                if (SplitHostPort(combined, out string host, out int splitPort, 0))
                {
                    peer["address"] = host;
                    if (splitPort > 0) peer["port"] = splitPort;
                }
                peer.Remove("endpoint");
            }

            string address = FirstNonEmpty(peer["address"]?.ToString(), peer["server"]?.ToString());
            if (address.Length > 0) peer["address"] = address;
            peer.Remove("server");

            int port = ToInt(FirstToken(peer, "port", "server_port"));
            if (port > 0) peer["port"] = port;
            peer.Remove("server_port");

            string publicKey = FirstNonEmpty(peer["public_key"]?.ToString(), peer["publicKey"]?.ToString());
            if (publicKey.Length > 0) peer["public_key"] = publicKey;
            peer.Remove("publicKey");

            string preShared = FirstNonEmpty(peer["pre_shared_key"]?.ToString(), peer["preSharedKey"]?.ToString(),
                                             peer["preshared_key"]?.ToString());
            if (preShared.Length > 0) peer["pre_shared_key"] = preShared;
            peer.Remove("preSharedKey");
            peer.Remove("preshared_key");

            int keepAlive = ToInt(FirstToken(peer, "persistent_keepalive_interval", "persistentKeepalive", "keepAlive", "keep_alive"));
            if (keepAlive > 0) peer["persistent_keepalive_interval"] = keepAlive;
            peer.Remove("persistentKeepalive");
            peer.Remove("keepAlive");
            peer.Remove("keep_alive");

            var allowed = ReadList(FirstToken(peer, "allowed_ips", "allowedIPs"));
            peer["allowed_ips"] = new JArray(allowed.Length > 0 ? allowed : DefaultAllowedIps);
            peer.Remove("allowedIPs");

            var reserved = ReadList(FirstToken(peer, "reserved"));
            if (reserved.Length > 0) peer["reserved"] = ToIntArray(reserved);
        }

        private static string[] ReadList(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return new string[0];

            if (token is JArray array)
            {
                return array.Select(v => v?.ToString()?.Trim())
                            .Where(v => !string.IsNullOrWhiteSpace(v))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();
            }

            return SplitList(token.ToString());
        }

        private static JArray ToIntArray(string[] values)
        {
            var array = new JArray();
            foreach (var value in values)
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    array.Add(parsed);
            }
            return array;
        }

        private static TunnelParseResult BuildFromOpenVpnObject(JObject endpoint)
        {
            endpoint["type"] = "openvpn-client";

            var servers = new JArray();
            if (endpoint["servers"] is JArray source)
            {
                foreach (var item in source.OfType<JObject>())
                {
                    string host = item["server"]?.ToString() ?? "";
                    int port = ToInt(item["server_port"]);
                    if (host.Length == 0 || port <= 0) continue;

                    var server = new JObject { ["server"] = host, ["server_port"] = port };
                    string network = item["network"]?.ToString() ?? "";
                    if (network.Length > 0) server["network"] = network;
                    servers.Add(server);
                }
                endpoint.Remove("servers");
            }

            if (servers.Count == 0)
            {
                string host = endpoint["server"]?.ToString() ?? "";
                int port = ToInt(endpoint["server_port"]);
                if (host.Length == 0 || port <= 0)
                    return new TunnelParseResult { Error = "This OpenVPN config has no remote server." };

                servers.Add(new JObject { ["server"] = host, ["server_port"] = port });
                endpoint.Remove("server");
                endpoint.Remove("server_port");
            }

            endpoint["servers"] = servers;

            string mode = endpoint["mode"]?.ToString() ?? "";
            if (mode.Length == 0)
                endpoint["mode"] = endpoint["tls"] != null || endpoint["static_key"] == null ? "tls" : "static_key";

            if (endpoint["tls"] is JObject tls)
            {
                NormalizePem(tls, "certificate");
                NormalizePem(tls, "client_certificate");
                NormalizePem(tls, "client_key");
                if (tls["control_wrap"] is JObject wrap) NormalizePem(wrap, "key");
            }

            var result = new TunnelParseResult { Kind = TunnelKind.OpenVpn, Endpoint = endpoint };
            result.User = endpoint["username"]?.ToString() ?? "";
            result.Password = endpoint["password"]?.ToString() ?? "";
            return result;
        }

        private static void NormalizePem(JObject owner, string key)
        {
            var token = owner[key];
            if (token == null) return;

            string value = token.ToString();
            if (value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0) return;

            value = value.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = value.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);
            owner[key] = string.Join("\n", lines);
        }

        private static string CleanLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "";
            label = label.Replace("\r", " ").Replace("\n", " ").Trim();
            while (label.Contains("  ")) label = label.Replace("  ", " ");
            if (label.Length > 40) label = label.Substring(0, 40).Trim();
            return label;
        }

        private static string StripWhitespace(string text)
            => new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

        private static JToken SortKeys(JToken token)
        {
            if (token is JObject obj)
            {
                var sorted = new JObject();
                foreach (var property in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                    sorted[property.Name] = SortKeys(property.Value);
                return sorted;
            }

            if (token is JArray array)
            {
                var sorted = new JArray();
                foreach (var item in array) sorted.Add(SortKeys(item));
                return sorted;
            }

            return token;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return "";
        }

        private static JToken FirstToken(JObject obj, params string[] keys)
        {
            if (obj == null) return null;
            foreach (var key in keys)
            {
                var token = obj[key];
                if (token != null && token.Type != JTokenType.Null) return token;
            }
            return null;
        }

        private static int ToInt(JToken token)
        {
            if (token == null) return 0;
            if (token.Type == JTokenType.Integer) return token.Value<int>();

            int.TryParse(token.ToString().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed);
            return parsed;
        }

        private static string[] SplitList(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new string[0];
            return value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => v.Trim())
                        .Where(v => v.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
        }

        private static string[] SplitArgs(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new string[0];
            return value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string PercentDecode(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            try { return Uri.UnescapeDataString(value).Trim(); }
            catch { return value.Trim(); }
        }

        private static bool SplitHostPort(string text, out string host, out int port, int defaultPort)
        {
            host = "";
            port = defaultPort;
            text = (text ?? "").Trim();
            if (text.Length == 0) return false;

            if (text.StartsWith("["))
            {
                int close = text.IndexOf(']');
                if (close < 0) return false;

                host = text.Substring(1, close - 1).Trim();
                string tail = text.Substring(close + 1);
                if (tail.StartsWith(":")) int.TryParse(tail.Substring(1).Trim(), out port);
                if (port <= 0 || port > 65535) port = defaultPort;
                return host.Length > 0;
            }

            int colon = text.LastIndexOf(':');
            if (colon > 0)
            {
                host = text.Substring(0, colon).Trim();
                if (!int.TryParse(text.Substring(colon + 1).Trim(), out port)) port = defaultPort;
            }
            else
            {
                host = text.Trim();
            }

            if (port <= 0 || port > 65535) port = defaultPort;
            return host.Length > 0;
        }

        private static string FirstArg(string value)
        {
            var args = SplitArgs(value);
            return args.Length > 0 ? args[0] : "";
        }

        private static string SecondArg(string value)
        {
            var args = SplitArgs(value);
            return args.Length > 1 ? args[1] : "";
        }

        // ── WireGuard inputs ────────────────────────────────────────────────────────────────

        private static bool LooksLikeWireGuardIni(string text)
        {
            string lower = text.ToLowerInvariant();
            return lower.Contains("[interface]") && lower.Contains("[peer]");
        }

        private static TunnelParseResult ParseWireGuardLink(string link)
        {
            string body = link.StartsWith("wireguard://", StringComparison.OrdinalIgnoreCase)
                ? link.Substring("wireguard://".Length)
                : link.Substring("wg://".Length);

            string label = "";
            int hash = body.IndexOf('#');
            if (hash >= 0)
            {
                label = PercentDecode(body.Substring(hash + 1));
                body = body.Substring(0, hash);
            }

            string query = "";
            int q = body.IndexOf('?');
            if (q >= 0)
            {
                query = body.Substring(q + 1);
                body = body.Substring(0, q);
            }

            var qs = ParseQueryPairs(query);

            string privateKey = "";
            string host = "";
            int port = 51820;

            int at = body.LastIndexOf('@');
            if (at >= 0)
            {
                privateKey = PercentDecode(body.Substring(0, at));
                SplitHostPort(body.Substring(at + 1), out host, out port, 51820);
            }
            else
            {
                SplitHostPort(body, out host, out port, 51820);
            }

            if (privateKey.Length == 0) privateKey = QueryValue(qs, "privatekey", "private_key", "secretkey", "secret_key");
            if (host.Length == 0) host = QueryValue(qs, "host", "server", "remote");
            if (port <= 0) port = ParseInt(QueryValue(qs, "port", "server_port"), 51820);

            string publicKey = QueryValue(qs, "publickey", "public_key", "pk", "peerpublickey");
            string addressValue = QueryValue(qs, "address", "ip", "local_address", "localaddress");

            if (privateKey.Length == 0) return new TunnelParseResult { Error = "This WireGuard link has no private key." };
            if (publicKey.Length == 0) return new TunnelParseResult { Error = "This WireGuard link has no public key." };
            if (host.Length == 0) return new TunnelParseResult { Error = "This WireGuard link has no server address." };

            var addresses = SplitList(addressValue);
            if (addresses.Length == 0) return new TunnelParseResult { Error = "This WireGuard link has no tunnel address." };

            var peer = new JObject
            {
                ["address"] = host,
                ["port"] = port,
                ["public_key"] = publicKey
            };

            var allowed = SplitList(QueryValue(qs, "allowedips", "allowed_ips", "allowedip"));
            peer["allowed_ips"] = new JArray(allowed.Length > 0 ? allowed : DefaultAllowedIps);

            string preShared = QueryValue(qs, "presharedkey", "pre_shared_key", "psk");
            if (preShared.Length > 0) peer["pre_shared_key"] = preShared;

            int keepAlive = ParseInt(QueryValue(qs, "keepalive", "persistentkeepalive", "persistent_keepalive"), 0);
            if (keepAlive > 0) peer["persistent_keepalive_interval"] = keepAlive;

            var reserved = SplitList(QueryValue(qs, "reserved").Replace('+', ','));
            if (reserved.Length > 0) peer["reserved"] = ToIntArray(reserved);

            var endpoint = new JObject
            {
                ["type"] = "wireguard",
                ["private_key"] = privateKey,
                ["address"] = new JArray(addresses),
                ["peers"] = new JArray { peer }
            };

            int mtu = ParseInt(QueryValue(qs, "mtu"), 0);
            if (mtu > 0) endpoint["mtu"] = mtu;

            int listenPort = ParseInt(QueryValue(qs, "listenport", "listen_port"), 0);
            if (listenPort > 0) endpoint["listen_port"] = listenPort;

            int workers = ParseInt(QueryValue(qs, "workers"), 0);
            if (workers > 0) endpoint["workers"] = workers;

            return new TunnelParseResult { Kind = TunnelKind.WireGuard, Endpoint = endpoint, Label = label };
        }

        private static Dictionary<string, string> ParseQueryPairs(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(query)) return result;

            foreach (var pair in query.Split('&'))
            {
                if (pair.Length == 0) continue;

                int eq = pair.IndexOf('=');
                string key = PercentDecode(eq < 0 ? pair : pair.Substring(0, eq));
                string value = eq < 0 ? "" : PercentDecode(pair.Substring(eq + 1));
                if (key.Length > 0 && !result.ContainsKey(key)) result[key] = value;
            }

            return result;
        }

        private static string QueryValue(Dictionary<string, string> query, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (query.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return "";
        }

        private static TunnelParseResult ParseWireGuardIni(string text)
        {
            var interfaceValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var peers = new List<Dictionary<string, string>>();
            Dictionary<string, string> current = null;

            foreach (var raw in SplitLines(text))
            {
                string line = StripComment(raw).Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    string section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                    if (section == "interface") current = interfaceValues;
                    else if (section == "peer")
                    {
                        current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        peers.Add(current);
                    }
                    else current = null;
                    continue;
                }

                if (current == null) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (key.Length > 0) current[key] = value;
            }

            string privateKey = Value(interfaceValues, "privatekey", "private_key");
            var addresses = SplitList(Value(interfaceValues, "address"));

            if (privateKey.Length == 0)
                return new TunnelParseResult { Error = "This WireGuard config has no PrivateKey." };
            if (addresses.Length == 0)
                return new TunnelParseResult { Error = "This WireGuard config has no Address." };

            var endpoint = new JObject
            {
                ["type"] = "wireguard",
                ["private_key"] = privateKey,
                ["address"] = new JArray(addresses)
            };

            int mtu = ParseInt(Value(interfaceValues, "mtu"), 0);
            if (mtu > 0) endpoint["mtu"] = mtu;

            int listenPort = ParseInt(Value(interfaceValues, "listenport", "listen_port"), 0);
            if (listenPort > 0) endpoint["listen_port"] = listenPort;

            var peerArray = new JArray();
            foreach (var values in peers)
            {
                if (!SplitHostPort(Value(values, "endpoint"), out string host, out int port, 51820)) continue;

                string publicKey = Value(values, "publickey", "public_key");
                if (publicKey.Length == 0) continue;

                var peer = new JObject
                {
                    ["address"] = host,
                    ["port"] = port,
                    ["public_key"] = publicKey
                };

                var allowed = SplitList(Value(values, "allowedips", "allowed_ips"));
                peer["allowed_ips"] = new JArray(allowed.Length > 0 ? allowed : DefaultAllowedIps);

                string preShared = Value(values, "presharedkey", "pre_shared_key");
                if (preShared.Length > 0) peer["pre_shared_key"] = preShared;

                int keepAlive = ParseInt(Value(values, "persistentkeepalive", "keepalive"), 0);
                if (keepAlive > 0) peer["persistent_keepalive_interval"] = keepAlive;

                peerArray.Add(peer);
            }

            if (peerArray.Count == 0)
                return new TunnelParseResult { Error = "This WireGuard config has no usable [Peer]." };

            endpoint["peers"] = peerArray;
            return new TunnelParseResult { Kind = TunnelKind.WireGuard, Endpoint = endpoint };
        }

        private static string Value(Dictionary<string, string> values, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return "";
        }

        private static string[] SplitLines(string text)
            => (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        private static string StripComment(string line)
        {
            if (line == null) return "";

            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("#") || trimmed.StartsWith(";")) return "";

            for (int i = 1; i < line.Length; i++)
            {
                if (line[i] != '#' && line[i] != ';') continue;
                if (!char.IsWhiteSpace(line[i - 1])) continue;
                return line.Substring(0, i);
            }

            return line;
        }

        // ── OpenVPN inputs ──────────────────────────────────────────────────────────────────

        private static bool LooksLikeOpenVpn(string text)
        {
            string lower = text.ToLowerInvariant();
            if (lower.Contains("<ca>") || lower.Contains("<tls-crypt>") || lower.Contains("<tls-auth>")
             || lower.Contains("<cert>") || lower.Contains("<key>") || lower.Contains("<secret>")) return true;

            bool hasRemote = false;
            bool hasSignal = false;

            foreach (var raw in SplitLines(text))
            {
                string line = StripComment(raw).Trim();
                if (line.Length == 0) continue;

                string key = DirectiveKey(line);
                if (key == "remote") hasRemote = true;
                else if (key == "dev" || key == "proto" || key == "client" || key == "tls-client"
                      || key == "auth-user-pass" || key == "remote-cert-tls") hasSignal = true;
            }

            return hasRemote && hasSignal;
        }

        private static string DirectiveKey(string line)
        {
            int space = line.IndexOfAny(new[] { ' ', '\t' });
            return (space < 0 ? line : line.Substring(0, space)).Trim().ToLowerInvariant();
        }

        private sealed class OpenVpnOptions
        {
            public string Network = "udp";
            public readonly List<Tuple<string, int, string>> Remotes = new List<Tuple<string, int, string>>();
            public bool RemoteRandom;
            public bool Client;
            public string Username = "";
            public string Password = "";
            public bool NeedsCredentials;
            public string AuthRetry = "";
            public string KeyDirection = "";
            public string TlsAuthDirection = "";
            public bool HasTlsAuth;
            public bool HasStaticKey;
            public string Cipher = "";
            public string[] DataCiphers = new string[0];
            public string DataCiphersFallback = "";
            public string Auth = "";
            public int Mtu;
            public int MssFix = -1;
            public int RenegSec = -1;
            public int RenegBytes;
            public int RenegPackets;
            public int PingInterval;
            public int PingRestart;
            public int TlsTimeout;
            public int HandshakeWindow;
            public int ExplicitExitNotify;
            public int Fragment;
            public string CompLzo = "";
            public string Compress = "";
            public string AllowCompression = "";
            public string RemoteCertTls = "";
            public string NsCertType = "";
            public string[] RemoteCertKu = new string[0];
            public string RemoteCertEku = "";
            public string ServerName = "";
            public string ServerNameType = "";
            public string[] PeerFingerprint = new string[0];
            public string TlsVersionMin = "";
            public string TlsVersionMax = "";
            public string TlsCipher = "";
            public string TlsGroups = "";
            public string CrlPath = "";
            public bool RedirectGateway;
            public string RedirectGatewayFlags = "";
            public bool RedirectPrivate;
            public bool BlockIpv6;
            public bool RouteNoPull;
            public bool Tap;
            public string LocalAddress = "";
            public string PeerAddress = "";
            public readonly List<string> PullFilters = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public readonly Dictionary<string, string> Blocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static List<string> ExtractInlineBlocks(string text, OpenVpnOptions options)
        {
            var directives = new List<string>();
            string currentBlock = null;
            var blockLines = new List<string>();

            foreach (var raw in SplitLines(text))
            {
                string line = raw ?? "";

                if (currentBlock != null)
                {
                    string trimmed = line.Trim();
                    int close = trimmed.IndexOf("</" + currentBlock + ">", StringComparison.OrdinalIgnoreCase);
                    if (close >= 0)
                    {
                        string head = trimmed.Substring(0, close).Trim();
                        if (head.Length > 0) blockLines.Add(head);

                        options.Blocks[currentBlock] = string.Join("\n", blockLines);
                        currentBlock = null;
                        blockLines.Clear();
                        continue;
                    }

                    if (trimmed.Length > 0) blockLines.Add(trimmed);
                    continue;
                }

                string clean = StripComment(line).Trim();
                if (clean.Length == 0) continue;

                var inline = InlineBlockRegex.Match(clean);
                if (inline.Success && BlockTags.Contains(inline.Groups["tag"].Value))
                {
                    options.Blocks[inline.Groups["tag"].Value] = inline.Groups["body"].Value.Trim();
                    continue;
                }

                if (clean.StartsWith("<") && clean.EndsWith(">") && !clean.StartsWith("</"))
                {
                    string tag = clean.Substring(1, clean.Length - 2).Trim().ToLowerInvariant();
                    if (BlockTags.Contains(tag))
                    {
                        currentBlock = tag;
                        blockLines.Clear();
                        continue;
                    }
                }

                directives.Add(clean);
            }

            return directives;
        }

        // ── OpenVPN parse ───────────────────────────────────────────────────────────────────

        private static TunnelParseResult ParseOpenVpn(string text)
        {
            var options = new OpenVpnOptions();
            var directives = ExtractInlineBlocks(text, options);

            foreach (var directive in directives)
            {
                string key = DirectiveKey(directive);
                string value = directive.Substring(key.Length).Trim();
                ApplyOpenVpnDirective(options, key, value);
            }

            return BuildOpenVpnEndpoint(options);
        }

        private static void ApplyOpenVpnDirective(OpenVpnOptions options, string key, string value)
        {
            switch (key)
            {
                case "client":
                case "tls-client":
                    options.Client = true;
                    break;

                case "dev":
                {
                    string dev = FirstArg(value).ToLowerInvariant();
                    if (dev.StartsWith("tap")) options.Tap = true;
                    break;
                }

                case "dev-type":
                    if (FirstArg(value).ToLowerInvariant().StartsWith("tap")) options.Tap = true;
                    break;

                case "proto":
                {
                    string net = NormalizeNetwork(value);
                    if (net.Length > 0) options.Network = net;
                    break;
                }

                case "remote":
                {
                    var args = SplitArgs(value);
                    if (args.Length == 0) break;

                    string host = args[0];
                    int port = 1194;
                    string net = "";

                    if (args.Length > 1)
                    {
                        if (int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPort)) port = parsedPort;
                        else net = NormalizeNetwork(args[1]);
                    }
                    if (args.Length > 2) net = NormalizeNetwork(args[2]);
                    if (port <= 0 || port > 65535) port = 1194;

                    options.Remotes.Add(Tuple.Create(host, port, net));
                    break;
                }

                case "ifconfig":
                {
                    options.LocalAddress = FirstArg(value);
                    options.PeerAddress = SecondArg(value);
                    break;
                }

                case "remote-random": options.RemoteRandom = true; break;
                case "remote-cert-tls": options.RemoteCertTls = NormalizeCertificateSide(FirstArg(value)); break;
                case "remote-cert-ku": options.RemoteCertKu = SplitArgs(value); break;
                case "remote-cert-eku": options.RemoteCertEku = value; break;
                case "ns-cert-type": options.NsCertType = NormalizeCertificateSide(FirstArg(value)); break;

                case "verify-x509-name":
                {
                    options.ServerName = FirstArg(value);
                    string type = SecondArg(value).ToLowerInvariant();
                    options.ServerNameType = type == "subject" || type == "name-prefix" ? type : "name";
                    break;
                }

                case "peer-fingerprint": options.PeerFingerprint = SplitArgs(value); break;
                case "tls-version-min": options.TlsVersionMin = NormalizeTlsVersion(FirstArg(value)); break;
                case "tls-version-max": options.TlsVersionMax = NormalizeTlsVersion(FirstArg(value)); break;
                case "tls-cipher": options.TlsCipher = value; break;
                case "tls-groups": options.TlsGroups = value; break;
                case "crl-verify": options.CrlPath = FirstArg(value); break;
                case "tls-timeout": options.TlsTimeout = ParseInt(FirstArg(value), 0); break;
                case "handshake-window": options.HandshakeWindow = ParseInt(FirstArg(value), 0); break;
                case "explicit-exit-notify": options.ExplicitExitNotify = ParseInt(FirstArg(value), 1); break;

                case "auth-user-pass":
                case "askpass":
                    options.NeedsCredentials = true;
                    break;

                case "username": options.Username = value; break;
                case "password": options.Password = value; break;
                case "auth-retry": options.AuthRetry = NormalizeAuthRetry(FirstArg(value)); break;
                case "key-direction": options.KeyDirection = FirstArg(value); break;

                case "tls-auth":
                    options.HasTlsAuth = true;
                    options.TlsAuthDirection = SecondArg(value);
                    break;

                case "secret":
                case "static-key":
                    options.HasStaticKey = true;
                    break;

                case "cipher": options.Cipher = FirstArg(value); break;
                case "data-ciphers": options.DataCiphers = SplitCiphers(value); break;
                case "data-ciphers-fallback": options.DataCiphersFallback = FirstArg(value); break;
                case "auth": options.Auth = FirstArg(value).ToUpperInvariant(); break;

                case "tun-mtu":
                case "mtu": options.Mtu = ParseInt(FirstArg(value), 0); break;

                case "mssfix": options.MssFix = ParseInt(FirstArg(value), 1450); break;
                case "reneg-sec": options.RenegSec = ParseInt(FirstArg(value), 0); break;
                case "reneg-bytes": options.RenegBytes = ParseInt(FirstArg(value), 0); break;
                case "reneg-pkts": options.RenegPackets = ParseInt(FirstArg(value), 0); break;
                case "ping": options.PingInterval = ParseInt(FirstArg(value), 0); break;
                case "ping-restart": options.PingRestart = ParseInt(FirstArg(value), 0); break;

                case "comp-lzo": options.CompLzo = value.Length == 0 ? "yes" : FirstArg(value); break;
                case "compress": options.Compress = value.Length == 0 ? "stub" : FirstArg(value); break;
                case "allow-compression": options.AllowCompression = FirstArg(value).ToLowerInvariant(); break;

                case "redirect-gateway": options.RedirectGateway = true; options.RedirectGatewayFlags = value; break;
                case "redirect-private": options.RedirectPrivate = true; break;
                case "block-ipv6": options.BlockIpv6 = true; break;
                case "route-nopull": options.RouteNoPull = true; break;
                case "pull-filter": options.PullFilters.Add(value); break;
                case "fragment": options.Fragment = ParseInt(FirstArg(value), 0); break;

                case "ca":
                case "cert":
                case "key":
                case "tls-crypt":
                case "tls-crypt-v2":
                case "pkcs12":
                    if (value.Length > 0) options.Warnings.Add($"{key} file references are ignored; use inline blocks instead.");
                    break;

                case "http-proxy":
                case "socks-proxy":
                    options.Warnings.Add($"{key} is not supported by sing-box.");
                    break;

                case "server":
                case "push":
                case "tls-server":
                    options.Warnings.Add($"{key} is a server side directive and was ignored.");
                    break;
            }
        }

        private static TunnelParseResult BuildOpenVpnEndpoint(OpenVpnOptions options)
        {
            if (options.Tap)
                return new TunnelParseResult { Kind = TunnelKind.OpenVpn, Error = "TAP (layer 2) OpenVPN configs are not supported; use a TUN config." };

            if (options.Remotes.Count == 0)
                return new TunnelParseResult { Kind = TunnelKind.OpenVpn, Error = "This OpenVPN config has no remote server." };

            bool hasTlsMaterial = options.Client
                               || options.HasTlsAuth
                               || options.RemoteCertTls.Length > 0
                               || options.Blocks.ContainsKey("ca")
                               || options.Blocks.ContainsKey("cert")
                               || options.Blocks.ContainsKey("key")
                               || options.Blocks.ContainsKey("tls-auth")
                               || options.Blocks.ContainsKey("tls-crypt")
                               || options.Blocks.ContainsKey("tls-crypt-v2");

            bool staticKey = options.HasStaticKey && !hasTlsMaterial;

            var endpoint = new JObject
            {
                ["type"] = "openvpn-client",
                ["mode"] = staticKey ? "static_key" : "tls"
            };

            if (options.Network.Length > 0) endpoint["network"] = options.Network;

            var servers = new JArray();
            foreach (var remote in options.Remotes)
            {
                if (staticKey && servers.Count == 1) break;

                var server = new JObject { ["server"] = remote.Item1, ["server_port"] = remote.Item2 };
                if (remote.Item3.Length > 0 && remote.Item3 != options.Network) server["network"] = remote.Item3;
                servers.Add(server);
            }

            if (staticKey && options.Remotes.Count > 1)
                options.Warnings.Add("Static key mode supports a single remote; extra remotes were dropped.");

            endpoint["servers"] = servers;

            if (options.RemoteRandom) endpoint["remote_random"] = true;

            var tls = BuildOpenVpnTls(options);
            if (tls.Count > 0) endpoint["tls"] = tls;

            if (options.Blocks.TryGetValue("auth-user-pass", out string credentials) && credentials.Length > 0)
            {
                var lines = credentials.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
                if (lines.Length > 0 && options.Username.Length == 0) options.Username = lines[0];
                if (lines.Length > 1 && options.Password.Length == 0) options.Password = string.Join(" ", lines.Skip(1));
            }

            if (options.Username.Length > 0) endpoint["username"] = options.Username;
            if (options.Password.Length > 0) endpoint["password"] = options.Password;
            if (options.AuthRetry.Length > 0) endpoint["auth_retry"] = options.AuthRetry;

            if (staticKey)
            {
                if (!options.Blocks.TryGetValue("secret", out string secret) || secret.Length == 0)
                    return new TunnelParseResult { Kind = TunnelKind.OpenVpn, Error = "This static key OpenVPN config has no inline <secret> block." };

                if (options.LocalAddress.Length == 0)
                    return new TunnelParseResult { Kind = TunnelKind.OpenVpn, Error = "This static key OpenVPN config has no ifconfig address." };

                endpoint["static_key"] = secret;
                endpoint["address"] = options.LocalAddress;
                if (options.PeerAddress.Length > 0) endpoint["peer_address"] = options.PeerAddress;
            }

            ApplyOpenVpnExtras(endpoint, options);

            foreach (var warning in options.Warnings)
                SimpleLogger.Log($"[Tunnel] OpenVPN: {warning}");

            var result = new TunnelParseResult { Kind = TunnelKind.OpenVpn, Endpoint = endpoint };
            result.NeedsCredentials = options.NeedsCredentials && options.Username.Length == 0;
            result.User = options.Username;
            result.Password = options.Password;
            return result;
        }

        private static JObject BuildOpenVpnTls(OpenVpnOptions options)
        {
            var tls = new JObject();

            if (options.Blocks.TryGetValue("ca", out string ca) && ca.Length > 0) tls["certificate"] = ca;
            if (options.Blocks.TryGetValue("cert", out string cert) && cert.Length > 0) tls["client_certificate"] = cert;
            if (options.Blocks.TryGetValue("key", out string key) && key.Length > 0) tls["client_key"] = key;

            if (options.ServerName.Length > 0)
            {
                tls["server_name"] = options.ServerName;
                if (options.ServerNameType.Length > 0) tls["server_name_type"] = options.ServerNameType;
            }

            if (options.RemoteCertTls.Length > 0) tls["remote_certificate_tls"] = options.RemoteCertTls;
            if (options.NsCertType.Length > 0) tls["ns_certificate_type"] = options.NsCertType;
            if (options.RemoteCertKu.Length > 0) tls["remote_certificate_ku"] = new JArray(options.RemoteCertKu);
            if (options.RemoteCertEku.Length > 0) tls["remote_certificate_eku"] = options.RemoteCertEku;
            if (options.PeerFingerprint.Length > 0) tls["peer_fingerprint"] = new JArray(options.PeerFingerprint);
            if (options.TlsVersionMin.Length > 0) tls["version_min"] = options.TlsVersionMin;
            if (options.TlsVersionMax.Length > 0) tls["version_max"] = options.TlsVersionMax;
            if (options.TlsCipher.Length > 0) tls["cipher"] = options.TlsCipher;
            if (options.TlsGroups.Length > 0) tls["groups"] = options.TlsGroups;

            var wrap = BuildControlWrap(options);
            if (wrap != null) tls["control_wrap"] = wrap;

            return tls;
        }

        private static JObject BuildControlWrap(OpenVpnOptions options)
        {
            string type = "";
            string key = "";

            if (options.Blocks.TryGetValue("tls-crypt", out string crypt) && crypt.Length > 0) { type = "tls_crypt"; key = crypt; }
            else if (options.Blocks.TryGetValue("tls-crypt-v2", out string crypt2) && crypt2.Length > 0) { type = "tls_crypt_v2"; key = crypt2; }
            else if (options.Blocks.TryGetValue("tls-auth", out string auth) && auth.Length > 0) { type = "tls_auth"; key = auth; }
            else if (options.HasTlsAuth) type = "tls_auth";

            if (type.Length == 0) return null;

            var wrap = new JObject { ["type"] = type };
            if (key.Length > 0) wrap["key"] = key;

            if (type == "tls_auth")
            {
                string direction = DirectionName(FirstNonEmpty(options.TlsAuthDirection, options.KeyDirection));
                if (direction.Length > 0) wrap["direction"] = direction;
            }

            return wrap;
        }

        private static string DirectionName(string direction)
        {
            string value = (direction ?? "").Trim().ToLowerInvariant();
            if (value == "1" || value == "client" || value == "client-side") return "client";
            if (value == "0" || value == "server" || value == "server-side") return "server";
            return "";
        }

        private static void ApplyOpenVpnExtras(JObject endpoint, OpenVpnOptions options)
        {
            var ciphers = options.DataCiphers.Length > 0
                ? options.DataCiphers
                : (options.Cipher.Length > 0 ? new[] { options.Cipher } : new string[0]);

            if (ciphers.Length > 0) endpoint["data_ciphers"] = new JArray(ciphers);
            if (options.DataCiphersFallback.Length > 0) endpoint["data_ciphers_fallback"] = options.DataCiphersFallback;
            if (options.Auth.Length > 0) endpoint["auth"] = options.Auth;

            if (options.Mtu > 0) endpoint["mtu"] = options.Mtu;

            if (options.MssFix == 0) endpoint["mss_fix_disabled"] = true;
            else if (options.MssFix > 0) endpoint["mss_fix"] = options.MssFix;

            if (options.RenegSec == 0) endpoint["renegotiate_disabled"] = true;
            else if (options.RenegSec > 0) endpoint["renegotiate_interval"] = options.RenegSec + "s";

            if (options.RenegBytes > 0) endpoint["renegotiate_bytes"] = options.RenegBytes;
            if (options.RenegPackets > 0) endpoint["renegotiate_packets"] = options.RenegPackets;
            if (options.PingInterval > 0) endpoint["ping_interval"] = options.PingInterval + "s";
            if (options.PingRestart > 0) endpoint["ping_restart"] = options.PingRestart + "s";
            if (options.TlsTimeout > 0) endpoint["tls_timeout"] = options.TlsTimeout + "s";
            if (options.HandshakeWindow > 0) endpoint["handshake_window"] = options.HandshakeWindow + "s";
            if (options.ExplicitExitNotify > 0) endpoint["explicit_exit_notify"] = options.ExplicitExitNotify;
            if (options.Fragment > 0) endpoint["fragment"] = options.Fragment;

            string compressionLzo = NormalizeCompressionLzo(options.CompLzo);
            if (compressionLzo.Length > 0) endpoint["compression_lzo"] = compressionLzo;

            string compression = NormalizeCompression(options.Compress);
            if (compression.Length > 0) endpoint["compression"] = compression;

            string allowCompression = NormalizeAllowCompression(options.AllowCompression);
            if (allowCompression.Length > 0) endpoint["allow_compression"] = allowCompression;

            if (options.RedirectGateway)
            {
                endpoint["redirect_gateway"] = true;

                var flags = SplitArgs(options.RedirectGatewayFlags);
                if (flags.Length > 0) endpoint["redirect_gateway_flags"] = new JArray(flags);
            }

            if (options.RedirectPrivate) endpoint["redirect_private"] = true;
            if (options.BlockIpv6) endpoint["block_ipv6"] = true;
            if (options.RouteNoPull) endpoint["route_no_pull"] = true;

            if (options.PullFilters.Count > 0)
            {
                var filters = new JArray();
                foreach (var filter in options.PullFilters)
                {
                    var args = SplitArgs(filter);
                    if (args.Length < 2) continue;

                    string action = args[0].ToLowerInvariant();
                    if (action != "ignore" && action != "accept" && action != "reject") continue;

                    filters.Add(new JObject { ["action"] = action, ["text"] = string.Join(" ", args.Skip(1)) });
                }

                if (filters.Count > 0) endpoint["pull_filters"] = filters;
            }
        }

        // ── OpenVPN value normalizers ───────────────────────────────────────────────────────

        private static string NormalizeNetwork(string value)
        {
            string net = FirstArg(value).ToLowerInvariant();
            int dash = net.IndexOf('-');
            if (dash > 0) net = net.Substring(0, dash);

            switch (net)
            {
                case "udp":
                case "udp4":
                case "udp6":
                case "tcp":
                case "tcp4":
                case "tcp6":
                    return net;
                default:
                    return "";
            }
        }

        private static string NormalizeCertificateSide(string value)
        {
            string side = FirstArg(value).ToLowerInvariant();
            return side == "server" || side == "client" ? side : "";
        }

        private static string NormalizeTlsVersion(string value)
        {
            string version = FirstArg(value).ToLowerInvariant();
            return version == "1.0" || version == "1.1" || version == "1.2" || version == "1.3" ? version : "";
        }

        private static string NormalizeAuthRetry(string value)
        {
            string retry = FirstArg(value).ToLowerInvariant();
            return retry == "none" || retry == "nointeract" || retry == "interact" ? retry : "";
        }

        private static string NormalizeAllowCompression(string value)
        {
            string mode = FirstArg(value).ToLowerInvariant();
            return mode == "no" || mode == "asym" || mode == "yes" ? mode : "";
        }

        private static string[] SplitCiphers(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new string[0];

            return value.Split(new[] { ':', ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => v.Trim())
                        .Where(v => v.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
        }

        private static string NormalizeCompression(string value)
        {
            switch (FirstArg(value).ToLowerInvariant())
            {
                case "yes": return "stub";
                case "no": return "no";
                case "lz4": return "lz4";
                case "lz4-v2": return "lz4-v2";
                case "stub": return "stub";
                case "stub-v2": return "stub-v2";
                case "off": return "off";
                default: return "";
            }
        }

        private static string NormalizeCompressionLzo(string value)
        {
            switch (FirstArg(value).ToLowerInvariant())
            {
                case "yes": return "yes";
                case "no": return "no";
                case "adaptive": return "adaptive";
                case "asym": return "asym";
                default: return "";
            }
        }

        private static int ParseInt(string value, int fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
        }
    }
}

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
using System.Text;
using Newtonsoft.Json.Linq;
using System.Web;
using System.Collections.Specialized;
using System.Linq;
using System.Collections.Generic;

namespace CrimsonX.Services
{
    public static class XrayLinkParser
    {
        public static bool TryParseLink(string link, out string jsonResult)
        {
            jsonResult = string.Empty;
            if (string.IsNullOrWhiteSpace(link)) return false;

            link = link.Trim();
            try
            {
                JObject outbound = new JObject();

                if (link.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
                    outbound = ParseVmess(link.Substring(8));
                else if (link.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
                    outbound = ParseVless(link);
                else if (link.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase))
                    outbound = ParseTrojan(link);
                else if (link.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
                    outbound = ParseShadowsocks(link);
                else if (link.StartsWith("wireguard://", StringComparison.OrdinalIgnoreCase))
                    outbound = ParseWireguard(link);
                else if (link.StartsWith("socks://", StringComparison.OrdinalIgnoreCase)
                      || link.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase)
                      || link.StartsWith("socks4://", StringComparison.OrdinalIgnoreCase))
                    outbound = ParseSocks(link);
                else
                    return false;

                if (outbound == null || outbound["protocol"] == null)
                    return false;

                if (outbound["streamSettings"] is JObject parsedStream
                    && string.Equals(parsedStream["network"]?.ToString(), "quic", StringComparison.OrdinalIgnoreCase))
                    return false;

                var outboundsArray = new JArray { outbound };
                var root = new JObject { ["outbounds"] = outboundsArray };
                jsonResult = root.ToString(Newtonsoft.Json.Formatting.Indented);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryParseCustomConfig(string raw, out string outboundsJson)
        {
            outboundsJson = string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string text = raw.Trim();

            if (!text.StartsWith("{")) return TryParseLink(text, out outboundsJson);

            try
            {
                var root = JObject.Parse(text);

                if (root["outbounds"] is JArray arr)
                {
                    if (arr.Count == 0) return false;
                    outboundsJson = text;
                    return true;
                }

                if (root["protocol"] != null)
                {
                    var wrapped = new JArray();
                    wrapped.Add(root);
                    outboundsJson = new JObject { ["outbounds"] = wrapped }.ToString(Newtonsoft.Json.Formatting.None);
                    return true;
                }

                outboundsJson = text;
                return true;
            }
            catch
            {
                return false;
            }
        }


        public static string ExtractServerAddress(string jsonResult)
        {
            try
            {
                var root = JObject.Parse(jsonResult);
                if (root["outbounds"] is JArray arr && arr.Count > 0)
                {
                    var outb = arr[0] as JObject;
                    if (outb?["settings"] is JObject settings)
                    {
                        if (settings["vnext"] is JArray vnext && vnext.Count > 0)
                        {
                            var address = vnext[0]?["address"]?.ToString();
                            if (!string.IsNullOrEmpty(address)) return address;
                        }
                        if (settings["servers"] is JArray servers && servers.Count > 0)
                        {
                            var address = servers[0]?["address"]?.ToString();
                            if (!string.IsNullOrEmpty(address)) return address;
                        }
                        if (settings["peers"] is JArray peers && peers.Count > 0)
                        {
                            var endpoint = peers[0]?["endpoint"]?.ToString();
                            if (!string.IsNullOrEmpty(endpoint))
                            {
                                int colonIdx = endpoint.LastIndexOf(':');
                                if (colonIdx > 0) return endpoint.Substring(0, colonIdx);
                                return endpoint;
                            }
                        }
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        public static string GetSubnetOrDomain(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return string.Empty;
            if (System.Net.IPAddress.TryParse(address, out var ip))
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    var parts = address.Split('.');
                    if (parts.Length == 4) return $"{parts[0]}.{parts[1]}.{parts[2]}.0/24";
                }
            }
            return address.ToLowerInvariant();
        }

        public static bool IsLocalAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return false;
            address = address.Trim();

            if (address.StartsWith("[", StringComparison.Ordinal))
            {
                int close = address.IndexOf(']');
                address = close > 0 ? address.Substring(1, close - 1) : address.TrimStart('[');
            }

            if (string.Equals(address, "localhost", StringComparison.OrdinalIgnoreCase)) return true;

            if (!System.Net.IPAddress.TryParse(address, out var ip)) return false;
            if (System.Net.IPAddress.IsLoopback(ip)) return true;

            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var b = ip.GetAddressBytes();
                if (b[0] == 10) return true;                               // 10.0.0.0/8
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;  // 172.16.0.0/12
                if (b[0] == 192 && b[1] == 168) return true;               // 192.168.0.0/16
                if (b[0] == 169 && b[1] == 254) return true;               // 169.254.0.0/16
                return false;
            }

            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal;           // fe80::/10, fec0::/10
            }

            return false;
        }

        public static bool IsLocalOutbound(JObject outbound)
        {
            if (outbound == null) return false;

            var settings = outbound["settings"] as JObject;
            if (settings == null) return false;

            // VMess / VLESS / Trojan
            if (settings["vnext"] is JArray vnext && vnext.Count > 0)
                return IsLocalAddress(vnext[0]?["address"]?.ToString());

            // Shadowsocks / Socks
            if (settings["servers"] is JArray servers && servers.Count > 0)
                return IsLocalAddress(servers[0]?["address"]?.ToString());

            // WireGuard
            if (settings["peers"] is JArray peers && peers.Count > 0)
            {
                string endpoint = peers[0]?["endpoint"]?.ToString();
                if (!string.IsNullOrWhiteSpace(endpoint))
                {
                    if (endpoint.StartsWith("[", StringComparison.Ordinal))
                    {
                        int close = endpoint.IndexOf(']');
                        if (close > 0) return IsLocalAddress(endpoint.Substring(1, close - 1));
                    }
                    int colonIdx = endpoint.LastIndexOf(':');
                    return IsLocalAddress(colonIdx > 0 ? endpoint.Substring(0, colonIdx) : endpoint);
                }
            }

            return false;
        }

        private static string DecodeBase64(string b64)
        {
            b64 = b64.Trim().Replace("-", "+").Replace("_", "/");
            int mod = b64.Length % 4;
            if (mod > 0) b64 += new string('=', 4 - mod);
            return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
        }

        private static readonly string[] SupportedSchemes = { "vless://", "trojan://" };

        public static List<string> ExtractConfigs(string content)
        {
            var links = new List<string>();
            if (string.IsNullOrWhiteSpace(content)) return links;

            string decoded = DecodeFeedContent(content);

            var lines = decoded.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var l = line.Trim();
                foreach (var scheme in SupportedSchemes)
                {
                    if (l.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!IsGrpcLink(l))
                            links.Add(l);
                        break;
                    }
                }
            }
            return links;
        }

        private static string DecodeFeedContent(string content)
        {
            if (content.Contains("://")) return content;
            try { return DecodeBase64(content); }
            catch { return content; }
        }

        public static string DescribeSchemes(string content)
        {
            if (string.IsNullOrEmpty(content)) return "empty";

            int vless = 0, trojan = 0, ss = 0, vmess = 0, skipped = 0;
            foreach (var line in DecodeFeedContent(content).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var l = line.Trim();
                if (l.StartsWith("vless://", StringComparison.OrdinalIgnoreCase)) { vless++; if (IsGrpcLink(l)) skipped++; }
                else if (l.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase)) { trojan++; if (IsGrpcLink(l)) skipped++; }
                else if (l.StartsWith("ss://", StringComparison.OrdinalIgnoreCase)) ss++;
                else if (l.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase)) vmess++;
            }

            return $"vless={vless} trojan={trojan} ss={ss} vmess={vmess} grpc-skipped={skipped}";
        }

        public static bool IsGrpcLink(string link)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(link)) return false;

                int q = link.IndexOf('?');
                if (q < 0) return false;

                var query = HttpUtility.ParseQueryString(link.Substring(q + 1));
                string net = query["type"] ?? query["net"];
                return string.Equals(net, "grpc", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static bool IsGrpcOutbound(string outboundJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(outboundJson)) return false;

                var root = JObject.Parse(outboundJson);
                if (root["outbounds"] is JArray arr && arr.Count > 0 && arr[0] is JObject outb)
                {
                    if (outb["streamSettings"] is JObject stream)
                    {
                        if (string.Equals(stream["network"]?.ToString(), "grpc", StringComparison.OrdinalIgnoreCase))
                            return true;
                        if (stream["grpcSettings"] != null)
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static JObject ParseWireguard(string link)
        {
            string content = link.StartsWith("wireguard://", StringComparison.OrdinalIgnoreCase)
                ? link.Substring(12) : link;

            int queryIdx = content.IndexOf('?');
            int hashIdx = content.IndexOf('#');

            int endAuth = content.Length;
            if (queryIdx >= 0) endAuth = Math.Min(endAuth, queryIdx);
            if (hashIdx >= 0) endAuth = Math.Min(endAuth, hashIdx);
            string authorityPart = content.Substring(0, endAuth);

            string queryStr = "";
            if (queryIdx >= 0)
            {
                int endQuery = (hashIdx >= 0 && hashIdx > queryIdx) ? hashIdx : content.Length;
                queryStr = content.Substring(queryIdx + 1, endQuery - queryIdx - 1);
            }

            string privateKey = "", host = "";
            int port = 51820;

            int atIdx = authorityPart.IndexOf('@');
            if (atIdx > 0)
            {
                privateKey = HttpUtility.UrlDecode(authorityPart.Substring(0, atIdx));
                string hostPort = authorityPart.Substring(atIdx + 1);

                if (hostPort.StartsWith("["))
                {
                    int closeBracket = hostPort.IndexOf(']');
                    if (closeBracket > 0)
                    {
                        host = hostPort.Substring(1, closeBracket - 1);
                        string portStr = hostPort.Substring(closeBracket + 1);
                        if (portStr.StartsWith(":"))
                        {
                            var portText = portStr.Substring(1).Trim();
                            if (int.TryParse(portText, out int parsedPort)) port = parsedPort; 
                        }
                    }
                }
                else
                {
                    int colonIdx = hostPort.LastIndexOf(':');
                    if (colonIdx > 0)
                    {
                        host = hostPort.Substring(0, colonIdx);
                        var portText = hostPort.Substring(colonIdx + 1).Trim();
                        if (int.TryParse(portText, out int parsedPort)) port = parsedPort; 
                    }
                    else
                    {
                        host = hostPort;
                    }
                }
            }

            var query = HttpUtility.ParseQueryString(queryStr);

            var peerObj = new JObject
            {
                ["endpoint"] = $"{host}:{port}",
                ["publicKey"] = query["publickey"] ?? query["public_key"] ?? query["pk"] ?? ""
            };

            string reservedStr = query["reserved"];
            if (!string.IsNullOrEmpty(reservedStr))
            {
                var parts = reservedStr.Split(',').Select(s => int.TryParse(s, out var i) ? i : 0).ToArray();
                if (parts.Length == 3) peerObj["reserved"] = new JArray(parts);
            }

            var settings = new JObject
            {
                ["secretKey"] = privateKey
            };

            string addressStr = query["address"];
            if (!string.IsNullOrEmpty(addressStr))
            {
                settings["address"] = new JArray(addressStr.Split(','));
            }

            settings["peers"] = new JArray { peerObj };

            if (int.TryParse(query["mtu"], out int mtu))
            {
                settings["mtu"] = mtu;
            }

            return new JObject
            {
                ["protocol"] = "wireguard",
                ["settings"] = settings,
                ["streamSettings"] = new JObject { ["network"] = "raw" },
                ["mux"] = new JObject { ["enabled"] = false }
            };
        }

        private static JObject ParseVmess(string b64)
        {
            string json = DecodeBase64(b64);
            var v = JObject.Parse(json);

            var outbound = new JObject
            {
                ["protocol"] = "vmess",
                ["settings"] = new JObject
                {
                    ["vnext"] = new JArray
                    {
                        new JObject
                        {
                            ["address"] = v["add"]?.ToString(),
                            ["port"] = int.TryParse(v["port"]?.ToString(), out int p) ? p : 443,
                            ["users"] = new JArray
                            {
                                new JObject
                                {
                                    ["id"] = v["id"]?.ToString(),
                                    ["alterId"] = int.TryParse(v["aid"]?.ToString(), out int aid) ? aid : 0,
                                    ["security"] = string.IsNullOrEmpty(v["scy"]?.ToString()) ? "auto" : v["scy"]?.ToString()
                                }
                            }
                        }
                    }
                }
            };

            var query = new NameValueCollection();
            if (v["net"] != null) query["type"] = v["net"]?.ToString();
            if (v["tls"] != null) query["security"] = v["tls"]?.ToString();
            if (v["sni"] != null) query["sni"] = v["sni"]?.ToString();
            if (v["alpn"] != null) query["alpn"] = v["alpn"]?.ToString();
            if (v["host"] != null) query["host"] = v["host"]?.ToString();
            if (v["path"] != null) query["path"] = v["path"]?.ToString();
            if (v["fp"] != null) query["fp"] = v["fp"]?.ToString();
            if (v["type"] != null) query["headerType"] = v["type"]?.ToString();

            AddStreamSettings(outbound, query);
            return outbound;
        }

        private static JObject ParseVless(string link)
        {
            var uri = new Uri(link);
            var query = HttpUtility.ParseQueryString(uri.Query);

            var outbound = new JObject
            {
                ["protocol"] = "vless",
                ["settings"] = new JObject
                {
                    ["vnext"] = new JArray
                    {
                        new JObject
                        {
                            ["address"] = uri.IdnHost,
                            ["port"] = uri.Port,
                            ["users"] = new JArray
                            {
                                new JObject
                                {
                                    ["id"] = uri.UserInfo,
                                    ["encryption"] = query["encryption"] ?? "none"
                                }
                            }
                        }
                    }
                }
            };

            if (!string.IsNullOrEmpty(query["flow"]))
            {
                outbound["settings"]!["vnext"]![0]!["users"]![0]!["flow"] = query["flow"];
            }

            AddStreamSettings(outbound, query);
            return outbound;
        }

        private static JObject ParseTrojan(string link)
        {
            var uri = new Uri(link);
            var query = HttpUtility.ParseQueryString(uri.Query);

            var outbound = new JObject
            {
                ["protocol"] = "trojan",
                ["settings"] = new JObject
                {
                    ["servers"] = new JArray
                    {
                        new JObject
                        {
                            ["address"] = uri.IdnHost,
                            ["port"] = uri.Port,
                            ["password"] = uri.UserInfo
                        }
                    }
                }
            };

            AddStreamSettings(outbound, query);
            return outbound;
        }

        private static JObject ParseShadowsocks(string link)
        {
            string payload = link.Substring(5);

            int hashIdx = payload.IndexOf('#');
            if (hashIdx >= 0) payload = payload.Substring(0, hashIdx);

            string queryStr = "";
            int queryIdx = payload.IndexOf('?');
            if (queryIdx >= 0)
            {
                queryStr = payload.Substring(queryIdx + 1);
                payload = payload.Substring(0, queryIdx);
            }

            if (!string.IsNullOrEmpty(HttpUtility.ParseQueryString(queryStr)["plugin"]))
                return null;

            string userInfo;
            string authority;

            int atIdx = payload.LastIndexOf('@');
            if (atIdx >= 0)
            {
                userInfo = payload.Substring(0, atIdx);
                authority = payload.Substring(atIdx + 1);
            }
            else
            {
                string decoded;
                try { decoded = DecodeBase64(payload); }
                catch { return null; }

                int decodedAt = decoded.LastIndexOf('@');
                if (decodedAt < 0) return null;

                userInfo = decoded.Substring(0, decodedAt);
                authority = decoded.Substring(decodedAt + 1);
            }

            string methodPass = DecodeSsUserInfo(userInfo);
            string[] mpParts = methodPass.Split(new[] { ':' }, 2);
            if (mpParts.Length < 2 || string.IsNullOrWhiteSpace(mpParts[0]) || string.IsNullOrEmpty(mpParts[1]))
                return null;

            if (!TrySplitHostPort(authority, out string host, out int port))
                return null;

            var outbound = new JObject
            {
                ["protocol"] = "shadowsocks",
                ["settings"] = new JObject
                {
                    ["servers"] = new JArray
                    {
                        new JObject
                        {
                            ["address"] = host,
                            ["port"] = port,
                            ["method"] = mpParts[0],
                            ["password"] = mpParts[1]
                        }
                    }
                }
            };

            return outbound;
        }

        private static string DecodeSsUserInfo(string userInfo)
        {
            string raw = HttpUtility.UrlDecode(userInfo ?? "");
            if (raw.Length == 0) return "";

            if (raw.Contains(":")) return raw;

            try
            {
                string decoded = HttpUtility.UrlDecode(DecodeBase64(raw));
                return decoded.Contains(":") ? decoded : "";
            }
            catch
            {
                return "";
            }
        }

        private static bool TrySplitHostPort(string authority, out string host, out int port)
        {
            host = "";
            port = 0;
            if (string.IsNullOrWhiteSpace(authority)) return false;

            authority = authority.Trim();

            int separator;
            if (authority.StartsWith("[", StringComparison.Ordinal))
            {
                int close = authority.IndexOf(']');
                if (close < 0) return false;

                host = authority.Substring(1, close - 1);
                if (close + 1 >= authority.Length || authority[close + 1] != ':') return false;
                separator = close + 1;
            }
            else
            {
                separator = authority.LastIndexOf(':');
                if (separator <= 0) return false;

                host = authority.Substring(0, separator);
            }

            string portStr = authority.Substring(separator + 1).Trim();

            int slashIdx = portStr.IndexOf('/');
            if (slashIdx >= 0) portStr = portStr.Substring(0, slashIdx);

            return !string.IsNullOrWhiteSpace(host)
                && int.TryParse(portStr, out port)
                && port > 0 && port <= 65535;
        }

        private static JObject ParseSocks(string link)
        {
            var uri = new Uri(link);
            var query = HttpUtility.ParseQueryString(uri.Query);

            string user = "";
            string pass = "";
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                string raw = uri.UserInfo;

                if (raw.Contains(":", StringComparison.Ordinal))
                {
                    var ui = raw.Split(new[] { ':' }, 2);
                    user = HttpUtility.UrlDecode(ui[0]);
                    if (ui.Length > 1) pass = HttpUtility.UrlDecode(ui[1]);
                }
                else
                {
                    try
                    {
                        string decoded = DecodeBase64(raw);
                        if (decoded.Contains(":", StringComparison.Ordinal))
                        {
                            var ui = decoded.Split(new[] { ':' }, 2);
                            user = ui[0] ?? "";
                            pass = ui.Length > 1 ? (ui[1] ?? "") : "";
                        }
                        else
                        {
                            user = HttpUtility.UrlDecode(raw);
                        }
                    }
                    catch
                    {
                        user = HttpUtility.UrlDecode(raw);
                    }
                }
            }

            if (string.IsNullOrEmpty(user) && string.IsNullOrEmpty(pass))
            {
                user = "";
                pass = "";
            }

            var server = new JObject
            {
                ["address"] = uri.IdnHost,
                ["port"] = uri.Port
            };

            if (!string.IsNullOrEmpty(user) || !string.IsNullOrEmpty(pass))
            {
                if (!(string.IsNullOrEmpty(user) && string.IsNullOrEmpty(pass)))
                {
                    server["users"] = new JArray
                    {
                        new JObject
                        {
                            ["user"] = user,
                            ["pass"] = pass
                        }
                    };
                }
            }

            var outbound = new JObject
            {
                ["protocol"] = "socks",
                ["settings"] = new JObject
                {
                    ["servers"] = new JArray { server }
                }
            };

            AddStreamSettings(outbound, query);
            return outbound;
        }

        private static void AddStreamSettings(JObject outbound, NameValueCollection query)
        {
            var stream = new JObject();

            string net = query["type"]?.ToLowerInvariant() ?? "tcp";
            string security = query["security"]?.ToLowerInvariant() ?? "none";

            stream["network"] = net;
            if (security != "none") stream["security"] = security;

            if (security == "tls" || security == "reality")
            {
                var tlsObj = new JObject();

                string sni = query["sni"];
                if (!string.IsNullOrEmpty(sni)) tlsObj["serverName"] = sni;

                string fp = query["fp"];
                if (!string.IsNullOrEmpty(fp)) tlsObj["fingerprint"] = fp;

                string alpn = query["alpn"];
                if (!string.IsNullOrEmpty(alpn)) tlsObj["alpn"] = new JArray(alpn.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)));

                string ech = query["ech"];
                if (!string.IsNullOrEmpty(ech)) tlsObj["echConfigList"] = ech;

                string vcn = query["vcn"];
                if (!string.IsNullOrEmpty(vcn)) tlsObj["verifyPeerCertByName"] = vcn;

                string pcs = query["pcs"];
                if (!string.IsNullOrEmpty(pcs)) tlsObj["pinnedPeerCertSha256"] = pcs;

                string pqv = query["pqv"];
                if (!string.IsNullOrEmpty(pqv)) tlsObj["mldsa65Verify"] = pqv;

                bool allowInsecure = query["allowInsecure"] == "1" || query["insecure"] == "1" || query["allowInsecure"] == "true" || query["insecure"] == "true";
                if (allowInsecure) tlsObj["allowInsecure"] = true;

                if (security == "reality")
                {
                    string pbk = query["pbk"];
                    if (!string.IsNullOrEmpty(pbk)) tlsObj["password"] = pbk;

                    string sid = query["sid"];
                    if (!string.IsNullOrEmpty(sid)) tlsObj["shortId"] = sid;

                    string spx = query["spx"];
                    if (!string.IsNullOrEmpty(spx)) tlsObj["spiderX"] = spx;

                    string fm = query["fm"];
                    if (!string.IsNullOrEmpty(fm))
                    {
                        try { stream["finalMask"] = JObject.Parse(fm); }
                        catch { tlsObj["finalMask"] = fm; }
                    }
                }

                stream[security + "Settings"] = tlsObj;
            }

            if (net == "ws")
            {
                var wsObj = new JObject();
                string path = query["path"];
                if (!string.IsNullOrEmpty(path)) wsObj["path"] = path;

                string host = query["host"];
                if (!string.IsNullOrEmpty(host)) wsObj["host"] = host;

                stream["wsSettings"] = wsObj;
            }
            else if (net == "tcp" || net == "raw")
            {
                string headerType = query["headerType"];
                if (headerType == "http")
                {
                    var tcpObj = new JObject
                    {
                        ["header"] = new JObject
                        {
                            ["type"] = "http",
                            ["request"] = new JObject()
                        }
                    };

                    string path = query["path"];
                    tcpObj["header"]!["request"]!["path"] = new JArray(string.IsNullOrEmpty(path) ? "/" : path);

                    string host = query["host"];
                    if (!string.IsNullOrEmpty(host))
                        tcpObj["header"]!["request"]!["headers"] = new JObject { ["Host"] = new JArray(host.Split(',').Select(s => s.Trim())) };

                    stream[net == "raw" ? "rawSettings" : "tcpSettings"] = tcpObj;
                }
            }
            else if (net == "grpc")
            {
                var grpcObj = new JObject();

                string serviceName = query["serviceName"] ?? query["path"];
                if (!string.IsNullOrEmpty(serviceName)) grpcObj["serviceName"] = serviceName;

                string authority = query["authority"];
                if (!string.IsNullOrEmpty(authority)) grpcObj["authority"] = authority;

                string mode = query["mode"];
                if (!string.IsNullOrEmpty(mode)) grpcObj["multiMode"] = mode == "multi";

                stream["grpcSettings"] = grpcObj;
            }
            else if (net == "kcp" || net == "mkcp")
            {
                var kcpObj = new JObject();
                if (int.TryParse(query["mtu"], out int mtu)) kcpObj["mtu"] = mtu;
                if (int.TryParse(query["tti"], out int tti)) kcpObj["tti"] = tti;

                stream["kcpSettings"] = kcpObj;
            }
            else if (net == "httpupgrade")
            {
                var httpupgradeObj = new JObject();
                string path = query["path"];
                if (!string.IsNullOrEmpty(path)) httpupgradeObj["path"] = path;

                string host = query["host"];
                if (!string.IsNullOrEmpty(host)) httpupgradeObj["host"] = host;

                stream["httpupgradeSettings"] = httpupgradeObj;
            }
            else if (net == "xhttp")
            {
                var xhttpObj = new JObject();
                string path = query["path"];
                if (!string.IsNullOrEmpty(path)) xhttpObj["path"] = path;

                string host = query["host"];
                if (!string.IsNullOrEmpty(host)) xhttpObj["host"] = host;

                string mode = query["mode"];
                if (!string.IsNullOrEmpty(mode)) xhttpObj["mode"] = mode;

                string extra = query["extra"];
                if (!string.IsNullOrEmpty(extra))
                {
                    try { xhttpObj["extra"] = JObject.Parse(extra); }
                    catch { xhttpObj["extra"] = extra; }
                }

                stream["xhttpSettings"] = xhttpObj;
            }

            if (stream.Count > 0)
            {
                outbound["streamSettings"] = stream;
            }
        }
        private static string RawTokenString(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "";
            return token.Type == JTokenType.String ? token.ToString() : token.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static bool TryBuildShareLink(string outboundJson, out string link, string displayName = null)
        {
            link = string.Empty;
            if (string.IsNullOrWhiteSpace(outboundJson)) return false;

            try
            {
                string trimmed = outboundJson.Trim();
                if (!trimmed.StartsWith("{"))
                {
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        link = trimmed;
                        return true;
                    }

                    int hash = trimmed.IndexOf('#');
                    string basePart = hash >= 0 ? trimmed.Substring(0, hash) : trimmed;
                    link = basePart + "#" + Uri.EscapeDataString(displayName);
                    return true;
                }

                JObject root = JObject.Parse(trimmed);
                if (root["outbounds"] is JArray arr && arr.Count > 0)
                    root = (JObject)arr[0];

                string protocol = root["protocol"]?.ToString()?.ToLowerInvariant() ?? "";
                var settings = root["settings"] as JObject;
                var stream = root["streamSettings"] as JObject;

                string address = "", password = "", method = "", id = "", flow = "", alterId = "0", vmessSecurity = "auto";
                int port = 0;

                if (settings?["vnext"] is JArray vnext && vnext.Count > 0)
                {
                    var node = (JObject)vnext[0];
                    address = node["address"]?.ToString() ?? "";
                    port = node["port"]?.Value<int>() ?? 0;
                    if (node["users"] is JArray users && users.Count > 0)
                    {
                        var user = (JObject)users[0];
                        id = user["id"]?.ToString() ?? "";
                        flow = user["flow"]?.ToString() ?? "";
                        alterId = user["alterId"]?.ToString() ?? "0";
                        vmessSecurity = user["security"]?.ToString() ?? "auto";
                    }
                }
                else if (settings?["servers"] is JArray servers && servers.Count > 0)
                {
                    var node = (JObject)servers[0];
                    address = node["address"]?.ToString() ?? "";
                    port = node["port"]?.Value<int>() ?? 0;
                    password = node["password"]?.ToString() ?? "";
                    method = node["method"]?.ToString() ?? "";
                }

                if (string.IsNullOrWhiteSpace(address) || port <= 0) return false;

                string network = stream?["network"]?.ToString() ?? "tcp";
                string security = stream?["security"]?.ToString() ?? "none";
                var tls = stream?[security + "Settings"] as JObject;

                string sni = tls?["serverName"]?.ToString() ?? "";
                string fp = tls?["fingerprint"]?.ToString() ?? "";
                string alpn = tls?["alpn"] is JArray alpnArr && alpnArr.Count > 0
                    ? string.Join(",", alpnArr.Select(a => a.ToString()))
                    : "";
                bool insecure = tls?["allowInsecure"]?.Value<bool>() == true;
                string pbk = tls?["password"]?.ToString() ?? "";
                if (pbk.Length == 0) pbk = tls?["publicKey"]?.ToString() ?? "";
                string sid = tls?["shortId"]?.ToString() ?? "";
                string spx = tls?["spiderX"]?.ToString() ?? "";
                string fm = RawTokenString(stream?["finalMask"]);
                if (fm.Length == 0) fm = RawTokenString(tls?["finalMask"]);

                string path = "", host = "", serviceName = "", headerType = "", xhttpMode = "", xhttpExtra = "";
                switch (network)
                {
                    case "ws":
                        path = stream?["wsSettings"]?["path"]?.ToString() ?? "";
                        host = stream?["wsSettings"]?["host"]?.ToString() ?? "";
                        if (host.Length == 0) host = stream?["wsSettings"]?["headers"]?["Host"]?.ToString() ?? "";
                        break;
                    case "grpc":
                        serviceName = stream?["grpcSettings"]?["serviceName"]?.ToString() ?? "";
                        break;
                    case "tcp":
                    case "raw":
                    {
                        var rawSettings = stream?["rawSettings"] ?? stream?["tcpSettings"];
                        headerType = rawSettings?["header"]?["type"]?.ToString() ?? "";
                        if (headerType == "http")
                        {
                            path = (rawSettings?["header"]?["request"]?["path"] as JArray)?.FirstOrDefault()?.ToString() ?? "";
                            host = (rawSettings?["header"]?["request"]?["headers"]?["Host"] as JArray)?.FirstOrDefault()?.ToString() ?? "";
                        }
                        break;
                    }
                    case "httpupgrade":
                        path = stream?["httpupgradeSettings"]?["path"]?.ToString() ?? "";
                        host = stream?["httpupgradeSettings"]?["host"]?.ToString() ?? "";
                        break;
                    case "xhttp":
                    {
                        path = stream?["xhttpSettings"]?["path"]?.ToString() ?? "";
                        host = stream?["xhttpSettings"]?["host"]?.ToString() ?? "";
                        xhttpMode = stream?["xhttpSettings"]?["mode"]?.ToString() ?? "";
                        xhttpExtra = RawTokenString(stream?["xhttpSettings"]?["extra"]);
                        break;
                    }
                }

                string fragment = string.IsNullOrWhiteSpace(displayName) ? "" : "#" + Uri.EscapeDataString(displayName);

                if (protocol == "shadowsocks")
                {
                    link = $"ss://{Convert.ToBase64String(Encoding.UTF8.GetBytes($"{method}:{password}"))}@{address}:{port}{fragment}";
                    return true;
                }

                var query = new List<string> { "type=" + network };
                if (security != "none") query.Add("security=" + security);
                if (!string.IsNullOrEmpty(sni)) query.Add("sni=" + Uri.EscapeDataString(sni));
                if (!string.IsNullOrEmpty(fp)) query.Add("fp=" + fp);
                if (!string.IsNullOrEmpty(alpn)) query.Add("alpn=" + Uri.EscapeDataString(alpn));
                if (insecure) query.Add("allowInsecure=1");
                if (!string.IsNullOrEmpty(flow)) query.Add("flow=" + flow);
                if (!string.IsNullOrEmpty(pbk)) query.Add("pbk=" + Uri.EscapeDataString(pbk));
                if (!string.IsNullOrEmpty(sid)) query.Add("sid=" + Uri.EscapeDataString(sid));
                if (!string.IsNullOrEmpty(spx)) query.Add("spx=" + Uri.EscapeDataString(spx));
                if (!string.IsNullOrEmpty(fm)) query.Add("fm=" + Uri.EscapeDataString(fm));

                if (network == "ws" || network == "httpupgrade" || network == "xhttp")
                {
                    if (!string.IsNullOrEmpty(path)) query.Add("path=" + Uri.EscapeDataString(path));
                    if (!string.IsNullOrEmpty(host)) query.Add("host=" + Uri.EscapeDataString(host));

                    if (network == "xhttp")
                    {
                        if (!string.IsNullOrEmpty(xhttpMode)) query.Add("mode=" + xhttpMode);
                        if (!string.IsNullOrEmpty(xhttpExtra)) query.Add("extra=" + Uri.EscapeDataString(xhttpExtra));
                    }
                }
                else if (network == "grpc")
                {
                    if (!string.IsNullOrEmpty(serviceName)) query.Add("serviceName=" + Uri.EscapeDataString(serviceName));
                }
                else if ((network == "tcp" || network == "raw") && headerType == "http")
                {
                    query.Add("headerType=http");
                    if (!string.IsNullOrEmpty(path)) query.Add("path=" + Uri.EscapeDataString(path));
                    if (!string.IsNullOrEmpty(host)) query.Add("host=" + Uri.EscapeDataString(host));
                }

                string queryString = "?" + string.Join("&", query);

                switch (protocol)
                {
                    case "vless":
                        link = $"vless://{id}@{address}:{port}{queryString}{fragment}";
                        return true;

                    case "trojan":
                        link = $"trojan://{Uri.EscapeDataString(password)}@{address}:{port}{queryString}{fragment}";
                        return true;

                    case "vmess":
                    {
                        var vmess = new JObject
                        {
                            ["v"] = "2",
                            ["ps"] = displayName ?? "",
                            ["add"] = address,
                            ["port"] = port.ToString(),
                            ["id"] = id,
                            ["aid"] = alterId,
                            ["scy"] = vmessSecurity,
                            ["net"] = network,
                            ["type"] = string.IsNullOrEmpty(headerType) ? "none" : headerType,
                            ["host"] = host,
                            ["path"] = path,
                            ["tls"] = security == "none" ? "" : security,
                            ["sni"] = sni,
                            ["alpn"] = alpn,
                            ["fp"] = fp
                        };

                        link = "vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(vmess.ToString(Newtonsoft.Json.Formatting.None)));
                        return true;
                    }

                    default:
                        return false;
                }
            }
            catch
            {
                link = string.Empty;
                return false;
            }
        }
    }
}

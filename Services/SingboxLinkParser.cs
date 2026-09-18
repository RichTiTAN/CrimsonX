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
using System.Linq;
using System.Text;
using System.Web;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public static class SingboxLinkParser
    {
        private const string DefaultPacketEncoding = "xudp";

        private sealed class Parts
        {
            public string Scheme   = "";
            public string UserInfo = "";
            public string Host     = "";
            public int    Port     = 0;
            public string Fragment = "";
            public readonly Dictionary<string, string> Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public string Get(string key) => Query.TryGetValue(key, out var v) ? v : "";

            public bool Flag(string key)
            {
                if (!Query.TryGetValue(key, out var v)) return false;
                return v.Length == 0 || v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
        }

        public static bool TryParseLink(string link, out string outboundJson)
            => TryParseLink(link, out outboundJson, out _);

        public static bool TryParseLink(string link, out string outboundJson, out string label)
        {
            outboundJson = string.Empty;
            label = string.Empty;
            if (string.IsNullOrWhiteSpace(link)) return false;

            link = link.Trim();
            try
            {
                var outbound = ParseOutbound(link, out label);
                if (outbound == null || string.IsNullOrWhiteSpace(outbound["type"]?.ToString())) return false;

                outbound.Remove("tag");

                outboundJson = outbound.ToString(Newtonsoft.Json.Formatting.None);

                if (string.IsNullOrWhiteSpace(label)) label = DescribeOutbound(outbound);
                label = CleanLabel(label);
                if (label.Length == 0) label = outbound["type"].ToString();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string DescribeOutbound(JObject outbound)
        {
            try
            {
                string type   = outbound?["type"]?.ToString() ?? "";
                string server = outbound?["server"]?.ToString() ?? "";
                string port   = outbound?["server_port"]?.ToString() ?? "";
                if (type.Length == 0) return "";
                return server.Length > 0 ? $"{type} · {server}:{port}" : type;
            }
            catch { return ""; }
        }

        public static string LabelOf(string raw)
            => string.IsNullOrWhiteSpace(raw) ? "" : (TryParseLink(raw, out _, out var label) ? label : "");

        public static string KeyOf(string raw, string adapter)
            => Normalize(raw) + "|" + (string.IsNullOrWhiteSpace(adapter) ? "Default" : adapter.Trim());
        public static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            string text = raw.Trim();
            if (!TryParseLink(text, out var outboundJson, out _)) return StripWhitespace(text);

            try { return SortKeys(JObject.Parse(outboundJson)).ToString(Newtonsoft.Json.Formatting.None); }
            catch { return StripWhitespace(text); }
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

        public static JObject WithTagAndDial(JObject outbound, string tag, string adapterName, string adapterIp)
        {
            outbound["tag"] = tag;
            if (!string.IsNullOrWhiteSpace(adapterName) && !string.Equals(adapterName, "Default", StringComparison.OrdinalIgnoreCase))
            {
                outbound["bind_interface"] = adapterName.Trim();
                if (!string.IsNullOrWhiteSpace(adapterIp)) outbound["inet4_bind_address"] = adapterIp.Trim();
            }
            return outbound;
        }

        // ── Dispatch ────────────────────────────────────────────────────────────────────────

        private static JObject ParseOutbound(string link, out string label)
        {
            label = "";

            if (link.StartsWith("{")) return ParseRawJson(link, out label);

            if (link.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))                                     return ParseVless(link, out label);
            if (link.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))                                     return ParseVmess(link, out label);
            if (link.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase))                                    return ParseTrojan(link, out label);
            if (link.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))                                        return ParseShadowsocks(link, out label);
            if (link.StartsWith("anytls://", StringComparison.OrdinalIgnoreCase))                                    return ParseAnyTls(link, out label);
            if (link.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase)
             || link.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase))                                       return ParseHysteria2(link, out label);
            if (link.StartsWith("tuic://", StringComparison.OrdinalIgnoreCase))                                      return ParseTuic(link, out label);
            if (link.StartsWith("socks://", StringComparison.OrdinalIgnoreCase)
             || link.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase)
             || link.StartsWith("socks5h://", StringComparison.OrdinalIgnoreCase))                                   return ParseSocks(link, out label);
            if (link.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
             || link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))                                     return ParseHttp(link, out label);

            return null;
        }

        // ── URI helpers ─────────────────────────────────────────────────────────────────────

        private static Parts SplitUri(string link)
        {
            var parts = new Parts();
            int schemeIdx = link.IndexOf("://", StringComparison.Ordinal);
            if (schemeIdx <= 0) return null;

            parts.Scheme = link.Substring(0, schemeIdx).ToLowerInvariant();
            string rest = link.Substring(schemeIdx + 3);

            int hash = rest.IndexOf('#');
            if (hash >= 0)
            {
                parts.Fragment = SafeDecode(rest.Substring(hash + 1));
                rest = rest.Substring(0, hash);
            }

            int q = rest.IndexOf('?');
            if (q >= 0)
            {
                ParseQuery(rest.Substring(q + 1), parts.Query);
                rest = rest.Substring(0, q);
            }

            int at = rest.LastIndexOf('@');
            string hostPort = rest;
            if (at >= 0)
            {
                parts.UserInfo = rest.Substring(0, at);
                hostPort = rest.Substring(at + 1);
            }

            if (hostPort.StartsWith("["))
            {
                int close = hostPort.IndexOf(']');
                if (close < 0) return null;
                parts.Host = hostPort.Substring(1, close - 1);
                string tail = hostPort.Substring(close + 1);
                if (tail.StartsWith(":")) int.TryParse(tail.Substring(1), out parts.Port);
            }
            else
            {
                int colon = hostPort.LastIndexOf(':');
                if (colon > 0)
                {
                    parts.Host = hostPort.Substring(0, colon);
                    int.TryParse(hostPort.Substring(colon + 1), out parts.Port);
                }
                else
                {
                    parts.Host = hostPort;
                }
            }

            parts.Host = parts.Host.Trim();
            return parts.Host.Length == 0 ? null : parts;
        }

        private static void ParseQuery(string query, Dictionary<string, string> into)
        {
            foreach (var pair in query.Split('&'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                string key = SafeDecode(eq < 0 ? pair : pair.Substring(0, eq));
                string val = eq < 0 ? "" : SafeDecode(pair.Substring(eq + 1));
                if (key.Length > 0) into[key] = val;
            }
        }

        private static string SafeDecode(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            try { return HttpUtility.UrlDecode(value).Trim(); }
            catch { return value.Trim(); }
        }

        private static string DecodeBase64(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string s = value.Trim().Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "=";  break;
            }
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
            catch { return ""; }
        }

        private static string CleanLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "";
            label = label.Replace("\r", " ").Replace("\n", " ").Trim();
            if (label.Length > 40) label = label.Substring(0, 40).Trim();
            return label;
        }

        // ── TLS / transport helpers ─────────────────────────────────────────────────────────

        private static JObject BuildTls(Parts p, string security, bool defaultEnabled, out bool ok)
        {
            ok = true;
            security = (security ?? "").Trim().ToLowerInvariant();

            bool enable = security == "tls" || security == "reality" || security == "xtls"
                       || (defaultEnabled && security != "none" && security != "");

            string pbk = p.Get("pbk");
            if (security == "reality" && pbk.Length == 0) { ok = false; return null; }
            if (!enable) return null;

            var tls = new JObject { ["enabled"] = true };

            string sni = FirstNonEmpty(p.Get("sni"), p.Get("peer"), p.Get("host"), p.Get("serverName"));
            if (sni.Length > 0) tls["server_name"] = sni;

            if (p.Flag("insecure") || p.Get("allowInsecure") == "1" || p.Get("allow_insecure") == "1")
                tls["insecure"] = true;

            var alpn = SplitList(p.Get("alpn"));
            if (alpn.Length > 0) tls["alpn"] = new JArray(alpn);

            if (security == "reality")
            {
                var reality = new JObject { ["enabled"] = true, ["public_key"] = pbk };
                string sid = FirstNonEmpty(p.Get("sid"), p.Get("shortId"), p.Get("short_id"));
                if (sid.Length > 0) reality["short_id"] = sid;
                tls["reality"] = reality;
                tls["utls"] = BuildUtls(p);
            }
            else
            {
                string fp = FirstNonEmpty(p.Get("fp"), p.Get("fingerprint"));
                if (fp.Length > 0 || p.Get("utls") == "1") tls["utls"] = BuildUtls(p);
            }

            return tls;
        }

        private static JObject BuildUtls(Parts p)
        {
            string fp = FirstNonEmpty(p.Get("fp"), p.Get("fingerprint"));
            if (fp.Length == 0) fp = "chrome";
            return new JObject { ["enabled"] = true, ["fingerprint"] = fp };
        }

        private static bool TryBuildTransport(string net, string host, string path, string serviceName, out JObject transport)
        {
            transport = null;
            net = (net ?? "").Trim().ToLowerInvariant();

            switch (net)
            {
                case "":
                case "tcp":
                case "raw":
                    return true;

                case "ws":
                case "websocket":
                {
                    var t = new JObject { ["type"] = "ws" };
                    if (path.Length > 0) t["path"] = path;
                    if (host.Length > 0) t["headers"] = new JObject { ["Host"] = host };
                    transport = t;
                    return true;
                }

                case "grpc":
                {
                    var t = new JObject { ["type"] = "grpc" };
                    if (serviceName.Length > 0) t["service_name"] = serviceName;
                    transport = t;
                    return true;
                }

                case "http":
                case "h2":
                {
                    var t = new JObject { ["type"] = "http" };
                    var hosts = SplitList(host);
                    if (hosts.Length > 0) t["host"] = new JArray(hosts);
                    if (path.Length > 0) t["path"] = path;
                    transport = t;
                    return true;
                }

                case "httpupgrade":
                {
                    var t = new JObject { ["type"] = "httpupgrade" };
                    if (host.Length > 0) t["host"] = host;
                    if (path.Length > 0) t["path"] = path;
                    transport = t;
                    return true;
                }

                case "quic":
                    transport = new JObject { ["type"] = "quic" };
                    return true;

                default:
                    return false;
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            return "";
        }

        private static string[] SplitList(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();
            return value.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => v.Trim())
                        .Where(v => v.Length > 0)
                        .ToArray();
        }

        // ── vless ───────────────────────────────────────────────────────────────────────────

        private static JObject ParseVless(string link, out string label)
        {
            label = "";
            var p = SplitUri(link);
            if (p == null) return null;

            string uuid = SafeDecode(p.UserInfo);
            if (uuid.Length == 0) return null;

            int port = NormalizePort(p.Port, 443);
            string security = FirstNonEmpty(p.Get("security"), "none");
            string net = p.Get("type");
            string host = FirstNonEmpty(p.Get("host"), p.Get("hostName"));
            string path = p.Get("path");
            string serviceName = FirstNonEmpty(p.Get("serviceName"), p.Get("servicename"));

            var ob = new JObject
            {
                ["type"]        = "vless",
                ["server"]      = p.Host,
                ["server_port"] = port,
                ["uuid"]        = uuid
            };

            string flow = p.Get("flow");
            if (flow.Length > 0) ob["flow"] = flow;

            string pe = FirstNonEmpty(p.Get("packetEncoding"), p.Get("packet_encoding"));
            ob["packet_encoding"] = pe.Length > 0 ? pe : DefaultPacketEncoding;

            if (!TryBuildTransport(net, host, path, serviceName, out var transport)) return null;
            if (transport != null) ob["transport"] = transport;

            var tls = BuildTls(p, security, false, out bool tlsOk);
            if (!tlsOk) return null;
            if (tls != null) ob["tls"] = tls;

            label = FirstNonEmpty(p.Fragment, $"vless · {p.Host}:{port}");
            return ob;
        }

        // ── vmess ───────────────────────────────────────────────────────────────────────────

        private static JObject ParseVmess(string link, out string label)
        {
            label = "";

            string body = link.Substring("vmess://".Length).Trim();
            string fragment = "";
            int hash = body.IndexOf('#');
            if (hash >= 0)
            {
                fragment = SafeDecode(body.Substring(hash + 1));
                body = body.Substring(0, hash);
            }

            string json = DecodeBase64(body);
            if (json.Length == 0) return null;

            var j = JObject.Parse(json);

            string server = j["add"]?.ToString() ?? "";
            string uuid   = j["id"]?.ToString() ?? "";
            int    port   = 0;
            int.TryParse(j["port"]?.ToString(), out port);
            if (server.Length == 0 || uuid.Length == 0 || port <= 0 || port > 65535) return null;

            var ob = new JObject
            {
                ["type"]        = "vmess",
                ["server"]      = server,
                ["server_port"] = port,
                ["uuid"]        = uuid
            };

            if (int.TryParse(j["aid"]?.ToString(), out int aid) && aid != 0) ob["alter_id"] = aid;

            string scy = j["scy"]?.ToString() ?? "";
            if (scy.Length > 0 && !scy.Equals("auto", StringComparison.OrdinalIgnoreCase)) ob["security"] = scy;

            string pe = FirstNonEmpty(j["packetEncoding"]?.ToString(), j["packet_encoding"]?.ToString());
            ob["packet_encoding"] = pe.Length > 0 ? pe : DefaultPacketEncoding;

            var p = new Parts { Host = server, Port = port };

            void SetQuery(string key, string value)
            {
                if (!string.IsNullOrWhiteSpace(value)) p.Query[key] = value.Trim();
            }

            SetQuery("sni", j["sni"]?.ToString() ?? "");
            SetQuery("fp", j["fp"]?.ToString() ?? "");
            SetQuery("alpn", j["alpn"]?.ToString() ?? "");
            SetQuery("insecure", j["allowInsecure"]?.ToString() ?? "");
            SetQuery("allowInsecure", j["allowInsecure"]?.ToString() ?? "");

            string net   = j["net"]?.ToString() ?? "";
            string host  = j["host"]?.ToString() ?? "";
            string vpath = j["path"]?.ToString() ?? "";
            bool   grpc  = net.Equals("grpc", StringComparison.OrdinalIgnoreCase);

            if (!TryBuildTransport(net, host, grpc ? "" : vpath, grpc ? vpath : "", out var transport)) return null;
            if (transport != null) ob["transport"] = transport;

            string tlsMode = FirstNonEmpty(j["tls"]?.ToString(), j["security"]?.ToString());
            var tls = BuildTls(p, tlsMode, false, out bool tlsOk);
            if (!tlsOk) return null;
            if (tls != null) ob["tls"] = tls;

            label = FirstNonEmpty(j["ps"]?.ToString(), fragment, $"vmess · {server}:{port}");
            return ob;
        }

        private static int NormalizePort(int port, int fallback)
            => port > 0 && port <= 65535 ? port : fallback;

        // ── trojan ──────────────────────────────────────────────────────────────────────────

        private static JObject ParseTrojan(string link, out string label)
        {
            label = "";
            var p = SplitUri(link);
            if (p == null) return null;

            string password = SafeDecode(p.UserInfo);
            if (password.Length == 0) return null;

            int port = NormalizePort(p.Port, 443);

            var ob = new JObject
            {
                ["type"]        = "trojan",
                ["server"]      = p.Host,
                ["server_port"] = port,
                ["password"]    = password
            };

            string net = p.Get("type");
            string host = p.Get("host");
            string path = p.Get("path");
            string serviceName = FirstNonEmpty(p.Get("serviceName"), p.Get("servicename"));
            if (!TryBuildTransport(net, host, path, serviceName, out var transport)) return null;
            if (transport != null) ob["transport"] = transport;

            string security = FirstNonEmpty(p.Get("security"), "tls");
            var tls = BuildTls(p, security, true, out bool tlsOk);
            if (!tlsOk) return null;
            if (tls != null) ob["tls"] = tls;

            label = FirstNonEmpty(p.Fragment, $"trojan · {p.Host}:{port}");
            return ob;
        }

        // ── shadowsocks ─────────────────────────────────────────────────────────────────────

        private static JObject ParseShadowsocks(string link, out string label)
        {
            label = "";
            string rest = link.Substring("ss://".Length);

            string fragment = "";
            int hash = rest.IndexOf('#');
            if (hash >= 0)
            {
                fragment = SafeDecode(rest.Substring(hash + 1));
                rest = rest.Substring(0, hash);
            }

            var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int q = rest.IndexOf('?');
            if (q >= 0)
            {
                ParseQuery(rest.Substring(q + 1), query);
                rest = rest.Substring(0, q);
            }

            string method = "", password = "", host = "";
            int port = 0;

            int at = rest.LastIndexOf('@');
            if (at >= 0)
            {
                string user = rest.Substring(0, at);
                string hostPort = rest.Substring(at + 1);

                string creds = user.Contains(":") ? SafeDecode(user) : DecodeBase64(user);
                if (creds.Length == 0) creds = SafeDecode(user);
                int colon = creds.IndexOf(':');
                if (colon <= 0) return null;
                method = creds.Substring(0, colon).Trim();
                password = creds.Substring(colon + 1);

                if (!TrySplitHostPort(hostPort, out host, out port)) return null;
            }
            else
            {
                string decoded = DecodeBase64(rest);
                if (decoded.Length == 0) decoded = SafeDecode(rest);
                int at2 = decoded.LastIndexOf('@');
                if (at2 <= 0) return null;

                string creds = decoded.Substring(0, at2);
                int colon = creds.IndexOf(':');
                if (colon <= 0) return null;
                method = creds.Substring(0, colon).Trim();
                password = creds.Substring(colon + 1);

                if (!TrySplitHostPort(decoded.Substring(at2 + 1), out host, out port)) return null;
            }

            if (method.Length == 0 || host.Length == 0) return null;

            var ob = new JObject
            {
                ["type"]        = "shadowsocks",
                ["server"]      = host,
                ["server_port"] = NormalizePort(port, 8388),
                ["method"]      = method,
                ["password"]    = password
            };

            if (query.TryGetValue("plugin", out var pluginRaw) && !string.IsNullOrWhiteSpace(pluginRaw))
            {
                var plugin = MapShadowsocksPlugin(pluginRaw.Trim());
                if (plugin == null) return null;
                ob["plugin"] = plugin.Value.Name;
                if (plugin.Value.Opts.Length > 0) ob["plugin_opts"] = plugin.Value.Opts;
            }

            label = FirstNonEmpty(fragment, $"ss · {host}:{ob["server_port"]}");
            return ob;
        }

        private static (string Name, string Opts)? MapShadowsocksPlugin(string plugin)
        {
            int semi = plugin.IndexOf(';');
            string name = (semi < 0 ? plugin : plugin.Substring(0, semi)).Trim().ToLowerInvariant();
            string opts = semi < 0 ? "" : plugin.Substring(semi + 1).Trim();

            switch (name)
            {
                case "obfs-local":
                case "simple-obfs":
                case "obfs":
                    return ("obfs-local", opts);
                case "v2ray-plugin":
                    return ("v2ray-plugin", opts);
                default:
                    return null;
            }
        }

        private static bool TrySplitHostPort(string value, out string host, out int port)
        {
            host = "";
            port = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;

            value = value.Trim();
            if (value.StartsWith("["))
            {
                int close = value.IndexOf(']');
                if (close < 0) return false;
                host = value.Substring(1, close - 1);
                string tail = value.Substring(close + 1);
                if (tail.StartsWith(":")) int.TryParse(tail.Substring(1), out port);
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

            host = host.Trim();
            return host.Length > 0;
        }

        // ── socks / http ────────────────────────────────────────────────────────────────────

        private static JObject ParseSocks(string link, out string label)
        {
            label = "";
            var p = SplitUri(link);
            if (p == null) return null;

            int port = NormalizePort(p.Port, 1080);

            var ob = new JObject
            {
                ["type"]        = "socks",
                ["version"]     = "5",
                ["server"]      = p.Host,
                ["server_port"] = port
            };

            string userInfo = SafeDecode(p.UserInfo);
            int colon = userInfo.IndexOf(':');
            if (colon > 0)
            {
                ob["username"] = userInfo.Substring(0, colon);
                ob["password"] = userInfo.Substring(colon + 1);
            }
            else if (userInfo.Length > 0)
            {
                ob["username"] = userInfo;
            }

            string security = p.Get("security");
            if (security.Equals("tls", StringComparison.OrdinalIgnoreCase))
            {
                var tls = BuildTls(p, "tls", false, out bool tlsOk);
                if (!tlsOk) return null;
                if (tls != null) ob["tls"] = tls;
            }

            label = FirstNonEmpty(p.Fragment, $"socks · {p.Host}:{port}");
            return ob;
        }

        private static JObject ParseHttp(string link, out string label)
        {
            label = "";
            var p = SplitUri(link);
            if (p == null) return null;

            bool tlsEnabled = p.Scheme == "https";
            int port = NormalizePort(p.Port, tlsEnabled ? 443 : 80);

            var ob = new JObject
            {
                ["type"]        = "http",
                ["server"]      = p.Host,
                ["server_port"] = port
            };

            string userInfo = SafeDecode(p.UserInfo);
            int colon = userInfo.IndexOf(':');
            if (colon > 0)
            {
                ob["username"] = userInfo.Substring(0, colon);
                ob["password"] = userInfo.Substring(colon + 1);
            }
            else if (userInfo.Length > 0)
            {
                ob["username"] = userInfo;
            }

            string security = FirstNonEmpty(p.Get("security"), tlsEnabled ? "tls" : "");
            var tls = BuildTls(p, security, false, out bool tlsOk);
            if (!tlsOk) return null;
            if (tls != null) ob["tls"] = tls;

            label = FirstNonEmpty(p.Fragment, $"http · {p.Host}:{port}");
            return ob;
        }

        // ── hysteria2 ───────────────────────────────────────────────────────────────────────

        private static JObject ParseHysteria2(string link, out string label)
        {
            label = "";
            var p = SplitUri(link);
            if (p == null) return null;

            string auth = SafeDecode(p.UserInfo);
            int colon = auth.IndexOf(':');
            string password = colon > 0 ? auth.Substring(colon + 1) : auth;   // "user:pass" links use the pass part
            if (password.Length == 0) return null;

            int port = NormalizePort(p.Port, 443);

            var ob = new JObject
            {
                ["type"]        = "hysteria2",
                ["server"]      = p.Host,
                ["server_port"] = port,
                ["password"]    = password
            };

            string obfs = p.Get("obfs");
            if (obfs.Length > 0)
            {
                if (!obfs.Equals("salamander", StringComparison.OrdinalIgnoreCase)) return null;
                var obfsNode = new JObject { ["type"] = "salamander" };
                string obfsPass = FirstNonEmpty(p.Get("obfs-password"), p.Get("obfs_password"), p.Get("obfsPassword"));
                if (obfsPass.Length > 0) obfsNode["password"] = obfsPass;
                ob["obfs"] = obfsNode;
            }

            ob["tls"] = BuildQuicTls(p, "h3");
            label = FirstNonEmpty(p.Fragment, $"hysteria2 · {p.Host}:{port}");
            return ob;
        }

        // ── tuic ────────────────────────────────────────────────────────────────────────────

        private static JObject ParseTuic(string link, out string label)
        {
            label = "";
            var p = SplitUri(link);
            if (p == null) return null;

            string userInfo = SafeDecode(p.UserInfo);
            int colon = userInfo.IndexOf(':');
            if (colon <= 0) return null;

            string uuid     = userInfo.Substring(0, colon);
            string password = userInfo.Substring(colon + 1);
            if (uuid.Length == 0 || password.Length == 0) return null;

            int port = NormalizePort(p.Port, 443);

            var ob = new JObject
            {
                ["type"]        = "tuic",
                ["server"]      = p.Host,
                ["server_port"] = port,
                ["uuid"]        = uuid,
                ["password"]    = password
            };

            string congestion = FirstNonEmpty(p.Get("congestion_control"), p.Get("congestionControl"));
            if (congestion.Length > 0) ob["congestion_control"] = congestion.ToLowerInvariant();

            string relayMode = FirstNonEmpty(p.Get("udp_relay_mode"), p.Get("udpRelayMode"));
            if (relayMode.Length > 0) ob["udp_relay_mode"] = relayMode.ToLowerInvariant();

            ob["tls"] = BuildQuicTls(p, "h3");
            label = FirstNonEmpty(p.Fragment, $"tuic · {p.Host}:{port}");
            return ob;
        }

        private static JObject BuildQuicTls(Parts p, string defaultAlpn)
        {
            var tls = new JObject { ["enabled"] = true };

            string sni = FirstNonEmpty(p.Get("sni"), p.Get("peer"));
            if (sni.Length > 0) tls["server_name"] = sni;

            if (p.Flag("insecure") || p.Get("allow_insecure") == "1" || p.Get("allowInsecure") == "1")
                tls["insecure"] = true;

            var alpn = SplitList(p.Get("alpn"));
            tls["alpn"] = new JArray(alpn.Length > 0 ? alpn : new[] { defaultAlpn });

            return tls;
        }

        // ── anytls ──────────────────────────────────────────────────────────────────────────

        private static JObject ParseAnyTls(string link, out string label)
        {
            label = "";
            var p = SplitUri(link);
            if (p == null) return null;

            string userInfo = SafeDecode(p.UserInfo);
            int colon = userInfo.IndexOf(':');
            string password = colon > 0 ? userInfo.Substring(colon + 1) : userInfo;
            if (password.Length == 0) return null;

            int port = NormalizePort(p.Port, 443);

            var ob = new JObject
            {
                ["type"]        = "anytls",
                ["server"]      = p.Host,
                ["server_port"] = port,
                ["password"]    = password
            };

            var tls = BuildQuicTls(p, "h2");
            if (SplitList(p.Get("alpn")).Length == 0) tls.Remove("alpn");
            ob["tls"] = tls;

            label = FirstNonEmpty(p.Fragment, $"anytls · {p.Host}:{port}");
            return ob;
        }

        // ── raw sing-box outbound JSON ──────────────────────────────────────────────────────

        private static JObject ParseRawJson(string text, out string label)
        {
            label = "";

            var ob = JObject.Parse(text);
            ob.Remove("tag");

            if (ob["outbounds"] is JArray arr)
            {
                if (arr.Count != 1 || arr[0] is not JObject only) return null;

                only.Remove("tag");
                ob = only;
            }

            string type = ob["type"]?.ToString() ?? "";
            if (type.Length == 0) return null;

            string server = ob["server"]?.ToString() ?? "";
            string port   = ob["server_port"]?.ToString() ?? "";
            if (server.Length > 0 && port.Length == 0) return null;

            foreach (var key in WrapperKeys) ob.Remove(key);

            label = FirstNonEmpty(ob["label"]?.ToString(), DescribeOutbound(ob));
            ob.Remove("label");
            return ob;
        }
        private static readonly string[] WrapperKeys =
        {
            "outbounds", "inbounds", "endpoints", "route", "dns", "log", "services",
            "experimental", "certificate", "ntp", "script"
        };

    }
}

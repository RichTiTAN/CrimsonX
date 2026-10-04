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
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public enum ConfigTarget
    {
        Pool,

        Xray,

        Singbox
    }

    public static class ConfigConverter
    {
        public static bool CanConvert(string raw) => TryConvert(raw, out _, out _, out _);

        public static bool TryConvert(string raw, out string xrayJson, out string label, out string error)
        {
            xrayJson = "";
            label    = "";
            error    = "";
            var sb = ExtractOutbound(raw, out label, out error);
            if (sb == null) return false;
            var xray = ConvertOutbound(sb, out error);
            if (xray == null) return false;
            xrayJson = new JObject { ["outbounds"] = new JArray { xray } }.ToString();
            return true;
        }

        public static bool IsXrayOutbound(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string text = raw.Trim();
            if (!text.StartsWith("{")) return false;
            try
            {
                var root = JObject.Parse(text);
                if (root["outbounds"] is JArray arr)
                    return arr.OfType<JObject>().Any(o => o["protocol"] != null);
                return root["protocol"] != null;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryConvertToSingbox(string raw, out string singboxJson, out string label, out string error)
        {
            singboxJson = "";
            label = "";
            error = "";
            if (!IsXrayOutbound(raw))
            {
                error = "this JSON is not an xray config, so there is nothing to convert for sing-box";
                return false;
            }
            if (!XrayLinkParser.TryParseCustomConfig(raw, out string xrayDoc))
            {
                error = "this xray config could not be read";
                return false;
            }
            JObject outbound;
            try
            {
                var root = JObject.Parse(xrayDoc);
                outbound = (root["outbounds"] as JArray)?.OfType<JObject>().FirstOrDefault(o => IsDialableProtocol(o["protocol"]?.ToString() ?? ""));
            }
            catch
            {
                error = "invalid JSON";
                return false;
            }
            if (outbound == null)
            {
                error = "this xray config holds no outbound";
                return false;
            }
            var converted = ConvertToSingbox(outbound, out error);
            if (converted == null) return false;
            singboxJson = converted.ToString(Newtonsoft.Json.Formatting.None);
            label = SingboxLinkParser.DescribeOutbound(converted);
            return true;
        }

        private static JObject ConvertToSingbox(JObject xray, out string error)
        {
            error = "";
            string protocol = xray["protocol"]?.ToString()?.ToLowerInvariant() ?? "";
            var settings = xray["settings"] as JObject;
            if (protocol.Length == 0) { error = "missing the xray 'protocol' field"; return null; }
            if (protocol is "wireguard" or "openvpn" or "openvpn-client")
            {
                error = $"xray '{protocol}' is dialed by the sing-box tunnel engine; it is not converted here";
                return null;
            }
            var sb = new JObject();
            switch (protocol)
            {
                case "vless":
                case "vmess":
                {
                    var node = (settings?["vnext"] as JArray)?.OfType<JObject>().FirstOrDefault();
                    if (!ServerPort(node, sb, out string serverError)) { error = serverError; return null; }
                    var user = (node?["users"] as JArray)?.OfType<JObject>().FirstOrDefault();
                    string id = user?["id"]?.ToString() ?? "";
                    if (id.Length == 0) { error = $"{protocol} outbound has no user id"; return null; }
                    sb["type"] = protocol;
                    sb["uuid"] = id;
                    if (protocol == "vless")
                    {
                        string flow = user["flow"]?.ToString() ?? "";
                        if (flow.Length > 0) sb["flow"] = flow;
                    }
                    else
                    {
                        sb["alter_id"] = IntOf(user, "alterId");
                        sb["security"] = NonEmpty(user["security"]) ?? "auto";
                    }
                    break;
                }
                case "trojan":
                case "shadowsocks":
                case "socks":
                case "http":
                {
                    var node = (settings?["servers"] as JArray)?.OfType<JObject>().FirstOrDefault();
                    if (!ServerPort(node, sb, out string serverError)) { error = serverError; return null; }
                    sb["type"] = protocol;
                    if (protocol == "trojan")
                    {
                        string password = node["password"]?.ToString() ?? "";
                        if (password.Length == 0) { error = "trojan outbound has no password"; return null; }
                        sb["password"] = password;
                    }
                    else if (protocol == "shadowsocks")
                    {
                        string method   = node["method"]?.ToString() ?? "";
                        string password = node["password"]?.ToString() ?? "";
                        if (method.Length == 0 || password.Length == 0) { error = "shadowsocks outbound has no method or password"; return null; }
                        sb["method"]   = method;
                        sb["password"] = password;
                    }
                    else
                    {
                        if (protocol == "socks") sb["version"] = "5";
                        var user = (node["users"] as JArray)?.OfType<JObject>().FirstOrDefault();
                        string username = FirstNonEmpty(user?["user"]?.ToString() ?? "", user?["username"]?.ToString() ?? "");
                        string password = FirstNonEmpty(user?["pass"]?.ToString() ?? "", user?["password"]?.ToString() ?? "");
                        if (username.Length > 0) sb["username"] = username;
                        if (password.Length > 0) sb["password"] = password;
                    }
                    break;
                }
                case "hysteria":
                {
                    if (IntOf(settings, "version") != 2)
                    {
                        error = "only hysteria v2 (version 2) can be expressed as a sing-box hysteria2 outbound";
                        return null;
                    }
                    string address = settings?["address"]?.ToString() ?? "";
                    int    port    = IntOf(settings, "port");
                    string auth    = settings?["auth"]?.ToString() ?? "";
                    if (address.Length == 0 || port <= 0) { error = "hysteria outbound has no usable server address"; return null; }
                    if (auth.Length == 0) { error = "hysteria outbound has no auth"; return null; }
                    sb["type"]        = "hysteria2";
                    sb["server"]      = address;
                    sb["server_port"] = port;
                    sb["password"]    = auth;
                    if (settings?["obfs"] is JObject obfs && (obfs["type"]?.ToString() ?? "") == "salamander")
                    {
                        string obfsPassword = obfs["password"]?.ToString() ?? "";
                        if (obfsPassword.Length > 0)
                            sb["obfs"] = new JObject { ["type"] = "salamander", ["password"] = obfsPassword };
                    }
                    if (settings?["udpHop"] is JObject hop)
                    {
                        var ports = StrArray(hop["ports"]).SelectMany(p => p.Split(','))
                                                          .Select(p => p.Trim().Replace('-', ':'))
                                                          .Where(p => p.Length > 0)
                                                          .ToList();
                        if (ports.Count > 0) sb["server_ports"] = new JArray(ports);
                        int interval = IntOf(hop, "interval");
                        if (interval > 0) sb["hop_interval"] = interval;
                    }
                    break;
                }
                default:
                    error = $"xray '{protocol}' has no sing-box counterpart";
                    return null;
            }
            var stream = xray["streamSettings"] as JObject;
            if (!BuildSingboxTls(stream, sb, out string tlsError)) { error = tlsError; return null; }
            if (!BuildSingboxTransport(stream, sb, out string transportError)) { error = transportError; return null; }
            if (protocol == "hysteria" && sb["tls"] == null)
                sb["tls"] = new JObject { ["enabled"] = true };
            BuildSingboxMux(xray["mux"] as JObject, sb);
            return sb;
        }

        private static bool ServerPort(JObject node, JObject sb, out string error)
        {
            error = "";
            string address = node?["address"]?.ToString() ?? "";
            int    port    = IntOf(node, "port");
            if (address.Length == 0 || port <= 0)
            {
                error = "this outbound has no usable server address";
                return false;
            }
            sb["server"]      = address;
            sb["server_port"] = port;
            return true;
        }

        private static bool BuildSingboxTls(JObject stream, JObject sb, out string error)
        {
            error = "";
            string security = stream?["security"]?.ToString()?.ToLowerInvariant() ?? "";
            if (security.Length == 0 || security == "none") return true;
            if (security != "tls" && security != "reality" && security != "xtls")
            {
                error = $"xray '{security}' security has no sing-box counterpart";
                return false;
            }
            var tlsSettings = stream?[security + "Settings"] as JObject;
            var tls = new JObject { ["enabled"] = true };
            string sni = FirstNonEmpty(tlsSettings?["serverName"]?.ToString() ?? "", tlsSettings?["server_name"]?.ToString() ?? "");
            if (sni.Length > 0) tls["server_name"] = sni;
            if (BoolOf(tlsSettings, "insecure", false) || BoolOf(tlsSettings, "allowInsecure", false)) tls["insecure"] = true;
            var alpn = StrArray(tlsSettings?["alpn"]);
            if (alpn.Count > 0) tls["alpn"] = new JArray(alpn);
            string fingerprint = NonEmpty(tlsSettings?["fingerprint"]) ?? "";
            if (fingerprint.Length > 0)
                tls["utls"] = new JObject { ["enabled"] = true, ["fingerprint"] = fingerprint };
            if (security == "reality")
            {
                string publicKey = tlsSettings?["publicKey"]?.ToString() ?? "";
                if (publicKey.Length == 0)
                {
                    error = "the reality block has no publicKey";
                    return false;
                }
                tls["reality"] = new JObject
                {
                    ["enabled"]    = true,
                    ["public_key"] = publicKey,
                    ["short_id"]   = tlsSettings?["shortId"]?.ToString() ?? ""
                };
            }
            if (FinalMask.HasTcpFragment(FinalMask.FromStream(stream)))
            {
                tls["fragment"] = true;
                SimpleLogger.LogOnce(
                    $"mask-converter|{sb["server"]}",
                    "[Converter] The xray final mask was approximated as plain TLS fragmentation (sing-box has no packet rules).");
            }
            sb["tls"] = tls;
            return true;
        }

        private static bool BuildSingboxTransport(JObject stream, JObject sb, out string error)
        {
            error = "";
            string network = stream?["network"]?.ToString()?.ToLowerInvariant() ?? "";
            switch (network)
            {
                case "":
                case "tcp":
                case "raw":
                    return true;
                case "ws":
                {
                    var ws = stream["wsSettings"] as JObject;
                    var transport = new JObject
                    {
                        ["type"] = "ws",
                        ["path"] = NonEmpty(ws?["path"]) ?? "/"
                    };
                    string host = FirstNonEmpty(ws?["host"]?.ToString() ?? "", ws?["headers"]?["Host"]?.ToString() ?? "");
                    if (host.Length > 0) transport["headers"] = new JObject { ["Host"] = host };
                    sb["transport"] = transport;
                    return true;
                }
                case "grpc":
                {
                    var grpc = stream["grpcSettings"] as JObject;
                    sb["transport"] = new JObject
                    {
                        ["type"]         = "grpc",
                        ["service_name"] = grpc?["serviceName"]?.ToString() ?? ""
                    };
                    return true;
                }
                case "http":
                case "h2":
                {
                    var h2 = stream["httpSettings"] as JObject;
                    var transport = new JObject
                    {
                        ["type"] = "http",
                        ["path"] = NonEmpty(h2?["path"]) ?? "/"
                    };
                    var hosts = StrArray(h2?["host"]);
                    if (hosts.Count > 0) transport["host"] = new JArray(hosts);
                    sb["transport"] = transport;
                    return true;
                }
                case "httpupgrade":
                case "xhttp":
                {
                    var source = stream[network + "Settings"] as JObject;
                    var transport = new JObject
                    {
                        ["type"] = network,
                        ["path"] = NonEmpty(source?["path"]) ?? "/"
                    };
                    string host = NonEmpty(source?["host"]) ?? "";
                    if (host.Length > 0) transport["host"] = host;
                    string mode = NonEmpty(source?["mode"]) ?? "";
                    if (mode.Length > 0) transport["mode"] = mode;
                    sb["transport"] = transport;
                    return true;
                }
                default:
                    error = $"xray '{network}' transport has no sing-box counterpart";
                    return false;
            }
        }

        private static void BuildSingboxMux(JObject mux, JObject sb)
        {
            if (mux == null || !BoolOf(mux, "enabled", false)) return;
            sb["multiplex"] = new JObject
            {
                ["enabled"]     = true,
                ["protocol"]    = NonEmpty(mux["protocol"]) ?? "h2mux",
                ["max_streams"] = IntOf(mux, "maxStreams", 8),
                ["padding"]     = BoolOf(mux, "padding", false)
            };
        }

        public static bool TryXrayOutbound(string raw, out string xrayDoc, out string label, out string error)
        {
            xrayDoc = "";
            label = "";
            error = "";
            string text = (raw ?? "").Trim();
            if (text.Length == 0)
            {
                error = "empty config";
                return false;
            }
            if (TryTunnel(text, out _, out _))
            {
                error = "OpenVPN / WireGuard configs are chained by sing-box, xray cannot dial them";
                return false;
            }
            JObject single = null;
            bool isJson = text.StartsWith("{", StringComparison.Ordinal);
            if (!isJson
                && XrayLinkParser.TryParseCustomConfig(text, out string linkDoc)
                && linkDoc.Contains("\"protocol\"", StringComparison.Ordinal)
                && PickXrayOutbound(linkDoc, out single, out _))
            {
                if (SingboxLinkParser.TryParseLink(text, out _, out string linkLabel) && linkLabel.Length > 0)
                    label = linkLabel;
            }
            else if (IsXrayOutbound(text))
            {
                if (!PickXrayOutbound(text, out single, out error)) return false;
            }
            else if (TryConvert(text, out string converted, out string convertedLabel, out string convertError))
            {
                if (!PickXrayOutbound(converted, out single, out error))
                {
                    error = error.Length > 0 ? error : convertError;
                    return false;
                }
                label = convertedLabel;
            }
            else
            {
                error = convertError.Length > 0 ? convertError : "this config cannot be read";
                return false;
            }
            xrayDoc = new JObject { ["outbounds"] = new JArray { single } }.ToString(Newtonsoft.Json.Formatting.None);
            if (label.Length == 0)
            {
                string protocol = single["protocol"]?.ToString() ?? "";
                string server   = XrayLinkParser.ExtractServerAddress(xrayDoc);
                label = server.Length > 0 ? $"{protocol} · {server}" : protocol;
            }
            return label.Length > 0;
        }

        public static bool TrySingboxOutbound(string raw, out string singboxJson, out string label, out string error)
        {
            singboxJson = "";
            label = "";
            error = "";
            string text = (raw ?? "").Trim();
            if (text.Length == 0)
            {
                error = "empty config";
                return false;
            }
            if (TryTunnel(text, out _, out _))
            {
                error = "OpenVPN / WireGuard configs run through the tunnel engine, not as a plain outbound";
                return false;
            }
            if (SingboxLinkParser.TryParseLink(text, out string fromLink, out string linkLabel))
            {
                singboxJson = fromLink;
                label = linkLabel;
                return true;
            }
            if (TryConvertToSingbox(text, out string converted, out string convertedLabel, out string convertError))
            {
                singboxJson = converted;
                label = convertedLabel;
                return true;
            }
            error = text.StartsWith("{", StringComparison.Ordinal) && convertError.Length > 0
                ? convertError
                : LinkRefusalNote(text);
            SimpleLogger.Log($"[Converter] Refused as a sing-box outbound: {error}");
            return false;
        }

        private static string LinkRefusalNote(string text)
        {
            int colon = text.IndexOf("://", StringComparison.Ordinal);
            string scheme = colon > 0 ? text.Substring(0, colon) : "";
            string kind   = scheme.Length > 0 ? scheme + " share link" : "config";
            if (colon > 0 && TryXrayOutbound(text, out _, out _, out _))
                return $"this {kind} has no sing-box form (the xray panes would take it, an Apps & Games proxy cannot)";
            return $"this {kind} cannot be read (its scheme or parameters are not supported)";
        }

        public static bool TryTunnel(string raw, out TunnelParseResult tunnel, out string error)
        {
            tunnel = null;
            error = "";
            if (!TunnelConfigParser.TryParse(raw ?? "", out var parsed) || parsed?.Endpoint == null)
            {
                error = parsed?.Error is { Length: > 0 } reason ? reason : "this is not an OpenVPN / WireGuard config";
                return false;
            }
            tunnel = parsed;
            return true;
        }

        public static bool TryAcceptFor(string raw, ConfigTarget target, out string error)
        {
            error = "";
            string text = (raw ?? "").Trim();
            if (text.Length == 0)
            {
                error = "empty config";
                return false;
            }
            if (TryTunnel(text, out _, out _)) return true;
            return target switch
            {
                ConfigTarget.Xray => TryXrayOutbound(text, out _, out _, out error),
                ConfigTarget.Singbox => TrySingboxOutbound(text, out _, out _, out error),
                _ => TryAcceptAnywhere(text, out error),
            };
        }

        private static bool TryAcceptAnywhere(string text, out string error)
        {
            error = "";
            if (TryXrayOutbound(text, out _, out _, out string xrayError)) return true;
            if (TrySingboxOutbound(text, out _, out _, out _)) return true;
            error = xrayError;
            return false;
        }

        public static string LabelFor(string raw)
        {
            string text = (raw ?? "").Trim();
            if (text.Length == 0) return "";
            if (TryTunnel(text, out var tunnel, out _)) return tunnel.Label ?? "";
            if (TryXrayOutbound(text, out _, out string xrayLabel, out _) && xrayLabel.Length > 0) return xrayLabel;
            if (TrySingboxOutbound(text, out _, out string singboxLabel, out _) && singboxLabel.Length > 0) return singboxLabel;
            return text.Length <= 40 ? text : text.Substring(0, 40) + "…";
        }

        public static string KeyFor(string raw, string adapter)
        {
            string text = (raw ?? "").Trim();
            string adapterName = string.IsNullOrWhiteSpace(adapter) ? "Default" : adapter.Trim();
            string suffix = "|" + adapterName;
            if (text.Length == 0) return suffix;
            if (TunnelConfigParser.LooksLikeTunnel(text)) return TunnelConfigParser.KeyOf(text, adapterName);
            if (TryXrayOutbound(text, out string xrayDoc, out _, out _)) return "xray|" + Compact(xrayDoc) + suffix;
            if (TrySingboxOutbound(text, out string singboxJson, out _, out _)) return "singbox|" + Compact(singboxJson) + suffix;
            return "raw|" + Compact(text) + suffix;
        }

        private static bool PickXrayOutbound(string document, out JObject outbound, out string error)
        {
            outbound = null;
            error = "";
            JObject root;
            try { root = JObject.Parse(document); }
            catch { error = "invalid JSON"; return false; }
            var outbounds = root["outbounds"] as JArray;
            var candidate = outbounds?.OfType<JObject>().FirstOrDefault(o => IsDialableProtocol(o["protocol"]?.ToString() ?? ""));
            if (candidate == null && root["protocol"] != null && IsDialableProtocol(root["protocol"]!.ToString()))
                candidate = root;
            if (candidate == null)
            {
                error = "this config holds no xray outbound";
                return false;
            }
            outbound = (JObject)candidate.DeepClone();
            return true;
        }

        private static bool IsDialableProtocol(string protocol)
        {
            return (protocol ?? "").Trim().ToLowerInvariant() switch
            {
                "" => false,
                "freedom" => false,
                "blackhole" => false,
                "dns" => false,
                "loopback" => false,
                "dokodemo-door" => false,
                _ => true
            };
        }

        private static string Compact(string text)
            => new string((text ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());

        internal static string TypeOf(JObject o) => o?["type"]?.ToString() ?? "";

        private static bool IsProxyType(string type)
        {
            switch (type)
            {
                case "vless":
                case "vmess":
                case "trojan":
                case "shadowsocks":
                case "socks":
                case "http":
                case "hysteria2":
                case "wireguard":
                    return true;
                default:
                    return false;
            }
        }

        private static JObject? ExtractOutbound(string raw, out string label, out string error)
        {
            label = "";
            error = "";
            string text = (raw ?? "").Trim();
            if (text.Length == 0)
            {
                error = "empty config";
                return null;
            }
            JObject candidate;
            if (!text.StartsWith("{"))
            {
                if (TunnelConfigParser.TryParse(text, out var tunnel) && tunnel?.Endpoint != null)
                {
                    if (TypeOf(tunnel.Endpoint) != "wireguard")
                    {
                        error = "OpenVPN is dialed by sing-box; xray has no OpenVPN outbound";
                        return null;
                    }
                    label = tunnel.Label ?? "";
                    candidate = (JObject)tunnel.Endpoint.DeepClone();
                }
                else
                {
                    if (!SingboxLinkParser.TryParseLink(text, out string fromLink, out label))
                    {
                        error = "this is not a sing-box share link or outbound JSON";
                        return null;
                    }
                    try { candidate = JObject.Parse(fromLink); }
                    catch { error = "invalid JSON"; return null; }
                }
            }
            else
            {
                JObject root;
                try { root = JObject.Parse(text); }
                catch { error = "invalid JSON"; return null; }
                if (root["endpoints"] is JArray endpoints && endpoints.Count > 0)
                {
                    if (!TunnelConfigParser.TryParse(text, out var endpointTunnel) || endpointTunnel?.Endpoint == null)
                    {
                        error = "this sing-box endpoint list holds no usable endpoint";
                        return null;
                    }
                    if (TypeOf(endpointTunnel.Endpoint) != "wireguard")
                    {
                        error = "OpenVPN is dialed by sing-box; xray has no OpenVPN outbound";
                        return null;
                    }
                    label = endpointTunnel.Label ?? "";
                    candidate = (JObject)endpointTunnel.Endpoint.DeepClone();
                }
                else if (root["outbounds"] is JArray arr)
                {
                    var only = arr.OfType<JObject>().FirstOrDefault(o => IsProxyType(TypeOf(o)));
                    if (only == null)
                    {
                        error = "this sing-box config holds no outbound that xray can speak";
                        return null;
                    }
                    candidate = (JObject)only.DeepClone();
                }
                else
                {
                    candidate = root;
                }
            }
            if (TypeOf(candidate).Length == 0)
            {
                error = "missing the sing-box 'type' field";
                return null;
            }
            if (!IsProxyType(TypeOf(candidate)))
            {
                error = $"sing-box '{TypeOf(candidate)}' has no xray equivalent";
                return null;
            }
            StripDialFields(candidate);
            if (label.Length == 0) label = SingboxLinkParser.DescribeOutbound(candidate);
            return candidate;
        }

        private static JObject? ConvertOutbound(JObject sb, out string error)
        {
            error = "";
            string type   = TypeOf(sb);
            string server = sb["server"]?.ToString() ?? "";
            int    port   = IntOf(sb, "server_port");
            if (type != "wireguard" && (server.Length == 0 || port <= 0))
            {
                error = "this outbound has no usable server address";
                return null;
            }
            var xray = new JObject();
            var settings = new JObject();
            switch (type)
            {
                case "vless":
                {
                    string uuid = sb["uuid"]?.ToString() ?? "";
                    if (uuid.Length == 0) { error = "vless outbound has no uuid"; return null; }
                    var user = new JObject { ["id"] = uuid, ["encryption"] = "none" };
                    string flow = sb["flow"]?.ToString() ?? "";
                    if (flow.Length > 0) user["flow"] = flow;
                    settings["vnext"] = new JArray
                    {
                        new JObject
                        {
                            ["address"] = server,
                            ["port"]    = port,
                            ["users"]   = new JArray { user }
                        }
                    };
                    break;
                }
                case "vmess":
                {
                    string uuid = sb["uuid"]?.ToString() ?? "";
                    if (uuid.Length == 0) { error = "vmess outbound has no uuid"; return null; }
                    settings["vnext"] = new JArray
                    {
                        new JObject
                        {
                            ["address"] = server,
                            ["port"]    = port,
                            ["users"]   = new JArray
                            {
                                new JObject
                                {
                                    ["id"]       = uuid,
                                    ["alterId"]  = IntOf(sb, "alter_id"),
                                    ["security"] = NonEmpty(sb["security"]) ?? "auto",
                                    ["level"]    = 0
                                }
                            }
                        }
                    };
                    break;
                }
                case "trojan":
                {
                    string password = sb["password"]?.ToString() ?? "";
                    if (password.Length == 0) { error = "trojan outbound has no password"; return null; }
                    settings["servers"] = new JArray
                    {
                        new JObject { ["address"] = server, ["port"] = port, ["password"] = password }
                    };
                    break;
                }
                case "shadowsocks":
                {
                    string method   = sb["method"]?.ToString() ?? "";
                    string password = sb["password"]?.ToString() ?? "";
                    if (method.Length == 0 || password.Length == 0)
                    {
                        error = "shadowsocks outbound has no method / password";
                        return null;
                    }
                    var serverObj = new JObject
                    {
                        ["address"]  = server,
                        ["port"]     = port,
                        ["method"]   = method,
                        ["password"] = password
                    };
                    string plugin = sb["plugin"]?.ToString() ?? "";
                    if (plugin.Length > 0)
                    {
                        serverObj["plugin"] = plugin;
                        string opts = sb["plugin_opts"]?.ToString() ?? "";
                        if (opts.Length > 0) serverObj["plugin_opts"] = opts;
                    }
                    settings["servers"] = new JArray { serverObj };
                    break;
                }
                case "socks":
                case "http":
                {
                    var serverObj = new JObject { ["address"] = server, ["port"] = port };
                    string user = sb["username"]?.ToString() ?? "";
                    string pass = sb["password"]?.ToString() ?? "";
                    if (user.Length > 0 || pass.Length > 0)
                    {
                        serverObj["users"] = new JArray
                        {
                            new JObject { ["user"] = user, ["pass"] = pass, ["level"] = 0 }
                        };
                    }
                    settings["servers"] = new JArray { serverObj };
                    break;
                }
                case "hysteria2":
                {
                    string password = sb["password"]?.ToString() ?? "";
                    if (password.Length == 0)
                    {
                        error = "hysteria2 outbound has no password";
                        return null;
                    }
                    settings["address"] = server;
                    settings["port"]    = port;
                    settings["auth"]    = password;
                    settings["version"] = 2;
                    if (sb["obfs"] is JObject obfs)
                    {
                        string obfsType     = NonEmpty(obfs["type"]) ?? "";
                        string obfsPassword = NonEmpty(obfs["password"]) ?? "";
                        if (obfsType == "salamander" && obfsPassword.Length > 0)
                        {
                            settings["obfs"] = new JObject { ["type"] = "salamander", ["password"] = obfsPassword };
                        }
                        else if (obfsType.Length > 0)
                        {
                            SimpleLogger.Log($"[Converter] hysteria2 obfs '{obfsType}' has no xray equivalent; it was dropped.");
                        }
                    }
                    var hopPorts = StrArray(sb["server_ports"]);
                    if (hopPorts.Count > 0)
                    {
                        var hop = new JObject
                        {
                            ["ports"] = string.Join(",", hopPorts.Select(p => p.Replace(':', '-')))
                        };
                        int interval = IntOf(sb, "hop_interval");
                        if (interval > 0) hop["interval"] = interval;
                        settings["udpHop"] = hop;
                    }
                    if (IntOf(sb, "up_mbps") > 0 || IntOf(sb, "down_mbps") > 0)
                        SimpleLogger.Log("[Converter] hysteria2 up_mbps / down_mbps have no xray equivalent; they were dropped.");
                    break;
                }
                case "wireguard":
                {
                    string privateKey = sb["private_key"]?.ToString() ?? "";
                    if (privateKey.Length == 0)
                    {
                        error = "wireguard endpoint has no private_key";
                        return null;
                    }
                    settings["secretKey"] = privateKey;
                    var addresses = StrArray(sb["address"]);
                    if (addresses.Count > 0) settings["address"] = new JArray(addresses);
                    int wgMtu = IntOf(sb, "mtu");
                    if (wgMtu > 0) settings["mtu"] = wgMtu;
                    int listenPort = IntOf(sb, "listen_port");
                    if (listenPort > 0) settings["listenPort"] = listenPort;
                    int workers = IntOf(sb, "workers");
                    if (workers > 0) settings["workers"] = workers;
                    if (sb["peers"] is not JArray sourcePeers || sourcePeers.Count == 0)
                    {
                        error = "wireguard endpoint has no peer";
                        return null;
                    }
                    var peers = new JArray();
                    foreach (var item in sourcePeers.OfType<JObject>())
                    {
                        string publicKey = item["public_key"]?.ToString() ?? "";
                        if (publicKey.Length == 0) continue;
                        string host    = FirstNonEmpty(item["address"]?.ToString() ?? "", item["server"]?.ToString() ?? "");
                        int    peerPort = IntOf(item, "port", IntOf(item, "server_port"));
                        if (host.Length == 0 || peerPort <= 0) continue;
                        string endpoint = host.Contains(':') && !host.StartsWith("[")
                            ? $"[{host}]:{peerPort}"
                            : $"{host}:{peerPort}";
                        var peer = new JObject { ["endpoint"] = endpoint, ["publicKey"] = publicKey };
                        string preShared = FirstNonEmpty(item["pre_shared_key"]?.ToString() ?? "", item["preSharedKey"]?.ToString() ?? "");
                        if (preShared.Length > 0) peer["preSharedKey"] = preShared;
                        int keepAlive = IntOf(item, "persistent_keepalive_interval", IntOf(item, "keepAlive"));
                        if (keepAlive > 0) peer["keepAlive"] = keepAlive;
                        var allowed = StrArray(item["allowed_ips"]);
                        if (allowed.Count > 0) peer["allowedIPs"] = new JArray(allowed);
                        var reserved = IntArray(item["reserved"]);
                        if (reserved.Count > 0) peer["reserved"] = new JArray(reserved);
                        peers.Add(peer);
                    }
                    if (peers.Count == 0)
                    {
                        error = "wireguard endpoint has no usable peer";
                        return null;
                    }
                    settings["peers"] = peers;
                    break;
                }
                default:
                    error = $"sing-box '{type}' has no xray equivalent";
                    return null;
            }
            var stream = BuildStreamSettings(sb, out string streamError);
            if (stream == null)
            {
                error = streamError;
                return null;
            }
            string tag = sb["tag"]?.ToString() ?? "";
            if (tag.Length > 0) xray["tag"] = tag;
            xray["protocol"] = type == "hysteria2" ? "hysteria" : type;
            xray["settings"] = settings;
            if (stream.Count > 0) xray["streamSettings"] = stream;
            var mux = BuildMux(sb);
            if (mux != null) xray["mux"] = mux;
            return xray;
        }

        private static JObject? BuildStreamSettings(JObject sb, out string error)
        {
            error = "";
            var stream = new JObject();
            var tls     = sb["tls"] as JObject;
            var reality = tls?["reality"] as JObject;
            if (reality != null && BoolOf(reality, "enabled", true))
            {
                string publicKey = reality["public_key"]?.ToString() ?? "";
                if (publicKey.Length == 0)
                {
                    error = "the reality block has no public_key";
                    return null;
                }
                stream["security"] = "reality";
                stream["realitySettings"] = new JObject
                {
                    ["serverName"]  = NonEmpty(reality["server_name"]) ?? NonEmpty(tls?["server_name"]) ?? "",
                    ["publicKey"]   = publicKey,
                    ["shortId"]     = NonEmpty(reality["short_id"]) ?? "",
                    ["fingerprint"] = Fingerprint(tls)
                };
            }
            else if (tls != null && BoolOf(tls, "enabled", true))
            {
                stream["security"] = "tls";
                var tlsSettings = new JObject();
                if (BoolOf(tls, "insecure", false)) tlsSettings["insecure"] = true;
                string sni = NonEmpty(tls["server_name"]);
                if (sni != null) tlsSettings["serverName"] = sni;
                var alpn = StrArray(tls["alpn"]);
                if (alpn.Count > 0) tlsSettings["alpn"] = new JArray(alpn);
                string fp = Fingerprint(tls);
                if (fp.Length > 0) tlsSettings["fingerprint"] = fp;
                stream["tlsSettings"] = tlsSettings;
                if (BoolOf(tls, "fragment", false))
                    SimpleLogger.LogOnce(
                        $"mask-xray|{sb["server"]}",
                        "[Converter] sing-box TLS fragmentation has no packet rules to translate into an xray final mask; the mask was skipped.");
            }
            var transport        = sb["transport"] as JObject;
            string transportType = transport?["type"]?.ToString() ?? "";
            string network       = transportType switch
            {
                "ws"          => "ws",
                "grpc"        => "grpc",
                "http"        => "http",
                "h2"          => "http",
                "httpupgrade" => "httpupgrade",
                "xhttp"       => "xhttp",
                "tcp"         => "tcp",
                _             => ""
            };
            if (string.Equals(sb["type"]?.ToString() ?? "", "hysteria2", StringComparison.OrdinalIgnoreCase))
            {
                var hysteria = new JObject { ["version"] = 2 };
                string hysteriaAuth = sb["password"]?.ToString() ?? "";
                if (hysteriaAuth.Length > 0) hysteria["auth"] = hysteriaAuth;
                if (sb["obfs"] is JObject hysteriaObfs)
                {
                    string obfsType     = hysteriaObfs["type"]?.ToString() ?? "";
                    string obfsPassword = hysteriaObfs["password"]?.ToString() ?? "";
                    if (obfsType == "salamander" && obfsPassword.Length > 0)
                        hysteria["obfs"] = new JObject { ["type"] = "salamander", ["password"] = obfsPassword };
                }
                var hysteriaHopPorts = StrArray(sb["server_ports"]);
                if (hysteriaHopPorts.Count > 0)
                {
                    var hop = new JObject { ["ports"] = string.Join(",", hysteriaHopPorts.Select(p => p.Replace(':', '-'))) };
                    int hopInterval = IntOf(sb, "hop_interval");
                    if (hopInterval > 0) hop["interval"] = hopInterval;
                    hysteria["udpHop"] = hop;
                }
                stream["network"]          = "hysteria";
                stream["hysteriaSettings"] = hysteria;
            }
            else
            {
                if (transportType.Length > 0 && network.Length == 0)
                {
                    error = $"the sing-box '{transportType}' transport has no xray equivalent";
                    return null;
                }
                if (network.Length > 0 && network != "tcp")
                {
                    stream["network"] = network;
                    var transportSettings = BuildTransportSettings(transport!, network, out string transportError);
                    if (transportSettings == null)
                    {
                        error = transportError;
                        return null;
                    }
                    stream[network + "Settings"] = transportSettings;
                }
            }
            return stream;
        }

        private static JObject? BuildTransportSettings(JObject transport, string network, out string error)
        {
            error = "";
            switch (network)
            {
                case "ws":
                {
                    var ws = new JObject { ["path"] = NonEmpty(transport["path"]) ?? "/" };
                    var headers = RawObject(transport["headers"]);
                    if (headers != null) ws["headers"] = headers;
                    return ws;
                }
                case "grpc":
                    return new JObject
                    {
                        ["serviceName"] = NonEmpty(transport["service_name"]) ?? "",
                        ["multiMode"]   = false
                    };
                case "http":
                {
                    var http = new JObject { ["path"] = NonEmpty(transport["path"]) ?? "/" };
                    var host = StrArray(transport["host"]);
                    if (host.Count > 0) http["host"] = new JArray(host);
                    return http;
                }
                case "httpupgrade":
                {
                    var upgrade = new JObject
                    {
                        ["host"] = NonEmpty(transport["host"]) ?? "",
                        ["path"] = NonEmpty(transport["path"]) ?? "/"
                    };
                    var upgradeHeaders = RawObject(transport["headers"]);
                    if (upgradeHeaders != null) upgrade["headers"] = upgradeHeaders;
                    return upgrade;
                }
                case "xhttp":
                {
                    var xhttp = new JObject
                    {
                        ["host"] = NonEmpty(transport["host"]) ?? "",
                        ["path"] = NonEmpty(transport["path"]) ?? "/",
                        ["mode"] = NonEmpty(transport["mode"]) ?? "auto"
                    };
                    if (transport["extra"] is JObject extra) xhttp["extra"] = extra.DeepClone();
                    return xhttp;
                }
                default:
                    error = $"the sing-box '{network}' transport has no xray equivalent";
                    return null;
            }
        }

        private static JObject? BuildMux(JObject sb)
        {
            if (sb["multiplex"] is not JObject mux || !BoolOf(mux, "enabled", true)) return null;
            return new JObject
            {
                ["enabled"]    = true,
                ["protocol"]   = NonEmpty(mux["protocol"]) ?? "h2mux",
                ["maxStreams"] = IntOf(mux, "max_streams", 8),
                ["padding"]    = BoolOf(mux, "padding", false)
            };
        }

        private static JObject? RawObject(JToken? token)
            => token is JObject obj && obj.Count > 0 ? (JObject)obj.DeepClone() : null;

        private static string Fingerprint(JObject? tls)
        {
            string direct = tls?["fingerprint"]?.ToString() ?? "";
            if (direct.Length > 0) return direct;
            if (tls?["utls"] is JObject utls && BoolOf(utls, "enabled", true))
                return NonEmpty(utls["fingerprint"]) ?? "";
            return "";
        }

        private static string? NonEmpty(JToken? token)
        {
            string value = token?.ToString() ?? "";
            return value.Trim().Length > 0 ? value : null;
        }

        private static int IntOf(JObject? obj, string name, int fallback = 0)
        {
            try
            {
                var token = obj?[name];
                if (token == null) return fallback;
                if (token.Type == JTokenType.Integer) return token.ToObject<int>();
                return int.TryParse(token.ToString(), out int parsed) ? parsed : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static bool BoolOf(JObject? obj, string name, bool fallback)
        {
            try
            {
                var token = obj?[name];
                if (token == null) return fallback;
                if (token.Type == JTokenType.Boolean) return token.ToObject<bool>();
                if (token.Type == JTokenType.String && bool.TryParse(token.ToString(), out bool parsed)) return parsed;
                return fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static List<string> StrArray(JToken? token)
        {
            var list = new List<string>();
            if (token == null) return list;
            if (token is JArray arr)
            {
                foreach (var item in arr)
                {
                    string value = item?.ToString() ?? "";
                    if (value.Trim().Length > 0) list.Add(value);
                }
                return list;
            }
            string single = token.ToString();
            if (single.Trim().Length > 0) list.Add(single);
            return list;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return "";
        }

        private static List<int> IntArray(JToken? token)
        {
            var list = new List<int>();
            if (token is not JArray arr) return list;
            foreach (var item in arr)
            {
                if (item == null) continue;
                if (item.Type == JTokenType.Integer) { list.Add(item.ToObject<int>()); continue; }
                if (int.TryParse(item.ToString(), out int parsed)) list.Add(parsed);
            }
            return list;
        }

        private static void StripDialFields(JObject outbound)
        {
            foreach (var key in DialFieldNames) outbound.Remove(key);
        }

        private static readonly string[] DialFieldNames =
        {
            "label", "bind_interface", "inet4_bind_address", "inet6_bind_address", "domain_resolver",
            "tcp_fast_open", "tcp_multi_path", "udp_fragment", "connect_timeout", "detour"
        };
    }
}

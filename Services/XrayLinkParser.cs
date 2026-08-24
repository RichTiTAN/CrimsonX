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
                else
                    return false;

                if (outbound == null || outbound["protocol"] == null)
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

        private static string DecodeBase64(string b64)
        {
            b64 = b64.Trim().Replace("-", "+").Replace("_", "/");
            int mod = b64.Length % 4;
            if (mod > 0) b64 += new string('=', 4 - mod);
            return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
        }


        public static List<string> ExtractVlessConfigs(string content)
        {
            var links = new List<string>();
            if (string.IsNullOrWhiteSpace(content)) return links;

            string decoded = content;
            try
            {
                if (!content.Contains("://"))
                {
                    decoded = DecodeBase64(content);
                }
            }
            catch { }

            var lines = decoded.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var l = line.Trim();
                if (l.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
                {
                    links.Add(l);
                }
            }
            return links;
        }

        private static JObject ParseWireguard(string link)
        {
            var uri = new Uri(link);
            var query = HttpUtility.ParseQueryString(uri.Query);

            var outbound = new JObject
            {
                ["protocol"] = "wireguard",
                ["settings"] = new JObject
                {
                    ["secretKey"] = uri.UserInfo,
                    ["endpoint"] = uri.IdnHost + ":" + uri.Port,
                    ["peers"] = new JArray
                    {
                        new JObject
                        {
                            ["publicKey"] = query["publickey"] ?? query["public_key"] ?? query["pk"] ?? "",
                        }
                    }
                }
            };
            
            if (query["reserved"] != null)
            {
                var parts = query["reserved"].Split(',').Select(s => int.TryParse(s, out var i) ? i : 0).ToArray();
                if (parts.Length == 3)
                    outbound["settings"]["reserved"] = new JArray(parts);
            }
            if (query["mtu"] != null && int.TryParse(query["mtu"], out int mtu))
                outbound["settings"]["mtu"] = mtu;

            return outbound;
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
            string methodPass = "";
            string hostPort = "";

            int hashIdx = payload.IndexOf("#");
            if (hashIdx >= 0) payload = payload.Substring(0, hashIdx);

            if (payload.Contains("@"))
            {
                string[] parts = payload.Split(new[] { '@' }, 2);
                methodPass = DecodeBase64(parts[0]);
                hostPort = parts[1];
            }
            else
            {
                string decoded = DecodeBase64(payload);
                if (decoded.Contains("@"))
                {
                    string[] parts = decoded.Split(new[] { '@' }, 2);
                    methodPass = parts[0];
                    hostPort = parts[1];
                }
            }

            string[] mpParts = methodPass.Split(new[] { ':' }, 2);
            string[] hpParts = hostPort.Split(new[] { ':' }, 2);

            if (mpParts.Length < 2 || hpParts.Length < 2)
                return null;

            string portStr = hpParts[1];
            int slashIdx = portStr.IndexOf('/');
            if (slashIdx >= 0) portStr = portStr.Substring(0, slashIdx);
            
            int questionIdx = portStr.IndexOf('?');
            if (questionIdx >= 0) portStr = portStr.Substring(0, questionIdx);
            
            if (!int.TryParse(portStr, out int port))
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
                            ["address"] = hpParts[0],
                            ["port"] = port,
                            ["method"] = mpParts[0],
                            ["password"] = mpParts[1]
                        }
                    }
                }
            };

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
                if (!string.IsNullOrEmpty(pcs)) tlsObj["pinnedCA256"] = pcs;
                
                string pqv = query["pqv"];
                if (!string.IsNullOrEmpty(pqv)) tlsObj["mldsa65Verify"] = pqv;

                bool allowInsecure = query["allowInsecure"] == "1" || query["insecure"] == "1" || query["allowInsecure"] == "true" || query["insecure"] == "true";
                if (allowInsecure) tlsObj["allowInsecure"] = true;

                if (security == "reality")
                {
                    string pbk = query["pbk"];
                    if (!string.IsNullOrEmpty(pbk)) tlsObj["publicKey"] = pbk;
                    
                    string sid = query["sid"];
                    if (!string.IsNullOrEmpty(sid)) tlsObj["shortId"] = sid;
                    
                    string spx = query["spx"];
                    if (!string.IsNullOrEmpty(spx)) tlsObj["spiderX"] = spx;
                    
                    string fm = query["fm"];
                    if (!string.IsNullOrEmpty(fm)) tlsObj["finalMask"] = fm;
                }
                
                stream[security + "Settings"] = tlsObj;
            }

            if (net == "ws")
            {
                var wsObj = new JObject();
                string path = query["path"];
                if (!string.IsNullOrEmpty(path)) wsObj["path"] = path;
                
                string host = query["host"];
                if (!string.IsNullOrEmpty(host)) wsObj["headers"] = new JObject { ["Host"] = host };
                
                stream["wsSettings"] = wsObj;
            }
            else if (net == "tcp")
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
                        
                    stream["tcpSettings"] = tcpObj;
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
            else if (net == "kcp")
            {
                var kcpObj = new JObject();
                string headerType = query["headerType"];
                if (!string.IsNullOrEmpty(headerType)) kcpObj["header"] = new JObject { ["type"] = headerType };
                
                string seed = query["seed"];
                if (!string.IsNullOrEmpty(seed)) kcpObj["seed"] = seed;
                
                if (int.TryParse(query["mtu"], out int mtu)) kcpObj["mtu"] = mtu;
                if (int.TryParse(query["tti"], out int tti)) kcpObj["tti"] = tti;
                
                stream["kcpSettings"] = kcpObj;
            }
            else if (net == "quic")
            {
                var quicObj = new JObject();
                string headerType = query["headerType"];
                if (!string.IsNullOrEmpty(headerType)) quicObj["header"] = new JObject { ["type"] = headerType };
                
                string quicSec = query["quicSecurity"];
                if (!string.IsNullOrEmpty(quicSec)) quicObj["security"] = quicSec;
                
                string key = query["key"];
                if (!string.IsNullOrEmpty(key)) quicObj["key"] = key;
                
                stream["quicSettings"] = quicObj;
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
    }
}

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
using System.Linq;
using CrimsonX.Models;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public static class ExitNodeChain
    {
        public const string EndpointTag = "exit-node";

        public const string InboundTag = "exit-in";

        public const string TransportTag = "proxy";

        public const int DefaultProxyPort = 10920;

        public static int ProxyPort { get; private set; } = DefaultProxyPort;

        public static int AllocateProxyPort()
        {
            if (IsPortFree(DefaultProxyPort))
            {
                if (ProxyPort != DefaultProxyPort)
                {
                    ProxyPort = DefaultProxyPort;
                    SimpleLogger.Log($"[ExitNode] The proxy inbound uses 127.0.0.1:{ProxyPort}.");
                }

                return ProxyPort;
            }

            for (int candidate = DefaultProxyPort + 1; candidate <= DefaultProxyPort + 40; candidate++)
            {
                if (!IsPortFree(candidate)) continue;

                ProxyPort = candidate;
                SimpleLogger.Log($"[ExitNode] Port {DefaultProxyPort} is busy, so the proxy inbound uses 127.0.0.1:{ProxyPort}.");
                return ProxyPort;
            }

            ProxyPort = DefaultProxyPort;
            SimpleLogger.Log($"[ExitNode] No free port near {DefaultProxyPort} was found; the exit chain may not start.");
            return ProxyPort;
        }

        private static bool IsPortFree(int port)
        {
            System.Net.Sockets.TcpListener? listener = null;
            try
            {
                listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                listener.Start();
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { listener?.Stop(); } catch { }
            }
        }

        public const int XrayProxyPort = 10919;

        public static bool ChainActive { get; private set; }

        public static void SetChainActive(bool active)
        {
            if (ChainActive == active) return;

            ChainActive = active;
            SimpleLogger.Log(active
                ? $"[ExitNode] The exit chain is active; proxied traffic now leaves through sing-box 127.0.0.1:{ProxyPort}."
                : "[ExitNode] The exit chain is inactive; proxied traffic goes through xray's proxy again.");
        }

        public static int ActivePort => ChainActive ? ProxyPort : XrayProxyPort;

        public static bool IsTunnelExit(AppConfig config) => Plane(config) == ExitNodePlane.SingboxEndpoint;

        public enum ExitNodePlane
        {
            None,

            SingboxEndpoint,

            SingboxOutbound,

            XrayOutbound
        }

        private static string _planeCacheRaw = "";
        private static ExitNodePlane _planeCache = ExitNodePlane.None;

        public static ExitNodePlane Plane(AppConfig config)
        {
            if (config == null || !config.EnableV2rayChain) return ExitNodePlane.None;

            string raw = (config.V2rayChainJson ?? "").Trim();
            if (raw.Length == 0) return ExitNodePlane.None;
            if (_planeCacheRaw == raw) return _planeCache;

            ExitNodePlane plane = TunnelConfigParser.LooksLikeTunnel(raw) ? ExitNodePlane.SingboxEndpoint
                                : IsPlainTcpVless(raw)                  ? ExitNodePlane.SingboxOutbound
                                                                        : ExitNodePlane.XrayOutbound;

            _planeCacheRaw = raw;
            _planeCache    = plane;
            return plane;
        }

        public static bool IsSingboxExit(AppConfig config)
            => Plane(config) is ExitNodePlane.SingboxEndpoint or ExitNodePlane.SingboxOutbound;

        public static bool IsPlainTcpVless(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;

            try
            {
                var outbound = ReadVlessOutbound(raw);
                return outbound != null && HasNoTls(outbound) && IsPlainTransport(outbound);
            }
            catch (Exception ex)
            {
                SimpleLogger.Log($"[ExitNode] The exit node could not be read as a sing-box config: {ex.Message}");
                return false;
            }
        }

        private static JObject? ReadVlessOutbound(string raw)
        {
            string text = raw.Trim();

            if (text.StartsWith("{", StringComparison.Ordinal))
            {
                var fromJson = FirstVlessOutbound(text);
                if (fromJson != null) return fromJson;

                return ConfigConverter.TryConvertToSingbox(text, out string singbox, out _, out _)
                    ? FirstVlessOutbound(singbox)
                    : null;
            }

            if (text.StartsWith("vless://", StringComparison.OrdinalIgnoreCase)
                && SingboxLinkParser.TryParseLink(text, out string linkOutbound, out _))
                return FirstVlessOutbound(linkOutbound);

            return null;
        }

        private static JObject? FirstVlessOutbound(string json)
        {
            var root = JObject.Parse(json);
            var candidates = root["outbounds"] is JArray array ? array.OfType<JObject>() : new[] { root };

            return candidates.FirstOrDefault(o =>
                string.Equals(o["type"]?.ToString(), "vless", StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasNoTls(JObject outbound)
            => outbound["tls"] is not JObject tls || tls["enabled"]?.ToObject<bool>() == false;

        private static bool IsPlainTransport(JObject outbound)
            => outbound["transport"] is not JObject transport
               || transport["type"]?.ToString() is "tcp" or "raw";

        public static bool ShouldChain(AppConfig config)
            => IsSingboxExit(config)
               && string.Equals(config.LastXrayMode, "VPN Mode", StringComparison.OrdinalIgnoreCase);

        public static int DisplayPort(AppConfig config) => ShouldChain(config) ? ProxyPort : XrayProxyPort;

        public static JObject? BuildEndpoint(AppConfig config, out bool chained, out string error)
        {
            chained = false;
            error   = "";

            if (config == null || !config.EnableV2rayChain) return null;

            string raw = (config.V2rayChainJson ?? "").Trim();
            if (raw.Length == 0) return null;

            if (!TunnelConfigParser.TryParse(raw, out var tunnel) || tunnel?.Endpoint == null || !tunnel.Success)
                return null;

            if (!string.Equals(config.LastXrayMode, "VPN Mode", StringComparison.OrdinalIgnoreCase))
            {
                error = $"'{tunnel.Label}' is an OpenVPN/WireGuard exit node and only chains in VPN Mode.";
                return null;
            }

            if (!TunnelCredentialResolver.ApplyStored(tunnel))
            {
                error = $"'{tunnel.Label}' needs OpenVPN credentials before it can act as the exit node.";
                return null;
            }

            var endpoint = (JObject)tunnel.Endpoint.DeepClone();
            endpoint.Remove("tag");
            endpoint.Remove("bind_interface");
            endpoint.Remove("inet4_bind_address");

            endpoint["tag"]    = EndpointTag;
            endpoint["detour"] = TransportTag;

            chained = true;
            return endpoint;
        }

        public static JObject? BuildSingboxOutbound(AppConfig config, out bool chained, out string error)
        {
            chained = false;
            error   = "";

            if (config == null || !config.EnableV2rayChain) return null;

            string raw = (config.V2rayChainJson ?? "").Trim();
            if (raw.Length == 0 || !IsPlainTcpVless(raw)) return null;

            var outbound = ReadVlessOutbound(raw);
            if (outbound == null) return null;

            if (!string.Equals(config.LastXrayMode, "VPN Mode", StringComparison.OrdinalIgnoreCase))
            {
                error = $"'{SingboxLinkParser.DescribeOutbound(outbound)}' is a vless exit node and only chains in VPN Mode.";
                return null;
            }

            outbound.Remove("tag");
            outbound.Remove("bind_interface");
            outbound.Remove("inet4_bind_address");

            outbound["tag"]    = EndpointTag;
            outbound["detour"] = TransportTag;

            chained = true;
            return outbound;
        }

        public static JObject? BuildExit(AppConfig config, out ExitNodePlane plane, out bool chained, out string error)
        {
            plane = Plane(config);

            switch (plane)
            {
                case ExitNodePlane.SingboxEndpoint:
                    return BuildEndpoint(config, out chained, out error);

                case ExitNodePlane.SingboxOutbound:
                    return BuildSingboxOutbound(config, out chained, out error);

                default:
                    chained = false;
                    error   = "";
                    return null;
            }
        }

        public static void PlaceInto(JObject document, JObject exitObject, ExitNodePlane plane)
        {
            if (document == null || exitObject == null) return;

            if (plane == ExitNodePlane.SingboxEndpoint)
            {
                document["endpoints"] = new JArray { exitObject.DeepClone() };
                return;
            }

            if (document["outbounds"] is JArray outbounds) outbounds.Add(exitObject.DeepClone());
            else document["outbounds"] = new JArray { exitObject.DeepClone() };
        }

        public static void AddProxyInbound(JObject document, AppConfig config)
        {
            if (document == null || config == null) return;

            var inbound = new JObject
            {
                ["type"]        = "mixed",
                ["tag"]         = InboundTag,
                ["listen"]      = config.AllowLanConnections ? "0.0.0.0" : "127.0.0.1",
                ["listen_port"] = ProxyPort
            };

            bool lanAuth = config.AllowLanConnections
                && config.EnableLanAuth
                && !string.IsNullOrWhiteSpace(config.LanAuthUsername)
                && !string.IsNullOrWhiteSpace(config.LanAuthPassword);

            if (lanAuth)
            {
                inbound["users"] = new JArray
                {
                    new JObject
                    {
                        ["username"] = config.LanAuthUsername,
                        ["password"] = config.LanAuthPassword
                    }
                };
            }

            if (document["inbounds"] is JArray inbounds) inbounds.Add(inbound);
            else document["inbounds"] = new JArray { inbound };
        }
    }
}

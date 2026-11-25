/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public enum EngineSupport
    {
        Both,
        XrayOnly
    }

    public enum LinkCarrier
    {
        TcpLike,
        UdpLike
    }

    public sealed class StreamProfile
    {
        public string Method { get; init; } = "raw";
        public EngineSupport Engine { get; init; } = EngineSupport.Both;
        public LinkCarrier Carrier { get; init; } = LinkCarrier.TcpLike;
        public bool Shareable { get; init; } = true;
        public bool Removed { get; init; }
        public string Replacement { get; init; } = "";

        public static StreamProfile FromStream(JObject stream)
            => FromMethod(StreamSettings.MethodOf(stream), StreamSettings.AlpnOf(stream));

        public static StreamProfile FromQuery(NameValueCollection query)
            => FromMethod(query?["type"], StreamSettings.AlpnOf(query?["alpn"]));

        public static StreamProfile FromMethod(string method, IReadOnlyList<string> alpn = null)
        {
            string normalized = Normalize(method);
            bool h3 = HasAlpn(alpn, "h3");
            bool h2 = HasAlpn(alpn, "h2");
            switch (normalized)
            {
                case "xhttp":
                    return new StreamProfile
                    {
                        Method = normalized,
                        Engine = EngineSupport.XrayOnly,
                        Carrier = h3 ? LinkCarrier.UdpLike : LinkCarrier.TcpLike
                    };
                case "kcp":
                case "hysteria":
                    return new StreamProfile
                    {
                        Method = normalized,
                        Engine = EngineSupport.XrayOnly,
                        Carrier = LinkCarrier.UdpLike
                    };
                case "masque":
                    return new StreamProfile
                    {
                        Method = normalized,
                        Engine = EngineSupport.XrayOnly,
                        Carrier = h2 && !h3 ? LinkCarrier.TcpLike : LinkCarrier.UdpLike
                    };
                case "xdrive":
                    return new StreamProfile
                    {
                        Method = normalized,
                        Engine = EngineSupport.XrayOnly,
                        Carrier = h3 ? LinkCarrier.UdpLike : LinkCarrier.TcpLike,
                        Shareable = false
                    };
                case "http":
                case "quic":
                    return new StreamProfile
                    {
                        Method = normalized,
                        Engine = EngineSupport.XrayOnly,
                        Carrier = LinkCarrier.TcpLike,
                        Shareable = false,
                        Removed = true,
                        Replacement = "xhttp"
                    };
                default:
                    return new StreamProfile { Method = normalized };
            }
        }

        public static string Normalize(string method)
        {
            if (string.IsNullOrWhiteSpace(method)) return "raw";
            switch (method.Trim().ToLowerInvariant())
            {
                case "tcp":
                    return "raw";
                case "mkcp":
                    return "kcp";
                case "websocket":
                    return "ws";
                case "splithttp":
                    return "xhttp";
                case "h2":
                case "h3":
                    return "http";
                default:
                    return method.Trim().ToLowerInvariant();
            }
        }

        private static bool HasAlpn(IReadOnlyList<string> alpn, string value)
        {
            if (alpn == null) return false;
            foreach (string item in alpn)
            {
                if (string.Equals(item?.Trim(), value, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}

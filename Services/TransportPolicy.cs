/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public enum TransportContext
    {
        CustomConfig,
        ExitNode,
        CustomProxy
    }

    public static class TransportPolicy
    {
        public static StreamProfile ProfileOf(string config)
        {
            if (string.IsNullOrWhiteSpace(config)) return null;
            string text = config.Trim();
            string json;
            if (text.StartsWith("{", StringComparison.Ordinal))
            {
                json = text;
            }
            else if (!XrayLinkParser.TryParseLink(text, out json))
            {
                return null;
            }

            try
            {
                var root = JObject.Parse(json);
                var first = (root["outbounds"] as JArray)?.OfType<JObject>().FirstOrDefault() ?? root;
                return StreamProfile.FromStream(first["streamSettings"] as JObject);
            }
            catch
            {
                return null;
            }
        }

        public static bool NeedsXray(string config)
            => ProfileOf(config)?.Engine == EngineSupport.XrayOnly;

        public static bool IsUdp(string config)
            => ProfileOf(config)?.Carrier == LinkCarrier.UdpLike;

        public static string Refusal(string config, TransportContext context)
        {
            if (context == TransportContext.CustomConfig) return "";
            var profile = ProfileOf(config);
            if (profile == null) return "";
            if (profile.Removed)
            {
                return profile.Replacement.Length > 0
                    ? $"the {profile.Method} transport was removed from Xray, use {profile.Replacement}"
                    : $"the {profile.Method} transport was removed from Xray";
            }
            if (context == TransportContext.CustomProxy && profile.Engine == EngineSupport.XrayOnly)
                return $"the {profile.Method} transport only runs on Xray, so it cannot carry app rules";
            if (profile.Carrier == LinkCarrier.UdpLike)
                return $"the {profile.Method} transport sends UDP, so it only works as a custom config";
            return "";
        }

        public static bool Allowed(string config, TransportContext context)
            => Refusal(config, context).Length == 0;
    }
}

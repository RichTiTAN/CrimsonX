/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public static class StreamSettings
    {
        public const string MethodKey = "method";
        public const string LegacyNetworkKey = "network";

        public static string MethodOf(JObject stream)
        {
            if (stream == null) return "raw";
            string method = stream[MethodKey]?.ToString();
            if (string.IsNullOrWhiteSpace(method)) method = stream[LegacyNetworkKey]?.ToString();
            return string.IsNullOrWhiteSpace(method) ? "raw" : method.Trim();
        }

        public static void SetMethod(JObject stream, string method)
        {
            if (stream == null || string.IsNullOrWhiteSpace(method)) return;
            stream.Remove(LegacyNetworkKey);
            stream[MethodKey] = method.Trim();
        }

        public static List<string> AlpnOf(JObject stream)
        {
            var list = new List<string>();
            if (stream == null) return list;
            foreach (string security in new[] { "tlsSettings", "realitySettings" })
            {
                if (stream[security]?["alpn"] is not JArray alpn) continue;
                foreach (var item in alpn)
                {
                    string value = item?.ToString() ?? "";
                    if (value.Length > 0) list.Add(value);
                }
            }
            return list;
        }

        public static List<string> AlpnOf(string alpn)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(alpn)) return list;
            foreach (string part in alpn.Split(','))
            {
                string value = part.Trim();
                if (value.Length > 0) list.Add(value);
            }
            return list;
        }
    }
}

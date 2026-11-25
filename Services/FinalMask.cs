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
    public static class FinalMask
    {
        private static readonly string[] Keys = { "finalmask", "finalMask" };

        public static JToken Read(string fm, out string warning)
        {
            warning = "";
            if (string.IsNullOrWhiteSpace(fm)) return null;
            try
            {
                return JToken.Parse(fm);
            }
            catch
            {
                warning = "the final mask in this config is not valid JSON, so xray never sees it";
                return null;
            }
        }

        public static JToken FromStream(JToken streamOrOutbound)
        {
            if (streamOrOutbound is not JObject obj) return null;
            return MaskOn(obj) ?? (obj["streamSettings"] is JObject stream ? MaskOn(stream) : null);
        }

        private static JToken MaskOn(JObject owner)
        {
            foreach (string key in Keys)
            {
                if (owner[key] is JToken direct) return direct;
            }
            foreach (string security in new[] { "tlsSettings", "realitySettings" })
            {
                if (owner[security] is not JObject settings) continue;
                foreach (string key in Keys)
                {
                    if (settings[key] is JToken nested) return nested;
                }
            }
            return null;
        }

        public static bool HasTcpFragment(JToken mask)
            => mask?["tcp"] is JArray tcp
            && tcp.OfType<JObject>().Any(e => string.Equals(e["type"]?.ToString() ?? "", "fragment", StringComparison.OrdinalIgnoreCase));

        public static bool LooksLikeMaskFailure(string xrayMessage)
        {
            if (string.IsNullOrWhiteSpace(xrayMessage)) return false;
            if (xrayMessage.Contains("mask", StringComparison.OrdinalIgnoreCase)) return true;
            return xrayMessage.Contains("lengths entry", StringComparison.OrdinalIgnoreCase)
                || xrayMessage.Contains("LengthMin", StringComparison.OrdinalIgnoreCase)
                || xrayMessage.Contains("DelayMin", StringComparison.OrdinalIgnoreCase);
        }

        public static bool Strip(JObject outbound)
        {
            if (outbound == null) return false;
            bool removed = StripKeys(outbound);
            if (outbound["streamSettings"] is JObject stream)
            {
                removed |= StripKeys(stream);
                foreach (string security in new[] { "tlsSettings", "realitySettings" })
                {
                    if (stream[security] is JObject settings) removed |= StripKeys(settings);
                }
            }
            return removed;
        }

        public static string StripDocument(string document)
        {
            try
            {
                var root = JObject.Parse(document);
                if (root["outbounds"] is JArray outbounds)
                {
                    foreach (var outbound in outbounds.OfType<JObject>()) Strip(outbound);
                }
                else
                {
                    Strip(root);
                }
                return root.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch
            {
                return document;
            }
        }

        public static string StripFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            string trimmed = text.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal)) return StripDocument(trimmed);
            if (!trimmed.Contains("://", StringComparison.Ordinal)) return text;
            int hash = trimmed.IndexOf('#');
            string body     = hash >= 0 ? trimmed.Substring(0, hash) : trimmed;
            string fragment = hash >= 0 ? trimmed.Substring(hash) : "";
            int question = body.IndexOf('?');
            if (question < 0) return text;
            string head = body.Substring(0, question + 1);
            var kept = body.Substring(question + 1)
                           .Split('&')
                           .Where(pair => pair.Length > 0
                                       && !pair.Split('=')[0].Equals("fm", StringComparison.OrdinalIgnoreCase));
            return head + string.Join("&", kept) + fragment;
        }

        private static bool StripKeys(JObject owner)
        {
            bool removed = false;
            foreach (string key in Keys)
            {
                if (owner[key] == null) continue;
                owner.Remove(key);
                removed = true;
            }
            return removed;
        }
    }
}

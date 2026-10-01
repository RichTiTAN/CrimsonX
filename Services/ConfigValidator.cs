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
using System.Threading.Tasks;
using CrimsonX.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public enum ConfigCheckLevel
    {
        Offline,

        Live
    }

    public sealed class ConfigCheckResult
    {
        public bool Ok { get; set; }

        public bool Unreadable { get; set; }

        public bool NeedsCredentials { get; set; }

        public bool IsTunnel { get; set; }

        public string Reason { get; set; } = "";

        public string Label { get; set; } = "";

        public string MaskNote { get; set; } = "";

        public string WithoutMask { get; set; } = "";

        public string EngineJson { get; set; } = "";
    }

    public static class ConfigValidator
    {
        private const string ProbeTag = "custom-probe";

        public static async Task<ConfigCheckResult> CheckAsync(string raw, AppConfig cfg, ConfigTarget target,
            ConfigCheckLevel level = ConfigCheckLevel.Offline, string adapterName = "", string adapterIp = "", string sbDirOverride = "")
        {
            var result = new ConfigCheckResult();

            string text = (raw ?? "").Trim();
            if (text.Length == 0)
            {
                result.Unreadable = true;
                result.Reason = "empty config";
                return result;
            }

            string sbDir = !string.IsNullOrWhiteSpace(sbDirOverride) ? sbDirOverride : cfg?.SbDir ?? "";

            if (ConfigConverter.TryTunnel(text, out var tunnel, out _))
            {
                result.IsTunnel = true;
                result.Label = tunnel.Label ?? "";

                return level == ConfigCheckLevel.Live
                    ? await CheckTunnelLiveAsync(sbDir, tunnel, adapterName, adapterIp, result).ConfigureAwait(false)
                    : CheckTunnelOffline(sbDir, tunnel, result);
            }

            switch (target)
            {
                case ConfigTarget.Xray:
                    return await CheckForXrayAsync(text, cfg, result).ConfigureAwait(false);

                case ConfigTarget.Singbox:
                    return await CheckForSingboxAsync(text, sbDir, adapterName, adapterIp, result).ConfigureAwait(false);

                default:
                    result.Ok = ConfigConverter.TryAcceptFor(text, ConfigTarget.Pool, out string poolError);
                    if (result.Ok) result.Label = ConfigConverter.LabelFor(text);
                    else { result.Unreadable = true; result.Reason = poolError; }
                    return result;
            }
        }

        public static string ShortReason(string reason, int max = 150)
        {
            string text = (reason ?? "").Trim();
            return text.Length <= max ? text : text.Substring(0, max).Trim() + "…";
        }

        private static ConfigCheckResult CheckTunnelOffline(string sbDir, TunnelParseResult tunnel, ConfigCheckResult result)
        {
            string endpointJson = (tunnel.Endpoint ?? new JObject()).ToString(Formatting.None);

            result.Ok = SingboxConfigValidator.CheckEndpoint(sbDir, endpointJson);
            if (!result.Ok) result.Reason = "sing-box rejected this OpenVPN / WireGuard config";

            return result;
        }

        private static async Task<ConfigCheckResult> CheckTunnelLiveAsync(string sbDir, TunnelParseResult tunnel, string adapterName, string adapterIp, ConfigCheckResult result)
        {
            if (!TunnelCredentialResolver.ApplyStored(tunnel))
            {
                result.NeedsCredentials = true;
                result.Reason = "this tunnel needs a username and password";
                return result;
            }

            int port = 0;
            IDisposable lease = null;
            string tunnelError = "";

            bool started = await Task.Run(() => TunnelEngine.StartTransient(sbDir, tunnel, adapterName, adapterIp, out port, out lease, out tunnelError)).ConfigureAwait(false);
            try { lease?.Dispose(); } catch { }

            result.Ok = started;
            if (!started) result.Reason = tunnelError.Length > 0 ? tunnelError : "the tunnel could not be started";

            return result;
        }

        private static async Task<ConfigCheckResult> CheckForXrayAsync(string text, AppConfig cfg, ConfigCheckResult result)
        {
            if (!ConfigConverter.TryXrayOutbound(text, out string xrayDoc, out string label, out string convertError))
            {
                result.Unreadable = true;
                result.Reason = convertError;
                return result;
            }

            result.EngineJson = xrayDoc;
            result.Label = label;

            var xrayCheck = await XrayConfigValidator.CheckDetailedAsync(cfg, text).ConfigureAwait(false);

            result.Ok          = xrayCheck.Ok;
            result.MaskNote    = xrayCheck.MaskNote;
            result.WithoutMask = xrayCheck.WithoutMask;
            if (!result.Ok) result.Reason = xrayCheck.Error;

            return result;
        }

        private static async Task<ConfigCheckResult> CheckForSingboxAsync(string text, string sbDir, string adapterName, string adapterIp, ConfigCheckResult result)
        {
            if (!ConfigConverter.TrySingboxOutbound(text, out string outboundJson, out string label, out string convertError))
            {
                result.Unreadable = true;
                result.Reason = convertError.Length > 0 ? convertError : "this config cannot be used here";
                return result;
            }

            result.EngineJson = outboundJson;
            result.Label = label;

            bool ok;
            try
            {
                var tagged = SingboxLinkParser.WithTagAndDial(JObject.Parse(outboundJson), ProbeTag, adapterName, adapterIp);
                string probe = tagged.ToString(Formatting.None);

                ok = await Task.Run(() => SingboxConfigValidator.CheckOutbound(sbDir, probe)).ConfigureAwait(false);
            }
            catch
            {
                result.Unreadable = true;
                return result;
            }

            result.Ok = ok;
            if (!ok) result.Reason = "sing-box rejected this config";

            return result;
        }
    }
}

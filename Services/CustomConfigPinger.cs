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

using System.Threading;
using System.Threading.Tasks;
using CrimsonX.Models;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public static class CustomConfigPinger
    {
        public static async Task<ConfigTestResult> ProbeAsync(string raw, AppConfig cfg, string adapterName, string adapterIp, CancellationToken ct)
        {
            string text = (raw ?? "").Trim();
            if (text.Length == 0 || cfg == null) return new ConfigTestResult { Link = raw ?? "" };

            string bindIp = !string.IsNullOrWhiteSpace(adapterIp)
                ? adapterIp.Trim()
                : XrayConfigWriter.ProbeSendThrough(cfg);

            if (TunnelConfigParser.TryParse(text, out var tunnel) && tunnel != null && TunnelTcpProbe.IsOpenVpn(tunnel))
            {
                var tcp = await Task.Run(() => TunnelTcpProbe.Probe(tunnel, bindIp, TunnelTcpProbe.DefaultTimeoutMs), ct).ConfigureAwait(false);
                string route = TunnelTcpProbe.RouteText(bindIp);

                if (tcp.Ok)
                {
                    return new ConfigTestResult { Link = text, Success = true, Ping = tcp.Ms };
                }

                bool tunnelActive = XrayPipelineManager.ActiveOutbounds.Count > 0;
                string hint = tunnelActive ? " (a session is up, so the probe may be captured by the active tunnel)" : "";
                return new ConfigTestResult { Link = text, Success = false, TimedOut = tcp.TimedOut };
            }

            string xrayJson = "";
            if (ConfigConverter.IsXrayOutbound(text)) xrayJson = text;
            else if (ConfigConverter.TryConvert(text, out string converted, out _, out _)) xrayJson = converted;

            if (xrayJson.Length > 0)
            {
                var xrayResult = await ConfigTester.TestConfigAsync(ApplyBindAddress(xrayJson, bindIp), cfg, ct).ConfigureAwait(false);
                if (xrayResult.Success) return xrayResult;

                if (xrayResult.Kind == ConfigPingKind.Slow) return xrayResult;

                if (TunnelConfigParser.TryParse(text, out var wireGuard) && wireGuard != null && TunnelTcpProbe.IsWireGuard(wireGuard))
                    return await TcpFallbackAsync(wireGuard, xrayResult, bindIp, ct).ConfigureAwait(false);

                if (ConfigConverter.TrySingboxOutbound(text, out string sbOutbound, out _, out _) && sbOutbound.Length > 0)
                {
                    var sbResult = await SingboxConfigTester.TestOutboundAsync(sbOutbound, cfg, adapterName, adapterIp, ct).ConfigureAwait(false);
                    if (sbResult.Success)
                    {
                        sbResult.Reason = xrayResult.Reason;
                        return sbResult;
                    }

                    xrayResult.Reason = xrayResult.Reason.Length > 0 ? xrayResult.Reason : sbResult.Reason;
                    return xrayResult;
                }

                return xrayResult;
            }

            return await SingboxConfigTester.TestAsync(text, cfg, adapterName, adapterIp, ct).ConfigureAwait(false);
        }

        private static async Task<ConfigTestResult> TcpFallbackAsync(TunnelParseResult tunnel, ConfigTestResult xrayResult, string bindIp, CancellationToken ct)
        {
            var tcp = await Task.Run(() => TunnelTcpProbe.Probe(tunnel, bindIp, TunnelTcpProbe.DefaultTimeoutMs), ct).ConfigureAwait(false);
            string route = TunnelTcpProbe.RouteText(bindIp);

            if (tcp.Ok)
            {
                return new ConfigTestResult
                {
                    Link = tunnel.Raw, Success = true, Ping = tcp.Ms,
                    Kind = ConfigPingKind.Ok, IsTcpPing = true, Reason = xrayResult.Reason
                };
            }


            return new ConfigTestResult
            {
                Link = tunnel.Raw,
                Success = false,
                TimedOut = tcp.TimedOut || xrayResult.TimedOut,
                Kind = tcp.TimedOut || xrayResult.TimedOut ? ConfigPingKind.TimedOut : ConfigPingKind.Rejected,
                Reason = xrayResult.Reason.Length > 0 ? xrayResult.Reason : tcp.Error,
                EngineLog = xrayResult.EngineLog,
                IsTcpPing = true
            };
        }

        private static string ApplyBindAddress(string xrayJson, string bindIp)
        {
            if (string.IsNullOrWhiteSpace(bindIp)) return xrayJson;

            try
            {
                var root = JObject.Parse(xrayJson);
                var outbound = (root["outbounds"] as JArray)?[0] as JObject;
                if (outbound == null || XrayLinkParser.IsLocalOutbound(outbound)) return xrayJson;

                outbound["sendThrough"] = bindIp;
                return root.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch
            {
                return xrayJson;
            }
        }
    }
}

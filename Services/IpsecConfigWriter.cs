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
using System.IO;
using System.Linq;
using System.Text;
using CrimsonX.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public sealed class IpsecGroupPlan
    {
        public string ConfigPath { get; init; } = "";

        public string WorkingDirectory { get; init; } = "";

        public string StatusPath { get; init; } = "";

        public Dictionary<string, string> Environment { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public static class IpsecConfigWriter
    {
        private static readonly Encoding NoBom = new UTF8Encoding(false);

        public static IpsecGroupPlan Write(string sbDir, string group, IList<TunnelTarget> targets, string timeout = "")
        {
            if (targets == null || targets.Count == 0) return null;
            if (string.IsNullOrWhiteSpace(sbDir)) return null;
            try
            {
                string dir = Path.Combine(sbDir, "tunnel", DirectoryName(group));
                Directory.CreateDirectory(dir);
                var plan = new IpsecGroupPlan
                {
                    WorkingDirectory = dir,
                    ConfigPath = Path.Combine(dir, "ipsec.json"),
                    StatusPath = Path.Combine(dir, "status.json")
                };
                var tunnels = new JArray();
                for (int i = 0; i < targets.Count; i++) tunnels.Add(BuildTunnel(plan, targets[i], i, timeout));
                var document = new JObject
                {
                    ["log"] = new JObject { ["level"] = "info" },
                    ["status_file"] = plan.StatusPath,
                    ["shutdown_on_stdin_eof"] = true,
                    ["tunnels"] = tunnels
                };
                File.WriteAllText(plan.ConfigPath, document.ToString(Formatting.Indented), NoBom);
                return plan;
            }
            catch (Exception ex)
            {
                SimpleLogger.Log($"[Tunnel] The IKEv2 config could not be written: {ex.Message}");
                return null;
            }
        }

        private static JObject BuildTunnel(IpsecGroupPlan plan, TunnelTarget target, int index, string timeout)
        {
            var endpoint = target?.Parsed?.Endpoint ?? new JObject();
            var tunnel = new JObject
            {
                ["tag"] = target?.Tag ?? "tunnel-" + index,
                ["listen"] = "127.0.0.1:" + (target?.Port ?? 0),
                ["server"] = ServerOf(target, endpoint)
            };
            string bind = BindOf(target);
            if (bind.Length > 0) tunnel["bind"] = bind;

            bool psk = string.Equals(endpoint["auth"]?.ToString() ?? "", "psk", StringComparison.OrdinalIgnoreCase);
            var auth = new JObject { ["mode"] = psk ? "psk" : "eap" };
            if (psk)
            {
                string pskName = $"CRIMSONX_IPSEC_{index}_PSK";
                auth["psk"] = "env:" + pskName;
                plan.Environment[pskName] = endpoint["psk"]?.ToString() ?? "";
            }
            else
            {
                string userEnv = $"CRIMSONX_IPSEC_{index}_USER";
                string passEnv = $"CRIMSONX_IPSEC_{index}_PASS";
                auth["username"] = "env:" + userEnv;
                auth["password"] = "env:" + passEnv;
                plan.Environment[userEnv] = endpoint["username"]?.ToString() ?? "";
                plan.Environment[passEnv] = endpoint["password"]?.ToString() ?? "";
            }
            tunnel["auth"] = auth;

            Copy(endpoint, tunnel, "remote_id");
            Copy(endpoint, tunnel, "local_id");
            Copy(endpoint, tunnel, "dns");
            Copy(endpoint, tunnel, "rekey");
            Copy(endpoint, tunnel, "ike_rekey");
            Copy(endpoint, tunnel, "dpd");
            string ownTimeout = endpoint["timeout"]?.ToString() ?? "";
            string budget = ownTimeout.Trim().Length > 0 ? ownTimeout : (timeout ?? "");
            if (budget.Trim().Length > 0) tunnel["timeout"] = budget.Trim();
            int mtu = ToInt(endpoint["mtu"]);
            if (mtu > 0) tunnel["mtu"] = mtu;

            string ca = endpoint["ca"]?.ToString() ?? "";
            if (ca.Trim().Length > 0)
            {
                string caPath = Path.Combine(plan.WorkingDirectory, $"ca-{index}.pem");
                File.WriteAllText(caPath, ca.Trim() + Environment.NewLine, NoBom);
                tunnel["ca_file"] = caPath;
            }
            return tunnel;
        }

        private static string ServerOf(TunnelTarget target, JObject endpoint)
        {
            string host = (target?.ServerOverride ?? "").Trim();
            if (host.Length == 0) host = endpoint["server"]?.ToString() ?? "";
            if (host.Length == 0) return "";
            int port = ToInt(endpoint["server_port"]);
            if (port <= 0) port = 500;
            bool literalV6 = host.Contains(':') && !host.StartsWith("[", StringComparison.Ordinal);
            return literalV6 ? "[" + host + "]:" + port : host + ":" + port;
        }

        private static string BindOf(TunnelTarget target)
        {
            string name = (target?.AdapterName ?? "").Trim();
            if (name.Length == 0) return "";
            if (string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase)) return "";
            var binding = AdapterBindings.Resolve(name, target.AdapterIp ?? "");
            string source = AdapterBindings.SourceFor(binding, EndpointFamily.Ipv4);
            if (source.Length == 0) return "";
            if (string.Equals(binding.Name, TunProfile.InterfaceName, StringComparison.OrdinalIgnoreCase))
            {
                SimpleLogger.Log("[Tunnel] An IKEv2 tunnel cannot be pinned to the CrimsonX adapter; the default route is used instead.");
                return "";
            }
            return source;
        }

        private static void Copy(JObject source, JObject target, string key)
        {
            string value = source[key]?.ToString() ?? "";
            if (value.Trim().Length > 0) target[key] = value.Trim();
        }

        private static int ToInt(JToken token)
        {
            if (token == null) return 0;
            return int.TryParse(token.ToString().Trim(), out int parsed) ? parsed : 0;
        }

        private static string DirectoryName(string group)
        {
            string text = new string((group ?? "").Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
            return text.Length == 0 ? "custom-ipsec" : text + "-ipsec";
        }
    }
}

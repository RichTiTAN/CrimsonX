/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public static class WarpCommand
    {
        public static List<string> Build(WarpEndpoint endpoint, string? protocol)
        {
            var args = WarpProtocols.Args(protocol);
            args.AddRange(new[]
            {
                "--scan", "balanced",
                endpoint.Family == "v6" ? "-6" : "-4",
                "--quick-reconnect",
                "--bind", endpoint.SocksHostPort,
                "--config", "aether.toml",
                "--log-level", "info"
            });
            if (!string.IsNullOrWhiteSpace(endpoint.UpstreamUrl))
            {
                args.Add("--upstream");
                args.Add(endpoint.UpstreamUrl);
            }
            return args;
        }
    }

    public static class WarpPorts
    {
        public static void Assign(WarpPlan plan)
        {
            var used = new HashSet<int>();
            foreach (var endpoint in plan.Endpoints)
            {
                endpoint.SocksPort = Next(used);
                if (!endpoint.IsDefault) endpoint.ShimPort = Next(used);
            }
        }

        private static int Next(HashSet<int> used)
        {
            for (int attempt = 0; attempt < 64; attempt++)
            {
                TcpListener? listener = null;
                try
                {
                    listener = new TcpListener(IPAddress.Loopback, 0);
                    listener.Start();
                    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    if (port > 0 && port < 65535 && used.Add(port)) return port;
                }
                catch (Exception ex)
                {
                    SimpleLogger.Log(ex);
                }
                finally
                {
                    try { listener?.Stop(); } catch { }
                }
            }
            throw new InvalidOperationException("no free loopback port is available for WARP");
        }
    }

    public static class WarpShim
    {
        public static JObject BuildConfig(WarpPlan plan)
        {
            var inbounds = new JArray();
            var outbounds = new JArray();
            var rules = new JArray();
            foreach (var endpoint in plan.Endpoints)
            {
                if (endpoint.ShimPort <= 0 || endpoint.IsDefault) continue;
                string inboundTag = "warp-shim-in-" + endpoint.Tag;
                string outboundTag = "warp-shim-out-" + endpoint.Tag;
                inbounds.Add(new JObject
                {
                    ["tag"] = inboundTag,
                    ["listen"] = "127.0.0.1",
                    ["port"] = endpoint.ShimPort,
                    ["protocol"] = "socks",
                    ["settings"] = new JObject { ["auth"] = "noauth", ["udp"] = true }
                });
                outbounds.Add(new JObject
                {
                    ["tag"] = outboundTag,
                    ["protocol"] = "freedom",
                    ["settings"] = new JObject(),
                    ["sendThrough"] = endpoint.AdapterIp
                });
                rules.Add(new JObject
                {
                    ["type"] = "field",
                    ["inboundTag"] = new JArray(inboundTag),
                    ["outboundTag"] = outboundTag
                });
            }
            return new JObject
            {
                ["log"] = new JObject { ["loglevel"] = "warning" },
                ["inbounds"] = inbounds,
                ["outbounds"] = outbounds,
                ["routing"] = new JObject
                {
                    ["domainStrategy"] = "AsIs",
                    ["rules"] = rules
                }
            };
        }

        public static void Write(JObject document, string path)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, document.ToString(Formatting.Indented), new UTF8Encoding(false));
        }
    }
}

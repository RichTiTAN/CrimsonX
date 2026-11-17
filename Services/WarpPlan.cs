/*
 * CrimsonX - A GUI VPN client that fetches, tests and load-balances multiple xray configs suited for your network.
 * Copyright (C) 2026 RichTiTAN
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using CrimsonX.Models;

namespace CrimsonX.Services
{
    public static class WarpProtocols
    {
        public const string MasqueH2 = "masque-h2";
        public const string MasqueH3 = "masque-h3";
        public const string Wg = "wg";
        public const string Gool = "gool";
        public const string GoolClassic = "gool-classic";
        public const string Mim = "mim";

        public static readonly string[] All = { MasqueH2, MasqueH3, Wg, Gool, GoolClassic, Mim };

        public static string Normalise(string? value)
        {
            string text = (value ?? "").Trim().ToLowerInvariant();
            foreach (string protocol in All)
            {
                if (string.Equals(protocol, text, StringComparison.Ordinal)) return protocol;
            }
            return MasqueH2;
        }

        public static List<string> Args(string? value)
        {
            switch (Normalise(value))
            {
                case MasqueH3: return new List<string> { "--masque", "--h3" };
                case Wg: return new List<string> { "--wg" };
                case Gool: return new List<string> { "--gool" };
                case GoolClassic: return new List<string> { "--gool-classic" };
                case Mim: return new List<string> { "--mim" };
                default: return new List<string> { "--masque", "--h2" };
            }
        }
    }

    public sealed class WarpEndpoint
    {
        public string Tag { get; init; } = "";

        public string AdapterName { get; init; } = "";

        public string AdapterIp { get; init; } = "";

        public string Family { get; init; } = "v4";

        public string InstanceKey { get; init; } = "default";

        public int SocksPort { get; set; }

        public int ShimPort { get; set; }

        public string InstanceDir { get; set; } = "";

        public bool Started { get; set; }

        public bool IsDefault => AdapterIp.Length == 0;

        public string SocksHostPort => "127.0.0.1:" + SocksPort;

        public string ProxyUrl => "socks5://127.0.0.1:" + SocksPort;

        public string UpstreamUrl => ShimPort > 0 ? "socks5://127.0.0.1:" + ShimPort : "";

        public string Key => IsDefault ? "default" : AdapterIp + "|" + AdapterName;
    }

    public sealed class WarpPlan
    {
        public bool Active { get; init; }

        public string Protocol { get; init; } = WarpProtocols.MasqueH2;

        public List<WarpEndpoint> Endpoints { get; } = new();

        public List<WarpEndpoint> Copies { get; } = new();

        public List<WarpEndpoint> ConfigEndpoints { get; } = new();

        public List<WarpEndpoint> StartNow { get; } = new();

        public Dictionary<int, WarpEndpoint> Slots { get; } = new();

        public Dictionary<string, WarpEndpoint> Nodes { get; } = new(StringComparer.Ordinal);

        public WarpEndpoint? Default { get; set; }

        public string FallbackReason { get; set; } = "";

        public bool LoadBalanced => Copies.Count > 1;

        public WarpEndpoint? ForSlot(int slot)
            => Slots.TryGetValue(slot, out var endpoint) ? endpoint : null;

        public WarpEndpoint? ForAdapter(string? name, string? ip)
        {
            string address = (ip ?? "").Trim();
            if (address.Length == 0) return null;
            string adapter = (name ?? "").Trim();
            foreach (var endpoint in Endpoints)
            {
                if (!string.Equals(endpoint.AdapterIp, address, StringComparison.OrdinalIgnoreCase)) continue;
                if (adapter.Length == 0 || string.Equals(endpoint.AdapterName, adapter, StringComparison.OrdinalIgnoreCase))
                    return endpoint;
            }
            return null;
        }

        public string? TagForNode(string? json)
            => !string.IsNullOrWhiteSpace(json) && Nodes.TryGetValue(json!, out var endpoint) ? endpoint.Tag : null;

        public void RememberNode(string? json, WarpEndpoint? endpoint)
        {
            if (endpoint != null && !string.IsNullOrWhiteSpace(json)) Nodes[json!] = endpoint;
        }

        public static string Slug(string? text)
        {
            var builder = new System.Text.StringBuilder();
            foreach (char c in (text ?? "").Trim())
            {
                if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
                else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
            }
            string value = builder.ToString().Trim('-');
            return value.Length == 0 ? "default" : value;
        }
    }

    public static class WarpPlanBuilder
    {
        public static WarpPlan Build(
            AppConfig? config,
            Func<string, bool>? isAvailable = null,
            Func<string, string, AdapterBinding>? resolveAdapter = null)
        {
            string protocol = WarpProtocols.Normalise(config?.WarpProtocol);
            if (config == null || !config.EnableWarp)
                return new WarpPlan { Active = false, Protocol = protocol };

            var plan = new WarpPlan { Active = true, Protocol = protocol };
            var check = isAvailable ?? AdapterBindings.IsAddressAvailable;
            var resolve = resolveAdapter ?? AdapterBindings.Resolve;
            var byKey = new Dictionary<string, WarpEndpoint>(StringComparer.OrdinalIgnoreCase);
            int next = 0;

            WarpEndpoint NewDefault(string tag, string instanceKey)
            {
                var endpoint = new WarpEndpoint { Tag = tag, InstanceKey = instanceKey };
                plan.Endpoints.Add(endpoint);
                return endpoint;
            }

            WarpEndpoint DefaultEndpoint()
            {
                if (byKey.TryGetValue("default", out var hit)) return hit;
                var endpoint = NewDefault("cx-warp-default", "default");
                byKey["default"] = endpoint;
                return endpoint;
            }

            WarpEndpoint AdapterEndpoint(string tag, AdapterBinding binding, string instanceKey, bool dedupe = true)
            {
                string ip = binding.Ipv4.Length > 0 ? binding.Ipv4 : binding.Ipv6;
                string name = (binding.Name ?? "").Trim();
                if (ip.Length == 0 || string.Equals(name, TunProfile.InterfaceName, StringComparison.OrdinalIgnoreCase))
                    return dedupe ? DefaultEndpoint() : NewDefault(tag, instanceKey);
                string key = ip + "|" + name;
                if (dedupe && byKey.TryGetValue(key, out var hit)) return hit;
                var endpoint = new WarpEndpoint
                {
                    Tag = tag,
                    AdapterName = name,
                    AdapterIp = ip,
                    Family = ip.Contains(':') ? "v6" : "v4",
                    InstanceKey = instanceKey
                };
                if (dedupe) byKey[key] = endpoint;
                plan.Endpoints.Add(endpoint);
                return endpoint;
            }

            if (XrayConfigWriter.UsesPerConfigAdapters(config))
            {
                foreach (int slot in new[] { 1, 2 })
                {
                    string raw = slot == 1 ? config.CustomConfig1 : config.CustomConfig2;
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var (name, ip) = XrayConfigWriter.CustomConfigAdapter(config, slot);
                    WarpEndpoint endpoint;
                    if (ip.Length > 0 && check(ip))
                    {
                        endpoint = AdapterEndpoint(
                            "cx-warp-slot" + slot,
                            resolve(name, ip),
                            "slot" + slot,
                            dedupe: false);
                    }
                    else
                    {
                        if (ip.Length > 0) plan.FallbackReason = "adapter";
                        endpoint = NewDefault("cx-warp-slot" + slot, "slot" + slot);
                    }
                    plan.Slots[slot] = endpoint;
                    plan.StartNow.Add(endpoint);
                }
                if (plan.Slots.Count == 0)
                {
                    plan.Default = DefaultEndpoint();
                    plan.StartNow.Add(plan.Default);
                }
                else
                {
                    plan.Default = plan.Slots.Values.FirstOrDefault();
                }
                return plan;
            }

            if (config.EnableLoadBalanceAdapters)
            {
                foreach (string pick in config.LoadBalanceAdapters ?? new List<string>())
                {
                    var (name, ip) = XrayConfigWriter.SplitAdapter(pick);
                    if (ip.Length == 0 || !check(ip)) continue;
                    var binding = resolve(name, ip);
                    string address = binding.Ipv4.Length > 0 ? binding.Ipv4 : binding.Ipv6;
                    if (address.Length == 0 || string.Equals(binding.Name, TunProfile.InterfaceName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var endpoint = AdapterEndpoint("cx-warp-" + (++next), binding, "adapter-" + WarpPlan.Slug(address));
                    if (!plan.Copies.Contains(endpoint)) plan.Copies.Add(endpoint);
                }
                if (plan.Copies.Count > 0)
                {
                    plan.Default = plan.Copies[0];
                    plan.StartNow.AddRange(plan.Copies);
                    return plan;
                }
                plan.FallbackReason = "load-balance";
                plan.Default = DefaultEndpoint();
                plan.StartNow.Add(plan.Default);
                return plan;
            }

            AdapterBinding selectedBinding = AdapterBinding.Empty;
            if (config.EnableAdapterBinding && !string.IsNullOrWhiteSpace(config.SelectedAdapterIp))
            {
                string name = config.SelectedAdapterName ?? "";
                string ip = (config.SelectedAdapterIp ?? "").Trim();
                if (check(ip)) selectedBinding = resolve(name, ip);
                else plan.FallbackReason = "adapter";
            }

            for (int i = 0; i < 2; i++)
            {
                string tag = i == 0 ? "cx-warp-a" : "cx-warp-b";
                string instanceKey = i == 0 ? "config-a" : "config-b";
                var endpoint = selectedBinding.HasAny
                    ? AdapterEndpoint(tag, selectedBinding, instanceKey, dedupe: false)
                    : NewDefault(tag, instanceKey);
                plan.ConfigEndpoints.Add(endpoint);
            }
            if (selectedBinding.HasAny && plan.ConfigEndpoints.All(endpoint => endpoint.IsDefault))
                plan.FallbackReason = "adapter";
            plan.Default = plan.ConfigEndpoints[0];
            plan.StartNow.Add(plan.Default);
            return plan;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using CrimsonX.Models;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    internal static class TunnelHostPruner
    {
        private static readonly TimeSpan Throttle = TimeSpan.FromSeconds(60);

        private static DateTime _lastSweptUtc = DateTime.MinValue;

        public static int Sweep(AppConfig cfg, IEnumerable<AppGameRule> rules, bool force = false)
        {
            if (cfg == null || !ConfigService.ConfigLoaded) return 0;
            if (!force && DateTime.UtcNow - _lastSweptUtc < Throttle) return 0;
            _lastSweptUtc = DateTime.UtcNow;
            int removed = 0;
            try
            {
                removed = TunnelHostMemory.PruneTo(LiveHosts(cfg, rules));
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
            if (removed > 0)
                SimpleLogger.Log($"[DNS] Forgot {removed} remembered hostname(s) whose config is gone.");
            return removed;
        }

        public static int SweepFromApp() => Sweep(MainWindow.Instance?.Config, SafeRules());

        internal static HashSet<string> LiveHosts(AppConfig cfg, IEnumerable<AppGameRule> rules)
        {
            var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (cfg != null)
            {
                Add(hosts, cfg.CustomConfig1);
                Add(hosts, cfg.CustomConfig2);
                Add(hosts, cfg.V2rayChainJson);
                Add(hosts, cfg.ExitNodeCustomJson);
                try
                {
                    foreach (var entry in AppCustomConfigStore.Snapshot(cfg))
                    {
                        if (entry != null) Add(hosts, entry.Raw);
                    }
                }
                catch (Exception ex)
                {
                    SimpleLogger.Log(ex);
                }
            }
            if (rules != null)
            {
                foreach (var rule in rules)
                {
                    if (rule == null) continue;
                    Add(hosts, rule.CustomProxyRaw);
                }
            }
            try
            {
                foreach (var choice in AppOptimizeStore.Load().Choices)
                {
                    if (choice == null) continue;
                    Add(hosts, choice.PrevCustomProxyRaw);
                    Add(hosts, choice.AppliedRaw);
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
            foreach (string raw in TunnelDrafts.All) Add(hosts, raw);
            return hosts;
        }

        private static void Add(HashSet<string> hosts, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            try
            {
                if (!TunnelConfigParser.TryParse(raw.Trim(), out var tunnel) || tunnel == null || !tunnel.Success) return;
                foreach (string host in HostsOf(tunnel.Endpoint)) hosts.Add(host);
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
        }

        internal static IEnumerable<string> HostsOf(JObject endpoint)
        {
            var hosts = new List<string>();
            if (endpoint == null) return hosts;
            void Push(string value)
            {
                string text = (value ?? "").Trim();
                if (text.Length == 0) return;
                if (System.Net.IPAddress.TryParse(text, out _)) return;
                if (!hosts.Contains(text)) hosts.Add(text);
            }
            Push(endpoint["server"]?.ToString());
            if (endpoint["servers"] is JArray servers)
            {
                foreach (var server in servers.OfType<JObject>()) Push(server["server"]?.ToString());
            }
            if (endpoint["peers"] is JArray peers)
            {
                foreach (var peer in peers.OfType<JObject>()) Push(peer["address"]?.ToString());
            }
            return hosts;
        }

        private static IEnumerable<AppGameRule> SafeRules()
        {
            try { return AppRulesService.Load() ?? new List<AppGameRule>(); }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                return new List<AppGameRule>();
            }
        }
    }

    internal static class TunnelDrafts
    {
        private static readonly object Sync = new object();

        private static string _first = "";

        private static string _second = "";

        private static string _exitNode = "";

        internal static IEnumerable<string> All
        {
            get
            {
                lock (Sync) return new[] { _first, _second, _exitNode };
            }
        }

        internal static void SetSlot(int slot, string raw)
        {
            lock (Sync)
            {
                if (slot == 2) _second = raw ?? "";
                else _first = raw ?? "";
            }
        }

        internal static void SetExitNode(string raw)
        {
            lock (Sync) _exitNode = raw ?? "";
        }
    }
}
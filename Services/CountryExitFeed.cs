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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CrimsonX.Models;

namespace CrimsonX.Services
{
    public sealed class CountryNode
    {
        public string Code { get; set; } = "";

        public string Link { get; set; } = "";
    }

    public static class CountryExitFeed
    {
        public const int SourceIndex = 8;

        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);

        private static readonly TimeSpan FetchBudget = TimeSpan.FromSeconds(8);

        private static List<CountryNode> _nodes = new List<CountryNode>();

        public static IReadOnlyList<CountryNode> Nodes => _nodes;

        public static bool Loaded { get; private set; }

        public static string LastError { get; private set; } = "";

        public static DateTime LastFetchUtc { get; private set; } = DateTime.MinValue;

        public static event Action? Changed;

        public static CountryNode? NodeFor(string? code)
        {
            string wanted = (code ?? "").Trim();
            if (wanted.Length == 0) return null;
            return _nodes.FirstOrDefault(n => string.Equals(n.Code, wanted, StringComparison.OrdinalIgnoreCase));
        }

        internal static void SeedForTests(IEnumerable<CountryNode> nodes)
        {
            _nodes = nodes?.ToList() ?? new List<CountryNode>();
            Loaded = true;
            LastError = "";
            LastFetchUtc = DateTime.UtcNow;
        }

        internal static void ResetForTests()
        {
            _nodes = new List<CountryNode>();
            Loaded = false;
            LastFetchUtc = DateTime.MinValue;
        }

        public static string DisplayName(CountryNode? node)
            => node == null ? "" : CrimsonX.Localization.GeoTranslation.GetCountryEn(node.Code, node.Code);

        public static HashSet<string>? AllowedContinents(AppConfig? cfg)
        {
            if (cfg == null || !cfg.EnableExcludedContinents) return null;
            var excluded = cfg.ExcludedContinents ?? new List<string>();
            string[] names = CrimsonX.Localization.CountryContinents.Names;
            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in names)
                if (!excluded.Contains(name)) keep.Add(name);
            if (keep.Count == 0 || keep.Count == names.Length) return null;
            return keep;
        }

        public static bool IsVisible(CountryNode? node, HashSet<string>? allowed)
        {
            if (allowed == null || node == null) return true;
            string code = (node.Code ?? "").Trim().ToUpperInvariant();
            if (!CrimsonX.Localization.CountryContinents.Map.TryGetValue(code, out string? continent)) return true;
            return allowed.Contains(continent);
        }

        public static List<CountryNode> Parse(string? content)
        {
            var byCode = new Dictionary<string, CountryNode>(StringComparer.OrdinalIgnoreCase);
            foreach (string link in XrayLinkParser.ExtractConfigs(content ?? ""))
            {
                string fragment = XrayLinkParser.LinkFragment(link);
                string code = CountryCodeFrom(fragment);
                if (code.Length != 2) continue;
                byCode[code] = new CountryNode { Code = code.ToUpperInvariant(), Link = link };
            }
            return byCode.Values
                .OrderBy(n => DisplayName(n), StringComparer.OrdinalIgnoreCase)
                .ThenBy(n => n.Code, StringComparer.Ordinal)
                .ToList();
        }

        public static string CountryCodeFrom(string? fragment)
        {
            if (string.IsNullOrWhiteSpace(fragment)) return "";
            int bar = fragment.IndexOf('|');
            string head = (bar >= 0 ? fragment.Substring(0, bar) : fragment).Trim();
            if (head.Length != 2) return "";
            char a = head[0], b = head[1];
            if (!char.IsLetter(a) || !char.IsLetter(b)) return "";
            return new string(new[] { char.ToUpperInvariant(a), char.ToUpperInvariant(b) });
        }

        public static async Task<bool> RefreshAsync(AppConfig? cfg, CancellationToken ct = default)
        {
            var nodes = await FetchFromWorkersAsync(cfg, ct).ConfigureAwait(false);
            if (nodes == null || nodes.Count == 0)
            {
                LastError = CrimsonX.Localization.AppStrings.ExitNodeSourceUnavailable;
                return false;
            }
            _nodes = nodes;
            Loaded = true;
            LastError = "";
            LastFetchUtc = DateTime.UtcNow;
            SimpleLogger.Log($"[ExitFeed] {nodes.Count} countries offered by source {SourceIndex}.");
            Changed?.Invoke();
            return true;
        }

        public static async Task<bool> EnsureFreshAsync(AppConfig? cfg, CancellationToken ct = default)
        {
            if (Loaded && DateTime.UtcNow - LastFetchUtc < StaleAfter) return true;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(FetchBudget);
            try
            {
                return await RefreshAsync(cfg, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch { return Loaded; }
        }

        public static bool SelectCountry(AppConfig? cfg, CountryNode? node)
        {
            if (cfg == null || node == null) return false;
            if (!string.Equals(cfg.ExitNodeSource, "DEFAULT", StringComparison.OrdinalIgnoreCase))
            {
                cfg.ExitNodeCustomJson = cfg.V2rayChainJson ?? "";
                cfg.ExitNodeCustomEnabled = cfg.EnableV2rayChain;
            }
            cfg.ExitNodeSource = "DEFAULT";
            cfg.ExitNodeCountry = node.Code;
            cfg.V2rayChainJson = node.Link;
            cfg.EnableV2rayChain = true;
            cfg.ExitNodeSuspended = false;
            cfg.ExitNodeAuto = false;
            return true;
        }

        public static bool SelectCustom(AppConfig? cfg, bool arm = false)
        {
            if (cfg == null) return false;
            string restored = cfg.ExitNodeCustomJson ?? "";
            bool armed = arm ? !string.IsNullOrWhiteSpace(restored) : cfg.ExitNodeCustomEnabled && !string.IsNullOrWhiteSpace(restored);
            bool changed = !string.Equals(cfg.ExitNodeSource, "CUSTOM", StringComparison.OrdinalIgnoreCase)
                           || !string.Equals(cfg.V2rayChainJson ?? "", restored, StringComparison.Ordinal)
                           || cfg.EnableV2rayChain != armed
                           || cfg.ExitNodeAuto;
            cfg.ExitNodeSource = "CUSTOM";
            cfg.ExitNodeSuspended = false;
            cfg.ExitNodeAuto = false;
            cfg.V2rayChainJson = restored;
            cfg.EnableV2rayChain = armed;
            if (arm) cfg.ExitNodeCustomEnabled = armed;
            return changed;
        }

        public static bool SelectAuto(AppConfig? cfg)
        {
            if (cfg == null) return false;
            bool changed = !cfg.ExitNodeAuto || cfg.EnableV2rayChain || cfg.ExitNodeSuspended;
            cfg.ExitNodeAuto = true;
            cfg.EnableV2rayChain = false;
            cfg.ExitNodeSuspended = false;
            return changed;
        }

        public static bool CustomExitAvailable(AppConfig? cfg)
        {
            if (cfg == null) return false;
            string json = string.Equals(cfg.ExitNodeSource, "CUSTOM", StringComparison.OrdinalIgnoreCase)
                ? cfg.V2rayChainJson
                : cfg.ExitNodeCustomJson;
            return !string.IsNullOrWhiteSpace(json);
        }

        public static bool Reconcile(AppConfig? cfg)
        {
            if (cfg == null) return false;
            if (cfg.ExitNodeAuto) return false;
            if (!string.Equals(cfg.ExitNodeSource, "DEFAULT", StringComparison.OrdinalIgnoreCase)) return false;
            if (!Loaded) return false;
            string code = (cfg.ExitNodeCountry ?? "").Trim().ToUpperInvariant();
            if (code.Length == 0) return false;
            var node = NodeFor(code);
            if (node == null)
            {
                if (!cfg.EnableV2rayChain && !cfg.ExitNodeSuspended) return false;
                cfg.EnableV2rayChain = false;
                cfg.ExitNodeSuspended = true;
                return true;
            }
            string want = (node.Link ?? "").Trim();
            bool changed = false;
            if (!string.Equals((cfg.V2rayChainJson ?? "").Trim(), want, StringComparison.Ordinal))
            {
                cfg.V2rayChainJson = want;
                changed = true;
            }
            if (cfg.ExitNodeSuspended || !cfg.EnableV2rayChain)
            {
                cfg.EnableV2rayChain = true;
                cfg.ExitNodeSuspended = false;
                changed = true;
            }
            return changed;
        }

        private static async Task<List<CountryNode>?> FetchFromWorkersAsync(AppConfig? cfg, CancellationToken ct)
        {
            string[] workers = AppSecrets.WorkerUrls;
            foreach (string worker in workers)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    string sha = await FetchShaAsync(worker, ct).ConfigureAwait(false);
                    string shaPath = CachePath(cfg, $"worker_sha_{SourceIndex}.bin");
                    string dataPath = CachePath(cfg, $"worker_data_{SourceIndex}.bin");
                    string? content = null;
                    if (sha.Length > 0 && string.Equals(ConfigCache.LoadString(shaPath), sha, StringComparison.Ordinal))
                        content = ConfigCache.LoadString(dataPath);
                    if (string.IsNullOrEmpty(content))
                    {
                        content = await FetchDataAsync(worker, ct).ConfigureAwait(false);
                        if (string.IsNullOrEmpty(content)) continue;
                        if (sha.Length > 0)
                        {
                            ConfigCache.SaveString(shaPath, sha);
                            ConfigCache.SaveString(dataPath, content);
                        }
                    }
                    var nodes = Parse(content);
                    if (nodes.Count > 0) return nodes;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    SimpleLogger.Log($"[ExitFeed] source {SourceIndex} on {worker} failed: {ex.Message}");
                }
            }
            return null;
        }

        private static async Task<string> FetchShaAsync(string worker, CancellationToken ct)
        {
            string url = $"{worker}/api/{SourceIndex}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("CrimsonX-App/1.0");
            using var resp = await Client.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return "";
            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            try { return Newtonsoft.Json.Linq.JObject.Parse(json)["sha"]?.ToString() ?? ""; }
            catch { return ""; }
        }

        private static async Task<string> FetchDataAsync(string worker, CancellationToken ct)
        {
            string url = $"{worker}/{SourceIndex}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("CrimsonX-App/1.0");
            using var resp = await Client.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return "";
            return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }

        private static string CachePath(AppConfig? cfg, string file)
        {
            string baseDir = cfg?.BaseDir ?? "";
            if (baseDir.Length == 0) baseDir = AppContext.BaseDirectory;
            return Path.Combine(baseDir, "Data", "cache", file);
        }
    }
}

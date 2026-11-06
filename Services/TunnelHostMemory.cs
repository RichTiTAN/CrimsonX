using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CrimsonX.Services
{
    internal static class TunnelHostMemory
    {
        internal sealed class Entry
        {
            public string[] Addresses { get; set; } = Array.Empty<string>();

            public DateTime SavedUtc { get; set; }

            public DateTime LastOkUtc { get; set; }

            public int Failures { get; set; }
        }

        private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);

        private const int MaxFailures = 3;

        private const int MaxHosts = 256;

        private static readonly object Sync = new object();

        private static Dictionary<string, Entry> _map;

        internal static Func<DateTime> Clock = () => DateTime.UtcNow;

        internal static string PathOverride = "";

        private static string StorePath
            => PathOverride.Length > 0 ? PathOverride : SecureJsonStore.PathFor("tunnel_hosts.bin");

        private static string KeyOf(string host) => (host ?? "").Trim().ToLowerInvariant();

        internal static IReadOnlyList<string> Remembered(string host)
        {
            string key = KeyOf(host);
            if (key.Length == 0) return Array.Empty<string>();
            lock (Sync)
            {
                if (!Map().TryGetValue(key, out var entry)) return Array.Empty<string>();
                if (IsStale(entry)) return Array.Empty<string>();
                return entry.Addresses ?? Array.Empty<string>();
            }
        }

        internal static void MarkSucceeded(string host)
        {
            string key = KeyOf(host);
            if (key.Length == 0) return;
            lock (Sync)
            {
                if (!Map().TryGetValue(key, out var entry)) return;
                entry.LastOkUtc = Clock();
                entry.Failures = 0;
                Save();
            }
        }

        internal static void MarkFailed(string host)
        {
            string key = KeyOf(host);
            if (key.Length == 0) return;
            lock (Sync)
            {
                var map = Map();
                if (!map.TryGetValue(key, out var entry)) return;
                entry.Failures++;
                if (entry.Failures >= MaxFailures) map.Remove(key);
                Save();
            }
        }

        internal static void Remember(string host, IReadOnlyList<string> addresses)
        {
            string key = KeyOf(host);
            if (key.Length == 0 || addresses == null) return;
            var clean = addresses.Where(IsPublic).Distinct(StringComparer.Ordinal).ToArray();
            if (clean.Length == 0) return;
            lock (Sync)
            {
                var map = Map();
                bool changed = false;
                if (map.TryGetValue(key, out var entry))
                {
                    if (entry.Addresses == null || !entry.Addresses.SequenceEqual(clean, StringComparer.Ordinal))
                    {
                        entry.Addresses = clean;
                        changed = true;
                    }
                    if (entry.Failures != 0)
                    {
                        entry.Failures = 0;
                        changed = true;
                    }
                    if ((Clock() - entry.SavedUtc).TotalMinutes >= 1)
                    {
                        entry.SavedUtc = Clock();
                        changed = true;
                    }
                }
                else
                {
                    map[key] = new Entry { Addresses = clean, SavedUtc = Clock() };
                    changed = true;
                }
                if (!changed) return;
                Prune(map);
                Save();
            }
        }

        internal static int PruneTo(IEnumerable<string> hosts)
        {
            lock (Sync)
            {
                var map = Map();
                if (map.Count == 0) return 0;
                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string host in hosts ?? Enumerable.Empty<string>())
                {
                    string key = KeyOf(host);
                    if (key.Length > 0) keep.Add(key);
                }
                if (keep.Count == 0) return 0;
                var drop = map.Keys.Where(key => !keep.Contains(key)).ToList();
                if (drop.Count == 0) return 0;
                foreach (string key in drop) map.Remove(key);
                Save();
                return drop.Count;
            }
        }

        internal static void Clear()
        {
            lock (Sync)
            {
                _map = null;
                try
                {
                    string path = StorePath;
                    if (path.Length > 0 && File.Exists(path)) File.Delete(path);
                }
                catch (Exception ex)
                {
                    SimpleLogger.Log(ex);
                }
            }
        }

        internal static void Reload()
        {
            lock (Sync) _map = null;
        }

        private static Dictionary<string, Entry> Map()
        {
            if (_map != null) return _map;
            _map = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = StorePath;
                if (path.Length == 0 || !File.Exists(path)) return _map;
                var loaded = SecureJsonStore.Load<Dictionary<string, Entry>>(path);
                if (loaded != null) _map = loaded;
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
            return _map;
        }

        private static void Save()
        {
            try
            {
                string path = StorePath;
                if (path.Length == 0) return;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (!SecureJsonStore.Save(path, _map))
                    SimpleLogger.Log("[DNS] the remembered host addresses could not be saved");
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
        }

        private static void Prune(Dictionary<string, Entry> map)
        {
            foreach (string stale in map.Where(pair => IsStale(pair.Value)).Select(pair => pair.Key).ToList())
            {
                map.Remove(stale);
            }
            if (map.Count <= MaxHosts) return;
            foreach (string oldest in map.OrderByDescending(pair => pair.Value.SavedUtc)
                                         .Skip(MaxHosts)
                                         .Select(pair => pair.Key)
                                         .ToList())
            {
                map.Remove(oldest);
            }
        }

        private static bool IsStale(Entry entry) => entry == null || Clock() - entry.SavedUtc > Ttl;

        private static bool IsPublic(string address)
            => !string.IsNullOrWhiteSpace(address) && !BootstrapDns.IsPrivateAddress(address.Trim());
    }
}
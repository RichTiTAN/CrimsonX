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
using System.Threading.Tasks;
using Avalonia.Threading;
using Newtonsoft.Json;

namespace CrimsonX.Services
{
    public sealed class TunnelCredential
    {
        public string User { get; set; } = "";

        public string Password { get; set; } = "";

        public bool Remember { get; set; }
    }

    public static class TunnelCredentialStore
    {
        private static readonly object Sync = new object();
        private static Dictionary<string, TunnelCredential> _cache;
        private static DateTime _stampUtc;
        private static long _length;

        private static string StorePath()
        {
            return SecureJsonStore.PathFor("tunnel_credentials.bin");
        }

        private static string LegacyStorePath()
        {
            var baseDir = MainWindow.Instance?.GetAppPath("Data\\Apps") ?? "Data\\Apps";
            return Path.Combine(baseDir, "tunnel_credentials.json");
        }

        public static TunnelCredential Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            lock (Sync)
            {
                var map = LoadCore();
                return map.TryGetValue(key, out var credential) ? credential : null;
            }
        }

        public static void Remember(string key, string user, string password)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            lock (Sync)
            {
                var map = LoadCore();
                map[key] = new TunnelCredential { User = user ?? "", Password = password ?? "", Remember = true };
                SaveCore(map);
            }
        }

        public static void Forget(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            lock (Sync)
            {
                var map = LoadCore();
                if (map.Remove(key)) SaveCore(map);
            }
        }

        private static Dictionary<string, TunnelCredential> LoadCore()
        {
            var path = StorePath();
            SecureJsonStore.AdoptPlaintextFile(LegacyStorePath(), path);
            try
            {
                if (!File.Exists(path))
                {
                    _cache = new Dictionary<string, TunnelCredential>(StringComparer.Ordinal);
                    return _cache;
                }
                var info = new FileInfo(path);
                if (_cache != null && info.LastWriteTimeUtc == _stampUtc && info.Length == _length) return _cache;
                var loaded = SecureJsonStore.Load<Dictionary<string, TunnelCredential>>(path)
                             ?? new Dictionary<string, TunnelCredential>(StringComparer.Ordinal);
                _cache = new Dictionary<string, TunnelCredential>(loaded, StringComparer.Ordinal);
                _stampUtc = info.LastWriteTimeUtc;
                _length = info.Length;
                return _cache;
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                _cache = new Dictionary<string, TunnelCredential>(StringComparer.Ordinal);
                return _cache;
            }
        }

        private static void SaveCore(Dictionary<string, TunnelCredential> map)
        {
            try
            {
                var path = StorePath();
                SecureJsonStore.Save(path, map);
                var info = new FileInfo(path);
                _stampUtc = info.LastWriteTimeUtc;
                _length = info.Length;
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
        }
    }

    public static class TunnelCredentialResolver
    {
        public static bool ApplyStored(TunnelParseResult tunnel)
        {
            if (tunnel == null || tunnel.Endpoint == null) return false;
            if (!NeedsCredentials(tunnel)) return true;
            string key = TunnelConfigParser.Normalize(tunnel.Raw);
            var stored = TunnelCredentialStore.Get(key);
            if (stored == null || stored.User.Length == 0) return false;
            TunnelConfigParser.WithCredentials(tunnel.Endpoint, stored.User, stored.Password);
            return true;
        }

        public static bool NeedsCredentials(TunnelParseResult tunnel)
        {
            if (tunnel == null || tunnel.Endpoint == null) return false;
            string type = tunnel.Endpoint["type"]?.ToString() ?? "";
            if (!type.Equals("openvpn-client", StringComparison.OrdinalIgnoreCase)) return false;
            if (!tunnel.NeedsCredentials) return false;
            return (tunnel.Endpoint["username"]?.ToString() ?? "").Length == 0;
        }

        public static async Task<bool> ApplyAsync(TunnelParseResult tunnel)
        {
            if (tunnel == null || tunnel.Endpoint == null) return false;
            string type = tunnel.Endpoint["type"]?.ToString() ?? "";
            if (!type.Equals("openvpn-client", StringComparison.OrdinalIgnoreCase)) return true;
            string user = tunnel.Endpoint["username"]?.ToString() ?? "";
            string password = tunnel.Endpoint["password"]?.ToString() ?? "";
            if (user.Length > 0 || !tunnel.NeedsCredentials) return true;
            string key = TunnelConfigParser.Normalize(tunnel.Raw);
            var stored = TunnelCredentialStore.Get(key);
            if (stored != null && stored.User.Length > 0)
            {
                TunnelConfigParser.WithCredentials(tunnel.Endpoint, stored.User, stored.Password);
                return true;
            }
            var entered = await TunnelCredentialPrompt.AskAsync(tunnel.Label, user, password);
            if (entered == null || entered.User.Length == 0) return false;
            TunnelConfigParser.WithCredentials(tunnel.Endpoint, entered.User, entered.Password);
            if (entered.Remember) TunnelCredentialStore.Remember(key, entered.User, entered.Password);
            return true;
        }
    }

    public static class TunnelCredentialPrompt
    {
        public static Task<TunnelCredential> AskAsync(string label, string user, string password)
        {
            var owner = MainWindow.Instance;
            if (owner == null) return Task.FromResult<TunnelCredential>(null);
            return Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dialog = new CrimsonX.Dialogs.TunnelCredentialsDialog(label, user, password);
                return await dialog.ShowDialog<TunnelCredential>(owner);
            });
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CrimsonX.Services
{
    public static class TunnelResolver
    {
        internal sealed class Result
        {
            public IReadOnlyList<string> Addresses { get; init; } = Array.Empty<string>();

            public string Layer { get; init; } = "";

            public bool Poisoned { get; set; }
        }

        private sealed class Entry
        {
            public Result Value = new Result();

            public DateTime ExpiresUtc;
        }

        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

        private static readonly TimeSpan EmptyTtl = TimeSpan.FromSeconds(30);

        private static readonly TimeSpan LayerCooldown = TimeSpan.FromMinutes(5);

        private static readonly int QueryTimeoutMs = 3000;

        private static readonly string[] PlainServers =
        {
            "1.1.1.1", "1.0.0.1", "8.8.8.8", "8.8.4.4", "9.9.9.9", "94.140.14.14", "208.67.222.222"
        };

        private static readonly Dictionary<string, Entry> Cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, DateTime> DeadLayers = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private static readonly object Sync = new object();

        internal static Func<string, string, IReadOnlyList<string>> Doh { get; set; } = BootstrapDns.Resolve;

        internal static Func<string, string, IReadOnlyList<string>> Plain { get; set; } = PlainDns;

        internal static Func<string, string, IReadOnlyList<string>> System { get; set; } = SystemDns;

        public static string SourceOf(TunnelTarget target)
            => target == null ? "" : SourceFor(target.AdapterName, target.AdapterIp);

        public static string SourceOf(Models.AppConfig config)
        {
            if (config == null || !config.EnableAdapterBinding) return "";
            return SourceFor(config.SelectedAdapterName, config.SelectedAdapterIp);
        }

        public static string SourceFor(string adapterName, string adapterIp)
        {
            var binding = AdapterBindings.Resolve(adapterName ?? "", adapterIp ?? "");
            return AdapterBindings.SourceFor(binding, EndpointFamily.Ipv4);
        }

        public static IReadOnlyList<string> Resolve(string host, string sourceAddress)
            => ResolveDetailed(host, sourceAddress).Addresses;

        internal static Result ResolveDetailed(string host, string sourceAddress)
        {
            if (string.IsNullOrWhiteSpace(host)) return new Result();
            string name = host.Trim();
            string source = (sourceAddress ?? "").Trim();
            if (IPAddress.TryParse(name, out _))
                return new Result { Addresses = new[] { name }, Layer = "literal" };

            string key = name + "@" + source;
            lock (Sync)
            {
                if (Cache.TryGetValue(key, out var cached) && cached.ExpiresUtc > DateTime.UtcNow) return cached.Value;
            }

            bool poisoned = false;
            var failed = new List<string>();
            var layers = new (string Name, Func<string, string, IReadOnlyList<string>> Run)[]
            {
                ("DoH", Doh),
                ("plain DNS", Plain),
                ("system DNS", System)
            };
            foreach (var layer in layers)
            {
                if (layer.Run == null) continue;
                if (IsDead(source, layer.Name)) continue;
                var answers = PublicAnswers(layer.Run(name, source), ref poisoned);
                if (answers.Count == 0)
                {
                    failed.Add(layer.Name);
                    continue;
                }
                foreach (string dead in failed) MarkDead(source, dead);
                var hit = new Result { Addresses = answers, Layer = layer.Name, Poisoned = poisoned };
                Store(key, hit);
                TunnelHostMemory.Remember(name, answers);
                TunnelHostPruner.SweepFromApp();
                SimpleLogger.Log(
                    $"[DNS] {name} via {layer.Name}{(source.Length > 0 ? " through " + source : "")} -> {string.Join(", ", answers)}");
                return hit;
            }

            var remembered = TunnelHostMemory.Remembered(name);
            if (remembered.Count > 0)
            {
                var hit = new Result { Addresses = remembered, Layer = "remembered", Poisoned = poisoned };
                Store(key, hit);
                SimpleLogger.Log(
                    $"[DNS] {name} used the remembered address {string.Join(", ", remembered)}" +
                    (poisoned ? " because every resolver answered with a private address" : ""));
                return hit;
            }
            if (poisoned) TunnelHostMemory.MarkFailed(name);

            var miss = new Result { Addresses = Array.Empty<string>(), Layer = "", Poisoned = poisoned };
            Store(key, miss);
            if (poisoned)
                SimpleLogger.Log(
                    $"[DNS] {name} only answered with a private address; the network is rewriting DNS - put the server IP in the profile.");
            else
                SimpleLogger.Log($"[DNS] {name} could not be resolved by DoH, plain DNS or the system resolver.");
            return miss;
        }

        internal static void Clear()
        {
            lock (Sync)
            {
                Cache.Clear();
                DeadLayers.Clear();
            }
        }

        private static List<string> PublicAnswers(IReadOnlyList<string> answers, ref bool poisoned)
        {
            var list = new List<string>();
            if (answers == null) return list;
            foreach (string answer in answers)
            {
                if (string.IsNullOrWhiteSpace(answer)) continue;
                string text = answer.Trim();
                if (!IPAddress.TryParse(text, out var address)) continue;
                if (address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (BootstrapDns.IsPrivateAddress(text))
                {
                    poisoned = true;
                    continue;
                }
                if (!list.Contains(text)) list.Add(text);
            }
            return list;
        }

        private static bool IsDead(string source, string layer)
        {
            lock (Sync)
            {
                return DeadLayers.TryGetValue(source + "|" + layer, out var until) && until > DateTime.UtcNow;
            }
        }

        private static void MarkDead(string source, string layer)
        {
            if (string.Equals(layer, "system DNS", StringComparison.Ordinal)) return;
            lock (Sync) DeadLayers[source + "|" + layer] = DateTime.UtcNow + LayerCooldown;
        }

        private static void Store(string key, Result value)
        {
            var span = value.Addresses.Count > 0 ? Ttl : EmptyTtl;
            lock (Sync) Cache[key] = new Entry { Value = value, ExpiresUtc = DateTime.UtcNow + span };
        }

        private static IReadOnlyList<string> PlainDns(string host, string sourceAddress)
        {
            foreach (string server in PlainServers)
            {
                var answers = Query(host, server, sourceAddress);
                if (answers.Count > 0) return answers;
            }
            return Array.Empty<string>();
        }

        private static List<string> Query(string host, string server, string sourceAddress)
        {
            var result = new List<string>();
            Socket socket = null;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Bind(new IPEndPoint(sourceAddress.Length > 0 ? IPAddress.Parse(sourceAddress) : IPAddress.Any, 0));
                socket.ReceiveTimeout = QueryTimeoutMs;
                socket.SendTo(BuildQuery(host), new IPEndPoint(IPAddress.Parse(server), 53));
                var buffer = new byte[1500];
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int read = socket.ReceiveFrom(buffer, ref from);
                ParseAnswers(buffer, read, result);
                SimpleLogger.Log($"[DNS] plain DNS {server} answered {host} with {(result.Count > 0 ? string.Join(", ", result) : "nothing")}");
            }
            catch (Exception ex)
            {
                SimpleLogger.Log($"[DNS] plain DNS {server} could not answer for {host}: {ex.Message}");
            }
            finally
            {
                try { socket?.Dispose(); } catch { }
            }
            return result;
        }

        private static byte[] BuildQuery(string host)
        {
            var buffer = new List<byte> { 0x13, 0x37, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            foreach (string label in host.Split('.'))
            {
                byte[] bytes = Encoding.ASCII.GetBytes(label);
                if (bytes.Length == 0 || bytes.Length > 63) continue;
                buffer.Add((byte)bytes.Length);
                buffer.AddRange(bytes);
            }
            buffer.Add(0x00);
            buffer.Add(0x00);
            buffer.Add(0x01);
            buffer.Add(0x00);
            buffer.Add(0x01);
            return buffer.ToArray();
        }

        private static void ParseAnswers(byte[] buffer, int length, List<string> into)
        {
            if (length < 12) return;
            int questions = (buffer[4] << 8) | buffer[5];
            int answers = (buffer[6] << 8) | buffer[7];
            int offset = 12;
            for (int i = 0; i < questions && offset < length; i++)
            {
                offset = SkipName(buffer, offset, length) + 4;
            }
            for (int i = 0; i < answers && offset + 10 <= length; i++)
            {
                offset = SkipName(buffer, offset, length);
                if (offset + 10 > length) return;
                int type = (buffer[offset] << 8) | buffer[offset + 1];
                int size = (buffer[offset + 8] << 8) | buffer[offset + 9];
                offset += 10;
                if (offset + size > length) return;
                if (type == 1 && size == 4)
                {
                    string text = $"{buffer[offset]}.{buffer[offset + 1]}.{buffer[offset + 2]}.{buffer[offset + 3]}";
                    if (!into.Contains(text)) into.Add(text);
                }
                offset += size;
            }
        }

        private static int SkipName(byte[] buffer, int offset, int length)
        {
            while (offset < length)
            {
                byte label = buffer[offset];
                if (label == 0) return offset + 1;
                if ((label & 0xC0) == 0xC0) return offset + 2;
                offset += label + 1;
            }
            return length;
        }

        private static IReadOnlyList<string> SystemDns(string host, string sourceAddress)
        {
            try
            {
                return Dns.GetHostAddresses(host)
                    .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => address.ToString())
                    .ToArray();
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                return Array.Empty<string>();
            }
        }
    }
}
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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using CrimsonX.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public sealed class TunnelTarget
    {
        public string Key { get; set; } = "";

        public TunnelParseResult Parsed { get; set; }

        public string Tag { get; set; } = "";

        public int Port { get; set; }

        public string AdapterName { get; set; } = "";

        public string AdapterIp { get; set; } = "";
    }

    public enum TunnelTestState
    {
        NotATunnel,

        Ready,

        Failed
    }

    public sealed class TunnelTestPrep
    {
        public TunnelTestState State { get; set; } = TunnelTestState.NotATunnel;

        public string OutboundJson { get; set; } = "";

        public IDisposable Lease { get; set; }
    }

    public static class TunnelEngine
    {
        public const string GroupCustom = "custom";

        public const string GroupRules = "rules";

        private const int MaxAttempts = 2;
        private const int ReadinessTimeoutMs = 4500;

        private sealed class GroupState
        {
            public Process Process;
            public string Dir = "";
            public string Signature = "";
            public readonly Dictionary<string, TunnelTarget> Active = new Dictionary<string, TunnelTarget>(StringComparer.Ordinal);
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, GroupState> Groups = new Dictionary<string, GroupState>(StringComparer.OrdinalIgnoreCase);

        private static int _probeSweepDone;

        private static GroupState State(string group)
        {
            string name = string.IsNullOrWhiteSpace(group) ? GroupCustom : group.Trim();
            if (!Groups.TryGetValue(name, out var state))
            {
                state = new GroupState();
                Groups[name] = state;
            }
            return state;
        }

        public static bool IsRunning(string group)
        {
            lock (Sync) { return IsRunningLocked(State(group)); }
        }

        public static int? PortFor(string group, string key)
        {
            lock (Sync)
            {
                var state = State(group);
                if (state.Process == null) return null;
                return state.Active.TryGetValue(key ?? "", out var target) ? (int?)target.Port : null;
            }
        }

        public static string TagFor(string group, string key)
        {
            lock (Sync)
            {
                return State(group).Active.TryGetValue(key ?? "", out var target) ? target.Tag : "";
            }
        }

        public static bool GroupServes(string group, string key, string adapterName, string adapterIp)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;

            lock (Sync)
            {
                var state = State(group);
                if (!IsRunningLocked(state)) return false;
                if (!state.Active.ContainsKey(key)) return false;
                if (state.Signature.Length == 0) return true;

                return string.Equals(state.Signature, AdapterSignature(adapterName, adapterIp), StringComparison.OrdinalIgnoreCase);
            }
        }

        public static string Describe(string group)
        {
            lock (Sync)
            {
                var state = State(group);
                if (!IsRunningLocked(state)) return "none";

                var ports = state.Active.Values
                    .OrderBy(t => t.Tag, StringComparer.Ordinal)
                    .Select(t => t.Port);

                return $"{state.Active.Count} endpoint(s) pid={state.Process?.Id} ports=[{string.Join(",", ports)}]";
            }
        }

        // ── Public API ──────────────────────────────────────────────────────────────────────

        public static bool EnsureStarted(AppConfig cfg, IList<TunnelParseResult> tunnels, string adapterName, string adapterIp, out string error)
            => EnsureStarted(cfg, GroupCustom, tunnels, adapterName, adapterIp, out error);

        public static bool EnsureStarted(AppConfig cfg, string group, IList<TunnelParseResult> tunnels, string adapterName, string adapterIp, out string error)
        {
            var targets = new List<TunnelTarget>();
            int index = 0;

            foreach (var tunnel in tunnels ?? new List<TunnelParseResult>())
            {
                if (tunnel == null || !tunnel.Success || tunnel.Endpoint == null) continue;

                string key = TunnelConfigParser.KeyOf(tunnel.Raw, adapterName);
                if (key.Length == 0) key = tunnel.Raw;
                if (targets.Any(t => t.Key == key)) continue;

                targets.Add(new TunnelTarget { Key = key, Parsed = tunnel, Tag = "tunnel-" + index++ });
            }

            return EnsureTargetsStarted(cfg, group, targets, adapterName, adapterIp, out error);
        }

        public static bool EnsureTargetsStarted(AppConfig cfg, string group, List<TunnelTarget> targets, string adapterName, string adapterIp, out string error)
        {
            error = "";

            if (targets == null || targets.Count == 0)
            {
                error = "No usable OpenVPN / WireGuard config.";
                return false;
            }

            string sbDir = cfg?.SbDir ?? "";
            if (sbDir.Length == 0)
            {
                error = "The sing-box directory is not available.";
                return false;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                targets[i].Tag = "tunnel-" + i;
                if (string.IsNullOrWhiteSpace(targets[i].AdapterName)) targets[i].AdapterName = adapterName ?? "";
                if (string.IsNullOrWhiteSpace(targets[i].AdapterIp)) targets[i].AdapterIp = adapterIp ?? "";
            }

            lock (Sync)
            {
                var state = State(group);

                string signature = AdapterSignature(adapterName, adapterIp);
                if (IsRunningLocked(state) && signature == state.Signature && SameTargets(state, targets))
                {
                    foreach (var target in targets)
                    {
                        if (state.Active.TryGetValue(target.Key, out var live)) target.Port = live.Port;
                    }
                    return true;
                }

                foreach (var target in targets)
                {
                    if (state.Active.TryGetValue(target.Key, out var previous)) target.Port = previous.Port;
                }

                StopLocked(state);

                string dir = Path.Combine(sbDir, "tunnel", GroupDir(group));
                state.Dir = dir;

                if (TryStartBatch(sbDir, state, targets, out error))
                {
                    state.Signature = signature;
                    LogStarted(state, group, targets);
                    return true;
                }

                if (targets.Count > 1)
                {
                    SimpleLogger.Log($"[Tunnel] ({group}) batch start failed ({error}); verifying each config on its own.");

                    var survivors = new List<TunnelTarget>();
                    foreach (var target in targets)
                    {
                        if (CanStartAlone(sbDir, target)) survivors.Add(target);
                        else SimpleLogger.Log($"[Tunnel] '{target.Parsed?.Label}' could not be started and was skipped.");
                    }

                    if (survivors.Count > 0 && TryStartBatch(sbDir, state, survivors, out error))
                    {
                        state.Signature = signature;
                        LogStarted(state, group, survivors);
                        return true;
                    }
                }

                StopLocked(state);
                error = error.Length > 0 ? error : "The tunnel engine could not be started.";
                return false;
            }
        }

        public static void Stop()
        {
            lock (Sync)
            {
                foreach (var name in Groups.Keys.ToList()) StopLocked(State(name));
            }
        }

        public static void Stop(string group)
        {
            lock (Sync) { StopLocked(State(group)); }
        }

        public static JObject BuildXraySocksOutbound(int port)
        {
            return new JObject
            {
                ["outbounds"] = new JArray
                {
                    new JObject
                    {
                        ["tag"] = "tunnel-socks",
                        ["protocol"] = "socks",
                        ["settings"] = new JObject
                        {
                            ["servers"] = new JArray
                            {
                                new JObject { ["address"] = "127.0.0.1", ["port"] = port }
                            }
                        },
                        ["streamSettings"] = new JObject { ["network"] = "tcp" },
                        ["mux"] = new JObject { ["enabled"] = false }
                    }
                }
            };
        }

        public static JObject BuildSingboxSocksOutbound(int port)
        {
            return new JObject
            {
                ["type"] = "socks",
                ["server"] = "127.0.0.1",
                ["server_port"] = port
            };
        }

        public static bool StartTransient(AppConfig cfg, TunnelParseResult tunnel, string adapterName, string adapterIp, out int port, out IDisposable lease)
            => StartTransient(cfg?.SbDir ?? "", tunnel, adapterName, adapterIp, out port, out lease, out _);

        public static bool StartTransient(string sbDir, TunnelParseResult tunnel, string adapterName, string adapterIp, out int port, out IDisposable lease)
            => StartTransient(sbDir, tunnel, adapterName, adapterIp, out port, out lease, out _);

        public static bool StartTransient(string sbDir, TunnelParseResult tunnel, string adapterName, string adapterIp,
            out int port, out IDisposable lease, out string error)
        {
            port = 0;
            lease = null;
            error = "";

            if (tunnel == null || !tunnel.Success || tunnel.Endpoint == null)
            {
                error = tunnel?.Error is { Length: > 0 } tunnelParseError ? tunnelParseError : "this is not an OpenVPN / WireGuard config";
                return false;
            }

            if (string.IsNullOrWhiteSpace(sbDir))
            {
                error = "sing-box's folder is not set";
                return false;
            }

            SweepStaleProbeDirs(sbDir);

            var target = new TunnelTarget
            {
                Key = TunnelConfigParser.Normalize(tunnel.Raw),
                Parsed = tunnel,
                Tag = "tunnel-0",
                Port = TryAllocatePort(),
                AdapterName = adapterName ?? "",
                AdapterIp = adapterIp ?? ""
            };
            if (target.Port <= 0)
            {
                error = "no free loopback port was available for the tunnel";
                return false;
            }

            string dir = Path.Combine(sbDir, "tunnel_probe_" + Guid.NewGuid().ToString("N"));
            var outcome = StartEngine(sbDir, dir, new List<TunnelTarget> { target });
            if (outcome.Process == null)
            {
                error = outcome.Error.Length > 0 ? outcome.Error : "the tunnel engine could not be started";
                SimpleLogger.Log($"[Tunnel] Test engine failed: {error}");
                return false;
            }

            if (!Settled(new List<TunnelTarget> { target }, outcome.Process))
            {
                error = "the tunnel instance exited right after start (see the sing-box log lines above)";
                SimpleLogger.Log("[Tunnel] Test engine exited right after startup.");
                KillProcess(outcome.Process);
                TryDeleteDirectory(dir);
                return false;
            }

            SimpleLogger.Log($"[Tunnel] Test engine for '{tunnel.Label}' listening on 127.0.0.1:{target.Port} (pid={outcome.Process.Id}).");
            port = target.Port;
            lease = new TransientTunnelLease(outcome.Process, dir);
            return true;
        }

        public static async System.Threading.Tasks.Task<TunnelTestPrep> PrepareTestAsync(string raw, AppConfig cfg, string adapterName, string adapterIp)
        {
            var prep = new TunnelTestPrep { State = TunnelTestState.NotATunnel };
            if (string.IsNullOrWhiteSpace(raw)) return prep;
            if (!TunnelConfigParser.TryParse(raw, out var tunnel)) return prep;

            if (tunnel == null || !tunnel.Success)
            {
                SimpleLogger.Log($"[Tunnel] The custom config cannot be tested: {tunnel?.Error}");
                prep.State = TunnelTestState.Failed;
                return prep;
            }

            if (!await TunnelCredentialResolver.ApplyAsync(tunnel))
            {
                prep.State = TunnelTestState.Failed;
                return prep;
            }

            if (!StartTransient(cfg, tunnel, adapterName, adapterIp, out int port, out var lease))
            {
                prep.State = TunnelTestState.Failed;
                return prep;
            }

            prep.State = TunnelTestState.Ready;
            prep.OutboundJson = BuildXraySocksOutbound(port).ToString(Formatting.None);
            prep.Lease = lease;
            return prep;
        }

        private sealed class TransientTunnelLease : IDisposable
        {
            private readonly Process _process;
            private readonly string _dir;

            public TransientTunnelLease(Process process, string dir)
            {
                _process = process;
                _dir = dir;
            }

            public void Dispose()
            {
                try { if (!_process.HasExited) _process.Kill(); } catch { }
                try { _process.Dispose(); } catch { }
                try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
            }
        }

        // ── Engine lifecycle ────────────────────────────────────────────────────────────────

        private sealed class EngineStart
        {
            public Process Process;
            public string Error = "";
            public bool Retryable;
            public bool Exited;
        }

        private static bool TryStartBatch(string sbDir, GroupState state, List<TunnelTarget> targets, out string error)
        {
            error = "";
            if (targets.Count == 0)
            {
                error = "Nothing to start.";
                return false;
            }

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                foreach (var target in targets)
                {
                    if (target.Port <= 0) target.Port = TryAllocatePort();
                }

                if (targets.Any(t => t.Port <= 0))
                {
                    error = "No free loopback port is available for the tunnel.";
                    continue;
                }

                var outcome = StartEngine(sbDir, state.Dir, targets);
                if (outcome.Process != null)
                {
                    if (Settled(targets, outcome.Process))
                    {
                        state.Process = outcome.Process;

                        state.Active.Clear();
                        foreach (var target in targets) state.Active[target.Key] = target;

                        return true;
                    }

                    error = "The tunnel engine exited right after startup (see the [sing-box.exe] log lines).";
                    KillProcess(outcome.Process);
                    SimpleLogger.Log("[Tunnel] Engine exited right after startup; retrying once.");

                    if (attempt >= 2) return false;
                    foreach (var target in targets) target.Port = 0;
                    continue;
                }

                error = outcome.Error;

                if (!outcome.Retryable)
                {
                    SimpleLogger.Log($"[Tunnel] Engine start aborted: {outcome.Error}");
                    return false;
                }

                if (outcome.Exited && attempt >= 2)
                {
                    SimpleLogger.Log("[Tunnel] sing-box exited during startup; not retrying.");
                    return false;
                }

                SimpleLogger.Log($"[Tunnel] Engine start attempt {attempt}/{MaxAttempts} failed: {outcome.Error}");
            }

            return false;
        }

        private static bool Settled(List<TunnelTarget> targets, Process process)
        {
            Thread.Sleep(1200);

            if (HasExited(process)) return false;

            foreach (var target in targets)
            {
                if (!WaitForPort(target.Port, process, 600)) return false;
            }

            return true;
        }

        private static bool CanStartAlone(string sbDir, TunnelTarget target)
        {
            var probe = new TunnelTarget
            {
                Key = target.Key,
                Parsed = target.Parsed,
                Tag = "tunnel-0",
                Port = TryAllocatePort(),
                AdapterName = target.AdapterName,
                AdapterIp = target.AdapterIp
            };
            if (probe.Port <= 0) return false;

            string dir = Path.Combine(sbDir, "tunnel_probe_" + Guid.NewGuid().ToString("N"));
            var outcome = StartEngine(sbDir, dir, new List<TunnelTarget> { probe });

            bool ok = outcome.Process != null;
            if (!ok) SimpleLogger.Log($"[Tunnel] '{target.Parsed?.Label}' was rejected: {outcome.Error}");

            KillProcess(outcome.Process);
            TryDeleteDirectory(dir);
            return ok;
        }

        private static EngineStart StartEngine(string sbDir, string dir, List<TunnelTarget> targets)
        {
            var outcome = new EngineStart();

            try
            {
                Directory.CreateDirectory(dir);

                string configPath = Path.Combine(dir, "config.json");
                File.WriteAllText(configPath, BuildConfig(targets).ToString(Formatting.Indented));

                string exe = MainWindow.Instance?.GetAppPath(@"Data\sing_box\sing-box.exe");
                if (string.IsNullOrWhiteSpace(exe)) exe = Path.Combine(sbDir, "sing-box.exe");

                if (!File.Exists(exe))
                {
                    outcome.Error = "sing-box.exe was not found.";
                    return outcome;
                }

                if (!SingboxConfigValidator.Check(sbDir, configPath))
                {
                    outcome.Error = "The generated tunnel config was rejected by sing-box check.";
                    return outcome;
                }

                var process = ProcessService.StartProcessDirect(exe, "run -c config.json", dir);
                if (process == null)
                {
                    outcome.Error = "sing-box could not be started.";
                    outcome.Retryable = true;
                    return outcome;
                }

                if (HasExited(process))
                {
                    outcome.Error = "sing-box exited right after start (see the [sing-box.exe] log lines).";
                    outcome.Retryable = true;
                    outcome.Exited = true;
                    KillProcess(process);
                    return outcome;
                }

                foreach (var target in targets)
                {
                    if (WaitForPort(target.Port, process, ReadinessTimeoutMs)) continue;

                    bool exited = HasExited(process);

                    outcome.Error = exited
                        ? "sing-box exited before the socks inbound was ready (see the [sing-box.exe] log lines)."
                        : $"The tunnel did not start listening on 127.0.0.1:{target.Port}.";

                    outcome.Retryable = true;
                    outcome.Exited = exited;
                    KillProcess(process);
                    return outcome;
                }

                outcome.Process = process;
                return outcome;
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                outcome.Error = ex.Message;
                outcome.Retryable = true;
                return outcome;
            }
        }

        // ── Config generation ───────────────────────────────────────────────────────────────

        private static JObject BuildConfig(List<TunnelTarget> targets)
        {
            var inbounds = new JArray();
            var endpoints = new JArray();
            var routeRules = new JArray();
            var dnsServers = new JArray();
            var dnsRules = new JArray();

            foreach (var target in targets)
            {
                string inboundTag = target.Tag + "-in";

                inbounds.Add(new JObject
                {
                    ["type"] = "socks",
                    ["tag"] = inboundTag,
                    ["listen"] = "127.0.0.1",
                    ["listen_port"] = target.Port
                });

                var endpoint = (JObject)target.Parsed.Endpoint.DeepClone();
                TunnelConfigParser.WithTag(endpoint, target.Tag, target.AdapterName, target.AdapterIp);
                endpoints.Add(endpoint);

                routeRules.Add(new JObject
                {
                    ["inbound"] = new JArray(inboundTag),
                    ["action"] = "route",
                    ["outbound"] = target.Tag
                });

                dnsServers.Add(new JObject
                {
                    ["type"] = "udp",
                    ["tag"] = "dns-" + target.Tag,
                    ["server"] = "1.1.1.1",
                    ["detour"] = target.Tag
                });

                dnsRules.Add(new JObject
                {
                    ["inbound"] = new JArray(inboundTag),
                    ["action"] = "route",
                    ["server"] = "dns-" + target.Tag
                });
            }

            dnsServers.Add(new JObject
            {
                ["type"] = "udp",
                ["tag"] = "dns-fallback",
                ["server"] = "8.8.8.8"
            });

            return new JObject
            {
                ["log"] = new JObject { ["level"] = "fatal", ["timestamp"] = false },
                ["dns"] = new JObject
                {
                    ["servers"] = dnsServers,
                    ["rules"] = dnsRules,
                    ["final"] = "dns-fallback",
                    ["strategy"] = "ipv4_only"
                },
                ["inbounds"] = inbounds,
                ["endpoints"] = endpoints,
                ["outbounds"] = new JArray { new JObject { ["type"] = "direct", ["tag"] = "direct" } },
                ["route"] = new JObject
                {
                    ["rules"] = routeRules,
                    ["final"] = "direct",
                    ["default_domain_resolver"] = new JObject { ["server"] = "dns-fallback" }
                }
            };
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────

        private static string GroupDir(string group)
            => string.Equals(group?.Trim(), GroupRules, StringComparison.OrdinalIgnoreCase) ? GroupRules : GroupCustom;

        private static bool IsRunningLocked(GroupState state)
        {
            if (state.Process == null) return false;

            try
            {
                if (state.Process.HasExited)
                {
                    state.Process.Dispose();
                    state.Process = null;
                    state.Active.Clear();
                    return false;
                }
                return true;
            }
            catch
            {
                state.Process = null;
                state.Active.Clear();
                return false;
            }
        }

        private static void StopLocked(GroupState state)
        {
            var process = state.Process;
            string dir = state.Dir;

            state.Process = null;
            state.Dir = "";
            state.Signature = "";
            state.Active.Clear();

            KillProcess(process);

            if (dir.Length > 0)
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
                catch (Exception ex) { SimpleLogger.Log($"[Tunnel] could not remove {dir}: {ex.Message}"); }
            }
        }

        private static bool SameTargets(GroupState state, List<TunnelTarget> desired)
        {
            if (state.Active.Count != desired.Count) return false;

            foreach (var target in desired)
            {
                if (!state.Active.ContainsKey(target.Key)) return false;
            }

            return true;
        }

        private static string AdapterSignature(string adapterName, string adapterIp)
        {
            string name = string.IsNullOrWhiteSpace(adapterName) ? "Default" : adapterName.Trim();
            return name + "|" + (adapterIp ?? "").Trim();
        }

        private static void LogStarted(GroupState state, string group, List<TunnelTarget> targets)
        {
            foreach (var target in targets)
            {
                SimpleLogger.Log($"[Tunnel:{group}] {target.Parsed?.KindText} {target.Parsed?.ServerText()} → socks 127.0.0.1:{target.Port} " +
                                 $"(tag={target.Tag}, pid={state.Process?.Id})");
            }
        }

        private static int TryAllocatePort()
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                TcpListener listener = null;
                try
                {
                    listener = new TcpListener(IPAddress.Loopback, 0);
                    listener.Start();
                    int port = ((IPEndPoint)listener.LocalEndpoint).Port;

                    if (port > 0 && port < 65535) return port;
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

            return 0;
        }

        private static bool WaitForPort(int port, Process process, int timeoutMs)
        {
            int deadline = Environment.TickCount + timeoutMs;

            while (Environment.TickCount < deadline)
            {
                if (process != null && HasExited(process)) return false;

                TcpClient client = null;
                try
                {
                    client = new TcpClient();
                    var connect = client.BeginConnect(IPAddress.Loopback, port, null, null);
                    if (connect.AsyncWaitHandle.WaitOne(200))
                    {
                        client.EndConnect(connect);
                        return true;
                    }
                }
                catch { }
                finally
                {
                    try { client?.Close(); } catch { }
                }

                Thread.Sleep(60);
            }

            return false;
        }

        private static bool HasExited(Process process)
        {
            if (process == null) return true;

            try { if (process.HasExited) return true; }
            catch { return true; }

            try
            {
                using var live = Process.GetProcessById(process.Id);
                return live.HasExited;
            }
            catch (ArgumentException) { return true; }
            catch { return false; }
        }

        private static void KillProcess(Process process)
        {
            if (process == null) return;

            try { if (!process.HasExited) process.Kill(); } catch { }
            try { process.Dispose(); } catch { }
        }

        private static void TryDeleteDirectory(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }

        private static void SweepStaleProbeDirs(string sbDir)
        {
            if (Interlocked.Exchange(ref _probeSweepDone, 1) != 0) return;

            try
            {
                if (!Directory.Exists(sbDir)) return;

                var cutoff = DateTime.UtcNow.AddMinutes(-30);
                foreach (var dir in Directory.GetDirectories(sbDir, "tunnel_probe_*"))
                {
                    try { if (Directory.GetLastWriteTimeUtc(dir) < cutoff) Directory.Delete(dir, true); } catch { }
                }
            }
            catch { }
        }
    }
}

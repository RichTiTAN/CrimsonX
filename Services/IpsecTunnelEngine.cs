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
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public static class IpsecTunnelEngine
    {
        private const int ReadinessTimeoutMs = 60000;

        private const string FailoverTimeout = "6s";

        private sealed class EngineProcess
        {
            public string Group = "";

            public Process Process;

            public StreamWriter Input;

            public int Stopped;

            public readonly List<string> Errors = new List<string>();
        }

        private sealed class GroupState
        {
            public string Name = "";

            public string Dir = "";

            public string Signature = "";

            public EngineProcess Engine;

            public readonly Dictionary<string, TunnelTarget> Active = new Dictionary<string, TunnelTarget>(StringComparer.Ordinal);

            public List<TunnelTarget> Planned = new List<TunnelTarget>();

            public readonly Dictionary<string, string> LocalIps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public CancellationTokenSource StartCts;

            public int Epoch;
        }

        private sealed class TransientIpsecLease : IDisposable
        {
            private readonly EngineProcess _engine;

            private readonly string _dir;

            public TransientIpsecLease(EngineProcess engine, string dir)
            {
                _engine = engine;
                _dir = dir;
            }

            public void Dispose()
            {
                StopProcess(_engine);
                TryDeleteDirectory(_dir);
            }
        }

        private static readonly object Sync = new object();

        private static readonly object LiveSync = new object();

        private static readonly Dictionary<string, GroupState> Groups = new Dictionary<string, GroupState>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<EngineProcess> Live = new List<EngineProcess>();

        private static GroupState State(string group)
        {
            string name = string.IsNullOrWhiteSpace(group) ? TunnelEngine.GroupCustom : group.Trim();
            if (!Groups.TryGetValue(name, out var state))
            {
                state = new GroupState { Name = name };
                Groups[name] = state;
            }
            return state;
        }

        public static bool IsRunning(string group)
        {
            lock (Sync)
            {
                return IsRunningLocked(State(group));
            }
        }

        private static int _rebuilding;

        internal static bool IsRebuilding => Volatile.Read(ref _rebuilding) > 0;

        private static void BeginRebuild() => Interlocked.Increment(ref _rebuilding);

        private static void EndRebuild() => Interlocked.Decrement(ref _rebuilding);

        public static int? PortFor(string group, string key)
        {
            lock (Sync)
            {
                return PortIn(State(group), key);
            }
        }

        internal static List<TunnelTarget> PlannedTargets(string group)
        {
            lock (Sync)
            {
                var state = State(group);
                var found = new List<TunnelTarget>();
                foreach (var target in state.Planned)
                {
                    if (target != null && target.Port > 0) found.Add(target);
                }
                foreach (var target in state.Active.Values)
                {
                    if (target == null || target.Port <= 0) continue;
                    if (!found.Contains(target)) found.Add(target);
                }
                return found;
            }
        }

        public static int? PortForId(string group, string id)
        {
            lock (Sync)
            {
                var state = State(group);
                if (!IsRunningLocked(state)) return null;
                return state.Active.TryGetValue(id ?? "", out var target) ? target.Port : null;
            }
        }

        public static string TagFor(string group, string key)
        {
            lock (Sync)
            {
                var state = State(group);
                if (!IsRunningLocked(state)) return "";
                if (state.Active.TryGetValue(key ?? "", out var target)) return target.Tag;
                foreach (var live in state.Active.Values)
                {
                    if (string.Equals(live.Key, key ?? "", StringComparison.Ordinal)) return live.Tag;
                }
                return "";
            }
        }

        internal static List<TunnelTarget> ActiveTargets(string group)
        {
            lock (Sync)
            {
                var state = State(group);
                if (!IsRunningLocked(state)) return new List<TunnelTarget>();
                return state.Active.Values.Where(target => target != null && target.Port > 0).ToList();
            }
        }

        public static bool GroupServes(string group, string key, string adapterName, string adapterIp)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            lock (Sync)
            {
                var state = State(group);
                if (!IsRunningLocked(state)) return false;
                if (!state.Active.TryGetValue(key, out var target)) return false;
                if (state.Signature.Length == 0) return true;
                return string.Equals(
                    AdapterSignature(target.AdapterName, target.AdapterIp),
                    AdapterSignature(adapterName, adapterIp),
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        public static string Describe(string group)
        {
            lock (Sync)
            {
                var state = State(group);
                if (!IsRunningLocked(state)) return "";
                var parts = new List<string>();
                foreach (var target in state.Active.Values.OrderBy(t => t.Tag, StringComparer.Ordinal))
                {
                    string ip = state.LocalIps.TryGetValue(target.Tag, out var found) ? found : "";
                    parts.Add($"{target.Tag} 127.0.0.1:{target.Port}{(ip.Length > 0 ? " (" + ip + ")" : "")}");
                }
                string pid = state.Engine?.Process != null ? state.Engine.Process.Id.ToString() : "?";
                return $"ikev2 pid={pid} " + string.Join(", ", parts);
            }
        }

        private static int? PortIn(GroupState state, string key)
        {
            if (!IsRunningLocked(state)) return null;
            if (state.Active.TryGetValue(key ?? "", out var target)) return target.Port;
            foreach (var live in state.Active.Values)
            {
                if (string.Equals(live.Key, key ?? "", StringComparison.Ordinal)) return live.Port;
            }
            return null;
        }

        private static bool IsRunningLocked(GroupState state)
            => state.Engine != null && !HasExited(state.Engine.Process);

        private static string AdapterSignature(string adapterName, string adapterIp)
            => (adapterName ?? "").Trim() + "|" + (adapterIp ?? "").Trim();

        private static string TargetsSignature(IEnumerable<TunnelTarget> targets)
        {
            var parts = targets.Select(t =>
                TunnelEngine.IdOf(t) + "|" + AdapterSignature(t.AdapterName, t.AdapterIp) + "|"
                + (t.Parsed?.Endpoint?.ToString(Newtonsoft.Json.Formatting.None) ?? ""));
            return string.Join("\n", parts);
        }

        public static bool EnsureStarted(AppConfig cfg, string sbDir, string group, List<TunnelTarget> targets, out string error)
            => EnsureStarted(cfg, sbDir, group, targets, CancellationToken.None, out error);

        public static bool EnsureStarted(AppConfig cfg, string sbDir, string group, List<TunnelTarget> targets,
            CancellationToken token, out string error)
        {
            BeginRebuild();
            try
            {
                return EnsureStartedCore(cfg, sbDir, group, targets, token, out error);
            }
            finally
            {
                EndRebuild();
            }
        }

        private static bool EnsureStartedCore(AppConfig cfg, string sbDir, string group, List<TunnelTarget> targets,
            CancellationToken token, out string error)
        {
            error = "";
            if (targets == null || targets.Count == 0)
            {
                error = "No usable IKEv2 config.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(sbDir))
            {
                error = "The sing-box directory is not available.";
                return false;
            }
            string signature = TargetsSignature(targets);
            GroupState state;
            CancellationTokenSource startCts;
            int epoch;
            EngineProcess previous;
            lock (Sync)
            {
                state = State(group);
                if (IsRunningLocked(state) && signature == state.Signature)
                {
                    foreach (var target in targets)
                    {
                        if (state.Active.TryGetValue(TunnelEngine.IdOf(target), out var live)) target.Port = live.Port;
                    }
                    return true;
                }
                if (token.IsCancellationRequested)
                {
                    error = "cancelled by the user";
                    return false;
                }
                foreach (var target in targets)
                {
                    if (target.Port <= 0) target.Port = AllocatePort();
                }
                if (targets.Any(t => t.Port <= 0))
                {
                    error = "no free loopback port was available for the IKEv2 engine";
                    return false;
                }
                try { state.StartCts?.Cancel(); } catch { }
                startCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                state.StartCts = startCts;
                epoch = ++state.Epoch;
                previous = DetachLocked(state);
            }
            if (previous != null) StopProcess(previous);
            try
            {
                return StartCore(state, epoch, startCts.Token, sbDir, group, targets, signature, out error);
            }
            finally
            {
                lock (Sync)
                {
                    if (ReferenceEquals(state.StartCts, startCts)) state.StartCts = null;
                }
                startCts.Dispose();
            }
        }

        private static bool StartCore(GroupState state, int epoch, CancellationToken token, string sbDir, string group,
            List<TunnelTarget> targets, string signature, out string error)
        {
            error = "";
            string exe = IpsecEngine.ExecutablePath(sbDir);
            if (exe.Length == 0)
            {
                error = "the IKEv2 engine (crimsonipsec.exe) is not installed";
                return false;
            }
            var attempts = BuildAttempts(targets);
            int maxAttempts = Math.Max(1, MaxAttempts(attempts));
            lock (Sync)
            {
                state.Planned = new List<TunnelTarget>(targets);
            }
            string budget = attempts.Any(a => a.Addresses.Count > 1) ? FailoverTimeout : "";
            SimpleLogger.Log($"[Tunnel] ({group}) ikev2 start: {attempts.Count} tunnel(s), {maxAttempts} attempt(s) - " +
                             $"{string.Join(", ", attempts.Select(a => $"{a.Tag}: {a.Addresses.Count} address(es)"))}.");
            foreach (var entry in attempts)
            {
                string resolved = entry.Addresses.Count > 0 ? string.Join(", ", entry.Addresses) : "no address";
                SimpleLogger.Log($"[Tunnel] ({group}) {entry.Tag} {entry.Host} -> {resolved}");
            }
            IpsecGroupPlan plan = null;
            EngineProcess engine = null;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (token.IsCancellationRequested)
                {
                    error = "cancelled by the user";
                    SimpleLogger.Log($"[Tunnel] ({group}) ikev2 start cancelled by the user.");
                    return false;
                }
                foreach (var entry in attempts)
                {
                    if (entry.Target == null) continue;
                    entry.Target.ServerOverride = AddressAt(entry);
                    entry.Target.Budget = entry.Addresses.Count > 1 ? FailoverTimeout : "";
                }
                plan = IpsecConfigWriter.Write(sbDir, group, targets, "");
                if (plan == null)
                {
                    error = "the IKEv2 config could not be written";
                    return false;
                }
                string dialing = string.Join(", ", attempts.Select(a => $"{a.Tag} → {DialText(a)}"));
                SimpleLogger.Log($"[Tunnel] ({group}) ikev2 attempt {attempt + 1}/{maxAttempts}: {dialing} (tunnel timeout {(budget.Length > 0 ? budget : "default")}).");
                engine = StartEngine(exe, plan, group, out string startError);
                if (engine == null)
                {
                    TryDeleteDirectory(plan.WorkingDirectory);
                    error = startError;
                    return false;
                }
                if (WaitForPorts(engine, targets, plan.StatusPath, token))
                {
                    foreach (var entry in attempts) TunnelHostMemory.MarkSucceeded(entry.Host);
                    PersistCursors(attempts);
                    break;
                }
                bool cancelled = token.IsCancellationRequested;
                var states = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var entry in attempts)
                {
                    states[entry.Tag] = ReadStatusStateForTag(plan.StatusPath, entry.Tag);
                }
                string reason = cancelled ? "cancelled by the user" : FirstReason(ReadStatusError(plan.StatusPath), LastEngineError(engine));
                StopProcess(engine);
                TryDeleteDirectory(plan.WorkingDirectory);
                engine = null;
                error = reason.Length > 0 ? reason : "the IKEv2 engine did not bring the tunnel up";
                if (cancelled)
                {
                    SimpleLogger.Log($"[Tunnel] ({group}) ikev2 start cancelled by the user.");
                    return false;
                }
                if (!AdvanceFailed(attempts, tag => states.TryGetValue(tag, out string state) ? state : ""))
                {
                    SimpleLogger.Log($"[Tunnel] ({group}) ikev2 {dialing} failed ({error}); no address is left to try.");
                    break;
                }
                PersistCursors(attempts);
                SimpleLogger.Log($"[Tunnel] ({group}) ikev2 {dialing} failed ({error}); retrying " +
                                 $"{string.Join(", ", attempts.Select(a => $"{a.Tag} → {DialText(a)}"))}.");
            }
            if (engine == null)
            {
                error += SinkholeHint(targets);
                SimpleLogger.Log($"[Tunnel] ({group}) IKEv2 start failed: {error}");
                return false;
            }
            lock (Sync)
            {
                if (state.Epoch == epoch && !token.IsCancellationRequested)
                {
                    state.Dir = plan.WorkingDirectory;
                    state.Engine = engine;
                    state.Signature = signature;
                    state.Active.Clear();
                    state.LocalIps.Clear();
                    foreach (var target in targets) state.Active[TunnelEngine.IdOf(target)] = target;
                    ReadStatus(state, plan.StatusPath);
                    LogStarted(state, group, targets);
                    return true;
                }
            }
            StopProcess(engine);
            TryDeleteDirectory(plan.WorkingDirectory);
            error = "cancelled by the user";
            SimpleLogger.Log($"[Tunnel] ({group}) ikev2 start was stopped before it finished; the engine was discarded.");
            return false;
        }

        private static string FirstReason(string first, string second)
            => string.IsNullOrWhiteSpace(first) ? second : first;

        public static void Stop(string group)
        {
            BeginRebuild();
            try
            {
                EngineProcess detached;
                EngineProcess[] live;
                lock (Sync)
                {
                    var state = State(group);
                    state.Epoch++;
                    state.Planned = new List<TunnelTarget>();
                    try { state.StartCts?.Cancel(); } catch { }
                    detached = DetachLocked(state);
                }
                live = TakeLive(group);
                if (detached != null) StopProcess(detached);
                foreach (var engine in live) StopProcess(engine);
            }
            finally
            {
                EndRebuild();
            }
        }

        public static void StopAll()
        {
            BeginRebuild();
            try
            {
                var detached = new List<EngineProcess>();
                EngineProcess[] live;
                lock (Sync)
                {
                    foreach (var state in Groups.Values)
                    {
                        state.Epoch++;
                        state.Planned = new List<TunnelTarget>();
                        try { state.StartCts?.Cancel(); } catch { }
                        var engine = DetachLocked(state);
                        if (engine != null) detached.Add(engine);
                    }
                }
                live = TakeLive(null);
                foreach (var engine in detached) StopProcess(engine);
                foreach (var engine in live) StopProcess(engine);
            }
            finally
            {
                EndRebuild();
            }
        }

        private static EngineProcess[] TakeLive(string group)
        {
            lock (LiveSync)
            {
                var found = group == null
                    ? Live.ToArray()
                    : Live.Where(e => string.Equals(e.Group, group, StringComparison.OrdinalIgnoreCase)).ToArray();
                foreach (var engine in found) Live.Remove(engine);
                return found;
            }
        }

        private static EngineProcess DetachLocked(GroupState state)
        {
            if (state == null) return null;
            var engine = state.Engine;
            state.Engine = null;
            state.Signature = "";
            state.Active.Clear();
            state.LocalIps.Clear();
            state.Dir = "";
            return engine;
        }

        public static bool StartTransient(string sbDir, TunnelParseResult tunnel, string adapterName, string adapterIp,
            out int port, out IDisposable lease, out string error)
        {
            port = 0;
            lease = null;
            error = "";
            if (tunnel == null || !tunnel.Success || tunnel.Endpoint == null)
            {
                error = tunnel?.Error is { Length: > 0 } parseError ? parseError : "this is not an IKEv2 config";
                return false;
            }
            if (string.IsNullOrWhiteSpace(sbDir))
            {
                error = "sing-box's folder is not set";
                return false;
            }
            string exe = IpsecEngine.ExecutablePath(sbDir);
            if (exe.Length == 0)
            {
                error = "the IKEv2 engine (crimsonipsec.exe) is not installed";
                return false;
            }
            int allocated = AllocatePort();
            if (allocated <= 0)
            {
                error = "no free loopback port was available for the tunnel";
                return false;
            }
            var target = new TunnelTarget
            {
                Key = TunnelConfigParser.Normalize(tunnel.Raw),
                Parsed = tunnel,
                Tag = "tunnel-0",
                Port = allocated,
                AdapterName = adapterName ?? "",
                AdapterIp = adapterIp ?? ""
            };
            target.ServerOverride = ServerCandidates(new List<TunnelTarget> { target })[0];
            var plan = IpsecConfigWriter.Write(sbDir, "probe" + Guid.NewGuid().ToString("N").Substring(0, 8), new List<TunnelTarget> { target });
            if (plan == null)
            {
                error = "the IKEv2 config could not be written";
                return false;
            }
            var engine = StartEngine(exe, plan, "probe", out string startError);
            if (engine == null)
            {
                TryDeleteDirectory(plan.WorkingDirectory);
                error = startError;
                return false;
            }
            if (!WaitForPorts(engine, new List<TunnelTarget> { target }, plan.StatusPath, CancellationToken.None))
            {
                string reason = FirstReason(ReadStatusError(plan.StatusPath), LastEngineError(engine));
                StopProcess(engine);
                TryDeleteDirectory(plan.WorkingDirectory);
                error = reason.Length > 0 ? reason : "the tunnel engine could not be started";
                SimpleLogger.Log($"[Tunnel] IKEv2 test engine failed: {error}");
                return false;
            }
            SimpleLogger.Log($"[Tunnel] IKEv2 test engine for '{tunnel.Label}' listening on 127.0.0.1:{allocated} (pid={engine.Process.Id}).");
            port = allocated;
            lease = new TransientIpsecLease(engine, plan.WorkingDirectory);
            return true;
        }

        private static EngineProcess StartEngine(string exe, IpsecGroupPlan plan, string group, out string error)
        {
            error = "";
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "-config \"" + plan.ConfigPath + "\"",
                    WorkingDirectory = plan.WorkingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                foreach (var pair in plan.Environment) info.Environment[pair.Key] = pair.Value;
                var process = Process.Start(info);
                if (process == null)
                {
                    error = "the IKEv2 engine could not be started";
                    return null;
                }
                var engine = new EngineProcess { Process = process, Input = process.StandardInput, Group = group ?? "" };
                lock (LiveSync) Live.Add(engine);
                process.ErrorDataReceived += (_, e) =>
                {
                    if (string.IsNullOrWhiteSpace(e.Data)) return;
                    string line = e.Data.Trim();
                    lock (engine.Errors)
                    {
                        engine.Errors.Add(line);
                        if (engine.Errors.Count > 60) engine.Errors.RemoveAt(0);
                    }
                    SimpleLogger.Log($"[Ipsec:{group}] {line}");
                };
                process.BeginErrorReadLine();
                process.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data)) SimpleLogger.Log($"[Ipsec:{group}] {e.Data.Trim()}");
                };
                process.BeginOutputReadLine();
                return engine;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                SimpleLogger.Log($"[Tunnel] The IKEv2 engine could not be started: {ex.Message}");
                return null;
            }
        }

        private static bool WaitForPorts(EngineProcess engine, IList<TunnelTarget> targets, string statusPath, CancellationToken token)
        {
            var pending = targets.ToList();
            int deadline = Environment.TickCount + ReadinessTimeoutMs;
            string lastState = "";
            while (pending.Count > 0 && Environment.TickCount < deadline)
            {
                if (token.IsCancellationRequested) return false;
                if (HasExited(engine.Process)) return false;
                string state = ReadStatusState(statusPath);
                if (state != lastState && state.Length > 0)
                {
                    lastState = state;
                    SimpleLogger.Log($"[Ipsec:{engine.Group}] status: {state}");
                }
                if (state == "failed" || state == "stopped") return false;
                foreach (var target in pending.ToList())
                {
                    if (IsPortOpen(target.Port)) pending.Remove(target);
                }
                if (pending.Count > 0) Thread.Sleep(250);
            }
            return pending.Count == 0;
        }

        internal static string ReadStatusState(string path)
        {
            var states = ReadTunnelStates(path, out _);
            if (states.Count == 0) return "";
            if (states.Any(s => s == "failed")) return "failed";
            if (states.Any(s => s == "stopped")) return "stopped";
            return states.All(s => s == "up") ? "up" : "starting";
        }

        internal static string ReadStatusError(string path)
        {
            ReadTunnelStates(path, out string error);
            return error;
        }

        internal static string ReadStatusStateForTag(string path, string tag)
        {
            foreach (var entry in ReadTunnelEntries(path))
            {
                if (!string.Equals(entry.Tag, tag ?? "", StringComparison.Ordinal)) continue;
                return entry.State;
            }
            return "";
        }

        internal static string ReadStatusErrorForTag(string path, string tag)
        {
            foreach (var entry in ReadTunnelEntries(path))
            {
                if (!string.Equals(entry.Tag, tag ?? "", StringComparison.Ordinal)) continue;
                return entry.Error;
            }
            return "";
        }

        private sealed class StatusEntry
        {
            public string Tag = "";

            public string State = "";

            public string Error = "";
        }

        private static List<StatusEntry> ReadTunnelEntries(string path)
        {
            var entries = new List<StatusEntry>();
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return entries;
                var document = JObject.Parse(File.ReadAllText(path));
                if (document["tunnels"] is not JArray tunnels) return entries;
                foreach (var item in tunnels.OfType<JObject>())
                {
                    entries.Add(new StatusEntry
                    {
                        Tag = item["tag"]?.ToString() ?? "",
                        State = item["state"]?.ToString() ?? "",
                        Error = item["error"]?.ToString() ?? ""
                    });
                }
            }
            catch { }
            return entries;
        }

        private static List<string> ReadTunnelStates(string path, out string error)
        {
            error = "";
            var states = new List<string>();
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return states;
                var document = JObject.Parse(File.ReadAllText(path));
                if (document["tunnels"] is not JArray tunnels) return states;
                foreach (var item in tunnels.OfType<JObject>())
                {
                    states.Add(item["state"]?.ToString() ?? "");
                    string reason = item["error"]?.ToString() ?? "";
                    if (reason.Length > 0 && error.Length == 0) error = reason;
                }
            }
            catch { }
            return states;
        }

        private static string LastEngineError(EngineProcess engine)
        {
            if (engine == null) return "";
            lock (engine.Errors)
            {
                for (int i = engine.Errors.Count - 1; i >= 0; i--)
                {
                    string line = engine.Errors[i];
                    if (line.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
                        || line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0) return line;
                }
                return engine.Errors.Count > 0 ? engine.Errors[engine.Errors.Count - 1] : "";
            }
        }

        internal static Func<string, string, IReadOnlyList<string>> Resolver = TunnelResolver.Resolve;

        private static readonly Dictionary<string, int> AddressCursor = new Dictionary<string, int>(StringComparer.Ordinal);

        internal static string CursorKey(TunnelTarget target) => TunnelEngine.IdOf(target);

        internal static int CursorOf(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return 0;
            lock (Sync) return AddressCursor.TryGetValue(key, out int index) ? index : 0;
        }

        internal static void SetCursor(string key, int index)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            lock (Sync) AddressCursor[key] = Math.Max(0, index);
        }

        internal static void RotateToNextAddress(IEnumerable<TunnelTarget> targets)
            => RotateToNextAddress(targets, Resolver);

        internal static void RotateToNextAddress(IEnumerable<TunnelTarget> targets, Func<string, string, IReadOnlyList<string>> resolver)
        {
            var updates = new List<(string Key, int Next)>();
            foreach (var target in targets ?? Enumerable.Empty<TunnelTarget>())
            {
                string key = CursorKey(target);
                if (key.Length == 0) continue;
                int count = AddressesFor(target, resolver).Count;
                if (count <= 1) continue;
                updates.Add((key, (CursorOf(key) + 1) % count));
            }
            foreach (var update in updates) SetCursor(update.Key, update.Next);
        }

        internal static void PersistCursors(IEnumerable<IpsecStartAttempt> attempts)
        {
            foreach (var attempt in attempts ?? Enumerable.Empty<IpsecStartAttempt>())
            {
                if (attempt?.Target == null) continue;
                SetCursor(CursorKey(attempt.Target), attempt.Index);
            }
        }

        internal sealed class IpsecStartAttempt
        {
            public TunnelTarget Target;

            public string Tag = "";

            public string Host = "";

            public IReadOnlyList<string> Addresses = Array.Empty<string>();

            public int Index;
        }

        internal static IReadOnlyList<string> AddressesFor(TunnelTarget target)
            => AddressesFor(target, Resolver);

        internal static IReadOnlyList<string> AddressesFor(TunnelTarget target, Func<string, string, IReadOnlyList<string>> resolver)
        {
            string host = ServerHostOf(target);
            if (host.Length == 0) return Array.Empty<string>();
            var found = resolver(host, TunnelResolver.SourceOf(target)) ?? Array.Empty<string>();
            if (found.Count > 0) return found;
            SimpleLogger.Log($"[Tunnel] no public address for {host}; the tunnel will dial the hostname");
            return Array.Empty<string>();
        }

        internal static List<IpsecStartAttempt> BuildAttempts(IEnumerable<TunnelTarget> targets)
            => BuildAttempts(targets, Resolver);

        internal static List<IpsecStartAttempt> BuildAttempts(IEnumerable<TunnelTarget> targets, Func<string, string, IReadOnlyList<string>> resolver)
        {
            var list = (targets ?? Enumerable.Empty<TunnelTarget>()).ToList();
            var resolved = list.AsParallel().AsOrdered().Select(target => AddressesFor(target, resolver)).ToArray();
            var attempts = new List<IpsecStartAttempt>();
            for (int i = 0; i < list.Count; i++)
            {
                var target = list[i];
                var attempt = new IpsecStartAttempt
                {
                    Target = target,
                    Tag = target?.Tag ?? "",
                    Host = ServerHostOf(target),
                    Addresses = resolved[i]
                };
                if (attempt.Addresses.Count > 0)
                {
                    attempt.Index = CursorOf(CursorKey(target)) % attempt.Addresses.Count;
                }
                attempts.Add(attempt);
            }
            return attempts;
        }

        internal static int MaxAttempts(IEnumerable<IpsecStartAttempt> attempts)
        {
            int max = 0;
            foreach (var attempt in attempts ?? Enumerable.Empty<IpsecStartAttempt>())
            {
                if (attempt.Addresses.Count > max) max = attempt.Addresses.Count;
            }
            return max;
        }

        internal static string AddressAt(IpsecStartAttempt attempt)
        {
            if (attempt == null || attempt.Addresses.Count == 0) return "";
            return attempt.Addresses[Math.Min(attempt.Index, attempt.Addresses.Count - 1)];
        }

        internal static string DialText(IpsecStartAttempt attempt)
        {
            string address = AddressAt(attempt);
            return address.Length > 0 ? address : attempt.Host;
        }

        internal static bool AdvanceFailed(IEnumerable<IpsecStartAttempt> attempts, Func<string, string> stateOfTag)
        {
            bool advanced = false;
            foreach (var attempt in attempts ?? Enumerable.Empty<IpsecStartAttempt>())
            {
                string state = stateOfTag?.Invoke(attempt.Tag) ?? "";
                if (string.Equals(state, "up", StringComparison.OrdinalIgnoreCase)) continue;
                if (attempt.Index + 1 >= attempt.Addresses.Count) continue;
                attempt.Index++;
                advanced = true;
            }
            return advanced;
        }

        internal static List<string> ServerCandidates(IEnumerable<TunnelTarget> targets)
            => ServerCandidates(targets, Resolver);

        internal static List<string> ServerCandidates(IEnumerable<TunnelTarget> targets, Func<string, string, IReadOnlyList<string>> resolver)
        {
            var candidates = new List<string>();
            foreach (var target in targets ?? Enumerable.Empty<TunnelTarget>())
            {
                foreach (string address in AddressesFor(target, resolver))
                {
                    if (!candidates.Contains(address)) candidates.Add(address);
                }
                break;
            }
            if (candidates.Count == 0) candidates.Add("");
            return candidates;
        }

        private static string ServerHostOf(TunnelTarget target)
            => target?.Parsed?.Endpoint?["server"]?.ToString() ?? "";

        private static string SinkholeHint(IEnumerable<TunnelTarget> targets)
        {
            foreach (var target in targets ?? Enumerable.Empty<TunnelTarget>())
            {
                string server = target?.Parsed?.Endpoint?["server"]?.ToString() ?? "";
                if (server.Length == 0) continue;
                if (IPAddress.TryParse(server, out _)) continue;
                try
                {
                    foreach (var address in Dns.GetHostAddresses(server))
                    {
                        if (!IsPrivateAddress(address)) continue;
                        return $" - '{server}' resolves to {address}, which is not a public address, so the network is most likely rewriting DNS. Put the server's IP in the profile and keep the hostname as the remote ID.";
                    }
                }
                catch { }
            }
            return "";
        }

        private static bool IsPrivateAddress(IPAddress address)
            => address != null && BootstrapDns.IsPrivateAddress(address.ToString());

        private static void ReadStatus(GroupState state, string statusPath)
        {
            try
            {
                if (!File.Exists(statusPath)) return;
                var document = JObject.Parse(File.ReadAllText(statusPath));
                if (!(document["tunnels"] is JArray tunnels)) return;
                foreach (var item in tunnels.OfType<JObject>())
                {
                    string tag = item["tag"]?.ToString() ?? "";
                    string localIp = item["local_ip"]?.ToString() ?? "";
                    if (tag.Length > 0 && localIp.Length > 0) state.LocalIps[tag] = localIp;
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
        }

        private static void LogStarted(GroupState state, string group, IEnumerable<TunnelTarget> targets)
        {
            var parts = new List<string>();
            foreach (var target in targets)
            {
                string ip = state.LocalIps.TryGetValue(target.Tag, out var found) ? " (" + found + ")" : "";
                parts.Add($"{target.Tag} {target.Parsed?.ServerText()} → socks 127.0.0.1:{target.Port}{ip}");
            }
            SimpleLogger.Log($"[Tunnel:{group}] ikev2 engine pid={state.Engine?.Process?.Id} {string.Join("; ", parts)}");
        }

        private static int AllocatePort()
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
            return 0;
        }

        private static bool IsPortOpen(int port)
        {
            if (port <= 0) return false;
            TcpClient client = null;
            try
            {
                client = new TcpClient();
                var connect = client.BeginConnect(IPAddress.Loopback, port, null, null);
                if (connect.AsyncWaitHandle.WaitOne(250))
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
            return false;
        }

        private static bool HasExited(Process process)
        {
            if (process == null) return true;
            try { return process.HasExited; }
            catch { return true; }
        }

        private static void StopProcess(EngineProcess engine)
        {
            if (engine == null) return;
            if (Interlocked.Exchange(ref engine.Stopped, 1) != 0) return;
            lock (LiveSync) Live.Remove(engine);
            try { engine.Input?.Close(); } catch { }
            try
            {
                if (engine.Process != null && !engine.Process.HasExited && !engine.Process.WaitForExit(1200))
                    engine.Process.Kill();
            }
            catch { }
            try { engine.Process?.Dispose(); } catch { }
        }

        public static void KillNow()
        {
            EngineProcess[] live;
            lock (LiveSync)
            {
                live = Live.ToArray();
                Live.Clear();
            }
            foreach (var engine in live)
            {
                if (Interlocked.Exchange(ref engine.Stopped, 1) != 0) continue;
                try { engine.Input?.Close(); } catch { }
                try
                {
                    if (engine.Process != null && !engine.Process.HasExited && !engine.Process.WaitForExit(400))
                        engine.Process.Kill();
                }
                catch { }
                try { engine.Process?.Dispose(); } catch { }
            }
        }

        private static void TryDeleteDirectory(string dir)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
                    Directory.Delete(dir, true);
                    return;
                }
                catch
                {
                    if (attempt == 2) return;
                    Thread.Sleep(120);
                }
            }
        }
    }
}

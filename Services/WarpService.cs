/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using CrimsonX.Models;
using CrimsonX.Localization;

namespace CrimsonX.Services
{
    public sealed class WarpStartException : Exception
    {
        public WarpStartException(string message) : base(message) { }

        public WarpStartException(string message, Exception inner) : base(message, inner) { }

        public string Detail { get; init; } = "";

        public bool Retryable { get; init; } = true;
    }

    public static class WarpService
    {
        private const int MasqueMtu = 1280;

        private const int ReadyTimeoutMs = 90000;

        private const int ProbeAttempts = 3;

        private const int StartRetries = 3;

        private const string TraceUrl = "https://www.cloudflare.com/cdn-cgi/trace";

        private static readonly object Sync = new();

        private static readonly List<AetherRuntime> Runtimes = new();

        private static Process? _shimProcess;

        private static WarpPlan? _currentPlan;

        private static bool _stopping;

        private static int _generation;

        private sealed class AetherRuntime
        {
            public WarpEndpoint Endpoint = null!;

            public Process Process = null!;

            public int Generation;

            public readonly ConcurrentQueue<string> Tail = new();

            public string AetherPath = "";
        }

        public static WarpPlan? CurrentPlan
        {
            get { lock (Sync) return _currentPlan; }
        }

        public static bool Active => CurrentPlan?.Active == true;

        public static WarpEndpoint? ForProbe(string? adapterName, string? adapterIp)
        {
            var plan = CurrentPlan;
            if (plan == null || !plan.Active) return null;
            var endpoint = plan.ForAdapter(adapterName, adapterIp)
                ?? plan.Default
                ?? plan.Endpoints.FirstOrDefault();
            return endpoint != null && endpoint.SocksPort > 0 ? endpoint : null;
        }

        public static async Task<WarpPlan> StartAsync(AppConfig? config, CancellationToken ct = default)
        {
            var plan = WarpPlanBuilder.Build(config);
            if (!plan.Active)
            {
                StopAll();
                return plan;
            }

            StopAll();
            string aether = AppPath(@"Data\aether\aether.exe");
            if (!File.Exists(aether))
                throw new WarpStartException("Cloudflare WARP is not installed correctly.") { Retryable = false };

            string xray = AppPath(@"Data\Xray\xray.exe");
            WarpPorts.Assign(plan);
            foreach (var endpoint in plan.Endpoints)
            {
                endpoint.InstanceDir = AppPath(Path.Combine("Data", "aether", "instances", endpoint.InstanceKey));
            }

            WarpStartException? last = null;
            for (int attempt = 0; attempt <= StartRetries; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    foreach (var endpoint in plan.Endpoints) endpoint.Started = false;
                    int generation;
                    lock (Sync)
                    {
                        _stopping = false;
                        generation = ++_generation;
                    }

                    if (plan.Endpoints.Any(endpoint => !endpoint.IsDefault))
                        StartShim(plan, xray);

                    foreach (var endpoint in plan.StartNow.Count > 0 ? plan.StartNow : plan.Endpoints)
                    {
                        ct.ThrowIfCancellationRequested();
                        await StartEndpointAsync(endpoint, plan.Protocol, aether, generation, ct).ConfigureAwait(false);
                    }

                    lock (Sync) _currentPlan = plan;
                    TunnelResolver.WarpDoh = ResolveThroughWarp;
                    TunnelResolver.Clear();
                    SimpleLogger.Log($"[WARP] connected ({Describe(plan)}).");
                    return plan;
                }
                catch (OperationCanceledException)
                {
                    StopAll();
                    throw;
                }
                catch (WarpStartException ex)
                {
                    last = ex;
                    StopAll();
                    if (!ex.Retryable || attempt >= StartRetries) break;
                    SimpleLogger.Log($"[WARP] attempt {attempt + 1}/{StartRetries + 1} failed; restarting Aether (retry {attempt + 1}/{StartRetries})...");
                    try { await Task.Delay(TimeSpan.FromSeconds(attempt + 1), ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                }
                catch (Exception ex)
                {
                    last = new WarpStartException(
                        "Cloudflare WARP could not start. Check your connection or turn WARP off and try again.", ex);
                    StopAll();
                    if (attempt >= StartRetries) break;
                    SimpleLogger.Log($"[WARP] attempt {attempt + 1}/{StartRetries + 1} failed; restarting Aether (retry {attempt + 1}/{StartRetries})...");
                    try { await Task.Delay(TimeSpan.FromSeconds(attempt + 1), ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                }
            }

            throw last ?? new WarpStartException(
                "Cloudflare WARP could not start. Check your connection or turn WARP off and try again.");
        }

        public static async Task EnsureEndpointAsync(WarpEndpoint endpoint, CancellationToken ct = default)
        {
            if (endpoint == null) return;
            WarpPlan plan;
            string aether;
            int generation;
            lock (Sync)
            {
                plan = _currentPlan!;
                if (plan == null || !plan.Active)
                    throw new WarpStartException("Cloudflare WARP is not running.");
                if (endpoint.Started) return;
                aether = AppPath(@"Data\aether\aether.exe");
                generation = _generation;
            }
            if (!File.Exists(aether))
                throw new WarpStartException("Cloudflare WARP is not installed correctly.") { Retryable = false };
            if (string.IsNullOrWhiteSpace(endpoint.InstanceDir))
                endpoint.InstanceDir = AppPath(Path.Combine("Data", "aether", "instances", endpoint.InstanceKey));
            await StartEndpointAsync(endpoint, plan.Protocol, aether, generation, ct).ConfigureAwait(false);
            endpoint.Started = true;
        }

        public static void StopAll()
        {
            List<AetherRuntime> runtimes;
            Process? shim;
            lock (Sync)
            {
                _stopping = true;
                _generation++;
                _currentPlan = null;
                runtimes = Runtimes.ToList();
                Runtimes.Clear();
                shim = _shimProcess;
                _shimProcess = null;
            }

            foreach (var runtime in runtimes)
                Kill(runtime.Process, runtime.Tail);

            Kill(shim, null);
            TunnelResolver.WarpDoh = null;
            TunnelResolver.Clear();
            lock (Sync) _stopping = false;
        }

        private static IReadOnlyList<string> ResolveThroughWarp(string host, string source)
        {
            var plan = CurrentPlan;
            if (plan == null || !plan.Active) return Array.Empty<string>();
            var endpoint = plan.ForAdapter("", source);
            if (endpoint == null && string.IsNullOrWhiteSpace(source))
                endpoint = plan.Default;
            endpoint ??= plan.Default ?? plan.Endpoints.FirstOrDefault();
            return endpoint == null
                ? Array.Empty<string>()
                : BootstrapDns.ResolveViaProxy(host, endpoint.ProxyUrl);
        }

        private static void StartShim(WarpPlan plan, string xray)
        {
            if (!File.Exists(xray))
                throw new WarpStartException("The WARP adapter shim is missing.") { Retryable = false };

            string path = AppPath(@"Data\aether\runtime\shim.json");
            WarpShim.Write(WarpShim.BuildConfig(plan), path);
            var psi = new ProcessStartInfo
            {
                FileName = xray,
                WorkingDirectory = Path.GetDirectoryName(path) ?? AppPath(@"Data\aether\runtime"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(path);

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { _ = e.Data; };
            process.ErrorDataReceived += (_, e) => { _ = e.Data; };
            process.Start();
            try { JobManager.AddProcess(process); } catch (Exception ex) { SimpleLogger.Log(ex); }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            lock (Sync) _shimProcess = process;

            foreach (var endpoint in plan.Endpoints)
            {
                if (endpoint.ShimPort <= 0) continue;
                if (!WaitPort(endpoint.ShimPort, 15000))
                    throw new WarpStartException("The WARP adapter shim did not start.");
            }
        }

        private static async Task StartEndpointAsync(
            WarpEndpoint endpoint, string protocol, string aether, int generation, CancellationToken ct)
        {
            Directory.CreateDirectory(endpoint.InstanceDir);
            var runtime = StartAetherProcess(endpoint, protocol, aether, generation);
            await WaitPortAsync(endpoint.SocksPort, ReadyTimeoutMs, ct).ConfigureAwait(false);
            await ProbeAsync(endpoint, ct).ConfigureAwait(false);
            endpoint.Started = true;
        }

        private static AetherRuntime StartAetherProcess(
            WarpEndpoint endpoint, string protocol, string aether, int generation)
        {
            var psi = new ProcessStartInfo
            {
                FileName = aether,
                WorkingDirectory = endpoint.InstanceDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (string arg in WarpCommand.Build(endpoint, protocol))
                psi.ArgumentList.Add(arg);
            psi.Environment["AETHER_MASQUE_MTU"] = MasqueMtu.ToString();

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var runtime = new AetherRuntime
            {
                Endpoint = endpoint,
                Process = process,
                Generation = generation,
                AetherPath = aether
            };
            process.OutputDataReceived += (_, e) => OnAetherLine(runtime, e.Data);
            process.ErrorDataReceived += (_, e) => OnAetherLine(runtime, e.Data);
            process.Start();
            try { JobManager.AddProcess(process); } catch (Exception ex) { SimpleLogger.Log(ex); }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.Exited += (_, _) => OnAetherExit(runtime);
            lock (Sync) Runtimes.Add(runtime);
            return runtime;
        }

        private static async Task ProbeAsync(WarpEndpoint endpoint, CancellationToken ct)
        {
            Exception? last = null;
            for (int attempt = 1; attempt <= ProbeAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var handler = new SocketsHttpHandler
                    {
                        Proxy = new WebProxy(endpoint.ProxyUrl),
                        UseProxy = true,
                        ConnectTimeout = TimeSpan.FromSeconds(10)
                    };
                    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
                    string trace = await client.GetStringAsync(TraceUrl, ct).ConfigureAwait(false);
                    if (trace.IndexOf("warp=on", StringComparison.OrdinalIgnoreCase) >= 0)
                        return;
                    last = new Exception("the trace did not report warp=on");
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                    last = ex;
                }
                if (attempt < ProbeAttempts)
                {
                    try { await Task.Delay(1500, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                }
            }
            throw new WarpStartException(
                "Cloudflare WARP could not connect. Check your connection or turn WARP off and try again.",
                last ?? new Exception("warp probe failed"))
            {
                Detail = (last?.Message ?? "") + TailText(endpoint.Tag)
            };
        }

        private static bool WaitPort(int port, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                using var client = new TcpClient();
                try
                {
                    var task = client.ConnectAsync(IPAddress.Loopback, port);
                    if (task.Wait(250) && client.Connected) return true;
                }
                catch { }
                Thread.Sleep(150);
            }
            return false;
        }

        private static async Task WaitPortAsync(int port, int timeoutMs, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                using var client = new TcpClient();
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(500);
                    await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token).ConfigureAwait(false);
                    if (client.Connected) return;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                catch { }
                await Task.Delay(150, ct).ConfigureAwait(false);
            }
            throw new WarpStartException("Cloudflare WARP did not start in time.");
        }

        private static void OnAetherLine(AetherRuntime runtime, string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            string text = line.Trim();
            while (runtime.Tail.Count >= 40) runtime.Tail.TryDequeue(out _);
            runtime.Tail.Enqueue(text);
        }

        private static void OnAetherExit(AetherRuntime runtime)
        {
            bool intentional;
            lock (Sync)
            {
                intentional = _stopping || runtime.Generation != _generation;
                if (!intentional) Runtimes.Remove(runtime);
            }
            if (intentional) return;
            SimpleLogger.Log("[WARP] connection lost; reconnecting.");
            _ = Task.Run(() => RestartAsync(runtime));
        }

        private static async Task RestartAsync(AetherRuntime old)
        {
            string protocol;
            int generation;
            lock (Sync)
            {
                if (_stopping || _currentPlan == null) return;
                protocol = _currentPlan.Protocol;
                generation = _generation;
            }

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try { await Task.Delay(attempt == 1 ? 1000 : 3000).ConfigureAwait(false); }
                catch { return; }
                lock (Sync)
                {
                    if (_stopping || generation != _generation || _currentPlan == null) return;
                }
                try
                {
                    Directory.CreateDirectory(old.Endpoint.InstanceDir);
                    StartAetherProcess(old.Endpoint, protocol, old.AetherPath, generation);
                    await WaitPortAsync(old.Endpoint.SocksPort, ReadyTimeoutMs, CancellationToken.None).ConfigureAwait(false);
                    await ProbeAsync(old.Endpoint, CancellationToken.None).ConfigureAwait(false);
                    SimpleLogger.Log("[WARP] reconnected.");
                    return;
                }
                catch
                {
                }
            }
            UiEventBus.Instance.PublishToast(AppStrings.ToastWarpConnectionLost, false);
        }

        private static void Kill(Process? process, ConcurrentQueue<string>? tail)
        {
            if (process == null) return;
            try { if (!process.HasExited) process.Kill(true); } catch { }
            try { process.WaitForExit(2000); } catch { }
            try { process.Dispose(); } catch { }
        }

        private static string TailText(string tag)
        {
            List<string> lines;
            lock (Sync)
            {
                var runtime = Runtimes.FirstOrDefault(item =>
                    string.Equals(item.Endpoint.Tag, tag, StringComparison.OrdinalIgnoreCase));
                lines = runtime?.Tail.ToList() ?? new List<string>();
            }
            if (lines.Count == 0) return "";
            return " | " + string.Join(" | ", lines.Skip(Math.Max(0, lines.Count - 6)));
        }

        private static string Describe(WarpPlan plan)
        {
            var adapters = plan.Endpoints
                .Where(endpoint => !endpoint.IsDefault)
                .Select(endpoint => endpoint.AdapterName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return adapters.Count == 0
                ? plan.Protocol
                : adapters.Count == 1
                    ? plan.Protocol + " via " + adapters[0]
                    : plan.Protocol + " via " + adapters.Count + " adapters";
        }

        private static string AppPath(string relPath)
        {
            var window = MainWindow.Instance;
            if (window != null) return window.GetAppPath(relPath);
            return Path.Combine(AppContext.BaseDirectory, relPath);
        }
    }
}

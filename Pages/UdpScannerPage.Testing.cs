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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using CrimsonX.Localization;
using CrimsonX.Services;

namespace CrimsonX.Pages
{
    public partial class UdpScannerPage
    {
        private readonly List<string> _adapterIps = new List<string> { "" };
        private readonly List<string> _adapterNames = new List<string> { "" };
        private Task? _scanTask;
        private int _lastGoal = 10;
        private int _lastPadded;
        private int _testedCount;
        private bool _scanStarting;
        private bool _startAborted;
        private DateTime _scanStartedAt;
        private readonly List<ConfigTestResult> _overLimit = new List<ConfigTestResult>();

        // ── Scanning ──

        private async void Start_Click(object? sender, RoutedEventArgs e)
        {
            if (_isScanning)
            {
                if ((DateTime.UtcNow - _scanStartedAt).TotalMilliseconds < 800)
                {
                    SimpleLogger.Log("[UdpScanner] Ignoring stop request right after start");
                    return;
                }

                RequestStopScan();
                return;
            }

            if (_scanStarting)
            {
                SimpleLogger.Log("[UdpScanner] Second start ignored - the previous run is still finishing");
                return;
            }

            var previous = _scanTask;
            if (previous != null && !previous.IsCompleted)
            {
                _scanStarting = true;
                _startAborted = false;
                SetScanning(true);

                try { await previous; } catch { }
                finally { _scanStarting = false; }

                if (_startAborted)
                {
                    _startAborted = false;
                    SetScanning(false);
                    RefreshStatusText();
                    return;
                }
            }

            var current = RunScanAsync();
            _scanTask = current;
            await current;
        }
        private void RequestStopScan()
        {
            if (_scanStarting) _startAborted = true;

            _isScanning = false;

            StopDots();

            var start = this.FindControl<Button>("btnStart");
            if (start != null) AppStrings.ApplyBtn(start, AppStrings.UdpScannerStart);

            StopScanning();

            UpdateStatus(AppStrings.UdpScannerStopped);
        }
        private void UpdateScanProgress()
        {
            if (!_isScanning) return;
            UpdateStatus(string.Format(AppStrings.UdpScannerScanning, _items.Count, _lastGoal, _testedCount));
        }
        private void RefreshStatusText()
        {
            if (_isScanning) UpdateScanProgress();
            else if (_items.Count == 0) UpdateStatus(AppStrings.UdpScannerIdle);
            else if (_lastPadded > 0) UpdateStatus(string.Format(AppStrings.UdpScannerFoundPadded, _items.Count, _items.Count - _lastPadded));
            else UpdateStatus(string.Format(AppStrings.UdpScannerFound, _items.Count));
        }

        private async Task RunScanAsync()
        {
            int goal = SelectedAmount();
            int concurrency = SelectedConcurrency();
            int discardPing = SelectedDiscardPing();
            string adapterIp = SelectedAdapterIp();

            _lastGoal = goal;

            _items.Clear();
            _seen.Clear();
            _overLimit.Clear();
            _testedCount = 0;
            RefreshResults();
            UpdateStatus(string.Format(AppStrings.UdpScannerScanning, 0, goal, 0));

            StopScanning();
            _scanCts?.Dispose();
            _scanCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            var ct = _scanCts.Token;

            SetScanning(true);
            _scanStartedAt = DateTime.UtcNow;
            bool cancelled = false;
            bool failed = false;
            int padded = 0;
            var scanWatch = Stopwatch.StartNew();

            try
            {
                int sourceIndex = -1;
                while (sourceIndex < Main.ScanWorkerSourceCount && _items.Count < goal)
                {
                    ct.ThrowIfCancellationRequested();

                    List<string> configs;
                    try
                    {
                        configs = await Main.FetchScanConfigsAsync(sourceIndex, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        SimpleLogger.Log($"[UdpScanner] Fetch source {sourceIndex} failed: {ex.Message}");
                        configs = new List<string>();
                    }

                    if (configs == null || configs.Count == 0)
                    {
                        SimpleLogger.Log($"[UdpScanner] Source {sourceIndex} handed over no candidates");
                        sourceIndex++;
                        continue;
                    }

                    var queue = new ConcurrentQueue<string>(configs);
                    var tasks = new List<Task<ConfigTestResult>>();

                    while (_items.Count < goal && queue.TryDequeue(out string? cfgLink))
                    {
                        ct.ThrowIfCancellationRequested();
                        tasks.Add(ConfigTester.TestUdpOnlyAsync(cfgLink!, Main.Config, ct, adapterIp));

                        if (tasks.Count < concurrency && !queue.IsEmpty) continue;

                        var results = await Task.WhenAll(tasks);
                        tasks.Clear();

                        _testedCount += results.Length;
                        UpdateScanProgress();

                        foreach (var r in results)
                        {
                            if (!r.Success || !r.UdpOk) continue;
                            if (string.IsNullOrWhiteSpace(r.OutboundJson) || _seen.Contains(r.OutboundJson)) continue;
                            if (!Main.IsConfigAllowedForScan(r)) continue;

                            if (discardPing > 0 && r.Ping > discardPing)
                            {
                                _seen.Add(r.OutboundJson);
                                _overLimit.Add(r);
                                continue;
                            }

                            _seen.Add(r.OutboundJson);
                            AddResult(r);

                            if (_isScanning) UpdateScanProgress();
                            if (_items.Count >= goal) break;
                        }
                    }

                    if (tasks.Count > 0)
                    {
                        try { await Task.WhenAll(tasks); } catch { }
                        tasks.Clear();
                    }

                    sourceIndex++;
                }

                if (_items.Count < goal && _overLimit.Count > 0)
                {
                    _overLimit.Sort((a, b) => a.Ping.CompareTo(b.Ping));

                    foreach (var r in _overLimit)
                    {
                        if (_items.Count >= goal) break;
                        AddResult(r, overLimit: true);
                        padded++;
                    }

                    SimpleLogger.Log($"[UdpScanner] Amount not reached below {discardPing} ms - filled {padded} slot(s) with slower configs");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                cancelled = true;
            }
            catch (OperationCanceledException ex)
            {
                failed = true;
                SimpleLogger.Log($"[UdpScanner] A test reported a timeout as a cancellation: {ex.Message}");
            }
            catch (Exception ex)
            {
                failed = true;
                SimpleLogger.Log($"[UdpScanner] Scan failed: {ex.Message}");
            }
            finally
            {
                SetScanning(false);
            }

            if (_items.Count > 0)
            {
                _items.Sort((a, b) => a.Ping.CompareTo(b.Ping));
                RefreshResults();
            }

            _lastPadded = padded;

            bool goalReached = _items.Count >= _lastGoal;

            string reason = cancelled ? "cancelled"
                          : failed ? "aborted by an error"
                          : padded > 0 ? $"sources exhausted - {padded} slower config(s) filled the list"
                          : goalReached ? "goal reached"
                          : "all sources tested - nothing left to test";

            SimpleLogger.Log($"[UdpScanner] Scan finished: {_items.Count}/{_lastGoal} configs in {scanWatch.Elapsed.TotalSeconds:0.0}s ({reason})" +
                             $"{(discardPing > 0 ? $", discard limit {discardPing} ms" : "")}");

            if (cancelled) UpdateStatus(AppStrings.UdpScannerStopped);
            else if (_items.Count == 0) UpdateStatus(AppStrings.UdpScannerNone);
            else if (padded > 0) UpdateStatus(string.Format(AppStrings.UdpScannerFoundPadded, _items.Count, _items.Count - padded));
            else if (goalReached) UpdateStatus(string.Format(AppStrings.UdpScannerFound, _items.Count));
            else UpdateStatus(string.Format(AppStrings.UdpScannerGoalNotReached, _items.Count, _lastGoal));
        }

        // ── Options ──

        private int SelectedAmount()
        {
            int index = this.FindControl<ComboBox>("cmbAmount")?.SelectedIndex ?? 1;
            return index == 0 ? 5 : index == 2 ? 15 : 10;
        }

        private int SelectedConcurrency()
        {
            int index = this.FindControl<ComboBox>("cmbConcurrency")?.SelectedIndex ?? 0;
            return index == 1 ? 10 : 5;
        }
        private int SelectedDiscardPing()
        {
            int index = this.FindControl<ComboBox>("cmbDiscard")?.SelectedIndex ?? 0;
            return index <= 0 ? 0 : index * 100;
        }

        private void SetScanning(bool scanning)
        {
            _isScanning = scanning;

            if (scanning) StartDots(); else StopDots();

            var start = this.FindControl<Button>("btnStart");
            if (start != null)
                AppStrings.ApplyBtn(start, scanning ? AppStrings.UdpScannerStop : AppStrings.UdpScannerStart);

            var amount = this.FindControl<ComboBox>("cmbAmount");
            if (amount != null) amount.IsEnabled = !scanning;

            var concurrency = this.FindControl<ComboBox>("cmbConcurrency");
            if (concurrency != null) concurrency.IsEnabled = !scanning;

            var discard = this.FindControl<ComboBox>("cmbDiscard");
            if (discard != null) discard.IsEnabled = !scanning;

            var adapter = this.FindControl<ComboBox>("cmbAdapter");
            if (adapter != null) adapter.IsEnabled = !scanning;
        }

        private void StopScanning()
        {
            try { _scanCts?.Cancel(); } catch { }
        }

        // ── Test adapter ──

        private void LoadAdapters()
        {
            var cmb = this.FindControl<ComboBox>("cmbAdapter");
            if (cmb == null) return;

            _loadingOptions = true;

            while (cmb.Items.Count > 1) cmb.Items.RemoveAt(cmb.Items.Count - 1);
            _adapterIps.Clear();
            _adapterIps.Add("");
            _adapterNames.Clear();
            _adapterNames.Add("");

            try
            {
                foreach (var adapter in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (adapter.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

                    string ip = "";
                    foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            ip = unicast.Address.ToString();
                            break;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(ip)) continue;

                    _adapterIps.Add(ip);
                    _adapterNames.Add(adapter.Name);

                    var item = new ComboBoxItem
                    {
                        Content = adapter.Name,
                        FontSize = 10,
                        FontWeight = FontWeight.SemiBold
                    };
                    ToolTip.SetTip(item, $"{adapter.Name} - {ip}");
                    cmb.Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Log($"[UdpScanner] Adapter scan failed: {ex.Message}");
            }

            _loadingOptions = true;
            try
            {
                cmb.SelectedIndex = 0;

                var cfg = Main.Config;
                if (!string.IsNullOrWhiteSpace(cfg.UdpScanAdapterIp))
                {
                    int byIp = _adapterIps.FindIndex(ip => ip == cfg.UdpScanAdapterIp);
                    if (byIp > 0) cmb.SelectedIndex = byIp;
                }
                else if (!string.IsNullOrWhiteSpace(cfg.UdpScanAdapterName))
                {
                    int byName = _adapterNames.FindIndex(n => string.Equals(n, cfg.UdpScanAdapterName, StringComparison.OrdinalIgnoreCase));
                    if (byName > 0) cmb.SelectedIndex = byName;
                }
            }
            finally
            {
                _loadingOptions = false;
            }
        }
        private string SelectedAdapterIp()
        {
            int index = this.FindControl<ComboBox>("cmbAdapter")?.SelectedIndex ?? 0;
            return index > 0 && index < _adapterIps.Count ? _adapterIps[index] : "";
        }

        private string SelectedAdapterName()
        {
            int index = this.FindControl<ComboBox>("cmbAdapter")?.SelectedIndex ?? 0;
            return index > 0 && index < _adapterNames.Count ? _adapterNames[index] : "";
        }
    }
}

/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrimsonX.Services;

namespace CrimsonX
{
    public partial class MainWindow
    {
    // ── UDP Scanner Support ──

        internal async Task<List<string>> FetchScanConfigsAsync(int index, CancellationToken ct)
        {
            if (index < 0)
            {
                string cachePath = GetAppPath(@"Data\cache\cache.bin");
                return await Task.Run(() => ScrubScanConfigs(
                    CrimsonX.Services.ConfigCache.LoadCache(cachePath)
                        .Where(c => !CrimsonX.Services.XrayLinkParser.IsGrpcOutbound(c))
                        .ToList(), _cfg.CustomConfig1, _cfg.CustomConfig2), ct);
            }
            List<string> configs = await FetchConfigsFromWorker(index, ct);
            return await Task.Run(() => ScrubScanConfigs(configs, _cfg.CustomConfig1, _cfg.CustomConfig2), ct);
        }

        private static List<string> ScrubScanConfigs(List<string>? configs, string? custom1, string? custom2)
        {
            if (configs == null) return new List<string>();
            if (!string.IsNullOrWhiteSpace(custom1) && CrimsonX.Services.XrayLinkParser.TryParseCustomConfig(custom1!, out string c1Json))
            {
                configs.RemoveAll(c => c == c1Json || c.Contains(c1Json));
            }
            if (!string.IsNullOrWhiteSpace(custom2) && CrimsonX.Services.XrayLinkParser.TryParseCustomConfig(custom2!, out string c2Json))
            {
                configs.RemoveAll(c => c == c2Json || c.Contains(c2Json));
            }
            return configs;
        }

        internal bool IsConfigAllowedForScan(ConfigTestResult r) => IsConfigAllowed(r);

        internal int ScanWorkerSourceCount => WorkerSourceCount;
    }
}

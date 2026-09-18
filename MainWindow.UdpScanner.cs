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
            List<string> configs;

            if (index < 0)
            {
                configs = CrimsonX.Services.ConfigCache.LoadCache(GetAppPath(@"Data\cache\cache.bin"));
                configs = configs.Where(c => !CrimsonX.Services.XrayLinkParser.IsGrpcOutbound(c)).ToList();
            }
            else
            {
                configs = await FetchConfigsFromWorker(index, ct);
            }

            if (configs == null) return new List<string>();

            if (!string.IsNullOrWhiteSpace(_cfg.CustomConfig1) && CrimsonX.Services.XrayLinkParser.TryParseLink(_cfg.CustomConfig1, out string c1Json))
            {
                configs.RemoveAll(c => c == c1Json || c.Contains(c1Json));
            }
            if (!string.IsNullOrWhiteSpace(_cfg.CustomConfig2) && CrimsonX.Services.XrayLinkParser.TryParseLink(_cfg.CustomConfig2, out string c2Json))
            {
                configs.RemoveAll(c => c == c2Json || c.Contains(c2Json));
            }

            return configs;
        }

        internal bool IsConfigAllowedForScan(ConfigTestResult r) => IsConfigAllowed(r);

        internal int ScanWorkerSourceCount => WorkerSourceCount;
    }
}

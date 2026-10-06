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

namespace CrimsonX.Services
{
    public sealed class AppOptimizeChoice
    {
        public string RuleId { get; set; } = "";
        public bool Chosen { get; set; }
        public string PrevTcpRouting { get; set; } = "";
        public string PrevUdpRouting { get; set; } = "";
        public string PrevTcpAdapter { get; set; } = "";
        public string PrevUdpAdapter { get; set; } = "";
        public string PrevCustomProxyRaw { get; set; } = "";
        public string PrevCustomProxyLabel { get; set; } = "";
    }

    public sealed class AppOptimizeState
    {
        public bool OnConnect { get; set; }
        public bool Ready { get; set; }
        public string FoundRaw { get; set; } = "";
        public string FoundLabel { get; set; } = "";
        public long FoundPingMs { get; set; }
        public string AdapterName { get; set; } = "Default";
        public string AdapterIp { get; set; } = "";
        public List<AppOptimizeChoice> Choices { get; set; } = new List<AppOptimizeChoice>();
    }

    public static class AppOptimizeStore
    {
        private const string FileName = "app_optimize.bin";

        public static AppOptimizeState Load()
        {
            try
            {
                var state = SecureJsonStore.Load<AppOptimizeState>(SecureJsonStore.PathFor(FileName));
                if (state == null) return new AppOptimizeState();
                if (state.Choices == null) state.Choices = new List<AppOptimizeChoice>();
                state.Choices.RemoveAll(c => c == null || string.IsNullOrEmpty(c.RuleId));
                return state;
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                return new AppOptimizeState();
            }
        }

        public static void Save(AppOptimizeState state)
        {
            if (state == null) return;
            try
            {
                SecureJsonStore.Save(SecureJsonStore.PathFor(FileName), state);
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
        }
    }
}

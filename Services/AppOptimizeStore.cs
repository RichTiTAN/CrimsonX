/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
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
        public string AppliedRaw { get; set; } = "";
    }

    public sealed class AppOptimizeState
    {
        public bool OnConnect { get; set; }
        public bool AlternateMode { get; set; }
        public bool Ready { get; set; }
        public string FoundRaw { get; set; } = "";
        public string FoundLabel { get; set; } = "";
        public long FoundPingMs { get; set; }
        public string AdapterName { get; set; } = "Default";
        public string AdapterIp { get; set; } = "";
        public string Region { get; set; } = "";
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

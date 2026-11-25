/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using System.Collections.Generic;
using System.Linq;

namespace CrimsonX.Services
{
    public static class ConfigSelectionPolicy
    {
        public const int MinimumConnectedConfigs = 2;
        public const int PreferredConnectedConfigs = 5;

        public static bool HasFlow(ConfigTestResult? result)
            => result != null && result.Flowed;

        public static bool HasMeasuredSpeed(ConfigTestResult? result)
            => result != null && result.Flowed && result.Speed > 0;

        public static bool NeedsMoreFlow(int flowed)
            => flowed < PreferredConnectedConfigs;

        public static bool CanConnect(int measured, int fallback)
            => measured + fallback >= MinimumConnectedConfigs;

        public static bool ShouldUseFallback(int measured)
            => measured < MinimumConnectedConfigs;

        public static List<ConfigTestResult> SelectForConnection(IEnumerable<ConfigTestResult> ranked, int count)
            => ranked == null
                ? new List<ConfigTestResult>()
                : ranked.Where(r => HasFlow(r)).Take(Math.Max(0, count)).ToList();
    }

    public sealed class NoFlowingConfigException : Exception
    {
        public NoFlowingConfigException(string message) : base(message) { }
    }
}

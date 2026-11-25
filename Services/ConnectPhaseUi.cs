/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;

namespace CrimsonX.Services
{
    public enum StatusKind
    {
        Idle,

        ComingUp,

        Connected
    }

    public static class ConnectPhaseUi
    {
        public static bool IsComingUp(bool isConnected, bool isEngineRunning, bool isReconnecting)
            => isReconnecting || (isEngineRunning && !isConnected);

        public static bool ShouldShowApplyChanges(bool isConnected, bool isReconnecting, bool hasPendingRuleChanges, string? mode)
            => isConnected
            && !isReconnecting
            && hasPendingRuleChanges
            && string.Equals(mode, "VPN Mode", StringComparison.OrdinalIgnoreCase);

        public static bool CanApplyChanges(bool isOffered, bool isReconnecting) => isOffered && !isReconnecting;

        public static bool ConnectClickMeansDisconnect(bool isConnected, bool isEngineRunning, bool isReconnecting)
            => isConnected || isEngineRunning || isReconnecting;

        public static StatusKind Status(bool isConnected, bool isEngineRunning, bool isReconnecting)
            => IsComingUp(isConnected, isEngineRunning, isReconnecting) ? StatusKind.ComingUp
             : isConnected                                               ? StatusKind.Connected
             :                                                             StatusKind.Idle;

        public static double Approach(double current, double target)
        {
            double gap = target - current;
            if (Math.Abs(gap) < 0.5) return target;
            return current + gap * 0.22;
        }

        public const double BreathPeriodSeconds = 1.6;

        public static double Breath(double secondsIntoPhase)
        {
            if (double.IsNaN(secondsIntoPhase) || secondsIntoPhase <= 0) return 0;
            double phase = (secondsIntoPhase % BreathPeriodSeconds) / BreathPeriodSeconds;   // 0..1
            return 0.5 - 0.5 * Math.Cos(2 * Math.PI * phase);
        }
    }

    public sealed class ConnectBreath
    {
        public const double PeakOpacity = 0.9;

        private DateTime? _startedUtc;

        public double Next(DateTime nowUtc, bool comingUp)
        {
            if (!comingUp)
            {
                _startedUtc = null;
                return 0;
            }
            _startedUtc ??= nowUtc;
            return ConnectPhaseUi.Breath((nowUtc - _startedUtc.Value).TotalSeconds) * PeakOpacity;
        }
    }
}

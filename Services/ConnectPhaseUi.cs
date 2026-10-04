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

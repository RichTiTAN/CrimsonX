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
    public static class ConnectProgress
    {
        public const int Starting = 5;
        public const int Preparing = 10;
        public const int Discovery = 15;
        public const int SpeedStart = 65;
        public const int SpeedComplete = 82;
        public const int EngineStart = 85;
        public const int ExitNode = 95;
        public const int Connected = 100;

        public static int Flow(int count)
            => 20 + Math.Min(40, Math.Max(0, count) * 8);

        public static int Speed(int completed, int total)
            => total <= 0
                ? SpeedStart
                : SpeedStart + Math.Min(15, (int)(15.0 * Math.Clamp(completed, 0, total) / total));
    }
}

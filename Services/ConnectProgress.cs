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

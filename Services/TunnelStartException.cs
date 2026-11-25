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
    public sealed class TunnelStartException : Exception
    {
        public TunnelStartException(string message, string detail) : base(message)
            => Detail = detail ?? "";

        public string Detail { get; }
    }
}

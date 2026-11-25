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
    public sealed class DnsState
    {
        public string   AdapterName { get; init; } = "";

        public bool     WasDhcp     { get; init; }

        public string[] Servers     { get; init; } = Array.Empty<string>();
    }
}

/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using CrimsonX.Models;

namespace CrimsonX.Services
{
    public static class CustomConfigSelection
    {
        public static string Raw(AppConfig? config, int slot)
            => (slot == 2 ? config?.CustomConfig2 : config?.CustomConfig1) ?? "";

        public static bool HasRaw(AppConfig? config, int slot)
            => !string.IsNullOrWhiteSpace(Raw(config, slot));

        public static bool SlotEnabled(AppConfig? config, int slot)
            => config != null
               && HasRaw(config, slot)
               && (slot == 2 ? config.CustomConfig2Enabled : config.CustomConfig1Enabled);

        public static bool IsSlotActive(AppConfig? config, int slot)
            => config != null && config.EnableCustomConfigs && SlotEnabled(config, slot);

        public static int ActiveCount(AppConfig? config)
        {
            int count = 0;
            if (IsSlotActive(config, 1)) count++;
            if (IsSlotActive(config, 2)) count++;
            return count;
        }

        public static bool AnyTunnelRaw(AppConfig? config)
            => TunnelConfigParser.LooksLikeTunnel(Raw(config, 1))
               || TunnelConfigParser.LooksLikeTunnel(Raw(config, 2));

        public static bool AnySlotEnabled(AppConfig? config)
            => SlotEnabled(config, 1) || SlotEnabled(config, 2);
    }
}

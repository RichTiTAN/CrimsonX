/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

namespace CrimsonX.Models
{
    public class AppGameRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public bool IsEnabled { get; set; } = true;

        public bool IsPinned { get; set; } = false;

        public string AppType { get; set; } = "Game";

        public string ExeName { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public string IconBase64 { get; set; } = "";

        public string IconAsset { get; set; } = "";

        public string DefaultKey { get; set; } = "";

        public List<string> ProcessNames { get; set; } = new List<string>();

        public List<string> Domains { get; set; } = new List<string>();

        public string Country { get; set; } = "";

        public string Region { get; set; } = "";

        public string TcpRouting { get; set; } = "Proxy";

        public string UdpRouting { get; set; } = "Direct";

        public string TcpAdapter { get; set; } = "Default";

        public string UdpAdapter { get; set; } = "Default";

        public string CustomProxyRaw { get; set; } = "";

        public string CustomProxyLabel { get; set; } = "";
    }
}

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

namespace CrimsonX.Models
{
    public class AppGameRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public bool IsEnabled { get; set; } = true;

        public bool IsPinned { get; set; } = false;

        public string AppType { get; set; } = "Game";

        public string ExeName { get; set; } = "";

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
    }
}

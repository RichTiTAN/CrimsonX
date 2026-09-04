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

namespace CrimsonX.Services
{
    public sealed class GeoTraceResult
    {
        public string Country       { get; init; } = "";
        public string Continent     { get; init; } = "";
        public string CountryCode   { get; init; } = "";
        public string ContinentCode { get; init; } = "";
        public long   PingMs        { get; init; }
    }

    public sealed class StatsSnapshot
    {
        public string   SpeedUp      { get; init; } = "0 KB/s";
        public string   SpeedDn      { get; init; } = "0 KB/s";
        public long     DiffUpBytes  { get; init; }
        public long     DiffDnBytes  { get; init; }
        public double[] UpHistory    { get; init; } = System.Array.Empty<double>();
        public double[] DnHistory    { get; init; } = System.Array.Empty<double>();
    }
}

/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

namespace CrimsonX.Services
{
    public enum GeoFailure
    {
        None,
        Timeout,
        Failed,
        Cancelled
    }

    public sealed class GeoTraceResult
    {
        public string Country       { get; init; } = "";
        public string Continent     { get; init; } = "";
        public string CountryCode   { get; init; } = "";
        public string ContinentCode { get; init; } = "";
        public string Ip            { get; init; } = "";
        public long   PingMs        { get; init; }
        public string Error         { get; init; } = "";
        public GeoFailure Failure   { get; init; }
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

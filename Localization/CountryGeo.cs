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
using System.Collections.Generic;
using System.Linq;

namespace CrimsonX.Localization
{
    public static class CountryGeo
    {
        public static readonly Dictionary<string, (double Lat, double Lon)> Map =
            new Dictionary<string, (double Lat, double Lon)>(StringComparer.OrdinalIgnoreCase)
        {
            ["AE"] = (23.4, 53.8),
            ["AF"] = (33.9, 67.7),
            ["AL"] = (41.2, 20.2),
            ["AM"] = (40.1, 45.0),
            ["AO"] = (-11.2, 17.9),
            ["AR"] = (-38.4, -63.6),
            ["AT"] = (47.5, 14.6),
            ["AU"] = (-25.3, 133.8),
            ["AZ"] = (40.1, 47.6),
            ["BA"] = (43.9, 17.7),
            ["BD"] = (23.7, 90.4),
            ["BE"] = (50.5, 4.5),
            ["BF"] = (12.2, -1.6),
            ["BG"] = (42.7, 25.5),
            ["BI"] = (-3.4, 29.9),
            ["BJ"] = (9.3, 2.3),
            ["BN"] = (4.5, 114.7),
            ["BO"] = (-16.3, -63.6),
            ["BR"] = (-14.2, -51.9),
            ["BS"] = (25.0, -77.4),
            ["BT"] = (27.5, 90.4),
            ["BW"] = (-22.3, 24.7),
            ["BY"] = (53.7, 27.9),
            ["BZ"] = (17.2, -88.5),
            ["CA"] = (56.1, -106.3),
            ["CD"] = (-4.0, 21.8),
            ["CF"] = (6.6, 20.9),
            ["CG"] = (-0.2, 15.8),
            ["CH"] = (46.8, 8.2),
            ["CI"] = (7.5, -5.5),
            ["CL"] = (-35.7, -71.5),
            ["CM"] = (7.4, 12.4),
            ["CN"] = (35.9, 104.2),
            ["CO"] = (4.6, -74.3),
            ["CR"] = (9.7, -84.2),
            ["CU"] = (21.5, -78.0),
            ["CY"] = (35.1, 33.4),
            ["CZ"] = (49.8, 15.5),
            ["DE"] = (51.2, 10.4),
            ["DJ"] = (11.8, 42.6),
            ["DK"] = (56.3, 9.5),
            ["DO"] = (18.7, -70.2),
            ["DZ"] = (28.0, 1.7),
            ["EC"] = (-1.8, -78.2),
            ["EE"] = (58.6, 25.0),
            ["EG"] = (26.8, 30.8),
            ["EH"] = (24.2, -12.9),
            ["ER"] = (15.2, 39.8),
            ["ES"] = (40.5, -3.7),
            ["ET"] = (9.1, 40.5),
            ["FI"] = (61.9, 25.7),
            ["FJ"] = (-17.7, 178.1),
            ["FK"] = (-51.8, -59.0),
            ["FR"] = (46.2, 2.2),
            ["GA"] = (-0.8, 11.6),
            ["GB"] = (54.0, -2.9),
            ["GE"] = (42.3, 43.4),
            ["GH"] = (7.9, -1.0),
            ["GL"] = (71.7, -42.6),
            ["GM"] = (13.4, -15.3),
            ["GN"] = (9.9, -9.7),
            ["GQ"] = (1.6, 10.3),
            ["GR"] = (39.1, 21.8),
            ["GT"] = (15.8, -90.2),
            ["GW"] = (12.0, -15.0),
            ["GY"] = (4.9, -58.9),
            ["HK"] = (22.3, 114.2),
            ["HN"] = (15.2, -86.2),
            ["HR"] = (45.1, 15.2),
            ["HT"] = (18.9, -72.7),
            ["HU"] = (47.2, 19.5),
            ["ID"] = (-2.5, 118.0),
            ["IE"] = (53.2, -8.0),
            ["IL"] = (31.0, 34.9),
            ["IN"] = (22.9, 79.6),
            ["IQ"] = (33.2, 43.7),
            ["IR"] = (32.4, 53.7),
            ["IS"] = (64.9, -18.6),
            ["IT"] = (42.8, 12.8),
            ["JM"] = (18.1, -77.3),
            ["JO"] = (31.3, 36.4),
            ["JP"] = (36.2, 138.3),
            ["KE"] = (0.2, 37.9),
            ["KG"] = (41.5, 74.6),
            ["KH"] = (12.7, 104.9),
            ["KP"] = (40.2, 127.5),
            ["KR"] = (36.5, 127.9),
            ["KW"] = (29.3, 47.6),
            ["KZ"] = (48.2, 67.3),
            ["LA"] = (19.4, 102.6),
            ["LB"] = (33.9, 35.9),
            ["LK"] = (7.6, 80.7),
            ["LR"] = (6.5, -9.4),
            ["LS"] = (-29.6, 28.2),
            ["LT"] = (55.2, 23.9),
            ["LU"] = (49.8, 6.1),
            ["LV"] = (56.9, 24.9),
            ["LY"] = (26.3, 17.3),
            ["MA"] = (31.8, -7.1),
            ["MD"] = (47.2, 28.5),
            ["ME"] = (42.7, 19.4),
            ["MG"] = (-19.4, 46.7),
            ["MK"] = (41.6, 21.7),
            ["ML"] = (17.6, -4.0),
            ["MM"] = (21.9, 95.9),
            ["MN"] = (46.9, 103.8),
            ["MR"] = (20.3, -10.3),
            ["MT"] = (35.9, 14.4),
            ["MW"] = (-13.3, 34.3),
            ["MX"] = (23.6, -102.6),
            ["MY"] = (4.2, 101.9),
            ["MZ"] = (-18.7, 35.5),
            ["NA"] = (-22.1, 17.2),
            ["NC"] = (-21.3, 165.6),
            ["NE"] = (17.6, 8.1),
            ["NG"] = (9.1, 8.7),
            ["NI"] = (12.9, -85.2),
            ["NL"] = (52.2, 5.5),
            ["NO"] = (64.6, 12.0),
            ["NP"] = (28.4, 84.1),
            ["NZ"] = (-41.8, 172.8),
            ["OM"] = (21.0, 57.0),
            ["PA"] = (8.5, -80.8),
            ["PE"] = (-9.2, -75.0),
            ["PG"] = (-6.3, 143.9),
            ["PH"] = (12.9, 121.8),
            ["PK"] = (30.4, 69.3),
            ["PL"] = (52.1, 19.4),
            ["PR"] = (18.2, -66.4),
            ["PS"] = (31.9, 35.2),
            ["PT"] = (39.4, -8.2),
            ["PY"] = (-23.4, -58.4),
            ["QA"] = (25.3, 51.2),
            ["RO"] = (45.9, 25.0),
            ["RS"] = (44.2, 20.9),
            ["RU"] = (61.5, 96.0),
            ["RW"] = (-1.9, 29.9),
            ["SA"] = (23.9, 45.1),
            ["SB"] = (-9.6, 160.2),
            ["SD"] = (12.9, 30.2),
            ["SE"] = (62.2, 17.6),
            ["SG"] = (1.35, 103.8),
            ["SI"] = (46.1, 14.8),
            ["SK"] = (48.7, 19.7),
            ["SL"] = (8.5, -11.8),
            ["SN"] = (14.5, -14.5),
            ["SO"] = (5.2, 46.2),
            ["SR"] = (4.1, -55.9),
            ["SS"] = (7.9, 30.2),
            ["SV"] = (13.8, -88.9),
            ["SY"] = (34.8, 39.0),
            ["SZ"] = (-26.5, 31.5),
            ["TD"] = (15.4, 18.7),
            ["TG"] = (8.6, 1.0),
            ["TH"] = (15.1, 101.0),
            ["TJ"] = (38.9, 71.3),
            ["TL"] = (-8.9, 125.7),
            ["TM"] = (39.0, 59.6),
            ["TN"] = (34.1, 9.6),
            ["TR"] = (39.0, 35.2),
            ["TT"] = (10.7, -61.3),
            ["TW"] = (23.7, 121.0),
            ["TZ"] = (-6.4, 34.9),
            ["UA"] = (48.4, 31.2),
            ["UG"] = (1.4, 32.3),
            ["US"] = (39.8, -98.6),
            ["UY"] = (-32.5, -55.8),
            ["UZ"] = (41.4, 64.6),
            ["VE"] = (6.4, -66.6),
            ["VN"] = (14.1, 108.3),
            ["VU"] = (-15.4, 166.9),
            ["XK"] = (42.6, 20.9),
            ["YE"] = (15.6, 48.0),
            ["ZA"] = (-30.6, 22.9),
            ["ZM"] = (-13.1, 27.8),
            ["ZW"] = (-19.0, 29.2),
        };

        public static bool TryGet(string code, out double lat, out double lon)
        {
            lat = 0;
            lon = 0;
            if (string.IsNullOrWhiteSpace(code)) return false;
            if (!Map.TryGetValue(code.Trim(), out var point)) return false;
            lat = point.Lat;
            lon = point.Lon;
            return true;
        }

        public static double DistanceKm(double latA, double lonA, double latB, double lonB)
        {
            const double earthKm = 6371.0;
            double dLat = (latB - latA) * Math.PI / 180.0;
            double dLon = (lonB - lonA) * Math.PI / 180.0;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(latA * Math.PI / 180.0) * Math.Cos(latB * Math.PI / 180.0)
                     * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return earthKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        public static string Nearest(string fromCode, IEnumerable<string> codes)
        {
            if (!TryGet(fromCode, out double lat, out double lon)) return null;
            string best = null;
            double bestKm = double.MaxValue;
            foreach (string code in codes ?? Enumerable.Empty<string>())
            {
                if (!TryGet(code, out double cLat, out double cLon)) continue;
                double km = DistanceKm(lat, lon, cLat, cLon);
                if (km >= bestKm) continue;
                bestKm = km;
                best = code;
            }
            return best;
        }
    }
}

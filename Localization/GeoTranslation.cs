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

using System.Collections.Generic;

namespace CrimsonX.Localization
{
    public static class GeoTranslation
    {
        public static readonly Dictionary<string, string> ContinentsFa = new Dictionary<string, string>
        {
            ["NA"] = "آمریکای شمالی",
            ["EU"] = "اروپا",
            ["AS"] = "آسیا",
            ["SA"] = "آمریکای جنوبی",
            ["AF"] = "آفریقا",
            ["OC"] = "اقیانوسیه",
            ["AN"] = "قطب جنوب"
        };

        public static readonly Dictionary<string, string> CountriesFa = new Dictionary<string, string>
        {
            ["US"] = "ایالات متحده",
            ["GB"] = "بریتانیا",
            ["DE"] = "آلمان",
            ["FR"] = "فرانسه",
            ["CA"] = "کانادا",
            ["NL"] = "هلند",
            ["SG"] = "سنگاپور",
            ["JP"] = "ژاپن",
            ["AU"] = "استرالیا",
            ["IN"] = "هند",
            ["RU"] = "روسیه",
            ["CH"] = "سوئیس",
            ["SE"] = "سوئد",
            ["FI"] = "فنلاند",
            ["NO"] = "نروژ",
            ["DK"] = "دانمارک",
            ["IT"] = "ایتالیا",
            ["ES"] = "اسپانیا",
            ["PT"] = "پرتغال",
            ["PL"] = "لهستان",
            ["RO"] = "رومانی",
            ["BG"] = "بلغارستان",
            ["UA"] = "اوکراین",
            ["TR"] = "ترکیه",
            ["IR"] = "ایران",
            ["AE"] = "امارات متحده",
            ["SA"] = "عربستان سعودی",
            ["BR"] = "برزیل",
            ["AR"] = "آرژانتین",
            ["ZA"] = "آفریقای جنوبی",
            ["KR"] = "کره جنوبی",
            ["CN"] = "چین",
            ["TW"] = "تایوان",
            ["HK"] = "هنگ کنگ",
            ["ID"] = "اندونزی",
            ["MY"] = "مالزی",
            ["TH"] = "تایلند",
            ["VN"] = "ویتنام",
            ["PH"] = "فیلیپین",
            ["NZ"] = "نیوزیلند",
            ["MX"] = "مکزیک",
            ["AT"] = "اتریش",
            ["BE"] = "بلژیک",
            ["CZ"] = "جمهوری چک",
            ["GR"] = "یونان",
            ["HU"] = "مجارستان",
            ["IE"] = "ایرلند",
            ["IL"] = "اسرائیل",
            ["LU"] = "لوکزامبورگ",
            ["IS"] = "ایسلند",
            ["EE"] = "استونی",
            ["LV"] = "لتونی",
            ["LT"] = "لیتوانی",
            ["MD"] = "مولداوی",
            ["RS"] = "صربستان",
            ["SK"] = "اسلواکی",
            ["SI"] = "اسلوونی",
            ["HR"] = "کرواسی",
            ["BA"] = "بوسنی",
            ["CY"] = "قبرس",
            ["MT"] = "مالت",
            ["GE"] = "گرجستان",
            ["AM"] = "ارمنستان",
            ["AZ"] = "آذربایجان",
            ["KZ"] = "قزاقستان",
            ["UZ"] = "ازبکستان",
            ["PK"] = "پاکستان",
            ["BD"] = "بنگلادش",
            ["EG"] = "مصر",
            ["MA"] = "مراکش",
            ["DZ"] = "الجزایر",
            ["TN"] = "تونس",
            ["NG"] = "نیجریه",
            ["KE"] = "کنیا",
            ["CO"] = "کلمبیا",
            ["CL"] = "شیلی",
            ["PE"] = "پرو",
            ["VE"] = "ونزوئلا",
            ["CU"] = "کوبا",
            ["AL"] = "آلبانی",
            ["MK"] = "مقدونیه",
            ["BY"] = "بلاروس",
            ["AF"] = "افغانستان",
            ["IQ"] = "عراق",
            ["SY"] = "سوریه",
            ["LB"] = "لبنان",
            ["JO"] = "اردن",
            ["KW"] = "کویت",
            ["QA"] = "قطر",
            ["OM"] = "عمان",
            ["YE"] = "یمن"
        };
        
        public static readonly Dictionary<string, string> CountriesEn = new Dictionary<string, string>
        {
            ["US"] = "United States",
            ["GB"] = "United Kingdom",
            ["DE"] = "Germany",
            ["FR"] = "France",
            ["CA"] = "Canada",
            ["NL"] = "Netherlands",
            ["SG"] = "Singapore",
            ["JP"] = "Japan",
            ["AU"] = "Australia",
            ["IN"] = "India",
            ["RU"] = "Russia",
            ["CH"] = "Switzerland",
            ["SE"] = "Sweden",
            ["FI"] = "Finland",
            ["NO"] = "Norway",
            ["DK"] = "Denmark",
            ["IT"] = "Italy",
            ["ES"] = "Spain",
            ["PT"] = "Portugal",
            ["PL"] = "Poland",
            ["RO"] = "Romania",
            ["BG"] = "Bulgaria",
            ["UA"] = "Ukraine",
            ["TR"] = "Turkey",
            ["IR"] = "Iran",
            ["AE"] = "United Arab Emirates",
            ["SA"] = "Saudi Arabia",
            ["BR"] = "Brazil",
            ["AR"] = "Argentina",
            ["ZA"] = "South Africa",
            ["KR"] = "South Korea",
            ["CN"] = "China",
            ["TW"] = "Taiwan",
            ["HK"] = "Hong Kong",
            ["ID"] = "Indonesia",
            ["MY"] = "Malaysia",
            ["TH"] = "Thailand",
            ["VN"] = "Vietnam",
            ["PH"] = "Philippines",
            ["NZ"] = "New Zealand",
            ["MX"] = "Mexico",
            ["AT"] = "Austria",
            ["BE"] = "Belgium",
            ["CZ"] = "Czechia",
            ["GR"] = "Greece",
            ["HU"] = "Hungary",
            ["IE"] = "Ireland",
            ["IL"] = "Israel",
            ["LU"] = "Luxembourg",
            ["IS"] = "Iceland",
            ["EE"] = "Estonia",
            ["LV"] = "Latvia",
            ["LT"] = "Lithuania",
            ["MD"] = "Moldova",
            ["RS"] = "Serbia",
            ["SK"] = "Slovakia",
            ["SI"] = "Slovenia",
            ["HR"] = "Croatia",
            ["BA"] = "Bosnia",
            ["CY"] = "Cyprus",
            ["MT"] = "Malta",
            ["GE"] = "Georgia",
            ["AM"] = "Armenia",
            ["AZ"] = "Azerbaijan",
            ["KZ"] = "Kazakhstan",
            ["UZ"] = "Uzbekistan",
            ["PK"] = "Pakistan",
            ["BD"] = "Bangladesh",
            ["EG"] = "Egypt",
            ["MA"] = "Morocco",
            ["DZ"] = "Algeria",
            ["TN"] = "Tunisia",
            ["NG"] = "Nigeria",
            ["KE"] = "Kenya",
            ["CO"] = "Colombia",
            ["CL"] = "Chile",
            ["PE"] = "Peru",
            ["VE"] = "Venezuela",
            ["CU"] = "Cuba",
            ["AL"] = "Albania",
            ["MK"] = "North Macedonia",
            ["BY"] = "Belarus",
            ["AF"] = "Afghanistan",
            ["IQ"] = "Iraq",
            ["SY"] = "Syria",
            ["LB"] = "Lebanon",
            ["JO"] = "Jordan",
            ["KW"] = "Kuwait",
            ["QA"] = "Qatar",
            ["OM"] = "Oman",
            ["YE"] = "Yemen"
        };

        public static string GetCountryEn(string code, string fallback)
        {
            if (string.IsNullOrWhiteSpace(code)) return fallback;
            return CountriesEn.TryGetValue(code.ToUpperInvariant(), out var name) ? name : fallback;
        }

        public static string GetCountryFa(string code, string fallback)
        {
            if (string.IsNullOrWhiteSpace(code)) return fallback;
            return CountriesFa.TryGetValue(code.ToUpper(), out var f) ? f : fallback;
        }

        public static string GetContinentFa(string code, string fallback)
        {
            if (string.IsNullOrWhiteSpace(code)) return fallback;
            return ContinentsFa.TryGetValue(code.ToUpper(), out var f) ? f : fallback;
        }
    }
}

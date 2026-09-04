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
using System.IO;
using CrimsonX.Models;
using Newtonsoft.Json;

namespace CrimsonX.Services
{
    public static class AppRulesService
    {
        private static string RulesPath()
        {
            var baseDir = MainWindow.Instance?.GetAppPath("Data\\Apps") ?? "Data\\Apps";
            Directory.CreateDirectory(baseDir);
            return Path.Combine(baseDir, "rules.json");
        }

        public static List<AppGameRule> Load()
        {
            try
            {
                var path = RulesPath();
                if (!File.Exists(path)) return new List<AppGameRule>();
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<List<AppGameRule>>(json) ?? new List<AppGameRule>();
            }
            catch
            {
                return new List<AppGameRule>();
            }
        }

        public static void Save(List<AppGameRule> rules)
        {
            try
            {
                var path = RulesPath();
                File.WriteAllText(path, JsonConvert.SerializeObject(rules, Formatting.Indented));
            }
            catch { }
        }
    }
}

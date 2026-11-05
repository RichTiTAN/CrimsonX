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
using System.IO;

namespace CrimsonX.Services
{
    public static class IpsecEngine
    {
        public const string RelativePath = @"Data\ipsec\crimsonipsec.exe";

        public static string ExecutablePath(string sbDir)
        {
            try
            {
                string fromApp = MainWindow.Instance?.GetAppPath(RelativePath) ?? "";
                if (fromApp.Length > 0 && File.Exists(fromApp)) return fromApp;
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
            string fromSb = Search(Path.GetDirectoryName(sbDir ?? ""), RelativePath);
            if (fromSb.Length > 0) return fromSb;
            return Search(AppContext.BaseDirectory, RelativePath);
        }

        public static bool IsInstalled(string sbDir) => ExecutablePath(sbDir).Length > 0;

        private static string Search(string start, string relative)
        {
            try
            {
                var dir = new DirectoryInfo(string.IsNullOrWhiteSpace(start) ? AppContext.BaseDirectory : start);
                for (int depth = 0; dir != null && depth < 6; depth++, dir = dir.Parent)
                {
                    string candidate = Path.Combine(dir.FullName, relative);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
            return "";
        }
    }
}

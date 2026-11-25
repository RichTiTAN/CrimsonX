/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
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

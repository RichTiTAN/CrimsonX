/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CrimsonX.Services
{
    public static class ProxyService
    {
        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int lpdwBufferLength);

        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_REFRESH = 37;

        public static void SetSystemProxy(bool enable) => SetSystemProxy(enable, ExitNodeChain.XrayProxyPort);

        public static void SetSystemProxy(bool enable, int port)
        {
            if (port <= 0) port = ExitNodeChain.XrayProxyPort;
            string endpoint = "127.0.0.1:" + port;
            CrimsonX.Services.BackgroundTask.Run("system proxy", () => Task.Run(() =>
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
                    if (key != null)
                    {
                        if (enable)
                        {
                            key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                            key.SetValue("ProxyServer", endpoint, RegistryValueKind.String);
                        }
                        else
                        {
                            key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                            key.DeleteValue("ProxyServer", false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    SimpleLogger.Log(ex);
                }
                try { RefreshProxy(); }
                catch (Exception ex) { SimpleLogger.Log(ex); }
            }));
        }

        public static void DisableSystemProxy()
        {
            SetSystemProxy(false);
        }

        public static void RefreshProxy()
        {
            try
            {
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
            }
            catch { }
        }
    }
}

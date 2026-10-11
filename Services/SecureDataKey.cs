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
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CrimsonX.Services
{
    internal static class SecureDataKey
    {
        private static readonly object Sync = new object();
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CrimsonX.DataKey.v1");
        private static byte[] _current;

        internal static readonly byte[] LegacyKey = Encoding.UTF8.GetBytes(AppSecrets.CacheKey);
        internal static readonly byte[] LegacyIv = Encoding.UTF8.GetBytes(AppSecrets.CacheIv);

        internal static string KeyPath() => SecureJsonStore.PathFor("data.key");

        internal static byte[] Current()
        {
            lock (Sync)
            {
                if (_current == null) _current = LoadOrCreate() ?? LegacyKey;
                return _current;
            }
        }

        internal static bool UsingLegacy()
        {
            byte[] key = Current();
            return key.Length == LegacyKey.Length && CryptographicOperations.FixedTimeEquals(key, LegacyKey);
        }

        private static byte[] LoadOrCreate()
        {
            string path = KeyPath();
            if (File.Exists(path))
            {
                try
                {
                    byte[] stored = Unprotect(File.ReadAllBytes(path), Entropy);
                    if (stored.Length == 32) return stored;
                }
                catch (Exception ex)
                {
                    SimpleLogger.Log($"[DataKey] The stored key could not be read: {ex.Message}");
                    BackupUnreadable(path);
                }
            }
            try
            {
                byte[] fresh = RandomNumberGenerator.GetBytes(32);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, Protect(fresh, Entropy));
                return fresh;
            }
            catch (Exception ex)
            {
                SimpleLogger.Log($"[DataKey] Windows DPAPI is unavailable, keeping the built-in key: {ex.Message}");
                return null;
            }
        }

        private static void BackupUnreadable(string path)
        {
            try
            {
                File.Move(path, path + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak", overwrite: true);
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
        }

        private const int CryptProtectUiForbidden = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Length;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DataBlob input, string description, ref DataBlob entropy,
            IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, ref DataBlob entropy,
            IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        private static byte[] Protect(byte[] plain, byte[] entropy)
        {
            var input = ToBlob(plain);
            var extra = ToBlob(entropy);
            try
            {
                if (!CryptProtectData(ref input, "CrimsonX", ref extra, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var output))
                    throw new CryptographicException("CryptProtectData failed with error " + Marshal.GetLastWin32Error());
                try
                {
                    return FromBlob(output);
                }
                finally
                {
                    LocalFree(output.Data);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(input.Data);
                Marshal.FreeHGlobal(extra.Data);
            }
        }

        private static byte[] Unprotect(byte[] protectedBytes, byte[] entropy)
        {
            var input = ToBlob(protectedBytes);
            var extra = ToBlob(entropy);
            try
            {
                if (!CryptUnprotectData(ref input, IntPtr.Zero, ref extra, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var output))
                    throw new CryptographicException("CryptUnprotectData failed with error " + Marshal.GetLastWin32Error());
                try
                {
                    return FromBlob(output);
                }
                finally
                {
                    LocalFree(output.Data);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(input.Data);
                Marshal.FreeHGlobal(extra.Data);
            }
        }

        private static DataBlob ToBlob(byte[] bytes)
        {
            var blob = new DataBlob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
            Marshal.Copy(bytes, 0, blob.Data, bytes.Length);
            return blob;
        }

        private static byte[] FromBlob(DataBlob blob)
        {
            var bytes = new byte[blob.Length];
            Marshal.Copy(blob.Data, bytes, 0, blob.Length);
            return bytes;
        }
    }
}

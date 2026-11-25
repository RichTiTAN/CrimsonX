/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CrimsonX.Services;

public static class EulaStore
{
    private const string FolderName = "CrimsonX";
    private const string FileName = "eula.accepted";

    internal static string EulaPath => Path.Combine(AppContext.BaseDirectory, "EULA.txt");

    internal static string AcceptedFilePath
    {
        get
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(root))
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(root, FolderName, FileName);
        }
    }

    internal static string TokenFor(string text)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? "")));
    }

    private static string? CurrentToken()
    {
        try
        {
            if (!File.Exists(EulaPath)) return null;
            return TokenFor(File.ReadAllText(EulaPath));
        }
        catch (Exception ex)
        {
            SimpleLogger.Log(ex);
            return null;
        }
    }

    public static bool IsAccepted()
    {
        try
        {
            string? token = CurrentToken();
            if (token == null || !File.Exists(AcceptedFilePath)) return false;
            return string.Equals(File.ReadAllText(AcceptedFilePath).Trim(), token, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            SimpleLogger.Log(ex);
            return false;
        }
    }

    public static bool Accept()
    {
        try
        {
            string? token = CurrentToken();
            if (token == null) return false;
            string path = AcceptedFilePath;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string temp = path + ".tmp";
            File.WriteAllText(temp, token);
            File.Move(temp, path, true);
            return true;
        }
        catch (Exception ex)
        {
            SimpleLogger.Log(ex);
            return false;
        }
    }
}

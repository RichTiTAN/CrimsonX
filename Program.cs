/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using Avalonia;
using System;

namespace CrimsonX;

class Program
{
    private static System.Threading.Mutex? _mutex;

    [STAThread]
    public static void Main(string[] args)
    {
        const string appName = "CrimsonX_SingleInstanceMutex";
        _mutex = new System.Threading.Mutex(true, appName, out bool createdNew);
        if (!createdNew)
        {
            return;
        }
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            CrimsonX.Services.SimpleLogger.LogFatal(e.ExceptionObject as Exception
                ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown"));
        TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                CrimsonX.Services.BackgroundTask.Report("unobserved task", e.Exception);
                e.SetObserved();
            };
        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            CrimsonX.Services.SimpleLogger.Shutdown();
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}

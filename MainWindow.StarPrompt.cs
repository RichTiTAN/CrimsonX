/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace CrimsonX;

public partial class MainWindow
{
    // ── GitHub star prompt ──

    private const string GitHubRepoUrl = "https://github.com/RichTiTAN/CrimsonX";

    private const int StarPromptDelaySeconds = 10;

    private DispatcherTimer? _starPromptTimer;
    private int _starPromptSeconds;
    private bool _starPromptShown;
    private bool _starPromptDismissed;

    internal void InitStarPrompt()
    {
        if (_cfg.HideStarPrompt) return;
        _starPromptTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _starPromptTimer.Tick += (_, _) => OnStarPromptTick();
        _starPromptTimer.Start();
    }

    private void OnStarPromptTick()
    {
        if (_cfg.HideStarPrompt) return;
        if (!_state.IsConnected)
        {
            _starPromptSeconds = 0;
            if (_starPromptShown)
            {
                _starPromptShown = false;
                HideStarPrompt();
            }
            return;
        }
        if (_starPromptShown || _starPromptDismissed) return;
        if (_state.IsReconnecting)
        {
            _starPromptSeconds = 0;
            return;
        }
        if (++_starPromptSeconds < StarPromptDelaySeconds) return;
        _starPromptShown = true;
        ShowStarPrompt();
    }

    private void ShowStarPrompt()
    {
        var prompt = this.FindControl<Border>("panStarPrompt");
        if (prompt == null) return;
        prompt.IsVisible = true;
        prompt.IsHitTestVisible = true;
        DispatcherTimer.RunOnce(() => prompt.Opacity = 1, TimeSpan.FromMilliseconds(20));
    }

    private void HideStarPrompt()
    {
        var prompt = this.FindControl<Border>("panStarPrompt");
        if (prompt == null || !prompt.IsVisible) return;
        prompt.IsHitTestVisible = false;
        prompt.Opacity = 0;
        DispatcherTimer.RunOnce(() =>
        {
            if (_starPromptShown) return;
            prompt.IsVisible = false;
        }, TimeSpan.FromMilliseconds(320));
    }

    private void StarPrompt_Click(object? sender, RoutedEventArgs e)
    {
        _starPromptShown = false;
        _starPromptDismissed = true;
        HideStarPrompt();
        try
        {
            Process.Start(new ProcessStartInfo(GitHubRepoUrl) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
        }
    }

    private void StarPromptClose_Click(object? sender, RoutedEventArgs e)
    {
        _starPromptShown = false;
        _starPromptDismissed = true;
        HideStarPrompt();
    }

    private void StarPromptHide_Click(object? sender, RoutedEventArgs e)
    {
        _starPromptShown = false;
        _starPromptDismissed = true;
        _cfg.HideStarPrompt = true;
        RequestConfigSave();
        HideStarPrompt();
    }
}

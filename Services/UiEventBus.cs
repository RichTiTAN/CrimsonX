/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using Avalonia.Threading;

namespace CrimsonX.Services
{
    public sealed class ToastEvent
    {
        public string Message { get; init; } = "";
        public bool   Success { get; init; }
    }

    public sealed class UiEventBus
    {
        public static readonly UiEventBus Instance = new UiEventBus();

        private UiEventBus() { }

        public event System.Action<ToastEvent>? ToastRequested;

        public void PublishToast(string message, bool success = false)
        {
            Dispatcher.UIThread.Post(() => ToastRequested?.Invoke(new ToastEvent { Message = message, Success = success }));
        }

        public event System.Action<int>? ConnectionProgress;

        public int LastConnectionProgress { get; private set; } = -1;

        public event System.Action? RulesApplied;

        public void PublishConnectionProgress(int percent)
        {
            LastConnectionProgress = percent;
            Dispatcher.UIThread.Post(() => ConnectionProgress?.Invoke(percent));
        }

        public void PublishRulesApplied()
        {
            Dispatcher.UIThread.Post(() => RulesApplied?.Invoke());
        }
    }
}

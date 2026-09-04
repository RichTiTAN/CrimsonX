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
            ToastRequested?.Invoke(new ToastEvent { Message = message, Success = success });
        }

        public event System.Action<int>? ConnectionProgress;

        public void PublishConnectionProgress(int percent)
        {
            ConnectionProgress?.Invoke(percent);
        }
    }
}

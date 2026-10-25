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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace CrimsonX.Pages
{
    public static class PasteRouter
    {
        private static readonly ConditionalWeakTable<object, object> Hooked = new ConditionalWeakTable<object, object>();
        private static readonly ConditionalWeakTable<object, object> HookedContext = new ConditionalWeakTable<object, object>();
        private static readonly HashSet<object> Running = new HashSet<object>();

        public static bool IsPasteGesture(KeyEventArgs e)
            => (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.V)
            || (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.Insert);

        public static async Task<string> ReadClipboardAsync(Control anchor)
        {
            try
            {
                var top = TopLevel.GetTopLevel(anchor);
                if (top?.Clipboard == null) return "";
                var transfer = await top.Clipboard.TryGetDataAsync();
                if (transfer == null) return "";
                try { return (await transfer.TryGetTextAsync()) ?? ""; }
                finally
                {
                    if (transfer is IDisposable disposable) disposable.Dispose();
                    else if (transfer is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
                }
            }
            catch (Exception ex)
            {
                CrimsonX.Services.SimpleLogger.Log(ex);
                return "";
            }
        }

        public static void Attach(Control control, Func<string, Task> onPasted)
        {
            if (control == null || onPasted == null) return;
            AttachGesture(control, onPasted);
            AttachContext(control, onPasted);
            if (control is TextBox textBox)
            {
                AttachTextBox(textBox, onPasted);
                return;
            }
            if (control is ComboBox combo)
            {
                AttachInner(combo, onPasted);
                combo.TemplateApplied += (_, _) => AttachInner(combo, onPasted);
            }
        }

        private static void AttachGesture(Control control, Func<string, Task> onPasted)
        {
            if (!Hooked.TryAdd(control, control)) return;
            control.AddHandler(InputElement.KeyDownEvent, async (object? sender, KeyEventArgs e) =>
            {
                if (!IsPasteGesture(e)) return;
                e.Handled = true;
                await PasteAsync(control, onPasted);
            }, RoutingStrategies.Tunnel);
        }

        private static void AttachContext(Control control, Func<string, Task> onPasted)
        {
            if (!HookedContext.TryAdd(control, control)) return;
            control.AddHandler(InputElement.ContextRequestedEvent, async (object? sender, ContextRequestedEventArgs e) =>
            {
                e.Handled = true;
                await PasteAsync(control, onPasted);
            }, RoutingStrategies.Tunnel);
        }

        private static void AttachTextBox(TextBox textBox, Func<string, Task> onPasted)
        {
            if (!Hooked.TryAdd(textBox, textBox)) return;
            textBox.AddHandler(TextBox.PastingFromClipboardEvent, async (object? sender, RoutedEventArgs e) =>
            {
                e.Handled = true;
                await PasteAsync(textBox, onPasted);
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        private static void AttachInner(ComboBox combo, Func<string, Task> onPasted)
        {
            var inner = combo.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            if (inner == null) return;
            AttachTextBox(inner, onPasted);
        }

        private static async Task PasteAsync(Control anchor, Func<string, Task> onPasted)
        {
            if (anchor == null || onPasted == null) return;
            if (!Running.Add(anchor)) return;
            try
            {
                string text = (await ReadClipboardAsync(anchor)).Trim();
                if (text.Length == 0) return;
                await onPasted(text);
            }
            finally
            {
                Running.Remove(anchor);
            }
        }
    }
}

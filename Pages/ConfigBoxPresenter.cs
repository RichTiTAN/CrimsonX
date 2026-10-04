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
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace CrimsonX.Pages
{
    internal sealed class ConfigBoxPresenter
    {
        private readonly Func<string, string> _readRaw;
        private readonly Action<string, string> _writeRaw;
        private readonly Func<string, string> _labelFor;
        private readonly Dictionary<string, Control> _boxes = new(StringComparer.Ordinal);

        public ConfigBoxPresenter(Func<string, string> readRaw, Action<string, string> writeRaw, Func<string, string> labelFor)
        {
            _readRaw  = readRaw;
            _writeRaw = writeRaw;
            _labelFor = labelFor;
        }

        public bool Suppressed { get; private set; }

        public IDisposable Suppress() => new SuppressScope(this);

        private sealed class SuppressScope : IDisposable
        {
            private readonly ConfigBoxPresenter _presenter;
            private readonly bool _previous;

            public SuppressScope(ConfigBoxPresenter presenter)
            {
                _presenter = presenter;
                _previous  = presenter.Suppressed;
                presenter.Suppressed = true;
            }

            public void Dispose() => _presenter.Suppressed = _previous;
        }

        public void Attach(string key, Control? box)
        {
            if (box != null) _boxes[key] = box;
        }

        public Control? Box(string key) => _boxes.TryGetValue(key, out var box) ? box : null;

        public bool IsEmpty(string key) => GetText(Box(key)).Trim().Length == 0;

        public string Label(string raw) => _labelFor(raw ?? "") ?? "";

        public void Show(string key, string? raw)
        {
            var box = Box(key);
            if (box == null) return;
            string text = (raw ?? "").Trim();
            using var _ = Suppress();
            SetText(box, text.Length == 0 ? "" : Label(text));
        }

        public void ShowAll(bool force = false)
        {
            foreach (var key in _boxes.Keys)
            {
                var box = Box(key);
                if (box == null) continue;
                string raw     = (_readRaw(key) ?? "").Trim();
                string title   = raw.Length == 0 ? "" : Label(raw);
                string current = GetText(box).Trim();
                if (!force && current.Length > 0
                    && !string.Equals(current, raw, StringComparison.Ordinal)
                    && !string.Equals(current, title, StringComparison.Ordinal))
                    continue;
                using var _ = Suppress();
                SetText(box, current.Length == 0 && !force ? "" : title);
            }
        }

        public string Resolve(string key, Func<string, bool> adopt)
        {
            string visible = GetText(Box(key)).Trim();
            string stored  = (_readRaw(key) ?? "").Trim();
            if (visible.Length == 0) return "";
            if (stored.Length > 0
                && (string.Equals(visible, stored, StringComparison.Ordinal)
                 || string.Equals(visible, Label(stored), StringComparison.Ordinal)))
                return stored;
            if (adopt == null || !adopt(visible)) return "";
            _writeRaw(key, visible);
            Show(key, visible);
            return visible;
        }

        // ── paste: the one copy of the gesture and the clipboard read ───────────────────────────

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

        public void AttachPaste(string key, Func<string, Task> onPasted)
        {
            var box = Box(key);
            if (box == null) return;
            box.AddHandler(InputElement.KeyDownEvent, async (object? sender, KeyEventArgs e) =>
            {
                if (!IsPasteGesture(e)) return;
                e.Handled = true;
                string text = (await ReadClipboardAsync(box)).Trim();
                if (text.Length == 0) return;
                await onPasted(text);
            }, RoutingStrategies.Tunnel);
        }

        private static string GetText(Control? box)
            => box switch
            {
                TextBox textBox => textBox.Text ?? "",
                ComboBox combo  => combo.Text ?? "",
                _               => ""
            };

        private static void SetText(Control box, string text)
        {
            switch (box)
            {
                case TextBox textBox: textBox.Text = text; break;
                case ComboBox combo:  combo.Text  = text; break;
            }
        }
    }
}

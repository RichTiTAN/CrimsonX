/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

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

        public string Text(string key) => GetText(Box(key)).Trim();

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

        public string Resolve(string key, Func<string, bool> adopt) => Resolve(key, adopt, null);

        public string Resolve(string key, Func<string, bool> adopt, Func<string, string>? rawOfTitle)
        {
            string visible = GetText(Box(key)).Trim();
            string stored  = (_readRaw(key) ?? "").Trim();
            if (visible.Length == 0) return "";
            if (stored.Length > 0
                && (string.Equals(visible, stored, StringComparison.Ordinal)
                 || string.Equals(visible, Label(stored), StringComparison.Ordinal)))
                return stored;
            string titled = (rawOfTitle?.Invoke(visible) ?? "").Trim();
            if (titled.Length > 0)
            {
                _writeRaw(key, titled);
                Show(key, titled);
                return titled;
            }
            if (adopt == null || !adopt(visible)) return "";
            _writeRaw(key, visible);
            Show(key, visible);
            return visible;
        }

        // ── paste: the one copy of the gesture and the clipboard read ───────────────────────────

        public static bool IsPasteGesture(KeyEventArgs e) => PasteRouter.IsPasteGesture(e);

        public static Task<string> ReadClipboardAsync(Control anchor) => PasteRouter.ReadClipboardAsync(anchor);

        public void AttachPaste(string key, Func<string, Task> onPasted)
        {
            var box = Box(key);
            if (box == null) return;
            PasteRouter.Attach(box, onPasted);
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

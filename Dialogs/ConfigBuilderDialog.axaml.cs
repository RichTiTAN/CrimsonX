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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CrimsonX.Localization;
using CrimsonX.Services;

namespace CrimsonX.Dialogs
{
    public partial class ConfigBuilderDialog : Window
    {
        private readonly ConfigBuilderScope _scope;
        private readonly Dictionary<string, Control> _controls = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ConfigField> _fields = new Dictionary<string, ConfigField>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _prefill;
        private ConfigProtocol? _protocol;

        public ConfigBuilderDialog() : this("", ConfigBuilderScope.CustomConfig)
        {
        }

        public ConfigBuilderDialog(string existing, ConfigBuilderScope scope, string? preferredLabel = null)
        {
            InitializeComponent();
            _scope = scope;
            _prefill = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool editing = !string.IsNullOrWhiteSpace(existing);
            Title = editing ? AppStrings.BuilderEditTitle : AppStrings.BuilderNewTitle;
            lblBuilderProtocol.Text = AppStrings.BuilderProtocolLabel;
            string startingId = "";
            if (editing && ConfigBuilder.TryRead(existing, scope, out string protocolId, out var values, out _))
            {
                startingId = protocolId;
                foreach (var pair in values) _prefill[pair.Key] = pair.Value;
            }
            if (!string.IsNullOrWhiteSpace(preferredLabel)) _prefill["label"] = preferredLabel.Trim();
            BuildProtocolList(startingId);
            cmbBuilderProtocol.SelectionChanged += (_, _) => OnProtocolChanged();
            OnProtocolChanged();
        }

        public static Task<string?> ShowAsync(string existing, ConfigBuilderScope scope, string? preferredLabel = null)
        {
            var owner = MainWindow.Instance;
            if (owner == null) return Task.FromResult<string?>(null);
            return Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dialog = new ConfigBuilderDialog(existing, scope, preferredLabel);
                return await dialog.ShowDialog<string?>(owner);
            });
        }

        private void BuildProtocolList(string startingId)
        {
            var protocols = ConfigBuilderSchema.Protocols(_scope);
            foreach (var protocol in protocols)
                cmbBuilderProtocol.Items.Add(new ComboBoxItem { Content = protocol.Label, Tag = protocol.Id, FontSize = 12 });
            int index = 0;
            for (int i = 0; i < protocols.Count; i++)
            {
                if (!string.Equals(protocols[i].Id, startingId, StringComparison.OrdinalIgnoreCase)) continue;
                index = i;
                break;
            }
            if (protocols.Count > 0) cmbBuilderProtocol.SelectedIndex = index;
        }

        private void OnProtocolChanged()
        {
            _protocol = null;
            if (cmbBuilderProtocol.SelectedItem is ComboBoxItem item && item.Tag is string id)
                _protocol = ConfigBuilderSchema.Find(id);
            RenderFields();
        }

        private void ShowError(string message)
        {
            lblBuilderError.Text = message;
            lblBuilderError.IsVisible = message.Length > 0;
        }

        private void RenderFields()
        {
            panBuilderFields.Children.Clear();
            _controls.Clear();
            _fields.Clear();
            ShowError("");
            if (_protocol == null) return;
            string group = "";
            foreach (var row in ConfigBuilderLayout.Rows(_protocol.Fields))
            {
                string rowGroup = row[0].Group;
                if (!string.Equals(rowGroup, group, StringComparison.Ordinal))
                {
                    group = rowGroup;
                    AddSectionHeader(group);
                }
                panBuilderFields.Children.Add(BuildRow(row));
            }
            RefreshEnablement();
        }

        private void AddSectionHeader(string title)
        {
            var header = new StackPanel { Spacing = 6, Margin = new Thickness(0, 16, 0, 8) };
            header.Children.Add(new Border { Height = 1, Background = Brush.Parse("#1FFFFFFF") });
            header.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 9,
                FontWeight = FontWeight.Bold,
                Foreground = Brush.Parse("#8B949E"),
                LetterSpacing = 1.6,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            });
            panBuilderFields.Children.Add(header);
        }

        private Control BuildRow(ConfigField[] row)
        {
            if (row.Length == 1)
            {
                var single = BuildCell(row[0]);
                single.Margin = new Thickness(0, 0, 0, 8);
                return single;
            }

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions(ConfigBuilderLayout.ColumnDefinitions(row)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            for (int column = 0; column < row.Length && column < 2; column++)
            {
                var cell = BuildCell(row[column]);
                Grid.SetColumn(cell, column * 2);
                grid.Children.Add(cell);
            }
            return grid;
        }

        private Control BuildCell(ConfigField field)
        {
            var panel = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Top };
            panel.Children.Add(new TextBlock
            {
                Text = field.Required ? field.Label + " *" : field.Label,
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = Brush.Parse("#A0AEC0"),
                LetterSpacing = 1
            });

            Control control;
            switch (field.Kind)
            {
                case ConfigFieldKind.Combo:
                {
                    var combo = new ComboBox
                    {
                        Height = 28,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Background = Brush.Parse("#1A1C20"),
                        BorderBrush = Brush.Parse("#2D3748"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Foreground = Brush.Parse("#E2E8F0"),
                        FontSize = 12
                    };
                    foreach (string option in field.Options) combo.Items.Add(option);
                    string current = _prefill.TryGetValue(field.Key, out string picked) && !string.IsNullOrWhiteSpace(picked) ? picked : field.Default;
                    combo.SelectedItem = field.Options.Contains(current) ? current : (field.Options.Length > 0 ? field.Options[0] : null);
                    combo.SelectionChanged += (_, _) => RefreshEnablement();
                    control = combo;
                    break;
                }
                case ConfigFieldKind.Check:
                {
                    bool isChecked = field.Default == "1" || (_prefill.TryGetValue(field.Key, out string flag) && flag == "1");
                    var check = new CheckBox
                    {
                        IsChecked = isChecked,
                        Foreground = Brush.Parse("#E2E8F0"),
                        FontSize = 12,
                        Margin = new Thickness(0, 4, 0, 0),
                        VerticalAlignment = VerticalAlignment.Top
                    };
                    check.IsCheckedChanged += (_, _) => RefreshEnablement();
                    control = check;
                    break;
                }
                default:
                {
                    bool multiline = field.Multiline;
                    var box = new TextBox
                    {
                        Height = multiline ? 64 : 28,
                        AcceptsReturn = multiline,
                        TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = multiline ? 10 : 12,
                        Background = Brush.Parse("#1A1C20"),
                        BorderBrush = Brush.Parse("#2D3748"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Foreground = Brush.Parse("#E2E8F0"),
                        Padding = new Thickness(8, 4),
                        VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center,
                        PlaceholderText = field.Placeholder,
                        Text = _prefill.TryGetValue(field.Key, out string text) ? text : field.Default
                    };
                    if (field.Kind == ConfigFieldKind.Password) box.PasswordChar = '●';
                    box.TextChanged += (_, _) => RefreshEnablement();
                    control = box;
                    break;
                }
            }

            panel.Children.Add(control);
            _controls[field.Key] = control;
            _fields[field.Key] = field;
            return panel;
        }

        private Dictionary<string, string> CollectValues()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in _controls)
            {
                switch (pair.Value)
                {
                    case ComboBox combo when combo.SelectedItem is string selected:
                        values[pair.Key] = selected;
                        break;
                    case CheckBox check:
                        values[pair.Key] = check.IsChecked == true ? "1" : "0";
                        break;
                    case TextBox box:
                        values[pair.Key] = box.Text ?? "";
                        break;
                }
            }
            return values;
        }

        private void RefreshEnablement()
        {
            if (_protocol == null) return;
            var values = CollectValues();
            foreach (var pair in _controls)
            {
                if (!_fields.TryGetValue(pair.Key, out var field)) continue;
                bool enabled = true;
                if (!string.IsNullOrWhiteSpace(field.EnableKey))
                {
                    string gate = values.TryGetValue(field.EnableKey, out string value) ? value : "";
                    enabled = field.EnableValues.Any(option => string.Equals(option, gate, StringComparison.OrdinalIgnoreCase));
                }
                pair.Value.IsEnabled = enabled;
            }
            var groups = _protocol.ExclusiveSets;
            var active = groups.Select(group => group.Any(key => HasValue(values, key))).ToList();
            if (active.Count(x => x) == 1)
            {
                int index = active.IndexOf(true);
                for (int i = 0; i < groups.Count; i++)
                {
                    if (i == index) continue;
                    foreach (string key in groups[i])
                        if (_controls.TryGetValue(key, out var control)) control.IsEnabled = false;
                }
            }
        }

        private static bool HasValue(Dictionary<string, string> values, string key)
        {
            if (!values.TryGetValue(key, out string value)) return false;
            value = (value ?? "").Trim();
            return value.Length > 0 && value != "0" && !value.Equals("none", StringComparison.OrdinalIgnoreCase);
        }

        private void Save_Click(object? sender, RoutedEventArgs e)
        {
            if (_protocol == null)
            {
                ShowError("Pick a protocol.");
                return;
            }
            var (raw, buildError) = ConfigBuilder.Build(_protocol.Id, CollectValues());
            if (buildError.Length > 0)
            {
                ShowError(buildError);
                return;
            }
            if (!ConfigBuilder.Validate(raw, _scope, out string validation))
            {
                ShowError(validation.Length > 0 ? validation : "this config could not be read");
                return;
            }
            Close(raw);
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

        private void Dialog_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter)
            {
                if (e.Source is TextBox box && box.AcceptsReturn) return;
                Save_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }
    }
}

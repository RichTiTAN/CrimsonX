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

using Avalonia.Controls;
using Avalonia.Input;
using AS = CrimsonX.Localization.AppStrings;

namespace CrimsonX.Dialogs
{
    public partial class CustomProxyHintDialog : Window
    {
        public CustomProxyHintDialog()
        {
            InitializeComponent();
            ApplyLanguage();
        }

        private void ApplyLanguage()
        {
            FlowDirection = AS.IsPersian
                ? Avalonia.Media.FlowDirection.RightToLeft
                : Avalonia.Media.FlowDirection.LeftToRight;

            TextBlock? F(string name) => this.FindControl<TextBlock>(name);

            Title = AS.CustomProxyHintTitle;

            AS.Apply(F("lblStep1Tag"),  AS.CustomProxyHintStep1Tag, forceLtr: true);
            AS.Apply(F("lblStep1Text"), AS.CustomProxyHintStep1);
            AS.Apply(F("lblStep2Tag"),  AS.CustomProxyHintStep2Tag, forceLtr: true);
            AS.Apply(F("lblStep2Text"), AS.CustomProxyHintStep2);
            AS.Apply(F("lblStep3Tag"),  AS.CustomProxyHintStep3Tag, forceLtr: true);
            AS.Apply(F("lblStep3Text"), AS.CustomProxyHintStep3);
            AS.Apply(F("lblStep4Tag"),  AS.CustomProxyHintStep4Tag, forceLtr: true);
            AS.Apply(F("lblStep4Text"), AS.CustomProxyHintStep4);
        }

        private void Dialog_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close("Close");
        }
    }
}

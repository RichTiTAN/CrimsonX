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
using Avalonia.Interactivity;

namespace CrimsonX.Dialogs
{
    public partial class ConfirmDialog : Window
    {
        public string DialogTitle { get; set; } = "";
        public string DialogMessage { get; set; } = "";
        public string YesText { get; set; } = "YES";
        public string NoText { get; set; } = "NO";

        public ConfirmDialog()
        {
            DataContext = this;
            InitializeComponent();
        }

        public ConfirmDialog(string title, string message, string yesText, string noText)
        {
            DialogTitle = title;
            DialogMessage = message;
            YesText = yesText;
            NoText = noText;
            DataContext = this;

            if (CrimsonX.Localization.AppStrings.IsPersian)
                FlowDirection = Avalonia.Media.FlowDirection.RightToLeft;

            InitializeComponent();
        }

        private void Yes_Click(object? sender, RoutedEventArgs e) => Close("Yes");
        private void No_Click(object? sender, RoutedEventArgs e) => Close("No");
    }
}

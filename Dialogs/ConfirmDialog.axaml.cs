/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
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

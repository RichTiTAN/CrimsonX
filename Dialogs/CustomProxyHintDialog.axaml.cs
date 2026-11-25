/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
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

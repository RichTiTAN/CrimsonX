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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace CrimsonX.Controls;

public partial class NavigationBar : UserControl
{
    public event EventHandler<string>? NavChanged;
    private RadioButton? _selectedButton;

    public NavigationBar()
    {
        InitializeComponent();
        this.LayoutUpdated += (s, e) => UpdateUnderline();
    }

    public void ApplyLanguage()
    {
        bool isFa = CrimsonX.Localization.AppStrings.IsPersian;
        var pWide = new Thickness(20, 8);
        var pNorm = new Thickness(12, 8);

        var btnNavHome = this.FindControl<RadioButton>("btnNavHome");
        if (btnNavHome != null) 
        {
            btnNavHome.Content = CrimsonX.Localization.AppStrings.NavHome;
            btnNavHome.Padding = isFa ? pWide : pNorm;
        }
        
        var btnNavSplit = this.FindControl<RadioButton>("btnNavSplit");
        if (btnNavSplit != null) 
        {
            btnNavSplit.Content = CrimsonX.Localization.AppStrings.NavSplitTunneling;
            btnNavSplit.Padding = pNorm;
        }
        
        var btnNavSettings = this.FindControl<RadioButton>("btnNavSettings");
        if (btnNavSettings != null) 
        {
            btnNavSettings.Content = CrimsonX.Localization.AppStrings.NavSettings;
            btnNavSettings.Padding = pNorm;
        }
        
        var btnNavAbout = this.FindControl<RadioButton>("btnNavAbout");
        if (btnNavAbout != null) 
        {
            btnNavAbout.Content = CrimsonX.Localization.AppStrings.NavAbout;
            btnNavAbout.Padding = pNorm;
        }
        
        var btnNavThemes = this.FindControl<RadioButton>("btnNavThemes");
        if (btnNavThemes != null) 
        {
            btnNavThemes.Content = CrimsonX.Localization.AppStrings.NavThemes;
            btnNavThemes.Padding = isFa ? pWide : pNorm;
        }

        this.InvalidateMeasure();
        
        var navStack = this.FindControl<StackPanel>("NavStack");
        if (navStack != null)
        {
            foreach (var child in navStack.Children)
            {
                child.InvalidateMeasure();
            }
            navStack.InvalidateMeasure();
        }
        
        Dispatcher.UIThread.Post(() => UpdateUnderline(), DispatcherPriority.Render);
    }

    private void NavButton_Checked(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb)
        {
            if (rb.IsChecked == true)
            {
                _selectedButton = rb;
                UpdateUnderline();

                if (rb.Tag is string tag)
                {
                    NavChanged?.Invoke(this, tag);
                }
            }
        }
    }

    private void UserControl_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateUnderline();
    }

    private void UpdateUnderline()
    {
        if (_selectedButton == null) return;

        var container = this.FindControl<Panel>("MainContainer");
        var underline = this.FindControl<Border>("AnimatedUnderline");
        
        if (container == null || underline == null) return;

        double width = _selectedButton.Bounds.Width;
        if (width == 0) return;
        
        var point = _selectedButton.TranslatePoint(new Point(0, 0), container);
        if (!point.HasValue) return;

        double xPos = point.Value.X;
        
        double underlineWidth = 34;
        double centerOffset = xPos + (width / 2) - (underlineWidth / 2);

        underline.Margin = new Thickness(centerOffset, 0, 0, 0);
    }
}

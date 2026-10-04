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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;

namespace CrimsonX.Behaviors
{
    public static class ScrollScrimBehavior
    {
        private const double BandFraction = 0.03;

        private const double BandMidFraction = 0.5;

        private static readonly Color BandMidColour = Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF);

        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsEnabled", typeof(ScrollScrimBehavior), false);

        public static bool GetIsEnabled(AvaloniaObject element) => element.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(AvaloniaObject element, bool value) => element.SetValue(IsEnabledProperty, value);

        public static readonly AttachedProperty<double> TopInsetProperty =
            AvaloniaProperty.RegisterAttached<ScrollViewer, double>("TopInset", typeof(ScrollScrimBehavior), 0d);

        public static double GetTopInset(AvaloniaObject element) => element.GetValue(TopInsetProperty);
        public static void SetTopInset(AvaloniaObject element, double value) => element.SetValue(TopInsetProperty, value);

        static ScrollScrimBehavior()
        {
            IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>(OnIsEnabledChanged);
            TopInsetProperty.Changed.AddClassHandler<ScrollViewer>((scroller, _) => Apply(scroller));
        }

        private static void OnIsEnabledChanged(ScrollViewer scroller, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
            {
                scroller.ScrollChanged += OnScrollChanged;
                scroller.SizeChanged += OnSizeChanged;
                Apply(scroller);
            }
            else
            {
                scroller.ScrollChanged -= OnScrollChanged;
                scroller.SizeChanged -= OnSizeChanged;
                scroller.OpacityMask = null;
            }
        }

        private static void OnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (sender is ScrollViewer scroller) Apply(scroller);
        }

        private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (sender is ScrollViewer scroller) Apply(scroller);
        }

        private static void Apply(ScrollViewer scroller)
            => scroller.OpacityMask = BuildMask(topBand: scroller.Offset.Y > 0.5,
                                               insetFraction: InsetFraction(scroller.Bounds.Height, GetTopInset(scroller)));

        public static double InsetFraction(double viewportHeight, double insetPixels)
        {
            if (viewportHeight <= 0 || insetPixels <= 0) return 0;
            double cap = Math.Max(0, 1 - (BandFraction * 2) - 0.01);
            double fraction = insetPixels / viewportHeight;
            return fraction > cap ? cap : fraction;
        }

        public static IBrush BuildMask(bool topBand, double insetFraction = 0)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative)
            };
            double edge = BandFraction;
            double mid = BandFraction * BandMidFraction;
            double start = topBand && insetFraction > 0 ? insetFraction : 0;
            brush.GradientStops.Add(new GradientStop(topBand ? Colors.Transparent : Colors.White, 0));
            if (start > 0) brush.GradientStops.Add(new GradientStop(Colors.Transparent, start));
            brush.GradientStops.Add(new GradientStop(topBand ? BandMidColour : Colors.White, start + mid));
            brush.GradientStops.Add(new GradientStop(Colors.White, start + edge));
            brush.GradientStops.Add(new GradientStop(Colors.White, 1 - edge));
            brush.GradientStops.Add(new GradientStop(BandMidColour, 1 - mid));
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            return brush;
        }
    }
}

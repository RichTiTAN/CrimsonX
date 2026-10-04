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
using Avalonia.Media;

namespace CrimsonX.Controls
{
    public class ProgressRing : Control
    {
        public static readonly StyledProperty<double> ValueProperty =
            AvaloniaProperty.Register<ProgressRing, double>(nameof(Value));

        public static readonly StyledProperty<IBrush?> RingBrushProperty =
            AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(RingBrush));

        public static readonly StyledProperty<IBrush?> TrackBrushProperty =
            AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(TrackBrush));

        public static readonly StyledProperty<double> ThicknessProperty =
            AvaloniaProperty.Register<ProgressRing, double>(nameof(Thickness), 2d);

        static ProgressRing()
        {
            AffectsRender<ProgressRing>(ValueProperty, RingBrushProperty, TrackBrushProperty, ThicknessProperty);
        }

        public double Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public IBrush? RingBrush
        {
            get => GetValue(RingBrushProperty);
            set => SetValue(RingBrushProperty, value);
        }

        public IBrush? TrackBrush
        {
            get => GetValue(TrackBrushProperty);
            set => SetValue(TrackBrushProperty, value);
        }

        public double Thickness
        {
            get => GetValue(ThicknessProperty);
            set => SetValue(ThicknessProperty, value);
        }

        public override void Render(DrawingContext context)
        {
            double width = Bounds.Width, height = Bounds.Height;
            if (width <= 2 || height <= 2) return;
            double thickness = Math.Max(1, Thickness);
            double radius = Math.Max(0, (Math.Min(width, height) - thickness) / 2);
            if (radius <= 0.5) return;
            var centre = new Point(width / 2, height / 2);
            if (TrackBrush != null)
                context.DrawEllipse(null, new Pen(TrackBrush, thickness), centre, radius, radius);
            var arc = BuildArc(Value, new Size(width, height), thickness);
            if (arc != null && RingBrush != null)
                context.DrawGeometry(null, new Pen(RingBrush, thickness, lineCap: PenLineCap.Round), arc);
        }

        public static (Point Start, Point End, double Sweep, bool IsLargeArc, double Radius)? ArcGeometry(
            double value, Size size, double thickness)
        {
            double fraction = Math.Clamp(value, 0, 1);
            if (fraction <= 0) return null;
            double line = Math.Max(1, thickness);
            double radius = Math.Max(0, (Math.Min(size.Width, size.Height) - line) / 2);
            if (radius <= 0.5) return null;
            var centre = new Point(size.Width / 2, size.Height / 2);
            double sweep = fraction * 360;
            return (PointOnRing(centre, radius, -90),
                    PointOnRing(centre, radius, sweep - 90),
                    sweep,
                    sweep > 180,
                    radius);
        }

        public static StreamGeometry? BuildArc(double value, Size size, double thickness)
        {
            var arc = ArcGeometry(value, size, thickness);
            if (arc == null) return null;
            var (start, end, sweep, isLargeArc, radius) = arc.Value;
            var top = new Point(size.Width / 2, size.Height / 2 - radius);
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                if (sweep >= 359.9)
                {
                    var bottom = new Point(top.X, top.Y + radius * 2);
                    context.BeginFigure(top, false);
                    context.ArcTo(bottom, new Size(radius, radius), 0, false, SweepDirection.Clockwise);
                    context.ArcTo(top, new Size(radius, radius), 0, false, SweepDirection.Clockwise);
                    context.EndFigure(false);
                    return geometry;
                }
                context.BeginFigure(start, false);
                context.ArcTo(end, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise);
                context.EndFigure(false);
            }
            return geometry;
        }

        private static Point PointOnRing(Point centre, double radius, double degrees)
        {
            double radians = degrees * Math.PI / 180;
            return new Point(centre.X + radius * Math.Cos(radians), centre.Y + radius * Math.Sin(radians));
        }
    }
}

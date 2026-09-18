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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CrimsonX.Controls
{
    public class StabilityGraph : Control
    {
        private const int Slots = 20;
        private const double MinScalePing = 60;
        private const double TopPadding = 4;
        private const double BottomPadding = 2;

        private static readonly IBrush LineBrush = new SolidColorBrush(Color.Parse("#A0AEC0"));
        private static readonly IBrush FillBrush = new SolidColorBrush(Color.Parse("#2A8B949E"));
        private static readonly IBrush AvgBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xE2, 0xE8, 0xF0));
        private static readonly IBrush LossBrush = new SolidColorBrush(Color.Parse("#CCFC8181"));
        private static readonly IBrush LossFillBrush = new SolidColorBrush(Color.FromArgb(0x14, 0xFC, 0x81, 0x81));

        private readonly List<(bool Ok, long Ping)> _samples = new List<(bool, long)>();

        public void Reset()
        {
            _samples.Clear();
            InvalidateVisual();
        }

        public void AddSample(bool ok, long ping)
        {
            _samples.Add((ok, ping));
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            double width = Bounds.Width, height = Bounds.Height;
            if (width <= 8 || height <= 8) return;

            double drawHeight = height - TopPadding - BottomPadding;
            double baseline = height - BottomPadding;
            double step = width / (Slots - 1);

            if (_samples.Count == 0) return;

            long maxPing = (long)MinScalePing;
            long totalPing = 0;
            int okCount = 0;
            foreach (var sample in _samples)
            {
                if (!sample.Ok) continue;
                okCount++;
                totalPing += sample.Ping;
                if (sample.Ping > maxPing) maxPing = sample.Ping;
            }
            maxPing = (long)(maxPing * 1.1);

            if (okCount > 0)
            {
                double avg = totalPing / (double)okCount;
                double avgY = baseline - Math.Clamp(avg / maxPing * drawHeight, 2, drawHeight);
                for (double x = 0; x < width; x += 7)
                {
                    context.DrawLine(new Pen(AvgBrush, 1), new Point(x, avgY), new Point(Math.Min(x + 3, width), avgY));
                }
            }

            for (int i = 0; i < _samples.Count && i < Slots; i++)
            {
                if (_samples[i].Ok) continue;

                double x = (i * step) - (step / 2) + 1;
                double markW = Math.Max(2, step - 2);
                context.DrawRectangle(LossFillBrush, null, new Rect(x, TopPadding, markW, drawHeight));
                context.DrawRectangle(LossBrush, null, new Rect(x, baseline - 7, markW, 7));
            }

            var runs = new List<List<Point>>();
            List<Point>? run = null;

            for (int i = 0; i < Slots; i++)
            {
                if (i < _samples.Count && _samples[i].Ok)
                {
                    double y = baseline - (_samples[i].Ping / (double)maxPing * drawHeight);
                    if (run == null)
                    {
                        run = new List<Point>();
                        runs.Add(run);
                    }
                    run.Add(new Point(i * step, y));
                }
                else
                {
                    run = null;
                }
            }

            foreach (var points in runs)
            {
                if (points.Count == 1)
                {
                    context.DrawEllipse(LineBrush, null, points[0], 2, 2);
                    continue;
                }

                context.DrawGeometry(FillBrush, null, BuildSpline(points, true, baseline));
                context.DrawGeometry(null, new Pen(LineBrush, 2) { LineJoin = PenLineJoin.Round }, BuildSpline(points, false, baseline));
            }
        }

        private static StreamGeometry BuildSpline(List<Point> points, bool isFill, double baseline)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                if (points.Count == 0) return geometry;

                if (isFill)
                {
                    ctx.BeginFigure(new Point(points[0].X, baseline), true);
                    ctx.LineTo(points[0]);
                }
                else
                {
                    ctx.BeginFigure(points[0], false);
                }

                for (int i = 1; i < points.Count; i++)
                {
                    var p0 = i >= 2 ? points[i - 2] : points[i - 1];
                    var p1 = points[i - 1];
                    var p2 = points[i];
                    var p3 = i + 1 < points.Count ? points[i + 1] : points[i];

                    const double t = 0.25;
                    var cp1 = new Point(p1.X + ((p2.X - p0.X) * t), p1.Y + ((p2.Y - p0.Y) * t));
                    var cp2 = new Point(p2.X - ((p3.X - p1.X) * t), p2.Y - ((p3.Y - p1.Y) * t));

                    ctx.CubicBezierTo(cp1, cp2, p2);
                }

                if (isFill)
                {
                    ctx.LineTo(new Point(points[points.Count - 1].X, baseline));
                }
            }
            return geometry;
        }
    }
}

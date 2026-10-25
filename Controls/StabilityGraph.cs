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
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CrimsonX.Controls
{
    public class StabilityGraph : Control
    {
        private const int Slots = 20;
        private const double MinScalePing = 60;
        private const double AxisTop = 13;
        private const double AxisBottom = 24;
        private const double AxisLeft = 34;
        private const double AxisLeftEmpty = 6;

        private static readonly IBrush LineBrush = new SolidColorBrush(Color.Parse("#A0AEC0"));
        private static readonly IBrush FillBrush = new SolidColorBrush(Color.Parse("#2A8B949E"));
        private static readonly IBrush AvgBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xE2, 0xE8, 0xF0));
        private static readonly IBrush LossBrush = new SolidColorBrush(Color.Parse("#CCFC8181"));
        private static readonly IBrush LossFillBrush = new SolidColorBrush(Color.FromArgb(0x14, 0xFC, 0x81, 0x81));
        private static readonly IBrush AxisBrush = new SolidColorBrush(Color.Parse("#3A4757"));
        private static readonly IBrush GridBrush = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
        private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#5A6270"));

        private readonly List<(bool Ok, long Ping)> _samples = new List<(bool, long)>();

        private bool _reserveScale;

        public string AxisYLabel { get; private set; } = "PING";

        public string AxisXLabel { get; private set; } = "TIME";

        public void SetAxisLabels(string? y, string? x)
        {
            AxisYLabel = y ?? "";
            AxisXLabel = x ?? "";
            InvalidateVisual();
        }

        public void ReserveScale(bool reserve)
        {
            if (_reserveScale == reserve) return;
            _reserveScale = reserve;
            InvalidateVisual();
        }

        public void Reset()
        {
            _samples.Clear();
            _reserveScale = false;
            InvalidateVisual();
        }

        public void AddSample(bool ok, long ping)
        {
            _samples.Add((ok, ping));
            while (_samples.Count > Slots) _samples.RemoveAt(0);
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            double width = Bounds.Width, height = Bounds.Height;
            if (width <= 8 || height <= 8) return;
            double top = AxisTop;
            double baseline = height - AxisBottom;
            double drawHeight = baseline - top;
            if (drawHeight <= 4) return;

            long maxPing = ScaleMaxPing();
            double left = AxisLeftEmpty;
            double right = width;
            if (_reserveScale || _samples.Count > 0)
            {
                double labelWidth = Math.Max(MeasureLabel(maxPing.ToString()), MeasureLabel((maxPing / 2).ToString()));
                left = Math.Clamp(labelWidth + 10, AxisLeftEmpty, AxisLeft);
                right = width - labelWidth;
            }
            double plotWidth = right - left;
            if (plotWidth <= 4) return;

            DrawAxes(context, left, top, right, baseline, maxPing);

            if (_samples.Count == 0) return;
            double step = plotWidth / (Slots - 1);
            long totalPing = 0;
            int okCount = 0;
            foreach (var sample in _samples)
            {
                if (!sample.Ok) continue;
                okCount++;
                totalPing += sample.Ping;
            }
            if (okCount > 0)
            {
                double avg = totalPing / (double)okCount;
                double avgY = baseline - Math.Clamp(avg / maxPing * drawHeight, 2, drawHeight);
                for (double x = left; x < right; x += 7)
                {
                    context.DrawLine(new Pen(AvgBrush, 1), new Point(x, avgY), new Point(Math.Min(x + 3, right), avgY));
                }
            }
            for (int i = 0; i < _samples.Count && i < Slots; i++)
            {
                if (_samples[i].Ok) continue;
                double x = left + (i * step) - (step / 2) + 1;
                double markW = Math.Max(2, step - 2);
                context.DrawRectangle(LossFillBrush, null, new Rect(x, top, markW, drawHeight));
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
                    run.Add(new Point(left + (i * step), y));
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

        private long ScaleMaxPing()
        {
            long maxPing = (long)MinScalePing;
            foreach (var sample in _samples)
            {
                if (sample.Ok && sample.Ping > maxPing) maxPing = sample.Ping;
            }
            return (long)(maxPing * 1.1);
        }

        private void DrawAxes(DrawingContext context, double left, double top, double right, double baseline, long maxPing)
        {
            var axisPen = new Pen(AxisBrush, 1);
            var gridPen = new Pen(GridBrush, 1);
            double height = baseline - top;
            double midY = top + (height / 2);
            double midX = left + ((right - left) / 2);

            context.DrawLine(gridPen, new Point(left, midY), new Point(right, midY));
            context.DrawLine(axisPen, new Point(left, top), new Point(left, baseline));
            context.DrawLine(axisPen, new Point(left, baseline), new Point(right, baseline));

            DrawLabel(context, AxisYLabel, AxisLeftEmpty, 0, LabelAlign.Left);
            if (_samples.Count > 0)
            {
                DrawLabel(context, maxPing.ToString(), left - 3, 11, LabelAlign.Right);
                DrawLabel(context, (maxPing / 2).ToString(), left - 3, midY - 5, LabelAlign.Right);
            }

            DrawLabel(context, "0", left, baseline + 2, LabelAlign.Left);
            DrawLabel(context, "5s", midX, baseline + 2, LabelAlign.Center);
            DrawLabel(context, "10s", right, baseline + 2, LabelAlign.Right);
            DrawLabel(context, AxisXLabel, right, baseline + 13, LabelAlign.Right);
        }

        private static double MeasureLabel(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 8, LabelBrush);
            return formatted.Width;
        }

        private enum LabelAlign
        {
            Left,

            Center,

            Right
        }

        private static void DrawLabel(DrawingContext context, string text, double x, double y, LabelAlign align)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 8, LabelBrush);
            double offset = align switch
            {
                LabelAlign.Center => formatted.Width / 2,
                LabelAlign.Right => formatted.Width,
                _ => 0
            };
            context.DrawText(formatted, new Point(x - offset, y));
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

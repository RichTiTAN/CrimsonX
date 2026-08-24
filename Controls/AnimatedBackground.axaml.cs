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
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using System;

namespace CrimsonX.Controls
{
    public partial class AnimatedBackground : UserControl
    {
        public static AnimatedBackground? Instance { get; private set; }

        private DispatcherTimer? _bgTimer;
        private DateTime _startTime;

        private TranslateTransform? _b1Trans, _b2Trans, _b3Trans;
        private ScaleTransform?    _e1Scale, _e2Scale, _e3Scale;
        private Ellipse?           _e1, _e2, _e3;
        private GradientStop?      _gs1Center, _gs1Edge;
        private GradientStop?      _gs2Center, _gs2Edge;
        private GradientStop?      _gs3Center, _gs3Edge;

        private const double D1 = 20.0;
        private static readonly (double x, double y)[] BKf1 = { (-150, 10), (50, -120), (-60, 120) };
        private static readonly (double sx, double sy, double op)[] EKf1 = { (1.4, 0.7, 0.6), (0.8, 1.2, 0.9), (1.2, 0.9, 0.7) };

        private const double D2 = 25.0;
        private static readonly (double x, double y)[] BKf2 = { (20, -150), (-120, 80), (140, -40) };
        private static readonly (double sx, double sy, double op)[] EKf2 = { (0.7, 1.4, 0.7), (1.3, 0.8, 1.0), (0.9, 1.1, 0.6) };

        private const double D3 = 18.0;
        private static readonly (double x, double y)[] BKf3 = { (150, 60), (-80, 130), (40, -130) };
        private static readonly (double sx, double sy, double op)[] EKf3 = { (1.3, 0.8, 0.6), (0.8, 1.3, 0.8), (1.2, 0.9, 0.7) };

        public AnimatedBackground()
        {
            Instance = this;
            InitializeComponent();
            GenerateDotMatrixOverlay();
        }

        protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            if (this.FindControl<Border>("b1") is { } b1 &&
                b1.RenderTransform is TranslateTransform bt1) _b1Trans = bt1;
            if (this.FindControl<Border>("b2") is { } b2 &&
                b2.RenderTransform is TranslateTransform bt2) _b2Trans = bt2;
            if (this.FindControl<Border>("b3") is { } b3 &&
                b3.RenderTransform is TranslateTransform bt3) _b3Trans = bt3;

            _e1 = this.FindControl<Ellipse>("e1");
            _e2 = this.FindControl<Ellipse>("e2");
            _e3 = this.FindControl<Ellipse>("e3");

            if (_e1?.RenderTransform is ScaleTransform st1) _e1Scale = st1;
            if (_e2?.RenderTransform is ScaleTransform st2) _e2Scale = st2;
            if (_e3?.RenderTransform is ScaleTransform st3) _e3Scale = st3;

            ResolveGradientStops(_e1, out _gs1Center, out _gs1Edge);
            ResolveGradientStops(_e2, out _gs2Center, out _gs2Edge);
            ResolveGradientStops(_e3, out _gs3Center, out _gs3Edge);

            _startTime = DateTime.UtcNow;
            _bgTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) }; 
            _bgTimer.Tick += BgTimer_Tick;
            _bgTimer.Start();
        }

        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _bgTimer?.Stop();
            _bgTimer = null;
        }

        private static void ResolveGradientStops(Ellipse? ellipse, out GradientStop? center, out GradientStop? edge)
        {
            center = edge = null;
            if (ellipse?.Fill is RadialGradientBrush rgb && rgb.GradientStops.Count >= 2)
            {
                center = rgb.GradientStops[0];
                edge   = rgb.GradientStops[1];
            }
        }

        private void BgTimer_Tick(object? sender, EventArgs e)
        {
            double elapsed = (DateTime.UtcNow - _startTime).TotalSeconds;

            if (_b1Trans != null && _e1Scale != null && _e1 != null)
                ApplyBlob(_b1Trans, _e1Scale, _e1, elapsed, D1, BKf1, EKf1);
            if (_b2Trans != null && _e2Scale != null && _e2 != null)
                ApplyBlob(_b2Trans, _e2Scale, _e2, elapsed, D2, BKf2, EKf2);
            if (_b3Trans != null && _e3Scale != null && _e3 != null)
                ApplyBlob(_b3Trans, _e3Scale, _e3, elapsed, D3, BKf3, EKf3);
        }

        private static void ApplyBlob(
            TranslateTransform trans, ScaleTransform scale, Ellipse ellipse,
            double elapsed, double duration,
            (double x, double y)[] bKf,
            (double sx, double sy, double op)[] eKf)
        {
            double cycle = elapsed % (2.0 * duration);
            double t = cycle < duration ? cycle / duration : 1.0 - (cycle - duration) / duration;

            double tx, ty, sx, sy, op;
            if (t <= 0.5)
            {
                double tt = t / 0.5;
                tx = Lerp(bKf[0].x, bKf[1].x, tt);
                ty = Lerp(bKf[0].y, bKf[1].y, tt);
                sx = Lerp(eKf[0].sx, eKf[1].sx, tt);
                sy = Lerp(eKf[0].sy, eKf[1].sy, tt);
                op = Lerp(eKf[0].op, eKf[1].op, tt);
            }
            else
            {
                double tt = (t - 0.5) / 0.5;
                tx = Lerp(bKf[1].x, bKf[2].x, tt);
                ty = Lerp(bKf[1].y, bKf[2].y, tt);
                sx = Lerp(eKf[1].sx, eKf[2].sx, tt);
                sy = Lerp(eKf[1].sy, eKf[2].sy, tt);
                op = Lerp(eKf[1].op, eKf[2].op, tt);
            }

            trans.X         = tx;
            trans.Y         = ty;
            scale.ScaleX    = sx;
            scale.ScaleY    = sy;
            ellipse.Opacity = op;
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public void UpdateTheme(Color c1, Color c2, Color c3)
        {
            if (_gs1Center != null) _gs1Center.Color = c1;
            if (_gs1Edge   != null) _gs1Edge.Color   = Color.FromArgb(0, c1.R, c1.G, c1.B);

            if (_gs2Center != null) _gs2Center.Color = c2;
            if (_gs2Edge   != null) _gs2Edge.Color   = Color.FromArgb(0, c2.R, c2.G, c2.B);

            if (_gs3Center != null) _gs3Center.Color = c3;
            if (_gs3Edge   != null) _gs3Edge.Color   = Color.FromArgb(0, c3.R, c3.G, c3.B);
        }

        private void GenerateDotMatrixOverlay()
        {
            int width  = 2560;
            int height = 1440;

            var bitmap = new WriteableBitmap(
                new Avalonia.PixelSize(width, height),
                new Avalonia.Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);

            using (var fb = bitmap.Lock())
            {
                int    stride = fb.RowBytes;
                IntPtr ptr    = fb.Address;

                unsafe
                {
                    byte* pStart = (byte*)ptr;
                    for (int i = 0; i < height * stride; i++) pStart[i] = 0;
                }

                var rnd        = new Random();
                int dotSpacing = 22;

                unsafe
                {
                    for (int y = 0; y < height; y += dotSpacing)
                    {
                        for (int x = 0; x < width; x += dotSpacing)
                        {
                            if (rnd.NextDouble() < 0.20) continue;

                            byte alphaByte = (byte)((0.1 + rnd.NextDouble() * 0.5) * 255);

                            for (int dy = 0; dy < 3; dy++)
                            {
                                if (y + dy >= height) continue;
                                byte* row = (byte*)ptr + ((y + dy) * stride);
                                for (int dx = 0; dx < 3; dx++)
                                {
                                    if (x + dx >= width) continue;
                                    int offset = (x + dx) * 4;
                                    row[offset + 0] = 0;
                                    row[offset + 1] = 0;
                                    row[offset + 2] = 0;
                                    row[offset + 3] = alphaByte;
                                }
                            }
                        }
                    }
                }
            }

            imgDotOverlay.Source = bitmap;
        }
    }
}

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
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;

namespace CrimsonX.Behaviors
{
    public static class SmoothScrollBehavior
    {
        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsEnabled", typeof(SmoothScrollBehavior), false);

        public static bool GetIsEnabled(AvaloniaObject element) => element.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(AvaloniaObject element, bool value) => element.SetValue(IsEnabledProperty, value);

        private static DispatcherTimer? _animTimer;
        private static double _targetOffset = 0;
        private static ScrollViewer? _currentScroller;
        private static double _scrollVelocity = 0;

        static SmoothScrollBehavior()
        {
            IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>(OnIsEnabledChanged);
        }

        private static void OnIsEnabledChanged(ScrollViewer scroller, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
            {
                scroller.AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            }
            else
            {
                scroller.RemoveHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged);
            }
        }

        private static void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (sender is ScrollViewer scroller)
            {
                if (e.Handled || e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control))
                    return;

                var visual = e.Source as Avalonia.Visual;
                ScrollViewer? targetScroller = null;
                while (visual != null)
                {
                    if (visual is ScrollViewer sv && sv.Extent.Height > sv.Viewport.Height)
                    {
                        double min = 0;
                        double max = sv.Extent.Height - sv.Viewport.Height;
                        bool canScroll = !((sv.Offset.Y <= min && e.Delta.Y > 0) || (sv.Offset.Y >= max && e.Delta.Y < 0));
                        if (canScroll)
                        {
                            targetScroller = sv;
                            break;
                        }
                    }
                    visual = visual.GetVisualParent();
                }

                if (targetScroller != null && targetScroller != scroller)
                    return;

                if (targetScroller != scroller)
                    return;

                if (_currentScroller != scroller || (_animTimer != null && !_animTimer.IsEnabled))
                {
                    _currentScroller = scroller;
                    _targetOffset = scroller.Offset.Y;
                    _scrollVelocity = 0;
                }

                if (Math.Sign(e.Delta.Y) != Math.Sign(_scrollVelocity))
                {
                    _scrollVelocity = 0;
                }

                double scrollAmount = 180; 
                _scrollVelocity += e.Delta.Y * scrollAmount;
                

                _targetOffset = _currentScroller.Offset.Y - _scrollVelocity;
                
                double maxOffset = scroller.Extent.Height - scroller.Viewport.Height;
                _targetOffset = Math.Max(0, Math.Min(_targetOffset, maxOffset));

                e.Handled = true;

                if (_animTimer == null)
                {
                    _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) }; 
                    _animTimer.Tick += AnimTimer_Tick;
                }
                
                if (!_animTimer.IsEnabled)
                    _animTimer.Start();
            }
        }

        private static void AnimTimer_Tick(object? sender, EventArgs e)
        {
            if (_currentScroller == null)
            {
                _animTimer?.Stop();
                return;
            }

            double currentOffset = _currentScroller.Offset.Y;
            double diff = _targetOffset - currentOffset;

            _scrollVelocity *= 0.82; 

            if (Math.Abs(diff) < 1.0 && Math.Abs(_scrollVelocity) < 1.0)
            {
                _currentScroller.Offset = new Vector(_currentScroller.Offset.X, _targetOffset);
                _animTimer?.Stop();
            }
            else
            {
                double easeAmount = diff * 0.28; 
                _currentScroller.Offset = new Vector(_currentScroller.Offset.X, currentOffset + easeAmount);
            }
        }
    }
}

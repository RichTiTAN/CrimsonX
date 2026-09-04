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
using System.Collections;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace CrimsonX.Helpers
{
    public class DragReorderHelper
    {
        private readonly ItemsControl _itemsControl;
        private readonly string[] _handleNames;
        private readonly Action<int, int> _onReordered;
        
        private bool _isDragging;
        private Control? _draggedContainer;
        private int _originalIndex = -1;
        private int _currentIndex = -1;
        private Point _startPointerPos;

        public DragReorderHelper(ItemsControl itemsControl, string handleName, Action<int, int> onReordered)
            : this(itemsControl, new[] { handleName }, onReordered)
        {
        }

        public DragReorderHelper(ItemsControl itemsControl, string[] handleNames, Action<int, int> onReordered)
        {
            _itemsControl = itemsControl;
            _handleNames = handleNames ?? new[] { "DragHandle" };
            _onReordered = onReordered;
            
            _itemsControl.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _itemsControl.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _itemsControl.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }
        
        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var src = e.Source as Visual;
            if (src == null) return;
            
            var handle = src.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(c => _handleNames.Contains(c.Name));
            if (handle == null) return;
            
            _draggedContainer = src.GetSelfAndVisualAncestors().OfType<ContentPresenter>().FirstOrDefault(c => c.Parent == _itemsControl || (c.Parent is Panel p && p.Parent is ItemsPresenter));
            if (_draggedContainer == null) return;
            
            _originalIndex = _itemsControl.IndexFromContainer(_draggedContainer);
            if (_originalIndex < 0) return;
            
            _currentIndex = _originalIndex;
            _isDragging = true;
            _startPointerPos = e.GetPosition(_itemsControl);
            
            _draggedContainer.ZIndex = 1000;
            
            var containers = _itemsControl.GetRealizedContainers();
            foreach (var container in containers)
            {
                if (container != null && container.RenderTransform == null)
                {
                    var transform = new TranslateTransform();
                    transform.Transitions = new Transitions
                    {
                        new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(250), Easing = new CubicEaseOut() }
                    };
                    container.RenderTransform = transform;
                }
            }
            
            if (_draggedContainer.RenderTransform is TranslateTransform dragTransform)
            {
                dragTransform.Transitions?.Clear();
            }
            
            e.Handled = true;
        }
        
        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isDragging || _draggedContainer == null) return;
            
            var currentPos = e.GetPosition(_itemsControl);
            double deltaY = currentPos.Y - _startPointerPos.Y;
            
            if (_draggedContainer.RenderTransform is TranslateTransform t)
            {
                t.Y = deltaY;
            }
            
            var containers = _itemsControl.GetRealizedContainers().ToList();
            if (containers.Count == 0) return;
            
            double itemHeight = _draggedContainer.Bounds.Height;
            if (itemHeight == 0) itemHeight = 49;
            
            int newIndex = _originalIndex + (int)Math.Round(deltaY / itemHeight);
            newIndex = Math.Max(0, Math.Min(containers.Count - 1, newIndex));
            
            if (newIndex != _currentIndex)
            {
                _currentIndex = newIndex;
                
                for (int i = 0; i < containers.Count; i++)
                {
                    if (i == _originalIndex) continue;
                    
                    var container = containers[i];
                    if (container?.RenderTransform is TranslateTransform ct)
                    {
                        if (ct.Transitions == null || ct.Transitions.Count == 0)
                        {
                            ct.Transitions = new Transitions { new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(250), Easing = new CubicEaseOut() } };
                        }
                        
                        if (i > _originalIndex && i <= _currentIndex)
                        {
                            ct.Y = -itemHeight; 
                        }
                        else if (i < _originalIndex && i >= _currentIndex)
                        {
                            ct.Y = itemHeight; 
                        }
                        else
                        {
                            ct.Y = 0; 
                        }
                    }
                }
            }
        }
        
        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_isDragging) return;
            
            _isDragging = false;
            
            if (_draggedContainer != null)
            {
                _draggedContainer.ZIndex = 0;
            }
            
            var containers = _itemsControl.GetRealizedContainers();
            foreach (var container in containers)
            {
                if (container != null && container.RenderTransform is TranslateTransform ct)
                {
                    ct.Transitions?.Clear();
                    ct.Y = 0;
                }
            }
            
            if (_currentIndex != _originalIndex && _currentIndex >= 0)
            {
                _onReordered?.Invoke(_originalIndex, _currentIndex);
            }
            
            _draggedContainer = null;
        }
    }
}

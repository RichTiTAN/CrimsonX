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
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using CrimsonX.Models;
using CrimsonX.Services;
using AS = CrimsonX.Localization.AppStrings;

namespace CrimsonX.Pages
{
    public partial class AppsGamesOverlay
    {
        private const int OptimizeRowRevealWidth = 70;
        private const int OptimizeRowGap = 10;
        private const int OptimizePillBodyHeight = 17;

        private const string OptimizeGearPath = "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.21,8.95 2.27,9.22 2.46,9.37L4.57,11C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.65 4.57,12.97L2.46,14.63C2.27,14.78 2.21,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.03 4.95,18.95L7.44,17.94C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.67 16.04,18.34 16.56,17.94L19.05,18.95C19.27,19.03 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z";

        private bool _optInit;
        private bool _optLayoutHooked;
        private bool _optPassQueued;
        private bool _optRunning;
        private bool _optReady;
        private bool _optOnConnect;
        private bool _optToggleVisible;
        private bool _optPressed;
        private bool _optLastEngineRunning;
        private bool _optApplyRestartPending;
        private bool _optRestartInFlight;
        private bool _optMenuShownVisual;
        private string _optFoundRaw = "";
        private string _optFoundLabel = "";
        private long _optFoundPingMs;

        private Canvas _optCanvas;
        private Border _optBox;
        private Panel _optInner;
        private TextBlock _optText;
        private Button _optMainButton;
        private Button _optGearButton;
        private Border _optFill;
        private ScaleTransform _optFillScale;
        private Border _optBreath;
        private Border _optReadyRing;
        private Panel _optMenu;
        private Button _optToggleButton;
        private Border _optTogglePill;
        private TextBlock _optToggleText;
        private TextBlock _optAdapterLabel;
        private ComboBox _optAdapterCombo;
        private Border _optAdapterPill;
        private IBrush _optIdleTextBrush;
        private IBrush _optReadyTextBrush;

        private AppOptimizeResult _optPendingResult;
        private string _optAdapterName = "Default";
        private string _optAdapterIp = "";
        private bool _optLoadingAdapter;
        private readonly List<string> _optAdapterNames = new List<string> { "Default" };
        private readonly List<string> _optAdapterIps = new List<string> { "" };
        private static readonly Cursor OptHandCursor = new Cursor(StandardCursorType.Hand);
        private static readonly Cursor OptArrowCursor = new Cursor(StandardCursorType.Arrow);

        private readonly ConnectBreath _optBreathAnim = new ConnectBreath();
        private DispatcherTimer _optTimer;
        private DispatcherTimer _optWatchTimer;
        private double _optFillTarget;
        private double _optFillCurrent;
        private CancellationTokenSource _optCts;
        private int _optRunId;
        private bool? _optLockState;
        private bool _optLockPanel;
        private bool _optResetting;
        private bool _optBusHooked;
        private Window _optWindow;
        private AppOptimizeState _optState = new AppOptimizeState();

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            this.AttachedToVisualTree -= Optimize_Attached;
            this.AttachedToVisualTree += Optimize_Attached;
            EnsureOptimizeUi();
        }

        private void Optimize_Attached(object? sender, VisualTreeAttachmentEventArgs e) => EnsureOptimizeUi();

        private void EnsureOptimizeUi()
        {
            AttachOptimizeHooks();
            if (_optInit)
            {
                ApplyOptimizeLanguage();
                ApplyOptimizeVisual();
                return;
            }
            var bar = this.FindControl<StackPanel>("panOverlayBottomBar");
            if (bar == null)
            {
                this.LayoutUpdated -= Optimize_LayoutRetry;
                this.LayoutUpdated += Optimize_LayoutRetry;
                return;
            }
            this.LayoutUpdated -= Optimize_LayoutRetry;
            _optInit = true;
            LoadOptimizeState();
            DropStaleOptimizeSession();
            BuildOptimizeUi(bar);
            WireOptimize();
            ApplyOptimizeLanguage();
            ApplyOptimizeVisual();
            OptimizeRefreshUi();
        }

        private void Optimize_LayoutRetry(object? sender, EventArgs e) => EnsureOptimizeUi();

        private void LoadOptimizeState()
        {
            _optState = AppOptimizeStore.Load();
            _optOnConnect = _optState.OnConnect;
            _optFoundRaw = _optState.FoundRaw ?? "";
            _optFoundLabel = _optState.FoundLabel ?? "";
            _optFoundPingMs = _optState.FoundPingMs;
            _optAdapterName = string.IsNullOrWhiteSpace(_optState.AdapterName) ? "Default" : _optState.AdapterName;
            _optAdapterIp = _optState.AdapterIp ?? "";
            _optReady = _optState.Ready && _optFoundRaw.Length > 0;
        }

        private void DropStaleOptimizeSession()
        {
            if (!_optReady && string.IsNullOrEmpty(_optState.FoundRaw)) return;
            ResetOptimizeOnExit(refresh: false);
        }

        private void WireOptimize()
        {
            if (!_optLayoutHooked)
            {
                _optLayoutHooked = true;
                this.LayoutUpdated += Optimize_LayoutUpdated;
                _ruleRows.CollectionChanged += (s, e) => PostOptimizePass();
            }
            _optWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _optWatchTimer.Tick += Optimize_WatchTick;
            _optWatchTimer.Start();
            _optLastEngineRunning = _state.IsEngineRunning;
        }

        private void Optimize_LayoutUpdated(object? sender, EventArgs e)
        {
            if (!_optInit) return;
            PositionOptimizeToggle();
        }

        private void PostOptimizePass()
        {
            if (!_optInit || _optPassQueued) return;
            _optPassQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _optPassQueued = false;
                OptimizeRefreshUi();
            }, DispatcherPriority.Background);
        }

        private void Optimize_WatchTick(object? sender, EventArgs e)
        {
            try
            {
                bool running = _state.IsEngineRunning;
                if (running && !_optLastEngineRunning && _optOnConnect && CanOptimizeNow()
                    && !_optRunning && !_optReady && _optPendingResult == null)
                {
                    _ = RunOptimizeAsync();
                }
                _optLastEngineRunning = running;
                if (_optPendingResult != null)
                {
                    if (_state.IsConnected)
                    {
                        var pending = _optPendingResult;
                        _optPendingResult = null;
                        PromoteOptimizeResult(pending);
                    }
                    else if (!_state.IsEngineRunning)
                    {
                        _optPendingResult = null;
                    }
                }
                if (_optReady) ApplyOptimizeToChosenRules();
                if (_optApplyRestartPending && !_optRestartInFlight && _state.IsConnected && !_isApplyingRules)
                {
                    _optApplyRestartPending = false;
                    _ = RestartForOptimizeAsync();
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
            ApplyOptimizeVisual();
            OptimizeRefreshUi();
        }

        private void OptimizeRefreshUi()
        {
            try
            {
                PositionOptimizeToggle();
                OptimizeRowPass();
                SyncOptimizeEditorLock();
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
        }

        private void BuildOptimizeUi(StackPanel bar)
        {
            _optInner = new Panel
            {
                MinHeight = OptimizePillBodyHeight,
                MinWidth = 84,
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RenderTransform = new ScaleTransform(1, 1)
            };
            _optInner.Transitions = new Transitions
            {
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(75) }
            };

            var fillClip = new Border
            {
                CornerRadius = new CornerRadius(6, 0, 0, 6),
                ClipToBounds = true,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _optFill = new Border
            {
                Background = OptimizeResourceBrush("ThemeGlowBrush", "#B82E42"),
                Opacity = 0,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative)
            };
            _optFillScale = new ScaleTransform(0, 1);
            _optFill.RenderTransform = _optFillScale;
            fillClip.Child = _optFill;

            _optBreath = new Border { Margin = new Thickness(-2), IsHitTestVisible = false };
            _optBreath.Classes.Add("connectBreath");

            _optReadyRing = new Border
            {
                Margin = new Thickness(-2),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                BorderBrush = OptimizeResourceBrush("ThemeSuccessBrush", "#38A169"),
                Opacity = 0,
                IsHitTestVisible = false
            };

            _optIdleTextBrush = new SolidColorBrush(Color.Parse("#E2E8F0"));
            _optReadyTextBrush = OptimizeResourceBrush("ThemeSuccessGlowBrush", "#38A169");
            _optText = new TextBlock
            {
                Text = AS.OptimizeLabel,
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                LetterSpacing = 0.5,
                Foreground = _optIdleTextBrush,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _optText.Transitions = new Transitions
            {
                new BrushTransition { Property = TextBlock.ForegroundProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() }
            };

            _optMainButton = new Button
            {
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                Padding = new Thickness(5, 0),
                MinWidth = 76,
                MinHeight = OptimizePillBodyHeight,
                CornerRadius = new CornerRadius(6, 0, 0, 6),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                Content = _optText
            };
            _optMainButton.Classes.Add("optBtn");
            ToolTip.SetTip(_optMainButton, AS.TtOptimize);
            _optMainButton.Click += OptimizeMain_Click;

            var gearIcon = new PathIcon
            {
                Data = Geometry.Parse(OptimizeGearPath),
                Width = 12,
                Height = 12,
                Foreground = new SolidColorBrush(Color.Parse("#8B949E")),
                IsHitTestVisible = false
            };
            _optGearButton = new Button
            {
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                Padding = new Thickness(5, 0),
                MinWidth = 22,
                MinHeight = OptimizePillBodyHeight,
                CornerRadius = new CornerRadius(0, 6, 6, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                Content = gearIcon
            };
            _optGearButton.Classes.Add("optBtn");
            ToolTip.SetTip(_optGearButton, AS.TtOptimizeGear);
            _optGearButton.Click += OptimizeGear_Click;

            var separator = new Border { Width = 1, Background = new SolidColorBrush(Color.Parse("#2D3748")) };

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_optMainButton, 0);
            Grid.SetColumn(separator, 1);
            Grid.SetColumn(_optGearButton, 2);
            content.Children.Add(_optMainButton);
            content.Children.Add(separator);
            content.Children.Add(_optGearButton);

            var press = new Border
            {
                Background = OptimizeResourceBrush("ButtonBackgroundPressed", "#1AFFFFFF"),
                Opacity = 0,
                IsHitTestVisible = false,
                CornerRadius = new CornerRadius(5),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            _optInner.Children.Add(fillClip);
            _optInner.Children.Add(_optBreath);
            _optInner.Children.Add(_optReadyRing);
            _optInner.Children.Add(content);
            _optInner.Children.Add(press);

            _optBox = new Border
            {
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = _optInner
            };
            _optBox.Classes.Add("floatBox");
            _optBox.Classes.Add("optBox");

            _optMenu = BuildOptimizeGearPanel();
            _optMenu.Opacity = 0;
            _optMenu.IsHitTestVisible = false;
            _optMenu.RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative);
            _optMenu.RenderTransform = TransformOperations.Parse("translateY(6px)");
            _optMenu.Transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(150), Easing = new CubicEaseOut() },
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(150), Easing = new CubicEaseOut() }
            };

            var masterBox = this.FindControl<Border>("panMasterBox");
            int index = masterBox != null ? bar.Children.IndexOf(masterBox) : -1;
            if (index < 0) bar.Children.Add(_optBox);
            else bar.Children.Insert(index, _optBox);

            _optCanvas = new Canvas
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _optCanvas.Children.Add(_optMenu);
            _optInner.Children.Add(_optCanvas);

            WireOptimizePress(press);
            SetApplyChangesBoxVisible(_applyChangesBoxVisible);
        }

        private Panel BuildOptimizeGearPanel()
        {
            var menu = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            menu.Children.Add(BuildOptimizeAdapterPill());
            menu.Children.Add(BuildOptimizeOnConnectPill());
            LoadOptimizeAdapters();
            return menu;
        }

        private Border BuildOptimizeAdapterPill()
        {
            _optAdapterLabel = new TextBlock
            {
                Text = AS.OptimizeAdapter,
                FontSize = 9,
                FontWeight = FontWeight.Bold,
                LetterSpacing = 0.5,
                Foreground = new SolidColorBrush(Color.Parse("#8B949E")),
                VerticalAlignment = VerticalAlignment.Center
            };
            var labelCell = new Border { Padding = new Thickness(9, 3), Child = _optAdapterLabel };

            _optAdapterCombo = new ComboBox
            {
                Width = 14,
                MinHeight = 18,
                Height = 18,
                FontSize = 9,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                VerticalContentAlignment = VerticalAlignment.Center,
                SelectionBoxItemTemplate = new FuncDataTemplate<object?>((_, _) => new Panel(), false)
            };
            ToolTip.SetTip(_optAdapterCombo, AS.TtOptimizeAdapter);
            _optAdapterCombo.SelectionChanged += OptimizeAdapter_Changed;
            Border? pill = null;
            _optAdapterCombo.TemplateApplied += (_, e) =>
            {
                if (e.NameScope.Find<PathIcon>("DropDownGlyph") is { } glyph)
                {
                    Grid.SetColumn(glyph, 0);
                    Grid.SetColumnSpan(glyph, 2);
                    glyph.HorizontalAlignment = HorizontalAlignment.Left;
                    glyph.Margin = new Thickness(0);
                }
                var popup = e.NameScope.Find<Popup>("PART_Popup")
                    ?? Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(_optAdapterCombo)
                        .OfType<Popup>().FirstOrDefault();
                if (popup != null && pill != null) popup.PlacementTarget = pill;
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(labelCell, 0);
            Grid.SetColumn(_optAdapterCombo, 1);
            row.Children.Add(labelCell);
            row.Children.Add(_optAdapterCombo);

            pill = new Border { Child = row };
            pill.Classes.Add("floatBox");
            pill.Cursor = OptHandCursor;
            pill.PointerPressed += (_, e) =>
            {
                if (_optRunning)
                {
                    e.Handled = true;
                    return;
                }
                if (e.Source is Visual src && _optAdapterCombo != null && IsVisualOrAncestor(src, _optAdapterCombo)) return;
                if (_optAdapterCombo != null) _optAdapterCombo.IsDropDownOpen = true;
                e.Handled = true;
            };
            _optAdapterPill = pill;
            return pill;
        }

        private Border BuildOptimizeOnConnectPill()
        {
            _optToggleText = new TextBlock
            {
                Text = AS.OptimizeOnConnect,
                FontSize = 9,
                FontWeight = FontWeight.Bold,
                LetterSpacing = 0.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            _optToggleButton = new Button
            {
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = _optToggleText
            };
            _optToggleButton.Classes.Add("pillBtn");
            ToolTip.SetTip(_optToggleButton, AS.TtOptimizeOnConnect);
            _optToggleButton.Click += OptimizeOnConnect_Click;

            var pill = new Border { Child = _optToggleButton };
            pill.Classes.Add("floatBox");
            pill.Classes.Add("optOnPill");
            _optTogglePill = pill;
            return pill;
        }

        private void LoadOptimizeAdapters()
        {
            var combo = _optAdapterCombo;
            if (combo == null) return;
            _optAdapterNames.Clear();
            _optAdapterNames.Add("Default");
            _optAdapterIps.Clear();
            _optAdapterIps.Add("");
            var items = new List<string> { AS.AdapterDefault };
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var ipv4 = nic.GetIPProperties().UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                    if (ipv4 == null || string.IsNullOrWhiteSpace(ipv4.Address.ToString())) continue;
                    items.Add($"{nic.Name} - {ipv4.Address}");
                    _optAdapterNames.Add(nic.Name);
                    _optAdapterIps.Add(ipv4.Address.ToString());
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
            _optLoadingAdapter = true;
            try
            {
                combo.ItemsSource = items;
                int index = 0;
                if (!string.IsNullOrWhiteSpace(_optAdapterIp))
                {
                    int byIp = _optAdapterIps.FindIndex(ip => string.Equals(ip, _optAdapterIp, StringComparison.Ordinal));
                    if (byIp > 0) index = byIp;
                }
                if (index == 0 && !string.IsNullOrWhiteSpace(_optAdapterName))
                {
                    int byName = _optAdapterNames.FindIndex(n => string.Equals(n, _optAdapterName, StringComparison.OrdinalIgnoreCase));
                    if (byName > 0) index = byName;
                }
                combo.SelectedIndex = index;
                _optAdapterName = _optAdapterNames[index];
                _optAdapterIp = _optAdapterIps[index];
                _optState.AdapterName = _optAdapterName;
                _optState.AdapterIp = _optAdapterIp;
                AppOptimizeStore.Save(_optState);
            }
            finally
            {
                _optLoadingAdapter = false;
            }
        }

        private void OptimizeAdapter_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (_optLoadingAdapter || _optRunning || _optAdapterCombo == null) return;
            int index = _optAdapterCombo.SelectedIndex;
            if (index < 0 || index >= _optAdapterNames.Count) index = 0;
            _optAdapterName = _optAdapterNames[index];
            _optAdapterIp = _optAdapterIps[index];
            _optState.AdapterName = _optAdapterName;
            _optState.AdapterIp = _optAdapterIp;
            AppOptimizeStore.Save(_optState);
            CloseOptimizeMenu();
        }

        private void PositionOptimizeToggle()
        {
            if (_optMenu == null) return;
            ApplyOptimizeMenuVisual();
            if (!_optToggleVisible) return;
            if (_optCanvas == null || _optBox == null) return;
            _optMenu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double menuWidth = _optMenu.Bounds.Width > 0 ? _optMenu.Bounds.Width : _optMenu.DesiredSize.Width;
            double menuHeight = _optMenu.Bounds.Height > 0 ? _optMenu.Bounds.Height : _optMenu.DesiredSize.Height;
            if (_optBox.Bounds.Width <= 0) return;
            double left = (_optBox.Bounds.Width - menuWidth) / 2;
            double top = -menuHeight - 11;
            double currentLeft = Canvas.GetLeft(_optMenu);
            if (double.IsNaN(currentLeft) || Math.Abs(currentLeft - left) > 0.5) Canvas.SetLeft(_optMenu, left);
            double currentTop = Canvas.GetTop(_optMenu);
            if (double.IsNaN(currentTop) || Math.Abs(currentTop - top) > 0.5) Canvas.SetTop(_optMenu, top);
        }

        private void ApplyOptimizeMenuVisual()
        {
            if (_optMenu == null) return;
            if (_optMenuShownVisual == _optToggleVisible)
            {
                _optMenu.IsHitTestVisible = _optToggleVisible;
                return;
            }
            _optMenuShownVisual = _optToggleVisible;
            _optMenu.IsHitTestVisible = _optToggleVisible;
            _optMenu.Opacity = _optToggleVisible ? 1.0 : 0.0;
            _optMenu.RenderTransform = TransformOperations.Parse(_optToggleVisible ? "translateY(0px)" : "translateY(6px)");
        }

        private void WireOptimizePress(Border press)
        {
            _optBox.AddHandler(InputElement.PointerPressedEvent, (s, e) =>
            {
                _optPressed = true;
                SetOptimizePressed(press, true);
            }, RoutingStrategies.Bubble, handledEventsToo: true);
            _optBox.AddHandler(InputElement.PointerReleasedEvent, (s, e) =>
            {
                if (!_optPressed) return;
                _optPressed = false;
                SetOptimizePressed(press, false);
            }, RoutingStrategies.Bubble, handledEventsToo: true);
            _optBox.AddHandler(InputElement.PointerCaptureLostEvent, (s, e) =>
            {
                _optPressed = false;
                SetOptimizePressed(press, false);
            }, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        private void SetOptimizePressed(Border press, bool pressed)
        {
            if (press != null) press.Opacity = pressed ? 1.0 : 0.0;
            if (_optInner != null)
            {
                double s = pressed ? 0.97 : 1.0;
                _optInner.RenderTransform = new ScaleTransform(s, s);
            }
        }

        private static IBrush OptimizeResourceBrush(string key, string fallback)
        {
            try
            {
                var res = global::Avalonia.Application.Current?.Resources[key];
                if (res is IBrush brush) return brush;
            }
            catch { }
            return new SolidColorBrush(Color.Parse(fallback));
        }

        private async void OptimizeMain_Click(object? sender, RoutedEventArgs e)
        {
            if (_optRunning || _optPendingResult != null)
            {
                AbandonOptimize();
                return;
            }
            if (_optReady)
            {
                DisableOptimize();
                return;
            }
            if (!CanOptimizeNow()) return;
            await RunOptimizeAsync();
        }

        private void AbandonOptimize()
        {
            _optRunId++;
            _optPendingResult = null;
            try { _optCts?.Cancel(); } catch { }
            _optRunning = false;
            _optFillTarget = 0;
            _optFillCurrent = 0;
            _optTimer?.Stop();
            ApplyOptimizeVisual();
        }

        private void OptimizeGear_Click(object? sender, RoutedEventArgs e)
        {
            _optToggleVisible = !_optToggleVisible;
            PositionOptimizeToggle();
        }

        private void OptimizeOnConnect_Click(object? sender, RoutedEventArgs e)
        {
            _optOnConnect = !_optOnConnect;
            _optState.OnConnect = _optOnConnect;
            AppOptimizeStore.Save(_optState);
            SyncOptimizeToggleVisual();
        }

        private void ApplyOptimizeLanguage()
        {
            if (_optMainButton != null) ToolTip.SetTip(_optMainButton, AS.TtOptimize);
            if (_optGearButton != null) ToolTip.SetTip(_optGearButton, AS.TtOptimizeGear);
            if (_optToggleButton != null) ToolTip.SetTip(_optToggleButton, AS.TtOptimizeOnConnect);
            if (_optAdapterCombo != null) ToolTip.SetTip(_optAdapterCombo, AS.TtOptimizeAdapter);
            SyncOptimizeText();
            SyncOptimizeToggleVisual();
        }

        private void SyncOptimizeText()
        {
            if (_optText == null) return;
            AS.Apply(_optText, IsOptimizingVisual ? AS.OptimizingLabel : _optReady ? AS.OptimizeReady : AS.OptimizeLabel);
            var brush = _optReady ? _optReadyTextBrush : _optIdleTextBrush;
            if (brush != null && !ReferenceEquals(_optText.Foreground, brush)) _optText.Foreground = brush;
        }

        private void SyncOptimizeToggleVisual()
        {
            if (_optToggleButton != null)
            {
                if (_optOnConnect)
                {
                    if (!_optToggleButton.Classes.Contains("on")) _optToggleButton.Classes.Add("on");
                }
                else
                {
                    _optToggleButton.Classes.Remove("on");
                }
            }
            if (_optTogglePill != null)
            {
                if (_optOnConnect)
                {
                    if (!_optTogglePill.Classes.Contains("on")) _optTogglePill.Classes.Add("on");
                }
                else
                {
                    _optTogglePill.Classes.Remove("on");
                }
            }
            if (_optToggleText != null) AS.Apply(_optToggleText, AS.OptimizeOnConnect);
            if (_optAdapterLabel != null) AS.Apply(_optAdapterLabel, AS.OptimizeAdapter);
        }

        private void ApplyOptimizeVisual()
        {
            if (!_optInit) return;
            SyncOptimizeText();
            if (_optReadyRing != null) _optReadyRing.Opacity = _optReady ? 1.0 : 0.0;
            if (IsOptimizingVisual)
            {
                EnsureOptimizeTimer();
                if (_optTimer != null && !_optTimer.IsEnabled) _optTimer.Start();
            }
            else
            {
                _optTimer?.Stop();
                _optFillTarget = 0;
                _optFillCurrent = 0;
                ApplyOptimizeFill(0, false);
                ApplyOptimizeBreath(false);
            }
            SyncOptimizeToggleVisual();
            SyncOptimizeEnabled();
            PositionOptimizeToggle();
        }

        private bool IsOptimizingVisual => _optRunning || _optPendingResult != null;

        private bool CanOptimizeNow()
            => (_state.IsEngineRunning || _state.IsConnected)
            && string.Equals(_cfg.LastXrayMode, "VPN Mode", StringComparison.OrdinalIgnoreCase);

        private void SyncOptimizeEnabled()
        {
            bool active = CanOptimizeNow() || _optReady || IsOptimizingVisual;
            if (_optBox != null)
            {
                var cursor = active ? OptHandCursor : OptArrowCursor;
                if (!ReferenceEquals(_optBox.Cursor, cursor)) _optBox.Cursor = cursor;
            }
            if (_optMainButton != null) _optMainButton.Cursor = active ? OptHandCursor : OptArrowCursor;
            if (_optGearButton != null) _optGearButton.Cursor = OptHandCursor;
            bool adapterLive = !_optRunning;
            if (_optAdapterCombo != null)
            {
                if (_optAdapterCombo.IsHitTestVisible != adapterLive) _optAdapterCombo.IsHitTestVisible = adapterLive;
                if (!adapterLive && _optAdapterCombo.IsDropDownOpen) _optAdapterCombo.IsDropDownOpen = false;
            }
            if (_optAdapterPill != null)
            {
                var pillCursor = adapterLive ? OptHandCursor : OptArrowCursor;
                if (!ReferenceEquals(_optAdapterPill.Cursor, pillCursor)) _optAdapterPill.Cursor = pillCursor;
            }
        }

        private void EnsureOptimizeTimer()
        {
            if (_optTimer != null) return;
            _optTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _optTimer.Tick += Optimize_TimerTick;
        }

        private void Optimize_TimerTick(object? sender, EventArgs e)
        {
            if (!IsOptimizingVisual)
            {
                _optTimer?.Stop();
                _optFillTarget = 0;
                _optFillCurrent = 0;
                ApplyOptimizeFill(0, false);
                ApplyOptimizeBreath(false);
                return;
            }
            if (!IsEffectivelyVisible) return;
            double diff = _optFillTarget - _optFillCurrent;
            if (Math.Abs(diff) < 0.005) _optFillCurrent = _optFillTarget;
            else _optFillCurrent += diff * 0.3;
            ApplyOptimizeFill(_optFillCurrent, _optFillCurrent > 0.001);
            ApplyOptimizeBreath(true);
        }

        private void SetOptimizeProgress(double pct)
        {
            if (!_optRunning) return;
            _optFillTarget = Math.Clamp(pct, 0.0, 1.0);
            EnsureOptimizeTimer();
            if (_optTimer != null && !_optTimer.IsEnabled) _optTimer.Start();
        }

        private void ApplyOptimizeFill(double pct, bool show)
        {
            if (_optFillScale == null || _optFill == null) return;
            _optFillScale.ScaleX = Math.Clamp(pct, 0.0, 1.0);
            _optFill.Opacity = show ? 0.35 : 0.0;
        }

        private void ApplyOptimizeBreath(bool running)
        {
            if (_optBreath == null) return;
            _optBreath.Opacity = _optBreathAnim.Next(DateTime.UtcNow, running);
        }

        private async Task RunOptimizeAsync()
        {
            if (_optRunning) return;
            if (!CanOptimizeNow()) return;
            int runId = ++_optRunId;
            var cts = new CancellationTokenSource();
            _optCts = cts;
            var ct = cts.Token;
            _optRunning = true;
            _optPendingResult = null;
            _optReady = false;
            _optState.Ready = false;
            AppOptimizeStore.Save(_optState);
            _optFillTarget = 0;
            _optFillCurrent = 0;
            ApplyOptimizeVisual();

            var progress = new Progress<double>(SetOptimizeProgress);
            try
            {
                bool connecting = !_state.IsConnected;
                int concurrency = connecting
                    ? AppOptimizerService.ConnectingConcurrency
                    : AppOptimizerService.ConnectedConcurrency;
                string adapterIp = _optAdapterIp ?? "";
                var result = await Task.Run(
                    () => AppOptimizerService.RunAsync(_cfg, adapterIp, concurrency, progress, ct), ct);
                if (runId != _optRunId || ct.IsCancellationRequested) return;
                if (result == null || string.IsNullOrEmpty(result.Raw))
                {
                    MainWindow.Instance?.ShowToast(AS.ToastOptimizeFailed, ToastKind.Error);
                    return;
                }
                if (_state.IsConnected) PromoteOptimizeResult(result);
                else _optPendingResult = result;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
            finally
            {
                cts.Dispose();
                if (runId == _optRunId)
                {
                    _optRunning = false;
                    if (ReferenceEquals(_optCts, cts)) _optCts = null;
                    ApplyOptimizeVisual();
                    if (_optReady) ApplyOptimizeToChosenRules();
                    PostOptimizePass();
                }
            }
        }

        private void PromoteOptimizeResult(AppOptimizeResult result)
        {
            if (result == null || string.IsNullOrEmpty(result.Raw)) return;
            _optFoundRaw = result.Raw;
            _optFoundLabel = result.Label;
            _optFoundPingMs = result.PingMs;
            _optReady = true;
            _optState.Ready = true;
            _optState.FoundRaw = result.Raw;
            _optState.FoundLabel = result.Label;
            _optState.FoundPingMs = result.PingMs;
            AppOptimizeStore.Save(_optState);
            ApplyOptimizeToChosenRules();
            if (_optState.Choices.Any(c => c.Chosen) && _state.IsEngineRunning) _optApplyRestartPending = true;
            MainWindow.Instance?.ShowToast(string.Format(AS.ToastOptimizeReady, _optFoundPingMs), ToastKind.Success);
        }

        private void OptimizeRowPass()
        {
            var lst = this.FindControl<ItemsControl>("lstRules");
            if (lst == null) return;
            var views = (lst.ItemsSource as IEnumerable<AppRuleViewModel>)?.ToList();
            if (views == null) return;
            for (int i = 0; i < views.Count; i++)
            {
                var container = lst.ContainerFromIndex(i);
                if (container == null) continue;
                var view = views[i];
                InjectOptimizeRowButton(container, view.RuleId);
                var buttons = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(container)
                    .OfType<Button>().Where(b => b.Name == "btnOptimizeItem").ToList();
                if (buttons.Count == 0) continue;
                bool on = IsRuleOptimized(view.RuleId);
                foreach (var button in buttons)
                {
                    double maxWidth = _optReady ? OptimizeRowRevealWidth : 0;
                    if (Math.Abs(button.MaxWidth - maxWidth) > 0.5) button.MaxWidth = maxWidth;
                    double leftMargin = _optReady && button.Classes.Contains("optRowSpaced") ? OptimizeRowGap : 0;
                    if (Math.Abs(button.Margin.Left - leftMargin) > 0.5)
                        button.Margin = new Thickness(leftMargin, 0, 0, 0);
                    if (button.Content is TextBlock label && label.Text != AS.OptimizeLabel) AS.Apply(label, AS.OptimizeLabel);
                    if (on)
                    {
                        if (!button.Classes.Contains("activeOpt")) button.Classes.Add("activeOpt");
                    }
                    else
                    {
                        button.Classes.Remove("activeOpt");
                    }
                }
            }
        }

        private void InjectOptimizeRowButton(Control container, string ruleId)
        {
            var handles = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(container)
                .OfType<Panel>()
                .Where(p => p.Name == "DragHandle" || p.Name == "DragHandleDefault")
                .ToList();
            foreach (var handle in handles)
            {
                if (handle.Parent is not Grid grid) continue;
                if (grid.Children.OfType<Button>().Any(b => b.Name == "btnOptimizeItem")) continue;
                int pencilColumn = handle.Name == "DragHandleDefault" ? 8 : 4;
                if (grid.ColumnDefinitions.Count <= pencilColumn) continue;
                foreach (var child in grid.Children.ToList())
                {
                    int column = Grid.GetColumn(child);
                    if (column >= pencilColumn) Grid.SetColumn(child, column + 1);
                }
                grid.ColumnDefinitions.Insert(pencilColumn, new ColumnDefinition { Width = GridLength.Auto });
                bool spaced = handle.Name == "DragHandleDefault";
                var button = BuildOptimizeRowButton(ruleId, spaced);
                if (spaced) button.Classes.Add("optRowSpaced");
                Grid.SetColumn(button, pencilColumn);
                grid.Children.Add(button);
            }
        }

        private Button BuildOptimizeRowButton(string ruleId, bool spaced)
        {
            var label = new TextBlock
            {
                Text = AS.OptimizeLabel,
                FontSize = 8,
                FontWeight = FontWeight.Bold,
                LetterSpacing = 0.5
            };
            var button = new Button
            {
                Name = "btnOptimizeItem",
                Tag = ruleId,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(7, 3),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(spaced && _optReady ? OptimizeRowGap : 0, 0, 0, 0),
                Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                ClipToBounds = true,
                MaxWidth = _optReady ? OptimizeRowRevealWidth : 0,
                Content = label
            };
            button.Transitions = new Transitions
            {
                new DoubleTransition { Property = Layoutable.MaxWidthProperty, Duration = TimeSpan.FromMilliseconds(200) },
                new ThicknessTransition { Property = Layoutable.MarginProperty, Duration = TimeSpan.FromMilliseconds(200) }
            };
            button.Classes.Add("optBtn");
            ToolTip.SetTip(button, AS.TtOptimizeItem);
            button.Click += OptimizeRow_Click;
            return button;
        }

        private bool IsRuleOptimized(string ruleId) => _optReady && IsRuleAppliedByState(ruleId);

        private bool IsRuleAppliedByState(string ruleId)
        {
            if (string.IsNullOrEmpty(_optState.FoundRaw) || string.IsNullOrEmpty(ruleId)) return false;
            var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule == null) return false;
            return string.Equals(rule.TcpRouting, "Custom", StringComparison.OrdinalIgnoreCase)
                && string.Equals(rule.UdpRouting, "Custom", StringComparison.OrdinalIgnoreCase)
                && string.Equals(rule.CustomProxyRaw, _optState.FoundRaw, StringComparison.Ordinal);
        }

        private void OptimizeRow_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string ruleId) return;
            if (!_optReady || string.IsNullOrEmpty(_optFoundRaw)) return;
            var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule == null) return;
            if (IsRuleOptimized(ruleId))
            {
                var choice = _optState.Choices.FirstOrDefault(c => c.RuleId == ruleId);
                if (choice != null) choice.Chosen = false;
                RevertRule(rule);
            }
            else
            {
                ApplyOptimizeToRule(rule);
            }
            AppOptimizeStore.Save(_optState);
            SaveRules();
            _hasPendingRuleChanges = true;
            UpdateOverlayConnectUI();
            RefreshOptimizeRow(ruleId);
            PostOptimizePass();
        }

        private void RefreshOptimizeRow(string ruleId)
        {
            var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule == null) return;
            for (int i = 0; i < _ruleRows.Count; i++)
            {
                if (string.Equals(_ruleRows[i].RuleId, ruleId, StringComparison.Ordinal))
                {
                    _ruleRows[i] = new AppRuleViewModel(rule);
                    return;
                }
            }
            RefreshList();
        }

        private void ApplyOptimizeToRule(AppGameRule rule)
        {
            if (rule == null || string.IsNullOrEmpty(_optFoundRaw)) return;
            var choice = EnsureOptimizeChoice(rule.Id);
            if (!choice.Chosen)
            {
                choice.PrevTcpRouting = rule.TcpRouting;
                choice.PrevUdpRouting = rule.UdpRouting;
                choice.PrevTcpAdapter = rule.TcpAdapter;
                choice.PrevUdpAdapter = rule.UdpAdapter;
                choice.PrevCustomProxyRaw = rule.CustomProxyRaw;
                choice.PrevCustomProxyLabel = rule.CustomProxyLabel;
            }
            choice.Chosen = true;
            string adapter = string.IsNullOrWhiteSpace(_optAdapterName) ? "Default" : _optAdapterName;
            rule.TcpRouting = "Custom";
            rule.UdpRouting = "Custom";
            rule.TcpAdapter = adapter;
            rule.UdpAdapter = adapter;
            rule.CustomProxyRaw = _optFoundRaw;
            rule.CustomProxyLabel = string.IsNullOrWhiteSpace(_optFoundLabel)
                ? AppRulesSingboxBuilder.LabelOf(_optFoundRaw)
                : _optFoundLabel;
        }

        private AppOptimizeChoice EnsureOptimizeChoice(string ruleId)
        {
            var choice = _optState.Choices.FirstOrDefault(c => c.RuleId == ruleId);
            if (choice == null)
            {
                choice = new AppOptimizeChoice { RuleId = ruleId };
                _optState.Choices.Add(choice);
            }
            return choice;
        }

        private void RevertRule(AppGameRule rule)
        {
            var choice = _optState.Choices.FirstOrDefault(c => c.RuleId == rule.Id);
            if (choice == null) return;
            if (!string.IsNullOrEmpty(choice.PrevTcpRouting)) rule.TcpRouting = choice.PrevTcpRouting;
            if (!string.IsNullOrEmpty(choice.PrevUdpRouting)) rule.UdpRouting = choice.PrevUdpRouting;
            if (!string.IsNullOrEmpty(choice.PrevTcpAdapter)) rule.TcpAdapter = choice.PrevTcpAdapter;
            if (!string.IsNullOrEmpty(choice.PrevUdpAdapter)) rule.UdpAdapter = choice.PrevUdpAdapter;
            rule.CustomProxyRaw = choice.PrevCustomProxyRaw ?? "";
            rule.CustomProxyLabel = choice.PrevCustomProxyLabel ?? "";
        }

        private void ApplyOptimizeToChosenRules()
        {
            if (!_optReady) return;
            var missing = _optState.Choices
                .Where(c => c.Chosen)
                .Select(c => _rules.FirstOrDefault(r => r.Id == c.RuleId))
                .Where(r => r != null && !IsRuleOptimized(r.Id))
                .ToList();
            if (missing.Count == 0) return;
            foreach (var rule in missing) ApplyOptimizeToRule(rule);
            AppOptimizeStore.Save(_optState);
            SaveRules(markDirty: false);
            if (_state.IsEngineRunning) _optApplyRestartPending = true;
            RefreshList();
        }

        private async Task RestartForOptimizeAsync()
        {
            var main = MainWindow.Instance;
            if (main == null) return;
            _optRestartInFlight = true;
            try
            {
                await main.RestartSingBoxOnlyAsync();
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
            finally
            {
                _optRestartInFlight = false;
            }
        }

        private void DisableOptimize()
        {
            RevertPersistedRules(_rules);
            ClearOptimizeReadyState();
            SaveRules(markDirty: false);
            if (_state.IsEngineRunning) _optApplyRestartPending = true;
            RefreshList();
            ApplyOptimizeVisual();
            PostOptimizePass();
        }

        private void AttachOptimizeHooks()
        {
            if (!_optBusHooked)
            {
                _optBusHooked = true;
                UiEventBus.Instance.ConnectionProgress += Optimize_ConnectionProgress;
            }
            if (_optWindow == null)
            {
                var window = TopLevel.GetTopLevel(this) as Window ?? MainWindow.Instance;
                if (window == null) return;
                _optWindow = window;
                window.Closing += (s, e) => ResetOptimizeOnExit(refresh: false);
                window.AddHandler(InputElement.PointerPressedEvent, Optimize_GlobalPointerPressed, RoutingStrategies.Tunnel);
            }
        }

        private void Optimize_GlobalPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!_optToggleVisible) return;
            if (_optAdapterCombo is { IsDropDownOpen: true }) return;
            if (e.Source is Visual source && IsInsideOptimizeMenu(source)) return;
            CloseOptimizeMenu();
        }

        private bool IsInsideOptimizeMenu(Visual source)
        {
            if (_optMenu != null && IsVisualOrAncestor(source, _optMenu)) return true;
            if (_optGearButton != null && IsVisualOrAncestor(source, _optGearButton)) return true;
            return false;
        }

        private static bool IsVisualOrAncestor(Visual source, Visual target)
        {
            if (ReferenceEquals(source, target)) return true;
            return Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(source).Contains(target);
        }

        private void CloseOptimizeMenu()
        {
            if (!_optToggleVisible) return;
            _optToggleVisible = false;
            ApplyOptimizeMenuVisual();
        }

        private void Optimize_ConnectionProgress(int percent)
        {
            if (percent >= 0) return;
            ResetOptimizeOnExit(refresh: true);
        }

        private void ResetOptimizeOnExit(bool refresh)
        {
            if (_optResetting) return;
            bool armed = _optReady
                || _optState.Ready
                || !string.IsNullOrEmpty(_optState.FoundRaw)
                || _optState.Choices.Any(c => c.Chosen);
            if (!armed) return;
            _optResetting = true;
            try
            {
                try { _optCts?.Cancel(); } catch { }
                _optPendingResult = null;
                bool inMemory = _rules.Count > 0;
                var rules = inMemory ? _rules : AppRulesService.Load();
                bool changed = RevertPersistedRules(rules);
                ClearOptimizeReadyState();
                if (changed)
                {
                    if (inMemory) SaveRules(markDirty: false);
                    else AppRulesService.Save(rules);
                }
                ApplyOptimizeVisual();
                if (refresh && inMemory) RefreshList();
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
            finally
            {
                _optResetting = false;
            }
        }

        private bool RevertPersistedRules(List<AppGameRule> rules)
        {
            string found = _optState.FoundRaw;
            if (rules == null || string.IsNullOrEmpty(found)) return false;
            bool changed = false;
            foreach (var rule in rules)
            {
                if (rule == null) continue;
                if (!string.Equals(rule.CustomProxyRaw, found, StringComparison.Ordinal)) continue;
                if (!string.Equals(rule.TcpRouting, "Custom", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(rule.UdpRouting, "Custom", StringComparison.OrdinalIgnoreCase)) continue;
                if (_optState.Choices.FirstOrDefault(c => c.RuleId == rule.Id) == null) continue;
                RevertRule(rule);
                changed = true;
            }
            return changed;
        }

        private void ClearOptimizeReadyState()
        {
            _optPendingResult = null;
            _optReady = false;
            _optFoundRaw = "";
            _optFoundLabel = "";
            _optFoundPingMs = 0;
            _optState.Ready = false;
            _optState.FoundRaw = "";
            _optState.FoundLabel = "";
            _optState.FoundPingMs = 0;
            AppOptimizeStore.Save(_optState);
        }

        private void SyncOptimizeEditorLock()
        {
            var editor = this.FindControl<Border>("panEditor");
            if (editor != null && editor.Opacity > 0 && !string.IsNullOrEmpty(_editingRuleId))
                SetCustomProxyLocked(false, IsRuleOptimized(_editingRuleId));
            var defaultEditor = this.FindControl<Border>("panDefaultEditor");
            if (defaultEditor != null && defaultEditor.Opacity > 0 && !string.IsNullOrEmpty(_editingDefaultRuleId))
                SetCustomProxyLocked(true, IsRuleOptimized(_editingDefaultRuleId));
        }

        private void SetCustomProxyLocked(bool defaultPanel, bool locked)
        {
            if (_optLockState == locked && _optLockPanel == defaultPanel) return;
            _optLockState = locked;
            _optLockPanel = defaultPanel;
            var box = CustomProxySectionBox(defaultPanel ? "lblDefaultCustomProxy" : "lblCustomProxy");
            if (box == null) return;
            box.Opacity = locked ? 0.5 : 1.0;
            box.IsHitTestVisible = !locked;
        }

        private Border CustomProxySectionBox(string labelName)
        {
            var label = this.FindControl<TextBlock>(labelName);
            if (label == null) return null;
            return Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(label)
                .OfType<Border>()
                .FirstOrDefault(b => b.Classes.Contains("editBox"));
        }
    }
}

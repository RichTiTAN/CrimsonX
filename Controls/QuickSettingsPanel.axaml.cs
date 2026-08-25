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
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System.Collections.Generic;

namespace CrimsonX.Controls
{
    public partial class QuickSettingsPanel : UserControl
    {
        public static QuickSettingsPanel? Instance { get; private set; }

        private Button _btnCustomize = null!;
        private StackPanel _panEditButtons = null!;
        
        private Button _btnSlot1 = null!;
        private Button _btnSlot2 = null!;
        
        private TextBlock _lblSlot1 = null!;
        private TextBlock _lblSlot2 = null!;
        private PathIcon _icoSlot1 = null!;
        private PathIcon _icoSlot2 = null!;
        
        private ToggleSwitch _togSlot1 = null!;
        private ToggleSwitch _togSlot2 = null!;
        
        private Grid _panSlot1Content = null!;
        private Grid _panSlot2Content = null!;
        
        private PathIcon _iconArrow1 = null!;
        private PathIcon _iconArrow2 = null!;

        private Popup _popupSlot1 = null!;
        private Popup _popupSlot2 = null!;
        private StackPanel _panPopupSlot1Items = null!;
        private StackPanel _panPopupSlot2Items = null!;

        private List<string> _availableSettings = new List<string>
        {
            "DIRECT UDP", "XRAY EXIT-NODE", "BIND ADAPTER", "DOH", 
            "SYSTEM DNS", "AD BLOCKER", "LAN CONNECTIONS", 
            "LAUNCH ON START-UP", "AUTO-CONNECT", "START MINIMIZED", 
            "MINIMIZE TO TRAY", "EXCLUDE LOCATIONS", "CUSTOM CONFIGS", 
            "DISABLE BACKGROUND CHECK", "DISABLE SEAMLESS SWAP"
        };

        private bool _isUpdating = false;

        private string _pendingSlot1 = "DIRECT UDP";
        private string _pendingSlot2 = "AUTO-CONNECT";

        public QuickSettingsPanel()
        {
            Instance = this;
            InitializeComponent();
        }

        private string GetLocalizedSettingName(string key)
        {
            switch (key)
            {
                case "DIRECT UDP": return CrimsonX.Localization.AppStrings.SplitTunnelDirectUDP;
                case "XRAY EXIT-NODE": return CrimsonX.Localization.AppStrings.CustomXrayExit;
                case "BIND ADAPTER": return CrimsonX.Localization.AppStrings.AdapterBinding;
                case "DOH": return CrimsonX.Localization.AppStrings.UpstreamDohUrl;
                case "SYSTEM DNS": return CrimsonX.Localization.AppStrings.SystemDns;
                case "AD BLOCKER": return CrimsonX.Localization.AppStrings.AdBlocker;
                case "LAN CONNECTIONS": return CrimsonX.Localization.AppStrings.AllowLan;
                case "LAUNCH ON START-UP": return CrimsonX.Localization.AppStrings.LaunchOnStartup;
                case "AUTO-CONNECT": return CrimsonX.Localization.AppStrings.AutoConnect;
                case "START MINIMIZED": return CrimsonX.Localization.AppStrings.StartMinimized;
                case "MINIMIZE TO TRAY": return CrimsonX.Localization.AppStrings.MinimizeToTray;
                case "EXCLUDE LOCATIONS": return CrimsonX.Localization.AppStrings.ExcludeLocationsTitle;
                case "CUSTOM CONFIGS": return CrimsonX.Localization.AppStrings.CustomConfigsTitle;
                case "DISABLE BACKGROUND CHECK": return CrimsonX.Localization.AppStrings.DisableBackgroundChecks;
                case "DISABLE SEAMLESS SWAP": return CrimsonX.Localization.AppStrings.DisableRefreshTimer;
                default: return key;
            }
        }

        private string? GetTooltipText(string key)
        {
            switch (key)
            {
                case "LOAD-BALANCE POLICY": return CrimsonX.Localization.AppStrings.TtLbPolicy;
                case "EXCLUDE LOCATIONS": return CrimsonX.Localization.AppStrings.ExcludeLocationsTooltip;
                case "DOH": return CrimsonX.Localization.AppStrings.TtDnsSettings;
                case "AD BLOCKER": return CrimsonX.Localization.AppStrings.TtAdBlocker;
                case "LANGUAGE": return CrimsonX.Localization.AppStrings.TtLanguage;
                case "DEBUG MODE": return CrimsonX.Localization.AppStrings.TtDebugMode;
                case "DIRECT UDP": return CrimsonX.Localization.AppStrings.SplitTunnelDirectUDPTooltip;
                case "XRAY EXIT-NODE": return CrimsonX.Localization.AppStrings.TtCustomXray;
                case "BIND ADAPTER": return CrimsonX.Localization.AppStrings.TtAdapterBinding;
                case "SYSTEM DNS": return CrimsonX.Localization.AppStrings.TtSystemDns;
                case "LAN CONNECTIONS": return CrimsonX.Localization.AppStrings.TtAllowLan;
                case "LAUNCH ON START-UP": return CrimsonX.Localization.AppStrings.TtLaunchOnStartup;
                case "AUTO-CONNECT": return CrimsonX.Localization.AppStrings.TtAutoConnect;
                case "START MINIMIZED": return CrimsonX.Localization.AppStrings.TtStartMinimized;
                case "MINIMIZE TO TRAY": return CrimsonX.Localization.AppStrings.TtMinimizeToTray;
                case "CUSTOM CONFIGS": return null;
                case "DISABLE BACKGROUND CHECK": return CrimsonX.Localization.AppStrings.TtDisableBackgroundChecks;
                case "DISABLE SEAMLESS SWAP": return CrimsonX.Localization.AppStrings.TtDisableRefreshTimer;
                default: return null;
            }
        }

        private Avalonia.Media.StreamGeometry? GetIconData(string key)
        {
            string pathData = key switch
            {
                "DIRECT UDP" => "M20,4H4C2.89,4 2,4.89 2,6V18C2,19.1 2.9,20 4,20H20C21.1,20 22,19.1 22,18V6C22,4.89 21.1,4 20,4M20,18H4V6H20V18M8.5,15H11L12.5,11.5L14,15H16.5L13.5,10L16.5,5H14L12.5,8.5L11,5H8.5L11.5,10L8.5,15Z",
                "XRAY EXIT-NODE" => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z",
                "BIND ADAPTER" => "M21 3H3C1.89 3 1 3.89 1 5v14c0 1.11.89 2 2 2h18c1.11 0 2-.89 2-2V5c0-1.11-.89-2-2-2zm-1 14H4V7h16v10z",
                "DOH" => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z",
                "SYSTEM DNS" => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z",
                "AD BLOCKER" => "M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4zm0 10.99h7c-.53 4.12-3.28 7.79-7 8.94V12H5V6.3l7-3.11v8.8z",
                "LAN CONNECTIONS" => "M16 11c1.66 0 2.99-1.34 2.99-3S17.66 5 16 5c-1.66 0-3 1.34-3 3s1.34 3 3 3zm-8 0c1.66 0 2.99-1.34 2.99-3S9.66 5 8 5C6.34 5 5 6.34 5 8s1.34 3 3 3zm0 2c-2.33 0-7 1.17-7 3.5V19h14v-2.5c0-2.33-4.67-3.5-7-3.5zm8 0c-.29 0-.62.02-.97.05 1.16.84 1.97 1.97 1.97 3.45V19h6v-2.5c0-2.33-4.67-3.5-7-3.5z",
                "LAUNCH ON START-UP" => "M13.13 22.19L11.5 18.36C13.07 17.78 14.54 17 15.9 16.09L13.13 22.19M5.64 12.5L1.81 10.87L7.91 8.1C7 9.46 6.22 10.93 5.64 12.5M21.61 2.39C21.61 2.39 16.66 .269 11 5.93C8.81 8.12 7.5 10.53 6.65 12.64L3.05 11.33L1.58 12.8L6.47 14.86L3.92 17.41L5 18.5L7.55 15.95L9.61 20.84L11.08 19.37L9.77 15.77C11.88 14.92 14.29 13.61 16.48 11.42C22.14 5.76 20.02 .81 20.02 .81L21.61 2.39Z",
                "AUTO-CONNECT" => "M3.9 12c0-1.71 1.39-3.1 3.1-3.1h4V7H7c-2.76 0-5 2.24-5 5s2.24 5 5 5h4v-1.9H7c-1.71 0-3.1-1.39-3.1-3.1zM8 13h8v-2H8v2zm9-6h-4v1.9h4c1.71 0 3.1 1.39 3.1 3.1s-1.39 3.1-3.1 3.1h-4V17h4c2.76 0 5-2.24 5-5s-2.24-5-5-5z",
                "START MINIMIZED" => "M19 13H5v-2h14v2z",
                "MINIMIZE TO TRAY" => "M21 3H3c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h18c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm0 16H3V5h18v14zm-10-7h2v3h-2z",
                "EXCLUDE LOCATIONS" => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z",
                "CUSTOM CONFIGS" => "M3 13h2v-2H3v2zm0 4h2v-2H3v2zm0-8h2V7H3v2zm4 4h14v-2H7v2zm0 4h14v-2H7v2zM7 7v2h14V7H7z",
                "DISABLE BACKGROUND CHECK" => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z",
                "DISABLE SEAMLESS SWAP" => "M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zM12 20c-4.42 0-8-3.58-8-8s3.58-8 8-8 8 3.58 8 8-3.58 8-8 8zm.5-13H11v6l5.25 3.15.75-1.23-4.5-2.67V7z",
                _ => ""
            };
            
            if (string.IsNullOrEmpty(pathData)) return null;
            return Avalonia.Media.StreamGeometry.Parse(pathData);
        }

        public void ApplyLanguage()
        {
            var F = new System.Func<string, Avalonia.Controls.TextBlock>(name => this.FindControl<Avalonia.Controls.TextBlock>(name));
            CrimsonX.Localization.AppStrings.Apply(F("lblQuickSettings"), CrimsonX.Localization.AppStrings.QuickSettings);
            CrimsonX.Localization.AppStrings.Apply(F("lblCustomize"), CrimsonX.Localization.AppStrings.Customize);
            CrimsonX.Localization.AppStrings.Apply(F("lblSave"), CrimsonX.Localization.AppStrings.IsPersian ? "ذخیره" : "SAVE");
            CrimsonX.Localization.AppStrings.Apply(F("lblCancel"), CrimsonX.Localization.AppStrings.IsPersian ? "لغو" : "CANCEL");
            
            _panPopupSlot1Items.Children.Clear();
            _panPopupSlot2Items.Children.Clear();
            PopulatePopupItems(_panPopupSlot1Items, 1);
            PopulatePopupItems(_panPopupSlot2Items, 2);
            
            CrimsonX.Localization.AppStrings.Apply(F("lblSlot1"), GetLocalizedSettingName(_pendingSlot1));
            CrimsonX.Localization.AppStrings.Apply(F("lblSlot2"), GetLocalizedSettingName(_pendingSlot2));
            
            _lblSlot1.Text = GetLocalizedSettingName(_pendingSlot1);
            _lblSlot2.Text = GetLocalizedSettingName(_pendingSlot2);
            
            _icoSlot1.Data = GetIconData(_pendingSlot1);
            _icoSlot2.Data = GetIconData(_pendingSlot2);
            global::Avalonia.Controls.ToolTip.SetTip(_lblSlot1, GetTooltipText(_pendingSlot1));
            global::Avalonia.Controls.ToolTip.SetTip(_lblSlot2, GetTooltipText(_pendingSlot2));
        }


        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            _btnCustomize = this.FindControl<Button>("btnCustomize")!;
            _panEditButtons = this.FindControl<StackPanel>("panEditButtons")!;
            
            _btnSlot1 = this.FindControl<Button>("btnSlot1")!;
            _btnSlot2 = this.FindControl<Button>("btnSlot2")!;
            
            _lblSlot1 = this.FindControl<TextBlock>("lblSlot1")!;
            _lblSlot2 = this.FindControl<TextBlock>("lblSlot2")!;
            
            _icoSlot1 = this.FindControl<PathIcon>("icoSlot1")!;
            _icoSlot2 = this.FindControl<PathIcon>("icoSlot2")!;
            
            _togSlot1 = this.FindControl<ToggleSwitch>("togSlot1")!;
            _togSlot2 = this.FindControl<ToggleSwitch>("togSlot2")!;
            
            _panSlot1Content = this.FindControl<Grid>("panSlot1Content")!;
            _panSlot2Content = this.FindControl<Grid>("panSlot2Content")!;
            
            _iconArrow1 = this.FindControl<PathIcon>("iconArrow1")!;
            _iconArrow2 = this.FindControl<PathIcon>("iconArrow2")!;

            _popupSlot1 = this.FindControl<Popup>("PopupSlot1")!;
            _popupSlot2 = this.FindControl<Popup>("PopupSlot2")!;
            _panPopupSlot1Items = this.FindControl<StackPanel>("panPopupSlot1Items")!;
            _panPopupSlot2Items = this.FindControl<StackPanel>("panPopupSlot2Items")!;

            PopulatePopupItems(_panPopupSlot1Items, 1);
            PopulatePopupItems(_panPopupSlot2Items, 2);

            this.AttachedToVisualTree += (s, e) => 
            {
                RefreshUI();
                var timer = new global::Avalonia.Threading.DispatcherTimer();
                timer.Interval = System.TimeSpan.FromSeconds(1);
                timer.Tick += (ts, te) => RefreshTogglesState();
                timer.Start();
            };
        }

        private void PopulatePopupItems(StackPanel container, int slotNumber)
        {
            var categories = new Dictionary<string, List<string>>
            {
                { "START-UP", new List<string> { "LAUNCH ON START-UP", "AUTO-CONNECT", "START MINIMIZED", "MINIMIZE TO TRAY" } },
                { "SPLIT TUNNELING", new List<string> { "DIRECT UDP", "EXCLUDE LOCATIONS", "AD BLOCKER" } },
                { "SYSTEM", new List<string> { "DISABLE BACKGROUND CHECK", "DISABLE SEAMLESS SWAP" } },
                { "CONNECTION", new List<string> { "XRAY EXIT-NODE", "BIND ADAPTER", "DOH", "SYSTEM DNS", "LAN CONNECTIONS", "CUSTOM CONFIGS" } }
            };

            foreach (var category in categories)
            {
                var headerText = CrimsonX.Localization.AppStrings.IsPersian ? GetPersianCategoryName(category.Key) : category.Key;
                
                var header = new TextBlock
                {
                    Text = headerText,
                    FontSize = 10,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    Foreground = Brush.Parse("#8B949E"),
                    Margin = new Thickness(8, container.Children.Count > 0 ? 12 : 4, 8, 4),
                    LetterSpacing = 1
                };
                container.Children.Add(header);

                foreach (var setting in category.Value)
                {
                    var btn = new Button
                    {
                        Classes = { "countryControl" },
                        Tag = setting,
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Padding = new Thickness(8, 6),
                        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                        CornerRadius = new CornerRadius(4)
                    };

                    var txt = new TextBlock
                    {
                        Text = GetLocalizedSettingName(setting),
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Brush.Parse("#E2E8F0"),
                        FontSize = 11,
                        FontWeight = Avalonia.Media.FontWeight.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };

                    var stp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                    var icon = new PathIcon { Data = GetIconData(setting), Width = 12, Height = 12, Foreground = Brush.Parse("#8B949E"), VerticalAlignment = VerticalAlignment.Center };
                    
                    stp.Children.Add(icon);
                    stp.Children.Add(txt);

                    btn.Content = stp;

                    btn.Click += (sender, e) => 
                    {
                        if (slotNumber == 1)
                        {
                            _pendingSlot1 = setting;
                            _lblSlot1.Text = GetLocalizedSettingName(setting);
                            _icoSlot1.Data = GetIconData(setting);
                            global::Avalonia.Controls.ToolTip.SetTip(_lblSlot1, GetTooltipText(setting));
                        }
                        else
                        {
                            _pendingSlot2 = setting;
                            _lblSlot2.Text = GetLocalizedSettingName(setting);
                            _icoSlot2.Data = GetIconData(setting);
                            global::Avalonia.Controls.ToolTip.SetTip(_lblSlot2, GetTooltipText(setting));
                        }
                        _ = ClosePopupAnimatedAsync();
                    };

                    container.Children.Add(btn);
                }
            }
        }

        private string GetPersianCategoryName(string key)
        {
            switch (key)
            {
                case "START-UP": return "اجرا";
                case "SPLIT TUNNELING": return "تونل‌بندی مجزا";
                case "SYSTEM": return "سیستم";
                case "CONNECTION": return "اتصال";
                default: return key;
            }
        }

        private void RefreshUI()
        {
            if (MainWindow.Instance?.Config == null) return;
            
            _pendingSlot1 = MainWindow.Instance.Config.QuickSetting1;
            _pendingSlot2 = MainWindow.Instance.Config.QuickSetting2;

            _lblSlot1.Text = GetLocalizedSettingName(_pendingSlot1);
            _lblSlot2.Text = GetLocalizedSettingName(_pendingSlot2);
            
            _icoSlot1.Data = GetIconData(_pendingSlot1);
            _icoSlot2.Data = GetIconData(_pendingSlot2);
            global::Avalonia.Controls.ToolTip.SetTip(_lblSlot1, GetTooltipText(_pendingSlot1));
            global::Avalonia.Controls.ToolTip.SetTip(_lblSlot2, GetTooltipText(_pendingSlot2));

            RefreshTogglesState();
        }

        private void RefreshTogglesState()
        {
            if (_isUpdating) return;
            if (MainWindow.Instance?.Config == null) return;

            _isUpdating = true;
            
            var orig1 = GetOriginalToggle(MainWindow.Instance.Config.QuickSetting1);
            if (orig1 != null) _togSlot1.IsChecked = orig1.IsChecked;

            var orig2 = GetOriginalToggle(MainWindow.Instance.Config.QuickSetting2);
            if (orig2 != null) _togSlot2.IsChecked = orig2.IsChecked;

            _isUpdating = false;
        }

        private ToggleSwitch? GetOriginalToggle(string name)
        {
            if (MainWindow.Instance == null) return null;
            if (Pages.SettingsPage.Instance == null) return null;

            var pnlSplit = MainWindow.Instance.FindControl<Panel>("viewSplitTunneling");
            Pages.SplitTunnelPage? splitTunnel = null;
            if (pnlSplit != null && pnlSplit.Children.Count > 0)
                splitTunnel = pnlSplit.Children[0] as Pages.SplitTunnelPage;

            switch (name)
            {
                case "DIRECT UDP": return splitTunnel?.FindControl<ToggleSwitch>("togDirectUDP");
                case "XRAY EXIT-NODE": return Pages.SettingsPage.Instance.FindControl<ToggleSwitch>("togXrayExitNode");
                case "BIND ADAPTER": return Pages.SettingsPage.Instance.FindControl<ToggleSwitch>("togAdapterBinding");
                case "DOH": return Pages.SettingsPage.Instance.FindControl<ToggleSwitch>("togDnsSettings");
                case "SYSTEM DNS": return Pages.SettingsPage.Instance.FindControl<ToggleSwitch>("togSysDns");
                case "AD BLOCKER": return Pages.SettingsPage.Instance.FindControl<ToggleSwitch>("btnAdBlockTog");
                case "LAN CONNECTIONS": return Pages.SettingsPage.Instance.FindControl<ToggleSwitch>("btnLanTog");
                case "LAUNCH ON START-UP": return Pages.SettingsPage.Instance.FindControl<ToggleSwitch>("btnBootTog");
                case "AUTO-CONNECT": return Pages.SettingsPage.Instance.FindControl<ToggleSwitch>("btnAutoTog");
                case "START MINIMIZED": return Pages.SettingsPage.Instance.FindControl<ToggleSwitch>("btnStartMinTog");
                case "MINIMIZE TO TRAY": return Pages.SettingsPage.Instance.FindControl<ToggleSwitch>("btnTrayTog");
                case "EXCLUDE LOCATIONS": return Pages.SettingsPage.Instance.FindControl<ToggleSwitch>("togExcludeLocations");
                case "CUSTOM CONFIGS": return Pages.SettingsPage.Instance.FindControl<ToggleSwitch>("togCustomConfigs");
                case "DISABLE BACKGROUND CHECK": return Pages.SettingsPage.Instance.FindControl<ToggleSwitch>("togDisableBgChecks");
                case "DISABLE SEAMLESS SWAP": return Pages.SettingsPage.Instance.FindControl<ToggleSwitch>("togDisableRefreshTimer");
            }
            return null;
        }

        private void SetEditMode(bool editMode)
        {
            _btnCustomize.Opacity = editMode ? 0 : 1;
            _btnCustomize.IsHitTestVisible = !editMode;

            _panEditButtons.Opacity = editMode ? 1 : 0;
            _panEditButtons.IsHitTestVisible = editMode;

            _panSlot1Content.Opacity = editMode ? 0.2 : 1.0;
            _panSlot2Content.Opacity = editMode ? 0.2 : 1.0;

            _iconArrow1.Opacity = editMode ? 1.0 : 0.0;
            _iconArrow2.Opacity = editMode ? 1.0 : 0.0;

            _btnSlot1.IsHitTestVisible = editMode;
            _btnSlot2.IsHitTestVisible = editMode;
        }

        private void btnCustomize_Click(object? sender, RoutedEventArgs e)
        {
            _pendingSlot1 = _lblSlot1.Text ?? "DIRECT UDP";
            _pendingSlot2 = _lblSlot2.Text ?? "AUTO-CONNECT";
            SetEditMode(true);
        }

        private void btnCancel_Click(object? sender, RoutedEventArgs e)
        {
            RefreshUI(); 
            SetEditMode(false);
            ClosePopups();
        }

        private void btnSave_Click(object? sender, RoutedEventArgs e)
        {
            if (MainWindow.Instance?.Config != null)
            {
                MainWindow.Instance.Config.QuickSetting1 = _pendingSlot1;
                MainWindow.Instance.Config.QuickSetting2 = _pendingSlot2;
                MainWindow.Instance.RequestConfigSave();
            }

            RefreshUI();
            SetEditMode(false);
            ClosePopups();
        }

        private async void btnSlot1_Click(object? sender, RoutedEventArgs e)
        {
            ClosePopups();
            _popupSlot1.PlacementTarget = _iconArrow1;
            _popupSlot1.Placement = PlacementMode.Center;
            _popupSlot1.HorizontalOffset = 0;
            _popupSlot1.VerticalOffset = 0;
            _popupSlot1.IsOpen = true;

            var ldo = MainWindow.Instance?.FindControl<Border>("LightDismissOverlay");
            if (ldo != null) ldo.IsVisible = true;

            await System.Threading.Tasks.Task.Delay(10);
            if (_popupSlot1.Child is Border popBorder) popBorder.Classes.Add("popupOpen");
        }

        private async void btnSlot2_Click(object? sender, RoutedEventArgs e)
        {
            ClosePopups();
            _popupSlot2.PlacementTarget = _iconArrow2;
            _popupSlot2.Placement = PlacementMode.Center;
            _popupSlot2.HorizontalOffset = 0;
            _popupSlot2.VerticalOffset = 0;
            _popupSlot2.IsOpen = true;

            var ldo = MainWindow.Instance?.FindControl<Border>("LightDismissOverlay");
            if (ldo != null) ldo.IsVisible = true;

            await System.Threading.Tasks.Task.Delay(10);
            if (_popupSlot2.Child is Border popBorder) popBorder.Classes.Add("popupOpen");
        }

        private async System.Threading.Tasks.Task ClosePopupAnimatedAsync()
        {
            if (_popupSlot1 != null && _popupSlot1.IsOpen)
            {
                if (_popupSlot1.Child is Border popBorder)
                {
                    popBorder.Classes.Remove("popupOpen");
                    await System.Threading.Tasks.Task.Delay(200);
                }
                _popupSlot1.IsOpen = false;
            }

            if (_popupSlot2 != null && _popupSlot2.IsOpen)
            {
                if (_popupSlot2.Child is Border popBorder)
                {
                    popBorder.Classes.Remove("popupOpen");
                    await System.Threading.Tasks.Task.Delay(200);
                }
                _popupSlot2.IsOpen = false;
            }

            var ldo = MainWindow.Instance?.FindControl<Border>("LightDismissOverlay");
            if (ldo != null) ldo.IsVisible = false;
        }

        public void ClosePopups()
        {
            _ = ClosePopupAnimatedAsync();
        }

        private void TogSlot1_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            if (_isUpdating) return;
            if (MainWindow.Instance?.Config == null) return;
            var orig = GetOriginalToggle(MainWindow.Instance.Config.QuickSetting1);
            if (orig != null && _togSlot1.IsChecked.HasValue)
                orig.IsChecked = _togSlot1.IsChecked.Value;
        }

        private void TogSlot2_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            if (_isUpdating) return;
            if (MainWindow.Instance?.Config == null) return;
            var orig = GetOriginalToggle(MainWindow.Instance.Config.QuickSetting2);
            if (orig != null && _togSlot2.IsChecked.HasValue)
                orig.IsChecked = _togSlot2.IsChecked.Value;
        }
    }
}

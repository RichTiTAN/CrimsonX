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
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia;
using CrimsonX.Models;
using CrimsonX.Localization;
using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Avalonia.Media.Imaging;

namespace CrimsonX.Pages
{
    public class AppItem
    {
        public string ExeName { get; set; } = "";
        public Avalonia.Media.Imaging.Bitmap? Icon { get; set; }
    }

    public partial class SplitTunnelPage : UserControl
    {
        public static SplitTunnelPage? Instance { get; private set; }

        public ObservableCollection<AppItem> AppItems { get; } = new();

        private AppConfig _cfg => MainWindow.Instance.Config;
        private AppState _state => MainWindow.Instance.State;

        private string _tempDomains = "";

        private string _tempBlock = "";

        private bool _isInitializingSettings = false;

        public SplitTunnelPage()
        {
            Instance = this;
            _isInitializingSettings = true;
            InitializeComponent();
            ApplyLanguage();
            var lstApps = this.FindControl<Avalonia.Controls.ItemsControl>("lstApps");
            if (lstApps != null) lstApps.ItemsSource = AppItems;
            _isInitializingSettings = false;
        }


        public void SyncUI()
        {
            _isInitializingSettings = true;
            try 
            {
                var togDirectUDP = this.FindControl<ToggleSwitch>("togDirectUDP");
                if (togDirectUDP != null) togDirectUDP.IsChecked = MainWindow.Instance.Config.EnableDirectUDP;
            }
            finally
            {
                _isInitializingSettings = false;
            }
        }

        public void ApplyLanguage()
        {
            TextBlock? F(string name) => this.FindControl<TextBlock>(name);
            Button? B(string name) => this.FindControl<Button>(name);
            
            bool fa = CrimsonX.Localization.AppStrings.IsPersian;

            CrimsonX.Localization.AppStrings.Apply(F("lblSplitTunnelingHeader"), CrimsonX.Localization.AppStrings.SplitTunneling, forceLtr: true);
            CrimsonX.Localization.AppStrings.Apply(F("lblDomainsAndIps"), CrimsonX.Localization.AppStrings.DomainsAndIps);
            CrimsonX.Localization.AppStrings.Apply(F("lblApplications"), CrimsonX.Localization.AppStrings.Applications);
            var lblSplitAppsWarning = this.FindControl<TextBlock>("lblSplitAppsWarning");
            if (lblSplitAppsWarning != null) lblSplitAppsWarning.Text = fa ? "هشدار: به حروف بزرگ و کوچک حساس است" : "Warning: Case sensitive";
            CrimsonX.Localization.AppStrings.Apply(F("lblBlockedDomainsIps"), CrimsonX.Localization.AppStrings.BlockedDomains);
            CrimsonX.Localization.AppStrings.Apply(F("lblDirectUdpHeader"), CrimsonX.Localization.AppStrings.SplitTunnelDirectUDP);
            CrimsonX.Localization.AppStrings.ApplyToolTip(F("lblDirectUdpHeader"), CrimsonX.Localization.AppStrings.SplitTunnelDirectUDPTooltip);
            CrimsonX.Localization.AppStrings.Apply(F("lblDirectUdpDesc"), CrimsonX.Localization.AppStrings.SplitTunnelDirectUDPDesc);
            
            var btnSplitDisabled = this.FindControl<Button>("btnSplitDisabled");
            var btnSplitExclusive = this.FindControl<Button>("btnSplitExclusive");
            var btnSplitInclusive = this.FindControl<Button>("btnSplitInclusive");
            
            CrimsonX.Localization.AppStrings.ApplyToolTip(btnSplitDisabled, CrimsonX.Localization.AppStrings.TtSplitDis);
            CrimsonX.Localization.AppStrings.ApplyToolTip(btnSplitExclusive, CrimsonX.Localization.AppStrings.TtSplitExc);
            CrimsonX.Localization.AppStrings.ApplyToolTip(btnSplitInclusive, CrimsonX.Localization.AppStrings.TtSplitInc);
            
            if (btnSplitDisabled?.Content is TextBlock tbDis) CrimsonX.Localization.AppStrings.Apply(tbDis, CrimsonX.Localization.AppStrings.Disabled);
            if (btnSplitExclusive?.Content is TextBlock tbEx) CrimsonX.Localization.AppStrings.Apply(tbEx, CrimsonX.Localization.AppStrings.Exclusive);
            if (btnSplitInclusive?.Content is TextBlock tbIn) CrimsonX.Localization.AppStrings.Apply(tbIn, CrimsonX.Localization.AppStrings.Inclusive);

            CrimsonX.Localization.AppStrings.ApplyBtn(this.FindControl<Button>("btnSaveDomains"), CrimsonX.Localization.AppStrings.Save);
            CrimsonX.Localization.AppStrings.ApplyBtn(this.FindControl<Button>("btnCancelDomains"), CrimsonX.Localization.AppStrings.Cancel);
            CrimsonX.Localization.AppStrings.ApplyBtn(this.FindControl<Button>("btnSaveBlock"), CrimsonX.Localization.AppStrings.Save);
            CrimsonX.Localization.AppStrings.ApplyBtn(this.FindControl<Button>("btnCancelBlock"), CrimsonX.Localization.AppStrings.Cancel);

            var lblSplitExplanation = this.FindControl<TextBlock>("lblSplitExplanation");
            if (lblSplitExplanation != null)
            {
                var modeStr = MainWindow.Instance?.Config?.SplitTunnelMode?.ToUpper() ?? "DISABLED";
                if (modeStr == "EXCLUSIVE")
                    lblSplitExplanation.Text = CrimsonX.Localization.AppStrings.SplitExplanationExclusive;
                else if (modeStr == "INCLUSIVE")
                    lblSplitExplanation.Text = CrimsonX.Localization.AppStrings.SplitExplanationInclusive;
                else
                    lblSplitExplanation.Text = "";
                    
                lblSplitExplanation.FlowDirection = fa 
                    ? Avalonia.Media.FlowDirection.RightToLeft 
                    : Avalonia.Media.FlowDirection.LeftToRight;
            }
            
            var btnAddApp = B("btnAddApp");
            if (btnAddApp != null) btnAddApp.Content = CrimsonX.Localization.AppStrings.Add;
            
            var btnSaveDomains = B("btnSaveDomains");
            if (btnSaveDomains != null) btnSaveDomains.Content = CrimsonX.Localization.AppStrings.Save;
            var btnCancelDomains = B("btnCancelDomains");
            if (btnCancelDomains != null) btnCancelDomains.Content = CrimsonX.Localization.AppStrings.Cancel;
            
            var btnSaveBlock = B("btnSaveBlock");
            if (btnSaveBlock != null) btnSaveBlock.Content = CrimsonX.Localization.AppStrings.Save;
            var btnCancelBlock = B("btnCancelBlock");
            if (btnCancelBlock != null) btnCancelBlock.Content = CrimsonX.Localization.AppStrings.Cancel;
            
            var txtSplitDomains = this.FindControl<global::Avalonia.Controls.TextBox>("txtSplitDomains");
            var btnToggleDomains = B("btnToggleDomains");
            if (txtSplitDomains != null && btnToggleDomains != null)
            {
                bool hasText = !string.IsNullOrWhiteSpace(txtSplitDomains.Text);
                btnToggleDomains.Content = hasText ? CrimsonX.Localization.AppStrings.Edit : CrimsonX.Localization.AppStrings.Add;
            }
            
            var txtSplitBlock = this.FindControl<global::Avalonia.Controls.TextBox>("txtSplitBlock");
            var btnToggleBlock = B("btnToggleBlock");
            if (txtSplitBlock != null && btnToggleBlock != null)
            {
                bool hasText = !string.IsNullOrWhiteSpace(txtSplitBlock.Text);
                btnToggleBlock.Content = hasText ? CrimsonX.Localization.AppStrings.Edit : CrimsonX.Localization.AppStrings.Add;
            }
        }

        protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty && IsVisible)
            {
                UpdateSplitTunnelUI();
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            
            AppStrings.Apply(this.FindControl<TextBlock>("lblSplitTunnelingHeader"), AppStrings.SplitTunneling, forceLtr: true);
            AppStrings.Apply(this.FindControl<TextBlock>("lblDirectUdpHeader"), AppStrings.SplitTunnelDirectUDP);
            AppStrings.ApplyToolTip(this.FindControl<TextBlock>("lblDirectUdpHeader"), AppStrings.SplitTunnelDirectUDPTooltip);
            AppStrings.Apply(this.FindControl<TextBlock>("lblDirectUdpDesc"), AppStrings.SplitTunnelDirectUDPDesc);
            
            var btnSplitDisabled = this.FindControl<Button>("btnSplitDisabled");
            var btnSplitExclusive = this.FindControl<Button>("btnSplitExclusive");
            var btnSplitInclusive = this.FindControl<Button>("btnSplitInclusive");
            
            AppStrings.ApplyToolTip(btnSplitDisabled, AppStrings.TtSplitDis);
            AppStrings.ApplyToolTip(btnSplitExclusive, AppStrings.TtSplitExc);
            AppStrings.ApplyToolTip(btnSplitInclusive, AppStrings.TtSplitInc);
            
            if (btnSplitDisabled?.Content is TextBlock tbDis) AppStrings.Apply(tbDis, AppStrings.Disabled);
            if (btnSplitExclusive?.Content is TextBlock tbEx) AppStrings.Apply(tbEx, AppStrings.Exclusive);
            if (btnSplitInclusive?.Content is TextBlock tbIn) AppStrings.Apply(tbIn, AppStrings.Inclusive);

            AppStrings.ApplyBtn(this.FindControl<Button>("btnSaveDomains"), AppStrings.Save);
            AppStrings.ApplyBtn(this.FindControl<Button>("btnCancelDomains"), AppStrings.Cancel);
            AppStrings.ApplyBtn(this.FindControl<Button>("btnSaveBlock"), AppStrings.Save);
            AppStrings.ApplyBtn(this.FindControl<Button>("btnCancelBlock"), AppStrings.Cancel);

            var lblSplitAppsWarning = this.FindControl<TextBlock>("lblSplitAppsWarning");
            if (lblSplitAppsWarning != null) lblSplitAppsWarning.Text = AppStrings.IsPersian ? "هشدار: به حروف بزرگ و کوچک حساس است" : "Warning: Case sensitive";
            
            var togDirectUDP = this.FindControl<ToggleSwitch>("togDirectUDP");

            UpdateSplitTunnelUI();
        }

        public void UpdateSplitTunnelUI()
        {
            this.FindControl<Button>("btnSplitDisabled")?.Classes.Remove("activeOpt");
            this.FindControl<Button>("btnSplitExclusive")?.Classes.Remove("activeOpt");
            this.FindControl<Button>("btnSplitInclusive")?.Classes.Remove("activeOpt");

            var modeStr = _cfg.SplitTunnelMode ?? "DISABLED";
            if (modeStr == "EXCLUSIVE") this.FindControl<Button>("btnSplitExclusive")?.Classes.Add("activeOpt");
            else if (modeStr == "INCLUSIVE") this.FindControl<Button>("btnSplitInclusive")?.Classes.Add("activeOpt");
            else this.FindControl<Button>("btnSplitDisabled")?.Classes.Add("activeOpt");

            var panSplitConfig = this.FindControl<Border>("panSplitConfig");
            if (panSplitConfig != null)
            {
                if (modeStr != "EXCLUSIVE" && modeStr != "INCLUSIVE")
                {
                    panSplitConfig.MaxHeight = 0;
                    panSplitConfig.Opacity = 0;
                }
                else
                {
                    panSplitConfig.MaxHeight = 800;
                    panSplitConfig.Opacity = 1;
                }
            }

            var lblSplitExplanation = this.FindControl<TextBlock>("lblSplitExplanation");
            if (lblSplitExplanation != null)
            {
                if (modeStr == "EXCLUSIVE")
                    lblSplitExplanation.Text = AppStrings.SplitExplanationExclusive;
                else if (modeStr == "INCLUSIVE")
                    lblSplitExplanation.Text = AppStrings.SplitExplanationInclusive;
                    
                lblSplitExplanation.FlowDirection = AppStrings.IsPersian 
                    ? Avalonia.Media.FlowDirection.RightToLeft 
                    : Avalonia.Media.FlowDirection.LeftToRight;
            }

            var panSplitDomains = this.FindControl<StackPanel>("panSplitDomains");
            var panSplitApps = this.FindControl<StackPanel>("panSplitApps");

            if (panSplitDomains != null && panSplitApps != null)
            {
                if (_cfg.LastXrayMode == "VPN Mode")
                {
                    panSplitDomains.IsEnabled = false;
                    panSplitDomains.Opacity = 0.3;
                    panSplitApps.IsEnabled = true;
                    panSplitApps.Opacity = 1.0;
                }
                else
                {
                    panSplitDomains.IsEnabled = true;
                    panSplitDomains.Opacity = 1.0;
                    panSplitApps.IsEnabled = false;
                    panSplitApps.Opacity = 0.3;
                }
            }
            
            var txtSplitDomains = this.FindControl<TextBox>("txtSplitDomains");
            if (txtSplitDomains != null)
            {
                if (txtSplitDomains.Text != _cfg.LastManualSplit) txtSplitDomains.Text = _cfg.LastManualSplit;
                InitPanel(this.FindControl<Border>("panDomainsEdit")!, this.FindControl<Border>("panDomainsToggle")!, txtSplitDomains, this.FindControl<Button>("btnToggleDomains")!);
            }
                
            AppItems.Clear();
            if (!string.IsNullOrWhiteSpace(_cfg.LastAppSplit))
            {
                var apps = _cfg.LastAppSplit.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(a => a.Trim()).Distinct();
                string iconsPath = MainWindow.Instance.GetAppPath(@"Data\cache\icons.bin");
                var iconCache = CrimsonX.Services.ConfigCache.LoadIconCache(iconsPath);
                
                foreach(var app in apps)
                {
                    Avalonia.Media.Imaging.Bitmap? bmp = null;
                    if (iconCache.TryGetValue(app, out string base64) && !string.IsNullOrEmpty(base64))
                    {
                        try
                        {
                            byte[] bytes = Convert.FromBase64String(base64);
                            using (var ms = new System.IO.MemoryStream(bytes))
                            {
                                bmp = new Avalonia.Media.Imaging.Bitmap(ms);
                            }
                        }
                        catch { }
                    }
                    AppItems.Add(new AppItem { ExeName = app, Icon = bmp });
                }
            }
                
            bool hasApps = AppItems.Count > 0;
            var panAppsGrid = this.FindControl<Border>("panAppsGrid");
            if (panAppsGrid != null) panAppsGrid.IsVisible = hasApps;
            
            var panAppsToggle = this.FindControl<Border>("panAppsToggle");
            if (panAppsToggle != null) panAppsToggle.CornerRadius = hasApps ? new Avalonia.CornerRadius(4, 4, 0, 0) : new Avalonia.CornerRadius(4);
            
            var btnAddApp = this.FindControl<Button>("btnAddApp");
            if (btnAddApp != null) btnAddApp.CornerRadius = hasApps ? new Avalonia.CornerRadius(0, 3, 0, 0) : new Avalonia.CornerRadius(0, 3, 3, 0);
            
            var txtSplitBlock = this.FindControl<TextBox>("txtSplitBlock");
            if (txtSplitBlock != null)
            {
                if (txtSplitBlock.Text != _cfg.LastBlockSplit) txtSplitBlock.Text = _cfg.LastBlockSplit;
                InitPanel(this.FindControl<Border>("panBlockEdit")!, this.FindControl<Border>("panBlockToggle")!, txtSplitBlock, this.FindControl<Button>("btnToggleBlock")!);
            }
        }

        private void SplitTunnel_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button clickedBtn)
            {
                string oldMode = _cfg.SplitTunnelMode ?? "DISABLED";
                
                if (clickedBtn.Name == "btnSplitExclusive") _cfg.SplitTunnelMode = "EXCLUSIVE";
                else if (clickedBtn.Name == "btnSplitInclusive") _cfg.SplitTunnelMode = "INCLUSIVE";
                else _cfg.SplitTunnelMode = "DISABLED";

                if (oldMode == _cfg.SplitTunnelMode) return;

                _cfg.EnableDirect = _cfg.SplitTunnelMode != "DISABLED";

                UpdateSplitTunnelUI();
                MainWindow.Instance.RequestSave();
                
                if (_state.IsEngineRunning)
                {
                    bool hasAnyInput = !string.IsNullOrWhiteSpace(_cfg.LastManualSplit) || 
                                       !string.IsNullOrWhiteSpace(_cfg.LastAppSplit) || 
                                       !string.IsNullOrWhiteSpace(_cfg.LastBlockSplit);

                    if (hasAnyInput)
                    {
                        MainWindow.Instance.RestartXray();
                    }
                }
            }
        }

        private void InitPanel(Border panel, Border togglePanel, TextBox tb, Button btnToggle)
        {
            bool hasText = !string.IsNullOrWhiteSpace(tb.Text);
            panel.Height = hasText ? 34 : 0;
            btnToggle.Content = hasText ? AppStrings.Edit : AppStrings.Add;
            togglePanel.CornerRadius = hasText ? new Avalonia.CornerRadius(4, 4, 0, 0) : new Avalonia.CornerRadius(4);
            
            if (hasText)
            {
                tb.Height = 17;
                tb.Margin = new Avalonia.Thickness(0);
                tb.IsHitTestVisible = false;
                tb.IsReadOnly = true;
                tb.Focusable = false;
                tb.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Arrow);
            }
            else
            {
                tb.Height = 46;
                tb.IsHitTestVisible = true;
                tb.IsReadOnly = false;
                tb.Focusable = true;
                tb.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Ibeam);
            }
        }

        private void TogglePanel(Border panel, Border togglePanel, TextBox tb, Button btnToggle, ref string tempStore)
        {
            if (panel.Height < 110)
            {
                tempStore = tb.Text ?? "";
                
                tb.Height = 56;
                tb.Margin = new Avalonia.Thickness(0, 5, 0, 0);
                tb.IsHitTestVisible = true;
                tb.IsReadOnly = false;
                tb.Focusable = true;
                tb.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Ibeam);
                btnToggle.Content = AppStrings.Edit;
                togglePanel.CornerRadius = new Avalonia.CornerRadius(4, 4, 0, 0);
                
                panel.Height = 110;
                tb.Focus();
            }
            else
            {
                ClosePanel(panel, togglePanel, tb, btnToggle);
            }
        }

        private void ClosePanel(Border panel, Border togglePanel, TextBox tb, Button btnToggle)
        {
            bool hasText = !string.IsNullOrWhiteSpace(tb.Text);
            
            btnToggle.Content = hasText ? AppStrings.Edit : AppStrings.Add;
            togglePanel.CornerRadius = hasText ? new Avalonia.CornerRadius(4, 4, 0, 0) : new Avalonia.CornerRadius(4);
            
            if (hasText)
            {
                tb.Height = 17;
                tb.Margin = new Avalonia.Thickness(0);
                tb.IsHitTestVisible = false;
                tb.IsReadOnly = true;
                tb.Focusable = false;
                tb.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Arrow);
            }
            
            panel.Height = hasText ? 34 : 0;
        }

        private void SplitToggle_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                if (btn.Name == "btnToggleDomains") TogglePanel(this.FindControl<Border>("panDomainsEdit")!, this.FindControl<Border>("panDomainsToggle")!, this.FindControl<TextBox>("txtSplitDomains")!, btn, ref _tempDomains);
                
                else if (btn.Name == "btnToggleBlock") TogglePanel(this.FindControl<Border>("panBlockEdit")!, this.FindControl<Border>("panBlockToggle")!, this.FindControl<TextBox>("txtSplitBlock")!, btn, ref _tempBlock);
            }
        }

        private void SplitSave_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                bool changed = false;
                if (btn.Name == "btnSaveDomains")
                {
                    var tb = this.FindControl<TextBox>("txtSplitDomains")!;
                    string newVal = tb.Text?.Trim() ?? "";
                    if (_cfg.LastManualSplit != newVal)
                    {
                        _cfg.LastManualSplit = newVal;
                        changed = true;
                    }
                    ClosePanel(this.FindControl<Border>("panDomainsEdit")!, this.FindControl<Border>("panDomainsToggle")!, tb, this.FindControl<Button>("btnToggleDomains")!);
                }
                
                else if (btn.Name == "btnSaveBlock")
                {
                    var tb = this.FindControl<TextBox>("txtSplitBlock")!;
                    string newVal = tb.Text?.Trim() ?? "";
                    if (_cfg.LastBlockSplit != newVal)
                    {
                        _cfg.LastBlockSplit = newVal;
                        changed = true;
                    }
                    ClosePanel(this.FindControl<Border>("panBlockEdit")!, this.FindControl<Border>("panBlockToggle")!, tb, this.FindControl<Button>("btnToggleBlock")!);
                }
                
                if (changed)
                {
                    MainWindow.Instance.RequestSave();
                    if (_state.IsEngineRunning)
                        MainWindow.Instance.RestartXray();
                }
            }
        }

        private void SplitCancel_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                if (btn.Name == "btnCancelDomains")
                {
                    var tb = this.FindControl<TextBox>("txtSplitDomains")!;
                    tb.Text = _tempDomains;
                    ClosePanel(this.FindControl<Border>("panDomainsEdit")!, this.FindControl<Border>("panDomainsToggle")!, tb, this.FindControl<Button>("btnToggleDomains")!);
                }
                
                else if (btn.Name == "btnCancelBlock")
                {
                    var tb = this.FindControl<TextBox>("txtSplitBlock")!;
                    tb.Text = _tempBlock;
                    ClosePanel(this.FindControl<Border>("panBlockEdit")!, this.FindControl<Border>("panBlockToggle")!, tb, this.FindControl<Button>("btnToggleBlock")!);
                }
            }
        }

        
        private void RemoveApp_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppItem item)
            {
                var currentApps = string.IsNullOrWhiteSpace(_cfg.LastAppSplit) ? new System.Collections.Generic.List<string>() : _cfg.LastAppSplit.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(a => a.Trim()).ToList();
                currentApps.RemoveAll(a => a.Equals(item.ExeName, StringComparison.OrdinalIgnoreCase));
                _cfg.LastAppSplit = string.Join(", ", currentApps);
                
                try
                {
                    string iconsPath = MainWindow.Instance.GetAppPath(@"Data\cache\icons.bin");
                    var iconCache = CrimsonX.Services.ConfigCache.LoadIconCache(iconsPath);
                    if (iconCache.Remove(item.ExeName))
                    {
                        CrimsonX.Services.ConfigCache.SaveIconCache(iconsPath, iconCache);
                    }
                }
                catch { }

                MainWindow.Instance.RequestSave();
                if (_state.IsEngineRunning)
                    MainWindow.Instance.RestartXray();
                
                UpdateSplitTunnelUI();
            }
        }

        private async void BrowseApp_Click(object? sender, RoutedEventArgs e)
        {
            var mainWindow = MainWindow.Instance;
            if (mainWindow == null) return;
            
            var storageProvider = mainWindow.StorageProvider;
            var fileOptions = new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select Application",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Executables") { Patterns = new[] { "*.exe" } },
                    new Avalonia.Platform.Storage.FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
                }
            };

            var result = await storageProvider.OpenFilePickerAsync(fileOptions);
            if (result != null && result.Count > 0)
            {
                var file = result[0];
                var exeName = file.Name;
                var localPath = file.Path.LocalPath;
                
                if (!string.IsNullOrWhiteSpace(localPath))
                {
                    try
                    {
                        using (var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(localPath))
                        {
                            if (sysIcon != null)
                            {
                                using (var bmp = sysIcon.ToBitmap())
                                {
                                    using (var ms = new System.IO.MemoryStream())
                                    {
                                        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                                        string base64 = Convert.ToBase64String(ms.ToArray());
                                        string iconsPath = MainWindow.Instance.GetAppPath(@"Data\cache\icons.bin");
                                        var iconCache = CrimsonX.Services.ConfigCache.LoadIconCache(iconsPath);
                                        iconCache[exeName] = base64;
                                        CrimsonX.Services.ConfigCache.SaveIconCache(iconsPath, iconCache);
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
                
                var currentApps = string.IsNullOrWhiteSpace(_cfg.LastAppSplit) ? new System.Collections.Generic.List<string>() : _cfg.LastAppSplit.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(a => a.Trim()).ToList();
                if (!currentApps.Contains(exeName, StringComparer.OrdinalIgnoreCase))
                {
                    currentApps.Add(exeName);
                    _cfg.LastAppSplit = string.Join(", ", currentApps);
                    MainWindow.Instance.RequestSave();
                    if (_state.IsEngineRunning)
                        MainWindow.Instance.RestartXray();
                    
                    UpdateSplitTunnelUI();
                }
            }
        }

        private void togDirectUDP_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            if (_isInitializingSettings) return;
            if (sender is ToggleSwitch tog)
            {
                bool val = tog.IsChecked ?? false;
                if (_cfg.EnableDirectUDP != val)
                {
                    _cfg.EnableDirectUDP = val;
                    MainWindow.Instance.SaveConfig();
                    if (_state.IsEngineRunning)
                        MainWindow.Instance.RestartXray();
                }
            }
        }

        private void txtSplit_TextChanged(object? sender, TextChangedEventArgs e)
        {
        }

        private bool _isDirectUdpExpanded = false;

        private void btnDirectUdpToggle_Click(object? sender, RoutedEventArgs e)
        {
            if (e != null)
            {
                var src = e.Source as global::Avalonia.Controls.Control;
                while (src != null)
                {
                    if (src.Name == "togDirectUDP") return;
                    src = src.Parent as global::Avalonia.Controls.Control;
                }
            }

            _isDirectUdpExpanded = !_isDirectUdpExpanded;
            var pan = this.FindControl<Border>("panDirectUdpExpanded");
            var ico = this.FindControl<global::Avalonia.Controls.PathIcon>("icoDirectUdpExpander");
            
            var panToggle = this.FindControl<Border>("panDirectUdpToggle");
            var btnToggle = this.FindControl<Button>("btnDirectUdpToggle");

            if (panToggle != null) panToggle.CornerRadius = _isDirectUdpExpanded ? new Avalonia.CornerRadius(8, 8, 0, 0) : new Avalonia.CornerRadius(8);
            if (btnToggle != null) btnToggle.CornerRadius = _isDirectUdpExpanded ? new Avalonia.CornerRadius(8, 8, 0, 0) : new Avalonia.CornerRadius(8);
            
            if (pan != null)
            {
                if (_isDirectUdpExpanded)
                {
                    pan.MaxHeight = 200;
                    pan.Opacity = 1;
                    if (ico != null) ico.RenderTransform = new global::Avalonia.Media.RotateTransform(180);
                    
                    var cmb = this.FindControl<ComboBox>("cmbUdpAdapters");
                    if (cmb != null && cmb.Items.Count == 0)
                    {
                        btnScanUdpAdapters_Click(null, null);
                    }
                }
                else
                {
                    pan.MaxHeight = 0;
                    pan.Opacity = 0;
                    if (ico != null) ico.RenderTransform = new global::Avalonia.Media.RotateTransform(0);
                }
            }
        }

        private bool _isScanningUdpAdapters = false;

        private void btnScanUdpAdapters_Click(object? sender, RoutedEventArgs e)
        {
            var cmb = this.FindControl<ComboBox>("cmbUdpAdapters");
            if (cmb == null) return;
            
            _isScanningUdpAdapters = true;
            try {
                cmb.Items.Clear();
                cmb.Items.Add("Default");
            var adapters = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            foreach (var adapter in adapters)
            {
                if (adapter.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && 
                    adapter.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                {
                    var properties = adapter.GetIPProperties();
                    var ipv4 = properties.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    if (ipv4 != null && !string.IsNullOrWhiteSpace(ipv4.Address.ToString()))
                    {
                        cmb.Items.Add($"{adapter.Name} - {ipv4.Address}");
                    }
                }
            }
            
            if (!string.IsNullOrWhiteSpace(_cfg.DirectUdpAdapterName) && !string.IsNullOrWhiteSpace(_cfg.DirectUdpAdapterIp))
            {
                string target = $"{_cfg.DirectUdpAdapterName} - {_cfg.DirectUdpAdapterIp}";
                if (cmb.Items.Contains(target))
                {
                    cmb.SelectedItem = target;
                }
                else
                {
                    MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastAdapterNoLongerAvail);
                    _cfg.DirectUdpAdapterName = "";
                    _cfg.DirectUdpAdapterIp = "";
                    MainWindow.Instance.SaveConfig();
                    cmb.SelectedIndex = 0;
                }
            }
            else
            {
                cmb.SelectedIndex = 0;
            }
            } finally {
                _isScanningUdpAdapters = false;
            }
        }

        private void cmbUdpAdapters_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isScanningUdpAdapters) return;
            var cmb = sender as ComboBox;
            if (cmb != null && cmb.SelectedItem is string selectedText)
            {
                if (selectedText == "Default")
                {
                    if (!string.IsNullOrWhiteSpace(_cfg.DirectUdpAdapterName) || !string.IsNullOrWhiteSpace(_cfg.DirectUdpAdapterIp))
                    {
                        _cfg.DirectUdpAdapterName = "";
                        _cfg.DirectUdpAdapterIp = "";
                        MainWindow.Instance.SaveConfig();
                        if (_state.IsEngineRunning)
                            MainWindow.Instance.RestartXray();
                    }
                }
                else
                {
                    var parts = selectedText.Split(new[] { " - " }, StringSplitOptions.None);
                    if (parts.Length >= 2)
                    {
                        var newIp   = parts[parts.Length - 1];
                        var newName = string.Join(" - ", parts, 0, parts.Length - 1);

                        bool changed = newIp != _cfg.DirectUdpAdapterIp || newName != _cfg.DirectUdpAdapterName;
                        if (changed)
                        {
                            _cfg.DirectUdpAdapterName = newName;
                            _cfg.DirectUdpAdapterIp = newIp;
                            MainWindow.Instance.RequestSave();
                            if (_state.IsEngineRunning)
                                MainWindow.Instance.RestartXray();
                        }
                    }
                }
            }
        }
    }
}



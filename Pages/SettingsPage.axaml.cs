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
using Avalonia.Threading;
using Avalonia.Input;
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls.Primitives;

namespace CrimsonX.Pages
{

    public class ExcludedContinentItem 
    { 
        public string Key { get; set; } = ""; 
        public string Display { get; set; } = ""; 
    }

    public partial class SettingsPage : UserControl
    {
        private bool _isInitializingSettings = false;
        public static SettingsPage Instance { get; private set; }
        

        public SettingsPage()
        {
            InitializeComponent();
            Instance = this;
        }

        public void SyncUI()
    {
        _isInitializingSettings = true;
        try 
        {
            var policy = MainWindow.Instance.Config.XrayBalancePolicy;
            string displayName = policy switch
            {
                "leastload"  => "LEAST LOAD",
                "roundrobin" => "ROUND ROBIN",
                "leastping"  => "LEAST PING",
                "random"     => "RANDOM",
                _            => policy?.ToUpperInvariant() ?? "LEAST PING"
            };
            var lbl = this.FindControl<global::Avalonia.Controls.TextBlock>("lblCurrentLbPolicy");
            if (lbl != null) lbl.Text = displayName;

            var _cfg = MainWindow.Instance.Config;
            
            var togExcludeLocations = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togExcludeLocations");
            if (togExcludeLocations != null) togExcludeLocations.IsChecked = _cfg.EnableExcludedContinents;
            
            var wrapExcludeLocations = this.FindControl<global::Avalonia.Controls.WrapPanel>("wrapExcludeLocations");
            if (wrapExcludeLocations != null && _cfg.ExcludedContinents != null)
            {
                foreach (var child in wrapExcludeLocations.Children)
                {
                    if (child is global::Avalonia.Controls.Primitives.ToggleButton tb && tb.Tag is string tag)
                    {
                        string fullContinentName = tag switch {
                            "AS" => "Asia",
                            "EU" => "Europe",
                            "NA" => "North America",
                            "SA" => "South America",
                            "AF" => "Africa",
                            "OC" => "Oceania",
                            "AN" => "Antarctica",
                            _ => tag
                        };
                        tb.IsChecked = _cfg.ExcludedContinents.Contains(fullContinentName);
                    }
                }
            }
            
            var lblExcludeLocations = this.FindControl<global::Avalonia.Controls.TextBlock>("lblExcludeLocations");
            if (lblExcludeLocations != null) 
            {
                lblExcludeLocations.Text = CrimsonX.Localization.AppStrings.ExcludeLocationsTitle;
                global::Avalonia.Controls.ToolTip.SetTip(lblExcludeLocations, CrimsonX.Localization.AppStrings.ExcludeLocationsTooltip);
            }
            
            var cmbExcludeContinents = this.FindControl<global::Avalonia.Controls.ComboBox>("cmbExcludeContinents");
            if (cmbExcludeContinents != null)
            {
                cmbExcludeContinents.PlaceholderText = CrimsonX.Localization.AppStrings.ExcludeContinentSelect.Replace("...", "").ToUpperInvariant();
                cmbExcludeContinents.SelectedIndex = -1;
                
                if (cmbExcludeContinents.Items is global::Avalonia.Controls.ItemCollection items && items.Count >= 7)
                {
                    if (items[0] is global::Avalonia.Controls.ComboBoxItem i0) i0.Content = CrimsonX.Localization.AppStrings.ExcludeContinentAsia.ToUpperInvariant();
                    if (items[1] is global::Avalonia.Controls.ComboBoxItem i1) i1.Content = CrimsonX.Localization.AppStrings.ExcludeContinentEurope.ToUpperInvariant();
                    if (items[2] is global::Avalonia.Controls.ComboBoxItem i2) i2.Content = CrimsonX.Localization.AppStrings.ExcludeContinentNorthAmerica.ToUpperInvariant();
                    if (items[3] is global::Avalonia.Controls.ComboBoxItem i3) i3.Content = CrimsonX.Localization.AppStrings.ExcludeContinentSouthAmerica.ToUpperInvariant();
                    if (items[4] is global::Avalonia.Controls.ComboBoxItem i4) i4.Content = CrimsonX.Localization.AppStrings.ExcludeContinentAfrica.ToUpperInvariant();
                    if (items[5] is global::Avalonia.Controls.ComboBoxItem i5) i5.Content = CrimsonX.Localization.AppStrings.ExcludeContinentOceania.ToUpperInvariant();
                    if (items[6] is global::Avalonia.Controls.ComboBoxItem i6) i6.Content = CrimsonX.Localization.AppStrings.ExcludeContinentAntarctica.ToUpperInvariant();
                }
            }
            
            var panExcludedContinents = this.FindControl<global::Avalonia.Controls.Border>("panExcludedContinents");
            if (panExcludedContinents != null)
            {
                bool hasItems = _cfg.ExcludedContinents != null && _cfg.ExcludedContinents.Count > 0;
                panExcludedContinents.MaxHeight = hasItems ? 45 : 0;
                panExcludedContinents.Opacity = hasItems ? 1 : 0;
            }

            
            var btnBootTog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("btnBootTog");
            if (btnBootTog != null) btnBootTog.IsChecked = _cfg.LaunchOnBoot;
            
            var btnAutoTog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("btnAutoTog");
            if (btnAutoTog != null) btnAutoTog.IsChecked = _cfg.AutoStart;
            
            var btnStartMinTog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("btnStartMinTog");
            if (btnStartMinTog != null) btnStartMinTog.IsChecked = _cfg.StartMinimized;
            
            var btnTrayTog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("btnTrayTog");
            if (btnTrayTog != null) btnTrayTog.IsChecked = _cfg.MinimizeToTray;
            
            var togDnsSettings = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togDnsSettings");
            if (togDnsSettings != null) togDnsSettings.IsChecked = _cfg.EnableUpstreamDoh;
            
            var cmbDohUrl = this.FindControl<global::Avalonia.Controls.ComboBox>("cmbDohUrl");
            if (cmbDohUrl != null) cmbDohUrl.Text = _cfg.UpstreamDohUrl;

            var togSysDns = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togSysDns");
            if (togSysDns != null) togSysDns.IsChecked = _cfg.EnableSystemDns;

            var txtSysDnsPrimary = this.FindControl<global::Avalonia.Controls.TextBox>("txtSysDnsPrimary");
            if (txtSysDnsPrimary != null) txtSysDnsPrimary.Text = _cfg.SystemDnsPrimary;

            var txtSysDnsSecondary = this.FindControl<global::Avalonia.Controls.TextBox>("txtSysDnsSecondary");
            if (txtSysDnsSecondary != null) txtSysDnsSecondary.Text = _cfg.SystemDnsSecondary;
            
            var btnAdBlockTog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("btnAdBlockTog");
            if (btnAdBlockTog != null) btnAdBlockTog.IsChecked = _cfg.EnableAdBlock;
            
            var btnLanTog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("btnLanTog");
            if (btnLanTog != null) btnLanTog.IsChecked = _cfg.AllowLanConnections;

            var togLanAuth = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togLanAuth");
            if (togLanAuth != null) togLanAuth.IsChecked = _cfg.EnableLanAuth;
            
            var btnDebugTog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("btnDebugTog");
            if (btnDebugTog != null) btnDebugTog.IsChecked = _cfg.DebugMode;

            var togDisableBgChecks = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togDisableBgChecks");
            if (togDisableBgChecks != null) togDisableBgChecks.IsChecked = _cfg.DisableBackgroundChecks;

            var togDisableRefreshTimer = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togDisableRefreshTimer");
            if (togDisableRefreshTimer != null) togDisableRefreshTimer.IsChecked = _cfg.DisableRefreshTimer;

            var togCustomConfigs = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togCustomConfigs");
            if (togCustomConfigs != null) togCustomConfigs.IsChecked = _cfg.EnableCustomConfigs;
            
            var txtCustomConfig1 = this.FindControl<global::Avalonia.Controls.TextBox>("txtCustomConfig1");
            if (txtCustomConfig1 != null) txtCustomConfig1.Text = _cfg.CustomConfig1;
            
            var txtCustomConfig2 = this.FindControl<global::Avalonia.Controls.TextBox>("txtCustomConfig2");
            if (txtCustomConfig2 != null) txtCustomConfig2.Text = _cfg.CustomConfig2;
            
            var chkAllowOneCustomConfig = this.FindControl<global::Avalonia.Controls.CheckBox>("chkAllowOneCustomConfig");
            if (chkAllowOneCustomConfig != null) chkAllowOneCustomConfig.IsChecked = _cfg.AllowOneCustomConfig;

            var togXrayExitNode = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togXrayExitNode");
            if (togXrayExitNode != null) togXrayExitNode.IsChecked = _cfg.EnableV2rayChain;

            var togDirectUDP = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togDirectUDP");
            if (togDirectUDP != null) togDirectUDP.IsChecked = _cfg.EnableDirectUDP;

            var togAdapterBinding = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togAdapterBinding");
            if (togAdapterBinding != null) togAdapterBinding.IsChecked = _cfg.EnableAdapterBinding;
        }
        finally
        {
            _isInitializingSettings = false;
        }
    }
        
        public void ApplyLanguage()
        {
            TextBlock? F(string name) => this.FindControl<TextBlock>(name);
            Button? B(string name)    => this.FindControl<Button>(name);
            bool fa = CrimsonX.Localization.AppStrings.IsPersian;

            var lblLanguage = F("lblCurrentLanguage");
            if (lblLanguage != null) lblLanguage.Text = fa ? "فارسی" : "ENGLISH";

            CrimsonX.Localization.AppStrings.Apply(F("lblSectionStartup"), CrimsonX.Localization.AppStrings.SectionStartup);
            CrimsonX.Localization.AppStrings.Apply(F("lblLaunchOnStartup"),  CrimsonX.Localization.AppStrings.LaunchOnStartup);
            CrimsonX.Localization.AppStrings.Apply(F("lblAutoConnect"), CrimsonX.Localization.AppStrings.AutoConnect);
            CrimsonX.Localization.AppStrings.Apply(F("lblStartMinimized"), CrimsonX.Localization.AppStrings.StartMinimized);
            CrimsonX.Localization.AppStrings.Apply(F("lblMinimizeToTray"), CrimsonX.Localization.AppStrings.MinimizeToTray);

            CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<TextBlock>("lblLaunchOnStartup"), CrimsonX.Localization.AppStrings.TtLaunchOnStartup);
            CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<TextBlock>("lblAutoConnect"), CrimsonX.Localization.AppStrings.TtAutoConnect);
            CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<TextBlock>("lblStartMinimized"), CrimsonX.Localization.AppStrings.TtStartMinimized);
            CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<TextBlock>("lblMinimizeToTray"), CrimsonX.Localization.AppStrings.TtMinimizeToTray);
            CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<Button>("btnRefreshPing"), CrimsonX.Localization.AppStrings.TtPingRefresh);

            CrimsonX.Localization.AppStrings.Apply(F("lblCustomConfigsTitle"), CrimsonX.Localization.AppStrings.CustomConfigsTitle);
            var chkAllow = this.FindControl<global::Avalonia.Controls.CheckBox>("chkAllowOneCustomConfig");
            if (chkAllow != null)
            {
                chkAllow.Content = CrimsonX.Localization.AppStrings.AllowOneCustomConfig;
                chkAllow.FontFamily = fa ? new global::Avalonia.Media.FontFamily("Segoe UI") : global::Avalonia.Media.FontFamily.Default;
                chkAllow.FlowDirection = fa ? global::Avalonia.Media.FlowDirection.RightToLeft : global::Avalonia.Media.FlowDirection.LeftToRight;
            }
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnCustomConfigsPing"), CrimsonX.Localization.AppStrings.PingBtn);
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnCustomConfigsClear"), CrimsonX.Localization.AppStrings.Clear);
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnCustomConfigsSave"), CrimsonX.Localization.AppStrings.Save);
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnXraySave"), CrimsonX.Localization.AppStrings.Save);
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnXrayCancel"), CrimsonX.Localization.AppStrings.Cancel);
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnDohSave"), CrimsonX.Localization.AppStrings.Save);
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnSysDnsSave"), CrimsonX.Localization.AppStrings.Save);
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnLanAuthSave"), CrimsonX.Localization.AppStrings.Save);
            
            CrimsonX.Localization.AppStrings.Apply(F("lblSectionConnection"), CrimsonX.Localization.AppStrings.SectionConnection);
            
            var tbCustomXray = this.FindControl<TextBlock>("lblCustomXrayExit");
            CrimsonX.Localization.AppStrings.Apply(tbCustomXray, CrimsonX.Localization.AppStrings.CustomXrayExit);
            CrimsonX.Localization.AppStrings.ApplyToolTip(tbCustomXray, CrimsonX.Localization.AppStrings.TtCustomXray);

            var tbAdapterBinding = this.FindControl<TextBlock>("lblAdapterBindingTitle");
            CrimsonX.Localization.AppStrings.Apply(tbAdapterBinding, CrimsonX.Localization.AppStrings.AdapterBinding);
            CrimsonX.Localization.AppStrings.ApplyToolTip(tbAdapterBinding, CrimsonX.Localization.AppStrings.TtAdapterBinding);
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnScanAdapters"), CrimsonX.Localization.AppStrings.ScanAdapters);
            
            var tbDnsSetting = this.FindControl<TextBlock>("lblDnsSettingTitle");
            CrimsonX.Localization.AppStrings.Apply(tbDnsSetting, CrimsonX.Localization.AppStrings.DnsSettings);
            CrimsonX.Localization.AppStrings.ApplyToolTip(tbDnsSetting, CrimsonX.Localization.AppStrings.TtDnsSettings);

            var tbAdBlocker = this.FindControl<TextBlock>("lblAdBlockerSetting");
            CrimsonX.Localization.AppStrings.Apply(tbAdBlocker, CrimsonX.Localization.AppStrings.AdBlocker);
            CrimsonX.Localization.AppStrings.ApplyToolTip(tbAdBlocker, CrimsonX.Localization.AppStrings.TtAdBlocker);
            
            var tbAllowLan = this.FindControl<TextBlock>("lblAllowLanSetting");
            CrimsonX.Localization.AppStrings.Apply(tbAllowLan, CrimsonX.Localization.AppStrings.AllowLan);
            CrimsonX.Localization.AppStrings.ApplyToolTip(tbAllowLan, CrimsonX.Localization.AppStrings.TtAllowLan);
            CrimsonX.Localization.AppStrings.Apply(this.FindControl<TextBlock>("lblLanAuthTitle"), CrimsonX.Localization.AppStrings.LanAuth);
            CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<global::Avalonia.Controls.TextBlock>("lblLanAuthTitle"), CrimsonX.Localization.AppStrings.TtLanAuth);

            CrimsonX.Localization.AppStrings.Apply(F("lblOutboundType"), CrimsonX.Localization.AppStrings.ProxyType);
            CrimsonX.Localization.AppStrings.Apply(F("lblOutboundAddress"), CrimsonX.Localization.AppStrings.AddressIp);
            CrimsonX.Localization.AppStrings.Apply(F("lblOutboundPort"), CrimsonX.Localization.AppStrings.Port);
            CrimsonX.Localization.AppStrings.Apply(F("lblOutboundAuth"), CrimsonX.Localization.AppStrings.Authentication);
            CrimsonX.Localization.AppStrings.Apply(F("lblOutboundUsername"), CrimsonX.Localization.AppStrings.Username);
            CrimsonX.Localization.AppStrings.Apply(F("lblOutboundPassword"), CrimsonX.Localization.AppStrings.Password);
            CrimsonX.Localization.AppStrings.Apply(F("lblUpstreamDoh"), CrimsonX.Localization.AppStrings.UpstreamDohUrl);
            CrimsonX.Localization.AppStrings.Apply(F("lblSysDnsTitle"), CrimsonX.Localization.AppStrings.SystemDns);
            CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<global::Avalonia.Controls.TextBlock>("lblSysDnsTitle"), CrimsonX.Localization.AppStrings.TtSystemDns);

            CrimsonX.Localization.AppStrings.Apply(F("lblDisableBgChecks"), CrimsonX.Localization.AppStrings.DisableBackgroundChecks);
            CrimsonX.Localization.AppStrings.ApplyToolTip(F("lblDisableBgChecks"), CrimsonX.Localization.AppStrings.TtDisableBackgroundChecks);
            CrimsonX.Localization.AppStrings.Apply(F("lblDisableRefreshTimer"), CrimsonX.Localization.AppStrings.DisableRefreshTimer);
            CrimsonX.Localization.AppStrings.ApplyToolTip(F("lblDisableRefreshTimer"), CrimsonX.Localization.AppStrings.TtDisableRefreshTimer);
            CrimsonX.Localization.AppStrings.Apply(F("lblSectionSystem"), CrimsonX.Localization.AppStrings.SectionSystem);
            
            CrimsonX.Localization.AppStrings.ApplyToolTip(F("lblCustomConfigsTitle"), CrimsonX.Localization.AppStrings.TtCustomConfigs);
            
            CrimsonX.Localization.AppStrings.Apply(F("lblLbPolicy"), CrimsonX.Localization.AppStrings.LbPolicy);
            CrimsonX.Localization.AppStrings.ApplyToolTip(F("lblLbPolicy"), CrimsonX.Localization.AppStrings.TtLbPolicy);
            CrimsonX.Localization.AppStrings.ApplyToolTip(B("btnLbLeastLoad"), CrimsonX.Localization.AppStrings.TtLbLeastLoad);
            CrimsonX.Localization.AppStrings.ApplyToolTip(B("btnLbRoundRobin"), CrimsonX.Localization.AppStrings.TtLbRoundRobin);
            CrimsonX.Localization.AppStrings.ApplyToolTip(B("btnLbLeastPing"), CrimsonX.Localization.AppStrings.TtLbLeastPing);
            CrimsonX.Localization.AppStrings.ApplyToolTip(B("btnLbRandom"), CrimsonX.Localization.AppStrings.TtLbRandom);

            var tbLanguageSetting = this.FindControl<TextBlock>("lblLanguageSetting");
            CrimsonX.Localization.AppStrings.Apply(tbLanguageSetting, CrimsonX.Localization.AppStrings.LanguageSetting);
            CrimsonX.Localization.AppStrings.ApplyToolTip(tbLanguageSetting, CrimsonX.Localization.AppStrings.TtLanguage);
            
            var tbDebugMode = this.FindControl<TextBlock>("lblDebugMode");
            CrimsonX.Localization.AppStrings.Apply(tbDebugMode, CrimsonX.Localization.AppStrings.DebugMode);
            CrimsonX.Localization.AppStrings.ApplyToolTip(tbDebugMode, CrimsonX.Localization.AppStrings.TtDebugMode);
            
            CrimsonX.Localization.AppStrings.Apply(F("lblDesktopShortcut"),  CrimsonX.Localization.AppStrings.DesktopShortcut);
            CrimsonX.Localization.AppStrings.Apply(F("lblStartMenuShortcut"), CrimsonX.Localization.AppStrings.StartMenuShortcut);

            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnDesktopShortcut"), CrimsonX.Localization.AppStrings.Create);
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnStartMenuShortcut"), CrimsonX.Localization.AppStrings.Create);
            
            SyncUI();
        }

    private void SetCustomConfigsExpanded(bool expanded)
    {
        var pan = this.FindControl<global::Avalonia.Controls.Border>("panCustomConfigs");
        var ico = this.FindControl<global::Avalonia.Controls.PathIcon>("icoCustomConfigsExpander");
        var panToggle = this.FindControl<global::Avalonia.Controls.Border>("panCustomConfigsToggle");
        var btnToggle = this.FindControl<global::Avalonia.Controls.Button>("btnCustomConfigsToggle");
        if (pan != null)
        {
            pan.MaxHeight = expanded ? 250 : 0;
            pan.Opacity = expanded ? 1 : 0;
            if (ico != null) ico.RenderTransform = new global::Avalonia.Media.RotateTransform(expanded ? 180 : 0);
            if (panToggle != null) panToggle.CornerRadius = expanded ? new global::Avalonia.CornerRadius(8, 8, 0, 0) : new global::Avalonia.CornerRadius(8);
            if (btnToggle != null) btnToggle.CornerRadius = expanded ? new global::Avalonia.CornerRadius(8, 8, 0, 0) : new global::Avalonia.CornerRadius(8);
        }
    }

    private void btnCustomConfigsToggle_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var src = e.Source as global::Avalonia.Controls.Control;
        while (src != null)
        {
            if (src.Name == "togCustomConfigs") return;
            src = src.Parent as global::Avalonia.Controls.Control;
        }

        var pan = this.FindControl<global::Avalonia.Controls.Border>("panCustomConfigs");
        if (pan != null)
        {
            SetCustomConfigsExpanded(pan.MaxHeight == 0);
        }
    }

    private void togCustomConfigs_IsCheckedChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isInitializingSettings) return;
        var tog = sender as global::Avalonia.Controls.ToggleSwitch;
        if (tog != null && tog.IsChecked != null)
        {
            if (tog.IsChecked == true)
            {
                var txt1 = this.FindControl<global::Avalonia.Controls.TextBox>("txtCustomConfig1");
                var txt2 = this.FindControl<global::Avalonia.Controls.TextBox>("txtCustomConfig2");
                bool isEmpty = string.IsNullOrWhiteSpace(txt1?.Text) && string.IsNullOrWhiteSpace(txt2?.Text);

                if (isEmpty)
                {
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { tog.IsChecked = false; });
                    SetCustomConfigsExpanded(true);
                    return;
                }

                if (txt1 != null) MainWindow.Instance.Config.CustomConfig1 = txt1.Text ?? "";
                if (txt2 != null) MainWindow.Instance.Config.CustomConfig2 = txt2.Text ?? "";
            }
            MainWindow.Instance.Config.EnableCustomConfigs = tog.IsChecked.Value;
            MainWindow.Instance.RequestConfigSave();
            NotifyCustomConfigsChanged(showSaveToast: false);
        }
    }

    private void NotifyCustomConfigsChanged(bool showSaveToast)
    {
        if (MainWindow.Instance.State.IsEngineRunning)
        {
            if (MainWindow.Instance.Config.LastXrayMode == "VPN Mode")
            {
                if (showSaveToast) MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastSavedReconnect, success: true);
                else MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
            }
            else
            {
                MainWindow.Instance.SmartRestartXray();
                if (showSaveToast) MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastSavedApplied, success: true);
                else MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastChangesApplied, success: true);
            }
        }
        else if (showSaveToast)
        {
            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastSaved, success: true);
        }
    }

    private void chkAllowOneCustomConfig_IsCheckedChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isInitializingSettings) return;
        var chk = sender as global::Avalonia.Controls.CheckBox;
        if (chk != null && chk.IsChecked != null)
        {
            MainWindow.Instance.Config.AllowOneCustomConfig = chk.IsChecked.Value;
            MainWindow.Instance.RequestConfigSave();
            NotifyCustomConfigsChanged(showSaveToast: false);
        }
    }

    private void btnCustomConfigsClear_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var txt1 = this.FindControl<global::Avalonia.Controls.TextBox>("txtCustomConfig1");
        var txt2 = this.FindControl<global::Avalonia.Controls.TextBox>("txtCustomConfig2");
        var chk = this.FindControl<global::Avalonia.Controls.CheckBox>("chkAllowOneCustomConfig");
        
        if (txt1 != null) txt1.Text = "";
        if (txt2 != null) txt2.Text = "";
        if (chk != null) chk.IsChecked = false;
        
        MainWindow.Instance.Config.CustomConfig1 = "";
        MainWindow.Instance.Config.CustomConfig2 = "";
        MainWindow.Instance.Config.AllowOneCustomConfig = false;

        var tog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togCustomConfigs");
        if (tog != null) tog.IsChecked = false;
        
        MainWindow.Instance.RequestConfigSave();
        NotifyCustomConfigsChanged(showSaveToast: false);
    }

    private void btnCustomConfigsSave_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var txt1 = this.FindControl<global::Avalonia.Controls.TextBox>("txtCustomConfig1");
        var txt2 = this.FindControl<global::Avalonia.Controls.TextBox>("txtCustomConfig2");
        var chk = this.FindControl<global::Avalonia.Controls.CheckBox>("chkAllowOneCustomConfig");

        if (txt1 != null) MainWindow.Instance.Config.CustomConfig1 = txt1.Text ?? "";
        if (txt2 != null) MainWindow.Instance.Config.CustomConfig2 = txt2.Text ?? "";
        if (chk != null) MainWindow.Instance.Config.AllowOneCustomConfig = chk.IsChecked ?? false;

        bool hasConfig = !string.IsNullOrWhiteSpace(MainWindow.Instance.Config.CustomConfig1) ||
                         !string.IsNullOrWhiteSpace(MainWindow.Instance.Config.CustomConfig2);

        var tog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togCustomConfigs");
        if (tog != null && tog.IsChecked != hasConfig)
        {
            _isInitializingSettings = true;
            tog.IsChecked = hasConfig;
            _isInitializingSettings = false;
        }
        MainWindow.Instance.Config.EnableCustomConfigs = hasConfig;

        SetCustomConfigsExpanded(false);

        MainWindow.Instance.RequestConfigSave();
        NotifyCustomConfigsChanged(showSaveToast: true);
    }

    private async void btnCustomConfigsPing_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var btn = sender as global::Avalonia.Controls.Button;
        if (btn != null) btn.Content = CrimsonX.Localization.AppStrings.ValidatingConfig;

        var txt1 = this.FindControl<global::Avalonia.Controls.TextBox>("txtCustomConfig1");
        var txt2 = this.FindControl<global::Avalonia.Controls.TextBox>("txtCustomConfig2");
        var chk = this.FindControl<global::Avalonia.Controls.CheckBox>("chkAllowOneCustomConfig");
        
        if (txt1 != null) MainWindow.Instance.Config.CustomConfig1 = txt1.Text ?? "";
        if (txt2 != null) MainWindow.Instance.Config.CustomConfig2 = txt2.Text ?? "";
        if (chk != null) MainWindow.Instance.Config.AllowOneCustomConfig = chk.IsChecked ?? false;
        MainWindow.Instance.RequestConfigSave();

        long ping1 = -1;
        long ping2 = -1;
        var ct = new System.Threading.CancellationTokenSource(15000).Token;

        if (!string.IsNullOrWhiteSpace(MainWindow.Instance.Config.CustomConfig1))
        {
            var r1 = await CrimsonX.Services.ConfigTester.TestConfigAsync(MainWindow.Instance.Config.CustomConfig1, MainWindow.Instance.Config, ct, false);
            if (r1 != null && r1.Success) ping1 = r1.Ping;
        }

        if (!string.IsNullOrWhiteSpace(MainWindow.Instance.Config.CustomConfig2))
        {
            var r2 = await CrimsonX.Services.ConfigTester.TestConfigAsync(MainWindow.Instance.Config.CustomConfig2, MainWindow.Instance.Config, ct, false);
            if (r2 != null && r2.Success) ping2 = r2.Ping;
        }

        if (btn != null) btn.Content = CrimsonX.Localization.AppStrings.PingBtn;

        string msg = "";
        if (ping1 != -1) msg += $"Config 1: {ping1}ms  ";
        if (ping2 != -1) msg += $"Config 2: {ping2}ms";
        if (ping1 == -1 && ping2 == -1) msg = CrimsonX.Localization.AppStrings.InvalidConfig;

        MainWindow.Instance.ShowToast(msg.Trim(), ping1 == -1 && ping2 == -1);
    }

    private void btnAdapterBindingToggle_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var src = e.Source as global::Avalonia.Controls.Control;
        while (src != null)
        {
            if (src.Name == "togAdapterBinding") return;
            src = src.Parent as global::Avalonia.Controls.Control;
        }

        var pan = this.FindControl<global::Avalonia.Controls.Border>("panAdapterBinding");
        var ico = this.FindControl<global::Avalonia.Controls.PathIcon>("icoAdapterBindingExpander");
        var panToggle = this.FindControl<global::Avalonia.Controls.Border>("panAdapterBindingToggle");
        var btnToggle = this.FindControl<global::Avalonia.Controls.Button>("btnAdapterBindingToggle");
        if (pan != null)
        {
            if (pan.MaxHeight == 0)
            {
                pan.MaxHeight = 200;
                pan.Opacity = 1;
                if (ico != null) ico.RenderTransform = new global::Avalonia.Media.RotateTransform(180);
                if (panToggle != null) panToggle.CornerRadius = new global::Avalonia.CornerRadius(8, 8, 0, 0);
                if (btnToggle != null) btnToggle.CornerRadius = new global::Avalonia.CornerRadius(8, 8, 0, 0);
                
                var cmb = this.FindControl<global::Avalonia.Controls.ComboBox>("cmbAdapters");
                if (cmb != null && cmb.Items.Count == 0)
                {
                    btnScanAdapters_Click(null, null);
                }
            }
            else
            {
                pan.MaxHeight = 0;
                pan.Opacity = 0;
                if (ico != null) ico.RenderTransform = new global::Avalonia.Media.RotateTransform(0);
                if (panToggle != null) panToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
                if (btnToggle != null) btnToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
            }
        }
    }

    private void btnDnsToggle_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var src = e.Source as global::Avalonia.Controls.Control;
        while (src != null)
        {
            if (src.Name == "togDnsSettings" || src.Name == "togSysDns")
                return;
            src = src.Parent as global::Avalonia.Controls.Control;
        }

        var panToggle = this.FindControl<global::Avalonia.Controls.Border>("panDnsToggle");
        var btnToggle = this.FindControl<global::Avalonia.Controls.Button>("btnDnsToggle");
        var pan       = this.FindControl<global::Avalonia.Controls.Border>("panDnsSettings");
        var ico       = this.FindControl<global::Avalonia.Controls.PathIcon>("icoDnsExpander");
        var cmbDohUrl = this.FindControl<global::Avalonia.Controls.ComboBox>("cmbDohUrl");

        if (pan != null && ico != null && cmbDohUrl != null)
        {
            if (pan.MaxHeight == 0)
            {
                cmbDohUrl.Text = MainWindow.Instance.Config.UpstreamDohUrl;

                var txtPrimary   = this.FindControl<global::Avalonia.Controls.TextBox>("txtSysDnsPrimary");
                var txtSecondary = this.FindControl<global::Avalonia.Controls.TextBox>("txtSysDnsSecondary");
                if (txtPrimary   != null) txtPrimary.Text   = MainWindow.Instance.Config.SystemDnsPrimary;
                if (txtSecondary != null) txtSecondary.Text = MainWindow.Instance.Config.SystemDnsSecondary;

                pan.MaxHeight = 340;
                pan.Opacity   = 1;

                var transform = new global::Avalonia.Media.RotateTransform(180);
                ico.RenderTransform = transform;

                if (panToggle != null) panToggle.CornerRadius = new global::Avalonia.CornerRadius(8, 8, 0, 0);
                if (btnToggle != null) btnToggle.CornerRadius = new global::Avalonia.CornerRadius(8, 8, 0, 0);
            }
            else
            {
                pan.MaxHeight = 0;
                pan.Opacity   = 0;
                var transform = new global::Avalonia.Media.RotateTransform(0);
                ico.RenderTransform = transform;
                if (panToggle != null) panToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
                if (btnToggle != null) btnToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
            }
        }
    }

    private void btnDohSave_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var cmbDohUrl = this.FindControl<global::Avalonia.Controls.ComboBox>("cmbDohUrl");
        var tog       = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togDnsSettings");

        if (cmbDohUrl != null && tog != null)
        {
            var url = cmbDohUrl.Text?.Trim() ?? "";
            MainWindow.Instance.Config.UpstreamDohUrl    = url;
            MainWindow.Instance.Config.EnableUpstreamDoh = true;

            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                tog.IsChecked = true;
            });

            MainWindow.Instance.RequestConfigSave();
            if (MainWindow.Instance.State.IsEngineRunning) MainWindow.Instance.SmartRestartXray();
        }
    }

    private void btnLanAuthSave_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var txtUser = this.FindControl<global::Avalonia.Controls.TextBox>("txtLanUser");
        var txtPass = this.FindControl<global::Avalonia.Controls.TextBox>("txtLanPass");
        var tog     = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togLanAuth");

        var user = txtUser?.Text?.Trim() ?? "";
        var pass = txtPass?.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(user))
        {
            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.IsPersian
                ? "\u0644\u0637\u0641\u0627\u064b \u0646\u0627\u0645 \u06a9\u0627\u0631\u0628\u0631\u06cc \u0631\u0627 \u0648\u0627\u0631\u062f \u06a9\u0646\u06cc\u062f."
                : "Please enter a username.");
            return;
        }

        MainWindow.Instance.Config.LanAuthUsername = user;
        MainWindow.Instance.Config.LanAuthPassword = pass;
        MainWindow.Instance.Config.EnableLanAuth   = true;

        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            if (tog != null) tog.IsChecked = true;
        });

        MainWindow.Instance.RequestConfigSave();

        if (MainWindow.Instance.State.IsEngineRunning)
        {
            if (MainWindow.Instance._pollMode == "VPN Mode")
                MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.IsPersian
                    ? "\u0628\u0631\u0627\u06cc \u0627\u0639\u0645\u0627\u0644 \u062a\u063a\u06cc\u06cc\u0631\u0627\u062a \u062f\u0648\u0628\u0627\u0631\u0647 \u0645\u062a\u0635\u0644 \u0634\u0648\u06cc\u062f."
                    : "Reconnect to apply the changes.");
            else
                MainWindow.Instance.SmartRestartXray();
        }
        else
        {
            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.IsPersian
                ? "\u0627\u0637\u0644\u0627\u0639\u0627\u062a \u0648\u0631\u0648\u062f \u0630\u062e\u06cc\u0631\u0647 \u0634\u062f."
                : "Credentials saved.", success: true);
        }
    }

    private void btnLanPassEye_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var txtPass = this.FindControl<global::Avalonia.Controls.TextBox>("txtLanPass");
        var ico     = this.FindControl<global::Avalonia.Controls.PathIcon>("icoLanPassEye");
        if (txtPass == null) return;

        MainWindow.Instance._lanPassVisible = !MainWindow.Instance._lanPassVisible;
        txtPass.PasswordChar = MainWindow.Instance._lanPassVisible ? '\0' : '\u2022';

        if (ico != null)
            ico.Data = MainWindow.Instance._lanPassVisible
                ? global::Avalonia.Media.Geometry.Parse("M12 7c2.76 0 5 2.24 5 5 0 .65-.13 1.26-.36 1.83l2.92 2.92c1.51-1.26 2.7-2.89 3.43-4.75-1.73-4.39-6-7.5-11-7.5-1.4 0-2.74.25-3.98.7l2.16 2.16C10.74 7.13 11.35 7 12 7zM2 4.27l2.28 2.28.46.46C3.08 8.3 1.78 10.02 1 12c1.73 4.39 6 7.5 11 7.5 1.55 0 3.03-.3 4.38-.84l.42.42L19.73 22 21 20.73 3.27 3 2 4.27zM7.53 9.8l1.55 1.55c-.05.21-.08.43-.08.65 0 1.66 1.34 3 3 3 .22 0 .44-.03.65-.08l1.55 1.55c-.67.33-1.41.53-2.2.53-2.76 0-5-2.24-5-5 0-.79.2-1.53.53-2.2zm4.31-.78l3.15 3.15.02-.16c0-1.66-1.34-3-3-3l-.17.01z")
                : global::Avalonia.Media.Geometry.Parse("M12 4.5C7 4.5 2.73 7.61 1 12c1.73 4.39 6 7.5 11 7.5s9.27-3.11 11-7.5c-1.73-4.39-6-7.5-11-7.5zM12 17c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5zm0-8c-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3-1.34-3-3-3z");
    }

    private void btnLanToggle_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var src = e.Source as global::Avalonia.Controls.Control;
        while (src != null)
        {
            if (src.Name == "btnLanTog" || src.Name == "togLanAuth") return;
            src = src.Parent as global::Avalonia.Controls.Control;
        }

        var panToggle = this.FindControl<global::Avalonia.Controls.Border>("panLanToggle");
        var btnToggle = this.FindControl<global::Avalonia.Controls.Button>("btnLanToggle");
        var pan       = this.FindControl<global::Avalonia.Controls.Border>("panLanSettings");
        var ico       = this.FindControl<global::Avalonia.Controls.PathIcon>("icoLanExpander");

        if (pan == null || ico == null) return;

        if (pan.MaxHeight == 0)
        {
            var txtUser = this.FindControl<global::Avalonia.Controls.TextBox>("txtLanUser");
            var txtPass = this.FindControl<global::Avalonia.Controls.TextBox>("txtLanPass");
            var tog     = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togLanAuth");
            if (txtUser != null) txtUser.Text = MainWindow.Instance.Config.LanAuthUsername;
            if (txtPass != null) txtPass.Text = MainWindow.Instance.Config.LanAuthPassword;
            if (tog     != null) tog.IsChecked = MainWindow.Instance.Config.EnableLanAuth;

            pan.MaxHeight = 160;
            pan.Opacity   = 1;
            ico.RenderTransform = new global::Avalonia.Media.RotateTransform(180);
            if (panToggle != null) panToggle.CornerRadius = new global::Avalonia.CornerRadius(8, 8, 0, 0);
            if (btnToggle != null) btnToggle.CornerRadius = new global::Avalonia.CornerRadius(8, 8, 0, 0);
        }
        else
        {
            pan.MaxHeight = 0;
            pan.Opacity   = 0;
            ico.RenderTransform = new global::Avalonia.Media.RotateTransform(0);
            if (panToggle != null) panToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
            if (btnToggle != null) btnToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
        }
    }

    private void btnScanAdapters_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs? e = null)
    {
        var cmb = this.FindControl<global::Avalonia.Controls.ComboBox>("cmbAdapters");
        if (cmb == null) return;
        
        cmb.Items.Clear();
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
        
        if (!string.IsNullOrWhiteSpace(MainWindow.Instance.Config.SelectedAdapterName) && !string.IsNullOrWhiteSpace(MainWindow.Instance.Config.SelectedAdapterIp))
        {
            var toSelect = $"{MainWindow.Instance.Config.SelectedAdapterName} - {MainWindow.Instance.Config.SelectedAdapterIp}";
            var itemsList = cmb.Items.Cast<string>().ToList();
            var index = itemsList.IndexOf(toSelect);
            if (index >= 0)
            {
                cmb.SelectedIndex = index;
            }
            else
            {
                MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.IsPersian ? "آداپتور شبکه قبلی شما دیگر در دسترس نیست." : "Your previously selected network adapter is no longer available.");
                MainWindow.Instance.Config.SelectedAdapterName = "";
                MainWindow.Instance.Config.SelectedAdapterIp = "";
                MainWindow.Instance.RequestConfigSave();
                
                if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
            }
        }
        else if (cmb.Items.Count > 0)
        {
            cmb.SelectedIndex = 0;
        }
    }

    private void btnSysDnsSave_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var txtPrimary   = this.FindControl<global::Avalonia.Controls.TextBox>("txtSysDnsPrimary");
        var txtSecondary = this.FindControl<global::Avalonia.Controls.TextBox>("txtSysDnsSecondary");
        var tog          = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togSysDns");

        var primary   = txtPrimary?.Text?.Trim()   ?? "";
        var secondary = txtSecondary?.Text?.Trim() ?? "";

        if (!CrimsonX.Services.DnsService.IsValidIpv4(primary))
        {
            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.IsPersian
                ? "لطفاً یک آدرس IPv4 معتبر برای DNS اول وارد کنید."
                : "Please enter a valid IPv4 address for the primary DNS.");
            return;
        }
        if (!string.IsNullOrWhiteSpace(secondary) && !CrimsonX.Services.DnsService.IsValidIpv4(secondary))
        {
            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.IsPersian
                ? "لطفاً یک آدرس IPv4 معتبر برای DNS دوم وارد کنید."
                : "Please enter a valid IPv4 address for the secondary DNS.");
            return;
        }

        MainWindow.Instance.Config.SystemDnsPrimary   = primary;
        MainWindow.Instance.Config.SystemDnsSecondary = secondary;
        MainWindow.Instance.Config.EnableSystemDns    = true;

        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            if (tog != null) tog.IsChecked = true;
        });

        MainWindow.Instance.RequestConfigSave();
        if (MainWindow.Instance.State.IsEngineRunning)
            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectDns);
    }

    private void btnXrayCancel_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var pan = this.FindControl<global::Avalonia.Controls.Border>("panXrayExitNode");
        var ico = this.FindControl<global::Avalonia.Controls.PathIcon>("icoXrayExitNodeExpander");
        var panToggle = this.FindControl<global::Avalonia.Controls.Border>("panXrayExitNodeToggle");
        var btnToggle = this.FindControl<global::Avalonia.Controls.Button>("btnXrayExitNodeToggle");
        if (pan != null && ico != null)
        {
            pan.MaxHeight = 0;
            pan.Opacity = 0;
            var transform = new global::Avalonia.Media.RotateTransform(0);
            ico.RenderTransform = transform;
            if (panToggle != null) panToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
            if (btnToggle != null) btnToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
        }
    }

    private void btnXrayExitNodeToggle_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {

        var src = e.Source as global::Avalonia.Controls.Control;
        while (src != null)
        {
            if (src.Name == "togXrayExitNode")
                return;
            src = src.Parent as global::Avalonia.Controls.Control;
        }
        var panToggle = this.FindControl<global::Avalonia.Controls.Border>("panXrayExitNodeToggle");
        var btnToggle = this.FindControl<global::Avalonia.Controls.Button>("btnXrayExitNodeToggle");
        var pan = this.FindControl<global::Avalonia.Controls.Border>("panXrayExitNode");
        var ico = this.FindControl<global::Avalonia.Controls.PathIcon>("icoXrayExitNodeExpander");
        var txt = this.FindControl<global::Avalonia.Controls.TextBox>("txtXrayJson");
        var tog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togXrayExitNode");
        
        if (pan != null && ico != null && txt != null && tog != null)
        {
            if (pan.MaxHeight == 0)
            {
                txt.Text = MainWindow.Instance.Config.V2rayChainJson;
                tog.IsChecked = MainWindow.Instance.Config.EnableV2rayChain;
                
                pan.MaxHeight = 350;
                pan.Opacity = 1;
                
                var transform = new global::Avalonia.Media.RotateTransform(180);
                ico.RenderTransform = transform;
                
                if (panToggle != null) panToggle.CornerRadius = new global::Avalonia.CornerRadius(8, 8, 0, 0);
                if (btnToggle != null) btnToggle.CornerRadius = new global::Avalonia.CornerRadius(8, 8, 0, 0);
            }
            else
            {
                pan.MaxHeight = 0;
                pan.Opacity = 0;
                
                var transform = new global::Avalonia.Media.RotateTransform(0);
                ico.RenderTransform = transform;
                
                if (panToggle != null) panToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
                if (btnToggle != null) btnToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
            }
        }
    }

    private async void btnXrayImport_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var topLevel = global::Avalonia.Controls.TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select Xray JSON File",
                AllowMultiple = false,
                FileTypeFilter = new[] { new global::Avalonia.Platform.Storage.FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } }, new global::Avalonia.Platform.Storage.FilePickerFileType("All Files") { Patterns = new[] { "*.*" } } }
            });

            if (files != null && files.Count > 0)
            {
                var file = files[0];
                var path = file.Path.LocalPath;
                if (System.IO.File.Exists(path))
                {
                    var txt = this.FindControl<global::Avalonia.Controls.TextBox>("txtXrayJson");
                    if (txt != null)
                        txt.Text = System.IO.File.ReadAllText(path);
                }
            }
        }
        catch
        {
            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastFailedImport);
        }
    }

    private async void btnXraySave_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var txt = this.FindControl<global::Avalonia.Controls.TextBox>("txtXrayJson");
        var tog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togXrayExitNode");
        
        if (txt != null && tog != null)
        {
            var text = txt.Text ?? "";
            bool enable = tog.IsChecked ?? false;
            
            if (string.IsNullOrWhiteSpace(text))
            {
                MainWindow.Instance.Config.V2rayChainJson = "";
                MainWindow.Instance.Config.EnableV2rayChain = enable;
                CrimsonX.Services.ConfigService.Save(MainWindow.Instance.Config, MainWindow.Instance.State, MainWindow.Instance.Config.CfgFile);
                
                btnXrayCancel_Click(sender, e);
                return;
            }
            
            try
            {
                var parsed = Newtonsoft.Json.Linq.JObject.Parse(text);
                Newtonsoft.Json.Linq.JToken? testNode = parsed["outbounds"] is Newtonsoft.Json.Linq.JArray arr ? arr.FirstOrDefault() : parsed;
                if (testNode?["protocol"] == null)
                    throw new Exception("Missing 'protocol' field.");
                
                var streamSettings = testNode["streamSettings"];
                if (streamSettings != null)
                {

                }
                
                var settings = testNode["settings"];
                if (settings != null)
                {
                    var flows = settings.SelectTokens("..flow").ToList();
                    foreach (var flow in flows)
                    {
                        if (flow.ToString().Contains("vision"))
                        {
                            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.IsPersian ? "کانفیگ های XTLS Vision به عنوان نود خروجی پشتیبانی نمی‌شوند." : "XTLS Vision configs cannot be used as a Custom Exit Node.");
                            return;
                        }
                    }
                }
                
                string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
                try
                {
                    System.IO.File.WriteAllText(tempFile, text);
                    
                    string xrayExe = System.IO.Path.Combine(MainWindow.Instance.Config.BaseDir, "Data", "xray", "xray.exe");
                    if (System.IO.File.Exists(xrayExe))
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = xrayExe,
                            Arguments = $"-test -config \"{tempFile}\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        
                        using (var proc = System.Diagnostics.Process.Start(psi))
                        {
                            if (proc != null)
                            {
                                var outTask = proc.StandardOutput.ReadToEndAsync();
                                var errTask = proc.StandardError.ReadToEndAsync();
                                await proc.WaitForExitAsync();
                                if (proc.ExitCode != 0)
                                {
                                    string err = await errTask;
                                    string outStr = await outTask;
                                    string msg = string.IsNullOrWhiteSpace(err) ? outStr : err;
                                    var lines = msg.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                                    msg = string.Join(" ", lines.Where(l => !l.Contains("Xray, Penetrates Everything") && !l.Contains("unified platform")));
                                    msg = msg.Trim();
                                    MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastXrayRejected + msg.Substring(0, System.Math.Min(msg.Length, 150)));
                                    return;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    try { if (System.IO.File.Exists(tempFile)) System.IO.File.Delete(tempFile); } catch (Exception ex) { CrimsonX.Services.SimpleLogger.Log(ex); }
                }

                MainWindow.Instance.Config.V2rayChainJson = text.Trim();
                MainWindow.Instance.Config.EnableV2rayChain = true;
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    tog.IsChecked = true;
                });
                
                CrimsonX.Services.ConfigService.Save(MainWindow.Instance.Config, MainWindow.Instance.State, MainWindow.Instance.Config.CfgFile);
                if (MainWindow.Instance.State.IsEngineRunning) MainWindow.Instance.SmartRestartXray();
                
                btnXrayCancel_Click(sender, e);
            }
            catch (Exception ex)
            {
                CrimsonX.Services.SimpleLogger.Log(ex);
                MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastInvalidJson + " " + ex.Message);
            }
        }
    }

    private async void SettingTog_CheckedChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isInitializingSettings) return;


        var tog = sender as global::Avalonia.Controls.ToggleSwitch;
        if (tog == null) return;

        bool val = tog.IsChecked ?? false;

        switch (tog.Name)
        {
            case "btnBootTog":
                try {
                    string exe = System.Environment.ProcessPath ?? "";
                    await CrimsonX.Services.ProcessService.UpdateBootScheduledTask(val, exe);
                    MainWindow.Instance.Config.LaunchOnBoot = val;
                } catch (System.Exception ex) {
                    MainWindow.Instance.Config.LaunchOnBoot = false;
                    tog.IsChecked = false;
                    MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastTaskFailed + ex.Message);
                }
                break;
            case "btnAutoTog":
                MainWindow.Instance.Config.AutoStart = val;
                break;
            case "btnStartMinTog":
                MainWindow.Instance.Config.StartMinimized = val;
                break;
            case "btnTrayTog":
                MainWindow.Instance.Config.MinimizeToTray = val;
                break;
            case "btnAdBlockTog":
                MainWindow.Instance.Config.EnableAdBlock = val;
                if (MainWindow.Instance.State.IsEngineRunning) MainWindow.Instance.SmartRestartXray();
                break;
            case "btnLanTog":
                MainWindow.Instance.Config.AllowLanConnections = val;
                MainWindow.Instance.UpdateLanPortUI();
                MainWindow.Instance.SmartRestartXray();
                break;
            case "btnDebugTog":
                MainWindow.Instance.Config.DebugMode = val;
                CrimsonX.Services.SimpleLogger.EnableLogging = val;
                break;
            case "togDisableBgChecks":
                MainWindow.Instance.Config.DisableBackgroundChecks = val;
                break;
            case "togDisableRefreshTimer":
                MainWindow.Instance.Config.DisableRefreshTimer = val;
                break;
        }

        MainWindow.Instance.RequestConfigSave();
    }

    private void Shortcut_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var btn = sender as global::Avalonia.Controls.Button;
        if (btn == null) return;

        try
        {
            Type? wshType = Type.GetTypeFromProgID("WScript.Shell");
            if (wshType == null) return;
            var ws = (dynamic)Activator.CreateInstance(wshType)!;

            string destPath = "";
            if (btn.Name == "btnDesktopShortcut")
            {
                destPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), "CrimsonX.lnk");
            }
            else if (btn.Name == "btnStartMenuShortcut")
            {
                string programsPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.StartMenu), "Programs");
                if (!System.IO.Directory.Exists(programsPath)) System.IO.Directory.CreateDirectory(programsPath);
                destPath = System.IO.Path.Combine(programsPath, "CrimsonX.lnk");
            }

            dynamic sc = ws.CreateShortcut(destPath);
            sc.TargetPath = System.Environment.ProcessPath ?? "";
            sc.WorkingDirectory = MainWindow.Instance.Config.BaseDir;
            sc.Save();

            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastShortcutCreated, success: true);
        }
        catch
        {
            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastShortcutFailed);
        }
    }

    private void togAdapterBinding_IsCheckedChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isInitializingSettings) return;

        var tog = sender as global::Avalonia.Controls.ToggleSwitch;
        if (tog != null)
        {
            if (tog.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(MainWindow.Instance.Config.SelectedAdapterIp))
                {
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { tog.IsChecked = false; });
                    var pan = this.FindControl<global::Avalonia.Controls.Border>("panAdapterBinding");
                    if (pan != null && pan.MaxHeight == 0)
                    {
                        pan.MaxHeight = 200;
                        pan.Opacity = 1;
                        var ico = this.FindControl<global::Avalonia.Controls.PathIcon>("icoAdapterBindingExpander");
                        if (ico != null) ico.RenderTransform = new global::Avalonia.Media.RotateTransform(180);
                        
                        var cmb = this.FindControl<global::Avalonia.Controls.ComboBox>("cmbAdapters");
                        if (cmb != null && cmb.Items.Count == 0)
                        {
                            btnScanAdapters_Click(null, null);
                        }
                    }
                    return;
                }
                else if (!MainWindow.Instance.Config.EnableAdapterBinding)
                {
                    MainWindow.Instance.Config.EnableAdapterBinding = true;
                    MainWindow.Instance.RequestConfigSave();
                    if (MainWindow.Instance.State.IsEngineRunning)
                        MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
                }
            }
            else
            {
                if (MainWindow.Instance.Config.EnableAdapterBinding)
                {
                    MainWindow.Instance.Config.EnableAdapterBinding = false;
                    MainWindow.Instance.RequestConfigSave();
                    if (MainWindow.Instance.State.IsEngineRunning) MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
                }
            }
            }
    }

    private void togDnsSettings_IsCheckedChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isInitializingSettings) return;

        var tog = sender as global::Avalonia.Controls.ToggleSwitch;
        if (tog == null) return;

        if (tog.IsChecked == true)
        {
            var cmbDohUrl = this.FindControl<global::Avalonia.Controls.ComboBox>("cmbDohUrl");
            var liveUrl   = cmbDohUrl?.Text?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(liveUrl))
                MainWindow.Instance.Config.UpstreamDohUrl = liveUrl;

            if (string.IsNullOrWhiteSpace(MainWindow.Instance.Config.UpstreamDohUrl))
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { tog.IsChecked = false; });
                return;
            }

            MainWindow.Instance.Config.EnableUpstreamDoh = true;
            MainWindow.Instance.RequestConfigSave();
            if (MainWindow.Instance.State.IsEngineRunning) MainWindow.Instance.SmartRestartXray();
        }
        else
        {
            if (MainWindow.Instance.Config.EnableUpstreamDoh)
            {
                MainWindow.Instance.Config.EnableUpstreamDoh = false;
                MainWindow.Instance.RequestConfigSave();
                if (MainWindow.Instance.State.IsEngineRunning) MainWindow.Instance.SmartRestartXray();
            }
        }
    }

    private void togLanAuth_IsCheckedChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isInitializingSettings) return;

        var tog = sender as global::Avalonia.Controls.ToggleSwitch;
        if (tog == null) return;

        if (tog.IsChecked == true)
        {
            var txtUser = this.FindControl<global::Avalonia.Controls.TextBox>("txtLanUser");
            var txtPass = this.FindControl<global::Avalonia.Controls.TextBox>("txtLanPass");
            var liveUser = txtUser?.Text?.Trim() ?? "";
            var livePass = txtPass?.Text?.Trim() ?? "";

            if (!string.IsNullOrWhiteSpace(liveUser))
            {
                MainWindow.Instance.Config.LanAuthUsername = liveUser;
                MainWindow.Instance.Config.LanAuthPassword = livePass;
            }

            if (string.IsNullOrWhiteSpace(MainWindow.Instance.Config.LanAuthUsername))
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { tog.IsChecked = false; });
                return;
            }

            MainWindow.Instance.Config.EnableLanAuth = true;
            MainWindow.Instance.RequestConfigSave();

            if (MainWindow.Instance.State.IsEngineRunning)
            {
                if (MainWindow.Instance._pollMode == "VPN Mode")
                    MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.IsPersian
                        ? "\u0628\u0631\u0627\u06cc \u0627\u0639\u0645\u0627\u0644 \u062a\u063a\u06cc\u06cc\u0631\u0627\u062a \u062f\u0648\u0628\u0627\u0631\u0647 \u0645\u062a\u0635\u0644 \u0634\u0648\u06cc\u062f."
                        : "Reconnect to apply the changes.");
                else
                    MainWindow.Instance.SmartRestartXray();
            }
        }
        else
        {
            if (MainWindow.Instance.Config.EnableLanAuth)
            {
                MainWindow.Instance.Config.EnableLanAuth = false;
                MainWindow.Instance.RequestConfigSave();

                if (MainWindow.Instance.State.IsEngineRunning)
                {
                    if (MainWindow.Instance._pollMode == "VPN Mode")
                        MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.IsPersian
                            ? "\u0628\u0631\u0627\u06cc \u0627\u0639\u0645\u0627\u0644 \u062a\u063a\u06cc\u06cc\u0631\u0627\u062a \u062f\u0648\u0628\u0627\u0631\u0647 \u0645\u062a\u0635\u0644 \u0634\u0648\u06cc\u062f."
                        : "Reconnect to apply the changes.");
                    else
                        MainWindow.Instance.SmartRestartXray();
                }
            }
        }
    }

    private void togSysDns_IsCheckedChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isInitializingSettings) return;

        var tog = sender as global::Avalonia.Controls.ToggleSwitch;
        if (tog == null) return;

        if (tog.IsChecked == true)
        {
            var txtPrimary   = this.FindControl<global::Avalonia.Controls.TextBox>("txtSysDnsPrimary");
            var txtSecondary = this.FindControl<global::Avalonia.Controls.TextBox>("txtSysDnsSecondary");
            var livePrimary   = txtPrimary?.Text?.Trim()   ?? "";
            var liveSecondary = txtSecondary?.Text?.Trim() ?? "";

            if (!string.IsNullOrWhiteSpace(livePrimary))
            {
                if (!CrimsonX.Services.DnsService.IsValidIpv4(livePrimary))
                {
                    MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.IsPersian
                        ? "لطفاً یک آدرس IPv4 معتبر برای DNS اول وارد کنید."
                        : "Please enter a valid IPv4 address for the primary DNS.");
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { tog.IsChecked = false; });
                    return;
                }
                if (!string.IsNullOrWhiteSpace(liveSecondary) && !CrimsonX.Services.DnsService.IsValidIpv4(liveSecondary))
                {
                    MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.IsPersian
                        ? "لطفاً یک آدرس IPv4 معتبر برای DNS دوم وارد کنید."
                        : "Please enter a valid IPv4 address for the secondary DNS.");
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { tog.IsChecked = false; });
                    return;
                }
                MainWindow.Instance.Config.SystemDnsPrimary   = livePrimary;
                MainWindow.Instance.Config.SystemDnsSecondary = liveSecondary;
            }

            if (string.IsNullOrWhiteSpace(MainWindow.Instance.Config.SystemDnsPrimary))
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { tog.IsChecked = false; });
                return;
            }

            MainWindow.Instance.Config.EnableSystemDns = true;
            MainWindow.Instance.RequestConfigSave();
            if (MainWindow.Instance.State.IsEngineRunning)
                MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectDns);
        }
        else
        {
            if (MainWindow.Instance.Config.EnableSystemDns)
            {
                MainWindow.Instance.Config.EnableSystemDns = false;
                MainWindow.Instance.RequestConfigSave();
                if (MainWindow.Instance.State.IsEngineRunning)
                    MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectDns);
            }
        }
    }

        private void togXrayExitNode_IsCheckedChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isInitializingSettings) return;

        var tog = sender as global::Avalonia.Controls.ToggleSwitch;
        if (tog != null)
        {
            if (tog.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(MainWindow.Instance.Config.V2rayChainJson))
                {
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() => tog.IsChecked = false);
                    
                    var panXrayExitNode = this.FindControl<global::Avalonia.Controls.Border>("panXrayExitNode");
                    var icoXrayExitNodeExpander = this.FindControl<global::Avalonia.Controls.PathIcon>("icoXrayExitNodeExpander");
                    
                    if (panXrayExitNode != null && panXrayExitNode.MaxHeight == 0)
                    {
                        panXrayExitNode.MaxHeight = 500;
                        panXrayExitNode.Opacity = 1;
                        if (icoXrayExitNodeExpander != null)
                            icoXrayExitNodeExpander.RenderTransform = new global::Avalonia.Media.RotateTransform(180);
                        
                        var panToggle = this.FindControl<global::Avalonia.Controls.Border>("panXrayExitNodeToggle");
                        var btnToggle = this.FindControl<global::Avalonia.Controls.Button>("btnXrayExitNodeToggle");
                        if (panToggle != null) panToggle.CornerRadius = new global::Avalonia.CornerRadius(8, 8, 0, 0);
                        if (btnToggle != null) btnToggle.CornerRadius = new global::Avalonia.CornerRadius(8, 8, 0, 0);
                    }
                    return;
                }
                else if (!MainWindow.Instance.Config.EnableV2rayChain)
                {
                    MainWindow.Instance.Config.EnableV2rayChain = true;
                    MainWindow.Instance.RequestConfigSave();
                    if (MainWindow.Instance.State.IsEngineRunning) MainWindow.Instance.SmartRestartXray();
                }
            }
            else
            {
                if (MainWindow.Instance.Config.EnableV2rayChain)
                {
                    MainWindow.Instance.Config.EnableV2rayChain = false;
                    MainWindow.Instance.RequestConfigSave();
                    if (MainWindow.Instance.State.IsEngineRunning) MainWindow.Instance.SmartRestartXray();
                }
            }
        }
    }

    private void txtXrayJson_TextChanged(object? sender, global::Avalonia.Controls.TextChangedEventArgs e)
    {
        var txt = sender as global::Avalonia.Controls.TextBox;
        if (txt == null || string.IsNullOrWhiteSpace(txt.Text)) return;

        string text = txt.Text.Trim();
        


        if (CrimsonX.Services.XrayLinkParser.TryParseLink(text, out string json))
        {
            txt.Text = json;
            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastLinkConverted, success: true);
        }
    }

    private void cmbAdapters_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        var cmb = sender as global::Avalonia.Controls.ComboBox;
        if (cmb != null && cmb.SelectedItem is string selectedText && !string.IsNullOrWhiteSpace(selectedText))
        {
            var parts = selectedText.Split(new[] { " - " }, StringSplitOptions.None);
            if (parts.Length >= 2)
            {
                var newIp   = parts[parts.Length - 1];
                var newName = string.Join(" - ", parts, 0, parts.Length - 1);

                bool changed = newIp != MainWindow.Instance.Config.SelectedAdapterIp;

                MainWindow.Instance.Config.SelectedAdapterName = newName;
                MainWindow.Instance.Config.SelectedAdapterIp = newIp;
                MainWindow.Instance.RequestConfigSave();

                if (changed && MainWindow.Instance.Config.EnableAdapterBinding && MainWindow.Instance.State.IsEngineRunning)
                {
                    MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
                }
            }
        }
    }

private async void BtnLanguage_Click(object? sender, RoutedEventArgs e)
    {
        bool isSelf = LanguagePopup != null && LanguagePopup.IsOpen && LanguagePopup.PlacementTarget?.Name == "btnLanguage";
        if (isSelf)
        {
            _ = ClosePopupAnimatedAsync();
            return;
        }

        if (LanguagePopup != null && LanguagePopup.IsOpen)
        {
            LanguagePopup.IsOpen = false;
            if (LanguagePopup.Child is Border oldBorder) oldBorder.Classes.Remove("popupOpen");
        }

        _ = ClosePopupAnimatedAsync();

        if (LanguagePopup != null)
        {
            LanguagePopup.PlacementTarget  = this.FindControl<Control>("btnLanguage");
            LanguagePopup.Placement        = PlacementMode.Bottom;
            LanguagePopup.HorizontalOffset = 0;
            LanguagePopup.VerticalOffset   = 5;
            LanguagePopup.IsOpen           = true;
            
            var sld = MainWindow.Instance.FindControl<global::Avalonia.Controls.Border>("LightDismissOverlay");
            if (sld != null) sld.IsVisible = true;
            
            await Task.Delay(10);
            if (LanguagePopup.Child is Border border) border.Classes.Add("popupOpen");
        }
    }

    private bool _isExcludeLocationsExpanded = false;
    private void btnExcludeLocationsToggle_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (e != null)
        {
            var src = e.Source as global::Avalonia.Controls.Control;
            while (src != null)
            {
                if (src.Name == "togExcludeLocations") return;
                src = src.Parent as global::Avalonia.Controls.Control;
            }
        }
        
        var pan = this.FindControl<global::Avalonia.Controls.Border>("panExcludeLocations");
        var ico = this.FindControl<global::Avalonia.Controls.PathIcon>("icoExcludeLocationsExpander");
        if (pan == null || ico == null) return;
        
        _isExcludeLocationsExpanded = !_isExcludeLocationsExpanded;
        pan.MaxHeight = _isExcludeLocationsExpanded ? 500 : 0;
        pan.Opacity = _isExcludeLocationsExpanded ? 1 : 0;
        
        var panToggle = this.FindControl<global::Avalonia.Controls.Border>("panExcludeLocationsToggle");
        var btnToggle = this.FindControl<global::Avalonia.Controls.Button>("btnExcludeLocationsToggle");
        if (panToggle != null) panToggle.CornerRadius = _isExcludeLocationsExpanded ? new global::Avalonia.CornerRadius(8, 8, 0, 0) : new global::Avalonia.CornerRadius(8);
        if (btnToggle != null) btnToggle.CornerRadius = _isExcludeLocationsExpanded ? new global::Avalonia.CornerRadius(8, 8, 0, 0) : new global::Avalonia.CornerRadius(8);
        
        if (ico.RenderTransform is global::Avalonia.Media.RotateTransform rt)
        {
            rt.Angle = _isExcludeLocationsExpanded ? 180 : 0;
        }
        else
        {
            ico.RenderTransform = new global::Avalonia.Media.RotateTransform { Angle = _isExcludeLocationsExpanded ? 180 : 0 };
        }
    }
    
    private void togExcludeLocations_IsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isInitializingSettings) return;
        var tog = sender as global::Avalonia.Controls.ToggleSwitch;
        if (tog == null || !tog.IsChecked.HasValue) return;
        
        var _cfg = MainWindow.Instance.Config;
        
        if (tog.IsChecked.Value)
        {
            if (_cfg.ExcludedContinents == null || _cfg.ExcludedContinents.Count == 0)
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    _isInitializingSettings = true;
                    tog.IsChecked = false;
                    _isInitializingSettings = false;
                });
                
                if (!_isExcludeLocationsExpanded)
                {
                    btnExcludeLocationsToggle_Click(null, new Avalonia.Interactivity.RoutedEventArgs());
                }
                return;
            }
        }
        
        _cfg.EnableExcludedContinents = tog.IsChecked.Value;
        MainWindow.Instance.RequestConfigSave();
        if (MainWindow.Instance.State.IsEngineRunning)
        {
            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
        }
    }
    
    private void ContinentPill_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is global::Avalonia.Controls.Primitives.ToggleButton tb && tb.Tag is string tag)
        {
            var _cfg = MainWindow.Instance.Config;
            if (_cfg.ExcludedContinents == null) _cfg.ExcludedContinents = new System.Collections.Generic.List<string>();
            
            string fullContinentName = tag switch {
                "AS" => "Asia",
                "EU" => "Europe",
                "NA" => "North America",
                "SA" => "South America",
                "AF" => "Africa",
                "OC" => "Oceania",
                "AN" => "Antarctica",
                _ => tag
            };
            
            if (tb.IsChecked.GetValueOrDefault())
            {
                if (!_cfg.ExcludedContinents.Contains(fullContinentName))
                    _cfg.ExcludedContinents.Add(fullContinentName);
            }
            else
            {
                _cfg.ExcludedContinents.Remove(fullContinentName);
            }
            MainWindow.Instance.RequestConfigSave();
            
            if (MainWindow.Instance.State.IsEngineRunning)
            {
                MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
            }
            
            var togExcludeLocations = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togExcludeLocations");
            if (togExcludeLocations != null)
            {
                bool shouldBeChecked = _cfg.ExcludedContinents.Count > 0;
                if (togExcludeLocations.IsChecked != shouldBeChecked)
                {
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        _isInitializingSettings = true;
                        togExcludeLocations.IsChecked = shouldBeChecked;
                        _isInitializingSettings = false;
                    });
                    
                    _cfg.EnableExcludedContinents = shouldBeChecked;
                    MainWindow.Instance.RequestConfigSave();
                }
            }
        }
    }

    private async void BtnLbPolicy_Click(object? sender, RoutedEventArgs e)
    {
        bool isSelf = LbPolicyPopup != null && LbPolicyPopup.IsOpen && LbPolicyPopup.PlacementTarget?.Name == "btnLbPolicy";
        if (isSelf)
        {
            _ = ClosePopupAnimatedAsync();
            return;
        }

        if (LbPolicyPopup != null && LbPolicyPopup.IsOpen)
        {
            LbPolicyPopup.IsOpen = false;
            if (LbPolicyPopup.Child is Border oldBorder) oldBorder.Classes.Remove("popupOpen");
        }

        _ = ClosePopupAnimatedAsync();

        if (LbPolicyPopup != null)
        {
            LbPolicyPopup.PlacementTarget  = this.FindControl<Control>("btnLbPolicy");
            LbPolicyPopup.Placement        = PlacementMode.Bottom;
            LbPolicyPopup.HorizontalOffset = 0;
            LbPolicyPopup.VerticalOffset   = 5;
            LbPolicyPopup.IsOpen           = true;

            var sld = MainWindow.Instance.FindControl<global::Avalonia.Controls.Border>("LightDismissOverlay");
            if (sld != null) sld.IsVisible = true;

            await Task.Delay(10);
            if (LbPolicyPopup.Child is Border border) border.Classes.Add("popupOpen");
        }
    }

    private void LanguageOption_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string lang)
        {
            var lbl = this.FindControl<TextBlock>("lblCurrentLanguage");
            if (lbl != null) lbl.Text = lang;

            MainWindow.Instance.Config.Language = lang;
            MainWindow.Instance.SaveConfig();
            MainWindow.Instance.ApplyLanguage();

            _ = ClosePopupAnimatedAsync();
        }
    }

    private void LbPolicyOption_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string policy)
        {
            string displayName = policy switch
            {
                "leastload"  => "LEAST LOAD",
                "roundrobin" => "ROUND ROBIN",
                "leastping"  => "LEAST PING",
                "random"     => "RANDOM",
                _            => policy.ToUpperInvariant()
            };

            var lbl = this.FindControl<TextBlock>("lblCurrentLbPolicy");
            if (lbl != null) lbl.Text = displayName;

            bool wasConnected = MainWindow.Instance.State.IsConnected || MainWindow.Instance.State.IsEngineRunning;
            MainWindow.Instance.Config.XrayBalancePolicy = policy;
            MainWindow.Instance.SaveConfig();

            if (wasConnected)
                MainWindow.Instance.SmartRestartXray();

            _ = ClosePopupAnimatedAsync();
        }
    }


        private static bool CheckBrackets(string json)
        {
            int openBraces = 0, closeBraces = 0;
            int openBrackets = 0, closeBrackets = 0;
            foreach (char c in json)
            {
                if (c == '{') openBraces++;
                if (c == '}') closeBraces++;
                if (c == '[') openBrackets++;
                if (c == ']') closeBrackets++;
            }
            return openBraces == closeBraces && openBrackets == closeBrackets;
        }


        private async Task ClosePopupAnimatedAsync()
        {
            if (LanguagePopup != null && LanguagePopup.IsOpen)
            {
                var popBorder = LanguagePopup.Child as Border;
                if (popBorder != null)
                {
                    popBorder.Classes.Remove("popupOpen");
                    await Task.Delay(200);
                }
                LanguagePopup.IsOpen = false;
            }

            if (LbPolicyPopup != null && LbPolicyPopup.IsOpen)
            {
                var popBorder = LbPolicyPopup.Child as Border;
                if (popBorder != null)
                {
                    popBorder.Classes.Remove("popupOpen");
                    await Task.Delay(200);
                }
                LbPolicyPopup.IsOpen = false;
            }

            bool anyPopupOpen = (LanguagePopup != null && LanguagePopup.IsOpen) || (LbPolicyPopup != null && LbPolicyPopup.IsOpen);
            if (!anyPopupOpen)
            {
                var sld = MainWindow.Instance.FindControl<global::Avalonia.Controls.Border>("LightDismissOverlay");
                if (sld != null) sld.IsVisible = false;
            }
        }

        public void ClosePopups()
        {
            _ = ClosePopupAnimatedAsync();
        }

    }
}

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
        private bool _isInitializingSettings = true;

        private bool _lbResetByPage;
        public static SettingsPage? Instance { get; private set; }
        internal static void ClearInstance() => Instance = null;
        

        public SettingsPage()
        {
            InitializeComponent();
            Instance = this;

        }

        internal void ShowSettingsPage()
        {
            var carousel = this.FindControl<Carousel>("settingsCarousel");
            if (carousel != null) carousel.SelectedIndex = 0;

            RefreshSavedConfigs();
        }

        // ── Page Sync & Localization ──

        public void SyncUI()
    {
        _isInitializingSettings = true;
        try 
        {
            RefreshCurrentLbPolicyLabel();

            var _cfg = MainWindow.Instance.Config;
            
            
            
            

            
            
            var cmbStartup = this.FindControl<global::Avalonia.Controls.ComboBox>("cmbStartupTab");
            if (cmbStartup != null) {
                int target = (_cfg.StartupTab == "AppsGames") ? 1 : 0;
                if (cmbStartup.SelectedIndex == target)
                    cmbStartup.SelectedIndex = -1;   
                cmbStartup.SelectedIndex = target;
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
            
            var cbCustomConfig1 = this.FindControl<global::Avalonia.Controls.ComboBox>("cbCustomConfig1");
            if (cbCustomConfig1 != null) cbCustomConfig1.Text = _cfg.CustomConfig1;
            
            var cbCustomConfig2 = this.FindControl<global::Avalonia.Controls.ComboBox>("cbCustomConfig2");
            if (cbCustomConfig2 != null) cbCustomConfig2.Text = _cfg.CustomConfig2;

            RefreshConfigCombos();
            
            var chkAllowOneCustomConfig = this.FindControl<global::Avalonia.Controls.CheckBox>("chkAllowOneCustomConfig");
            if (chkAllowOneCustomConfig != null) chkAllowOneCustomConfig.IsChecked = _cfg.AllowOneCustomConfig;

            var togXrayExitNode = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togXrayExitNode");
            if (togXrayExitNode != null) togXrayExitNode.IsChecked = _cfg.EnableV2rayChain;

            var togAdapterBinding = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togAdapterBinding");
            if (togAdapterBinding != null) togAdapterBinding.IsChecked = _cfg.EnableAdapterBinding;

            if (_cfg.EnableLoadBalanceAdapters
                && CrimsonX.Services.XrayConfigWriter.AdapterIps(_cfg.LoadBalanceAdapters).Count < 2)
            {
                _cfg.EnableLoadBalanceAdapters = false;
                MainWindow.Instance.RequestConfigSave();
            }

            var togLoadBalance = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togLoadBalance");
            if (togLoadBalance != null) togLoadBalance.IsChecked = _cfg.EnableLoadBalanceAdapters;

            SyncLbAdapterPanel();

            ApplyConnectionModeUI(_cfg.LastXrayMode);
        }
        finally
        {
            _isInitializingSettings = false;
        }
    }

        public void ApplyConnectionModeUI(string mode)
        {
            string wanted = CrimsonX.Services.ConnectionModes.Normalise(mode);

            this.FindControl<Button>("btnSetProxyMode")?.Classes.Remove("activeMode");
            this.FindControl<Button>("btnSetVpnMode")?.Classes.Remove("activeMode");
            this.FindControl<Button>("btnSetClearProxy")?.Classes.Remove("activeMode");

            string active = wanted switch
            {
                CrimsonX.Services.ConnectionModes.Vpn   => "btnSetVpnMode",
                CrimsonX.Services.ConnectionModes.Clear => "btnSetClearProxy",
                _                                        => "btnSetProxyMode"
            };

            this.FindControl<Button>(active)?.Classes.Add("activeMode");
        }

        private async void ConnectionMode_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button button) return;

                var main = MainWindow.Instance;
                if (main == null) return;

                await main.SetConnectionModeAsync(CrimsonX.Services.ConnectionModes.Normalise(button.Tag as string));
            }
            catch (Exception ex)
            {
                CrimsonX.Services.SimpleLogger.Log(ex);
            }
        }
        
        public void ApplyLanguage()
        {
            TextBlock? F(string name) => this.FindControl<TextBlock>(name);
            Button? B(string name)    => this.FindControl<Button>(name);
            bool fa = CrimsonX.Localization.AppStrings.IsPersian;

            var lblLanguage = F("lblCurrentLanguage");
            if (lblLanguage != null) lblLanguage.Text = CrimsonX.Localization.AppStrings.LblLanguageName;

            CrimsonX.Localization.AppStrings.Apply(F("lblSectionStartup"), CrimsonX.Localization.AppStrings.SectionStartup);
            
            CrimsonX.Localization.AppStrings.Apply(F("lblStartupTab"), CrimsonX.Localization.AppStrings.StartupTabLabel);
            CrimsonX.Localization.AppStrings.ApplyToolTip(F("lblStartupTab"), CrimsonX.Localization.AppStrings.TtStartupTab);
            var cbiHome = this.FindControl<global::Avalonia.Controls.ComboBoxItem>("cbiHome");
            if (cbiHome != null) cbiHome.Content = CrimsonX.Localization.AppStrings.NavHome;
            var cbiAppsGames = this.FindControl<global::Avalonia.Controls.ComboBoxItem>("cbiAppsGames");
            if (cbiAppsGames != null) cbiAppsGames.Content = CrimsonX.Localization.AppStrings.TabAppsGames;

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
            ApplySavedConfigsLanguage();
            CrimsonX.Localization.AppStrings.ApplyToolTip(F("lblAllowOneCustomConfig"), CrimsonX.Localization.AppStrings.OneConfigTooltip);
            var btn1 = B("btnCustomConfigsPing1");
            if (btn1 != null) btn1.Content = CrimsonX.Localization.AppStrings.PingBtn;
            var btn2 = B("btnCustomConfigsPing2");
            if (btn2 != null) btn2.Content = CrimsonX.Localization.AppStrings.PingBtn;

            var chkAllow = this.FindControl<global::Avalonia.Controls.CheckBox>("chkAllowOneCustomConfig");
            if (chkAllow != null)
            {
                if (chkAllow.Content is global::Avalonia.Controls.TextBlock tb)
                {
                    tb.Text = CrimsonX.Localization.AppStrings.AllowOneCustomConfig;
                    tb.FontFamily = fa ? new global::Avalonia.Media.FontFamily("Segoe UI") : global::Avalonia.Media.FontFamily.Default;
                    tb.FlowDirection = fa ? global::Avalonia.Media.FlowDirection.RightToLeft : global::Avalonia.Media.FlowDirection.LeftToRight;
                }
                else
                {
                    chkAllow.Content = CrimsonX.Localization.AppStrings.AllowOneCustomConfig;
                    chkAllow.FontFamily = fa ? new global::Avalonia.Media.FontFamily("Segoe UI") : global::Avalonia.Media.FontFamily.Default;
                    chkAllow.FlowDirection = fa ? global::Avalonia.Media.FlowDirection.RightToLeft : global::Avalonia.Media.FlowDirection.LeftToRight;
                }
            }
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnCustomConfigsPing1"), CrimsonX.Localization.AppStrings.PingBtn);
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnCustomConfigsPing2"), CrimsonX.Localization.AppStrings.PingBtn);
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnCustomConfigsSave1"), CrimsonX.Localization.AppStrings.Save);
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnCustomConfigsSave2"), CrimsonX.Localization.AppStrings.Save);
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnCustomConfigsSubmit"), CrimsonX.Localization.AppStrings.Submit);
            CrimsonX.Localization.AppStrings.ApplyToolTip(B("btnCustomConfigsSave1"), CrimsonX.Localization.AppStrings.SaveToPoolTooltip);
            CrimsonX.Localization.AppStrings.ApplyToolTip(B("btnCustomConfigsSave2"), CrimsonX.Localization.AppStrings.SaveToPoolTooltip);
            CrimsonX.Localization.AppStrings.ApplyToolTip(B("btnCustomConfigsSubmit"), CrimsonX.Localization.AppStrings.SubmitTooltip);
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnXraySave"), CrimsonX.Localization.AppStrings.Save);
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnXrayExitPing"), CrimsonX.Localization.AppStrings.PingBtn);
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnXrayCancel"), CrimsonX.Localization.AppStrings.Cancel);
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnDohSave"), CrimsonX.Localization.AppStrings.Save);
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnSysDnsSave"), CrimsonX.Localization.AppStrings.Save);
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnLanAuthSave"), CrimsonX.Localization.AppStrings.Save);
            
            CrimsonX.Localization.AppStrings.Apply(F("lblSectionConnection"), CrimsonX.Localization.AppStrings.SectionConnection);

            // the LEGACY CONNECTION MODES row (mode labels + their tooltips, moved out of the connect box)
            CrimsonX.Localization.AppStrings.Apply(F("lblLegacyModes"),    CrimsonX.Localization.AppStrings.SectionLegacyModes);
            CrimsonX.Localization.AppStrings.Apply(F("lblSetProxyMode"),   CrimsonX.Localization.AppStrings.ProxyMode);
            CrimsonX.Localization.AppStrings.Apply(F("lblSetVpnMode"),     CrimsonX.Localization.AppStrings.VpnMode);
            CrimsonX.Localization.AppStrings.Apply(F("lblSetClearProxy"),  CrimsonX.Localization.AppStrings.ClearProxy);

            CrimsonX.Localization.AppStrings.ApplyToolTip(B("btnSetProxyMode"),  CrimsonX.Localization.AppStrings.TtProxyMode);
            CrimsonX.Localization.AppStrings.ApplyToolTip(B("btnSetVpnMode"),    CrimsonX.Localization.AppStrings.TtVpnMode);
            CrimsonX.Localization.AppStrings.ApplyToolTip(B("btnSetClearProxy"), CrimsonX.Localization.AppStrings.TtClearProxy);
            
            var tbCustomXray = this.FindControl<TextBlock>("lblCustomXrayExit");
            CrimsonX.Localization.AppStrings.Apply(tbCustomXray, CrimsonX.Localization.AppStrings.CustomXrayExit);
            CrimsonX.Localization.AppStrings.ApplyToolTip(tbCustomXray, CrimsonX.Localization.AppStrings.TtCustomXray);

            var tbAdapterBinding = this.FindControl<TextBlock>("lblAdapterBindingTitle");
            CrimsonX.Localization.AppStrings.Apply(tbAdapterBinding, CrimsonX.Localization.AppStrings.AdapterBinding);
            CrimsonX.Localization.AppStrings.ApplyToolTip(tbAdapterBinding, CrimsonX.Localization.AppStrings.TtAdapterBinding);
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnScanAdapters"), CrimsonX.Localization.AppStrings.ScanAdapters);

            ApplyLoadBalanceLanguage();
            
            var tbDnsSetting = this.FindControl<TextBlock>("lblDnsSettingTitle");
            CrimsonX.Localization.AppStrings.Apply(tbDnsSetting, CrimsonX.Localization.AppStrings.DnsSettings);
            CrimsonX.Localization.AppStrings.ApplyToolTip(tbDnsSetting, CrimsonX.Localization.AppStrings.TtDnsSettings);

            
            var tbAllowLan = this.FindControl<TextBlock>("lblAllowLanSetting");
            CrimsonX.Localization.AppStrings.Apply(tbAllowLan, CrimsonX.Localization.AppStrings.AllowLan);
            CrimsonX.Localization.AppStrings.ApplyToolTip(tbAllowLan, CrimsonX.Localization.AppStrings.TtAllowLan);
            CrimsonX.Localization.AppStrings.Apply(this.FindControl<TextBlock>("lblLanAuthTitle"), CrimsonX.Localization.AppStrings.Authentication);
            CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<global::Avalonia.Controls.TextBlock>("lblLanAuthTitle"), CrimsonX.Localization.AppStrings.TtLanAuth);

            CrimsonX.Localization.AppStrings.Apply(F("lblOutboundType"), CrimsonX.Localization.AppStrings.TypeLabel);
            CrimsonX.Localization.AppStrings.Apply(F("lblOutboundAddress"), CrimsonX.Localization.AppStrings.AddressIp);
            CrimsonX.Localization.AppStrings.Apply(F("lblOutboundPort"), CrimsonX.Localization.AppStrings.Port);
            CrimsonX.Localization.AppStrings.Apply(F("lblOutboundAuth"), CrimsonX.Localization.AppStrings.Authentication);
            CrimsonX.Localization.AppStrings.Apply(F("lblOutboundUsername"), CrimsonX.Localization.AppStrings.TunnelCredsUser);
            CrimsonX.Localization.AppStrings.Apply(F("lblOutboundPassword"), CrimsonX.Localization.AppStrings.TunnelCredsPass);
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

            CrimsonX.Localization.AppStrings.Apply(F("lblClearWorkingCache"),  CrimsonX.Localization.AppStrings.ClearWorkingCache);
            CrimsonX.Localization.AppStrings.Apply(F("lblClearFetchedCache"),  CrimsonX.Localization.AppStrings.ClearFetchedCache);
            CrimsonX.Localization.AppStrings.ApplyToolTip(F("lblClearWorkingCache"), CrimsonX.Localization.AppStrings.TtClearWorkingCache);
            CrimsonX.Localization.AppStrings.ApplyToolTip(F("lblClearFetchedCache"), CrimsonX.Localization.AppStrings.TtClearFetchedCache);

            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnClearWorkingCache"), CrimsonX.Localization.AppStrings.Clear);
            CrimsonX.Localization.AppStrings.ApplyBtn(B("btnClearFetchedCache"), CrimsonX.Localization.AppStrings.Clear);
            
            SyncUI();
        }

        // ── Custom Configs ──

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
                var cb1 = this.FindControl<global::Avalonia.Controls.ComboBox>("cbCustomConfig1");
                var cb2 = this.FindControl<global::Avalonia.Controls.ComboBox>("cbCustomConfig2");
                bool isEmpty = string.IsNullOrWhiteSpace(cb1?.Text) && string.IsNullOrWhiteSpace(cb2?.Text);

                if (isEmpty)
                {
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { tog.IsChecked = false; });
                    SetCustomConfigsExpanded(true);
                    return;
                }

                ResolveCustomConfigSlots();
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
                if (showSaveToast) MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastSavedReconnect, kind: ToastKind.Success);
                else MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
            }
            else
            {
                MainWindow.Instance.SmartRestartXray();
                if (showSaveToast) MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastSavedApplied, kind: ToastKind.Success);
                else MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastChangesApplied, kind: ToastKind.Success);
            }
        }
        else if (showSaveToast)
        {
            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastSaved, kind: ToastKind.Success);
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


    private void btnCustomConfigsSubmit_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        => SubmitCustomConfigs();

    public void SetAllowOneCustomConfig(bool on)
    {
        var chk = this.FindControl<global::Avalonia.Controls.CheckBox>("chkAllowOneCustomConfig");
        if (chk != null) chk.IsChecked = on;
    }

    public void SubmitCustomConfigsFromQuickSettings(string cfg1, string cfg2, bool allowOne)
    {
        var cb1 = this.FindControl<global::Avalonia.Controls.ComboBox>("cbCustomConfig1");
        var cb2 = this.FindControl<global::Avalonia.Controls.ComboBox>("cbCustomConfig2");
        var chk = this.FindControl<global::Avalonia.Controls.CheckBox>("chkAllowOneCustomConfig");

        if (cb1 != null) cb1.Text = cfg1;
        if (cb2 != null) cb2.Text = cfg2;
        if (chk != null) chk.IsChecked = allowOne;

        SubmitCustomConfigs();

        ShowQuickSettingsConfigs();
    }

    private void SetCustomConfigSlotText(int slot, string text)
    {
        var combo = this.FindControl<global::Avalonia.Controls.ComboBox>(slot == 2 ? "cbCustomConfig2" : "cbCustomConfig1");
        if (combo != null) combo.Text = text;
    }

    public string ImportCustomConfig(int slot, string text)
    {
        SetCustomConfigSlotText(slot, text);

        if (slot == 2) btnCustomConfigImport2_Click(this, new global::Avalonia.Interactivity.RoutedEventArgs());
        else          btnCustomConfigImport1_Click(this, new global::Avalonia.Interactivity.RoutedEventArgs());

        var combo = this.FindControl<global::Avalonia.Controls.ComboBox>(slot == 2 ? "cbCustomConfig2" : "cbCustomConfig1");
        return combo?.Text ?? "";
    }

    public void PingCustomConfig(int slot, string text)
    {
        SetCustomConfigSlotText(slot, text);

        if (slot == 2) btnCustomConfigsPing2_Click(this, new global::Avalonia.Interactivity.RoutedEventArgs());
        else          btnCustomConfigsPing1_Click(this, new global::Avalonia.Interactivity.RoutedEventArgs());
    }

    public void SaveCustomConfig(int slot, string text)
    {
        SetCustomConfigSlotText(slot, text);

        if (slot == 2) btnCustomConfigsSave2_Click(this, new global::Avalonia.Interactivity.RoutedEventArgs());
        else          btnCustomConfigsSave1_Click(this, new global::Avalonia.Interactivity.RoutedEventArgs());
    }

    public void SubmitCustomConfigs()
    {
        var chk = this.FindControl<global::Avalonia.Controls.CheckBox>("chkAllowOneCustomConfig");

        ResolveCustomConfigSlots();
        ApplyCustomConfigTitles(true);

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

    private bool _isPinging1 = false;
    private bool _isPinging2 = false;

    private static string ReasonOf(CrimsonX.Services.ConfigTestResult res) =>
        res != null && res.Reason.Length > 0
            ? " " + CrimsonX.Localization.AppStrings.InvalidConfigReason + res.Reason
            : "";

    private async void btnCustomConfigsPing1_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (_isPinging1) return;
            _isPinging1 = true;
            
            var btn = sender as global::Avalonia.Controls.Button;
            if (btn != null) btn.IsEnabled = false;

            ResolveCustomConfigSlots();
            MainWindow.Instance.RequestConfigSave();

            long ping1 = -1;
            bool timedOut1 = false;
            CrimsonX.Services.ConfigTestResult res1 = null;
            using var cts1 = new System.Threading.CancellationTokenSource(15000);
            var ct = cts1.Token;

            if (!string.IsNullOrWhiteSpace(MainWindow.Instance.Config.CustomConfig1))
            {
                res1 = await CrimsonX.Services.CustomConfigPinger.ProbeAsync(
                    MainWindow.Instance.Config.CustomConfig1, MainWindow.Instance.Config, "", "", ct);

                if (res1 != null && res1.Success) ping1 = res1.Ping;
                else timedOut1 = res1 != null && res1.TimedOut;
            }

            if (btn != null) btn.IsEnabled = true;

            bool measured1 = res1 != null && res1.Measured;
            string msg = measured1 ? res1.Text("Config 1")
                       : timedOut1 ? CrimsonX.Localization.AppStrings.CustomProxyNoResponse
                       : CrimsonX.Localization.AppStrings.InvalidConfig + ReasonOf(res1);
            MainWindow.Instance.ShowToast(msg, measured1 ? ToastKind.Success : ToastKind.Error);
            
            _isPinging1 = false;
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
            _isPinging1 = false;
            if (sender is global::Avalonia.Controls.Button b) b.IsEnabled = true;
        }
    }

    private async void btnCustomConfigsPing2_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (_isPinging2) return;
            _isPinging2 = true;
            
            var btn = sender as global::Avalonia.Controls.Button;
            if (btn != null) btn.IsEnabled = false;

            ResolveCustomConfigSlots();
            MainWindow.Instance.RequestConfigSave();

            long ping2 = -1;
            bool timedOut2 = false;
            CrimsonX.Services.ConfigTestResult res2 = null;
            using var cts2 = new System.Threading.CancellationTokenSource(15000);
            var ct = cts2.Token;

            if (!string.IsNullOrWhiteSpace(MainWindow.Instance.Config.CustomConfig2))
            {
                res2 = await CrimsonX.Services.CustomConfigPinger.ProbeAsync(
                    MainWindow.Instance.Config.CustomConfig2, MainWindow.Instance.Config, "", "", ct);

                if (res2 != null && res2.Success) ping2 = res2.Ping;
                else timedOut2 = res2 != null && res2.TimedOut;
            }

            if (btn != null) btn.IsEnabled = true;

            bool measured2 = res2 != null && res2.Measured;
            string msg = measured2 ? res2.Text("Config 2")
                       : timedOut2 ? CrimsonX.Localization.AppStrings.CustomProxyNoResponse
                       : CrimsonX.Localization.AppStrings.InvalidConfig + ReasonOf(res2);
            MainWindow.Instance.ShowToast(msg, measured2 ? ToastKind.Success : ToastKind.Error);
            
            _isPinging2 = false;
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
            _isPinging2 = false;
            if (sender is global::Avalonia.Controls.Button b) b.IsEnabled = true;
        }
    }

        // ── Adapter Binding Panel ──

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

        // ── DNS Settings Panel (DoH / System DNS) ──

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

        // ── LAN Connections & Authentication ──

        private void btnLanAuthSave_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var txtUser = this.FindControl<global::Avalonia.Controls.TextBox>("txtLanUser");
        var txtPass = this.FindControl<global::Avalonia.Controls.TextBox>("txtLanPass");
        var tog     = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togLanAuth");

        var user = txtUser?.Text?.Trim() ?? "";
        var pass = txtPass?.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(user))
        {
            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastUsernameEmpty, ToastKind.Error);
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
                MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
            else
                MainWindow.Instance.SmartRestartXray();
        }
        else
        {
            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastLanAuthSaved, kind: ToastKind.Success);
        }
    }

    private bool _lanPassVisible = false;

    private void btnLanPassEye_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var txtPass = this.FindControl<global::Avalonia.Controls.TextBox>("txtLanPass");
        var ico     = this.FindControl<global::Avalonia.Controls.PathIcon>("icoLanPassEye");
        if (txtPass == null) return;

        _lanPassVisible = !_lanPassVisible;
        txtPass.PasswordChar = _lanPassVisible ? '\0' : '\u2022';

        if (ico != null)
            ico.Data = _lanPassVisible
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

        // ── Scan Network Adapters ──

        private void btnScanAdapters_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs? e = null)
    {
        var cmb = this.FindControl<global::Avalonia.Controls.ComboBox>("cmbAdapters");
        if (cmb == null) return;
        
        cmb.Items.Clear();
        foreach (var item in AdapterItems())
        {
            cmb.Items.Add(item);
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
                MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastAdapterNoLongerAvail, ToastKind.Error);
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

        PruneLbAdapterConfig();
        RefreshLbAdapterCombos();
    }


    internal void ScanAdapters() => btnScanAdapters_Click(null, null);
        // ── Load-Balanced Adapters ──

        private const int LbAdapterMaxSlots = 4;
        private const int LbAdapterMinSlots = 2;

        internal const string DefaultAdapterPolicy = "roundrobin";

        private static readonly string[] LbPolicyTags = { "leastload", "roundrobin", "leastping", "random" };

        private global::Avalonia.Controls.ComboBox? LbCombo(int slot) =>
            this.FindControl<global::Avalonia.Controls.ComboBox>("cmbLbAdapter" + slot);

        private global::Avalonia.Controls.Grid? LbRow(int slot) =>
            this.FindControl<global::Avalonia.Controls.Grid>("rowLbAdapter" + slot);

        private int LbVisibleSlots()
        {
            int stored = MainWindow.Instance.Config.LoadBalanceAdapters?.Count ?? 0;
            return Math.Max(LbAdapterMinSlots, Math.Min(LbAdapterMaxSlots, stored));
        }

        private string[] LbSlotValues()
        {
            var values = new string[LbAdapterMaxSlots];
            for (int slot = 1; slot <= LbAdapterMaxSlots; slot++)
                values[slot - 1] = LbCombo(slot)?.SelectedItem as string ?? "";
            return values;
        }

        private static List<string> AdapterItems()
        {
            var items = new List<string>();

            foreach (var adapter in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (adapter.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

                var ipv4 = adapter.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

                if (ipv4 != null && !string.IsNullOrWhiteSpace(ipv4.Address.ToString()))
                    items.Add($"{adapter.Name} - {ipv4.Address}");
            }

            return items;
        }

        private void RefreshLbAdapterCombos()
        {
            bool wasInit = _isInitializingSettings;
            _isInitializingSettings = true;
            try
            {
                int visible = LbVisibleSlots();
                var stored  = MainWindow.Instance.Config.LoadBalanceAdapters ?? new List<string>();
                var all     = AdapterItems();

                for (int slot = 1; slot <= LbAdapterMaxSlots; slot++)
                {
                    var cmb = LbCombo(slot);
                    var row = LbRow(slot);
                    if (cmb == null || row == null) continue;

                    row.IsVisible = slot <= visible;
                    if (slot > visible)
                    {
                        cmb.ItemsSource = null;
                        continue;
                    }

                    string keep = slot <= stored.Count ? stored[slot - 1] ?? "" : "";

                    var taken = new List<string>();
                    for (int other = 1; other <= visible; other++)
                    {
                        if (other == slot) continue;
                        string value = other <= stored.Count ? stored[other - 1] ?? "" : "";
                        if (!string.IsNullOrWhiteSpace(value)) taken.Add(value);
                    }

                    var items = all.Where(a => !taken.Contains(a)).ToList();
                    cmb.ItemsSource  = items;
                    cmb.SelectedItem = items.Contains(keep) ? keep : null;
                }

                var add = this.FindControl<global::Avalonia.Controls.Button>("btnLbAddAdapter");
                if (add != null) add.IsVisible = visible < LbAdapterMaxSlots;
            }
            finally
            {
                _isInitializingSettings = wasInit;
            }
        }

        private void PruneLbAdapterConfig()
        {
            var list = MainWindow.Instance.Config.LoadBalanceAdapters;
            if (list == null || list.Count == 0) return;

            var available = AdapterItems();
            bool changed  = false;

            for (int i = 0; i < list.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(list[i]) || available.Contains(list[i])) continue;
                list[i] = "";
                changed = true;
            }

            if (changed) MainWindow.Instance.RequestConfigSave();
        }

        private void StoreLbSlot(int slot, string picked)
        {
            var cfg  = MainWindow.Instance.Config;
            var list = cfg.LoadBalanceAdapters ??= new List<string>();

            while (list.Count < LbVisibleSlots()) list.Add("");

            if (slot < 1 || slot > list.Count) return;
            if (list[slot - 1] == picked) return;

            list[slot - 1] = picked;
            MainWindow.Instance.RequestConfigSave();
        }

        private void btnLbScanAdapters_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => ScanAdapters();

        private void btnLbAdapterAdd_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            var cfg  = MainWindow.Instance.Config;
            var list = cfg.LoadBalanceAdapters ??= new List<string>();

            while (list.Count < LbVisibleSlots()) list.Add("");
            if (list.Count >= LbAdapterMaxSlots) return;

            list.Add("");
            MainWindow.Instance.RequestConfigSave();

            RefreshLbAdapterCombos();
        }

        private void btnLbAdapterRemove_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is not global::Avalonia.Controls.Button btn || btn.Tag is not string slotText) return;
            if (!int.TryParse(slotText, out int slot) || slot <= LbAdapterMinSlots) return;

            var cfg  = MainWindow.Instance.Config;
            var list = cfg.LoadBalanceAdapters ??= new List<string>();
            if (slot > list.Count) return;

            bool hadPick = !string.IsNullOrWhiteSpace(list[slot - 1]);

            list.RemoveAt(slot - 1);
            MainWindow.Instance.RequestConfigSave();

            RefreshLbAdapterCombos();

            if (hadPick && MainWindow.Instance.State.IsEngineRunning)
                MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
        }

        private void cmbLbAdapter_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
        {
            if (_isInitializingSettings) return;
            if (sender is not global::Avalonia.Controls.ComboBox cmb) return;

            string name = cmb.Name ?? "";
            if (!int.TryParse(name.Replace("cmbLbAdapter", ""), out int slot)) return;

            string picked = cmb.SelectedItem as string ?? "";
            StoreLbSlot(slot, picked);

            RefreshLbAdapterCombos();

            if (!string.IsNullOrWhiteSpace(picked) && MainWindow.Instance.State.IsEngineRunning)
                MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
        }

        private void cmbLbPolicy_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
        {
            if (_isInitializingSettings) return;
            if (sender is not global::Avalonia.Controls.ComboBox cmb) return;

            int index = cmb.SelectedIndex;
            if (index < 0 || index >= LbPolicyTags.Length) return;

            string policy = LbPolicyTags[index];
            if (MainWindow.Instance.Config.AdapterBalancePolicy == policy) return;

            bool wasRunning = MainWindow.Instance.State.IsConnected || MainWindow.Instance.State.IsEngineRunning;

            MainWindow.Instance.Config.AdapterBalancePolicy = policy;
            MainWindow.Instance.SaveConfig();

            if (wasRunning) MainWindow.Instance.SmartRestartXray();
        }

        private int AdapterPolicyIndex()
        {
            int index = Array.IndexOf(LbPolicyTags, MainWindow.Instance.Config.AdapterBalancePolicy ?? "");
            return index >= 0 ? index : Math.Max(0, Array.IndexOf(LbPolicyTags, DefaultAdapterPolicy));
        }

        internal void RefreshCurrentLbPolicyLabel()
        {
            var lbl = this.FindControl<global::Avalonia.Controls.TextBlock>("lblCurrentLbPolicy");
            if (lbl != null)
                lbl.Text = CrimsonX.Localization.AppStrings.LoadBalancePolicyName(MainWindow.Instance.Config.XrayBalancePolicy);
        }

        private void ExpandLbPanel(bool open)
        {
            var pan       = this.FindControl<global::Avalonia.Controls.Border>("panLoadBalance");
            var ico       = this.FindControl<global::Avalonia.Controls.PathIcon>("icoLoadBalanceExpander");
            var panToggle = this.FindControl<global::Avalonia.Controls.Border>("panLoadBalanceToggle");
            var btnToggle = this.FindControl<global::Avalonia.Controls.Button>("btnLoadBalanceToggle");
            if (pan == null) return;

            pan.MaxHeight = open ? 320 : 0;
            pan.Opacity   = open ? 1 : 0;

            if (ico != null) ico.RenderTransform = new global::Avalonia.Media.RotateTransform(open ? 180 : 0);
            if (panToggle != null) panToggle.CornerRadius = open ? new global::Avalonia.CornerRadius(8, 8, 0, 0) : new global::Avalonia.CornerRadius(8);
            if (btnToggle != null) btnToggle.CornerRadius = open ? new global::Avalonia.CornerRadius(8, 8, 0, 0) : new global::Avalonia.CornerRadius(8);

            if (open) RefreshLbAdapterCombos();
        }

        private void CollapseAdapterBindingPanel()
        {
            var pan = this.FindControl<global::Avalonia.Controls.Border>("panAdapterBinding");
            var ico = this.FindControl<global::Avalonia.Controls.PathIcon>("icoAdapterBindingExpander");
            var panToggle = this.FindControl<global::Avalonia.Controls.Border>("panAdapterBindingToggle");
            var btnToggle = this.FindControl<global::Avalonia.Controls.Button>("btnAdapterBindingToggle");

            if (pan != null)
            {
                pan.MaxHeight = 0;
                pan.Opacity   = 0;
            }

            if (ico != null) ico.RenderTransform = new global::Avalonia.Media.RotateTransform(0);
            if (panToggle != null) panToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
            if (btnToggle != null) btnToggle.CornerRadius = new global::Avalonia.CornerRadius(8);
        }

        private void btnLoadBalanceToggle_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            var src = e.Source as global::Avalonia.Controls.Control;
            while (src != null)
            {
                if (src.Name == "togLoadBalance") return;
                src = src.Parent as global::Avalonia.Controls.Control;
            }

            var pan = this.FindControl<global::Avalonia.Controls.Border>("panLoadBalance");
            ExpandLbPanel(pan == null || pan.MaxHeight == 0);
        }

        private void togLoadBalance_IsCheckedChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_isInitializingSettings) return;

            var tog = sender as global::Avalonia.Controls.ToggleSwitch;
            if (tog == null) return;

            var cfg = MainWindow.Instance.Config;

            if (tog.IsChecked == true)
            {
                _lbResetByPage = false;

                if (CrimsonX.Services.XrayConfigWriter.AdapterIps(cfg.LoadBalanceAdapters).Count < 2)
                {
                    _lbResetByPage = true;
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { tog.IsChecked = false; });
                    ExpandLbPanel(true);
                    MainWindow.Instance.ShowToast(
                        CrimsonX.Localization.AppStrings.ToastLoadBalanceNeedsTwoAdapters, ToastKind.Error);
                    return;
                }

                if (cfg.EnableAdapterBinding)
                {
                    cfg.EnableAdapterBinding = false;
                    var bindTog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togAdapterBinding");
                    if (bindTog != null) global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { bindTog.IsChecked = false; });
                    CollapseAdapterBindingPanel();
                }

                if (!cfg.EnableLoadBalanceAdapters)
                {
                    cfg.EnableLoadBalanceAdapters = true;
                    MainWindow.Instance.RequestConfigSave();
                    if (MainWindow.Instance.State.IsEngineRunning)
                        MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
                }

            }
            else
            {
                if (_lbResetByPage)
                {
                    _lbResetByPage = false;
                    return;
                }

                if (cfg.EnableLoadBalanceAdapters)
                {
                    cfg.EnableLoadBalanceAdapters = false;
                    MainWindow.Instance.RequestConfigSave();
                    if (MainWindow.Instance.State.IsEngineRunning)
                        MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
                }

                ExpandLbPanel(false);
            }
        }

        internal void SyncLbAdapterPanel()
        {
            var cfg = MainWindow.Instance.Config;

            RefreshLbAdapterCombos();

            var policy = this.FindControl<global::Avalonia.Controls.ComboBox>("cmbLbPolicy");
            if (policy != null)
            {
                if (policy.ItemsSource == null)
                    policy.ItemsSource = LbPolicyTags.Select(CrimsonX.Localization.AppStrings.LoadBalancePolicyName).ToList();

                policy.SelectedIndex = AdapterPolicyIndex();
            }

            RefreshCurrentLbPolicyLabel();
            ExpandLbPanel(false);
        }

        private void ApplyLoadBalanceLanguage()
        {
            bool wasInit = _isInitializingSettings;
            _isInitializingSettings = true;
            try
            {
                var title = this.FindControl<TextBlock>("lblLoadBalanceTitle");
                CrimsonX.Localization.AppStrings.Apply(title, CrimsonX.Localization.AppStrings.LoadBalanceAdaptersTitle);
                CrimsonX.Localization.AppStrings.ApplyToolTip(title, CrimsonX.Localization.AppStrings.TtLoadBalanceAdapters);

                CrimsonX.Localization.AppStrings.Apply(this.FindControl<TextBlock>("lblLbPolicyTitle"), CrimsonX.Localization.AppStrings.LoadBalancePolicyTitle);
                CrimsonX.Localization.AppStrings.ApplyBtn(this.FindControl<Button>("btnLbAddAdapter"), CrimsonX.Localization.AppStrings.LoadBalanceAddAdapter);
                CrimsonX.Localization.AppStrings.ApplyBtn(this.FindControl<Button>("btnLbScanAdapters"), CrimsonX.Localization.AppStrings.OverlayScanAdapters);

                for (int slot = 1; slot <= LbAdapterMaxSlots; slot++)
                {
                    CrimsonX.Localization.AppStrings.Apply(this.FindControl<TextBlock>("lblLbAdapter" + slot), CrimsonX.Localization.AppStrings.AdapterSlot(slot));
                    CrimsonX.Localization.AppStrings.ApplyToolTip(this.FindControl<Button>("btnLbRemove" + slot), CrimsonX.Localization.AppStrings.TtLbRemoveAdapter);
                }

                var policy = this.FindControl<global::Avalonia.Controls.ComboBox>("cmbLbPolicy");
                if (policy != null)
                {
                    policy.ItemsSource = LbPolicyTags.Select(CrimsonX.Localization.AppStrings.LoadBalancePolicyName).ToList();
                    policy.SelectedIndex = AdapterPolicyIndex();
                    CrimsonX.Localization.AppStrings.ApplyToolTip(policy, CrimsonX.Localization.AppStrings.TtAdapterBalancePolicy);
                }

                RefreshCurrentLbPolicyLabel();
            }
            finally
            {
                _isInitializingSettings = wasInit;
            }
        }




        // ── System DNS Save ──

        private void btnSysDnsSave_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var txtPrimary   = this.FindControl<global::Avalonia.Controls.TextBox>("txtSysDnsPrimary");
        var txtSecondary = this.FindControl<global::Avalonia.Controls.TextBox>("txtSysDnsSecondary");
        var tog          = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togSysDns");

        var primary   = txtPrimary?.Text?.Trim()   ?? "";
        var secondary = txtSecondary?.Text?.Trim() ?? "";

        if (!CrimsonX.Services.DnsService.IsValidIpv4(primary))
        {
            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastInvalidDnsPrimary, ToastKind.Error);
            return;
        }
        if (!string.IsNullOrWhiteSpace(secondary) && !CrimsonX.Services.DnsService.IsValidIpv4(secondary))
        {
            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastInvalidDnsSecondary, ToastKind.Error);
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

        // ── Custom exit node: label, apply, validate, pool storage ──────────────────────────────

        // ── Custom exit node: presentation (the shared ConfigBoxPresenter) ───────────────────

        internal const string ExitNodeBoxKey = "exit-node";
        internal const string CustomConfigSlot1Key = "custom-config-1";
        internal const string CustomConfigSlot2Key = "custom-config-2";
        internal const string SavedConfigImportKey = "saved-config-import";

        internal static string SlotBoxKey(int slot) => slot == 2 ? CustomConfigSlot2Key : CustomConfigSlot1Key;

        internal const string QuickConfigSlot1Key = "quick-custom-config-1";
        internal const string QuickConfigSlot2Key = "quick-custom-config-2";

        internal static string QuickSlotBoxKey(int slot) => slot == 2 ? QuickConfigSlot2Key : QuickConfigSlot1Key;

        private ConfigBoxPresenter? _configBoxes;

        internal ConfigBoxPresenter ConfigBoxes
            => _configBoxes ??= new ConfigBoxPresenter(ReadConfigBoxRaw, WriteConfigBoxRaw, ConfigBoxTitle);

        private static string ReadConfigBoxRaw(string key)
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return "";

            return key switch
            {
                ExitNodeBoxKey       => cfg.V2rayChainJson ?? "",
                CustomConfigSlot1Key => cfg.CustomConfig1 ?? "",
                CustomConfigSlot2Key => cfg.CustomConfig2 ?? "",
                QuickConfigSlot1Key  => cfg.CustomConfig1 ?? "",
                QuickConfigSlot2Key  => cfg.CustomConfig2 ?? "",
                _                    => ""
            };
        }

        private static void WriteConfigBoxRaw(string key, string raw)
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null) return;

            switch (key)
            {
                case ExitNodeBoxKey:       cfg.V2rayChainJson = raw; break;
                case CustomConfigSlot1Key: cfg.CustomConfig1  = raw; break;
                case CustomConfigSlot2Key: cfg.CustomConfig2  = raw; break;
                case QuickConfigSlot1Key:  cfg.CustomConfig1  = raw; break;
                case QuickConfigSlot2Key:  cfg.CustomConfig2  = raw; break;
            }
        }

        internal static string ConfigBoxTitle(string raw)
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg != null)
            {
                string stored = CrimsonX.Services.AppCustomConfigStore.LabelFor(cfg, raw);
                if (stored.Length > 0 && !string.Equals(stored, raw, StringComparison.Ordinal)) return stored;
            }

            return CrimsonX.Services.ConfigConverter.LabelFor(raw);
        }

        private void ShowExitNodeRaw(string raw) => ConfigBoxes.Show(ExitNodeBoxKey, raw);

        private static void ApplyExitNodeChange() => MainWindow.Instance?.SmartRestartXray();

        private void StoreExitNodeInPool(CrimsonX.Models.AppConfig cfg, string raw)
        {
            try
            {
                switch (CrimsonX.Services.AppCustomConfigStore.Store(cfg, raw, out string label))
                {
                    case CrimsonX.Services.CustomConfigSaveResult.Saved:
                    case CrimsonX.Services.CustomConfigSaveResult.Updated:
                        MainWindow.Instance?.ShowToast($"{CrimsonX.Localization.AppStrings.ToastCustomProxySaved}: {label}", ToastKind.Success);
                        RefreshSavedConfigs();
                        RefreshConfigCombos();
                        break;

                    case CrimsonX.Services.CustomConfigSaveResult.PoolFull:
                        MainWindow.Instance?.ShowToast(CrimsonX.Localization.AppStrings.ToastCustomProxyPoolFull, ToastKind.Error);
                        break;
                }
            }
            catch (Exception ex)
            {
                CrimsonX.Services.SimpleLogger.Log(ex);
            }
        }

        private static async Task<bool> ValidateTunnelExitNodeAsync(CrimsonX.Models.AppConfig cfg, CrimsonX.Services.TunnelParseResult tunnel)
        {
            if (!await CrimsonX.Services.TunnelCredentialResolver.ApplyAsync(tunnel))
            {
                MainWindow.Instance?.ShowToast(CrimsonX.Localization.AppStrings.ToastTunnelNeedsCredentials, ToastKind.Error);
                return false;
            }

            if (!string.Equals(cfg.LastXrayMode, "VPN Mode", StringComparison.OrdinalIgnoreCase))
                MainWindow.Instance?.ShowToast(CrimsonX.Localization.AppStrings.ToastExitNodeNeedsVpnMode, ToastKind.Error);

            return true;
        }

        private async Task ApplyExitNodeRawAsync(string raw)
        {
            var cfg = MainWindow.Instance?.Config;
            if (cfg == null || string.IsNullOrWhiteSpace(raw)) return;

            raw = raw.Trim();

            if (CrimsonX.Services.TunnelConfigParser.TryParse(raw, out var tunnel) && tunnel.Success)
            {
                if (!await ValidateTunnelExitNodeAsync(cfg, tunnel))
                {
                    cfg.V2rayChainJson   = raw;
                    cfg.EnableV2rayChain = false;

                    var authTog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togXrayExitNode");
                    if (authTog != null) authTog.IsChecked = false;

                    ShowExitNodeRaw(raw);
                    MainWindow.Instance?.RequestConfigSave();

                    CrimsonX.Services.SimpleLogger.Log("[ExitNode] Saved without credentials; the exit node stays off until they are provided.");
                    return;
                }
            }
            else
            {
                bool singboxExit = CrimsonX.Services.ExitNodeChain.IsPlainTcpVless(raw);

                var verdict = await CrimsonX.Services.ConfigIntake.AcceptAsync(
                    raw, cfg,
                    singboxExit ? CrimsonX.Services.ConfigTarget.Singbox : CrimsonX.Services.ConfigTarget.Xray,
                    CrimsonX.Localization.AppStrings.PaneExitNode);

                if (!verdict.Accepted)
                {
                    MainWindow.Instance?.ShowToast(verdict.Toast, ToastKind.Error);
                    return;
                }

                if (verdict.Toast.Length > 0) MainWindow.Instance?.ShowToast(verdict.Toast, ToastKind.Error);

                raw = verdict.Raw;

                if (singboxExit)
                    CrimsonX.Services.SimpleLogger.Log("[ExitNode] A plain-TCP vless exit node is carried by sing-box, not xray.");
            }

            cfg.V2rayChainJson   = raw;
            cfg.EnableV2rayChain = true;

            var tog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togXrayExitNode");
            if (tog != null) tog.IsChecked = true;

            ShowExitNodeRaw(raw);
            MainWindow.Instance?.RequestConfigSave();
            ApplyExitNodeChange();
        }

        private void XrayExitNode_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
        {
            if (_isInitializingSettings || _suppressConfigComboSync || ConfigBoxes.Suppressed) return;
            if (sender is not global::Avalonia.Controls.ComboBox cb || cb.SelectedIndex <= 0) return;

            int index = cb.SelectedIndex - 1;
            if (index >= _configComboEntries.Count) return;

            _ = ApplyExitNodeRawAsync(_configComboEntries[index].Raw);
        }

        private async void btnXrayExitImport_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            string text = await PickConfigFileTextAsync();
            if (text.Length > 0) await ApplyExitNodeRawAsync(text);
        }

        private bool _isPingingExitNode;

        private async void btnXrayExitPing_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_isPingingExitNode) return;

            var cfg = MainWindow.Instance?.Config;
            var btn = sender as global::Avalonia.Controls.Button;
            string raw = (cfg?.V2rayChainJson ?? "").Trim();
            if (cfg == null || raw.Length == 0) return;

            _isPingingExitNode = true;
            if (btn != null) btn.Content = "…";

            try
            {
                string adapterName = cfg.EnableAdapterBinding ? cfg.SelectedAdapterName : "";
                string adapterIp   = CrimsonX.Services.XrayConfigWriter.ProbeSendThrough(cfg);

                var result = await CrimsonX.Services.CustomConfigPinger.ProbeAsync(
                    raw, cfg, adapterName, adapterIp, CancellationToken.None);

                if (btn != null)
                {
                    btn.Content = result.Success
                        ? (result.Ping > 0 ? result.Ping + "ms" : "OK")
                        : "FAIL";
                    await Task.Delay(2200);
                }
            }
            catch (Exception ex)
            {
                CrimsonX.Services.SimpleLogger.Log(ex);
            }
            finally
            {
                if (btn != null) btn.Content = CrimsonX.Localization.AppStrings.PingBtn;
                _isPingingExitNode = false;
            }
        }

        // ── Custom Xray Exit Node Panel ──

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
        var tog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togXrayExitNode");

        if (pan != null && ico != null && tog != null)
        {
            if (pan.MaxHeight == 0)
            {
                ShowExitNodeRaw(MainWindow.Instance.Config.V2rayChainJson);
                tog.IsChecked = MainWindow.Instance.Config.EnableV2rayChain;
                
                pan.MaxHeight = 120;
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

    private async void btnXraySave_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var tog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togXrayExitNode");

        if (MainWindow.Instance.Config != null && tog != null)
        {
            var text = (MainWindow.Instance.Config.V2rayChainJson ?? "").Trim();
            bool enable = true;
            
            if (string.IsNullOrWhiteSpace(text))
            {
                MainWindow.Instance.Config.V2rayChainJson = "";
                MainWindow.Instance.Config.EnableV2rayChain = enable;
                CrimsonX.Services.ConfigService.Save(MainWindow.Instance.Config, MainWindow.Instance.State, MainWindow.Instance.Config.CfgFile);
                
                btnXrayCancel_Click(sender, e);
                return;
            }
            
            if (CrimsonX.Services.TunnelConfigParser.TryParse(text, out var exitTunnel) && exitTunnel.Success)
            {
                if (await ValidateTunnelExitNodeAsync(MainWindow.Instance.Config, exitTunnel))
                {
                    MainWindow.Instance.Config.V2rayChainJson   = text;
                    MainWindow.Instance.Config.EnableV2rayChain = true;
                    if (tog != null) tog.IsChecked = true;

                    CrimsonX.Services.ConfigService.Save(MainWindow.Instance.Config, MainWindow.Instance.State, MainWindow.Instance.Config.CfgFile);

                    StoreExitNodeInPool(MainWindow.Instance.Config, text);
                    ShowExitNodeRaw(text);
                }

                btnXrayCancel_Click(sender, e);
                return;
            }

            try
            {
                if (!CrimsonX.Services.ConfigConverter.TryXrayOutbound(text, out _, out _, out string resolveError))
                {
                    MainWindow.Instance.ShowToast(resolveError.Length > 0 ? resolveError : CrimsonX.Localization.AppStrings.ToastConfigUnreadable, ToastKind.Error);
                    return;
                }

                string xrayError = await CrimsonX.Services.XrayConfigValidator.CheckAsync(MainWindow.Instance.Config, text);
                if (xrayError.Length > 0)
                {
                    string shortError = CrimsonX.Services.ConfigValidator.ShortReason(xrayError);
                    MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastXrayRejected + shortError, ToastKind.Error);
                    return;
                }

                MainWindow.Instance.Config.V2rayChainJson = text;
                MainWindow.Instance.Config.EnableV2rayChain = true;
                StoreExitNodeInPool(MainWindow.Instance.Config, text);
                ShowExitNodeRaw(text);
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    tog.IsChecked = true;
                });
                
                CrimsonX.Services.ConfigService.Save(MainWindow.Instance.Config, MainWindow.Instance.State, MainWindow.Instance.Config.CfgFile);
                
                btnXrayCancel_Click(sender, e);
            }
            catch (Exception ex)
            {
                CrimsonX.Services.SimpleLogger.Log(ex);
                MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastXrayRejected + " " + ex.Message, ToastKind.Error);
            }
        }
    }

        // ── Startup & System Setting Toggles ──

        
            private void CmbStartupTab_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingSettings) return;
        var cmb = sender as global::Avalonia.Controls.ComboBox;
        if (cmb != null && cmb.SelectedIndex >= 0)
        {
            string newVal = cmb.SelectedIndex == 1 ? "AppsGames" : "Home";
            CrimsonX.Services.SimpleLogger.Log("[Settings] StartupTab changed to: " + newVal);
            MainWindow.Instance.Config.StartupTab = newVal;
            MainWindow.Instance.SaveConfig();
        }
    }

    public void SetAdBlockToggle(bool enabled)
    {
        var toggle = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("btnAdBlockTog");
        if (toggle != null && toggle.IsChecked != enabled) toggle.IsChecked = enabled;
    }

    private async void SettingTog_CheckedChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
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
                    MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastTaskFailed + ex.Message, ToastKind.Error);
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
                MainWindow.Instance.SetAdBlock(val);
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
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
        }
    }

        // ── Desktop / Start-Menu Shortcuts ──

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

            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastShortcutCreated, kind: ToastKind.Success);
        }
        catch
        {
            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastShortcutFailed, ToastKind.Error);
        }
    }

        // ── Clear Cache Actions ──

        private void ClearWorkingCache_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            string cachePath = MainWindow.Instance.GetAppPath(@"Data\cache\cache.bin");
            if (System.IO.File.Exists(cachePath))
                System.IO.File.Delete(cachePath);

            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastCacheCleared, kind: ToastKind.Success);
        }
        catch
        {
            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastCacheClearFailed, ToastKind.Error);
        }
    }

    private void ClearFetchedCache_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            string cacheDir = MainWindow.Instance.GetAppPath(@"Data\cache");
            if (System.IO.Directory.Exists(cacheDir))
            {
                foreach (var file in System.IO.Directory.GetFiles(cacheDir, "worker_*.bin"))
                {
                    try { System.IO.File.Delete(file); } catch { }
                }
            }

            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastCacheCleared, kind: ToastKind.Success);
        }
        catch
        {
            MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastCacheClearFailed, ToastKind.Error);
        }
    }

        // ── Enable Toggles (Adapter / DNS / LAN / Xray) ──

        private void togAdapterBinding_IsCheckedChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isInitializingSettings) return;

        var tog = sender as global::Avalonia.Controls.ToggleSwitch;
        if (tog != null)
        {
            if (tog.IsChecked == true)
            {
                if (MainWindow.Instance.Config.EnableLoadBalanceAdapters)
                {
                    MainWindow.Instance.Config.EnableLoadBalanceAdapters = false;
                    var lbTog = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togLoadBalance");
                    if (lbTog != null) global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { lbTog.IsChecked = false; });
                    ExpandLbPanel(false);
                    MainWindow.Instance.RequestConfigSave();
                }

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
                    MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
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
                        MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastReconnectChanges);
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
                    MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastInvalidDnsPrimary, ToastKind.Error);
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { tog.IsChecked = false; });
                    return;
                }
                if (!string.IsNullOrWhiteSpace(liveSecondary) && !CrimsonX.Services.DnsService.IsValidIpv4(liveSecondary))
                {
                    MainWindow.Instance.ShowToast(CrimsonX.Localization.AppStrings.ToastInvalidDnsSecondary, ToastKind.Error);
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

        // ── Language & Load-Balance Popups ──

        private async void BtnLanguage_Click(object? sender, RoutedEventArgs e)
    {
        try
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
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
        }
    }

    

    private async void BtnLbPolicy_Click(object? sender, RoutedEventArgs e)
    {
        try
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
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
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
            bool wasConnected = MainWindow.Instance.State.IsConnected || MainWindow.Instance.State.IsEngineRunning;
            MainWindow.Instance.Config.XrayBalancePolicy = policy;
            MainWindow.Instance.SaveConfig();
            RefreshCurrentLbPolicyLabel();


            if (wasConnected)
                MainWindow.Instance.SmartRestartXray();

            _ = ClosePopupAnimatedAsync();
        }
    }


        // ── Xray JSON Validation & Popup Dismissal ──

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

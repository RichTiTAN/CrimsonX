/*
 * CrimsonX - A GUI client that runs multiple Tor instances and load-balances them.
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
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Input.Platform;
using CrimsonX.Models;
using CrimsonX.Services;
using CrimsonX.Localization;

namespace CrimsonX;

public partial class MainWindow : Window
{
    internal static MainWindow Instance { get; private set; }
    internal AppConfig Config => _cfg;
    internal AppState State => _state;
    internal void RequestSave() => RequestConfigSave();
    internal void RestartXray() => SmartRestartXray();

    private AppConfig _cfg;
    private AppState _state;
    private int? _xrayDebugPid, _sbDebugPid;
    private int? _xrayPid, _sbPid;

    private DispatcherTimer? _autoBootTimer; 
    private int _pollSelCount = 6;
    internal string _pollMode = "Proxy Mode";
    private string _pollSelBridge = "Direct";

    private string _activeBridge = "Direct";
    private int _activeEngines = 6;






    internal Models.AppState GetState() => _state;
    internal void ConnectDisconnect() => btnConnect_Click(null, new global::Avalonia.Interactivity.RoutedEventArgs());
    internal void SwitchToVpnMode()
    {
        _cfg.LastXrayMode = "VPN Mode";
        _pollMode = "VPN Mode";
        ApplyModeUI("VPN Mode");
        RequestConfigSave();
    }
    internal string GetSpeedText()
    {
        var down = this.FindControl<global::Avalonia.Controls.TextBlock>("lblDownloadSpeed")?.Text ?? "0 KB/s";
        var up = this.FindControl<global::Avalonia.Controls.TextBlock>("lblUploadSpeed")?.Text ?? "0 KB/s";
        var total = this.FindControl<global::Avalonia.Controls.TextBlock>("lblTotalData")?.Text ?? "0 MB";
        return $"⬇ {down} | ⬆ {up}\nTotal: {total}";
    }

    private bool _wasLanguagePopupOpen = false;

    private bool _wasLbPolicyPopupOpen = false;

    protected override void OnPropertyChanged(global::Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property.Name == "IsActive")
        {
            if (change.NewValue is bool isActive)
            {
                if (isActive)
                {
                    if (_wasLanguagePopupOpen && LanguagePopup != null)
                    {
                        LanguagePopup.IsOpen = true;
                    }

                    if (_wasLbPolicyPopupOpen && LbPolicyPopup != null)
                    {
                        LbPolicyPopup.IsOpen = true;
                    }
                }
                else
                {
                    if (LanguagePopup != null)
                    {
                        _wasLanguagePopupOpen = LanguagePopup.IsOpen;
                        if (LanguagePopup.IsOpen) LanguagePopup.IsOpen = false;
                    }

                    if (LbPolicyPopup != null)
                    {
                        _wasLbPolicyPopupOpen = LbPolicyPopup.IsOpen;
                        if (LbPolicyPopup.IsOpen) LbPolicyPopup.IsOpen = false;
                    }
                }
            }
        }
    }

    public MainWindow()
    {
        Instance = this;

        _cfg   = new AppConfig();
        _state = new AppState();

        _cfg.BaseDir = AppContext.BaseDirectory.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar);
        _cfg.CfgFile = System.IO.Path.Combine(_cfg.BaseDir, @"Data\multiplexer_settings.bin");
        _cfg.XrayDir = System.IO.Path.Combine(_cfg.BaseDir, @"Data\Xray");
        _cfg.SbDir   = System.IO.Path.Combine(_cfg.BaseDir, @"Data\sing_box");

        ConfigService.Load(_cfg, _state, _cfg.CfgFile);
        CrimsonX.Services.SimpleLogger.EnableLogging = _cfg.DebugMode;
        CrimsonX.Services.SimpleLogger.Log($"[Startup] CrimsonX v{Services.UpdateService.AppVersion} — Mode={_cfg.LastXrayMode}");

        InitializeComponent();
        DataContext = this;

        this.Deactivated += (s, e) => { CloseAllOverlays(); CrimsonX.Controls.AnimatedBackground.Instance?.SetFocusState(false); };
        this.Activated += (s, e) => CrimsonX.Controls.AnimatedBackground.Instance?.SetFocusState(true);

        this.LayoutUpdated += (s, e) => UpdateMiniNavUnderline();
        
        var lblVer = this.FindControl<global::Avalonia.Controls.TextBlock>("lblVersion");
        if (lblVer != null) lblVer.Text = Services.UpdateService.AppVersion;

        ApplyTheme(_cfg.ThemeColor);
        _pollMode = _cfg.LastXrayMode ?? "Proxy Mode";

        ApplyLoadedSettings();

        InitTrayIcon();
        InitNetDiag();
        InitLogClearTimer();

        

        if (!double.IsNaN(_cfg.WindowLeft) && !double.IsNaN(_cfg.WindowTop))
        {
            WindowStartupLocation = global::Avalonia.Controls.WindowStartupLocation.Manual;
            Position = new global::Avalonia.PixelPoint((int)_cfg.WindowLeft, (int)_cfg.WindowTop);
        }

        if (_cfg.StartMinimized)
        {
            WindowState = global::Avalonia.Controls.WindowState.Minimized;
        }

        bool isFirstOpen = true;
        this.Opened += (s, e) =>
        {
            if (isFirstOpen && _cfg.StartMinimized)
            {
                WindowState = global::Avalonia.Controls.WindowState.Minimized;
                if (_cfg.MinimizeToTray)
                {
                    Hide();
                }
            }
            if (isFirstOpen && _cfg.EnableAdapterBinding)
            {
                btnScanAdapters_Click(null, null);
            }
            isFirstOpen = false;
        };

        if (_cfg.AutoStart && !_state.IsFirstLaunch)
        {
            _autoBootTimer = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _autoBootTimer.Tick += (s, ev) =>
            {
                _autoBootTimer?.Stop();
                if (!_state.AbortBoot)
                    btnConnect_Click(null, new global::Avalonia.Interactivity.RoutedEventArgs());
            };
            _autoBootTimer.Start();
        }


        _ = CheckUpdateSilentAsync();
    }


    // ── Overlay & Popup Dismissal ──

    private async Task ClosePopupAnimatedAsync()
    {
        bool closeLanguage = LanguagePopup != null && LanguagePopup.IsOpen;
        bool closeLbPolicy = LbPolicyPopup != null && LbPolicyPopup.IsOpen;

        if (!closeLanguage && !closeLbPolicy) return;

        if (closeLanguage && LanguagePopup?.Child is Border lBorder) lBorder.Classes.Remove("popupOpen");
        if (closeLbPolicy && LbPolicyPopup?.Child is Border lpBorder) lpBorder.Classes.Remove("popupOpen");

        await Task.Delay(200);

        if (closeLanguage && LanguagePopup != null) LanguagePopup.IsOpen = false;
        if (closeLbPolicy && LbPolicyPopup != null) LbPolicyPopup.IsOpen = false;
        
        bool anyPopupOpen = (LanguagePopup != null && LanguagePopup.IsOpen) || (LbPolicyPopup != null && LbPolicyPopup.IsOpen);
        if (!anyPopupOpen)
        {
            var sld = this.FindControl<Border>("SettingsLightDismiss");
            if (sld != null) sld.IsVisible = false;
            
            var panSettings = this.FindControl<Border>("panSettingsOverlay");
            var panSplit = this.FindControl<Border>("panSplitOverlay");
            var panAbout = this.FindControl<Border>("panAboutOverlay");
            if ((panSettings == null || !panSettings.IsVisible) &&
                (panSplit == null || !panSplit.IsVisible) &&
                (panAbout == null || !panAbout.IsVisible))
            {
                LightDismissOverlay.IsVisible = false;
            }
        }
    }

    private void CloseAllOverlays()
    {
        Pages.SettingsPage.Instance?.ClosePopups();
        Controls.QuickSettingsPanel.Instance?.ClosePopups();
        if (LightDismissOverlay != null) LightDismissOverlay.IsVisible = false;
    }

    private void LightDismissOverlay_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        CloseAllOverlays();
    }

    private void CloseOverlay_Click(object? sender, RoutedEventArgs e)
    {
        CloseAllOverlays();
    }



    // ── Social Links & Wallet Copy ──

    private void BtnGithub_Click(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/RichTiTAN") { UseShellExecute = true })?.Dispose();
    }

    private void BtnTelegram_Click(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://t.me/itsTitanVPN") { UseShellExecute = true })?.Dispose();
    }

    private async void BtnCopyAddress_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Content is string address)
        {
            var clipboard = global::Avalonia.Controls.TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(address);
                ShowToast(CrimsonX.Localization.AppStrings.ToastAddressCopied, success: true);
            }
        }
    }

    private CancellationTokenSource? _updateCts;
    private string _remoteUpdateVersion = "0.0.0";
    private string _remoteMinUpdateVersion = "0.0.0";

    // ── Update Check & Installation ──

        private void SetUpdateUIStatus(string status)
    {
        var btnTitleUpdate = this.FindControl<global::Avalonia.Controls.Button>("btnTitleUpdate");
        if (btnTitleUpdate != null) btnTitleUpdate.Content = status;
        
        Pages.AboutPage.Instance?.SetUpdateStatus(status);
    }

    private async Task CheckUpdateSilentAsync()
    {
        try
        {
            var (remoteVer, remoteMin) = await Services.UpdateService.CheckForUpdatesAsync();
            if (remoteVer != null)
            {
                _remoteUpdateVersion = remoteVer;
                _remoteMinUpdateVersion = remoteMin ?? "0.0.0";
                var btnTitleUpdate = this.FindControl<global::Avalonia.Controls.Button>("btnTitleUpdate");
                if (btnTitleUpdate != null) btnTitleUpdate.IsVisible = true;
                
                string msg = CrimsonX.Localization.AppStrings.ToastNewUpdateAvailable;
                SetUpdateUIStatus(msg);
            }
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
        }
    }

    private async Task StartUpdateDownloadAsync()
    {
        if (_updateCts != null)
        {
            _updateCts.Cancel();
            _updateCts.Dispose();
            _updateCts = null;
            return;
        }

        if (string.IsNullOrEmpty(_remoteUpdateVersion) || _remoteUpdateVersion == "0.0.0") return;

        _updateCts = new System.Threading.CancellationTokenSource();
        var token = _updateCts.Token;

        try
        {
            await Services.UpdateService.DownloadAndInstallUpdateAsync(_remoteUpdateVersion, _cfg.BaseDir, (status) => 
            {
                SetUpdateUIStatus(status);
            }, token);

            ProxyService.SetSystemProxy(false);
            StopAllEngines(true);
            System.Environment.Exit(0);
        }
        catch (OperationCanceledException)
        {
            if (token.IsCancellationRequested)
            {
                ShowToast(CrimsonX.Localization.AppStrings.ToastUpdateCancelled);
            }
            else
            {
                ShowToast(CrimsonX.Localization.AppStrings.ToastUpdateDownloadTimeout);
            }
            string msg = CrimsonX.Localization.AppStrings.ToastNewUpdateAvailable;
            SetUpdateUIStatus(msg);
        }
        catch (Exception ex)
        {
            ShowToast(CrimsonX.Localization.AppStrings.ToastUpdateFailedPrefix + ex.Message);
            string msg = CrimsonX.Localization.AppStrings.ToastNewUpdateAvailable;
            SetUpdateUIStatus(msg);
        }
        finally
        {
            _updateCts?.Dispose();
            _updateCts = null;
        }
    }

    private async void BtnTitleUpdate_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_updateCts != null)
        {
            _updateCts.Cancel();
            _updateCts.Dispose();
            _updateCts = null;
            return;
        }

        bool isManual = Version.Parse(Services.UpdateService.AppVersion) < Version.Parse(_remoteMinUpdateVersion);
        var dialog = new Dialogs.UpdateDialog(isManual: isManual, _remoteUpdateVersion);
        var result = await dialog.ShowDialog<string>(this);
        
        if (result == "Primary")
        {
            if (isManual)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/RichTiTAN/CrimsonX/releases") { UseShellExecute = true })?.Dispose();
            else
                _ = StartUpdateDownloadAsync();
        }
        else if (result == "Secondary")
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/RichTiTAN/CrimsonX/releases") { UseShellExecute = true })?.Dispose();
        }
    }

    internal async void BtnCheckUpdate_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {

        if (_updateCts != null)
        {
            _updateCts.Cancel();
            _updateCts.Dispose();
            _updateCts = null;
            return;
        }

        if (!string.IsNullOrEmpty(_remoteUpdateVersion) && _remoteUpdateVersion != "0.0.0")
        {
            bool isManual = Version.TryParse(Services.UpdateService.AppVersion, out var localVer)
                         && Version.TryParse(_remoteMinUpdateVersion, out var remoteMinVer)
                         && localVer < remoteMinVer;
            var dialog = new Dialogs.UpdateDialog(isManual: isManual, _remoteUpdateVersion);
            var result = await dialog.ShowDialog<string>(this);
            
            if (result == "Primary")
            {
                if (isManual)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/RichTiTAN/CrimsonX/releases") { UseShellExecute = true })?.Dispose();
                else
                    _ = StartUpdateDownloadAsync();
            }
            else if (result == "Secondary")
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/RichTiTAN/CrimsonX/releases") { UseShellExecute = true })?.Dispose();
            }
            return;
        }

        Pages.AboutPage.Instance?.SetUpdateStatus(CrimsonX.Localization.AppStrings.UpdateChecking);
        _updateCts = new System.Threading.CancellationTokenSource();
        var token = _updateCts.Token;

        try
        {
            var (remoteVer, remoteMin) = await Services.UpdateService.CheckForUpdatesAsync(token);
            if (remoteVer == null)
            {
                ShowToast(CrimsonX.Localization.AppStrings.ToastLatestVersion, success: true);
                Pages.AboutPage.Instance?.SetUpdateStatus(CrimsonX.Localization.AppStrings.UpdateLatest);
                try { await Task.Delay(3000, token); } catch { }
                Pages.AboutPage.Instance?.SetUpdateStatus(CrimsonX.Localization.AppStrings.CheckForUpdates);
                _updateCts?.Dispose();
                _updateCts = null;
                return;
            }

            if (Version.TryParse(Services.UpdateService.AppVersion, out var localVer2)
             && Version.TryParse(remoteMin ?? "0.0.0", out var remoteMinVer2)
             && localVer2 < remoteMinVer2)
            {
                Pages.AboutPage.Instance?.SetUpdateStatus(CrimsonX.Localization.AppStrings.UpdateManualTitle);
                
                var dialog = new Dialogs.UpdateDialog(isManual: true, remoteVer);
                var result = await dialog.ShowDialog<string>(this);
                
                if (result == "Primary" || result == "Secondary")
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/RichTiTAN/CrimsonX/releases") { UseShellExecute = true })?.Dispose();
                }
                
                _updateCts?.Dispose();
                _updateCts = null;
                return;
            }

            _remoteUpdateVersion = remoteVer;
            var btnTitleUpdate = this.FindControl<global::Avalonia.Controls.Button>("btnTitleUpdate");
            if (btnTitleUpdate != null) btnTitleUpdate.IsVisible = true;

            string msg = CrimsonX.Localization.AppStrings.ToastNewUpdateAvailable;
            SetUpdateUIStatus(msg);

            _updateCts?.Dispose();
            _updateCts = null;

            var dialog2 = new Dialogs.UpdateDialog(isManual: false, remoteVer);
            var result2 = await dialog2.ShowDialog<string>(this);
            
            if (result2 == "Primary")
            {
                _ = StartUpdateDownloadAsync();
            }
            else if (result2 == "Secondary")
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/RichTiTAN/CrimsonX/releases") { UseShellExecute = true })?.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            if (token.IsCancellationRequested)
            {
                ShowToast(CrimsonX.Localization.AppStrings.ToastUpdateCancelled);
                Pages.AboutPage.Instance?.SetUpdateStatus(CrimsonX.Localization.AppStrings.ToastUpdateCancelled);
                try { await Task.Delay(2000); } catch { }
            }
            else
            {
                ShowToast(CrimsonX.Localization.AppStrings.ToastUpdateCheckTimeout);
            }
            Pages.AboutPage.Instance?.SetUpdateStatus(CrimsonX.Localization.AppStrings.CheckForUpdates);
        }
        catch (Exception ex)
        {
            ShowToast(CrimsonX.Localization.AppStrings.ToastUpdateFailedPrefix + ex.Message);
            Pages.AboutPage.Instance?.SetUpdateStatus(CrimsonX.Localization.AppStrings.CheckForUpdates);
        }
        finally
        {
            if (_updateCts != null)
            {
                _updateCts?.Dispose();
                _updateCts = null;
            }
        }
    }

    // ── Language Selector Popup ──

internal async void BtnLanguage_Click(object? sender, RoutedEventArgs e)
    {
        var target = sender as Control;
        bool isSelf = LanguagePopup != null && LanguagePopup.IsOpen && LanguagePopup.PlacementTarget == target;
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
            LanguagePopup.PlacementTarget  = target;
            LanguagePopup.Placement        = PlacementMode.Bottom;
            LanguagePopup.HorizontalOffset = 0;
            LanguagePopup.VerticalOffset   = 5;
            LanguagePopup.IsOpen           = true;
            
            var sld = this.FindControl<Border>("SettingsLightDismiss");
            if (sld != null) sld.IsVisible = true;
            
            await Task.Delay(10);
            if (LanguagePopup.Child is Border border) border.Classes.Add("popupOpen");
        }
    }

    internal void LanguageOption_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string lang)
        {
            var lbl = this.FindControl<TextBlock>("lblCurrentLanguage");
            if (lbl != null) lbl.Text = lang;

            _cfg.Language = lang;
            SaveConfig();
            ApplyLanguage();

            _ = ClosePopupAnimatedAsync();
        }
    }

    // ── Load-Balance Policy Popup ──

    internal async void BtnLbPolicy_Click(object? sender, RoutedEventArgs e)
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

            var sld = this.FindControl<Border>("SettingsLightDismiss");
            if (sld != null) sld.IsVisible = true;

            await Task.Delay(10);
            if (LbPolicyPopup.Child is Border border) border.Classes.Add("popupOpen");
        }
    }

    internal void LbPolicyOption_Click(object? sender, RoutedEventArgs e)
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

            bool wasConnected = _state.IsConnected || _state.IsEngineRunning;
            _cfg.XrayBalancePolicy = policy;
            SaveConfig();

            if (wasConnected)
                SmartRestartXray();

            _ = ClosePopupAnimatedAsync();
        }
    }

    private void SettingsLightDismiss_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((LanguagePopup != null && LanguagePopup.IsOpen) || (LbPolicyPopup != null && LbPolicyPopup.IsOpen))
        {
            _ = ClosePopupAnimatedAsync();
        }
    }


    // ── Localization ──

    public void ApplyLanguage()
    {
        AppStrings.SetLanguage(_cfg.Language);
        bool fa = AppStrings.IsPersian;
        
        panMainInteraction.FlowDirection = fa 
            ? global::Avalonia.Media.FlowDirection.RightToLeft 
            : global::Avalonia.Media.FlowDirection.LeftToRight;
            
        panMainInteraction.Margin = fa
            ? new global::Avalonia.Thickness(0, 20, 20, 0)
            : new global::Avalonia.Thickness(20, 20, 0, 0);

        this.FindControl<CrimsonX.Controls.NavigationBar>("navBar")?.ApplyLanguage();
        CrimsonX.Pages.SettingsPage.Instance?.ApplyLanguage();
        CrimsonX.Pages.ThemesPage.Instance?.ApplyLanguage();
        CrimsonX.Pages.AboutPage.Instance?.ApplyLanguage();
        this.FindControl<CrimsonX.Controls.QuickSettingsPanel>("quickSettings")?.ApplyLanguage();
        CrimsonX.Pages.SplitTunnelPage.Instance?.ApplyLanguage();
        this.FindControl<global::CrimsonX.Pages.AppsGamesOverlay>("overlayAppsGames")?.ApplyLanguage();

        TextBlock? F(string name) => this.FindControl<TextBlock>(name);
        Button? B(string name)    => this.FindControl<Button>(name);

        AppStrings.Apply(F("lblSidebarConnection"),  AppStrings.SectionConnection);

        AppStrings.Apply(F("lblSidebarSplitTunnel"), AppStrings.NavSplitTunneling);
        AppStrings.Apply(F("lblSidebarSettings"),    AppStrings.NavSettings);
        AppStrings.Apply(F("lblSidebarAbout"),       AppStrings.NavAbout);

        AppStrings.Apply(F("lblStatNav"), AppStrings.NavStats);
        AppStrings.Apply(F("lblLogNav"),  AppStrings.NavLogs);

        AppStrings.Apply(F("lblProxyMode"),  AppStrings.ProxyMode);
        AppStrings.Apply(F("lblVpnMode"),    AppStrings.VpnMode);
        AppStrings.Apply(F("lblClearProxy"), AppStrings.ClearProxy);

        AppStrings.ApplyToolTip(B("btnProxyMode"),  AppStrings.TtProxyMode);
        AppStrings.ApplyToolTip(B("btnVpnMode"),    AppStrings.TtVpnMode);
        AppStrings.ApplyToolTip(B("btnClearProxy"), AppStrings.TtClearProxy);

        AppStrings.ApplyToolTip(B("btnLbLeastLoad"), AppStrings.TtLbLeastLoad);
        AppStrings.ApplyToolTip(B("btnLbRoundRobin"), AppStrings.TtLbRoundRobin);
        AppStrings.ApplyToolTip(B("btnLbLeastPing"), AppStrings.TtLbLeastPing);
        AppStrings.ApplyToolTip(B("btnLbRandom"), AppStrings.TtLbRandom);
        
        var tbLbPolicy = this.FindControl<TextBlock>("lblLbPolicy");
        if (tbLbPolicy != null)
        {
            AppStrings.Apply(tbLbPolicy, AppStrings.LbPolicy);
            AppStrings.ApplyToolTip(tbLbPolicy, AppStrings.TtLbPolicy);
        }
        var btnLbPolicy = this.FindControl<Button>("btnLbPolicy");
        if (btnLbPolicy != null)
        {
            AppStrings.ApplyToolTip(btnLbPolicy, AppStrings.TtLbPolicy);
        }

        


        
        var panTimerContent = this.FindControl<StackPanel>("panTimerContent");
        if (panTimerContent != null)
        {
            panTimerContent.FlowDirection = fa 
                ? global::Avalonia.Media.FlowDirection.RightToLeft 
                : global::Avalonia.Media.FlowDirection.LeftToRight;
        }

        AppStrings.Apply(F("lblLogsStatus"),    AppStrings.LogsStatus);
        AppStrings.Apply(F("lblXrayLogHeader"), AppStrings.XrayLogHeader);
        AppStrings.Apply(F("lblConnectedFor"),  AppStrings.ConnectedFor);
        AppStrings.Apply(F("lblConnectedTo"),   AppStrings.ConnectedTo);
        var lblD = F("lblDisconnected");
        if (lblD != null) lblD.Text = AppStrings.StatusDisconnected;
        var lblLoc = F("lblCountryName");
        if (lblLoc != null && (lblLoc.Text == "Disconnected" || lblLoc.Text == "منتظر اتصال" || string.IsNullOrWhiteSpace(lblLoc.Text)))
            lblLoc.Text = AppStrings.StatusDisconnected;
        AppStrings.Apply(F("lblLocalPortLabel"), AppStrings.OpenLocalPort);
        AppStrings.Apply(F("lblLanPortLabel"), AppStrings.OpenLanPort);
        AppStrings.Apply(F("lblSessionLabel"),  AppStrings.SessionLabel);
        AppStrings.Apply(F("lblLocationLabel"), AppStrings.LocationLabel);
        AppStrings.Apply(F("lblPingLabel"),     AppStrings.PingLabel);
        AppStrings.Apply(F("lblTotalLabel"),    AppStrings.TotalLabel);
        AppStrings.Apply(F("lblDownloadLabel"), AppStrings.DownloadLabel);
        AppStrings.Apply(F("lblUploadLabel"),   AppStrings.UploadLabel);
        AppStrings.Apply(F("lblLogsDownloadLabel"), AppStrings.DownloadLabel);
        AppStrings.Apply(F("lblLogsUploadLabel"),   AppStrings.UploadLabel);

        var btnConn = this.FindControl<Button>("btnConnect");
        if (btnConn != null)
        {
            var txt = this.FindControl<TextBlock>("txtConnectBtn");
            if (txt != null)
            {
                bool connected = _state.IsConnected;
                txt.Text = connected
                    ? CrimsonX.Localization.AppStrings.StatusConnected
                    : CrimsonX.Localization.AppStrings.StatusConnect;
                txt.FlowDirection = fa
                    ? global::Avalonia.Media.FlowDirection.RightToLeft
                    : global::Avalonia.Media.FlowDirection.LeftToRight;
            }
        }

        AppStrings.Apply(F("lblSectionStartup"), AppStrings.SectionStartup, forceLtr: true);
        AppStrings.Apply(F("lblLaunchOnStartup"),  AppStrings.LaunchOnStartup);
        AppStrings.Apply(F("lblAutoConnect"), AppStrings.AutoConnect);
        AppStrings.Apply(F("lblStartMinimized"), AppStrings.StartMinimized);
        AppStrings.Apply(F("lblMinimizeToTray"), AppStrings.MinimizeToTray);

        AppStrings.ApplyToolTip(this.FindControl<TextBlock>("lblLaunchOnStartup"), AppStrings.TtLaunchOnStartup);
        AppStrings.ApplyToolTip(this.FindControl<TextBlock>("lblAutoConnect"), AppStrings.TtAutoConnect);
        AppStrings.ApplyToolTip(this.FindControl<TextBlock>("lblStartMinimized"), AppStrings.TtStartMinimized);
        AppStrings.ApplyToolTip(this.FindControl<TextBlock>("lblMinimizeToTray"), AppStrings.TtMinimizeToTray);
        AppStrings.ApplyToolTip(this.FindControl<Button>("btnRefreshPing"), AppStrings.TtPingRefresh);

        AppStrings.Apply(F("lblSectionConnection"), AppStrings.SectionConnection, forceLtr: true);

        tbLbPolicy = this.FindControl<TextBlock>("lblLbPolicy");


        var tbCustomXray = this.FindControl<TextBlock>("lblCustomXrayExit");
        AppStrings.Apply(tbCustomXray, AppStrings.CustomXrayExit);
        AppStrings.ApplyToolTip(tbCustomXray, AppStrings.TtCustomXray);
        

        var tbAdapterBinding = this.FindControl<TextBlock>("lblAdapterBindingTitle");
        AppStrings.Apply(tbAdapterBinding, AppStrings.AdapterBinding);
        AppStrings.ApplyToolTip(tbAdapterBinding, AppStrings.TtAdapterBinding);
        AppStrings.ApplyBtn(B("btnScanAdapters"), AppStrings.ScanAdapters);
        
        var tbDnsSetting = this.FindControl<TextBlock>("lblDnsSettingTitle");
        AppStrings.Apply(tbDnsSetting, AppStrings.DnsSettings);
        AppStrings.ApplyToolTip(tbDnsSetting, AppStrings.TtDnsSettings);

        var tbAdBlocker = this.FindControl<TextBlock>("lblAdBlockerSetting");
        AppStrings.Apply(tbAdBlocker, AppStrings.AdBlocker);
        AppStrings.ApplyToolTip(tbAdBlocker, AppStrings.TtAdBlocker);
        
        var tbAllowLan = this.FindControl<TextBlock>("lblAllowLanSetting");
        AppStrings.Apply(tbAllowLan, AppStrings.AllowLan);
        AppStrings.ApplyToolTip(tbAllowLan, AppStrings.TtAllowLan);
        AppStrings.Apply(this.FindControl<TextBlock>("lblLanAuthTitle"), AppStrings.Authentication);
        AppStrings.ApplyToolTip(this.FindControl<global::Avalonia.Controls.TextBlock>("lblLanAuthTitle"), AppStrings.TtLanAuth);

        AppStrings.Apply(F("lblOutboundType"), AppStrings.ProxyType);
        AppStrings.Apply(F("lblOutboundAddress"), AppStrings.AddressIp);
        AppStrings.Apply(F("lblOutboundPort"), AppStrings.Port);
        AppStrings.Apply(F("lblOutboundAuth"), AppStrings.Authentication);
        AppStrings.Apply(F("lblOutboundUsername"), AppStrings.Username);
        AppStrings.Apply(F("lblOutboundPassword"), AppStrings.Password);
        AppStrings.Apply(F("lblUpstreamDoh"), AppStrings.UpstreamDohUrl);
        AppStrings.Apply(F("lblSysDnsTitle"), AppStrings.SystemDns);
        AppStrings.ApplyToolTip(this.FindControl<global::Avalonia.Controls.TextBlock>("lblSysDnsTitle"), AppStrings.TtSystemDns);


        AppStrings.Apply(F("lblSectionSystem"),    AppStrings.SectionSystem, forceLtr: true);
        
        var tbLanguageSetting = this.FindControl<TextBlock>("lblLanguageSetting");
        AppStrings.Apply(tbLanguageSetting, AppStrings.LanguageSetting);
        AppStrings.ApplyToolTip(tbLanguageSetting, AppStrings.TtLanguage);
        
        var tbDebugMode = this.FindControl<TextBlock>("lblDebugMode");
        AppStrings.Apply(tbDebugMode, AppStrings.DebugMode);
        AppStrings.ApplyToolTip(tbDebugMode, AppStrings.TtDebugMode);
        
        AppStrings.Apply(F("lblDesktopShortcut"),  AppStrings.DesktopShortcut);
        AppStrings.Apply(F("lblStartMenuShortcut"), AppStrings.StartMenuShortcut);

        AppStrings.ApplyBtn(B("btnDesktopShortcut"), AppStrings.Create);
        AppStrings.ApplyBtn(B("btnStartMenuShortcut"), AppStrings.Create);

        AppStrings.Apply(F("lblSplitTunnelingHeader"), AppStrings.NavSplitTunneling, forceLtr: true);
        AppStrings.Apply(F("lblDomainsAndIps"), AppStrings.DomainsAndIps);
        AppStrings.Apply(F("lblApplications"), AppStrings.Applications);
        var lblSplitAppsWarning = this.FindControl<TextBlock>("lblSplitAppsWarning");
        if (lblSplitAppsWarning != null) lblSplitAppsWarning.Text = CrimsonX.Localization.AppStrings.WarningCaseSensitive;
        AppStrings.Apply(F("lblBlockedDomainsIps"), AppStrings.BlockedDomains);
        AppStrings.Apply(F("lblDirectUdpHeader"), AppStrings.SplitTunnelDirectUDP);
        AppStrings.ApplyToolTip(F("lblDirectUdpHeader"), AppStrings.SplitTunnelDirectUDPTooltip);
        AppStrings.Apply(F("lblDirectUdpDesc"), AppStrings.SplitTunnelDirectUDPDesc);
        


        var btnSplitDisabled  = this.FindControl<Button>("btnSplitDisabled");
        var btnSplitExclusive = this.FindControl<Button>("btnSplitExclusive");
        var btnSplitInclusive = this.FindControl<Button>("btnSplitInclusive");
        
        AppStrings.ApplyToolTip(btnSplitDisabled, AppStrings.TtSplitDis);
        AppStrings.ApplyToolTip(btnSplitExclusive, AppStrings.SplitExplanationExclusive);
        AppStrings.ApplyToolTip(btnSplitInclusive, AppStrings.SplitExplanationInclusive);
        
        if (btnSplitDisabled?.Content  is TextBlock tbDis) AppStrings.Apply(tbDis, AppStrings.Disabled);
        if (btnSplitExclusive?.Content is TextBlock tbEx)  AppStrings.Apply(tbEx, AppStrings.Exclusive);
        if (btnSplitInclusive?.Content is TextBlock tbIn)  AppStrings.Apply(tbIn, AppStrings.Inclusive);

        var btnToggleDomains = this.FindControl<Button>("btnToggleDomains");
        var btnToggleApps    = this.FindControl<Button>("btnToggleApps");
        var btnToggleBlock   = this.FindControl<Button>("btnToggleBlock");
        if (btnToggleDomains != null) 
            btnToggleDomains.Content = string.IsNullOrWhiteSpace(this.FindControl<TextBox>("txtSplitDomains")?.Text) ? AppStrings.Add : AppStrings.Edit;
        if (btnToggleApps    != null) 
            btnToggleApps.Content    = string.IsNullOrWhiteSpace(this.FindControl<TextBox>("txtSplitApps")?.Text) ? AppStrings.Add : AppStrings.Edit;
        if (btnToggleBlock   != null) 
            btnToggleBlock.Content   = string.IsNullOrWhiteSpace(this.FindControl<TextBox>("txtSplitBlock")?.Text) ? AppStrings.Add : AppStrings.Edit;

        var btnBrowseApp = this.FindControl<Button>("btnBrowseApp");
        if (btnBrowseApp != null) btnBrowseApp.Content = CrimsonX.Localization.AppStrings.Browse;

        Pages.AboutPage.Instance?.UpdateLocalization();
        Pages.ThemesPage.Instance?.UpdateLocalization();

        
        AppStrings.ApplyBtn(B("btnXraySave"), AppStrings.Save);
        AppStrings.ApplyBtn(B("btnXrayCancel"), AppStrings.Cancel);
        AppStrings.ApplyBtn(B("btnOutboundSave"), AppStrings.Save);
        AppStrings.ApplyBtn(B("btnOutboundCancel"), AppStrings.Cancel);
        AppStrings.ApplyBtn(B("btnDohSave"), AppStrings.Save);
        AppStrings.ApplyBtn(B("btnSysDnsSave"), AppStrings.Save);
        AppStrings.ApplyBtn(B("btnLanAuthSave"), AppStrings.Save);
        AppStrings.ApplyBtn(B("btnSaveDomains"), AppStrings.Save);
        AppStrings.ApplyBtn(B("btnCancelDomains"), AppStrings.Cancel);
        AppStrings.ApplyBtn(B("btnSaveApps"), AppStrings.Save);
        AppStrings.ApplyBtn(B("btnCancelApps"), AppStrings.Cancel);
        AppStrings.ApplyBtn(B("btnSaveBlock"), AppStrings.Save);
        AppStrings.ApplyBtn(B("btnCancelBlock"), AppStrings.Cancel);
        
        AppStrings.ApplyBtn(B("btnCaptchaSubmit"), AppStrings.Submit);
        AppStrings.ApplyBtn(B("btnCaptchaCancel"), AppStrings.Cancel);
        AppStrings.ApplyBtn(B("btnCustomSave"), AppStrings.Save);
        AppStrings.ApplyBtn(B("btnCustomCancel"), AppStrings.Cancel);




        if (_trayWidget != null)
            _trayWidget.ApplyLanguage(fa);
            
        UpdateLanPortUI();
        UpdateLocalPortUI();
        ApplyModeUI(_cfg.LastXrayMode);

        var txtConnectBtn = F("txtConnectBtn");
        var txtConnectedBtn = F("txtConnectedBtn");
        if (_state.IsConnected)
        {
            if (txtConnectedBtn != null) txtConnectedBtn.Text = AppStrings.StatusConnected;
            if (txtConnectBtn != null) txtConnectBtn.Text = AppStrings.StatusConnected;
        }
        else if (_state.IsEngineRunning)
        {
            if (txtConnectBtn != null) txtConnectBtn.Text = CrimsonX.Localization.AppStrings.StatusConnecting;
        }
        else
        {
            if (txtConnectBtn != null) txtConnectBtn.Text = AppStrings.StatusConnect;
        }
    }



    // ── Operating Mode Switching ──

    private async void Mode_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedBtn) return;
        if (clickedBtn.Name == "btnVpnMode" && _activeBridge == "snowflake" && !_cfg.EnableDirectUDP) return;
        if (_isModeHotSwapping) return;

        string newMode;
        if (clickedBtn.Name == "btnProxyMode")       newMode = "Proxy Mode";
        else if (clickedBtn.Name == "btnVpnMode")    newMode = "VPN Mode";
        else if (clickedBtn.Name == "btnClearProxy") newMode = "Clear Proxy";
        else                                         newMode = "Proxy Mode";

        string oldMode = _cfg.LastXrayMode ?? "Proxy Mode";
        if (oldMode == newMode) return;

        bool live = _state.IsEngineRunning || _state.IsConnected;
        if (live && newMode == "VPN Mode" && IsVpnAdapterInUse())
        {
            bool isFa = AppStrings.IsPersian;
            ShowToast(isFa
                ? "آداپتور VPN از قبل توسط برنامه دیگری در حال استفاده است!"
                : "VPN adapter is already in use by another app!");
            return;
        }

        _cfg.LastXrayMode = newMode;
        _pollMode = newMode;
        ApplyModeUI(newMode);
        RequestConfigSave();

        if (!live) return;

        _isModeHotSwapping = true;
        try
        {
            if (!await HotSwapConnectionModeAsync(oldMode, newMode))
            {
                _cfg.LastXrayMode = oldMode;
                _pollMode = oldMode;
                ApplyModeUI(oldMode);
                RequestConfigSave();
                await HotSwapConnectionModeAsync(newMode, oldMode);
            }
        }
        finally
        {
            _isModeHotSwapping = false;
        }
    }

    

    

    

    private async void Engines_ValueChanged(object? sender, global::Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (sender is global::Avalonia.Controls.Slider slider)
        {
            int engines = (int)slider.Value;
            var lbl = this.FindControl<TextBlock>("lblEngineCount");
            if (lbl != null) lbl.Text = engines.ToString();

            if (_activeEngines == engines) return;

            _activeEngines = engines;

            if (_state.IsEngineRunning)
                await OnEngineCountChanged(engines);
            else
            {
                RequestConfigSave();
            }
        }
    }


    // ── Apply Settings & Mode UI ──

    private void ApplyLoadedSettings()
    {
        this.FindControl<global::Avalonia.Controls.Button>("btnBridgeDirect")?.Classes.Remove("activeOpt");
        this.FindControl<global::Avalonia.Controls.Button>("btnBridgeObfs4")?.Classes.Remove("activeOpt");
        this.FindControl<global::Avalonia.Controls.Button>("btnBridgeSnowflake")?.Classes.Remove("activeOpt");
        this.FindControl<global::Avalonia.Controls.Button>("btnBridgeMeek")?.Classes.Remove("activeOpt");
        this.FindControl<global::Avalonia.Controls.Button>("btnBridgeConjure")?.Classes.Remove("activeOpt");
        this.FindControl<global::Avalonia.Controls.Button>("btnBridgeCustom")?.Classes.Remove("activeOpt");

        if (_activeBridge == "obfs4")          this.FindControl<global::Avalonia.Controls.Button>("btnBridgeObfs4")?.Classes.Add("activeOpt");
        else if (_activeBridge == "snowflake") this.FindControl<global::Avalonia.Controls.Button>("btnBridgeSnowflake")?.Classes.Add("activeOpt");
        else if (_activeBridge == "meek_lite") this.FindControl<global::Avalonia.Controls.Button>("btnBridgeMeek")?.Classes.Add("activeOpt");
        else if (_activeBridge == "conjure")   this.FindControl<global::Avalonia.Controls.Button>("btnBridgeConjure")?.Classes.Add("activeOpt");
        else if (_activeBridge == "Custom")    this.FindControl<global::Avalonia.Controls.Button>("btnBridgeCustom")?.Classes.Add("activeOpt");
        else                                   this.FindControl<global::Avalonia.Controls.Button>("btnBridgeDirect")?.Classes.Add("activeOpt");

        var txtCustomBridge = this.FindControl<global::Avalonia.Controls.TextBox>("txtCustomBridge");

        var sldEngines = this.FindControl<global::Avalonia.Controls.Slider>("sldEngines");
        if (sldEngines != null) sldEngines.Value = _activeEngines;
        var lblEngineCount = this.FindControl<global::Avalonia.Controls.TextBlock>("lblEngineCount");
        if (lblEngineCount != null) lblEngineCount.Text = _activeEngines.ToString();

        var chkLogs = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("chkLogs");
        if (chkLogs != null) chkLogs.IsChecked = _state.IsLogsOpen;

        _isInitializingSettings = true;
        
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


        var togXrayExitNode = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togXrayExitNode");
        if (togXrayExitNode != null) togXrayExitNode.IsChecked = _cfg.EnableV2rayChain;

        var togDirectUDP = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togDirectUDP");
        if (togDirectUDP != null) togDirectUDP.IsChecked = _cfg.EnableDirectUDP;


        var togAdapterBinding = this.FindControl<global::Avalonia.Controls.ToggleSwitch>("togAdapterBinding");
        var panAdapterBinding = this.FindControl<global::Avalonia.Controls.Border>("panAdapterBinding");
        var icoAdapterBindingExpander = this.FindControl<global::Avalonia.Controls.PathIcon>("icoAdapterBindingExpander");


        if (togAdapterBinding != null)
        {
            togAdapterBinding.IsChecked = _cfg.EnableAdapterBinding;
        }

        _isInitializingSettings = false;

        ApplyModeUI(_pollMode);

        UpdateLanPortUI();
        

        var langLbl = this.FindControl<TextBlock>("lblCurrentLanguage");
        if (langLbl != null) langLbl.Text = _cfg.Language;
        ApplyLanguage();
        CrimsonX.Pages.SettingsPage.Instance?.SyncUI();
        CrimsonX.Pages.SplitTunnelPage.Instance?.SyncUI();

        var lbLbl = this.FindControl<TextBlock>("lblCurrentLbPolicy");
        if (lbLbl != null)
        {
            lbLbl.Text = _cfg.XrayBalancePolicy switch
            {
                "leastload"  => "LEAST LOAD",
                "leastping"  => "LEAST PING",
                "roundrobin" => "ROUND ROBIN",
                "random"     => "RANDOM",
                "leastconn"  => "LEAST LOAD",
                "first"      => "ROUND ROBIN",
                _            => (_cfg.XrayBalancePolicy ?? "roundrobin").ToUpperInvariant()
            };
        }
    }

    private void ApplyModeUI(string mode)
    {
        this.FindControl<global::Avalonia.Controls.Button>("btnProxyMode")?.Classes.Remove("activeMode");
        this.FindControl<global::Avalonia.Controls.Button>("btnVpnMode")?.Classes.Remove("activeMode");
        this.FindControl<global::Avalonia.Controls.Button>("btnClearProxy")?.Classes.Remove("activeMode");

        var panVpnMode = this.FindControl<global::Avalonia.Controls.Panel>("panVpnMode");
        var btnVpnMode = this.FindControl<global::Avalonia.Controls.Button>("btnVpnMode");
        if (btnVpnMode != null)
        {
            if (_activeBridge == "snowflake" && !_cfg.EnableDirectUDP)
            {
                btnVpnMode.IsEnabled = false;
                btnVpnMode.Opacity = 0.3;
            }
            else
            {
                btnVpnMode.IsEnabled = true;
                btnVpnMode.Opacity = 1.0;
                if (panVpnMode != null) global::Avalonia.Controls.ToolTip.SetTip(panVpnMode, null);
            }
        }

        if (mode == "VPN Mode")       
            this.FindControl<global::Avalonia.Controls.Button>("btnVpnMode")?.Classes.Add("activeMode");
        else if (mode == "Clear Proxy") 
            this.FindControl<global::Avalonia.Controls.Button>("btnClearProxy")?.Classes.Add("activeMode");
        else                           
            this.FindControl<global::Avalonia.Controls.Button>("btnProxyMode")?.Classes.Add("activeMode");
            
        CrimsonX.Pages.SplitTunnelPage.Instance?.UpdateSplitTunnelUI();
    }




    // ── Custom Window Title Bar ──

    private void TitleBar_PointerPressed(object? sender, global::Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }



    private void Minimize_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_cfg.MinimizeToTray)
            Hide();
        else
            WindowState = WindowState.Minimized;
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosing(global::Avalonia.Controls.WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (this.WindowState == global::Avalonia.Controls.WindowState.Normal)
        {
            _cfg.WindowLeft = this.Position.X;
            _cfg.WindowTop = this.Position.Y;
            ConfigService.Save(_cfg, _state, _cfg.CfgFile);
        }

        StopAllEngines(isClosing: true);

        Dispose();
    }

    

    private System.Collections.Generic.Dictionary<global::Avalonia.Media.SolidColorBrush, (global::Avalonia.Media.Color Start, global::Avalonia.Media.Color End, System.DateTime StartTime)> _colorAnimations = new();
    private global::Avalonia.Threading.DispatcherTimer? _colorTimer;

    // ── Theme & Animation Application ──

    private void SetAnimatableBrush(string key, global::Avalonia.Media.Color color)
    {
        if (global::Avalonia.Application.Current?.Resources.TryGetValue(key, out var res) == true && res is global::Avalonia.Media.SolidColorBrush brush)
        {
            if (brush.Color == color) return;
            
            _colorAnimations[brush] = (brush.Color, color, System.DateTime.UtcNow);
            
            if (_colorTimer == null)
            {
                _colorTimer = new global::Avalonia.Threading.DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(16) };
                _colorTimer.Tick += (s, e) =>
                {
                    bool allDone = true;
                    var now = System.DateTime.UtcNow;
                    var keys = new System.Collections.Generic.List<global::Avalonia.Media.SolidColorBrush>(_colorAnimations.Keys);
                    foreach (var kvp in keys)
                    {
                        var anim = _colorAnimations[kvp];
                        var elapsed = (now - anim.StartTime).TotalMilliseconds;
                        if (elapsed >= 500)
                        {
                            kvp.Color = anim.End;
                            _colorAnimations.Remove(kvp);
                        }
                        else
                        {
                            allDone = false;
                            double t = elapsed / 500.0;
                            t = 1.0 - System.Math.Pow(1.0 - t, 3); 
                            byte a = (byte)(anim.Start.A + (anim.End.A - anim.Start.A) * t);
                            byte r = (byte)(anim.Start.R + (anim.End.R - anim.Start.R) * t);
                            byte g = (byte)(anim.Start.G + (anim.End.G - anim.Start.G) * t);
                            byte b = (byte)(anim.Start.B + (anim.End.B - anim.Start.B) * t);
                            kvp.Color = global::Avalonia.Media.Color.FromArgb(a, r, g, b);
                        }
                    }
                    if (allDone) _colorTimer.Stop();
                };
            }
            _colorTimer.Start();
        }
        else if (global::Avalonia.Application.Current != null)
        {
            global::Avalonia.Application.Current.Resources[key] = new global::Avalonia.Media.SolidColorBrush(color);
        }
    }

    internal void ApplyTheme(string themeName)
    {
        if (string.IsNullOrWhiteSpace(themeName)) themeName = "Crimson";
        global::Avalonia.Media.SolidColorBrush accent = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#B82E42"));
        global::Avalonia.Media.SolidColorBrush accentHover = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#D13A51"));
        global::Avalonia.Media.SolidColorBrush accentPressed = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#932535"));
        global::Avalonia.Media.Color glow = global::Avalonia.Media.Color.Parse("#FFE64A62");
        global::Avalonia.Media.SolidColorBrush glow1Brush = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#2B6CB0"));
        global::Avalonia.Media.SolidColorBrush glow2Brush = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#805AD5"));
        global::Avalonia.Media.SolidColorBrush glow3Brush = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#E53E3E"));
        switch (themeName)
        {
            case "Blue":
                accent = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#2B6CB0"));
                accentHover = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#3182CE"));
                accentPressed = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#2C5282"));
                glow = global::Avalonia.Media.Color.Parse("#63B3ED");
                glow1Brush = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#2B6CB0"));
                glow2Brush = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#3182CE"));
                glow3Brush = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#805AD5"));
                break;
            case "Purple":
                accent = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#6B46C1"));
                accentHover = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#805AD5"));
                accentPressed = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#553C9A"));
                glow = global::Avalonia.Media.Color.Parse("#B794F4");
                glow1Brush = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#6B46C1"));
                glow2Brush = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#805AD5"));
                glow3Brush = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#2B6CB0"));
                break;
            case "Green":
                accent = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#2F855A"));
                accentHover = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#38A169"));
                accentPressed = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#276749"));
                glow = global::Avalonia.Media.Color.Parse("#68D391");
                glow1Brush = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#2F855A"));
                glow2Brush = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#38A169"));
                glow3Brush = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#D69E2E"));
                break;
            case "Pink":
                accent = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#B83280"));
                accentHover = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#D53F8C"));
                accentPressed = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#97266D"));
                glow = global::Avalonia.Media.Color.Parse("#F687B3");
                glow1Brush = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#D53F8C"));
                glow2Brush = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#E53E3E"));
                glow3Brush = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#805AD5"));
                break;
            case "Yellow":
                accent = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#B7791F"));
                accentHover = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#D69E2E"));
                accentPressed = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#975A16"));
                glow = global::Avalonia.Media.Color.Parse("#F6E05E");
                glow1Brush = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#87BA4C"));
                glow2Brush = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#D69E2E"));
                glow3Brush = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#DD6B20"));
                break;
            case "Crimson":
            default:
                break; 
        }

        if (global::Avalonia.Application.Current != null)
        {
            global::Avalonia.Application.Current.Resources["ThemeGlow"] = glow;
            SetAnimatableBrush("ThemeAccent", accent.Color);
            SetAnimatableBrush("ThemeMutedBrush", global::Avalonia.Media.Color.FromArgb(80, accent.Color.R, accent.Color.G, accent.Color.B));
            SetAnimatableBrush("ThemeSelectionBrush", global::Avalonia.Media.Color.FromArgb(140, accent.Color.R, accent.Color.G, accent.Color.B));
            SetAnimatableBrush("ThemeAccentPointerOver", accentHover.Color);
            SetAnimatableBrush("ThemeAccentPressed", accentPressed.Color);
            SetAnimatableBrush("ThemeGlowBrush", glow);
            SetAnimatableBrush("ThemeGlow1Brush", glow1Brush.Color);
            SetAnimatableBrush("ThemeGlow2Brush", glow2Brush.Color);
            SetAnimatableBrush("ThemeGlow3Brush", glow3Brush.Color);
                        CrimsonX.Controls.AnimatedBackground.Instance?.UpdateTheme(glow1Brush.Color, glow2Brush.Color, glow3Brush.Color);
            CrimsonX.Controls.AnimatedBackground.Instance?.ApplySettings(_cfg.PauseGlows, _cfg.DisableGlows);
            SetAnimatableBrush("ToggleSwitchFillOn", accent.Color);
            SetAnimatableBrush("ToggleSwitchFillOnPointerOver", accentHover.Color);
            SetAnimatableBrush("ToggleSwitchFillOnPressed", accentPressed.Color);
            SetAnimatableBrush("SliderThumbBackground", accent.Color);
            SetAnimatableBrush("SliderThumbBackgroundPointerOver", accentHover.Color);
            SetAnimatableBrush("SliderThumbBackgroundPressed", accentPressed.Color);
            SetAnimatableBrush("SliderTrackValueFill", accent.Color);
            SetAnimatableBrush("SliderTrackValueFillPointerOver", accentHover.Color);
            SetAnimatableBrush("SliderTrackValueFillPressed", accentPressed.Color);
        }

        if (_state != null && _state.IsEngineRunning)
        {
            var txtConnectBtn = this.FindControl<global::Avalonia.Controls.TextBlock>("txtConnectBtn");
            if (txtConnectBtn != null && txtConnectBtn.Text == CrimsonX.Localization.AppStrings.StatusConnected) 
                txtConnectBtn.Foreground = new global::Avalonia.Media.SolidColorBrush(glow);
        }

        if (this.Resources.ContainsKey($"Theme{themeName}Brush"))
        {
            this.Resources["ThemeCurrentBrush"] = this.Resources[$"Theme{themeName}Brush"];
        }
    }

    


    
        public void UpdateGlobalAnimations()
    {
        bool glowsAllowed = !_cfg.PauseGlows && !_cfg.DisableGlows;
        
        if (glowsAllowed)
        {
            if (!this.Classes.Contains("anim-glows")) this.Classes.Add("anim-glows");
        }
        else
        {
            this.Classes.Remove("anim-glows");
        }

        CrimsonX.Controls.AnimatedBackground.Instance?.ApplySettings(
            _cfg.PauseGlows, 
            _cfg.DisableGlows
        );
    }

    private string _previousNav = "Home";

    // ── Tab Navigation ──

    private void NavBar_NavChanged(object? sender, string viewName)
    {
        _previousNav = viewName;
        var carousel = this.FindControl<global::Avalonia.Controls.Carousel>("MainCarousel");
        if (carousel == null) return;

        if (viewName == "Themes")
        {
            if (!this.Classes.Contains("themes-active")) this.Classes.Add("themes-active");
        }
        else
        {
            this.Classes.Remove("themes-active");
        }

        switch (viewName)
        {
            case "Home": carousel.SelectedIndex = 0; RestoreHomeMiniNav(); break;
            case "SplitTunneling": carousel.SelectedIndex = 1; break;
            case "Settings": carousel.SelectedIndex = 2; break;
            case "Themes": carousel.SelectedIndex = 3; break;
            case "About": carousel.SelectedIndex = 4; break;
            case "AppsGames": carousel.SelectedIndex = 5; break;
        }

        var panTabDarken = this.FindControl<global::Avalonia.Controls.Border>("panTabDarken");
        if (panTabDarken != null)
        {
            panTabDarken.Opacity = (carousel.SelectedIndex == 1 || carousel.SelectedIndex == 2 || carousel.SelectedIndex == 3 || carousel.SelectedIndex == 4 || carousel.SelectedIndex == 5) ? 1 : 0;
        }
        
        if (viewName == "AppsGames")
{
    var page = this.FindControl<global::CrimsonX.Pages.AppsGamesOverlay>("overlayAppsGames");
    if (page != null) page.LoadRules();
}
if (viewName == "SplitTunneling")
{
    var page = this.FindControl<global::CrimsonX.Pages.SplitTunnelPage>("pageSplit");
    if (page != null) page.SyncUI();
}
    }
}




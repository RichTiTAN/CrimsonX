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
    private bool _closeDeferred;

    private DispatcherTimer? _autoBootTimer; 
    internal string _pollMode = "Proxy Mode";

    internal Models.AppState GetState() => _state;
    internal void SwitchToVpnMode()
    {
        _cfg.LastXrayMode = CrimsonX.Services.ConnectionModes.Vpn;
        _pollMode = CrimsonX.Services.ConnectionModes.Vpn;
        ApplyModeUI(CrimsonX.Services.ConnectionModes.Vpn);
        RequestConfigSave();
    }
    internal string GetSpeedText()
    {
        var down = this.FindControl<global::Avalonia.Controls.TextBlock>("lblDownloadSpeed")?.Text ?? "0 KB/s";
        var up = this.FindControl<global::Avalonia.Controls.TextBlock>("lblUploadSpeed")?.Text ?? "0 KB/s";
        var total = this.FindControl<global::Avalonia.Controls.TextBlock>("lblTotalData")?.Text ?? "0 MB";
        return $"⬇ {down} | ⬆ {up}\nTotal: {total}";
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
        CrimsonX.Services.BackgroundTask.Run("engine report", () => CrimsonX.Services.EngineReport.AnnounceAsync(_cfg));
        CrimsonX.Services.GeneratedArtifacts.RunStartupCleanup();
        CrimsonX.Services.SecureJsonStore.MigrateLegacyStores();
        CrimsonX.Services.BackgroundTask.Run("dns heal", () => CrimsonX.Services.SystemDnsService.HealFromDiskAsync());
        InitializeComponent();
        var startupNav = this.FindControl<CrimsonX.Controls.NavigationBar>("navBar");
        startupNav?.SetAdBlockerState(_cfg.EnableAdBlock);
        if (_cfg.StartupTab == "AppsGames")
        {
            var navBar = this.FindControl<CrimsonX.Controls.NavigationBar>("navBar");
            if (navBar != null)
            {
                navBar.SelectTab("AppsGames");
            }
        }
        DataContext = this;
        this.Deactivated += (s, e) => { CloseAllOverlays(); CrimsonX.Controls.AnimatedBackground.Instance?.SetFocusState(false); };
        this.Activated += (s, e) => CrimsonX.Controls.AnimatedBackground.Instance?.SetFocusState(true);
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
                Pages.SettingsPage.Instance?.ScanAdapters();
            }
            isFirstOpen = false;
        };
        if (_cfg.AutoStart && !_state.IsFirstLaunch)
        {
            bool fired = false;
            void FireAutoConnect()
            {
                if (fired) return;
                fired = true;
                LayoutUpdated -= OnFirstLayout;
                _autoBootTimer?.Stop();
                if (!_state.AbortBoot)
                    Dispatcher.UIThread.Post(
                        () => BeginAutoConnect(),
                        DispatcherPriority.Background);
            }
            void OnFirstLayout(object? s, EventArgs e)
            {
                LayoutUpdated -= OnFirstLayout;
                DispatcherTimer.RunOnce(FireAutoConnect, TimeSpan.FromMilliseconds(250));
            }
            LayoutUpdated += OnFirstLayout;
            _autoBootTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
            _autoBootTimer.Tick += (s, ev) => FireAutoConnect();
            _autoBootTimer.Start();
        }
        CrimsonX.Services.BackgroundTask.Run("update check", () => CheckUpdateSilentAsync());
    }

    // ── Overlay & Popup Dismissal ──

    public void SetLightDismissLayer(bool visible)
    {
        if (LightDismissOverlay != null) LightDismissOverlay.IsVisible = visible;
    }

    private void CloseAllOverlays()
    {
        Pages.SettingsPage.Instance?.ClosePopups();
        Controls.QuickSettingsPanel.Instance?.ClosePopups();
        navBar?.ClosePopups();
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
        try
        {
        if (sender is Button btn && btn.Content is string address)
        {
            var clipboard = global::Avalonia.Controls.TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(address);
                ShowToast(CrimsonX.Localization.AppStrings.ToastAddressCopied, kind: ToastKind.Success);
            }
        }
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
        }
    }

    private CancellationTokenSource? _updateCts;
    private string _remoteUpdateVersion = "0.0.0";
    private string _remoteMinUpdateVersion = "0.0.0";

    // ── Update Check & Installation ──

    private void SetUpdateUIStatus(string status)
    {
        Pages.AboutPage.Instance?.SetUpdateStatus(status);
    }

    internal enum TitleUpdateState
    {
        Hidden,

        Available,

        Downloading,

        Extracting
    }

    private void SetTitleUpdateBadge(TitleUpdateState state, int percent = 0)
    {
        var badge = this.FindControl<global::Avalonia.Controls.StackPanel>("panTitleUpdate");
        if (badge == null) return;
        badge.IsVisible = state != TitleUpdateState.Hidden;
        var stripTitle = this.FindControl<global::Avalonia.Controls.TextBlock>("lblTabTitle");
        if (stripTitle != null)
        {
            bool badgeUp = state != TitleUpdateState.Hidden;
            global::Avalonia.Controls.Grid.SetColumnSpan(stripTitle, badgeUp ? 1 : 3);
            stripTitle.HorizontalAlignment = badgeUp
                ? global::Avalonia.Layout.HorizontalAlignment.Left
                : global::Avalonia.Layout.HorizontalAlignment.Center;
            stripTitle.Margin = badgeUp ? new global::Avalonia.Thickness(72, 0, 0, 0) : new global::Avalonia.Thickness(0);
        }
        if (!badge.IsVisible) return;
        var title  = this.FindControl<global::Avalonia.Controls.Button>("btnTitleUpdate");
        var dot    = this.FindControl<global::Avalonia.Controls.Shapes.Ellipse>("dotTitleUpdate");
        var ring   = this.FindControl<CrimsonX.Controls.ProgressRing>("ringTitleUpdate");
        var label  = this.FindControl<global::Avalonia.Controls.TextBlock>("lblTitleUpdate");
        var cancel = this.FindControl<global::Avalonia.Controls.Button>("btnTitleUpdateCancel");
        bool downloading = state == TitleUpdateState.Downloading;
        if (title != null && (state == TitleUpdateState.Available) != title.Classes.Contains("graphHover"))
        {
            if (state == TitleUpdateState.Available)
            {
                title.Classes.Add("graphHover");
                title.Cursor = new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand);
            }
            else
            {
                title.Classes.Remove("graphHover");
                title.Cursor = global::Avalonia.Input.Cursor.Default;
            }
        }
        if (dot != null)
        {
            dot.IsVisible = !downloading;
            dot.Fill = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse(
                state == TitleUpdateState.Extracting ? "#ED8936" : "#F56565"));
        }
        if (ring != null)
        {
            ring.IsVisible = downloading;
            ring.Value = Math.Clamp(percent / 100.0, 0, 1);
        }
        if (label != null)
        {
            label.Text = state switch
            {
                TitleUpdateState.Downloading => CrimsonX.Localization.AppStrings.UpdateBadgeDownloading,
                TitleUpdateState.Extracting  => CrimsonX.Localization.AppStrings.UpdateBadgeExtracting,
                _                            => CrimsonX.Localization.AppStrings.UpdateBadgeAvailable
            };
        }
        if (cancel != null) cancel.IsVisible = state != TitleUpdateState.Available;
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
                SetTitleUpdateBadge(TitleUpdateState.Available);
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
        SetTitleUpdateBadge(TitleUpdateState.Downloading, 0);
        try
        {
            await Services.UpdateService.DownloadAndInstallUpdateAsync(_remoteUpdateVersion, _cfg.BaseDir, (status) =>
            {
                SetUpdateUIStatus(status);
            }, token, (pct) =>
            {
                SetTitleUpdateBadge(pct >= 100 ? TitleUpdateState.Extracting : TitleUpdateState.Downloading, pct);
            });
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
                ShowToast(CrimsonX.Localization.AppStrings.ToastUpdateDownloadTimeout, ToastKind.Error);
            }
            SetUpdateUIStatus(CrimsonX.Localization.AppStrings.ToastNewUpdateAvailable);
            SetTitleUpdateBadge(TitleUpdateState.Available);
        }
        catch (Exception ex)
        {
            ShowToast(CrimsonX.Localization.AppStrings.ToastUpdateFailedPrefix + ex.Message, ToastKind.Error);
            SetUpdateUIStatus(CrimsonX.Localization.AppStrings.ToastNewUpdateAvailable);
            SetTitleUpdateBadge(TitleUpdateState.Available);
        }
        finally
        {
            _updateCts?.Dispose();
            _updateCts = null;
        }
    }

    private void BtnTitleUpdateCancel_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_updateCts == null) return;
        _updateCts.Cancel();
        _updateCts.Dispose();
        _updateCts = null;
        SetTitleUpdateBadge(TitleUpdateState.Available);
    }

    private async void BtnTitleUpdate_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
        if (_updateCts != null) return;
        bool isManual = Version.Parse(Services.UpdateService.AppVersion) < Version.Parse(_remoteMinUpdateVersion);
        var dialog = new Dialogs.UpdateDialog(isManual: isManual, _remoteUpdateVersion);
        var result = await dialog.ShowDialog<string>(this);
        if (result == "Primary")
        {
            if (isManual)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/RichTiTAN/CrimsonX/releases") { UseShellExecute = true })?.Dispose();
            else
                CrimsonX.Services.BackgroundTask.Run("update download", () => StartUpdateDownloadAsync());
        }
        else if (result == "Secondary")
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/RichTiTAN/CrimsonX/releases") { UseShellExecute = true })?.Dispose();
        }
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
        }
    }

    internal async void BtnCheckUpdate_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
        if (_updateCts != null)
        {
            _updateCts.Cancel();
            _updateCts.Dispose();
            _updateCts = null;
            SetTitleUpdateBadge(TitleUpdateState.Available);
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
                    CrimsonX.Services.BackgroundTask.Run("update download", () => StartUpdateDownloadAsync());
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
                ShowToast(CrimsonX.Localization.AppStrings.ToastLatestVersion, kind: ToastKind.Success);
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
            SetUpdateUIStatus(CrimsonX.Localization.AppStrings.ToastNewUpdateAvailable);
            SetTitleUpdateBadge(TitleUpdateState.Available);
            _updateCts?.Dispose();
            _updateCts = null;
            var dialog2 = new Dialogs.UpdateDialog(isManual: false, remoteVer);
            var result2 = await dialog2.ShowDialog<string>(this);
            if (result2 == "Primary")
            {
                CrimsonX.Services.BackgroundTask.Run("update download", () => StartUpdateDownloadAsync());
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
                ShowToast(CrimsonX.Localization.AppStrings.ToastUpdateCheckTimeout, ToastKind.Error);
            }
            Pages.AboutPage.Instance?.SetUpdateStatus(CrimsonX.Localization.AppStrings.CheckForUpdates);
        }
        catch (Exception ex)
        {
            ShowToast(CrimsonX.Localization.AppStrings.ToastUpdateFailedPrefix + ex.Message, ToastKind.Error);
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
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
        }
    }

    // ── Localization ──

    public void ApplyLanguage()
    {
        AppStrings.SetLanguage(_cfg.Language);
        bool fa = AppStrings.IsPersian;
        panMainInteraction.FlowDirection = global::Avalonia.Media.FlowDirection.LeftToRight;
        panMainInteraction.Margin = new global::Avalonia.Thickness(62, 0, 0, 0);
        this.FindControl<CrimsonX.Controls.NavigationBar>("navBar")?.ApplyLanguage();
        ApplyTitleBarName(_previousNav);
        CrimsonX.Pages.SettingsPage.Instance?.ApplyLanguage();
        this.FindControl<global::CrimsonX.Pages.UdpScannerPage>("pageUdpScanner")?.ApplyLanguage();
        CrimsonX.Pages.ThemesPage.Instance?.ApplyLanguage();
        CrimsonX.Pages.AboutPage.Instance?.ApplyLanguage();
        this.FindControl<CrimsonX.Controls.QuickSettingsPanel>("quickSettings")?.ApplyLanguage();
        CrimsonX.Pages.SplitTunnelPage.Instance?.ApplyLanguage();
        this.FindControl<global::CrimsonX.Pages.AppsGamesOverlay>("overlayAppsGames")?.ApplyLanguage();
        TextBlock? F(string name) => this.FindControl<TextBlock>(name);
        var panTimerContent = this.FindControl<StackPanel>("panTimerContent");
        if (panTimerContent != null)
        {
            panTimerContent.FlowDirection = fa 
                ? global::Avalonia.Media.FlowDirection.RightToLeft 
                : global::Avalonia.Media.FlowDirection.LeftToRight;
        }
        AppStrings.Apply(F("lblConnectedTo"),   AppStrings.ConnectedTo);
        var lblD = F("lblDisconnected");
        if (lblD != null) lblD.Text = "00:00:00";
        var lblLoc = F("lblCountryName");
        if (lblLoc != null && (lblLoc.Text == "Disconnected" || lblLoc.Text == "منتظر اتصال" || string.IsNullOrWhiteSpace(lblLoc.Text)))
            lblLoc.Text = AppStrings.StatusDisconnected;
        AppStrings.Apply(F("lblLocalPortLabel"), AppStrings.OpenLocalPort);
        AppStrings.Apply(F("lblLanPortLabel"), AppStrings.OpenLanPort);
        AppStrings.Apply(F("lblSessionLabel"),  AppStrings.SessionLabel);
        AppStrings.Apply(F("lblStatusLabel"),   AppStrings.StatusLabel);
        AppStrings.Apply(F("lblLocationLabel"), AppStrings.LocationLabel);
        AppStrings.Apply(F("lblPublicIpLabel"), AppStrings.PublicIpLabel);
        AppStrings.Apply(F("lblPingLabel"),     AppStrings.PingLabel);
        AppStrings.Apply(F("lblTotalLabel"),    AppStrings.TotalLabel);
        AppStrings.Apply(F("lblDownloadLabel"), AppStrings.DownloadLabel);
        AppStrings.Apply(F("lblUploadLabel"),   AppStrings.UploadLabel);
        UpdateStatusText();
        var btnConn = this.FindControl<Button>("btnConnect");
        if (btnConn != null)
        {
            var txt = this.FindControl<TextBlock>("txtConnectBtn");
            if (txt != null)
            {
                bool connected = _state.IsConnected && !_state.IsReconnecting;
                txt.Text = connected
                    ? CrimsonX.Localization.AppStrings.StatusConnected
                    : CrimsonX.Localization.AppStrings.StatusConnect;
                txt.FlowDirection = fa
                    ? global::Avalonia.Media.FlowDirection.RightToLeft
                    : global::Avalonia.Media.FlowDirection.LeftToRight;
            }
        }
        AppStrings.ApplyToolTip(this.FindControl<Button>("btnRefreshPing"), AppStrings.TtPingRefresh);
        AppStrings.ApplyToolTip(this.FindControl<Button>("btnPinTopBar"), AppStrings.PinTopBar);
        ApplyTopBarPin(_previousNav);
        HookStatTileResize();
        Pages.AboutPage.Instance?.UpdateLocalization();
        Pages.ThemesPage.Instance?.UpdateLocalization();
        if (_trayWidget != null)
            _trayWidget.ApplyLanguage(fa);
        UpdateLanPortUI();
        UpdateLocalPortUI();
        ApplyModeUI(_cfg.LastXrayMode);
        var txtConnectBtn = F("txtConnectBtn");
        var txtConnectedBtn = F("txtConnectedBtn");
        bool settled = _state.IsConnected && !_state.IsReconnecting;
        if (settled)
        {
            if (txtConnectedBtn != null) txtConnectedBtn.Text = AppStrings.StatusConnected;
            if (txtConnectBtn != null) txtConnectBtn.Text = AppStrings.StatusConnected;
        }
        else if (_state.IsEngineRunning || _state.IsReconnecting)
        {
            if (txtConnectBtn != null) txtConnectBtn.Text = CrimsonX.Localization.AppStrings.StatusConnecting;
        }
        else
        {
            if (txtConnectBtn != null) txtConnectBtn.Text = AppStrings.StatusConnect;
        }
    }

    // ── Operating Mode Switching ──

    internal async Task SetConnectionModeAsync(string newMode)
    {
        try
        {
        newMode = CrimsonX.Services.ConnectionModes.Normalise(newMode);
        if (_isModeHotSwapping) return;
        string oldMode = CrimsonX.Services.ConnectionModes.Normalise(_cfg.LastXrayMode);
        if (oldMode == newMode) return;
        bool live = _state.IsEngineRunning || _state.IsConnected;
        if (live && newMode == CrimsonX.Services.ConnectionModes.Vpn && IsVpnAdapterInUse())
        {
            ShowToast(CrimsonX.Localization.AppStrings.ToastVpnInUse, ToastKind.Error);
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
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
        }
    }

    // ── Apply Settings & Mode UI ──

    private void ApplyLoadedSettings()
    {
        _isInitializingSettings = true;
        _isInitializingSettings = false;
        ApplyModeUI(_pollMode);
        UpdateLanPortUI();
        ApplyLanguage();
        CrimsonX.Pages.SettingsPage.Instance?.SyncUI();
        CrimsonX.Pages.SplitTunnelPage.Instance?.SyncUI();
    }

    private void ApplyModeUI(string mode)
    {
        CrimsonX.Pages.SettingsPage.Instance?.ApplyConnectionModeUI(mode);
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
        {
            _restoreState = WindowState;
            Hide();
        }
        else
        {
            WindowState = WindowState.Minimized;
        }
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
        if (_closeDeferred)
        {
            Dispose();
            return;
        }
        if (CrimsonX.Services.SystemDnsService.HasPendingRestore
            && e.CloseReason != global::Avalonia.Controls.WindowCloseReason.OSShutdown)
        {
            _closeDeferred = true;
            e.Cancel = true;
            StopAllEngines(isClosing: true);
            CrimsonX.Services.BackgroundTask.Run("close", () => FinishCloseAsync());
            return;
        }
        StopAllEngines(isClosing: true);
        Dispose();
    }

    private async Task FinishCloseAsync()
    {
        try
        {
            var restore = CrimsonX.Services.SystemDnsService.RestoreAsync(500);
            await Task.WhenAny(restore, Task.Delay(500));
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
        }
        try { Close(); } catch (Exception ex) { CrimsonX.Services.SimpleLogger.Log(ex); }
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
        this.FindControl<CrimsonX.Controls.NavigationBar>("navBar")?.ApplyTheme();
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

    public void SetAdBlock(bool enabled)
    {
        if (_cfg.EnableAdBlock == enabled) return;
        _cfg.EnableAdBlock = enabled;
        CrimsonX.Pages.SettingsPage.Instance?.SetAdBlockToggle(enabled);
        this.FindControl<CrimsonX.Controls.NavigationBar>("navBar")?.SetAdBlockerState(enabled);
        if (_state.IsEngineRunning) SmartRestartXray();
        RequestConfigSave();
    }

    private void NavBar_AdBlockerToggled(object? sender, bool enabled) => SetAdBlock(enabled);

    private static string TitleBarNameFor(string viewName) => viewName switch
    {
        "SplitTunneling" => CrimsonX.Localization.AppStrings.NavSplitTunneling,
        "Themes" => CrimsonX.Localization.AppStrings.NavThemes,
        "UdpScanner" => CrimsonX.Localization.AppStrings.UdpScannerTitle,
        "About" => CrimsonX.Localization.AppStrings.NavAbout,
        "AppsGames" => CrimsonX.Localization.AppStrings.NavAppsGames,
        "Settings" => CrimsonX.Localization.AppStrings.NavSettings,
        "Home" => CrimsonX.Localization.AppStrings.AppName,
        _ => string.Empty,
    };

    private void ApplyTitleBarName(string viewName)
    {
        var label = this.FindControl<global::Avalonia.Controls.TextBlock>("lblTabTitle");
        if (label != null) label.Text = TitleBarNameFor(viewName);
    }

    private void NavBar_NavChanged(object? sender, string viewName)
    {
        using var _busy = CrimsonX.Services.UiBusy.Scope("nav " + viewName);
        _previousNav = viewName;
        ApplyTitleBarName(viewName);
        ApplyTopBarPin(viewName);
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
            case "Home":
                carousel.SelectedIndex = 0;
                this.FindControl<CrimsonX.Controls.QuickSettingsPanel>("quickSettings")?.SyncCustomConfigsView();
                break;
            case "SplitTunneling": carousel.SelectedIndex = 1; break;
            case "Settings":
                carousel.SelectedIndex = 2;
                CrimsonX.Pages.SettingsPage.Instance?.ShowSettingsPage();
                break;
            case "UdpScanner":
                carousel.SelectedIndex = 6;
                this.FindControl<CrimsonX.Pages.UdpScannerPage>("pageUdpScanner")?.OnEnter();
                break;
            case "Themes": carousel.SelectedIndex = 3; break;
            case "About": carousel.SelectedIndex = 4; break;
            case "AppsGames": carousel.SelectedIndex = 5; break;
        }
        CrimsonX.Controls.AnimatedBackground.Instance?.SetHomeTabActive(carousel.SelectedIndex == 0);
        var panTabDarken = this.FindControl<global::Avalonia.Controls.Border>("panTabDarken");
        if (panTabDarken != null)
        {
            panTabDarken.Opacity = carousel.SelectedIndex >= 1 ? 1 : 0;
        }
        if (viewName == "AppsGames")
        {
            var page = this.FindControl<global::CrimsonX.Pages.AppsGamesOverlay>("overlayAppsGames");
            if (page != null)
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => page.LoadRules(), global::Avalonia.Threading.DispatcherPriority.Background);
        }
if (viewName == "SplitTunneling")
{
    var page = this.FindControl<global::CrimsonX.Pages.SplitTunnelPage>("pageSplit");
    if (page != null) page.SyncUI();
}
    }
}

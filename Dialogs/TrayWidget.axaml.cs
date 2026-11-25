/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia;
using CrimsonX.Localization;

namespace CrimsonX.Dialogs
{
    public partial class TrayWidget : Window
    {
        private MainWindow? _main;
        private DispatcherTimer? _timer;
        private bool _isClosing = false;

        // Reuse these: UpdateUI runs once per second and should not allocate colours each tick.
        private static readonly IBrush ConnectedBrush = new SolidColorBrush(Color.FromRgb(0x68, 0xD3, 0x91));
        private static readonly IBrush NeutralBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));

        public TrayWidget()
        {
            InitializeComponent();
        }

        public TrayWidget(MainWindow main)
        {
            InitializeComponent();
            _main = main;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => UpdateUI();
            _timer.Start();
            UpdateUI();
            ApplyLanguage(AppStrings.IsPersian);
        }

        private void UpdateUI()
        {
            if (_main == null) return;
            if (_main.GetState().IsConnected)
            {
                SetStatus(AppStrings.StatusConnected, ConnectedBrush);
                SetToggleContent(AppStrings.StatusDisconnect);
            }
            else if (_main.GetState().IsEngineRunning)
            {
                SetStatus(AppStrings.StatusConnecting, NeutralBrush);
                SetToggleContent(AppStrings.StatusStopEngine);
            }
            else
            {
                SetStatus(AppStrings.StatusNotConnected, NeutralBrush);
                SetToggleContent(AppStrings.StatusConnect);
            }
            string speed = _main.GetSpeedText();
            if (lblSpeed.Text != speed) lblSpeed.Text = speed;
        }

        private void SetStatus(string text, IBrush brush)
        {
            if (lblStatus.Text != text) lblStatus.Text = text;
            if (!ReferenceEquals(lblStatus.Foreground, brush)) lblStatus.Foreground = brush;
        }

        private void SetToggleContent(string text)
        {
            if (!Equals(btnToggle.Content, text)) btnToggle.Content = text;
        }

        public void ApplyLanguage(bool isPersian)
        {
            UpdateUI();
            bool fa = isPersian;
            btnClose.Content      = AppStrings.TrayClose;
            btnShowWindow.Content = AppStrings.TrayShowWindow;
            lblStatus.FlowDirection = fa
                ? global::Avalonia.Media.FlowDirection.RightToLeft
                : global::Avalonia.Media.FlowDirection.LeftToRight;
        }

        private void BtnToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_main == null) return;
            _main.ConnectDisconnect();
            UpdateUI();
            ApplyLanguage(AppStrings.IsPersian);
        }

        private void BtnShowWindow_Click(object sender, RoutedEventArgs e)
        {
            if (_main == null) return;
            _main.Show();
            _main.WindowState = WindowState.Normal;
            _main.Activate();
            SafeClose();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            SafeClose();
            _main?.Close();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            SafeClose();
        }

        private void SafeClose()
        {
            if (_isClosing) return;
            _isClosing = true;
            _timer?.Stop();
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer?.Stop();
            base.OnClosed(e);
        }
    }
}

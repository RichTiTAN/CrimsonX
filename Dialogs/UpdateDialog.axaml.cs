/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using Avalonia.Controls;
using System.Windows.Input;
using System;

namespace CrimsonX.Dialogs
{
    public partial class UpdateDialog : Window
    {
        public string DialogTitle { get; set; } = "";
        public string DialogMessage { get; set; } = "";
        public string PrimaryButtonText { get; set; } = "";
        public string SecondaryButtonText { get; set; } = "";
        public string CancelButtonText { get; set; } = "";

        public ICommand ButtonCommand { get; }

        public UpdateDialog()
        {
            ButtonCommand = new RelayCommand(param => 
            {
                Close(param?.ToString());
            });
            DataContext = this;
            InitializeComponent();
        }

        public UpdateDialog(bool isManual, string remoteVer)
        {
            ButtonCommand = new RelayCommand(param => 
            {
                Close(param?.ToString());
            });
            DataContext = this;
            if (isManual)
            {
                DialogTitle = CrimsonX.Localization.AppStrings.UpdateManualTitle;
                DialogMessage = string.Format(CrimsonX.Localization.AppStrings.UpdateManualMsg, remoteVer);
                PrimaryButtonText = CrimsonX.Localization.AppStrings.BtnDownloadGithub;
            }
            else
            {
                DialogTitle = CrimsonX.Localization.AppStrings.UpdateAutoTitle;
                DialogMessage = string.Format(CrimsonX.Localization.AppStrings.UpdateAutoMsg, remoteVer);
                PrimaryButtonText = CrimsonX.Localization.AppStrings.BtnUpdateNow;
            }
            SecondaryButtonText = CrimsonX.Localization.AppStrings.BtnChangeLog;
            CancelButtonText = CrimsonX.Localization.AppStrings.Cancel;
            if (CrimsonX.Localization.AppStrings.IsPersian)
            {
                this.FlowDirection = Avalonia.Media.FlowDirection.RightToLeft;
            }
            InitializeComponent();
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        public RelayCommand(Action<object?> execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute(parameter);
    }
}

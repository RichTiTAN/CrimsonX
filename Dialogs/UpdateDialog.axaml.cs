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
            CancelButtonText = CrimsonX.Localization.AppStrings.BtnCancel;
            
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

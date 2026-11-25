/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System.Threading.Tasks;
using CrimsonX.Services;

namespace CrimsonX;

public partial class MainWindow
{
    private bool _updateCheckStarted;

    private async Task<bool> EnsureEulaAcceptedAsync()
    {
        if (EulaStore.IsAccepted()) return true;
        var dialog = new CrimsonX.Dialogs.EulaDialog(acceptance: true);
        string? result = await dialog.ShowDialog<string>(this);
        if (result != "Accept") return false;
        return EulaStore.Accept();
    }

    private void StartUpdateCheck()
    {
        if (_updateCheckStarted) return;
        _updateCheckStarted = true;
        CrimsonX.Services.BackgroundTask.Run("update check", () => CheckUpdateSilentAsync());
    }
}

/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CrimsonX.Localization;
using CrimsonX.Services;

namespace CrimsonX.Dialogs;

public partial class EulaDialog : Window
{
    public string DialogTitle { get; set; } = "";
    public string Intro { get; set; } = "";
    public string EulaText { get; set; } = "";
    public string AcceptText { get; set; } = "";
    public string DeclineText { get; set; } = "";
    public string CloseText { get; set; } = "";
    public bool IsAcceptance { get; set; }

    public EulaDialog() : this(true)
    {
    }

    public EulaDialog(bool acceptance)
    {
        IsAcceptance = acceptance;
        DialogTitle = AppStrings.EulaTitle;
        Intro = AppStrings.EulaIntro;
        AcceptText = AppStrings.EulaAccept;
        DeclineText = AppStrings.EulaDecline;
        CloseText = AppStrings.EulaClose;
        EulaText = ReadEula();
        DataContext = this;
        if (AppStrings.IsPersian)
            FlowDirection = global::Avalonia.Media.FlowDirection.RightToLeft;
        InitializeComponent();
        var body = this.FindControl<SelectableTextBlock>("txtEula");
        if (body != null)
            body.FlowDirection = global::Avalonia.Media.FlowDirection.LeftToRight;
        var accept = this.FindControl<Button>("btnAccept");
        if (accept != null) accept.IsVisible = acceptance;
        var decline = this.FindControl<Button>("btnDecline");
        if (decline != null) decline.IsVisible = acceptance;
        var close = this.FindControl<Button>("btnClose");
        if (close != null) close.IsVisible = !acceptance;
    }

    private static string ReadEula()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "EULA.txt");
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            SimpleLogger.Log(ex);
        }
        return "The End User License Agreement file (EULA.txt) is missing from this build.";
    }

    private void Accept_Click(object? sender, RoutedEventArgs e) => Close("Accept");

    private void Decline_Click(object? sender, RoutedEventArgs e) => Close("Decline");

    private void Close_Click(object? sender, RoutedEventArgs e) => Close("Close");
}

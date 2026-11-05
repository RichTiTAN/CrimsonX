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
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CrimsonX.Localization;
using CrimsonX.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Dialogs
{
    public partial class IpsecProfileDialog : Window
    {
        public IpsecProfileDialog()
        {
            InitializeComponent();
        }

        public IpsecProfileDialog(string existing) : this()
        {
            Title = AppStrings.IpsecProfileTitle;
            lblIpsecTitle.Text = AppStrings.IpsecProfileTitle;
            txtIpsecMtu.PlaceholderText = AppStrings.IpsecMtuHint;
            cmbIpsecAuth.SelectedIndex = 0;
            Prefill(existing);
            UpdateAuthFields();
            cmbIpsecAuth.SelectionChanged += (_, _) => UpdateAuthFields();
        }

        public static Task<string?> ShowAsync(string existing)
        {
            var owner = MainWindow.Instance;
            if (owner == null) return Task.FromResult<string?>(null);
            return Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dialog = new IpsecProfileDialog(existing);
                return await dialog.ShowDialog<string?>(owner);
            });
        }

        private void Prefill(string existing)
        {
            if (string.IsNullOrWhiteSpace(existing)) return;
            if (!TunnelConfigParser.TryParse(existing, out var parsed) || parsed?.Endpoint == null) return;
            var endpoint = parsed.Endpoint;
            if (!string.Equals(endpoint["type"]?.ToString(), "ipsec", StringComparison.OrdinalIgnoreCase)) return;
            txtIpsecServer.Text = endpoint["server"]?.ToString() ?? "";
            txtIpsecPort.Text = endpoint["server_port"]?.ToString() ?? "500";
            bool psk = string.Equals(endpoint["auth"]?.ToString(), "psk", StringComparison.OrdinalIgnoreCase);
            cmbIpsecAuth.SelectedIndex = psk ? 1 : 0;
            txtIpsecUser.Text = endpoint["username"]?.ToString() ?? "";
            txtIpsecPass.Text = endpoint["password"]?.ToString() ?? "";
            txtIpsecPsk.Text = endpoint["psk"]?.ToString() ?? "";
            txtIpsecRemoteId.Text = endpoint["remote_id"]?.ToString() ?? "";
            txtIpsecLocalId.Text = endpoint["local_id"]?.ToString() ?? "";
            txtIpsecCa.Text = endpoint["ca"]?.ToString() ?? "";
            txtIpsecDns.Text = endpoint["dns"]?.ToString() ?? "";
            txtIpsecMtu.Text = endpoint["mtu"]?.ToString() ?? "";
        }

        private bool PskMode => cmbIpsecAuth.SelectedIndex == 1;

        private void UpdateAuthFields()
        {
            bool psk = PskMode;
            txtIpsecUser.IsEnabled = !psk;
            txtIpsecPass.IsEnabled = !psk;
            txtIpsecPsk.IsEnabled = psk;
        }

        private void ShowError(string message)
        {
            lblIpsecError.Text = message;
            lblIpsecError.IsVisible = message.Length > 0;
        }

        private void Save_Click(object? sender, RoutedEventArgs e)
        {
            string server = (txtIpsecServer.Text ?? "").Trim();
            if (server.Length == 0 || server.IndexOf(' ') >= 0)
            {
                ShowError("Enter the server address.");
                return;
            }
            int port = 500;
            string portText = (txtIpsecPort.Text ?? "").Trim();
            if (portText.Length > 0
                && (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) || port <= 0 || port > 65535))
            {
                ShowError("The port has to be a number between 1 and 65535.");
                return;
            }

            string username = (txtIpsecUser.Text ?? "").Trim();
            string password = txtIpsecPass.Text ?? "";
            string psk = txtIpsecPsk.Text ?? "";
            if (PskMode)
            {
                if (psk.Length == 0)
                {
                    ShowError("Enter the pre-shared key.");
                    return;
                }
            }
            else
            {
                if (username.Length == 0)
                {
                    ShowError("Enter the username.");
                    return;
                }
                if (password.Length == 0)
                {
                    ShowError("Enter the password.");
                    return;
                }
            }

            string dns = (txtIpsecDns.Text ?? "").Trim();
            if (dns.Length > 0 && !IsIpAddress(dns))
            {
                ShowError("The DNS server has to be an IP address.");
                return;
            }

            var profile = new JObject
            {
                ["type"] = "ipsec",
                ["server"] = server,
                ["server_port"] = port,
                ["auth"] = PskMode ? "psk" : "eap"
            };
            if (PskMode)
            {
                profile["psk"] = psk;
            }
            else
            {
                profile["username"] = username;
                profile["password"] = password;
            }
            string remoteId = (txtIpsecRemoteId.Text ?? "").Trim();
            if (remoteId.Length > 0) profile["remote_id"] = remoteId;
            string localId = (txtIpsecLocalId.Text ?? "").Trim();
            if (localId.Length > 0) profile["local_id"] = localId;
            string ca = (txtIpsecCa.Text ?? "").Trim();
            if (ca.Length > 0) profile["ca"] = ca;
            if (dns.Length > 0) profile["dns"] = dns;

            string mtuText = (txtIpsecMtu.Text ?? "").Trim();
            if (mtuText.Length > 0)
            {
                if (!int.TryParse(mtuText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mtu)
                    || mtu < TunnelConfigParser.IpsecMinMtu || mtu > TunnelConfigParser.IpsecMaxMtu)
                {
                    ShowError($"The MTU has to be between {TunnelConfigParser.IpsecMinMtu} and {TunnelConfigParser.IpsecMaxMtu}.");
                    return;
                }
                profile["mtu"] = mtu;
            }

            string text = profile.ToString(Formatting.Indented);
            if (!TunnelConfigParser.TryParse(text, out var parsed) || !parsed.Success)
            {
                ShowError(parsed?.Error is { Length: > 0 } reason ? reason : "This profile could not be read.");
                return;
            }
            Close(text);
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

        private void Dialog_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Shift) == 0)
            {
                Save_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private static bool IsIpAddress(string value)
        {
            string text = value.Trim();
            int colon = text.LastIndexOf(':');
            if (colon > 0 && text.IndexOf(']') < colon) text = text.Substring(0, colon);
            return IPAddress.TryParse(text.Trim('[', ']'), out _);
        }
    }
}

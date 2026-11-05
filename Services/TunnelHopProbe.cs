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
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CrimsonX.Services
{
    public static class TunnelHopProbe
    {
        public const int DefaultTimeoutMs = 6000;

        internal sealed class Target
        {
            public string Host { get; init; } = "";

            public int Port { get; init; }

            public byte[] Request { get; init; } = Array.Empty<byte>();

            public string Label => $"{Host}:{Port}";
        }

        private static byte[] HttpHead(string host) =>
            Encoding.ASCII.GetBytes($"HEAD / HTTP/1.0\r\nHost: {host}\r\n\r\n");

        internal static IReadOnlyList<Target> Targets { get; } = new[]
        {
            new Target { Host = "1.1.1.1",        Port = 80, Request = HttpHead("1.1.1.1") },
            new Target { Host = "208.67.220.220", Port = 80, Request = HttpHead("208.67.220.220") },
            new Target { Host = "1.0.0.1",        Port = 80, Request = HttpHead("1.0.0.1") },
        };

        public sealed class TResult
        {
            public bool Ok { get; set; }

            public long Ms { get; set; } = -1;

            public string Target { get; set; } = "";

            public string Error { get; set; } = "";

            internal bool Definitive { get; set; }
        }

        public static async Task<TResult> ProbeAsync(int port, int timeoutMs = DefaultTimeoutMs, CancellationToken ct = default,
            string user = "", string password = "")
        {
            var result = new TResult();
            if (port <= 0)
            {
                result.Error = "no socks port";
                return result;
            }
            var failures = new List<string>();
            foreach (var target in Targets)
            {
                var attempt = await ProbeTargetAsync(port, target, timeoutMs, ct, user, password).ConfigureAwait(false);
                if (attempt.Ok)
                {
                    result.Ok = true;
                    result.Ms = attempt.Ms;
                    result.Target = target.Label;
                    return result;
                }
                if (ct.IsCancellationRequested)
                {
                    result.Error = "the probe was cancelled";
                    return result;
                }
                failures.Add($"{target.Label}: {attempt.Error}");
                if (attempt.Definitive)
                {
                    result.Definitive = true;
                    break;
                }
            }
            result.Error = string.Join("; ", failures);
            return result;
        }

        private static async Task<TResult> ProbeTargetAsync(int port, Target target, int timeoutMs, CancellationToken ct,
            string user, string password)
        {
            var result = new TResult();
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            try
            {
                await client.ConnectAsync(System.Net.IPAddress.Loopback, port, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                result.Error = $"the {target.Label} hop did not respond within {timeoutMs} ms";
                return result;
            }
            catch (SocketException ex)
            {
                result.Error = ex.Message;
                result.Definitive = true;
                return result;
            }
            try
            {
                var stream = client.GetStream();
                stream.ReadTimeout = timeoutMs;
                stream.WriteTimeout = timeoutMs;
                bool auth = !string.IsNullOrEmpty(user);
                await stream.WriteAsync(auth ? new byte[] { 0x05, 0x02, 0x00, 0x02 } : new byte[] { 0x05, 0x01, 0x00 }, cts.Token).ConfigureAwait(false);
                var greeting = await ReadAsync(stream, 2, cts.Token).ConfigureAwait(false);
                if (greeting == null || greeting[0] != 0x05)
                {
                    result.Error = "the socks inbound did not answer the handshake";
                    return result;
                }
                if (greeting[1] == 0x02)
                {
                    if (!auth)
                    {
                        result.Error = "the socks inbound asks for a username and password";
                        return result;
                    }
                    var userBytes = Encoding.UTF8.GetBytes(user);
                    var passBytes = Encoding.UTF8.GetBytes(password ?? "");
                    var authPacket = new byte[3 + userBytes.Length + passBytes.Length];
                    authPacket[0] = 0x01;
                    authPacket[1] = (byte)userBytes.Length;
                    Buffer.BlockCopy(userBytes, 0, authPacket, 2, userBytes.Length);
                    authPacket[2 + userBytes.Length] = (byte)passBytes.Length;
                    Buffer.BlockCopy(passBytes, 0, authPacket, 3 + userBytes.Length, passBytes.Length);
                    await stream.WriteAsync(authPacket, cts.Token).ConfigureAwait(false);
                    var authReply = await ReadAsync(stream, 2, cts.Token).ConfigureAwait(false);
                    if (authReply == null || authReply.Length < 2 || authReply[1] != 0x00)
                    {
                        result.Error = "the socks inbound rejected those credentials";
                        return result;
                    }
                }
                else if (greeting[1] != 0x00)
                {
                    result.Error = "the socks inbound did not accept a no-auth handshake";
                    return result;
                }
                await stream.WriteAsync(BuildConnect(target), cts.Token).ConfigureAwait(false);
                var reply = await ReadAsync(stream, 10, cts.Token).ConfigureAwait(false);
                if (reply == null || reply.Length < 2 || reply[1] != 0x00)
                {
                    result.Error = reply == null || reply.Length < 2
                        ? "no reply to the socks connect"
                        : $"the tunnel refused the connect (socks reply 0x{reply[1]:x2})";
                    return result;
                }
                var watch = System.Diagnostics.Stopwatch.StartNew();
                await stream.WriteAsync(target.Request, cts.Token).ConfigureAwait(false);
                var first = await ReadAsync(stream, 1, cts.Token).ConfigureAwait(false);
                watch.Stop();
                if (first == null || first.Length == 0)
                {
                    result.Error = "the tunnel accepted the connect but no data came back";
                    return result;
                }
                result.Ok = true;
                result.Ms = watch.ElapsedMilliseconds;
                return result;
            }
            catch (OperationCanceledException)
            {
                result.Error = $"the {target.Label} hop did not respond within {timeoutMs} ms";
                return result;
            }
            catch (SocketException ex)
            {
                result.Error = ex.Message;
                result.Definitive = ex.SocketErrorCode == SocketError.ConnectionRefused;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                return result;
            }
        }

        private static byte[] BuildConnect(Target target)
        {
            var address = IPAddress.Parse(target.Host).GetAddressBytes();
            var packet = new byte[4 + address.Length + 2];
            packet[0] = 0x05;
            packet[1] = 0x01;
            packet[2] = 0x00;
            packet[3] = address.Length == 16 ? (byte)0x04 : (byte)0x01;
            Buffer.BlockCopy(address, 0, packet, 4, address.Length);
            packet[4 + address.Length] = (byte)(target.Port >> 8);
            packet[5 + address.Length] = (byte)(target.Port & 0xFF);
            return packet;
        }

        private static async Task<byte[]> ReadAsync(NetworkStream stream, int count, CancellationToken ct)
        {
            var buffer = new byte[Math.Max(count, 64)];
            int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (read <= 0) return null;
            var slice = new byte[read];
            Array.Copy(buffer, slice, read);
            return slice;
        }
    }
}

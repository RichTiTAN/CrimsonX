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
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CrimsonX.Services
{
    public static class TunnelHopProbe
    {
        public const int DefaultTimeoutMs = 6000;

        private static readonly byte[] HttpRequest =
            Encoding.ASCII.GetBytes("HEAD / HTTP/1.0\r\nHost: 1.1.1.1\r\n\r\n");

        public sealed class TResult
        {
            public bool Ok { get; set; }

            public long Ms { get; set; } = -1;

            public string Error { get; set; } = "";
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
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            try
            {
                await client.ConnectAsync(System.Net.IPAddress.Loopback, port, cts.Token).ConfigureAwait(false);
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
                var connect = new byte[]
                {
                    0x05, 0x01, 0x00, 0x01,
                    0x01, 0x01, 0x01, 0x01,
                    (byte)(80 >> 8), (byte)(80 & 0xFF)
                };
                await stream.WriteAsync(connect, cts.Token).ConfigureAwait(false);
                var reply = await ReadAsync(stream, 10, cts.Token).ConfigureAwait(false);
                if (reply == null || reply.Length < 2 || reply[1] != 0x00)
                {
                    result.Error = reply == null || reply.Length < 2
                        ? "no reply to the socks connect"
                        : $"the tunnel refused the connect (socks reply 0x{reply[1]:x2})";
                    return result;
                }
                var watch = System.Diagnostics.Stopwatch.StartNew();
                await stream.WriteAsync(HttpRequest, cts.Token).ConfigureAwait(false);
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
                result.Error = $"the hop did not respond within {timeoutMs} ms";
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                return result;
            }
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

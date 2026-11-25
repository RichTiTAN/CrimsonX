/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Channels;

namespace CrimsonX.Services
{
    public static class SimpleLogger
    {
        // Debug logging must never grow without bound: keep a bounded queue, a single
        // append-only writer, and trim only when the line count actually exceeds the cap.
        private const int MaxQueuedMessages = 8192;
        private const int MaxLogLines = 1000;

        private static string LogFile = Path.Combine(AppContext.BaseDirectory, "Logs", "error.log");
        private static bool _dirCreated = false;
        private static bool _fallbackUsed = false;

        private static readonly Channel<string> _logChannel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(MaxQueuedMessages)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
        private static readonly Task _consumerTask;

        private static readonly object _fileLock = new();
        private static StreamWriter? _writer;
        private static FileStream? _fileStream;
        private static int _lineCount = 0;
        private static bool _trimRequired = false;

        static SimpleLogger()
        {
            _consumerTask = Task.Run(async () =>
            {
                try
                {
                    var reader = _logChannel.Reader;
                    var sb = new StringBuilder();
                    int buffered = 0;
                    while (await reader.WaitToReadAsync())
                    {
                        while (reader.TryRead(out var msg))
                        {
                            sb.Append(msg);
                            if (++buffered >= 32)
                            {
                                WriteBuffer(sb);
                                buffered = 0;
                            }
                        }
                        if (buffered > 0)
                        {
                            WriteBuffer(sb);
                            buffered = 0;
                        }
                    }
                }
                catch { }
            });
        }

        public static void Shutdown()
        {
            try
            {
                _logChannel.Writer.TryComplete();
                _consumerTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch { }
            CloseWriter();
        }

        private static void WriteBuffer(StringBuilder sb)
        {
            if (sb.Length == 0) return;
            if (TryWrite(sb)) return;
            if (_fallbackUsed) return;
            _fallbackUsed = true;
            _dirCreated = false;
            CloseWriter();
            LogFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrimsonX", "Logs", "error.log");
            TryWrite(sb);
        }

        private static bool TryWrite(StringBuilder sb)
        {
            try
            {
                lock (_fileLock)
                {
                    if (!_dirCreated)
                    {
                        var dir = Path.GetDirectoryName(LogFile);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }
                        _dirCreated = true;
                    }

                    EnsureWriter();
                    _lineCount += CountLines(sb);
                    _writer!.Write(sb);
                    _writer.Flush();
                    sb.Clear();

                    if (_lineCount > MaxLogLines)
                    {
                        _trimRequired = true;
                    }
                    if (_trimRequired)
                    {
                        TrimLogFile();
                    }
                    return true;
                }
            }
            catch
            {
                CloseWriter();
                return false;
            }
        }

        private static void EnsureWriter()
        {
            if (_writer != null) return;
            _fileStream = new FileStream(LogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            _writer = new StreamWriter(_fileStream, new UTF8Encoding(false)) { AutoFlush = false };
            _lineCount = CountExistingLines();
        }

        private static void CloseWriter()
        {
            lock (_fileLock)
            {
                try { _writer?.Flush(); } catch { }
                try { _writer?.Dispose(); } catch { }
                try { _fileStream?.Dispose(); } catch { }
                _writer = null;
                _fileStream = null;
            }
        }

        private static int CountExistingLines()
        {
            try
            {
                if (!File.Exists(LogFile)) return 0;
                int lines = 0;
                using var reader = new StreamReader(
                    new FileStream(LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite),
                    Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                while (reader.ReadLine() != null && lines <= MaxLogLines)
                {
                    lines++;
                }
                return lines;
            }
            catch
            {
                return 0;
            }
        }

        private static int CountLines(StringBuilder sb)
        {
            int lines = 0;
            for (int i = 0; i < sb.Length; i++)
            {
                if (sb[i] == '\n') lines++;
            }
            return lines;
        }

        private static void TrimLogFile()
        {
            try
            {
                CloseWriter();
                if (!File.Exists(LogFile))
                {
                    _lineCount = 0;
                    _trimRequired = false;
                    return;
                }

                var tail = new Queue<string>(MaxLogLines);
                foreach (string line in File.ReadLines(LogFile))
                {
                    if (tail.Count == MaxLogLines) tail.Dequeue();
                    tail.Enqueue(line);
                }

                File.WriteAllLines(LogFile, tail, new UTF8Encoding(false));
                _lineCount = tail.Count;
                _trimRequired = false;
            }
            catch
            {
                _lineCount = 0;
                _trimRequired = false;
                CloseWriter();
            }
        }

        public static bool EnableLogging { get; set; } = false;

        public static bool Enabled => EnableLogging;

        public static void LogFatal(Exception ex)
        {
            try
            {
                string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FATAL {ex}\n\n";
                if (!_logChannel.Writer.TryWrite(msg))
                {
                    WriteBuffer(new StringBuilder(msg));
                }
            }
            catch { }
        }

        public static void Log(Exception ex)
        {
            if (!EnableLogging) return;
            try
            {
                string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n";
                _logChannel.Writer.TryWrite(msg);
            }
            catch { }
        }
        
        public static void Log(string message)
        {
            if (!EnableLogging) return;
            try
            {
                string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";
                _logChannel.Writer.TryWrite(msg);
            }
            catch { }
        }

        private static readonly object _onceLock = new object();
        private static readonly Dictionary<string, int> _onceCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly HashSet<string> _firstKeys = new HashSet<string>(StringComparer.Ordinal);

        public static void LogFirst(string key, string message)
        {
            if (!EnableLogging) return;
            lock (_onceLock)
            {
                if (_firstKeys.Count >= LogDedupe.MaxKeys) _firstKeys.Clear();
                if (!_firstKeys.Add(key ?? "")) return;
            }
            Log(message);
        }

        public static void LogOnce(string key, string message)
        {
            if (!EnableLogging) return;
            string line = message;
            lock (_onceLock)
            {
                if (!LogDedupe.ShouldLog(key, _onceCounts, out bool summary, out int repeats)) return;
                if (summary)
                    line = $"{message} (this notice repeated {repeats} times; further repeats are suppressed)";
            }
            Log(line);
        }
    }
}

﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using GitUI;
using JetBrains.Annotations;

namespace GitCommands.Logging
{
    // TODO capture process working directory
    // TODO capture number of input bytes
    // TODO capture number of standard output bytes
    // TODO capture number of standard error bytes

    public sealed class ProcessOperation
    {
        private readonly CommandLogEntry _entry;
        private readonly Stopwatch _stopwatch;
        private readonly Action _raiseCommandsChanged;

        internal ProcessOperation(CommandLogEntry entry, Stopwatch stopwatch, Action raiseCommandsChanged)
        {
            _entry = entry;
            _stopwatch = stopwatch;
            _raiseCommandsChanged = raiseCommandsChanged;
        }

        public void LogProcessEnd(int? exitCode = null)
        {
            _entry.Duration = _stopwatch.Elapsed;
            _entry.ExitCode = exitCode;
            _raiseCommandsChanged();
        }

        public void SetProcessId(int processId)
        {
            _entry.ProcessId = processId;
            _raiseCommandsChanged();
        }

        public void NotifyDisposed()
        {
            if (_entry.Duration == null)
            {
                _entry.Duration = _stopwatch.Elapsed;
                _raiseCommandsChanged();
            }
        }
    }

    public sealed class CommandLogEntry
    {
        public string FileName { get; }
        public string Arguments { get; }
        private DateTime StartedAt { get; }
        public bool IsOnMainThread { get; }
        public TimeSpan? Duration { get; internal set; }
        public int? ProcessId { get; set; }
        public int? ExitCode { get; set; }
        [CanBeNull] public StackTrace CallStack { get; set; }

        internal CommandLogEntry(string fileName, string arguments, DateTime startedAt, bool isOnMainThread)
        {
            FileName = fileName;
            Arguments = arguments;
            StartedAt = startedAt;
            IsOnMainThread = isOnMainThread;
        }

        public string ColumnLine
        {
            get
            {
                var duration = Duration == null
                    ? "running"
                    : $"{((TimeSpan)Duration).TotalMilliseconds:0}ms";

                var fileName = FileName;

                if (fileName.EndsWith("git.exe"))
                {
                    fileName = "git";
                }

                var pid = ProcessId == null ? "     " : $"{ProcessId,5}";
                var exit = ExitCode == null ? "  " : $"{ExitCode,2}";

                return $"{StartedAt:HH:mm:ss.fff} {duration,7} {pid} {(IsOnMainThread ? "UI" : "  ")} {exit} {fileName} {Arguments}";
            }
        }

        public string Detail
        {
            get
            {
                var s = new StringBuilder();

                s.Append("File name:  ").AppendLine(FileName);
                s.Append("Arguments:  ").AppendLine(Arguments);
                s.Append("Process ID: ").Append(ProcessId == null ? "unknown" : ProcessId.ToString()).AppendLine();
                s.Append("Started at: ").Append(StartedAt.ToString("O")).AppendLine();
                s.Append("UI Thread?: ").Append(IsOnMainThread).AppendLine();
                s.Append("Duration:   ").Append(Duration == null ? "still running" : $"{Duration.Value.TotalMilliseconds:0.###} ms").AppendLine();
                s.Append("Exit code:  ").Append(ExitCode == null ? "unknown" : ExitCode.ToString()).AppendLine();

                if (CallStack != null)
                {
                    s.AppendLine("Call stack: ").Append(CallStack);
                }
                else
                {
                    s.Append("Call stack: not captured");
                }

                return s.ToString();
            }
        }
    }

    public static class CommandLog
    {
        public static event Action CommandsChanged;

        private static ConcurrentQueue<CommandLogEntry> _queue = new ConcurrentQueue<CommandLogEntry>();

        public static bool CaptureCallStacks { get; set; }

        public static IEnumerable<CommandLogEntry> Commands => _queue;

        public static ProcessOperation LogProcessStart(string fileName, string arguments = "")
        {
            const int MaxEntryCount = 500;

            var entry = new CommandLogEntry(fileName, arguments, DateTime.Now, ThreadHelper.JoinableTaskContext.IsOnMainThread);

            if (CaptureCallStacks)
            {
                entry.CallStack = new StackTrace();
            }

            _queue.Enqueue(entry);

            // Trim extra items
            while (_queue.Count >= MaxEntryCount)
            {
                _queue.TryDequeue(out _);
            }

            CommandsChanged?.Invoke();

            return new ProcessOperation(entry, Stopwatch.StartNew(), () => CommandsChanged?.Invoke());
        }

        public static void Clear()
        {
            _queue = new ConcurrentQueue<CommandLogEntry>();
            CommandsChanged?.Invoke();
        }
    }
}
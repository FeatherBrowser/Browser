using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FeatherBrowser.Features.WebView3;

internal sealed class ResourceSampler
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    private static readonly uint BasicCountersSize =
        (uint)Marshal.SizeOf<ProcessMemoryCountersEx>();

    private static readonly uint ExtendedCountersSize =
        (uint)Marshal.SizeOf<ProcessMemoryCountersEx2>();

    private readonly object _sampleLock = new();
    private Dictionary<int, CpuBaseline> _previous = new();

    public ResourceSnapshot Sample(IReadOnlyDictionary<int, string> processIds)
    {
        ArgumentNullException.ThrowIfNull(processIds);

        lock (_sampleLock)
        {
            int requested = processIds.Count;
            int logicalProcessors = Environment.ProcessorCount;
            var rows = new List<ProcessSample>(requested);
            var next = new Dictionary<int, CpuBaseline>(requested);

            foreach (var (id, kind) in processIds)
            {
                if (id <= 0)
                    continue;

                try
                {
                    ProcessReading reading = OperatingSystem.IsWindows()
                        ? ReadWindowsProcess(id)
                        : ReadManagedProcess(id);

                    double? cpuPercent = null;

                    if (reading.Cpu is { } current)
                    {
                        if (_previous.TryGetValue(id, out var previous) &&
                            previous.Created == current.Created &&
                            current.CpuTicks >= previous.CpuTicks)
                        {
                            double elapsed = Stopwatch.GetElapsedTime(
                                previous.Timestamp, current.Timestamp).TotalSeconds;

                            double cpuSeconds =
                                (current.CpuTicks - previous.CpuTicks) /
                                (double)TimeSpan.TicksPerSecond;

                            cpuPercent = CpuPercentage(
                                0, cpuSeconds, elapsed, logicalProcessors);
                        }

                        next.Add(id, current);
                    }

                    rows.Add(new ProcessSample(
                        id,
                        kind,
                        reading.WorkingSet,
                        reading.PrivateCommit,
                        reading.PrivateWorkingSet,
                        cpuPercent));
                }
                catch (Exception ex) when (ex is ArgumentException
                    or InvalidOperationException
                    or Win32Exception
                    or NotSupportedException)
                {
                    Trace.WriteLine($"Could not sample process {id} ({kind}): {ex.Message}");
                }
            }

            _previous = next;

            return new ResourceSnapshot(rows.AsReadOnly(), requested, DateTime.UtcNow);
        }
    }

    internal static double? CpuPercentage(
        double previous,
        double current,
        double elapsedSeconds,
        int logicalProcessors)
    {
        if (!double.IsFinite(previous) ||
            !double.IsFinite(current) ||
            !double.IsFinite(elapsedSeconds) ||
            previous < 0 ||
            current < previous ||
            elapsedSeconds <= 0 ||
            logicalProcessors <= 0)
        {
            return null;
        }

        return Math.Clamp(
            (current - previous) / elapsedSeconds / logicalProcessors * 100,
            0,
            100);
    }

    private static ProcessReading ReadWindowsProcess(int id)
    {
        using SafeProcessHandle handle = OpenProcess(
            ProcessQueryLimitedInformation, false, id);

        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var counters = new ProcessMemoryCountersEx2
        {
            Basic = new ProcessMemoryCountersEx { Size = ExtendedCountersSize },
            PrivateWorkingSetSize = nuint.MaxValue
        };

        long? privateWorkingSet = null;

        if (GetProcessMemoryInfo(handle, ref counters, ExtendedCountersSize))
        {
            if (counters.PrivateWorkingSetSize != nuint.MaxValue)
                privateWorkingSet = (long)counters.PrivateWorkingSetSize;
        }
        else
        {
            counters.Basic = new ProcessMemoryCountersEx { Size = BasicCountersSize };

            if (!GetProcessMemoryInfo(handle, ref counters, BasicCountersSize))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        CpuBaseline? cpu = null;

        if (GetProcessTimes(handle, out var created, out _, out var kernel, out var user))
        {
            cpu = new CpuBaseline(
                created.Ticks,
                kernel.Ticks + user.Ticks,
                Stopwatch.GetTimestamp());
        }

        return new ProcessReading(
            (long)counters.Basic.WorkingSetSize,
            (long)counters.Basic.PrivateUsage,
            privateWorkingSet,
            cpu);
    }

    private static ProcessReading ReadManagedProcess(int id)
    {
        using var process = Process.GetProcessById(id);

        var cpu = new CpuBaseline(
            (ulong)process.StartTime.ToUniversalTime().Ticks,
            (ulong)process.TotalProcessorTime.Ticks,
            Stopwatch.GetTimestamp());

        return new ProcessReading(
            process.WorkingSet64,
            process.PrivateMemorySize64,
            PrivateWorkingSet: null,
            Cpu: cpu);
    }

    private readonly record struct CpuBaseline(
        ulong Created,
        ulong CpuTicks,
        long Timestamp);

    private readonly record struct ProcessReading(
        long WorkingSet,
        long PrivateCommit,
        long? PrivateWorkingSet,
        CpuBaseline? Cpu);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;

        public readonly ulong Ticks => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx2
    {
        public ProcessMemoryCountersEx Basic;
        public nuint PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        SafeProcessHandle process,
        out FileTime creationTime,
        out FileTime exitTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [DllImport("psapi.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(
        SafeProcessHandle process,
        ref ProcessMemoryCountersEx2 counters,
        uint size);
}
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FeatherBrowser.Features.WebView3;

internal sealed class ResourceSampler
{
    private Dictionary<int, (long Started, double Cpu, long Timestamp)> _previous = [];

    public ResourceSnapshot Sample(IReadOnlyDictionary<int, string> processIds)
    {
        var rows = new List<ProcessSample>();
        var next = new Dictionary<int, (long Started, double Cpu, long Timestamp)>();

        foreach (var (id, kind) in processIds)
        {
            try
            {
                using Process process = Process.GetProcessById(id);

                long started = process.StartTime.ToUniversalTime().Ticks;
                double cpu = process.TotalProcessorTime.TotalSeconds;
                long now = Stopwatch.GetTimestamp();

                double? usage = null;

                if (_previous.TryGetValue(id, out var previous) &&
                    previous.Started == started)
                {
                    usage = CpuPercentage(
                        previous.Cpu,
                        cpu,
                        Stopwatch.GetElapsedTime(previous.Timestamp, now).TotalSeconds,
                        Environment.ProcessorCount);
                }

                next[id] = (started, cpu, now);

                rows.Add(
                    new ProcessSample(
                        id,
                        kind,
                        process.WorkingSet64,
                        process.PrivateMemorySize64,
                        PrivateWorkingSet(process),
                        usage));
            }
            catch (Exception ex) when (
                ex is ArgumentException
                    or InvalidOperationException
                    or Win32Exception
                    or NotSupportedException)
            {
            }
        }

        _previous = next;

        return new ResourceSnapshot(
            rows,
            processIds.Count,
            DateTime.UtcNow);
    }

    internal static double? CpuPercentage(
        double previous,
        double current,
        double elapsedSeconds,
        int logicalProcessors)
    {
        if (elapsedSeconds <= 0 ||
            current < previous ||
            logicalProcessors <= 0)
        {
            return null;
        }

        return Math.Clamp(
            (current - previous) /
            elapsedSeconds /
            logicalProcessors *
            100,
            0,
            100);
    }

    private static long? PrivateWorkingSet(Process process)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            return null;

        try
        {
            var counters = new ProcessMemoryCountersEx2 { cb = (uint)Marshal.SizeOf<ProcessMemoryCountersEx2>() };
            return GetProcessMemoryInfo(process.Handle, ref counters, counters.cb) &&
                   (counters.PrivateWorkingSetSize > 0 || counters.WorkingSetSize == 0)
                ? (long)counters.PrivateWorkingSetSize
                : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx2
    {
        public uint cb;
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
        public nuint PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCountersEx2 counters, uint size);
}

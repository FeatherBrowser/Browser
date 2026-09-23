using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FeatherBrowser.Features.WebView3;

// Own one sampler per browser window. The caller serializes samples off the UI thread.
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
                if (_previous.TryGetValue(id, out var previous) && previous.Started == started)
                    usage = CpuPercentage(previous.Cpu, cpu, Stopwatch.GetElapsedTime(previous.Timestamp, now).TotalSeconds, Environment.ProcessorCount);
                next[id] = (started, cpu, now);
                rows.Add(new ProcessSample(id, kind, process.WorkingSet64, process.PrivateMemorySize64, ReadPrivateWorkingSet(id), usage));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
            {
            }
        }
        _previous = next;
        return new ResourceSnapshot(rows, processIds.Count, DateTime.UtcNow);
    }

    internal static double? CpuPercentage(double previous, double current, double elapsedSeconds, int logicalProcessors)
    {
        if (elapsedSeconds <= 0 || current < previous || logicalProcessors <= 0) return null;
        return Math.Clamp((current - previous) / elapsedSeconds / logicalProcessors * 100, 0, 100);
    }

    private static long? ReadPrivateWorkingSet(int id)
    {
        using SafeProcessHandle handle = OpenProcess(0x1000, false, id);
        if (handle.IsInvalid) return null;
        var counters = new MemoryCounters { Size = (uint)Marshal.SizeOf<MemoryCounters>(), PrivateWorkingSet = UIntPtr.MaxValue };
        return GetProcessMemoryInfo(handle, ref counters, counters.Size) && counters.PrivateWorkingSet != UIntPtr.MaxValue
            && counters.PrivateWorkingSet.ToUInt64() > 0 && counters.PrivateWorkingSet.ToUInt64() <= counters.WorkingSet.ToUInt64()
            ? checked((long)counters.PrivateWorkingSet.ToUInt64()) : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryCounters
    {
        public uint Size, PageFaultCount;
        public UIntPtr PeakWorkingSet, WorkingSet, QuotaPeakPagedPool, QuotaPagedPool;
        public UIntPtr QuotaPeakNonPagedPool, QuotaNonPagedPool, Pagefile, PeakPagefile;
        public UIntPtr PrivateUsage, PrivateWorkingSet;
        public ulong SharedCommitUsage;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("psapi.dll", EntryPoint = "GetProcessMemoryInfo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(SafeProcessHandle process, ref MemoryCounters counters, uint size);
}

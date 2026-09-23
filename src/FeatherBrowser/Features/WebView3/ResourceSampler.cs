using System.ComponentModel;
using System.Diagnostics;

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
                        null,
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
}
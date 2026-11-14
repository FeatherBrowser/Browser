using System;
using System.Collections.Generic;
using System.Linq;

namespace FeatherBrowser.Features.WebView3;

internal sealed record ProcessSample(
    int Id,
    string Kind,
    long WorkingSet,
    long PrivateCommit,
    long? PrivateWorkingSet,
    double? CpuPercent);

internal sealed record ResourceSnapshot(
    IReadOnlyList<ProcessSample> Processes,
    int RequestedProcesses,
    DateTime CapturedUtc)
{
    public static ResourceSnapshot Empty { get; } =
        new(Array.Empty<ProcessSample>(), 0, DateTime.MinValue);

    public bool IsPartial => Processes.Count != RequestedProcesses;

    public bool HasPrivateWorkingSet =>
        Processes.Count > 0 && Processes.All(process => process.PrivateWorkingSet.HasValue);

    public bool HasCompleteCpuSample =>
        Processes.Count > 0 &&
        !IsPartial &&
        Processes.All(process => process.CpuPercent.HasValue);

    public long MemoryBytes => HasPrivateWorkingSet
        ? Processes.Sum(process => process.PrivateWorkingSet.GetValueOrDefault())
        : PrivateCommitBytes;

    public long WorkingSetBytes => Processes.Sum(process => process.WorkingSet);

    public long PrivateCommitBytes => Processes.Sum(process => process.PrivateCommit);

    public string MemoryLabel => HasPrivateWorkingSet ? "Private RAM" : "Private commit";

    public double? CpuPercent
    {
        get
        {
            double? total = null;

            foreach (var process in Processes)
            {
                if (process.CpuPercent is double percent)
                    total = (total ?? 0) + percent;
            }

            return total;
        }
    }
}
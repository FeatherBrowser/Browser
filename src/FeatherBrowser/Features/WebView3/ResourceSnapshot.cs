namespace FeatherBrowser.Features.WebView3;

internal sealed record ProcessSample(int Id, string Kind, long WorkingSet, long PrivateCommit, long? PrivateWorkingSet, double? CpuPercent);

internal sealed record ResourceSnapshot(IReadOnlyList<ProcessSample> Processes, int RequestedProcesses, DateTime CapturedUtc)
{
    public static ResourceSnapshot Empty { get; } = new([], 0, DateTime.MinValue);
    public bool HasPrivateWorkingSet => Processes.Count > 0 && Processes.All(p => p.PrivateWorkingSet.HasValue);
    public bool IsPartial => Processes.Count != RequestedProcesses;
    public long MemoryBytes => HasPrivateWorkingSet ? Processes.Sum(p => p.PrivateWorkingSet!.Value) : Processes.Sum(p => p.PrivateCommit);
    public long WorkingSetBytes => Processes.Sum(p => p.WorkingSet);
    public long PrivateCommitBytes => Processes.Sum(p => p.PrivateCommit);
    public string MemoryLabel => HasPrivateWorkingSet ? "Private RAM" : "Private commit";
    public double? CpuPercent => Processes.Any(p => p.CpuPercent.HasValue) ? Processes.Sum(p => p.CpuPercent ?? 0) : null;
}

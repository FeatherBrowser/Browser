using FeatherBrowser.Features.WebView3;

namespace FeatherBrowser.Tests;

public class ResourceTests
{
    [Theory]
    [InlineData(10, 14, 2, 4, 50d)]
    [InlineData(10, 10, 2, 4, 0d)]
    [InlineData(0, 100, 1, 2, 100d)]
    [InlineData(10, 9, 2, 4, null)]
    [InlineData(10, 14, 0, 4, null)]
    [InlineData(10, 14, -1, 4, null)]
    [InlineData(10, 14, 2, 0, null)]
    [InlineData(10, 14, 2, -1, null)]
    public void CpuUsageIsNormalizedClampedAndRejectsInvalidBaselines(
        double previous, double current, double elapsed, int processors, double? expected)
        => Assert.Equal(expected, ResourceSampler.CpuPercentage(previous, current, elapsed, processors));

    [Fact]
    public void UsesPrivateRamWhenEveryProcessProvidesIt()
    {
        var snapshot = new ResourceSnapshot([
            new(1, "UI", 500, 350, 200, 2), new(2, "Renderer", 400, 300, 100, 3)], 2, DateTime.UnixEpoch);
        Assert.Equal(300L, snapshot.MemoryBytes);
        Assert.Equal(900L, snapshot.WorkingSetBytes);
        Assert.Equal(650L, snapshot.PrivateCommitBytes);
        Assert.Equal("Private RAM", snapshot.MemoryLabel);
        Assert.Equal(5d, snapshot.CpuPercent);
        Assert.False(snapshot.IsPartial);
    }

    [Fact]
    public void MissingPrivateRamUsesConsistentCommitMetricForAllProcesses()
    {
        var snapshot = new ResourceSnapshot([
            new(1, "UI", 500, 350, 200, null), new(2, "Renderer", 400, 300, null, null)], 3, DateTime.UnixEpoch);
        Assert.Equal(650L, snapshot.MemoryBytes);
        Assert.Equal("Private commit", snapshot.MemoryLabel);
        Assert.True(snapshot.IsPartial);
        Assert.Null(snapshot.CpuPercent);
    }

    [Fact]
    public void EmptySnapshotDoesNotInventMeasurements()
    {
        Assert.Equal(0L, ResourceSnapshot.Empty.MemoryBytes);
        Assert.Null(ResourceSnapshot.Empty.CpuPercent);
        Assert.False(ResourceSnapshot.Empty.IsPartial);
    }

    [Fact]
    public void ExitedOrUnknownProcessProducesPartialSnapshot()
    {
        var snapshot = new ResourceSampler().Sample(new Dictionary<int, string> { [int.MaxValue] = "Exited" });
        Assert.Empty(snapshot.Processes);
        Assert.True(snapshot.IsPartial);
        Assert.Null(snapshot.CpuPercent);
    }
}

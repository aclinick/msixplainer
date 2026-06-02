using MSIXplainer.Services;

namespace MSIXplainer.Tests;

public class BandwidthPlannerServiceTests
{
    [Fact]
    public void Calculate_BasicNumbers()
    {
        // 10 MB delta, 1000 devices => 10 GB total transfer.
        var deltaPerDevice = 10L * 1024 * 1024;
        var est = BandwidthPlannerService.Calculate(
            deltaBytesPerDevice: deltaPerDevice,
            deviceCount: 1000,
            linkSpeedsMbps: [100],
            costPerGigabyteUsd: 0.08);

        Assert.Equal(deltaPerDevice, est.DeltaBytesPerDevice);
        Assert.Equal(1000, est.DeviceCount);
        Assert.Equal(deltaPerDevice * 1000, est.TotalBytes);

        Assert.NotNull(est.EstimatedCostUsd);
        // ~10.486 GB * $0.08 ≈ $0.838
        Assert.InRange(est.EstimatedCostUsd!.Value, 0.83, 0.85);

        var proj = Assert.Single(est.LinkProjections);
        Assert.Equal(100, proj.LinkSpeedMbps);
        // 10 MB ≈ 83_886_080 bits / 100_000_000 bits/s ≈ 0.84 s per device
        Assert.InRange(proj.PerDeviceDuration.TotalSeconds, 0.83, 0.85);
        // Serial fleet ≈ 838 s
        Assert.InRange(proj.SerialFleetDuration.TotalSeconds, 838, 840);
    }

    [Fact]
    public void Calculate_NoCost_OmitsEstimate()
    {
        var est = BandwidthPlannerService.Calculate(1024, 10, [10]);
        Assert.Null(est.EstimatedCostUsd);
        Assert.Null(est.CostPerGigabyteUsd);
    }

    [Fact]
    public void Calculate_MultipleLinkSpeeds_AllProjected()
    {
        var est = BandwidthPlannerService.Calculate(
            deltaBytesPerDevice: 1_000_000,
            deviceCount: 1,
            linkSpeedsMbps: [10, 100, 1000]);

        Assert.Equal(3, est.LinkProjections.Count);

        // Higher link speed => shorter duration.
        var s10 = est.LinkProjections[0].PerDeviceDuration;
        var s100 = est.LinkProjections[1].PerDeviceDuration;
        var s1000 = est.LinkProjections[2].PerDeviceDuration;
        Assert.True(s10 > s100);
        Assert.True(s100 > s1000);
    }

    [Theory]
    [InlineData(-1, 1, 100)]
    [InlineData(0, 0, 100)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 1, -10)]
    public void Calculate_RejectsInvalidInputs(long delta, int devices, int link)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BandwidthPlannerService.Calculate(delta, devices, [link]));
    }

    [Fact]
    public void Calculate_RejectsNegativeCost()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BandwidthPlannerService.Calculate(1024, 1, [100], costPerGigabyteUsd: -0.01));
    }

    [Fact]
    public void Calculate_ZeroDelta_StillValid()
    {
        var est = BandwidthPlannerService.Calculate(0, 100, [100], costPerGigabyteUsd: 0.10);
        Assert.Equal(0, est.TotalBytes);
        Assert.Equal(0, est.EstimatedCostUsd);
        Assert.All(est.LinkProjections, p => Assert.Equal(TimeSpan.Zero, p.PerDeviceDuration));
    }
}

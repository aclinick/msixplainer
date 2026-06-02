using MSIXplainer.Models;

namespace MSIXplainer.Services;

/// <summary>
/// Translates an update delta size into bandwidth and cost projections for
/// a planned device fleet rollout. Pure deterministic math — no I/O.
/// </summary>
public static class BandwidthPlannerService
{
    private const long BitsPerByte = 8;
    private const long MegabitsToBits = 1_000_000;
    private const long BytesPerGigabyte = 1_000_000_000;

    /// <summary>
    /// Computes total transfer size, per-device download time, and optional cost
    /// for the given delta and rollout parameters.
    /// </summary>
    /// <param name="deltaBytesPerDevice">Bytes one device downloads for the update.</param>
    /// <param name="deviceCount">Number of devices in the rollout. Must be ≥ 1.</param>
    /// <param name="linkSpeedsMbps">One or more link speeds to project against (Mbit/s).</param>
    /// <param name="costPerGigabyteUsd">Optional egress cost in USD per GB (decimal billing).</param>
    public static BandwidthEstimate Calculate(
        long deltaBytesPerDevice,
        int deviceCount,
        IReadOnlyList<int> linkSpeedsMbps,
        double? costPerGigabyteUsd = null)
    {
        if (deltaBytesPerDevice < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaBytesPerDevice), "Delta cannot be negative.");
        if (deviceCount < 1)
            throw new ArgumentOutOfRangeException(nameof(deviceCount), "Device count must be at least 1.");
        ArgumentNullException.ThrowIfNull(linkSpeedsMbps);

        var totalBytes = deltaBytesPerDevice * deviceCount;

        var projections = new List<LinkSpeedProjection>(linkSpeedsMbps.Count);
        foreach (var mbps in linkSpeedsMbps)
        {
            if (mbps <= 0)
                throw new ArgumentOutOfRangeException(nameof(linkSpeedsMbps), $"Link speed must be positive (got {mbps}).");

            var perDeviceSeconds = (double)(deltaBytesPerDevice * BitsPerByte) / (mbps * MegabitsToBits);
            var fleetSeconds = perDeviceSeconds * deviceCount;

            projections.Add(new LinkSpeedProjection
            {
                LinkSpeedMbps = mbps,
                PerDeviceDuration = TimeSpan.FromSeconds(perDeviceSeconds),
                SerialFleetDuration = TimeSpan.FromSeconds(fleetSeconds)
            });
        }

        double? cost = null;
        if (costPerGigabyteUsd is { } cpg)
        {
            if (cpg < 0)
                throw new ArgumentOutOfRangeException(nameof(costPerGigabyteUsd), "Cost per GB cannot be negative.");
            cost = (double)totalBytes / BytesPerGigabyte * cpg;
        }

        return new BandwidthEstimate
        {
            DeltaBytesPerDevice = deltaBytesPerDevice,
            DeviceCount = deviceCount,
            TotalBytes = totalBytes,
            CostPerGigabyteUsd = costPerGigabyteUsd,
            EstimatedCostUsd = cost,
            LinkProjections = projections
        };
    }
}

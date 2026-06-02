namespace MSIXplainer.Models;

/// <summary>
/// Bandwidth planning numbers derived from a delta size and rollout parameters.
/// </summary>
public sealed class BandwidthEstimate
{
    public required long DeltaBytesPerDevice { get; init; }
    public required int DeviceCount { get; init; }
    public required long TotalBytes { get; init; }
    public required double? CostPerGigabyteUsd { get; init; }
    public required double? EstimatedCostUsd { get; init; }

    /// <summary>Per-link projections (e.g. 10/100/1000 Mbps).</summary>
    public required IReadOnlyList<LinkSpeedProjection> LinkProjections { get; init; }
}

public sealed class LinkSpeedProjection
{
    public required int LinkSpeedMbps { get; init; }

    /// <summary>Time for a single device to pull the delta over this link.</summary>
    public required TimeSpan PerDeviceDuration { get; init; }

    /// <summary>
    /// Wall-clock time if every device pulls the delta serially through one link.
    /// (Useful as a worst-case ceiling for a shared bottleneck.)
    /// </summary>
    public required TimeSpan SerialFleetDuration { get; init; }
}

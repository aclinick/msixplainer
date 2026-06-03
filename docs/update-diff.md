# Update Diff & Bandwidth Planner

How MSIXplainer answers the question: *"If we push v1.1 of this app to 5,000 devices, how much will actually download and how long will it take?"*

## Why this isn't just `(size of new package)`

MSIX updates are **block-level**, not file-level. Each package ships with an `AppxBlockMap.xml` that breaks every file into ~64 KB blocks and records the SHA-256 hash of each block. When a client installs an update, it only downloads blocks whose hashes don't already exist locally.

So the real "update size" is:

```
update bytes = sum of bytes of blocks present in v1.1 BUT NOT in v1.0
```

A point-release where 90% of binaries are byte-identical might only transfer 10-20 MB even though the new package is 300 MB.

## Parity with `comparepackage.exe`

MSIXplainer's `UpdateDiffService` is **byte-exact** with Microsoft's `comparepackage.exe` from the Windows SDK. Verified against real packages including Microsoft Teams (`209,649,656` bytes — identical between the two tools).

Specifically:

- Block-hash comparison uses the same SHA-256 hashes encoded in `AppxBlockMap.xml`
- File-rename detection (a file moved from `bin/` to `lib/` with the same content) recognizes blocks as identical even though paths differ
- The "downloaded bytes" metric counts each unique new block exactly once, regardless of how many files contain it (duplicate detection)
- Bundle handling pairs inner packages by architecture (`x64` ↔ `x64`, `arm64` ↔ `arm64`) and sums their diffs

If you ever see a discrepancy with `comparepackage.exe`, that's a bug — please file an issue.

## Bandwidth planner formulas

Once you have `deltaBytesPerDevice` from the diff, the `BandwidthPlannerService` projects fleet impact using deterministic math (no I/O, no estimation heuristics):

```
totalBytes               = deltaBytesPerDevice × deviceCount

perDeviceSeconds         = (deltaBytesPerDevice × 8) / (linkSpeedMbps × 1_000_000)
serialFleetSeconds       = perDeviceSeconds × deviceCount

costUsd                  = (totalBytes / 1_000_000_000) × costPerGigabyteUsd
```

Notes on the units:

- **Mbps means decimal megabits** — `1_000_000` bits, not `1_048_576`. This matches how ISPs and Azure ExpressRoute bill bandwidth.
- **GB means decimal gigabytes** — `1_000_000_000` bytes, not `1_073_741_824`. Same reason.
- **`serialFleetDuration`** assumes one device at a time on the link — a worst-case lower-bound for sizing. Real concurrent rollouts will be faster, but the serial number is honest for capacity planning ("if I have ONE 100 Mbps WAN link feeding a branch office, how long is the trickle?").

## Top-N changed files

The diff report includes a per-file size delta table. By default the top 25 changed files (by added bytes) are shown; tune with `--top N`. Files that were renamed-but-identical are marked as such and contribute zero downloaded bytes.

## Duplicate detection

If the same content appears at multiple paths inside a package (common with localized resources, .NET satellite assemblies, etc.), the diff reports it. New copies of an already-downloaded block don't get counted twice — the user only pays the wire cost once.

## Inputs the planner doesn't model

To stay deterministic and honest, the planner intentionally does **not** model:

- Compression on the wire (HTTP transfer encoding) — MSIX is already deflate-compressed; double-counting would over-estimate savings
- Concurrent download fan-out — depends on your CDN, P2P (Delivery Optimization), bandwidth shaping, etc.
- Retry traffic / failure rates — environment-specific
- Storage costs at the CDN — only egress cost is modeled

Use the serial fleet duration as your "this is the slowest it could possibly be" sanity number, then divide by your real concurrency factor.

# Yet another Optimizer

![RimWorld 1.7](https://img.shields.io/badge/RimWorld-1.7-blue)
![License MIT](https://img.shields.io/badge/license-MIT-blue)

*Yet another Optimizer* (YaOpt) is a performance optimization mod for *RimWorld*. It improves TPS and FPS through Harmony patching and targeted caching.

## Features

Optimizations are grouped into three categories, all configurable in the in-game mod settings:

- **TPS** — Work-giver and pawn-list caching, ideology checks, stat caching, meditation/bed/idle throttling, lister and spawned-thing removal indexing.
- **FPS** — Pawn render node caching, silhouette cache optimization, texture lookup caching, mesh update throttling, and text parsing improvements.
- **Misc** — Accelerated XML/translation processing and startup-time caches.

The 1.7 line includes loading-only world-map prewarming, built-in Prepatcher patches and targeted compatibility optimizations. The retired 1.6 implementations have been removed; their setting IDs remain only as hidden save-compatibility placeholders. Burst acceleration is not available on 1.7.

## Requirements

- [RimWorld 1.7](https://store.steampowered.com/app/294100)
- [Harmony](https://github.com/pardeike/Harmony)

## Installation

1. Download the latest release
2. Extract to `RimWorld/Mods/`
3. Enable in the mod menu

## Building from Source

```
dotnet build Source/YaOpt.sln -c Release
```

The build references `Assembly-CSharp.dll`, `UnityStubs.dll` and `Waylith.dll` from the RimWorld install directory. The output is written to `1.7/Assemblies/YaOpt.dll`.

The last 1.6 release, including its source and binaries, is preserved on the `1.6` branch.

## License

MIT
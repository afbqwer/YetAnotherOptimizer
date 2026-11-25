# YetAnotherOptimizer Development Guide

This project is a high-performance optimization mod for **RimWorld 1.7**. The `master` branch is 1.7-only; the complete 1.6 source and binaries are preserved on the `1.6` branch.

The 1.6 parallel tick/work-giver implementation, lock-injection framework, old Prepatcher patches, compatibility submods and retired texture/Burst paths have been removed. Their setting IDs remain in `YaOptSettings.LockedOffOptimizations` only so old configuration files still load safely.

## 1. Project Architecture

### 1.1 `Source/YaOpt` (Core Module)

- `Patches/`: Harmony patches organized by target namespace.
  - `Early/`: Patches applied before other mods load through the `YetAnotherOptimizer.Early` Harmony instance.
  - `WorldMap/`: World-map and optional-mod compatibility optimizations.
  - `Compatibility/HumanoidAlienRaces/`: The remaining runtime compatibility patch, gated by `OptHARNeedCondition`.
  - `Debugging/`: Diagnostic patches, applied only in Debug builds where marked.
  - `Xml/`: Setting-gated XML patch operations.
- `Prepatch/`: Active 1.7 free patches (`PrepatcherFreePatches`, `PrepatcherMethods`, `PrepatcherState`, `PrepatcherFields`) running through `RimWorldWaylith.PrepatcherCompat`.
- `Helpers/`: Cache implementations, lifecycle callbacks, compatibility detection and small shared utilities.
  - `ParallelPreDrawHelper` now contains only the active fast `RecacheRequested` path.
  - `ThreadLocal/ThreadLocalHelper.cs` only creates per-thread scratch sets.
  - `ThreadSafe/GreedySpinLock.cs` and `LockException.cs` support `TransientPool<T>`; the old lock-injection framework is gone.
- `YaOptMod.cs`: Mod entry point, settings loading, early patching and logging.
- `YaOptGlobal.cs`: Shared state, soft-dependency lookups, main-thread marker and option snapshots.
- `CompatibilityChecker.cs`: Compatibility checks for other optimizer mods.
- `CompatibilityDef.cs` / `Defines/CompatibilityDefines.cs`: Active `BannedOptimizations` and `IgnoredToggleTabCaching` XML data.
- `PostIniter.cs`: Startup initialization and patch scheduling.
- `Settings/`: Option registration metadata and the settings window.

### 1.2 Other Source Directories

- `Source/NativeVEH/`: Native crash-reporting shim. It is not a managed mod assembly and must stay outside `Assemblies/`.
- `Source/YaOpt.Unity` and `Source/YaOpt.OtherMod.*`: Exist only on the `1.6` branch.

## 2. Directory Structure

```
Root
├── 1.7/                        # Release directory
│   ├── Assemblies/             # YaOpt.dll
│   └── Native/                 # YaOpt.NativeVEH.dll
├── Common/
│   ├── Defs/                   # Setting-gated XML patches and active CompatibilityDef data
│   └── Languages/              # ChineseSimplified, ChineseTraditional, English
├── Source/
│   ├── YaOpt/                  # Core C# project
│   │   ├── Defines/
│   │   ├── Helpers/
│   │   ├── Patches/
│   │   ├── Prepatch/
│   │   └── Settings/
│   ├── NativeVEH/
│   ├── Settings.props
│   └── YaOpt.sln
├── About/
├── LoadFolders.xml
└── Analyzer.xml
```

## 3. Coding Conventions

### 3.1 Naming and Style

- Class/method/property names use `PascalCase`; private fields use `_camelCase`; parameters and locals use `camelCase`.
- Use Allman braces, tabs for indentation, LF line endings and UTF-8.
- Ignore non-standard naming in `CompatibilityDef.cs`, where names mirror RimWorld XML deserialization.

### 3.2 Performance Guidelines

- Do not use LINQ in hot paths or introduce per-call classes, closures, `params object[]` or avoidable allocations.
- Use `[MethodImpl(MethodImplOptions.AggressiveInlining)]` only for small, frequently called methods.
- Prefer structs for hot-path data containers and pass them by `ref` when it avoids copying.
- Game ticks and cache invalidation are single-threaded. Independent 1.7 paths, such as world-map landform prewarming, manage their own concurrency and do not use the retired `NeedThreadSafe` machinery.
- `TransientPool<T>` is the remaining shared pool and uses `GreedySpinLock`; do not reintroduce the deleted lock-injection framework.

### 3.3 Harmony Patch Standards

- Prefer transpilers over prefixes/postfixes when modifying an existing method body.
- Match IL defensively and leave the original method untouched when an expected shape is missing.
- Explain why non-obvious IL manipulation is required.
- `[HarmonyPatch]` is standard; `[ManualPatch]` is for custom patch selection; `[EarlyPatch]` marks early patches.
- Patch classes are `internal static`. Name files `Namespace_Type_Method.cs`, or use `MultiTargets_` for multi-target patches.
- Current Harmony IDs are `YetAnotherOptimizer` and `YetAnotherOptimizer.Early`.

### 3.4 Accessing Vanilla Members

The project compiles against a publicized `Assembly-CSharp` (`Krafs.Publicizer`). At runtime the original assembly is loaded, so `AssemblyAttributes.cs` must continue emitting `IgnoresAccessChecksTo` for `Assembly-CSharp` and `0Harmony`. Removing those attributes or the publicizer strategies causes `MemberAccessException` on private-member access.

- Call publicized vanilla members directly instead of caching reflection delegates in hot paths.
- Use `nameof` for member names passed to `AccessTools`, `CodeInstruction` or string-based Harmony APIs.
- Prefer `typeof(X)` over `AccessTools.TypeByName` for vanilla types.
- Keep reflection for other mods, BCL members such as `List<T>._version`, and runtime generic dispatch.
- Publicizing does not devirtualize. An override of a publicized virtual member must not narrow its runtime visibility.

## 4. Build and Test

- Version parameters are in `Source/Settings.props`; local overrides are in gitignored `Source/UserSettings.props`.
- Build with `dotnet build Source/YaOpt.sln -c Release`. Ship Release builds only.
- Output: `1.7/Assemblies/YaOpt.dll`.
- Build the native shim with `Source/NativeVEH/build.bat`; output: `1.7/Native/YaOpt.NativeVEH.dll`.
- `DEBUG` enables diagnostic patches and profiling output.
- In-game verification is still required for save loading, map changes and long-running stability. See `1.7-MIGRATION-AUDIT.md` for the remaining smoke-test checklist.

## 5. Adding a New Optimization

1. Register an `OptimizationOption` property in `YaOptSettings.cs` and add translation keys.
2. Implement the patch under `Source/YaOpt/Patches/` and any helper under `Helpers/`.
3. Gate the patch on the option and preserve vanilla behavior when the patch cannot be applied.
4. Build, then test in RimWorld with a new save and an existing save.

## 6. Threading Model

- Thing ticks are serial on the Unity main thread; there are no concurrent Thing ticks or cache clears.
- `YaOptGlobal.IsInMainThread` remains active for optimizations that may run while a save is loading on a background thread.
- The 1.6 `Parallel*` managers, `Patches/ThreadSafe`, `Helpers/ThreadSafe` lock injectors, `YaOptSubMod` framework and old compatibility XML fields no longer exist.
- Locked setting properties are compatibility placeholders only. Do not add implementations for them on this branch.
- `OptWorldMapLandformsParallel` uses `System.Threading.Tasks` for loading-only Geological Landforms cache warming and is gated independently.

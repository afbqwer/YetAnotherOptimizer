# YetAnotherOptimizer Development Guide

This project is a high-performance optimization mod for **RimWorld 1.7**, designed to increase game frame rates (TPS/FPS) through Harmony patches, cache optimization and throttling. The `master` branch is 1.7-only and single-threaded; multithreading, Burst and other-mod compatibility optimizations are disabled. The 1.6 line, including `Source/YaOpt.Unity`, `Source/YaOpt.OtherMod.*` and the 1.6 binaries, is preserved on the `1.6` branch.

## 1. Project Architecture

The `master` branch contains the core module; the other two modules exist only on the `1.6` branch:

### 1.1 `Source/YaOpt` (Core Module)
- **Purpose**: Contains the main logic of the mod, Harmony patches, and general helper tools.
- **Key Components**:
  - `Patches/`: Harmony patches organized by target namespace. Subdirectories:
    - `Early/` — Patches applied before other mods load via the `EarlyHarmony` instance (ID `"YetAnotherOptimizer.Early"`).
    - `Prepatch/` — Patches for the Prepatcher mod.
    - `ThreadSafe/` — Thread-safety patches. Subcategories: `ThreadLocal/` (per-thread instance fields), `ThreadStatic/` (simple `[ThreadStatic]` replacements), `Locked/` (critical section lock injection), `Delayed/` (deferred execution). **1.6 legacy — never applied on this branch, see section 6.**
    - `Compatibility/` — Runtime compatibility patches for other mods, plus `Unpatcher.cs` for conflict resolution.
    - `Debugging/` — Debugging and diagnostic patches.
  - `Helpers/`: Utility classes including:
    - `ParallelPawnTickManager`, `ParallelMapTickManager`, `ParallelWorkGiver` — Multi-threaded tick computations via Unity Job System. **1.6 legacy — locked off on this branch, see section 6.**
    - `UpdateCallbackHelper.cs` — Central event system for game lifecycle hooks (pre-tick, post-tick, pre-render, post-render, cache-clear).
    - `ThreadLocal/` — Per-thread helper classes for common APIs. **1.6 legacy.**
    - `ThreadSafe/` — Lock primitives (`GreedySpinLock`, `GreedyMonitor`) and `LockPatchManager`. **1.6 legacy.**
  - `YaOptMod.cs`: Handles initialization, settings loading, early patching, and logging.
  - `YaOptGlobal.cs`: Central hub for mod state, settings, platform capabilities, thread-safety flags.
  - `YaOptSubMod.cs`: Base class for sub-modules with lifecycle hooks (`OnPreInit`, `OnInit`, `OnPostInit`, `OnPatch`, `OnUnpatch`).
  - `CompatibilityChecker.cs`: Validates compatibility with other optimizer mods (Performance Fish, Butter++, Performance Optimizer).
  - `PostIniter.cs`: Startup initializer that triggers patching after game loads.
  - `NativeLoader.cs`: Loads platform-specific Burst native libraries.

### 1.2 `Source/YaOpt.Unity` (Burst Module, 1.6 branch only)
- **Purpose**: Utilizes Unity's **Burst Compiler** for high-performance mathematical operations and data processing.
- **Features**:
  - For reference only. It is not a complete C# project. It needs to be imported into a Unity project for compilation.
  - Uses `Unity.Mathematics` (`float4`, `float3`) instead of standard `Vector3` to leverage SIMD instructions.
  - Uses `[BurstCompile]` to mark critical paths.
  - Strictly prohibits referencing any managed objects (reference types); only uses `NativeArray` and unmanaged structures (`struct`).

### 1.3 `Source/YaOpt.OtherMod.*` (Compatibility Submods, 1.6 branch only)
- **Purpose**: Compatibility patches for specific popular mods (e.g., `CombatExtended`, `FacialAnimation`, `HumanoidAlienRaces`, `ImageOpt`, `VanillaExpandedFramework`).
- **Architecture**: Each project extends `YaOptSubMod` and receives lifecycle calls during initialization and patching.
- **Principles**: Must reference target mod APIs via reflection or soft dependencies to avoid hard crashes if the target mod is missing.

## 2. Directory Structure

```
Root
├── 1.7/                        # Release directory
│   └── Assemblies/             # Main managed DLL (YaOpt.dll)
├── Common/
│   ├── Defs/
│   │   └── Compatibilities/    # CompatibilityDef XML files
│   └── Languages/              # Translation files (ChineseSimplified, ChineseTraditional, English)
├── Source/
│   ├── YaOpt/                  # Core C# project
│   │   ├── Defines/            # Compile-time compatibility definitions
│   │   ├── Helpers/            # Utility classes and trampolines
│   │   └── Patches/            # Harmony patches (Early, Prepatch, ThreadSafe, Compatibility, Debugging)
│   ├── NativeVEH/              # Native VEH shim (YaOptVeh.c + build.bat, builds YaOpt.NativeVEH.dll)
│   ├── Settings.props          # Shared build properties
│   └── YaOpt.sln               # Visual Studio solution
├── About/                      # Mod metadata (About.xml, icon)
├── LoadFolders.xml             # Loading logic
└── Analyzer.xml                # Dubs Performance Analyzer configuration
```

## 3. Coding Conventions

### 3.1 Naming & Style
- **Class/Method/Property**: `PascalCase` (e.g., `ParallelTickManager`, `DoSingleTick`)
- **Private Fields**: `_camelCase` (e.g., `_cachedMaterial`, `_padding0`)
- **Parameters/Local Variables**: `camelCase`
- **Brace Style**: **Allman** (braces on a new line)
  ```csharp
  if (condition)
  {
      DoSomething();
  }
  ```
- **Indentation**: **Tabs** (width 4), **LF** line endings, **UTF-8** encoding, trim trailing whitespace. Enforced by `.editorconfig`.
- **Exemption**: Ignore the non-standard naming in `Source/YaOpt/CompatibilityDef.cs`, where member names are designed for compatibility with RimWorld deserialization.

### 3.2 Performance Guidelines
Since this is an optimization mod, performance is the highest priority:
- **Hot Paths**: In `Tick`, `Draw`, `Update` loops:
  - **NO LINQ IN HOT CODE** (LINQ in the initialization code are acceptable). 
  - **Avoid Memory Allocations** (new Class(), lambda closures, params object[]).
- **Inlining**: Use `[MethodImpl(MethodImplOptions.AggressiveInlining)]` for small, frequently called methods.
- **Structs**: Strictly use `struct` in `YaOpt.Unity` and pass by `ref` to reduce copying. In core modules, prefer `struct` for hot-path data containers (e.g., `JobResult` in `ParallelWorkGiver`).
- **Unity Job System**: Use `Unity.Jobs` (`IJobParallelFor`, `JobHandle`, `NativeArray`, `NativeList`, `NativeQueue`) for multi-threaded data processing. Jobs must only access unmanaged data.
- **Thread Safety**:
  - Use `[ThreadStatic]` for simple per-thread mutable state.
  - Use `ConcurrentDictionary`, `ConcurrentBag`, `ConcurrentQueue` for thread-safe producer/consumer patterns.
  - Use `GreedySpinLock` / `GreedyMonitor` (from `Helpers/ThreadSafe/`) for lightweight critical sections when lock contention is low.
  - Lock patches declared via `CompatibilityDef` XML use `LockPatchManager` for runtime lock injection.
- **Bloom Filter**: Use for fast negative match filtering in hot paths (e.g., `CompatibilityDefines.IsJobFailurePredictingIgnored`).

### 3.3 Harmony Patch Standards
- **Transpiler Priority**: Prefer using `Transpiler` to modify IL instructions to avoid the overhead of Prefix/Postfix.
- **Defensive Programming**: When searching for instructions in a Transpiler, consider that other mods may have already modified the code.
- **Comments**: Complex IL operations must include comments explaining the intent (*Why*), not just the operation (*What*).
- **Patch Attributes**:
  - `[HarmonyPatch]` — Standard Harmony auto-detection. Used for most patches.
  - `[ManualPatch]` — For patches requiring custom logic beyond what auto-detection supports. The type must implement a static `Patch(Harmony)` or `bool Patch(Harmony)` method.
  - `[EarlyPatch]` — Marks patches to be applied on the `EarlyHarmony` instance before other mods load.
- **Harmony Instances**: The project maintains two Harmony instances:
  - `"YetAnotherOptimizer"` — Main instance for standard patches.
  - `"YetAnotherOptimizer.Early"` — Applied during mod constructor, before other mods' patches execute.
- **Patch File Naming**: Place patch files under `Patches/`. Name them `Namespace_Type_Method.cs` (e.g., `RimWorld_Pawn_Tick.cs`). For patches targeting multiple types or methods, use the `MultiTargets_` prefix (e.g., `MultiTargets_ClosestThingGlobal.cs`). Compatibility patch files use the target mod name as prefix (e.g., `CombatExtended_CompInventory_TrySwitchToWeapon.cs`).
- **Patch Class Modifiers**: All patch classes must be `internal static`.

### 3.4 Accessing Vanilla Members
The project compiles against a **publicized** copy of `Assembly-CSharp` (`Krafs.Publicizer` + `<Publicize Include="Assembly-CSharp" />` in `Source/YaOpt/YaOpt.csproj`), so non-public vanilla members are visible to the compiler. Publicizing only rewrites the copy the compiler sees — at runtime the game loads the original assembly and CoreCLR still enforces access checks. `PublicizerRuntimeStrategies` is `Unsafe;IgnoresAccessChecksTo`, and `Source/YaOpt/AssemblyAttributes.cs` hand-emits the `IgnoresAccessChecksTo` attributes for `Assembly-CSharp` and `0Harmony` (the hand-written shim is required because `GenerateAssemblyInfo=false` disables the automatic attribute mechanism). Removing the shim or the strategy re-enables runtime `MemberAccessException`s on every publicized private-member call — re-verify direct private-member access in-game before touching either.

- **Direct access first**: call `private` / `protected` / `internal` vanilla members directly instead of caching a `MethodDelegate` / `FieldRefAccess` or going through `GetValue` / `Invoke`. Direct access is checked at compile time and removes the reflection indirection from hot paths.
- **`nameof` for every member name that must stay a string**: `AccessTools.Method(typeof(X), nameof(X.M))`, `AccessTools.Field(typeof(X), nameof(X.f))`, `CodeInstruction.LoadField(typeof(X), nameof(X.f))`. A vanilla rename then breaks the build instead of failing at runtime. C# has no `infoof`, so keeping `AccessTools` to *identify* a member for a Harmony target or an IL operand is fine — only the string literal is replaced.
- **Prefer `typeof` over `TypeByName`**: nested vanilla types are publicized too, so `typeof(DynamicDrawManager.ThingCullDetails)` replaces `AccessTools.TypeByName("Verse.DynamicDrawManager/ThingCullDetails")`.
- **Keep reflection for**:
  - **Other mods** — only `Assembly-CSharp` is publicized, and a hard reference crashes when the mod is absent (see `CompatibilityChecker`, `ContentManager`, `CacheWarmer`).
  - **Members of assemblies outside `Assembly-CSharp`** — e.g. `List<T>._version` in the BCL (`Verse_ThingDef_HasAssignableCompFrom`).
  - **Generic dispatch that needs a runtime `Type`** — e.g. `ThreadLocalHelper.TryAllocate`, `ThingOwnerUtility.GetAllThingsRecursively`.
- **`MethodDelegate` arity pitfall**: `AccessTools.MethodDelegate<T>(method, null, virtualCall)` binds an *instance* method to a delegate whose parameter list matches the method's parameters and emits `this = null`. It only "works" while a prefix short-circuits the body. Put the declaring type in the delegate's first parameter, or call the member directly.
- **Visibility, not dispatch**: publicizing does not devirtualize. A `protected virtual` getter called directly is still a `callvirt`, exactly as `MethodDelegate` with the default `virtualCall: true` was. Only `virtualCall: false` skipped overrides. If this project ever overrides a publicized virtual member, declare it `public override` (or add a `<DoNotPublicize>` entry) — narrowing it back to `protected` is CS0507.
- **Cache invalidation**: Publicizer caches the rewritten assembly under `obj/Release/PublicizedAssemblies/`. If a change to the csproj or the game DLL seems to have no effect, delete that folder or rebuild with `-t:Rebuild`.

### 3.5 Documentation Style
- **Be Concise**: XML documentation should be brief and to the point.
- **Summary Only for Simple Members**: For straightforward methods/properties, a single `<summary>` line is sufficient.
- **Remarks for Complexity**: Use `<remarks>` only when explaining non-obvious behavior, thread safety, or performance implications.
- **No param/returns**: Do not write `<param>` or `<returns>` tags unless the meaning is truly ambiguous.
- **Avoid Lists**: Minimize use of `<list>` elements. Prefer direct prose.
- **Example**:
  ```csharp
  // Good - simple and clear
  /// <summary>
  /// Gets the singleton mod instance.
  /// </summary>
  public static YaOptMod Instance { get; }
  
  // Good - complex behavior warrants remarks
  /// <summary>
  /// Checks if thread-safe code paths are required.
  /// </summary>
  /// <remarks>
  /// Returns true when parallel pawn tick or job giver optimizations are enabled.
  /// </remarks>
  public static bool NeedThreadSafe => ...;
  ```

## 4. Build & Test
- **Build Config**: Version parameters are in `Source/Settings.props` (shared) and `Source/UserSettings.props` (local, gitignored; copy from `Source/UserSettings.props.template`).
- **Build**: Run `dotnet build Source\YaOpt.sln -c Release` or open `Source/YaOpt.sln` with Visual Studio 2022+. Ship Release builds only — the default Debug configuration compiles in the `DEBUG` performance-diagnostics code.
- **Native VEH shim**: Run `Source/NativeVEH/build.bat` to build `1.7/Native/YaOpt.NativeVEH.dll` (requires the MSVC toolchain paths at the top of the script). The shim backs the stack overflow reporter (`DebugHelper.InstallStackOverflowLogger`); its callback must stay pure native — managed callbacks of any kind fail the reverse-pinvoke cooperative-mode check on CoreCLR. The shim must NOT live in `Assemblies/`: the game loads every dll there as a managed mod assembly and a native dll would error on each startup.
- **Output**:
  - Main DLL: `1.7/Assemblies/YaOpt.dll`
  - Native shim: `1.7/Native/YaOpt.NativeVEH.dll`
- **Conditional Compilation**: `DEBUG` symbol enables performance diagnostics and profiling output.
- **Test**: Launch RimWorld with the mod loaded. Verify via in-game TPS display and log messages. `YAOPT_SO_TEST=1` self-tests the stack overflow logging chain (the game is expected to die from a deliberate overflow; check `YaOpt_StackOverflow_<pid>.log` in the save data folder).

## 5. Adding a New Optimization

1. **Register a toggle** in `YaOptSettings.cs` — add a boolean property with `[Unsaved]` attribute and corresponding translation keys.
2. **Write the Harmony patch** under `Source/YaOpt/Patches/` following the naming and modifier conventions in section 3.3.
3. **Add translations** in `Common/Languages/` (ChineseSimplified, ChineseTraditional, English) for the toggle label and description.
4. **Build and test** — run `dotnet build Source\YaOpt.sln`, then launch RimWorld to verify the optimization works correctly.

## 6. Threading Model

- **Game tick is single-threaded**: All Thing Tick calls are executed serially on a single thread — there are never two Things ticking concurrently. Parallel work giver assignment and parallel thought updates do execute within the logical tick cycle, but they block the main thread while running, so no other Thing is being ticked simultaneously.
- **No race conditions in ClearCache**: All `ClearCache` calls are invoked exclusively from the main thread. There are never concurrent cache clears, so caches do not require locks for write operations during invalidation.
- **Parallel code is 1.6 legacy**: `Helpers/ThreadLocal`, `Helpers/ThreadSafe`, `Patches/ThreadSafe`, the `Parallel*` managers and every `OptParallel*` toggle come from the 1.6 line and are archived with it. On this branch all of those toggles are listed in `YaOptSettings.LockedOffOptimizations`, so `YaOptGlobal.NeedThreadSafe` never becomes true and every patch gated on it is skipped — the code compiles but never runs. Do not extend it, and do not read its presence as evidence that multithreading is supported here. (One known trap lives there: `ThreadLocalReachability.QueueNewOpenRegion` is bound with `AccessTools.MethodDelegate`, which emits `this = null`; it is only safe while the matching prefix replaces the method body.)

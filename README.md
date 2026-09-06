# GasNet

[![CI](https://img.shields.io/github/actions/workflow/status/najoast/GasNet/ci.yml?branch=main&label=CI)](./.github/workflows/ci.yml)
[![License: MIT](https://img.shields.io/github/license/najoast/GasNet)](./LICENSE)
[![.NET](https://img.shields.io/badge/.NET-netstandard2.1%20%7C%20net10.0-512BD4)](#unity--godot-integration)

[English](./README.md) · [简体中文](./README.zh-CN.md)

> **A from-scratch C# reimplementation of Unreal Engine's Gameplay Ability System (GAS) on .NET 10.**
> One-to-one with the documentation · Zero engine dependencies in the core library · Runs on Unity, Godot 4, or a plain console.

---

## Why GasNet

If you want GAS's full semantics — tags, attribute aggregation, GameplayEffect lifecycle, ability tasks — without being locked into Unreal's UObject / networking / blueprint asset pipeline, GasNet gives you:

- **One-to-one with the documentation** ([GASDocumentation](https://github.com/tranek/GASDocumentation) §4.2 – §4.9), gameplay-logic semantics, not a stripped-down subset.
- **Zero external dependencies** in the core library. Multi-targets `netstandard2.1` (Unity 6, Godot 4 C#) and `net10.0` (desktop, server, tests).
- **117 xUnit tests** pin documentation semantics to assertions; **a 10-act console transcript** pins observable behavior to output.
- **Data-driven layer isolated as its own package** (`GasNet.Data`): GE catalogs live as JSON, with read/write symmetry. The core stays dependency-free.
- **Runnable Unity and Godot demo projects** (same JSON catalog, same core DLL) prove engine integration is just a handful of seams.

---

## Repository layout

```
GasNet.sln
├─ src/GasNet           Core library (no external deps, netstandard2.1 + net10.0)
├─ src/GasNet.Data      Data-driven layer: JSON catalog ↔ GameplayEffectDefinition (own assembly, keeps the core dep-free)
├─ src/GasNet.Sample    Sample content: attribute sets, GE definitions, abilities, cues (mirrors §2 of the docs)
├─ src/GasNet.Demo      Runnable combat scripted demo (console transcript)
├─ tools/GasNet.Editor  Local web editor (Blazor Server) for visually editing GE catalog JSON
└─ tests/GasNet.Tests   117 xUnit tests pinning documentation semantics to assertions
```

```bash
dotnet test GasNet.sln                          # 128/128 passing (incl. 12 in the Editor)
dotnet run --project src/GasNet.Demo            # 10-act combat transcript
dotnet run --project tools/GasNet.Editor        # launches the editor → http://localhost:5177
```

---

## Table of contents

- [UE → GasNet concept mapping](#ue--gasnet-concept-mapping)
- [Key semantics (one-to-one with the docs)](#key-semantics-one-to-one-with-the-docs)
- [Intentional deviations from UE](#intentional-deviations-from-ue)
- [Quick start](#quick-start)
- [Combined-scenarios demo (examples/UseCases)](#combined-scenarios-demo-examplesusecases)
- [Data-driven layer (src/GasNet.Data)](#data-driven-layer-srcgasnetdata)
- [Editor (tools/GasNet.Editor)](#editor-toolsgasneteditor)
- [Unity / Godot integration](#unity--godot-integration)
  - [Unity](#unity)
  - [Godot 4 (C#)](#godot-4-c)

---

## UE → GasNet concept mapping

| Unreal (GASDocumentation §) | GasNet (C#) | Notes |
|---|---|---|
| `FGameplayTag` / `UGameplayTagsManager` (§4.2) | `GameplayTag` / `GameplayTagsManager` | Hierarchical names register as handles; `MatchesTag` = self or ancestor match |
| `FGameplayTagContainer` | `GameplayTagContainer` | `HasTag` hierarchical match, `HasTagExact`, `HasAny/All/None` |
| `FGameplayTagCountContainer` | `GameplayTagCountContainer` | Count map + `RegisterGameplayTagEvent(NewOrRemoved/AnyCountChange)` (§4.2.1) |
| `FGameplayTagQuery` | `GameplayTagQuery` | All/Any/No expression tree; empty queries don't match (UE 5.3+) |
| `IAbilitySystemInterface` | `IAbilitySystemInterface` | `GetAbilitySystemComponent()` |
| `UAttributeSet` / `FGameplayAttributeData` (§4.3/4.4) | `AttributeSet` / `GameplayAttributeData` | Public fields + a reflection registry replace `ATTRIBUTE_ACCESSORS`/UPROPERTY |
| `FGameplayAttribute` | `GameplayAttribute` | `(SetType, FieldName)` tuple with a cached FieldInfo |
| `FAggregator` / `FAggregatorModChannel` (§4.5.4) | `AttributeAggregator` | See the aggregation formula below |
| `FAggregatorEvaluateMetaData` (§4.4.7) | `AggregatorEvaluateMetaData` | `Default`, `MostNegativeMod_AllPositiveMods`, `OnlyStrongestSlow_AllOtherMods` |
| `UGameplayEffect` (Blueprint subclass) | `GameplayEffectDefinition` | Data-style "archetype" object with a fluent builder; `Clone()` plays the archetype-copy role |
| `FGameplayEffectSpec` (§4.5.9) | `GameplayEffectSpec` | Level/Duration/Period overrides, DynamicGranted/AssetTags, SetByCaller |
| `MakeOutgoingSpec` / `ApplyGameplayEffectTo*` (§4.5.2) | `ASC.MakeOutgoingSpec` / `ApplyGameplayEffectToSelf/ToTarget` | All funnel into `ApplyGameplayEffectSpecToSelf` |
| `FActiveGameplayEffect` / `FActiveGameplayEffectsContainer` | `ActiveGameplayEffect` / `ActiveGameplayEffectsContainer` | With `OnRemoved/OnStackChanged/OnTimeChanged/OnPeriod` event sets |
| `FGameplayEffectModCallbackData` (§4.4.6) | `GameplayEffectModCallbackData` | Data received in `PostGameplayEffectExecute` |
| MMC (§4.5.11) | `ModifierMagnitudeCalculation` + `CustomCalculationMagnitude` | `RelevantAttributesToCapture`; captures bypass `PreAttributeChange` |
| ExecCalc (§4.5.12) | `GameplayEffectExecutionCalculation` | Capture definitions, `OutExecutionOutput.AddOutputModifier`, `MarkGameplayCuesHandledManually` |
| Four ModifierMagnitude kinds (§4.5.4) | `ScalableFloatMagnitude` / `AttributeBasedMagnitude` / `SetByCallerMagnitude` / `CustomCalculationMagnitude` | Coefficient formula `((Mag*Coef)+Pre)*Post`; Snapshot semantics match the docs' table |
| SetByCaller (§4.5.9.1) | `spec.Set/GetSetByCallerMagnitude(tag)` | Missing modifier pair → log error and return 0 (Divide is dangerous, per the docs) |
| Stacking (§4.5.5) | `StackingType/StackLimitCount/Stack*Policy` | AggregateBySource/Target, DurationRefresh/PeriodReset/Expiry — three policies |
| Granted abilities (§4.5.6) | `GrantedAbilityEntry` + `RemovalPolicy` | CancelImmediately / RemoveAbilityOnEnd / DoNothing |
| GE tag five-way (§4.5.7) | `AssetTags/GrantedTags/OngoingTagRequirements/ApplicationTagRequirements/TargetTagRequirements/RemoveGameplayEffectsWithTags` | The docs merge App/Target; here they are split into four containers per the engine, plus the immunity path |
| Immunity (§4.5.8) | `GrantedApplicationImmunityTags` + `OnImmunityBlockGameplayEffect` | Also supports immunity queries against the spec's asset tags |
| Custom application requirements (§4.5.13) | `GameplayEffectCustomApplicationRequirement` | On failure, stamp the `GameplayEffect.Fail.CustomRequirement` tag |
| Cost GE (§4.5.14) | `CostGameplayEffect` + `CheckCost/ApplyCost/CommitCost` | The MMC-reads-ability-instance pattern is available (`Context.AbilityInstance`) |
| Cooldown GE (§4.5.15) | `CooldownGameplayEffect` + `CooldownTags/CooldownDuration` | Shared-cooldown GE pattern: DynamicGrantedTags + SetByCaller `Data.Cooldown`; `GetCooldownTags()` union; `GetActiveEffectsTimeRemainingAndDuration` |
| `UGameplayAbility` (§4.6) | `GameplayAbility` | `CanActivateAbility/TryActivate/CommitAbility/EndAbility/CancelAbility`, failure tags, `OnGiveAbility/OnAvatarSet/OnRemoveAbility` |
| Instancing Policy (§4.6.7) | `GameplayAbilityInstancingPolicy` | NonInstanced reuses the shared prototype; PerActor lazy-clones and keeps state; PerExecution clones fresh per activation |
| Ability Tags (§4.6.9) | `AbilityTags/CancelAbilitiesWithTag/BlockAbilitiesWithTag/ActivationOwnedTags/Activation{Required,Blocked}Tags/Source*/Target*` | Source/Target containers are only evaluated on event triggers |
| Triggers / events (§4.6.4, §4.6.11) | `Triggers` + `ASC.HandleGameplayEvent` / `GameplayAbilitySystemLibrary.SendGameplayEventToActor` | Supports GameplayEvent / TagAdded / TagRemoved |
| Input (§4.6.2) | `AbilityLocalInputPressed/Released(inputID)` + `NotifyInputReleased` | "Hold-to-activate" abilities end on Release |
| GameplayCue (§4.8) | `GameplayCueNotify_Static/_Actor` + `GameplayCueManager` | Instant/period → `Executed`; Duration/Infinite → `Added(OnActive+WhileActive)`/`Removed`; instances managed per (target, tag) with parent-tag lookup fallback |
| `UAbilitySystemGlobals` (§4.9) | `AbilitySystemGlobals` | `InitGlobalData()` + global CueManager |
| Ability Task (§4.7) | `AbilityTask` + `WaitDelay` / `WaitInputPress` / `WaitInputRelease` / `WaitGameplayEvent` | Tasks are driven by `ASC.Tick` and reclaimed by `EndAbility`; waiting on target data is expressed via a host event + `WaitGameplayEvent`; `OnAbilityTick` remains as a per-ability Tick fallback |

---

## Key semantics (one-to-one with the docs)

**Attribute aggregation math (§4.5.4)**: `CurrentValue = ((Base + ΣAdd) * MultSum) / DivSum`,
where Multiply/Divide use a **1-biased sum**: `Σ mags − N + 1`. The docs' worked examples are pinned by tests:
two 1.5s multiply → ×2.0 (not ×2.25); two 0.5s → ×0; 1.1 and 0.5 → ×0.6; two 5s → ×9.
Override applies last; last-writer-wins. Instant GEs and period ticks apply their operators **one at a time** to `BaseValue` (the engine's ExecuteMod semantics);
Duration/Infinite GE modifiers flow into the aggregator and act on `CurrentValue`.

**Lifecycle**: Instant → permanently mutates `BaseValue`, emits no tags, only `Executed` cues;
Duration/Infinite → mutates `CurrentValue`, grants tags, emits `Added`/`Removed` cues;
period (`Period > 0`) → each tick executes as Instant (mutates `BaseValue` + `Executed` cue).
The tick that would fall on the expiry instant is swallowed by the expiry check (matches the engine's order: `CheckDuration` before period execution).

**Meta attributes** (§4.3.3): GEs only write `MetaDamage` / `MetaHeal`; `AttributeSet.PostGameplayEffectExecute` is responsible for distribution (armor-stack absorption, writing to Health, clearing meta attributes).

**Capture and derived attributes** (§4.5.11 / §4.3.5): captures are evaluated live through the ASC aggregator and **bypass `PreAttributeChange`** (you must clamp yourself);
non-snapshotting target-side captures auto-recompute when the dependencies change (with a depth cap to prevent oscillation); source-side captures only auto-recompute when the source is self — for other cases, use `SetActiveGameplayEffectLevel` to force a recompute.

**Cue triggering** (§4.8.2 / §4.8.8): cues fire only when a GE is successfully applied (not blocked by tags or immunity);
ExecCalc can call `MarkGameplayCuesHandledManually()` to suppress them.

---

## Intentional deviations from UE

- **No networking / prediction layer**: a local-authoritative, single-threaded model. `NetExecutionPolicy` is kept as a documentation-only enum; no replication, no `PredictionKey`, no `FastArray`, no RPC batching. For a "server-authoritative + client-presentational" networking architecture, see [docs/networking.md](docs/networking.md).
- **Host-driven time**: the ASC has no internal heartbeat; you must call `ASC.Tick(dt)` each frame. Tests and replays inject `ManualTimeSource`.
- **Data-driven as a "JSON catalog + host-side registration" model** (see the "Data-driven layer" section): no `.ini` / `.udt` asset pipeline; tags are still registered at runtime (tag names in JSON are auto-registered on first appearance — no pre-existing tag table is validated); no curves (`ScalableFloatMagnitude.ValuePerLevel` substitutes); no blueprints (logic classes ExecCalc / MMC / CAR / Ability are referenced from data as "type name + host registration").
- **AbilityTask is a lightweight subset** (§4.7): built-in `WaitDelay` / `WaitInputPress` / `WaitInputRelease` / `WaitGameplayEvent` — tasks attach to an active spec, are driven by `ASC.Tick` (deterministic under `ManualTimeSource`), and are reclaimed by `EndAbility` (cancellation triggers `OnCancelled`). Sub-actions for timing, input-waiting, and event-waiting no longer need hand-written `OnAbilityTick` timers. `NonInstanced` abilities can't carry tasks (same as the engine). Montage-style tasks and `WaitTargetData` are out of scope: waiting on target data is expressed via a host event + `WaitGameplayEvent`.
- **Stack modifiers don't scale with stack count**: matches the engine — stacking only increases `StackCount` and drives refresh/expiry policy; "per-stack +X" should be expressed via SetByCaller or stack-change events (the sample's armor stack is "presence = +10, hit drops a stack").
- **Multiply / Divide use the engine's default sum** (not the patched per-item-multiply in docs §4.5.4.1), with an extra `OnlyStrongestSlow_AllOtherMods` qualifier implementing §5.7's "only the strongest slow counts".
- **AbilitySpec has no network scope lock**: a single-threaded snapshot iteration replaces `ABILITYLIST_SCOPE_LOCK`.

---

## Quick start

```csharp
var clock = new ManualTimeSource();
var asc = new AbilitySystemComponent { TimeSource = clock };
var actor = new MyActor(asc);              // implements IAbilitySystemInterface
asc.InitAbilityActorInfo(actor, actor);
asc.AddSet<MyAttributeSet>();
asc.ApplyGameplayEffectToSelf(MyGE.InitAttributes);          // Instant GE initialization (§4.4.4)

asc.GiveAbility(new GameplayAbilitySpec(new MyAbility(), level: 1, inputID: 1));
asc.AbilityLocalInputPressed(1);

// Each frame:
clock.Advance(dt); asc.Tick(dt);

// Subscribe:
asc.GetGameplayAttributeValueChangeDelegate(MySet.HealthAttr).Handler += d => ...;
asc.RegisterGameplayTagEvent(MyTags.Stunned, GameplayTagEventType.NewOrRemoved, (tag, count) => ...);
```

The sample project (`GasNet.Sample`) mirrors the abilities list from §2 of the docs: Fireball (MMC cost + SetByCaller shared cooldown + ExecCalc damage), Sprint (Infinite GE + per-second stamina drain, ends on input release), Jump (NonInstanced),
Passive Armor Stacks (1 stack / 4s, cap 4, drops a stack on hit), CounterAttack (triggered by `Event.Hit`),
plus stun, slow, stacking armor, periodic heal GEs and both static- and actor-type cues.

---

## Combined-scenarios demo (examples/UseCases)

[`examples/UseCases`](./examples/UseCases) is a standalone console project that chains multiple APIs into
**readable end-to-end scenarios** — ability-task chains, tag-triggered cancellation, host events as targeting, data-driven spell configuration, and the networking bridge's DTO flow. It covers the dimension `GasNet.Tests`' unit tests can't reach: multiple code paths run together inside a single fixed-clock world (`SimWorld` + `ManualTimeSource`) and produce deterministic output, where the output itself is the documentation.

```bash
cd examples/UseCases
dotnet run
```

5 scenarios (filter by name: `dotnet run -- Channel`); the exit code is the number of failing scenarios. It also runs as a smoke test in CI. See [examples/UseCases/README.md](./examples/UseCases/README.md).

---

## Data-driven layer (src/GasNet.Data)

The C# counterpart of UE's "GE blueprint asset": a `GameplayEffectDefinition` lives on disk as a JSON catalog, loaded at runtime via
`GasNetDataLoader.LoadCatalog(File.ReadAllText(path), options)` into ordinary definition objects.
The core library stays dependency-free — the entire JSON pipeline lives in this standalone assembly (`netstandard2.1` + `net10.0`;
the ns2.1 target references the `System.Text.Json` package; net10.0 uses the built-in implementation). Read/write are symmetric:
`GasNetDataWriter.WriteCatalog(...)` writes (loaded or edited) definitions back into the same format —
fields equal to their defaults are omitted (clean diffs); write order matches input order.

```jsonc
{
  "effects": {
    "GE_Damage": {
      "durationPolicy": "Instant",                    // Instant | HasDuration | Infinite
      "duration": 5, "period": 1,                     // HasDuration/Infinite + period → periodic GE
      "modifiers": [ {
        "attribute": "BattleAttributeSet.Health",     // "SetTypeName.AttributeName"
        "op": "Add",                                  // Add | Multiply | Divide | Override
        "magnitude": { "type": "setByCaller", "tag": "Data.Damage" }
        // Also: scalableFloat(value,valuePerLevel), attributeBased(attribute,source,snapshot,
        //   useBaseValue,sourceTagFilter,targetTagFilter), customCalculation(calculation);
        //   common UE formula fields: coefficient(1), preMultiplyAdditive(0), postMultiplyAdditive(1)
      } ],
      "assetTags": [], "grantedTags": [], "cueTags": ["GameplayCue.Combat.Hit"],
      "applicationTagRequirements": { "require": [], "ignore": [] },
      "targetTagRequirements": {}, "ongoingTagRequirements": {},
      "removeGameplayEffectsWithTags": [], "grantedApplicationImmunityTags": [],
      "stacking": { "type": "AggregateByTarget", "limit": 4, "durationRefresh": "NeverRefresh",
                    "periodReset": "NeverReset", "expiry": "ClearEntireStack" },
      "grantedAbilities": [ { "ability": "BurnAbility", "inputId": 0,
                              "removalPolicy": "CancelAbilityImmediately" } ],
      "executions": [ { "calculation": "DamageExecution" } ],
      "customApplicationRequirements": [ { "requirement": "CanAffordMana" } ]
    }
  }
}
```

Parsing conventions:

- **Attributes** are referenced as `"SetTypeName.AttributeName"`; the host registers attribute set types with `options.RegisterAttributeSet<T>()`, and the loader validates field existence via `GameplayAttributeRegistry`.
- **Tags** are written as plain strings and auto-registered following the core's runtime-registration model (see "Intentional deviations").
- **Code fragments** (ExecCalc / MMC / CAR / granted abilities) are referenced by type name; the host registers them with `options.RegisterType<T>()` — no assembly scanning, which is naturally IL2CPP/trimming friendly. ExecCalc / MMC / CAR require a public parameterless constructor.
- **Unknown fields are always an error** (`GasNetDataException`, with the effect name and field path in the message) — typos in the data format must fail loudly. Duplicate effect names fail the same way (a JSON object silently letting later keys overwrite earlier ones is treated as a duplicate).
- Not yet expressible from JSON: `GameplayTagQuery` (`GrantedApplicationImmunityQuery` / `TagRequirements.TagQuery`).

Runnable examples: [examples/GodotDemo](./examples/GodotDemo) (`Data/BattleGE.json`) and
[examples/UnityDemo](./examples/UnityDemo) (`Assets/StreamingAssets/Data/BattleGE.json`) —
tweak Health/Attack in the JSON to re-tune, no recompile needed.

---

## Editor (tools/GasNet.Editor)

A local web tool (Blazor Server, pure .NET with no JS toolchain) for editing the JSON catalog above:

```bash
dotnet run --project tools/GasNet.Editor    # → http://localhost:5177
```

- **Profiles**: reflectively load the game-side managed DLL (the GodotDemo build artifact works) and auto-discover attribute sets and ExecCalc / MMC / CAR / ability types to populate the dropdowns. Running from inside the repo auto-fills the GodotDemo path. Assemblies are loaded as in-memory copies so the game DLL isn't locked (the game can be recompiled at any time; the UI prompts when a profile goes stale). GasNet-family dependencies are forced back to the editor's own copies to keep type identity consistent; classes referencing engine types (Godot Node, etc.) that fail to load are skipped.
- **Editing**: every data field of an effect (duration / period, modifiers and the four magnitude types, tags, the three tag-requirement classes, immunity, stacking, granted abilities, ExecCalc / CAR); clone an effect, document-level undo / redo, filter by name, reverse-lookup tags / attributes (which effect uses them), tag-input autocomplete with a soft warning for new tags, and a read-only JSON preview of a single effect.
- **Validation**: every change does a "write → real loader reads it back" round-trip; whatever the loader reports, the editor shows. A file the game can't load won't be silently saved (the editor asks for confirmation before saving). The editor itself never serializes JSON — both reads and writes go through `GasNet.Data`, so a format change only affects one place.

Current boundaries: no editing of `GameplayTagQuery`; attribute sets and logic classes can't be created here (those always live in code); no live connection to a running game. See [tools/GasNet.Editor/README.md](./tools/GasNet.Editor/README.md).

---

## Unity / Godot integration

The core library multi-targets: **`netstandard2.1`** (Unity 2019.4+ / Unity 6 Mono and IL2CPP, Godot 4 C# can all load it directly) and **`net10.0`** (desktop / server; the in-repo Sample / Demo / Tests also target this). The core library contains zero engine APIs; engine-side glue (input bridging, cue presentation, asset import) is up to the integrator.

### Unity

1. `dotnet build src/GasNet -c Release`, then drop `bin/Release/netstandard2.1/GasNet.dll` into `Assets/Plugins/`
   (or use NuGetForUnity to consume it as a package).
2. IL2CPP trim protection: the attribute system discovers `GameplayAttributeData` fields via reflection; with a high Managed Stripping Level, those fields may be stripped. Either of the two approaches below works:
   - Annotate your `AttributeSet` class with `[UnityEngine.Scripting.Preserve]`;
   - Or preserve this assembly and your attribute set in `Assets/link.xml`:

     ```xml
     <linker>
       <assembly fullname="GasNet" preserve="all"/>
       <assembly fullname="Assembly-CSharp">
         <namespace fullname="YourGame" preserve="all"/>
       </assembly>
     </linker>
     ```

   If a field does get stripped, `GameplayAttributeRegistry` emits a clear warning log (once).
3. If you use the data-driven layer, also drop `GasNet.Data.dll` (with its `System.Text.Json` dependency) into `Assets/Plugins/`, and preserve `GasNet.Data` and the relevant `System.Text.Json` assemblies in `link.xml`.
4. Per-frame driving: in `MonoBehaviour.Update()`, call `asc.Tick(Time.deltaTime)`; inject the engine clock as the time source:

   ```csharp
   asc.TimeSource = new EngineTimeSource(); // implements ITimeSource, returns Time.time
   GasNetLog.OnWarn = msg => Debug.LogWarning(msg);
   ```

**Runnable example**: [examples/UnityDemo](./examples/UnityDemo) — a complete Unity project (Unity 6,
`Assets/Plugins/` pre-supplied with the netstandard2.1 build artifact) demonstrating the same four seams as GodotDemo (time injection, Tick driving, MonoBehaviour holding the ASC, cue presentation adapter): spacebar to attack + auto enemy counter-attack; GE definitions are loaded by `src/GasNet.Data` from `Assets/StreamingAssets/Data/BattleGE.json` (the data-driven example).

### Godot 4 (C#)

1. In the game project's `.csproj`, add a direct `<ProjectReference Include="...\src\GasNet\GasNet.csproj"/>`; the netstandard2.1 asset is natively available on Godot's .NET runtime (6 / 8). Or just drop the DLL in, the same way.
2. In `_Process(double delta)`, call `asc.Tick((float)delta)`; the time source can be implemented as `ITimeSource` using `Time.GetTicksMsec() / 1000f`.
3. Cue presentation: derive from `GameplayCueNotify_Static` / `_Actor` and operate on `Node` / particles / audio in the event callbacks.

**Runnable example**: [examples/GodotDemo](./examples/GodotDemo) — a complete Godot project
demonstrating all four seams (time injection, Tick driving, node holding the ASC, cue presentation adapter): spacebar to attack + auto enemy counter-attack; GE definitions are loaded by `src/GasNet.Data` from `Data/BattleGE.json` (the data-driven example). Unity version:
[examples/UnityDemo](./examples/UnityDemo).
`GasNet.Sample`, `GasNet.Demo`, and `GasNet.Tests` stay on `net10.0` (host-side content / tools, not shipped into the engine alongside the core library).

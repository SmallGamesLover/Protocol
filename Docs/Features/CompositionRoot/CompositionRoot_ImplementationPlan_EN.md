# Composition Root — Implementation Plan
> Project: Protocol | Refactoring existing movement system

---

## Context and Goal

The current codebase manages initialization order between `CharacterMover2D`, `PlayerInputReader`, and `MovementDebugOverlay` by splitting logic across `Awake()` and `Start()`. This implicit ordering works for three components but will not scale as new systems (shooting, weapons, health) are added.

**Goal:** introduce a Composition Root pattern that centralizes dependency wiring in a single, explicit initialization sequence. Each component receives its dependencies via a public `Initialize(...)` method instead of resolving them internally.

**Constraint:** every intermediate step must compile and run. The refactoring replaces how components are initialized, not what they do — no behavioral changes.

---

## Phase 0 — PlayerCompositionRoot Stub

**Goal:** create the Composition Root script and attach it to the player GameObject. No components are migrated yet — this is scaffolding.

### 0.1 PlayerCompositionRoot

Create `PlayerCompositionRoot` MonoBehaviour in `Runtime/Core/` (new subfolder). This script will own all `[SerializeField]` config references for the player entity and call `Initialize` on every player component in dependency order.

At this stage: empty `Awake()` body. The script exists solely so it can be attached to the player GameObject and receive serialized field assignments in Inspector before any migration begins.

```
Assets/_Project/Scripts/SimpleGamesLover/Protocol/
    Runtime/
        Core/                ← new folder
            PlayerCompositionRoot.cs
        Movement/
            ...
```

Namespace: `SGL.Protocol.Runtime.Core`.

### 0.2 Verification
- Project compiles
- `PlayerCompositionRoot` is attached to the player GameObject alongside existing components
- Existing movement behavior is unchanged — `CharacterMover2D` still initializes itself via `Awake()`

---

## Phase 1 — Migrate CharacterMover2D

**Goal:** `CharacterMover2D` no longer initializes itself. All setup logic moves from `Awake()` to `Initialize(WalkingConfig, DodgeConfig)`. `PlayerCompositionRoot` takes over the initialization call.

### 1.1 Initialize Method

Add a public `Initialize(WalkingConfig walkingConfig, DodgeConfig dodgeConfig)` method to `CharacterMover2D`. This method will contain all logic currently in `Awake()`:

- `GetComponent<Rigidbody2D>()`, `GetComponent<BoxCollider2D>()`
- `LayerMask.NameToLayer("Platform")` cache
- `ContactFilter2D` setup
- `CollisionSlideResolver2D` instantiation
- State and FSM creation (WalkingState, DodgeState, sub-states)
- Transition registration
- `SetInitialState()` call

`GetComponent` calls for components on the same GameObject (`Rigidbody2D`, `BoxCollider2D`) stay inside `Initialize` — they are internal implementation details, not external dependencies.

### 1.2 Initialization Guard

Add a `private bool _initialized` field. Set to `true` at the end of `Initialize()`. Add an early return guard at the top of `FixedUpdate()`:

```csharp
private void FixedUpdate()
{
    if (!_initialized) return;
    // ... existing logic
}
```

This prevents `NullReferenceException` in the window between component creation and `Initialize()` call. The guard also serves as a safety net if a future developer adds the component to a GameObject without wiring it through a Composition Root — the component silently does nothing instead of crashing.

### 1.3 Config Migration

Remove `[SerializeField] private WalkingConfig WalkingConfig` and `[SerializeField] private DodgeConfig DodgeConfig` from `CharacterMover2D`. These become private fields assigned in `Initialize()`:

```csharp
private WalkingConfig _walkingConfig;
private DodgeConfig _dodgeConfig;
```

The corresponding `[SerializeField]` declarations move to `PlayerCompositionRoot`:

```csharp
[Header("Movement")]
[SerializeField] private WalkingConfig WalkingConfig;
[SerializeField] private DodgeConfig DodgeConfig;
```

> **Inspector values:** after removing `[SerializeField]` from `CharacterMover2D`, the assigned asset references are lost. They must be re-assigned on `PlayerCompositionRoot` in Inspector. This is a one-time manual step.

### 1.4 Composition Root Wiring

`PlayerCompositionRoot.Awake()` resolves and initializes `CharacterMover2D`:

```csharp
private void Awake()
{
    var mover = GetComponent<CharacterMover2D>();
    mover.Initialize(WalkingConfig, DodgeConfig);
}
```

### 1.5 Remove Old Awake

`CharacterMover2D.Awake()` is removed entirely (or left as an empty method if Unity workflow requires it). All initialization logic now lives in `Initialize()`.

### 1.6 Fields That Stay as SerializeField on CharacterMover2D

These are instance-specific configuration, not dependencies:

- `GroundCheckSize`, `GroundCheckOffset`, `GroundLayerMask`
- `CeilingCheckSize`, `CeilingCheckOffset`, `CeilingLayerMask`

They are tuned per-GameObject in Inspector and do not vary by context.

### 1.7 Verification
- Character moves, jumps, dodges, drops through platforms identically to before
- `WalkingConfig` and `DodgeConfig` assets are assigned on `PlayerCompositionRoot` in Inspector
- `CharacterMover2D` has no `[SerializeField]` config fields — configs come from `Initialize()`
- Tweaking config values in Play Mode still works (ScriptableObject instances are shared references)

---

## Phase 2 — Migrate PlayerInputReader

**Goal:** `PlayerInputReader` receives its `CharacterMover2D` dependency via `Initialize()` instead of resolving it internally.

### 2.1 Initialize Method

Add `public void Initialize(CharacterMover2D mover)`. Store the reference in a private field:

```csharp
private CharacterMover2D _mover;

public void Initialize(CharacterMover2D mover)
{
    _mover = mover;
    _initialized = true;
}
```

### 2.2 Initialization Guard

Add `_initialized` field and early return in `Update()`:

```csharp
private void Update()
{
    if (!_initialized) return;
    // ... existing input reading
}
```

### 2.3 Remove Self-Resolution

Remove whatever mechanism currently provides the `CharacterMover2D` reference — whether it is a `[SerializeField]` field, a `GetComponent` call in `Awake()`, or a `Start()` lookup. The reference now comes exclusively from `Initialize()`.

### 2.4 Composition Root Wiring

Expand `PlayerCompositionRoot.Awake()`:

```csharp
private void Awake()
{
    var mover = GetComponent<CharacterMover2D>();
    var inputReader = GetComponent<PlayerInputReader>();

    mover.Initialize(WalkingConfig, DodgeConfig);
    inputReader.Initialize(mover);
}
```

Order matters: `inputReader.Initialize(mover)` comes after `mover.Initialize(...)` because `PlayerInputReader` depends on a fully initialized mover.

### 2.5 Verification
- Input works: A/D movement, Space jump, Shift dodge, S drop-through
- `PlayerInputReader` has no `[SerializeField]` or `GetComponent` reference to `CharacterMover2D`
- The only way `PlayerInputReader` gets its mover reference is through `Initialize()`

---

## Phase 3 — Migrate MovementDebugOverlay

**Goal:** `MovementDebugOverlay` receives its `CharacterMover2D` dependency via `Initialize()`.

### 3.1 Initialize Method

```csharp
private CharacterMover2D _mover;

public void Initialize(CharacterMover2D mover)
{
    _mover = mover;
    _initialized = true;
}
```

Method body is wrapped in `#if UNITY_EDITOR` — consistent with the component's existing conditional compilation strategy. In builds, `Initialize` is an empty method (the class is an empty shell).

### 3.2 Initialization Guard

The component already has editor-only method bodies. Add the `_initialized` guard inside the `#if UNITY_EDITOR` block of each relevant method (`Update`, `OnGUI`, `OnDrawGizmos`).

`OnDrawGizmos` already checks `!enabled` — add `!_initialized` to the same guard.

### 3.3 Remove Self-Resolution

Remove the `[SerializeField]` reference to `CharacterMover2D` or the `GetComponent` call in `Awake()`. Remove the `Awake()` method if it becomes empty.

### 3.4 Composition Root Wiring

Final form of `PlayerCompositionRoot.Awake()`:

```csharp
private void Awake()
{
    var mover = GetComponent<CharacterMover2D>();
    var inputReader = GetComponent<PlayerInputReader>();
    var debugOverlay = GetComponent<MovementDebugOverlay>();

    mover.Initialize(WalkingConfig, DodgeConfig);
    inputReader.Initialize(mover);
    debugOverlay.Initialize(mover);
}
```

### 3.5 Fields That Stay as SerializeField on MovementDebugOverlay

- `ShowOverlay`, `ShowVelocityGizmo`, `VelocityGizmoScale` — visual tuning, stays in Inspector

`WalkingConfig` does **not** stay as `[SerializeField]` on the component. It is a ScriptableObject config dependency — same category as `WalkingConfig`/`DodgeConfig` on `CharacterMover2D` in Phase 1. Move it to `Initialize(CharacterMover2D mover, WalkingConfig walkingConfig)` and assign from the parameter inside `#if UNITY_EDITOR`. `PlayerCompositionRoot` already holds the `WalkingConfig` reference from Phase 1 and passes it through.

### 3.6 Verification
- Debug overlay displays FSM state, velocity, timers, flags — identical to before
- F1 toggles both overlay and gizmo
- `MovementDebugOverlay` has no `[SerializeField]` or `GetComponent` reference to `CharacterMover2D`
- Velocity gizmo in Scene View works correctly

---

## Phase 4 — Documentation

**Goal:** update project documentation to reflect the Composition Root pattern.

### 4.1 Architecture.md

Add a new section "Composition Root" between "Active Patterns" and "Open Decisions / Planned Work". Describes the pattern, the two-level scope (Scene + Entity), the SerializeField vs Initialize rule, and the initialization guard.

Add "Composition Root" entry to the "Active Patterns" section.

### 4.2 CLAUDE.md

Add a "Composition Root Rules" block with imperative instructions for code generation: how to add new components, where configs go, what stays in SerializeField.

### 4.3 Architecture.md — Class Map Update

Update the Class Map entries for `CharacterMover2D`, `PlayerInputReader`, and `MovementDebugOverlay` to reflect that they receive dependencies via `Initialize()`. Add `PlayerCompositionRoot` entry.

### 4.4 Architecture.md — Folder Structure Update

Add `Runtime/Core/` to the folder tree with `PlayerCompositionRoot.cs`.

---

## Phase 5 — Full Regression Verification

**Goal:** confirm that the entire movement system behaves identically to before the refactoring.

### 5.1 Manual Test Checklist
- Walk, run, decelerate to stop
- Jump: full height, low jump (short press), coyote time off edge, jump buffer on landing
- Fall: asymmetric fall speed, MaxFallSpeed cap, air control
- Dodge: from ground (Idle, Walk, Run), from air (Jump, Fall), into wall, off platform edge
- One-way platforms: jump through from below, land from above, drop-through on S, cascade through stacked platforms
- Ceiling: stops ascent, no trigger on one-way platforms
- Debug overlay: all data displays correctly, F1 toggle, velocity gizmo
- Play Mode config tweaking: changing ScriptableObject values applies immediately

### 5.2 Code Audit
- `CharacterMover2D` has no `Awake()` or `Start()` with initialization logic
- `PlayerInputReader` has no `Awake()` or `Start()` with dependency resolution
- `MovementDebugOverlay` has no `Awake()` or `Start()` with dependency resolution
- All three components have `Initialize(...)` methods
- `PlayerCompositionRoot.Awake()` is the only place where initialization order is defined
- No `[SerializeField]` config references on components that receive them via `Initialize()`

---

## Time Estimate

| Phase | Contents | Estimate (hours) |
|---|---|---|
| 0 | PlayerCompositionRoot stub | 0.5 |
| 1 | Migrate CharacterMover2D | 1–2 |
| 2 | Migrate PlayerInputReader | 0.5–1 |
| 3 | Migrate MovementDebugOverlay | 0.5–1 |
| 4 | Documentation | 1–2 |
| 5 | Full regression verification | 1–2 |
| **Total** | | **4–8** |

> Low-risk refactoring — no behavioral changes, only initialization restructuring. The main risk is lost Inspector references when `[SerializeField]` fields move between components. Each phase ends with verification to catch this immediately.

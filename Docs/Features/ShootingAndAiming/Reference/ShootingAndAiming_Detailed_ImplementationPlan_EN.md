# Shooting & Aiming — Implementation Plan
> Project: Protocol | New feature on top of existing movement system

---

## Context and Goal

The movement system (`CharacterMover2D`, HFSM, Composition Root) is complete through Phase 7. The next major feature is the shooting system — the second pillar of the core loop. The player needs to aim with the mouse, fire hitscan weapons, manage ammo, and switch between weapon slots.

**Goal:** implement the shooting and aiming systems described in `GDD_ShootingAndAiming_EN.md`, covering the prototype scope: Starter Pistol (semi-auto, single hitscan, infinite reserve), Assault Rifle (automatic, single hitscan), and Shotgun (semi-auto, spread hitscan). The system must integrate with the existing movement system without modifying its behavior.

**Constraint:** every phase must leave the project in a compilable, runnable state. New systems are additive — movement continues to work unchanged throughout. Each phase ends with a verification checklist.

**Prototype scope exclusions:** Sniper Rifle (penetrating hitscan), SMG, Machine Gun, draw/holster time, partial reload, Gaussian spread distribution, enemy hit reactions, crosshair art. These are documented as deferred in the GDD and are not part of this plan.

---

## Architecture Overview

### New Folder Structure

```
Assets/_Project/Scripts/SimpleGamesLover/Protocol/
    Runtime/
        Core/
            PlayerCompositionRoot.cs         ← updated
        Combat/                              ← new folder
            AimController.cs
            ShootingController.cs
            Weapon.cs
            WeaponConfig.cs
            WeaponHolder.cs
            CombatInputReader.cs
            CrosshairUI.cs
            Enums/
                FireMode.cs
                HitscanType.cs
        Movement/
            MovementInputReader.cs           ← renamed from PlayerInputReader
            CharacterMovementState.cs        ← new enum
            ...existing...
```

Namespace: `SGL.Protocol.Runtime.Combat` (and `SGL.Protocol.Runtime.Combat.Enums`).

### Design Decisions

**Rename `PlayerInputReader` → `MovementInputReader`.** The existing class is currently named `PlayerInputReader`, but it handles only movement input. With the addition of `CombatInputReader`, the old name becomes misleading — "Player" implies completeness, while the class serves only the Movement domain. Renaming to `MovementInputReader` establishes a symmetric naming convention (`{Domain}InputReader`) that scales naturally as new domains appear. This rename is a preliminary step before creating `CombatInputReader`.

**Separate `CombatInputReader` vs extending `MovementInputReader`.** `MovementInputReader` lives in `Runtime/Movement/` and is described in `Architecture.md` as "The only file referencing `Keyboard`/`Mouse`." Two options:

- *Extend `MovementInputReader`* — keeps a single input source but makes a Movement-namespace class depend on Combat classes. Grows into a monolith as systems are added.
- *Create `CombatInputReader`* — follows SRP. Each system domain has its own input reader. Both files reference `Keyboard`/`Mouse`, but each is scoped to its domain. When `InputActionAsset` is introduced post-prototype, each reader maps to its own Action Map naturally.

**Decision:** separate `CombatInputReader` in `Runtime/Combat/`. Update `Architecture.md` to note that both `MovementInputReader` and `CombatInputReader` reference `Keyboard`/`Mouse`.

**MonoBehaviours vs plain C# classes.** Following the movement system's pattern (MonoBehaviour orchestrator + plain C# state objects):

| Class | Type | Reason |
|---|---|---|
| `AimController` | MonoBehaviour | Needs `Update()` for cursor tracking, owns fire origin `Transform` |
| `ShootingController` | MonoBehaviour | Needs `Update()` for timers (fire rate, reload, spread recovery) |
| `CombatInputReader` | MonoBehaviour | Needs `Update()` for input polling |
| `WeaponHolder` | Plain C# class | Data + logic, no Unity lifecycle needed. Owned by `ShootingController` |
| `Weapon` | Plain C# class | Per-weapon runtime state (ammo, spread, timers). Created by `WeaponHolder` |
| `WeaponConfig` | ScriptableObject | Serialized weapon parameters, shareable assets |

**Character flip ownership.** The GDD states: "Character facing is always controlled by the aim position." and "If [CharacterMover2D] does contain flip logic, that logic must be removed — the aim system is the single source of truth for character direction." `AimController` owns flip. If `CharacterMover2D` currently has flip logic, it must be removed in Phase 1.

**Dodge integration.** The GDD states shooting and reloading are blocked during dodge. `ShootingController` reads dodge state from `CharacterMover2D` (via a public `IsDodging` property or the existing `DebugStateName`). This is a read-only dependency — `ShootingController` never modifies movement state.

**Input agnosticism (intent-based API).** The combat system replicates the same input-agnostic pattern used by the movement system, but it is worth documenting the principle explicitly since it applies across every domain.

The core idea: controller APIs describe **intents** (`Fire`, `Reload`, `AimAt`), not **input sources** (`LeftMouseButton`, `RKey`, `Mouse.position`). The controller does not know or care who called the method — a human player, an AI agent, a replay system, or a tutorial script. Input readers are **adapters** that translate a specific input source into universal intent calls.

For the combat system this means:

- `AimController` exposes `SetAimScreenPosition(Vector2)` for player input (mouse gives screen coordinates) and `SetAimDirection(Vector2)` for AI or scripted callers that operate in world space directly. Both methods produce the same internal result — a normalized `AimDirection`. The controller never reads `Mouse.current` itself.
- `ShootingController` exposes `OnFirePressed()`, `OnFireHeld()`, `RequestReload()`, `RequestWeaponSwitch(int)`. An AI enemy calls `OnFirePressed()` the same way `CombatInputReader` does — no special-cased AI API.
- `CombatInputReader` is a thin adapter: it reads `Mouse.current` and `Keyboard.current`, translates them into intent calls on `AimController` and `ShootingController`. It contains zero game logic — it does not decide *whether* to fire, only *that the player pressed the button*.

This pattern scales: when enemy AI is implemented, an `EnemyShooterAI` component calls the same public API (`SetAimDirection`, `OnFirePressed`) without any changes to `AimController` or `ShootingController`. The shooting system works identically for player and AI — only the adapter layer differs.

### Dependency Graph (final state)

```
PlayerCompositionRoot.Awake()
    ├── mover.Initialize(WalkingConfig, DodgeConfig)
    ├── aimController.Initialize(mainCamera)
    ├── shootingController.Initialize(mover, aimController, starterPistolConfig)
    ├── inputReader.Initialize(mover)
    └── combatInputReader.Initialize(aimController, shootingController)
    // debugOverlay.Initialize(mover, walkingConfig) — unchanged
```

`ShootingController` depends on `CharacterMover2D` (for dodge state and movement state for spread penalties) and `AimController` (for aim direction and fire origin). `CombatInputReader` depends on `AimController` and `ShootingController` to call their public APIs.

---

## Preliminary Step — Rename PlayerInputReader → MovementInputReader

**Goal:** rename the existing input reader class before any new combat code is created, to establish the `{Domain}InputReader` naming convention.

**Scope:** rename the class, file, and all references in `PlayerCompositionRoot`, `Architecture.md`, and `CLAUDE.md`. No behavioral changes — only the name changes. Verify that movement works identically after the rename.

> This is an atomic refactoring step, not mixed with new feature code. If the Composition Root refactoring (from the separate implementation plan) has not yet been completed, do the rename as part of that work.

---

## Phase 0 — Data Layer: Enums, WeaponConfig, Weapon

**Goal:** establish the data foundation. No MonoBehaviours, no runtime behavior — only types that the rest of the system will use. The project compiles with unused classes; existing movement works unchanged.

### 0.1 Enums

Create two enums in `Runtime/Combat/Enums/`:

**`FireMode.cs`** — `SemiAutomatic`, `Automatic`. Determines how LMB input maps to shot attempts.

**`HitscanType.cs`** — `Single`, `Spread`, `Penetrating`. Determines how many rays are cast and whether they stop on first contact. `Penetrating` is included in the enum for completeness but is not implemented in this plan (Sniper Rifle is deferred).

### 0.2 WeaponConfig ScriptableObject

Create `WeaponConfig` in `Runtime/Combat/`. A `ScriptableObject` with a `[CreateAssetMenu]` attribute for easy asset creation.

**Fields** (all serialized, grouped with `[Header]`):

```
[Header("Identity")]
string WeaponName

[Header("Firing")]
FireMode FireMode
HitscanType HitscanType
int FireRate                    // RPM
// Computed read-only: FireInterval = 60f / FireRate (same pattern as DodgeTime)

[Header("Hitscan")]
float MaxRange
int PelletCount                 // 1 for non-shotgun
float ConeHalfAngle             // 0 for non-shotgun

[Header("Damage")]
float Damage                    // For single hitscan: per-hit. For shotgun: TotalDamage
float CriticalMultiplier        // Default 2.0

[Header("Spread")]
float MinSpreadAngle
float MaxSpreadAngle
float RecoilPerShot
float RecoveryDelay
float RecoveryRate
float WalkSpreadPenalty
float RunSpreadPenalty
float AirSpreadPenalty

[Header("Ammo")]
int MagazineSize
int MaxReserve
float ReloadTime
bool InfiniteReserve            // true only for starter pistol
```

**Computed property:** `public float FireInterval => 60f / FireRate;` — read-only, derived from `FireRate`. Same pattern as `DodgeConfig.DodgeTime`.

**Shotgun damage note:** For shotgun, the `Damage` field represents `TotalDamage`. Per-pellet damage is computed: `public float PelletDamage => PelletCount > 1 ? Damage / PelletCount : Damage;`. For non-shotgun weapons (`PelletCount == 1`), `PelletDamage == Damage`.

### 0.3 Weapon Runtime Class

Create `Weapon` in `Runtime/Combat/`. A plain C# class (not MonoBehaviour) holding runtime state for one equipped weapon.

**Constructor:** `Weapon(WeaponConfig config)` — stores the config reference, initializes magazine to `MagazineSize`, reserve to `MaxReserve`, spread to `MinSpreadAngle`.

**State fields:**

```csharp
public WeaponConfig Config { get; }
public int CurrentMagazine { get; private set; }
public int CurrentReserve { get; private set; }
public float CurrentSpread { get; private set; }
public float FireTimer { get; private set; }         // time since last shot
public float ReloadTimer { get; private set; }       // remaining reload time, 0 = not reloading
public bool IsReloading => ReloadTimer > 0f;
```

**Methods (stubs at this phase — logic added in later phases):**

- `Tick(float deltaTime)` — updates timers (fire cooldown, reload, spread recovery)
- `Fire()` — applies recoil to spread, resets fire timer, decrements magazine
- `StartReload()` — begins reload timer
- `CompleteReload()` — transfers rounds from reserve to magazine
- `CancelReload()` — resets reload timer without transferring
- `ResetSpread()` — sets `CurrentSpread` to `MinSpreadAngle` (for weapon switch)
- `AddReserve(int amount)` — adds ammo to reserve, clamped to `MaxReserve`

At this phase, method bodies can be empty or minimal (just enough that the class compiles). Full logic is added in the phases where each system is implemented.

### 0.4 Create Config Assets

Create three `WeaponConfig` assets in `Assets/_Project/ScriptableObjects/Weapons/` (or similar data folder):

- `StarterPistol_Config` — semi-auto, single hitscan, `InfiniteReserve = true`
- `AssaultRifle_Config` — automatic, single hitscan
- `Shotgun_Config` — semi-auto, spread hitscan, `PelletCount > 1`, `ConeHalfAngle > 0`

> Concrete numeric values are balance questions. Use placeholder values that feel reasonable: pistol FireRate ≈ 600, AR FireRate ≈ 600, Shotgun FireRate ≈ 80. These will be tuned during playtesting.

### 0.5 Verification
- Project compiles with no errors
- Three `WeaponConfig` assets exist and are editable in Inspector
- `Weapon` can be instantiated in a test script: `var w = new Weapon(config);`
- Existing movement behavior is unchanged

---

## Phase 1 — Aim System and Character Flip

**Goal:** the player can aim with the mouse. A world-space direction is computed from the fire origin to the cursor. The character visually faces the cursor at all times. No shooting yet — only aiming and flipping.

### 1.1 AimController MonoBehaviour

Create `AimController` in `Runtime/Combat/`. Responsibilities:

- Receive screen-space cursor position via public API (input-agnostic, same pattern as `CharacterMover2D.Move()`)
- Convert to world-space via camera
- Compute aim direction from fire origin to aim world point
- Control character flip based on cursor position relative to character

**Public API (called by `CombatInputReader` or AI):**

```csharp
public void SetAimScreenPosition(Vector2 screenPosition)  // for player (mouse)
public void SetAimDirection(Vector2 worldDirection)        // for AI (direct direction)
```

`SetAimScreenPosition` converts screen coordinates to world space, then computes direction from fire origin. `SetAimDirection` accepts a normalized world-space direction directly — bypasses the screen-to-world conversion. Both methods update the same internal `AimDirection` and trigger the same flip logic. This dual API keeps the controller input-agnostic without forcing AI callers through an unnecessary coordinate conversion.

**Public read-only properties (consumed by `ShootingController`):**

```csharp
public Vector2 AimDirection { get; }        // normalized
public Vector2 AimWorldPoint { get; }       // for crosshair later
public Vector2 FireOriginPosition { get; }  // world position of fire origin transform
```

**Internal state:**

- `_camera` — reference to the main camera, received via `Initialize(Camera)`
- `_fireOrigin` — child `Transform` on the player, resolved via `GetComponentInChildren` or a serialized reference with a known tag/name. For the prototype: a `[SerializeField] private Transform FireOrigin` set in Inspector to a child object offset to the upper body area.

### 1.2 Fire Origin Setup

The fire origin is a child `Transform` on the player GameObject, offset to the chest/shoulder area. For the grey-box prototype (character is a rectangle), this is a simple empty child object positioned at approximately the upper-third of the character's height.

Create this child object manually in the scene. The `AimController` references it via `[SerializeField]` — this is instance configuration (the offset is tuned per-GameObject), not a dependency.

> **Future note:** when skeletal animation is added, the fire origin will be a bone or a child of a bone. The `[SerializeField]` reference will simply point to that bone's transform — no code change needed in `AimController`.

### 1.3 Character Flip

**Rule from GDD:** `cursorWorldPosition.x >= character.position.x` → face right (`localScale.x = 1`). Otherwise → face left (`localScale.x = -1`).

Flip is applied by `AimController` to `transform.localScale.x`. This flips the entire visual hierarchy including the fire origin, which ensures it stays on the correct side.

**Migration from movement system:** If `CharacterMover2D` or any movement sub-state currently sets `transform.localScale` or calls `SpriteRenderer.flipX`, that logic must be removed. `AimController` becomes the single source of truth for character facing. Check:
- `CharacterMover2D` itself
- `WalkSubState`, `RunSubState` (might flip based on horizontal input direction)
- `DodgeState` (might flip based on dodge direction)

If flip logic exists in movement, remove it and verify that movement still works (character moves correctly regardless of facing direction). Movement direction and facing direction are independent — the character can walk left while aiming right.

### 1.4 Initialize Method

```csharp
private Camera _camera;
private bool _initialized;

public void Initialize(Camera mainCamera)
{
    _camera = mainCamera;
    _initialized = true;
}
```

`Camera` is an external dependency — passed via `Initialize()`, not resolved internally. The Composition Root passes `Camera.main`.

> **Why not resolve Camera.main inside AimController?** `Camera.main` is a scene-level dependency. Resolving it internally hides the dependency and makes the component harder to test or reuse in contexts with multiple cameras. Passing it through `Initialize()` keeps the Composition Root as the single place that knows about the scene structure.

### 1.5 Initialization Guard

Guard in `Update()` (or `LateUpdate()` — see timing discussion below):

```csharp
private void Update()
{
    if (!_initialized) return;
    // ... aim update
}
```

### 1.6 Update Timing

`AimController` updates in `Update()`, not `FixedUpdate()`. Mouse position changes every frame; updating aim direction in `FixedUpdate()` would cause visible lag on high-framerate displays. The aim direction is a visual/input concern — it does not need to be synchronized with the physics tick.

`ShootingController` will read `AimDirection` in `Update()` when firing (raycasts are not physics-tick-dependent for hitscan). The movement system reads nothing from the aim system.

### 1.7 CombatInputReader Stub

Create `CombatInputReader` in `Runtime/Combat/`. At this phase it only reads mouse position and passes it to `AimController`. Shooting input is added in Phase 2.

```csharp
public void Initialize(AimController aimController, ShootingController shootingController)
{
    _aimController = aimController;
    _shootingController = shootingController;
    _initialized = true;
}

private void Update()
{
    if (!_initialized) return;

    // Aim
    if (Mouse.current != null)
        _aimController.SetAimScreenPosition(Mouse.current.position.ReadValue());
}
```

`ShootingController` is accepted in `Initialize()` but not used yet — avoids changing the signature in Phase 2.

### 1.8 Composition Root Wiring

Update `PlayerCompositionRoot.Awake()`:

```csharp
private void Awake()
{
    var mover = GetComponent<CharacterMover2D>();
    var inputReader = GetComponent<MovementInputReader>();
    var debugOverlay = GetComponent<MovementDebugOverlay>();
    var aimController = GetComponent<AimController>();
    var combatInputReader = GetComponent<CombatInputReader>();

    mover.Initialize(WalkingConfig, DodgeConfig);
    aimController.Initialize(Camera.main);
    inputReader.Initialize(mover);
    combatInputReader.Initialize(aimController, null); // shootingController not created yet
    debugOverlay.Initialize(mover, WalkingConfig);
}
```

`ShootingController` does not exist yet — pass `null` for now. `CombatInputReader` must handle `null` gracefully (only call shooting methods when the reference is non-null). Alternatively, pass a stub. The null-check is simpler for a temporary state that lasts one phase.

> **Inspector:** attach `AimController` and `CombatInputReader` to the player GameObject. Create the fire origin child transform and assign it to `AimController.FireOrigin` in Inspector.

### 1.9 Verification
- Character faces toward the mouse cursor at all times
- Moving the cursor to the left of the character flips it; moving right flips back
- Movement works unchanged: A/D walk, Space jump, Shift dodge, S drop-through
- Character can walk in one direction while facing the other (backpedaling)
- Fire origin child transform flips correctly with the character
- If movement previously had flip logic: confirm it is removed and movement still works

---

## Phase 2 — Firing Core: Fire Rate and Single Hitscan

**Goal:** the player can fire single-hitscan weapons. The Starter Pistol fires on click (semi-auto), the Assault Rifle fires on hold (auto). No spread, no ammo — infinite firing to isolate the fire rate and hitscan systems.

### 2.1 ShootingController MonoBehaviour

Create `ShootingController` in `Runtime/Combat/`. This is the central orchestrator for all combat logic that isn't aim or input.

**Initialize:**

```csharp
public void Initialize(CharacterMover2D mover, AimController aimController, WeaponConfig starterPistolConfig)
{
    _mover = mover;
    _aimController = aimController;
    _weaponHolder = new WeaponHolder();
    _weaponHolder.Equip(0, starterPistolConfig); // slot 0 = pistol
    _weaponHolder.SwitchTo(0);
    _initialized = true;
}
```

`CharacterMover2D` is a dependency — needed to check dodge state and movement state (for spread, in Phase 3). Received via `Initialize()`.

`WeaponHolder` is created internally — it is an implementation detail of `ShootingController`, not a dependency that varies by context. Same reasoning as `CollisionSlideResolver2D` being created inside `CharacterMover2D`.

### 2.2 WeaponHolder Class

Create `WeaponHolder` in `Runtime/Combat/`. Plain C# class, 6 slots.

```csharp
public class WeaponHolder
{
    public const int SlotCount = 6;

    private readonly Weapon[] _slots = new Weapon[SlotCount];
    private int _activeSlot = -1;

    public Weapon ActiveWeapon => _activeSlot >= 0 ? _slots[_activeSlot] : null;
    public int ActiveSlot => _activeSlot;

    public void Equip(int slot, WeaponConfig config) { ... }
    public void Unequip(int slot) { ... }
    public bool SwitchTo(int slot) { ... }
    public Weapon GetWeapon(int slot) { ... }
}
```

- `Equip` creates a new `Weapon(config)` in the specified slot
- `SwitchTo` changes active slot, returns false if slot is empty or same slot
- For this phase: only slot 0 (pistol) is equipped

### 2.3 HitscanLayerMask

Add `[SerializeField] private LayerMask HitscanLayerMask` to `ShootingController`. This is instance configuration — stays on the component, not in `Initialize()`. Configured in Inspector to include enemy colliders and Ground layer, exclude Player layer and Platform layer.

> **Single mask for all weapons** — per the GDD design decision. No per-weapon overrides.

### 2.4 Fire Rate Timer

`Weapon.FireTimer` tracks time since the last shot. Incremented in `Weapon.Tick(deltaTime)`. A shot can only fire if `FireTimer >= Config.FireInterval`.

```csharp
// In Weapon:
public bool CanFire => FireTimer >= Config.FireInterval && !IsReloading;

public void Tick(float deltaTime)
{
    FireTimer += deltaTime;
    // ... reload timer, spread recovery added in later phases
}
```

### 2.5 Firing Modes in CombatInputReader

`CombatInputReader` translates raw input into the correct API calls based on the active weapon's fire mode. This is an important detail: the input reader knows about fire modes because it determines *when* to call `Fire()`.

```csharp
// Semi-auto: fire on press only
if (Mouse.current.leftButton.wasPressedThisFrame)
    _shootingController.RequestFire();

// Automatic: fire while held
if (Mouse.current.leftButton.isPressed)
    _shootingController.RequestFire();
```

**How does `CombatInputReader` know the fire mode?** It calls `ShootingController.RequestFire()` unconditionally every frame the button is in the relevant state. `ShootingController` handles the mode logic internally:

**Option A — Input reader is mode-aware:** `CombatInputReader` checks `shootingController.ActiveFireMode` and calls `RequestFire()` only on press (semi-auto) or every frame (auto).

**Option B — Controller is mode-aware:** `CombatInputReader` sends both `FirePressed()` and `FireHeld()` signals. `ShootingController` picks the relevant one based on the active weapon's mode.

**Decision: Option B.** The input reader should not contain game logic. It reports raw input state; the controller interprets it. This mirrors how `MovementInputReader` calls `Move(direction)` every frame without knowing whether the character is in Walk or Run state — the controller decides.

```csharp
// CombatInputReader.Update():
if (Mouse.current.leftButton.wasPressedThisFrame)
    _shootingController.OnFirePressed();
if (Mouse.current.leftButton.isPressed)
    _shootingController.OnFireHeld();
```

**ShootingController logic:**

```csharp
public void OnFirePressed()
{
    _firePressed = true;
}

public void OnFireHeld()
{
    _fireHeld = true;
}

// In Update(), after input flags are set:
private void ProcessFiring()
{
    var weapon = _weaponHolder.ActiveWeapon;
    if (weapon == null) return;
    if (IsDodging()) return; // dodge blocks firing

    bool wantsFire = weapon.Config.FireMode == FireMode.SemiAutomatic
        ? _firePressed
        : _fireHeld;

    if (wantsFire && weapon.CanFire)
    {
        ExecuteHitscan(weapon);
        weapon.Fire();
    }

    _firePressed = false;
    _fireHeld = false;
}
```

### 2.6 Single Hitscan Execution

When a shot fires, `ShootingController` performs a `Physics2D.Raycast` from the fire origin along the aim direction (no spread yet), up to `MaxRange`.

```csharp
private void ExecuteHitscan(Weapon weapon)
{
    Vector2 origin = _aimController.FireOriginPosition;
    Vector2 direction = _aimController.AimDirection;
    float range = weapon.Config.MaxRange;

    RaycastHit2D hit = Physics2D.Raycast(origin, direction, range, HitscanLayerMask);

    if (hit.collider != null)
    {
        // Hit detected — damage application is a future system
        Debug.Log($"Hit: {hit.collider.name} at {hit.point}");
    }

    // Debug visualization
    Debug.DrawRay(origin, direction * (hit.collider != null ? hit.distance : range),
        hit.collider != null ? Color.red : Color.yellow, 0.1f);
}
```

For this phase, hits are logged and visualized with `Debug.DrawRay`. Damage application requires an enemy health system that doesn't exist yet — the hitscan system only needs to detect hits.

### 2.7 Dodge Check

`ShootingController` reads dodge state from `CharacterMover2D`. The cleanest approach is a public property on `CharacterMover2D`:

```csharp
// On CharacterMover2D — new public property:
public bool IsDodging => _topFsm.CurrentState is DodgeState;
```

This is a read-only observation, not a modification. It uses the existing typed reference to `DodgeState` that `CharacterMover2D` already holds. Adding this property is a minimal, non-breaking change to the movement system.

> **Alternative:** read `DebugStateName == "DodgeState"`. This works but couples to a string name. The typed check is more robust.

### 2.8 Update Composition Root Wiring

```csharp
[Header("Combat")]
[SerializeField] private WeaponConfig StarterPistolConfig;

private void Awake()
{
    var mover = GetComponent<CharacterMover2D>();
    var inputReader = GetComponent<MovementInputReader>();
    var debugOverlay = GetComponent<MovementDebugOverlay>();
    var aimController = GetComponent<AimController>();
    var shootingController = GetComponent<ShootingController>();
    var combatInputReader = GetComponent<CombatInputReader>();

    mover.Initialize(WalkingConfig, DodgeConfig);
    aimController.Initialize(Camera.main);
    shootingController.Initialize(mover, aimController, StarterPistolConfig);
    inputReader.Initialize(mover);
    combatInputReader.Initialize(aimController, shootingController);
    debugOverlay.Initialize(mover, WalkingConfig);
}
```

Order: `mover` → `aimController` → `shootingController` (depends on both) → input readers → debug.

> **Inspector:** attach `ShootingController` to the player GameObject. Assign `StarterPistolConfig` on `PlayerCompositionRoot`. Assign `HitscanLayerMask` on `ShootingController`.

### 2.9 Ammo Bypass for Testing

At this phase, `Weapon.Fire()` does not decrement ammo. The fire timer resets but magazine is not consumed. This allows testing fire rate and hitscan in isolation. Ammo is introduced in Phase 4.

```csharp
// Weapon.Fire() — Phase 2 version:
public void Fire()
{
    FireTimer = 0f;
    // No ammo decrement yet — added in Phase 4
}
```

### 2.10 Verification
- Clicking LMB fires a single hitscan ray (visible via `Debug.DrawRay` in Scene View)
- Holding LMB does NOT repeat fire with the Starter Pistol (semi-auto)
- Switching to an auto weapon (not yet possible via input — test by changing `StarterPistolConfig` to automatic temporarily) fires continuously while LMB is held
- Fire rate is respected: rapid clicking faster than `FireInterval` does not fire extra shots
- Firing is blocked during dodge
- Character still faces the cursor, movement works unchanged
- `Debug.DrawRay` shows red line on hit, yellow on miss

---

## Phase 3 — Spread and Accuracy

**Goal:** shots deviate from the perfect aim direction based on weapon spread. Spread increases with recoil on each shot, recovers after a pause, and is penalized by movement state.

### 3.1 Spread State on Weapon

`Weapon` already has `CurrentSpread` and `FireTimer` from Phase 0. Now implement the spread lifecycle in `Weapon.Tick()`:

```csharp
public void Tick(float deltaTime)
{
    FireTimer += deltaTime;

    // Spread recovery
    if (FireTimer >= Config.RecoveryDelay)
    {
        CurrentSpread = Mathf.Max(
            CurrentSpread - Config.RecoveryRate * deltaTime,
            Config.MinSpreadAngle);
    }
}
```

And in `Weapon.Fire()`:

```csharp
public void Fire()
{
    FireTimer = 0f;
    CurrentSpread = Mathf.Min(CurrentSpread + Config.RecoilPerShot, Config.MaxSpreadAngle);
}
```

### 3.2 Movement State Detection — Architectural Analysis

The GDD poses an open question: how should the shooting system determine the character's movement state for spread penalties? The spread system needs to know whether the character is Idle, Walking, Running, or Airborne. This information lives inside the movement system. The question is how to pass it across the domain boundary.

Three approaches were considered. The analysis is documented here as a reference for future cross-domain communication decisions.

**Option A — Pass `WalkingConfig` to `ShootingController.Initialize()`**

`ShootingController` receives `WalkingConfig` as a dependency and reads `WalkSpeed` / `RunSpeed` directly, comparing them with `CharacterMover2D.Velocity.x` to classify the movement state.

Pros:
- Simplest implementation — one extra parameter in `Initialize()`, direct field access.
- `ShootingController` gets live values even if configs are tweaked in Play Mode.

Cons:
- **Direct cross-domain dependency.** The `Combat` namespace depends on a `Movement` ScriptableObject at the config level. Renaming a field in `WalkingConfig` or splitting the SO breaks `ShootingController`.
- **Semantic leakage.** The shooting system learns *how* movement defines its speed tiers — information it does not need. It only needs the *result*: what state the character is in.
- **Scaling problem.** New movement modes (`FlyingState`, `SwimmingState`) with their own configs would require `ShootingController.Initialize()` to accept additional parameters. The signature grows with every new movement mode.

**Option B — Expose speed thresholds as properties on `CharacterMover2D`**

`CharacterMover2D` adds `public float WalkSpeed => _walkingConfig.WalkSpeed;` and `public float RunSpeed => _walkingConfig.RunSpeed;`. `ShootingController` reads these generic properties and compares with `Velocity.x`.

Pros:
- No dependency on `WalkingConfig`. `ShootingController` knows only `CharacterMover2D`, which it already receives.
- One less coupling level compared to Option A — the shooting system does not know about ScriptableObject configs.

Cons:
- **Semi-hidden coupling.** `ShootingController` still knows that the character has *exactly two discrete speed thresholds* and must compare velocity against them. If movement adds a third tier (Walk, Jog, Run) or switches to continuous speed scaling, the comparison logic in `ShootingController` must be updated.
- **Duplicated classification logic.** The movement system already knows the character is running (FSM is in `RunSubState`). The shooting system re-derives this from raw velocity, creating two sources of truth that can disagree — for example, during acceleration when velocity hasn't reached `RunSpeed` yet but the FSM is already in `RunSubState`.
- **API pollution.** `WalkSpeed` and `RunSpeed` are exposed on `CharacterMover2D` solely for external consumers. The movement system itself never uses them through these properties — its sub-states access them through `WalkingConfig` directly. Public API should reflect the component's own responsibilities, not the needs of other systems.

**Option C — `CharacterMover2D` exposes a `CharacterMovementState` enum**

`CharacterMover2D` computes and exposes a `public CharacterMovementState MovementState` property, returning `Idle`, `Walking`, `Running`, or `Airborne`. The shooting system reads the enum and maps it to penalties via a `switch` — no knowledge of speeds, configs, or FSM internals.

Pros:
- **Minimal API contract.** The boundary between domains is one enum and one property. The shooting system asks "what state?" and gets a semantic answer. It doesn't know *why* the character is running — just that it is.
- **Single source of truth.** The "am I running?" decision is made in one place (`CharacterMover2D`), not re-derived by every consumer.
- **Scales cleanly.** Adding `Crouching` or `Swimming` means extending the enum and one `get` accessor. Consumers add one `case` to their `switch`. No new dependencies, no new parameters.
- **AI-friendly.** AI agents reading `MovementState` get the same answer without knowing about configs.

Cons:
- **Requires modifying `CharacterMover2D`.** A new enum and property must be added — a small but non-zero change to the movement system. The change does not affect existing behavior.
- **Discretization.** The enum cannot express "how fast within the run range." If a future system needs continuous speed-based penalties (not discrete state-based), the enum alone is insufficient. However, the GDD defines discrete penalty tiers (Idle, Walk, Run, Air), so this is not a current concern.
- **`IsRunRequested` vs velocity for Running classification.** The property uses `IsRunRequested` (intent) rather than velocity comparison (observation). This is correct for player input (Shift held = running intent), but AI agents must remember to set `IsRunRequested = true` if they want the Running state recognized. This is a documented behavioral contract, consistent with how the movement system already works (see API Behavioral Notes — task 89 in `CharacterMover2D_Notes.md`).

**Decision: Option C.** At the domain boundary, the consumer should receive the *minimum information needed* to do its job. The shooting system needs a categorical state, not raw speeds. Option C provides exactly that — a semantic enum that hides all movement implementation details behind a single property.

```csharp
// New enum (in Runtime/Movement/ — owned by the movement domain):
public enum CharacterMovementState { Idle, Walking, Running, Airborne }

// On CharacterMover2D — new public property:
public CharacterMovementState MovementState
{
    get
    {
        if (!IsGrounded) return CharacterMovementState.Airborne;
        if (Mathf.Abs(Velocity.x) <= 0.01f) return CharacterMovementState.Idle;
        if (IsRunRequested) return CharacterMovementState.Running;
        return CharacterMovementState.Walking;
    }
}
```

> **Why `IsRunRequested` instead of speed comparison?** Speed is continuous and may be between walk and run speeds during acceleration. `IsRunRequested` reflects the player's intent: Shift held = running, regardless of current speed. This matches the GDD's conceptual penalty tiers. For AI agents: setting `IsRunRequested = true` is required for the Running state to be recognized — same contract as documented in task 89 for `IsJumpHeld`.

```csharp
// In ShootingController:
private float GetMovementSpreadPenalty(Weapon weapon)
{
    return _mover.MovementState switch
    {
        CharacterMovementState.Idle => 0f,
        CharacterMovementState.Walking => weapon.Config.WalkSpreadPenalty,
        CharacterMovementState.Running => weapon.Config.RunSpreadPenalty,
        CharacterMovementState.Airborne => weapon.Config.AirSpreadPenalty,
        _ => 0f
    };
}
```

### 3.4 Apply Spread to Shot Direction

When firing, the final spread angle is computed and applied:

```csharp
private Vector2 ApplySpread(Vector2 baseDirection, float finalSpread)
{
    float spreadOffset = Random.Range(-finalSpread, finalSpread);
    float baseAngle = Mathf.Atan2(baseDirection.y, baseDirection.x) * Mathf.Rad2Deg;
    float finalAngle = (baseAngle + spreadOffset) * Mathf.Deg2Rad;
    return new Vector2(Mathf.Cos(finalAngle), Mathf.Sin(finalAngle));
}
```

In `ExecuteHitscan`:

```csharp
float finalSpread = weapon.CurrentSpread + GetMovementSpreadPenalty(weapon);
Vector2 direction = ApplySpread(_aimController.AimDirection, finalSpread);
```

### 3.5 Spread Reset on Weapon Switch

Not applicable yet — weapon switching is Phase 5. But `Weapon.ResetSpread()` is already defined in Phase 0. It will be called from `WeaponHolder.SwitchTo()` in Phase 5.

### 3.6 Verification
- Standing still, first shot fires straight at the cursor (MinSpreadAngle = 0 for test config)
- Rapid firing causes visible spread increase — shots deviate further from center
- Stopping fire for RecoveryDelay seconds → spread visually tightens
- Walking adds noticeable spread; running adds more; jumping adds maximum
- Standing still after running → penalty disappears immediately (it's per-shot, not accumulated)
- Spread does not exceed MaxSpreadAngle under any combination of recoil + movement
- Movement still works unchanged

---

## Phase 4 — Ammo and Reload

**Goal:** weapons consume ammo when firing and must be reloaded. The reload can be interrupted by dodge. Starter Pistol has infinite reserve.

### 4.1 Ammo Consumption

Update `Weapon.Fire()` to decrement magazine:

```csharp
public void Fire()
{
    FireTimer = 0f;
    CurrentSpread = Mathf.Min(CurrentSpread + Config.RecoilPerShot, Config.MaxSpreadAngle);
    CurrentMagazine--;
}
```

Update `Weapon.CanFire`:

```csharp
public bool CanFire => FireTimer >= Config.FireInterval && !IsReloading && CurrentMagazine > 0;
```

### 4.2 Reload Logic

Reload is initiated by the player (R key) or automatically on dry fire attempt.

**In `Weapon`:**

```csharp
public bool CanReload => !IsReloading
    && CurrentMagazine < Config.MagazineSize
    && (CurrentReserve > 0 || Config.InfiniteReserve);

public void StartReload()
{
    ReloadTimer = Config.ReloadTime;
}

public void CompleteReload()
{
    int needed = Config.MagazineSize - CurrentMagazine;

    if (Config.InfiniteReserve)
    {
        CurrentMagazine = Config.MagazineSize;
    }
    else
    {
        int transferred = Mathf.Min(needed, CurrentReserve);
        CurrentMagazine += transferred;
        CurrentReserve -= transferred;
    }

    ReloadTimer = 0f;
}

public void CancelReload()
{
    ReloadTimer = 0f;
}
```

**In `Weapon.Tick()`:**

```csharp
if (IsReloading)
{
    ReloadTimer -= deltaTime;
    if (ReloadTimer <= 0f)
        CompleteReload();
}
```

### 4.3 Reload in ShootingController

```csharp
public void RequestReload()
{
    var weapon = _weaponHolder.ActiveWeapon;
    if (weapon == null) return;
    if (IsDodging()) return;
    if (!weapon.CanReload) return;

    weapon.StartReload();
}
```

**Auto-reload on dry fire:** When `OnFirePressed` or `OnFireHeld` triggers a fire attempt, the weapon has 0 magazine but ammo in reserve → start reload instead of firing:

```csharp
// In ProcessFiring():
if (wantsFire && weapon.CurrentMagazine == 0 && weapon.CanReload)
{
    weapon.StartReload();
    return;
}
```

**Firing blocked during reload:** Already handled by `Weapon.CanFire` checking `!IsReloading`.

### 4.4 Dodge Interrupts Reload

In `ShootingController.Update()`, check if dodge just started while reloading:

```csharp
private bool _wasDodging;

private void Update()
{
    if (!_initialized) return;

    bool isDodging = IsDodging();

    // Dodge interrupts reload
    if (isDodging && !_wasDodging)
    {
        _weaponHolder.ActiveWeapon?.CancelReload();
    }

    _wasDodging = isDodging;

    if (!isDodging)
    {
        _weaponHolder.ActiveWeapon?.Tick(Time.deltaTime);
        ProcessFiring();
    }
}
```

> **Design note:** The weapon timer is not ticked during dodge. This means fire cooldown and spread recovery pause while dodging. This is intentional — the character cannot act during dodge, so time should not progress on weapon systems. If playtesting reveals this feels wrong, ticking timers during dodge is a one-line change.

### 4.5 CombatInputReader — Reload Input

Add reload input to `CombatInputReader.Update()`:

```csharp
if (Keyboard.current.rKey.wasPressedThisFrame)
    _shootingController.RequestReload();
```

### 4.6 Verification
- Firing consumes ammo: magazine count decreases with each shot
- When magazine reaches 0, weapon stops firing
- Press R → reload animation (timer) → magazine refills from reserve
- Pressing LMB with empty magazine + ammo in reserve → auto-reload starts
- Pressing LMB with empty magazine + empty reserve → nothing happens
- Dodge interrupts reload: start reload, dodge mid-reload, magazine is NOT refilled
- Starter Pistol: reserve never runs out, reload always fills full magazine
- Create a test config with small magazine (e.g., 3 rounds) to easily verify ammo loop
- Movement still works unchanged

---

## Phase 5 — Weapon Inventory and Switching

**Goal:** the player has 6 weapon slots and can switch between them with number keys. Spread resets and reload cancels on switch.

### 5.1 Initial Loadout via Composition Root

Update `ShootingController.Initialize()` to accept additional test configs:

```csharp
public void Initialize(
    CharacterMover2D mover,
    AimController aimController,
    WeaponConfig starterPistolConfig,
    WeaponConfig[] testWeaponConfigs)  // optional, for prototype testing
```

For the prototype, `PlayerCompositionRoot` holds `[SerializeField]` references to test weapon configs and passes them. The shop system will handle dynamic equipping later.

```csharp
// In Initialize:
_weaponHolder.Equip(0, starterPistolConfig);
if (testWeaponConfigs != null)
{
    for (int i = 0; i < testWeaponConfigs.Length; i++)
    {
        if (testWeaponConfigs[i] != null)
            _weaponHolder.Equip(i + 1, testWeaponConfigs[i]); // slots 1+
    }
}
_weaponHolder.SwitchTo(0);
```

> **Composition Root config:** add `[SerializeField] private WeaponConfig[] TestWeaponLoadout` to `PlayerCompositionRoot` with a size-5 array (slots 1–5). Assign AssaultRifle to index 3 (slot 4) and Shotgun to index 1 (slot 2) to match the GDD slot mapping.

### 5.2 Weapon Switch Logic

**In `WeaponHolder`:**

```csharp
public bool SwitchTo(int slot)
{
    if (slot < 0 || slot >= SlotCount) return false;
    if (slot == _activeSlot) return false;
    if (_slots[slot] == null) return false;

    // Cancel reload on old weapon
    _slots[_activeSlot]?.CancelReload();

    // Reset spread on old weapon
    _slots[_activeSlot]?.ResetSpread();

    _activeSlot = slot;
    return true;
}
```

Per GDD: switching cancels reload (progress lost) and resets spread to `MinSpreadAngle`.

### 5.3 CombatInputReader — Weapon Switch Input

```csharp
// In CombatInputReader.Update():
for (int i = 0; i < 6; i++)
{
    if (Keyboard.current[Key.Digit1 + i].wasPressedThisFrame)
    {
        _shootingController.RequestWeaponSwitch(i);
        break;
    }
}
```

**In `ShootingController`:**

```csharp
public void RequestWeaponSwitch(int slot)
{
    _weaponHolder.SwitchTo(slot);
}
```

### 5.4 Active Weapon Ticking

Only the active weapon is ticked in `ShootingController.Update()` — inactive weapons retain their state (ammo counts, but timers frozen). This means an inactive weapon's fire timer doesn't advance, and its spread doesn't recover. Per the GDD, switching resets spread anyway, so the frozen spread is irrelevant.

### 5.5 Verification
- Press 1 → Starter Pistol active (semi-auto)
- Press 4 → Assault Rifle active (automatic)
- Press 2 → Shotgun active (semi-auto, no multi-ray yet — fires single ray, Phase 6 adds pellets)
- Pressing a key for the currently active slot → no effect
- Pressing a key for an empty slot → no effect
- Switching mid-reload cancels the reload
- Switching away from a heated weapon (high spread) and back → spread is reset to minimum
- Each weapon maintains its own ammo state independently

---

## Phase 6 — Spread Hitscan (Shotgun)

**Goal:** the Shotgun fires multiple hitscan rays in a cone pattern, with the two-stage spread calculation described in the GDD.

### 6.1 Multi-Ray Execution

`ExecuteHitscan` currently fires one ray. Refactor to support `PelletCount > 1`:

```csharp
private void ExecuteHitscan(Weapon weapon)
{
    Vector2 origin = _aimController.FireOriginPosition;
    Vector2 baseDirection = _aimController.AimDirection;
    float finalSpread = weapon.CurrentSpread + GetMovementSpreadPenalty(weapon);
    int pelletCount = weapon.Config.PelletCount;

    for (int i = 0; i < pelletCount; i++)
    {
        Vector2 direction;
        if (pelletCount > 1)
        {
            // Two-stage: cone offset + spread offset
            float coneOffset = Random.Range(
                -weapon.Config.ConeHalfAngle, weapon.Config.ConeHalfAngle);
            float spreadOffset = Random.Range(-finalSpread, finalSpread);
            direction = RotateDirection(baseDirection, coneOffset + spreadOffset);
        }
        else
        {
            direction = ApplySpread(baseDirection, finalSpread);
        }

        CastSingleRay(origin, direction, weapon);
    }
}
```

`CastSingleRay` handles a single `Physics2D.Raycast` + hit detection + debug visualization — the same logic that was previously inline in `ExecuteHitscan`.

### 6.2 Shotgun Damage

Each pellet uses `PelletDamage` (= `TotalDamage / PelletCount`). Different pellets can hit different enemies. This is handled by `CastSingleRay` dealing `weapon.Config.PelletDamage` per hit.

> Damage application itself is deferred — the enemy health system doesn't exist. But the per-pellet damage value is computed and available for when it does.

### 6.3 ConeHalfAngle = 0 for Non-Shotgun Weapons

For single-hitscan weapons, `ConeHalfAngle` is 0 and `PelletCount` is 1. The `pelletCount > 1` branch is never entered. No special-casing needed — the config values handle it.

### 6.4 Verification
- Shotgun fires multiple rays visible in Scene View (multiple `Debug.DrawRay` lines)
- Rays spread in a cone pattern centered on the aim direction
- Pellets can hit different colliders in the scene
- Standing still: pellets are distributed within the cone + MinSpread
- After sustained fire: pellets spread wider (recoil adds to the cone)
- Non-shotgun weapons are unaffected — Pistol and AR still fire single rays

---

## Phase 7 — Visual Feedback

**Goal:** add a dynamic crosshair that reflects spread, and placeholder muzzle flash and tracer effects. These are visual-only — no gameplay impact.

### 7.1 Dynamic Crosshair

The crosshair is a UI element that follows the mouse cursor and visualizes `FinalSpread`.

**Implementation approach:** A simple Canvas (Screen Space — Overlay) with four `RectTransform` line images (top, bottom, left, right) that move outward as spread increases and inward as it decreases.

Create a new MonoBehaviour `CrosshairUI` in `Runtime/Combat/`:

```csharp
public class CrosshairUI : MonoBehaviour
{
    [SerializeField] private RectTransform Top;
    [SerializeField] private RectTransform Bottom;
    [SerializeField] private RectTransform Left;
    [SerializeField] private RectTransform Right;
    [SerializeField] private float MinOffset;    // pixels at min spread
    [SerializeField] private float MaxOffset;    // pixels at max spread
```

`CrosshairUI` is configured with `[SerializeField]` references to UI elements — instance configuration. It receives `ShootingController` via `Initialize()` to read `FinalSpread`.

**Cursor hiding:** `Cursor.visible = false; Cursor.lockState = CursorLockMode.Confined;` in `Initialize()`. Restore in `OnDisable()`.

> For the prototype, the four lines can be simple white `Image` components on a Canvas. Art pass is deferred.

### 7.2 Muzzle Flash Placeholder

A simple sprite flash or particle effect at the fire origin position, played on each shot. For the prototype:

- Create a child `SpriteRenderer` on the fire origin transform, disabled by default
- On fire: enable it for a few frames, then disable

Alternatively, a basic `ParticleSystem` with a single burst emission. The exact implementation is an art/VFX decision — the plan only requires that the fire origin transform is the spawn point.

**Integration:** `ShootingController` calls a method after each shot (e.g., `_muzzleFlash.Play()`). This can be a simple MonoBehaviour on the fire origin child object, or managed internally by `ShootingController`.

### 7.3 Tracer Placeholder

A brief visual line from the fire origin to the hit point (or max range). For the prototype, `Debug.DrawRay` from Phase 2 is already doing this in Scene View. For Game View:

- Use `LineRenderer` with a short lifetime (disable after ~0.05s)
- Or instantiate a simple line prefab that auto-destroys

Tracers are cosmetic. No collision, no gameplay impact.

> **Pooling consideration:** For automatic weapons at high fire rates, creating/destroying tracer objects per shot may cause GC spikes. If performance is an issue, implement a simple object pool. For the prototype with 3 weapons, this is unlikely to be a problem.

### 7.4 Composition Root Wiring

`CrosshairUI` lives on a UI Canvas object, not on the player GameObject. It receives its dependency via `Initialize()` from whatever wires it — either `PlayerCompositionRoot` (if the crosshair is considered part of the player entity) or `SceneCompositionRoot` (if it's scene-level UI).

**Decision for prototype:** wire from `PlayerCompositionRoot`. The crosshair is tightly coupled to the player's weapon state. If multiple players are ever supported, each needs their own crosshair.

```csharp
// In PlayerCompositionRoot.Awake() — after shootingController is initialized:
var crosshair = FindObjectOfType<CrosshairUI>(); // or serialized reference
crosshair.Initialize(shootingController);
```

> **FindObjectOfType trade-off:** acceptable for a single-instance UI element in the prototype. For production, use a serialized reference or a scene-level registry.

### 7.5 Expose FinalSpread from ShootingController

`CrosshairUI` needs to read the current effective spread. Add a public property:

```csharp
// On ShootingController:
public float CurrentFinalSpread
{
    get
    {
        var weapon = _weaponHolder.ActiveWeapon;
        if (weapon == null) return 0f;
        return weapon.CurrentSpread + GetMovementSpreadPenalty(weapon);
    }
}
```

Also expose max possible spread for normalization:

```csharp
public float MaxPossibleSpread
{
    get
    {
        var weapon = _weaponHolder.ActiveWeapon;
        if (weapon == null) return 1f;
        return weapon.Config.MaxSpreadAngle + weapon.Config.AirSpreadPenalty;
    }
}
```

### 7.6 Verification
- Crosshair follows cursor, system cursor is hidden
- Crosshair lines spread outward when firing rapidly
- Crosshair tightens after RecoveryDelay seconds of not firing
- Walking/running/jumping causes visible crosshair spread increase
- Switching weapons updates crosshair to the new weapon's spread
- Muzzle flash plays at the fire origin on each shot
- Tracer line is briefly visible from fire origin to hit/max-range point
- All visual effects are cosmetic — removing them changes nothing about gameplay

---

## Phase 8 — Documentation

**Goal:** update project documentation to reflect the shooting system architecture.

### 8.1 Architecture.md — Folder Structure

Add `Runtime/Combat/` to the folder tree:

```
Runtime/
    Core/
        PlayerCompositionRoot.cs
    Combat/                  ← new
        AimController.cs
        ShootingController.cs
        CombatInputReader.cs
        Weapon.cs
        WeaponConfig.cs
        WeaponHolder.cs
        CrosshairUI.cs
        Enums/
            FireMode.cs
            HitscanType.cs
    Movement/
        MovementInputReader.cs       ← renamed from PlayerInputReader
        CharacterMovementState.cs    ← new enum
        ...existing...
```

### 8.2 Architecture.md — Class Map

Update the existing `PlayerInputReader` entry to reflect the rename to `MovementInputReader`.

Add entries for all new classes following the existing format:

- `MovementInputReader` — `Runtime/Movement/MovementInputReader.cs` — *(renamed from PlayerInputReader)* reads `Keyboard.current` for movement actions. Calls `CharacterMover2D` public API. Receives `CharacterMover2D` via `Initialize(CharacterMover2D)`. Depends on: `UnityEngine.InputSystem`, `CharacterMover2D`.
- `AimController` — `Runtime/Combat/AimController.cs` — converts screen-space cursor position to world-space aim direction. Controls character flip. Owns fire origin reference. Exposes dual API: `SetAimScreenPosition` for player input, `SetAimDirection` for AI. Receives deps via `Initialize(Camera)`. Depends on: `Camera`.
- `ShootingController` — `Runtime/Combat/ShootingController.cs` — orchestrates firing, spread, reload, hitscan execution. Owns `WeaponHolder` internally. Receives deps via `Initialize(CharacterMover2D, AimController, WeaponConfig, WeaponConfig[])`. Depends on: `CharacterMover2D`, `AimController`, `WeaponHolder`, `Weapon`, `WeaponConfig`.
- `CombatInputReader` — `Runtime/Combat/CombatInputReader.cs` — reads mouse/keyboard for combat actions. Calls `AimController` and `ShootingController` public APIs. The only combat file referencing `Keyboard`/`Mouse`. Receives deps via `Initialize(AimController, ShootingController)`. Depends on: `UnityEngine.InputSystem`, `AimController`, `ShootingController`.
- `WeaponConfig` — `Runtime/Combat/WeaponConfig.cs` — ScriptableObject with all weapon parameters. Computed read-only `FireInterval`, `PelletDamage`. No code deps.
- `WeaponHolder` — `Runtime/Combat/WeaponHolder.cs` — plain C# class. 6 weapon slots, active weapon tracking, switching with spread reset and reload cancel. Depends on: `Weapon`, `WeaponConfig`.
- `Weapon` — `Runtime/Combat/Weapon.cs` — plain C# class. Per-weapon runtime state: ammo, spread, timers. Depends on: `WeaponConfig`.
- `CrosshairUI` — `Runtime/Combat/CrosshairUI.cs` — UI element visualizing current spread angle. Receives deps via `Initialize(ShootingController)`. Depends on: `ShootingController`.
- `CharacterMovementState` — `Runtime/Movement/CharacterMovementState.cs` — enum (`Idle`, `Walking`, `Running`, `Airborne`). Exposed via `CharacterMover2D.MovementState` for cross-domain consumers.

Update `PlayerCompositionRoot` entry to include new dependencies.

### 8.3 Architecture.md — Active Patterns

Add entries:

**Input Agnosticism (Intent-Based API)** — a project-wide pattern applied to both movement and combat domains. Controllers expose public methods that describe **intents** (`Move`, `Jump`, `Fire`, `Reload`, `AimAt`), not **input sources** (`Keyboard.space`, `Mouse.leftButton`). Input readers are thin **adapters** that translate a specific input source into universal intent calls. This enables: (1) AI agents call the same API as the player — no special-cased AI code in controllers; (2) replay systems, tutorial scripts, and automated tests call the same API; (3) input source can change (keyboard → gamepad → InputActionAsset) without touching controllers. `MovementInputReader` and `CombatInputReader` are the only files referencing `Keyboard`/`Mouse`. `AimController` exposes a dual API: `SetAimScreenPosition` for player input, `SetAimDirection` for AI — both produce the same internal result.

**Value Object (WeaponConfig)** — ScriptableObject configuration pattern reused from `WalkingConfig`/`DodgeConfig`. Computed properties (`FireInterval`, `PelletDamage`) derived from serialized fields.

**Cross-Domain Enum Boundary (CharacterMovementState)** — when one domain (Combat) needs categorical information from another (Movement), the owning domain exposes a semantic enum rather than raw data or internal config types. This establishes a minimal API contract: the consumer gets exactly the answer it needs without learning implementation details of the provider. See Phase 3.2 architectural analysis for the full rationale and alternatives considered.

### 8.4 Architecture.md — Composition Root Section

Update the initialization order in the Composition Root section:

```
mover.Initialize(WalkingConfig, DodgeConfig)
aimController.Initialize(Camera.main)
shootingController.Initialize(mover, aimController, StarterPistolConfig, TestWeaponLoadout)
inputReader.Initialize(mover)
combatInputReader.Initialize(aimController, shootingController)
crosshairUI.Initialize(shootingController)
debugOverlay.Initialize(mover, WalkingConfig)
```

### 8.5 CLAUDE.md

Add a "Combat System Rules" block:
- Namespace: `SGL.Protocol.Runtime.Combat`
- `WeaponConfig` goes through `Initialize()` (it's a dependency, not instance config)
- `HitscanLayerMask` stays as `[SerializeField]` on `ShootingController` (instance config)
- `FireOrigin` stays as `[SerializeField]` on `AimController` (instance config)
- Combat input goes through `CombatInputReader`, not `MovementInputReader`
- All hitscan execution is in `ShootingController` — `Weapon` is state only, no raycasting
- Input readers follow `{Domain}InputReader` naming: `MovementInputReader`, `CombatInputReader`
- Controller APIs describe intents (`OnFirePressed`, `SetAimDirection`), never reference input hardware

### 8.6 GDD_ShootingAndAiming_EN.md — Close Open Questions

Update the Open Questions section:
- Movement state detection → closed: `CharacterMovementState` enum on `CharacterMover2D`, intent-based classification via `IsRunRequested`. Full analysis of three alternatives documented in implementation plan Phase 3.2

---

## Phase 9 — Full Integration Verification

**Goal:** confirm that the shooting system works correctly in all combinations with the movement system.

### 9.1 Manual Test Checklist — Shooting

- **Starter Pistol:** semi-auto click fires one shot. Holding LMB does NOT repeat. Infinite reserve — reload always works.
- **Assault Rifle:** holding LMB fires continuously at FireRate. Releasing stops immediately. First shot fires without delay.
- **Shotgun:** semi-auto click fires multiple rays in a cone. Visible spread pattern.
- **Spread:** increases with firing, recovers after pause, penalized by movement state (Idle < Walk < Run < Air).
- **Ammo:** magazine decrements, reload refills from reserve, empty weapon stops firing.
- **Reload:** R key starts reload. Dodge interrupts. Weapon switch interrupts. LMB during reload is ignored.
- **Dry fire:** LMB with empty magazine + reserve → auto-reload. LMB with both empty → no effect.
- **Weapon switch:** 1/2/4 switch between weapons. Spread resets. Reload cancels. Empty/same slot → no effect.
- **Crosshair:** follows cursor, expands/contracts with spread, hides system cursor.
- **Muzzle flash and tracers:** visible on each shot.

### 9.2 Manual Test Checklist — Movement Integration

- **Aim + movement:** character faces cursor while walking in any direction. Backpedaling works.
- **Dodge blocks shooting:** cannot fire or reload during dodge.
- **Dodge interrupts reload:** start reload, dodge mid-reload, magazine unchanged.
- **Spread during movement states:** visually confirm crosshair widens when walking, more when running, maximum when airborne.
- **Post-dodge weapon state:** after dodge completes, weapon returns to previous state (minus interrupted reload progress).
- **All movement verifications from Phase 7 of CharacterMover2D** still pass: walk, run, jump (full/low/coyote/buffer), fall, dodge, one-way platforms, ceiling, debug overlay.

### 9.3 Code Audit

- `AimController` has no input imports — receives cursor position via `SetAimScreenPosition()` and direction via `SetAimDirection()`
- `ShootingController` has no input imports — receives commands via public intent methods
- `CombatInputReader` is the only combat file referencing `Keyboard`/`Mouse`
- `MovementInputReader` is the only movement file referencing `Keyboard`/`Mouse` (renamed from `PlayerInputReader`)
- `CharacterMover2D` is not modified except for adding `IsDodging`, `MovementState`, and `CharacterMovementState` — no behavioral changes to movement
- All new components follow the Composition Root pattern: `Initialize()` for dependencies, `[SerializeField]` for instance config, `_initialized` guard
- `PlayerCompositionRoot` is the single place defining initialization order for all player components
- `WeaponConfig` assets are assigned on `PlayerCompositionRoot`, not on individual components
- Input readers follow `{Domain}InputReader` naming convention

---

## Time Estimate

| Phase | Contents | Estimate (hours) |
|---|---|---|
| Preliminary | Rename PlayerInputReader → MovementInputReader | 0.5 |
| 0 | Data layer: enums, WeaponConfig, Weapon | 1–2 |
| 1 | Aim system and character flip | 2–3 |
| 2 | Firing core: fire rate and single hitscan | 3–4 |
| 3 | Spread and accuracy | 2–3 |
| 4 | Ammo and reload | 2–3 |
| 5 | Weapon inventory and switching | 1–2 |
| 6 | Spread hitscan (Shotgun) | 1–2 |
| 7 | Visual feedback (crosshair, muzzle flash, tracers) | 2–4 |
| 8 | Documentation | 2–3 |
| 9 | Full integration verification | 2–3 |
| **Total** | | **18.5–29.5** |

> This is a new feature, not a refactoring — higher complexity and more unknowns than the Composition Root migration. The main risks are: (1) character flip migration if existing movement code currently handles it, (2) spread feel requiring iteration on config values, (3) crosshair UI setup if unfamiliar with Unity Canvas system. Each phase is independently verifiable, so issues are caught early.

---

## Open Decisions for Implementation

These are implementation-level decisions not resolved by the GDD. Each should be settled at the start of the relevant phase.

| Decision | Phase | Options | Notes |
|---|---|---|---|
| Fire origin: `[SerializeField] Transform` vs tag-based child lookup | 1 | SerializeField is simpler and explicit; tag is more flexible | Recommend SerializeField for prototype |
| Does CharacterMover2D currently have flip logic? | 1 | Check codebase before starting Phase 1 | If yes, remove it; if no, proceed directly |
| Weapon timer ticking during dodge | 4 | Pause all timers vs tick fire/recovery but block actions | Plan defaults to pause; change if feel is wrong |
| CrosshairUI wiring: PlayerCompositionRoot vs SceneCompositionRoot | 7 | Player-level is simpler for prototype | Revisit when SceneCompositionRoot is introduced |
| Muzzle flash implementation: SpriteRenderer vs ParticleSystem | 7 | Sprite is simpler; particles are more flexible | Either works for prototype |
| Tracer implementation: LineRenderer vs pooled prefab | 7 | LineRenderer is simpler for prototype | Pool if GC is an issue |

# Shooting & Aiming — Implementation Plan
> Project: Protocol | New feature on top of existing movement system

---

## Context and Goal

Implement shooting and aiming from `GDD_ShootingAndAiming_EN.md`. Prototype scope: Starter Pistol (semi-auto, single hitscan, infinite reserve), Assault Rifle (automatic, single hitscan), Shotgun (semi-auto, spread hitscan).

**Constraint:** every phase compiles and runs. Movement system unchanged throughout.

**Deferred:** Sniper Rifle (penetrating hitscan), SMG, Machine Gun, draw/holster time, partial reload, Gaussian spread distribution, enemy hit reactions, crosshair art.

---

## Architecture Overview

### Folder Structure

```
Runtime/
    Core/
        PlayerCompositionRoot.cs             ← updated
    Combat/                                  ← new
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
        MovementInputReader.cs               ← renamed from PlayerInputReader
        CharacterMovementState.cs            ← new enum
        ...existing...
```

Namespace: `SGL.Protocol.Runtime.Combat`.

### Design Decisions

**Rename `PlayerInputReader` → `MovementInputReader`.** With `CombatInputReader` added, the old name implies completeness it no longer has. New convention: `{Domain}InputReader`. Rename is a preliminary step before any combat code.

**Separate `CombatInputReader`.** Extending `MovementInputReader` would create a cross-namespace monolith (`Movement` depending on `Combat`). Separate readers follow SRP and map naturally to separate Action Maps when `InputActionAsset` is introduced.

**Class types:**

| Class | Type | Reason |
|---|---|---|
| `AimController` | MonoBehaviour | `Update()` for cursor tracking, owns fire origin `Transform` |
| `ShootingController` | MonoBehaviour | `Update()` for timers (fire rate, reload, spread recovery) |
| `CombatInputReader` | MonoBehaviour | `Update()` for input polling |
| `WeaponHolder` | Plain C# | Data + logic, no lifecycle. Owned by `ShootingController` |
| `Weapon` | Plain C# | Per-weapon runtime state. Created by `WeaponHolder` |
| `WeaponConfig` | ScriptableObject | Shareable weapon parameters |

**Character flip:** `AimController` is single source of truth. If `CharacterMover2D` has flip logic — remove it in Phase 1.

**Dodge integration:** `ShootingController` reads `CharacterMover2D.IsDodging` (read-only). Never modifies movement state.

**Input agnosticism (Intent-Based API).** Controllers expose **intents** (`Fire`, `Reload`, `AimAt`), never input sources. Input readers are thin **adapters**: translate `Mouse`/`Keyboard` into intent calls. AI agents call the same API — no special-cased code. `AimController` has dual API: `SetAimScreenPosition(Vector2)` for player (screen coords), `SetAimDirection(Vector2)` for AI (world direction). Both produce the same `AimDirection`.

### Dependency Graph

```
PlayerCompositionRoot.Awake()
    ├── mover.Initialize(WalkingConfig, DodgeConfig)
    ├── aimController.Initialize(mainCamera)
    ├── shootingController.Initialize(mover, aimController, starterPistolConfig)
    ├── movementInputReader.Initialize(mover)
    ├── combatInputReader.Initialize(aimController, shootingController)
    └── debugOverlay.Initialize(mover, WalkingConfig)
```

---

## Preliminary Step — Rename PlayerInputReader → MovementInputReader

Rename class, file, and all references in `PlayerCompositionRoot`. No behavioral changes. Verify movement works after rename.

---

## Phase 0 — Data Layer: Enums, WeaponConfig, Weapon

**Goal:** data foundation only. No MonoBehaviours, no runtime behavior.

### 0.1 Enums

`FireMode.cs` — `SemiAutomatic`, `Automatic`.
`HitscanType.cs` — `Single`, `Spread`, `Penetrating` (Penetrating included for completeness, not implemented).

### 0.2 WeaponConfig ScriptableObject

`[CreateAssetMenu]` ScriptableObject with all weapon parameters:

```
[Header("Firing")]       FireMode, HitscanType, int FireRate (RPM)
[Header("Hitscan")]      float MaxRange, int PelletCount, float ConeHalfAngle
[Header("Damage")]       float Damage, float CriticalMultiplier = 2f
[Header("Spread")]       float MinSpreadAngle, MaxSpreadAngle, RecoilPerShot,
                         RecoveryDelay, RecoveryRate,
                         WalkSpreadPenalty, RunSpreadPenalty, AirSpreadPenalty
[Header("Ammo")]         int MagazineSize, int MaxReserve, float ReloadTime, bool InfiniteReserve
```

Computed: `float FireInterval => 60f / FireRate;` and `float PelletDamage => PelletCount > 1 ? Damage / PelletCount : Damage;`.

### 0.3 Weapon Runtime Class

Plain C# class. Constructor: `Weapon(WeaponConfig config)` — sets magazine to `MagazineSize`, reserve to `MaxReserve`, spread to `MinSpreadAngle`.

State: `Config`, `CurrentMagazine`, `CurrentReserve`, `CurrentSpread`, `FireTimer`, `ReloadTimer`, `IsReloading`.

Methods (stubs, logic filled in later phases): `Tick(dt)`, `Fire()`, `StartReload()`, `CompleteReload()`, `CancelReload()`, `ResetSpread()`, `AddReserve(int)`.

### 0.4 Create Config Assets

Three assets in `Assets/_Project/ScriptableObjects/Weapons/`: `StarterPistol_Config` (semi-auto, single, InfiniteReserve=true), `AssaultRifle_Config` (auto, single), `Shotgun_Config` (semi-auto, spread, PelletCount>1, ConeHalfAngle>0). Concrete numeric values are placeholder — tuned during playtesting.

### 0.5 Verification
- Compiles. Config assets editable in Inspector. `new Weapon(config)` works. Movement unchanged.

---

## Phase 1 — Aim System and Character Flip

**Goal:** mouse aiming works, character faces cursor. No shooting yet.

### 1.1 AimController MonoBehaviour

Public API:

```csharp
public void SetAimScreenPosition(Vector2 screenPos)  // player (mouse)
public void SetAimDirection(Vector2 worldDirection)   // AI (direct)
```

Read-only: `Vector2 AimDirection`, `Vector2 AimWorldPoint`, `Vector2 FireOriginPosition`.

Internal: `_camera` via `Initialize(Camera)`. `[SerializeField] private Transform FireOrigin` — child transform at chest/shoulder area, set in Inspector (instance config).

### 1.2 Fire Origin Setup

Empty child GameObject on the player, offset to upper body. `[SerializeField]` on `AimController` points to it. Flips correctly with `localScale.x`.

### 1.3 Character Flip

Rule: `aimWorldPoint.x >= character.position.x` → `localScale.x = 1`, else `-1`.

**If `CharacterMover2D` or sub-states currently set `localScale` or `SpriteRenderer.flipX` — remove that logic.** Aim system is single source of truth. Movement direction and facing are independent.

### 1.4 Initialize and Guard

```csharp
public void Initialize(Camera mainCamera) { _camera = mainCamera; _initialized = true; }
```

Guard in `Update()`. Updates every frame (not `FixedUpdate`) — aim is visual/input, not physics.

### 1.5 CombatInputReader Stub

Reads `Mouse.current.position` → `_aimController.SetAimScreenPosition(...)`. `ShootingController` accepted in `Initialize()` but not used yet (null-safe).

### 1.6 Composition Root Wiring

Attach `AimController` and `CombatInputReader` to player GameObject. Create fire origin child. Update `PlayerCompositionRoot.Awake()`:

```csharp
aimController.Initialize(Camera.main);
combatInputReader.Initialize(aimController, null); // shootingController Phase 2
```

### 1.7 Verification
- Character faces cursor. Flip works both directions. Backpedaling works (walk left, aim right). Fire origin flips with character. Movement unchanged. If flip was removed from movement — movement still correct.

---

## Phase 2 — Firing Core: Fire Rate and Single Hitscan

**Goal:** fire single-hitscan weapons. Pistol semi-auto, AR automatic. No spread, no ammo.

### 2.1 ShootingController

```csharp
public void Initialize(CharacterMover2D mover, AimController aimController, WeaponConfig starterPistolConfig)
{
    _mover = mover;
    _aimController = aimController;
    _weaponHolder = new WeaponHolder();        // internal, like CollisionSlideResolver2D
    _weaponHolder.Equip(0, starterPistolConfig);
    _weaponHolder.SwitchTo(0);
    _initialized = true;
}
```

`[SerializeField] private LayerMask HitscanLayerMask` — instance config on the component.

### 2.2 WeaponHolder

Plain C# class. 6 slots (`Weapon[]`), `ActiveWeapon`, `ActiveSlot`. Methods: `Equip(slot, config)`, `Unequip(slot)`, `SwitchTo(slot)`, `GetWeapon(slot)`.

### 2.3 Fire Rate

`Weapon.CanFire => FireTimer >= Config.FireInterval && !IsReloading`. `Weapon.Tick(dt)` increments `FireTimer`.

### 2.4 Firing Modes — Input Design

`CombatInputReader` reports raw state, controller interprets:

```csharp
// CombatInputReader.Update():
if (Mouse.current.leftButton.wasPressedThisFrame) _shootingController.OnFirePressed();
if (Mouse.current.leftButton.isPressed)           _shootingController.OnFireHeld();
```

```csharp
// ShootingController.ProcessFiring():
bool wantsFire = weapon.Config.FireMode == FireMode.SemiAutomatic ? _firePressed : _fireHeld;
if (wantsFire && weapon.CanFire) { ExecuteHitscan(weapon); weapon.Fire(); }
```

### 2.5 Single Hitscan

`Physics2D.Raycast` from `FireOriginPosition` along `AimDirection`, distance `MaxRange`, mask `HitscanLayerMask`. Debug: `Debug.DrawRay` red on hit, yellow on miss.

### 2.6 Dodge Check — IsDodging Property

Add to `CharacterMover2D`:

```csharp
public bool IsDodging => _topFsm.CurrentState is DodgeState;
```

`ShootingController` blocks firing when `_mover.IsDodging`.

### 2.7 Composition Root Update

```csharp
[Header("Combat")]
[SerializeField] private WeaponConfig StarterPistolConfig;
```

Order: `mover` → `aimController` → `shootingController(mover, aimController, StarterPistolConfig)` → input readers → debug.

### 2.8 Ammo Bypass

`Weapon.Fire()` only resets `FireTimer`. No ammo decrement — added in Phase 4.

### 2.9 Verification
- LMB fires ray. Semi-auto: no repeat on hold. Auto (swap config to test): continuous fire on hold. Fire rate respected. Dodge blocks firing. Movement unchanged.

---

## Phase 3 — Spread and Accuracy

**Goal:** shots deviate from aim direction. Recoil on fire, recovery after pause, movement penalty.

### 3.1 Spread State on Weapon

```csharp
// Weapon.Tick(dt):
if (FireTimer >= Config.RecoveryDelay)
    CurrentSpread = Mathf.Max(CurrentSpread - Config.RecoveryRate * dt, Config.MinSpreadAngle);

// Weapon.Fire():
CurrentSpread = Mathf.Min(CurrentSpread + Config.RecoilPerShot, Config.MaxSpreadAngle);
```

### 3.2 Movement State Detection — Cross-Domain Boundary

Problem: `ShootingController` needs Idle/Walk/Run/Airborne for spread penalties. This info lives in the movement system. Three approaches analyzed:

| Approach | Mechanism | Key Problem |
|---|---|---|
| **A: Pass `WalkingConfig`** | `ShootingController` reads `WalkSpeed`/`RunSpeed` directly | Direct cross-domain dependency. Combat knows Movement config internals. Adding `FlyingState` breaks `Initialize()` signature. |
| **B: Expose speeds on `CharacterMover2D`** | `public float WalkSpeed => _walkingConfig.WalkSpeed;` | Hidden coupling — `ShootingController` re-derives state from velocity, duplicating classification logic. Two sources of truth can disagree during acceleration. Pollutes `CharacterMover2D` API with properties it doesn't use internally. |
| **C: Semantic enum** | `CharacterMover2D.MovementState` returns `CharacterMovementState` enum | Minimal contract — consumer gets categorical answer without speeds, configs, or FSM internals. Single source of truth. Scales via enum extension. |

**Decision: Option C.** Consumer should receive the minimum information needed. Enum hides all movement implementation.

```csharp
// Runtime/Movement/CharacterMovementState.cs:
public enum CharacterMovementState { Idle, Walking, Running, Airborne }

// CharacterMover2D — new property:
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

Uses `IsRunRequested` (intent) over velocity comparison — correct during acceleration when speed hasn't reached `RunSpeed` yet but FSM is already in `RunSubState`. AI agents must set `IsRunRequested = true` for Running to be recognized (same contract as `IsJumpHeld` — see task 89).

### 3.3 Movement Penalty in ShootingController

```csharp
private float GetMovementSpreadPenalty(Weapon weapon) => _mover.MovementState switch
{
    CharacterMovementState.Idle => 0f,
    CharacterMovementState.Walking => weapon.Config.WalkSpreadPenalty,
    CharacterMovementState.Running => weapon.Config.RunSpreadPenalty,
    CharacterMovementState.Airborne => weapon.Config.AirSpreadPenalty,
    _ => 0f
};
```

### 3.4 Apply Spread to Shot Direction

```csharp
float finalSpread = weapon.CurrentSpread + GetMovementSpreadPenalty(weapon);
float spreadOffset = Random.Range(-finalSpread, finalSpread);
float baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
float finalAngle = (baseAngle + spreadOffset) * Mathf.Deg2Rad;
Vector2 spreadDir = new(Mathf.Cos(finalAngle), Mathf.Sin(finalAngle));
```

### 3.5 Verification
- First shot accurate (MinSpread=0). Rapid fire → visible deviation. Recovery after pause. Walk/Run/Air add spread. Standing after running → penalty gone immediately. Spread capped at Max.

---

## Phase 4 — Ammo and Reload

**Goal:** weapons consume ammo, require reload. Dodge interrupts reload.

### 4.1 Ammo Consumption

`Weapon.Fire()` adds `CurrentMagazine--`. `CanFire` adds `&& CurrentMagazine > 0`.

### 4.2 Reload Logic

```csharp
// Weapon:
public bool CanReload => !IsReloading && CurrentMagazine < Config.MagazineSize
    && (CurrentReserve > 0 || Config.InfiniteReserve);

public void StartReload()  { ReloadTimer = Config.ReloadTime; }
public void CancelReload() { ReloadTimer = 0f; }
public void CompleteReload()
{
    int needed = Config.MagazineSize - CurrentMagazine;
    if (Config.InfiniteReserve) { CurrentMagazine = Config.MagazineSize; }
    else { int t = Mathf.Min(needed, CurrentReserve); CurrentMagazine += t; CurrentReserve -= t; }
    ReloadTimer = 0f;
}

// In Tick(dt):
if (IsReloading) { ReloadTimer -= dt; if (ReloadTimer <= 0f) CompleteReload(); }
```

### 4.3 ShootingController Integration

- `RequestReload()` — guarded by `IsDodging`, delegates to `weapon.StartReload()`.
- Auto-reload on dry fire: if `wantsFire && magazine == 0 && CanReload` → `StartReload()` instead of firing.
- Dodge interrupts: track `_wasDodging`, on dodge start → `CancelReload()`.
- Weapon timers not ticked during dodge (intentional — character can't act).

### 4.4 CombatInputReader

```csharp
if (Keyboard.current.rKey.wasPressedThisFrame) _shootingController.RequestReload();
```

### 4.5 Verification
- Firing depletes magazine. Empty → stops. R → reload → refill. LMB on empty+reserve → auto-reload. LMB on empty+empty → nothing. Dodge mid-reload → cancelled. Starter Pistol always has reserve. Small magazine config (3 rounds) for easy testing.

---

## Phase 5 — Weapon Inventory and Switching

**Goal:** 6 weapon slots, number key switching. Spread resets and reload cancels on switch.

### 5.1 Initial Loadout

Update `ShootingController.Initialize()` to accept `WeaponConfig[] testWeaponConfigs`. `PlayerCompositionRoot` gets `[SerializeField] private WeaponConfig[] TestWeaponLoadout` (size 5 for slots 1–5). Equip starter pistol in slot 0, test configs in subsequent slots.

### 5.2 Weapon Switch

```csharp
// WeaponHolder.SwitchTo(int slot):
if (slot == _activeSlot || _slots[slot] == null) return false;
_slots[_activeSlot]?.CancelReload();
_slots[_activeSlot]?.ResetSpread();
_activeSlot = slot;
return true;
```

### 5.3 Input

```csharp
for (int i = 0; i < 6; i++)
    if (Keyboard.current[Key.Digit1 + i].wasPressedThisFrame)
    { _shootingController.RequestWeaponSwitch(i); break; }
```

### 5.4 Verification
- 1/2/4 switch weapons. Same slot → no effect. Empty slot → no effect. Switch cancels reload. Switch resets spread. Each weapon keeps own ammo state.

---

## Phase 6 — Spread Hitscan (Shotgun)

**Goal:** multi-ray cone pattern with two-stage spread.

### 6.1 Multi-Ray Execution

Refactor `ExecuteHitscan` to loop `PelletCount` times:

```csharp
for (int i = 0; i < pelletCount; i++)
{
    float coneOffset = Random.Range(-Config.ConeHalfAngle, Config.ConeHalfAngle);
    float spreadOffset = Random.Range(-finalSpread, finalSpread);
    Vector2 dir = RotateDirection(baseDirection, coneOffset + spreadOffset);
    CastSingleRay(origin, dir, weapon);
}
```

For non-shotgun weapons: `PelletCount == 1`, `ConeHalfAngle == 0` — single-ray path unchanged.

Each pellet deals `PelletDamage` (`TotalDamage / PelletCount`). Different pellets can hit different targets.

### 6.2 Verification
- Shotgun fires multiple rays in cone (visible in Scene View). Pellets hit different colliders. Recoil widens cone. Pistol and AR unaffected.

---

## Phase 7 — Visual Feedback

**Goal:** dynamic crosshair, placeholder muzzle flash and tracers. Visual-only, no gameplay impact.

### 7.1 Dynamic Crosshair

`CrosshairUI` MonoBehaviour on a Screen Space Overlay Canvas. Four `RectTransform` lines expand/contract with `FinalSpread`. Hides system cursor.

`ShootingController` exposes: `float CurrentFinalSpread` and `float MaxPossibleSpread` (for normalization).

`CrosshairUI.Initialize(ShootingController)` — wired from `PlayerCompositionRoot` via `FindObjectOfType<CrosshairUI>()` (acceptable for single-instance prototype UI).

### 7.2 Muzzle Flash

Simple sprite flash or particle burst at fire origin on each shot. Implementation (SpriteRenderer vs ParticleSystem) is an art decision.

### 7.3 Tracers

Brief line from fire origin to hit point. `LineRenderer` with short lifetime, or `Debug.DrawRay` from Phase 2 is sufficient for prototype.

### 7.4 Verification
- Crosshair follows cursor, expands/contracts with spread and movement. Muzzle flash on shot. Tracer visible. Removing visuals changes nothing about gameplay.

---

## Phase 8 — Full Integration Verification

### 8.1 Shooting

- Starter Pistol: semi-auto, infinite reserve, reload works
- Assault Rifle: auto fire on hold, stops on release, no initial delay
- Shotgun: multi-ray cone, visible spread pattern
- Spread: recoil → recovery → movement penalties (Idle < Walk < Run < Air)
- Ammo: magazine depletion, reload, dry fire auto-reload, empty weapon
- Reload: R starts, dodge interrupts, weapon switch interrupts, LMB ignored during
- Weapon switch: 1/2/4, spread reset, reload cancel, empty/same slot no-op
- Crosshair, muzzle flash, tracers

### 8.2 Movement Integration

- Aim + movement: faces cursor while walking any direction
- Dodge blocks shooting and reload, interrupts reload in progress
- Spread reflects movement state (crosshair widens)
- All CharacterMover2D verifications still pass

### 8.3 Code Audit

- `AimController`, `ShootingController` — zero input imports, intent-based API only
- `CombatInputReader` — only combat file with `Keyboard`/`Mouse`
- `MovementInputReader` — only movement file with `Keyboard`/`Mouse`
- `CharacterMover2D` — only added `IsDodging`, `MovementState`, `CharacterMovementState`
- All components: `Initialize()` for deps, `[SerializeField]` for instance config, `_initialized` guard
- `PlayerCompositionRoot` — single initialization order source

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
| 7 | Visual feedback | 2–4 |
| 8 | Full integration verification | 2–3 |
| **Total** | | **17–27** |

---

## Open Decisions

| Decision | Phase | Notes |
|---|---|---|
| Fire origin: `[SerializeField] Transform` vs tag lookup | 1 | Recommend SerializeField |
| Does CharacterMover2D have flip logic? | 1 | Check codebase; remove if yes |
| Weapon timers during dodge: pause vs tick | 4 | Default pause; change if feel is wrong |
| CrosshairUI wiring: PlayerCompositionRoot vs SceneCompositionRoot | 7 | Player-level for prototype |
| Muzzle flash: SpriteRenderer vs ParticleSystem | 7 | Either works |
| Tracers: LineRenderer vs pooled prefab | 7 | LineRenderer for prototype |

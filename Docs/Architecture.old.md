# Protocol — Architecture

> Engine: Unity 6.3 | 2D URP | Language: C# | Genre: side-scroller shooter

---

## Folder & Assembly Structure

```
Assets/_Project/Scripts/SimpleGamesLover/Protocol/
    Shared/          — pure C# only, no UnityEngine dependency
        FSM/         — generic FSM infrastructure
    Runtime/         — MonoBehaviours, states, configs; Unity API allowed
        Movement/
            States/
    Tests/           — unit/integration tests, mirrors covered assembly
        Shared/
```

Namespace pattern: `SGL.Protocol.{Assembly}.{Subfolder}`
Examples:
- `Scripts/.../Shared/FSM/StateMachine.cs` → `SGL.Protocol.Shared.FSM`
- `Scripts/.../Runtime/Movement/CharacterMover2D.cs` → `SGL.Protocol.Runtime.Movement`
- `Scripts/.../Runtime/Movement/States/WalkingState.cs` → `SGL.Protocol.Runtime.Movement.States`

If a file could be used in an Runtime AND Editor script → it goes in `Shared/`, not `Runtime/`. 
Example: `Vector2Extensions.cs`.

---

## Class Map

| Class | File | Responsibility | Key Dependencies |
|-------|------|----------------|-----------------|
| `IState` | `Shared/FSM/IState.cs` | Lifecycle contract: `OnEnter()`, `OnExit()` | — |
| `ITickable` | `Shared/FSM/ITickable.cs` | Per-frame update contract: `Tick(float deltaTime)`. Standalone — does NOT inherit `IState` | — |
| `StateMachine<TState>` | `Shared/FSM/StateMachine.cs` | Generic FSM. Registers transitions, evaluates them in order, calls `OnExit`/`OnEnter`. No Tick. | `IState` |
| `Vector2Extensions` | `Shared/Vector2Extensions.cs` | Extension method `ProjectOnAxis(normal)` — projects a vector onto the surface tangent | UnityEngine |
| `CharacterMover2D` | `Runtime/Movement/CharacterMover2D.cs` | Top-level movement MonoBehaviour. Owns top FSM, performs ground/ceiling checks, applies movement via resolver. Manages one-way platform state. Input-agnostic public API. | `WalkingConfig`, `CollisionSlideResolver2D`, `StateMachine<IState>` |
| `WalkingConfig` | `Runtime/Movement/WalkingConfig.cs` | ScriptableObject with all movement parameters. Exposes computed properties (`Gravity`, `JumpVelocity`, `FallMultiplier`) and `HorizontalMoveParams` presets. | `HorizontalMoveParams` |
| `HorizontalMoveParams` | `Runtime/Movement/HorizontalMoveParams.cs` | `readonly struct`. Encapsulates `MaxSpeed`, `Acceleration`, `Deceleration` and the shared `Apply(velX, input, dt)` formula. Used by all five sub-states. | UnityEngine (Mathf) |
| `CollisionSlideResolver2D` | `Runtime/Movement/CollisionSlideResolver2D.cs` | Recursive collide-and-slide. Accepts optional `shouldIgnore` predicate (Strategy Pattern) applied after the dot-product filter. Returns safe displacement. `SKIN_WIDTH = 0.015f`, `MAX_BOUNCES = 3`. | `Vector2Extensions`, `Rigidbody2D` |
| `PlayerInputReader` | `Runtime/Movement/PlayerInputReader.cs` | Reads `Keyboard.current` (New Input System, no `.inputactions` asset). Calls `CharacterMover2D` public API. The only file referencing `Keyboard`/`Mouse`. | `UnityEngine.InputSystem` |
| `WalkingState` | `Runtime/Movement/States/WalkingState.cs` | Top-level FSM state. Owns a second `StateMachine<IState>` for sub-states. `OnEnter` calls `ResolveSubState()`. Manages coyote timer start on edge walk-off. | `CharacterMover2D`, `WalkingConfig`, `StateMachine<IState>` |
| `IdleSubState` | `Runtime/Movement/States/IdleSubState.cs` | No input, grounded. Decelerates to zero via `GroundWalkParams.Apply(v.x, 0f, dt)`. | `CharacterMover2D`, `WalkingConfig` |
| `WalkSubState` | `Runtime/Movement/States/WalkSubState.cs` | Horizontal input, no Shift. Accelerates to `WalkSpeed` via `GroundWalkParams.Apply()`. | `CharacterMover2D`, `WalkingConfig` |
| `RunSubState` | `Runtime/Movement/States/RunSubState.cs` | Horizontal input + Shift. Accelerates to `RunSpeed` via `GroundRunParams.Apply()`. | `CharacterMover2D`, `WalkingConfig` |
| `JumpSubState` | `Runtime/Movement/States/JumpSubState.cs` | `OnEnter`: sets `velocity.y = JumpVelocity`, consumes jump request, clears timers. `Tick`: gravity, low-jump multiplier, air control via `AirParams.Apply()`. | `CharacterMover2D`, `WalkingConfig` |
| `FallSubState` | `Runtime/Movement/States/FallSubState.cs` | `Tick`: fall gravity × `FallMultiplier`, clamp to `MaxFallSpeed`, air control, jump buffer capture, timer decrement. `OnExit`: zeroes `velocity.y`. | `CharacterMover2D`, `WalkingConfig` |
| `TestMover` | `Runtime/TestMover.cs` | Debug-only MonoBehaviour. WASD movement with `CollisionSlideResolver2D`, no FSM, no gravity. Used to test the resolver in isolation. | `CollisionSlideResolver2D` |

---

## FSM Hierarchy

Two-level HFSM implemented by reusing the same `StateMachine<TState>` class on both levels.

```
CharacterMover2D
└── StateMachine<IState>  _topFsm
        └── WalkingState  ← currently the only top-level state
                └── StateMachine<IState>  _subFsm
                        ├── IdleSubState
                        ├── WalkSubState
                        ├── RunSubState
                        ├── JumpSubState
                        └── FallSubState
```

**Why two levels:** a future `DodgeState` at the top level applies to all five sub-states with a single `WalkingState→DodgeState` transition instead of five.

### Sub-state transition map (registered in WalkingState constructor, checked in order)

- Ground → Jump: `CanJump()` = `IsJumpRequested && (IsGrounded || CoyoteTimer > 0)` — from Idle, Walk, Run
- Jump → Fall: `Velocity.y < 0`
- Ground → Fall: `!IsGrounded` — from Idle, Walk, Run (walked off edge)
- Fall → Jump (coyote): `IsJumpRequested && CoyoteTimer > 0`
- Fall → Jump (buffer): `IsGrounded && JumpBufferTimer > 0`
- Fall → Idle/Walk/Run: `IsGrounded && JumpBufferTimer <= 0` + input presence
- Idle ↔ Walk: input presence (no Shift)
- Walk ↔ Run: Shift held
- Run → Idle: input absent
- Idle → Run: input present + Shift

### ResolveSubState

Called in `WalkingState.OnEnter()`. Picks the starting sub-state based on current conditions:
- not grounded → Fall
- grounded + input + Shift → Run
- grounded + input → Walk
- otherwise → Idle

### Coyote time

Timer set in `WalkingState.Tick()` when entering Fall from any state except Jump (prevents double-jump via coyote). Decremented in `FallSubState.Tick()`.

### Jump buffer

Captured in `FallSubState.Tick()` when `IsJumpRequested` is true while airborne. `Fall→Jump` buffer transition fires on landing before `Fall→Idle/Walk`.

---

## Data Flow

### Per-frame (Update → FixedUpdate)

```
PlayerInputReader.Update()
    ├── CharacterMover2D.Move(direction)  → HorizontalInput = direction.x
    ├── CharacterMover2D.IsRunRequested   ← Shift held
    ├── CharacterMover2D.IsJumpHeld       ← Space held
    ├── CharacterMover2D.Jump()           → IsJumpRequested = true  (Space pressed)
    └── CharacterMover2D.DropThrough()    ← S held (chains through stacked platforms)

CharacterMover2D.FixedUpdate()
    ├── IsGrounded = CheckGround()
    │       array-based OverlapBox → filters _dropThroughTarget and positional platform check
    ├── IsCeiling  = CheckCeiling() using CeilingLayerMask (Ground only, excludes Platform)
    ├── if IsCeiling && Velocity.y > 0 → Velocity.y = 0
    ├── _topFsm.EvaluateTransitions()
    ├── (_topFsm.CurrentState as ITickable).Tick(dt)
    │       └── WalkingState.Tick(dt)
    │               ├── _subFsm.EvaluateTransitions()
    │               ├── coyote timer start (if just entered Fall, not from Jump)
    │               └── (_subFsm.CurrentState as ITickable).Tick(dt)
    │                       └── sub-state modifies CharacterMover2D.Velocity
    ├── ApplyMovement(dt)
    │       ├── CollisionSlideResolver2D.CollideAndSlide(
    │       │       Velocity * dt, _contactFilter, ShouldIgnorePlatformHit)
    │       └── Rigidbody2D.MovePosition(position + resolvedDisplacement)
    └── positional _dropThroughTarget clearing
            if charBottom < platformTop - SKIN_WIDTH → _dropThroughTarget = null
```

### One-way platform state on CharacterMover2D

`ShouldIgnorePlatformHit(RaycastHit2D hit)` — predicate injected into `CollisionSlideResolver2D`. Returns `true` (ignore hit) when any condition holds:
1. `hit.collider == _dropThroughTarget` — Mechanism 1: explicit drop-through override
2. `hit.normal.y < 0.5f` — side/bottom contact; platforms only block from above
3. `charBottom < platformTop - SKIN_WIDTH` — Mechanism 2: positional check (character below or passing through)

`DropThrough()` — finds the first Platform-layer collider in `_groundCheckBuffer` and stores it in `_dropThroughTarget`. Guard: `IsGrounded` must be true; no-op on solid Ground layer.

### Shared state on CharacterMover2D (read/written by sub-states)

| Property | Writer | Reader |
|----------|--------|--------|
| `Velocity` | all sub-states | all sub-states, `ApplyMovement` |
| `HorizontalInput` | `Move()` | Walk/Run/Jump/Fall sub-states |
| `IsGrounded` | `FixedUpdate` ground check | transition conditions |
| `IsCeiling` | `FixedUpdate` ceiling check | `FixedUpdate` (zero velocity.y) |
| `IsJumpRequested` | `Jump()`, cleared by `ConsumeJumpRequest()` | transition conditions |
| `IsJumpHeld` | PlayerInputReader | `JumpSubState` (low-jump gravity) |
| `IsRunRequested` | PlayerInputReader | transition conditions |
| `CoyoteTimer` | `WalkingState.Tick()` (set), `FallSubState.Tick()` (decrement) | transition conditions |
| `JumpBufferTimer` | `FallSubState.Tick()` (set + decrement), `JumpSubState.OnEnter()` (clear) | transition conditions |
| `_dropThroughTarget` | `DropThrough()` (set), `FixedUpdate` positional check (clear) | `ShouldIgnorePlatformHit`, `CheckGround` |

---

## CollisionSlideResolver2D — Algorithm

Recursive, up to `MAX_BOUNCES = 3` iterations.

1. Cast the collider from current position along movement direction using `Rigidbody2D.Cast` with an explicit position parameter. Cast distance = `velocity.magnitude + SKIN_WIDTH`.
2. Direction filter: skip hits where `dot(direction, normal) >= 0` (moving away from surface).
3. Predicate filter: if `shouldIgnore(hit)` returns true — skip the hit (Strategy Pattern). Only runs on hits that passed step 2.
4. If no valid hit remains → return full velocity.
5. On valid hit: `safeDisplacement = direction * max(0, hit.distance - SKIN_WIDTH)`. `remainder = velocity - direction * min(hit.distance, |velocity|)`.
6. Project remainder onto surface tangent via `Vector2Extensions.ProjectOnAxis`, preserve original magnitude.
7. Corner wedge check: if projected slide direction opposes the previous recursion's surface normal → return only `safeDisplacement`.
8. Recurse with slide vector from `position + safeDisplacement`.

---

## Active Patterns

**HFSM (Hierarchical Finite State Machine)**
Same `StateMachine<TState>` class reused at two levels. Top level handles macro states (Walking, Dodge); inner level handles movement sub-states. Sub-states are unaware of the top level. Example: `WalkingState` owns `StateMachine<IState> _subFsm`.

**Strategy Pattern (hit-filtering predicate)**
`CollisionSlideResolver2D.CollideAndSlide()` accepts `Func<RaycastHit2D, bool> shouldIgnore = null`. The resolver stays generic — platform logic is injected from `CharacterMover2D.ShouldIgnorePlatformHit`. Same predicate applies at every recursion level.

**Value Object (HorizontalMoveParams)**
`readonly struct` encapsulating the horizontal acceleration formula. All five sub-states call the same `Apply(velX, input, dt)` with different parameter sets from `WalkingConfig`. Created fresh on each property access — no caching issues with Inspector changes.

**ScriptableObject-driven configuration**
`WalkingConfig` (and future `DodgeConfig`) hold all tunable parameters. Tweakable in Play Mode without recompilation. Shared between player and enemies via separate asset instances.

**Input agnosticism**
`CharacterMover2D` has zero input imports. All input arrives via public methods (`Move`, `Jump`, `Dodge`, `DropThrough`). `PlayerInputReader` is the sole file referencing `Keyboard`/`Mouse`. The same component can be driven by AI.

**Separation of collision resolution**
`CollisionSlideResolver2D` only returns a displacement vector — it never calls `MovePosition`. `ApplyMovement()` applies it. Makes the resolver reusable by any future movement component (e.g. `FlyingState`).

---

## Open Decisions / Planned Work

- **DodgeState** (Phase 5): top-level FSM state, `WalkingState → DodgeState` on dodge input. `DodgeConfig` ScriptableObject. Mid-air dodge zeroes vertical velocity; on return `ResolveSubState()` must enter Fall, not Idle.
- **FlyingState** (post-prototype): airborne movement without gravity, uses `direction.y` from `Move()`. Reuses `CollisionSlideResolver2D`.
- **Slope handling**: `OverlapBox` ground check doesn't reliably detect angled surfaces. Running downhill produces a staircase effect. No fix scheduled.
- **Input actions asset**: `Keyboard.current` is read directly (no `.inputactions`). Rebinding and gamepad support will require migrating to `InputActionAsset` in a later phase.
- **Debug FSM display**: `OnGUI` overlay showing current top-level state + sub-state name planned for Phase 7.

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

If a file could be used in Runtime AND Editor → it goes in `Shared/`, not `Runtime/`.
Example: `Vector2Extensions.cs`.

---

## Class Map

- `IState` — `Shared/FSM/IState.cs` — lifecycle contract: `OnEnter()`, `OnExit()`. No deps.
- `ITickable` — `Shared/FSM/ITickable.cs` — per-frame update contract: `Tick(float deltaTime)`. Standalone, does NOT inherit `IState`. No deps.
- `StateMachine<TState>` — `Shared/FSM/StateMachine.cs` — generic FSM. Registers transitions, evaluates them in order, calls `OnExit`/`OnEnter`. No Tick. Depends on: `IState`.
- `Vector2Extensions` — `Shared/Vector2Extensions.cs` — extension `ProjectOnAxis(normal)`: projects a vector onto the surface tangent. Depends on: `UnityEngine`.
- `CharacterMover2D` — `Runtime/Movement/CharacterMover2D.cs` — top-level movement MonoBehaviour. Owns top FSM, ground/ceiling checks, applies movement via resolver, manages one-way platform state, exposes input-agnostic public API. Depends on: `WalkingConfig`, `DodgeConfig`, `CollisionSlideResolver2D`, `StateMachine<IState>`, `WalkingState`, `DodgeState`.
- `WalkingConfig` — `Runtime/Movement/WalkingConfig.cs` — ScriptableObject with all movement parameters. Exposes computed properties (`Gravity`, `JumpVelocity`, `FallMultiplier`) and `HorizontalMoveParams` presets. Depends on: `HorizontalMoveParams`.
- `DodgeConfig` — `Runtime/Movement/DodgeConfig.cs` — ScriptableObject with `DodgeDistance`, `DodgeSpeed`. Computed read-only `DodgeTime` for external systems (animation, UI). No code deps.
- `HorizontalMoveParams` — `Runtime/Movement/HorizontalMoveParams.cs` — `readonly struct`. Encapsulates `MaxSpeed`, `Acceleration`, `Deceleration` and the shared `Apply(velX, input, dt)` formula. Used by all five sub-states. Depends on: `Mathf`.
- `CollisionSlideResolver2D` — `Runtime/Movement/CollisionSlideResolver2D.cs` — recursive collide-and-slide. Accepts optional `shouldIgnore` predicate (Strategy Pattern) applied after the dot-product filter. Returns safe displacement. `SKIN_WIDTH = 0.03f`, `MAX_BOUNCES = 3`. Depends on: `Vector2Extensions`, `Rigidbody2D`.
- `PlayerInputReader` — `Runtime/Movement/PlayerInputReader.cs` — reads `Keyboard.current` (New Input System, no `.inputactions` asset). Calls `CharacterMover2D` public API. The only file referencing `Keyboard`/`Mouse`. Depends on: `UnityEngine.InputSystem`, `CharacterMover2D`.
- `WalkingState` — `Runtime/Movement/States/WalkingState.cs` — top-level FSM state. Owns a second `StateMachine<IState>` for sub-states. `OnEnter` calls `ResolveSubState()`. Manages coyote timer start on edge walk-off. Depends on: `CharacterMover2D`, `WalkingConfig`, `StateMachine<IState>`.
- `DodgeState` — `Runtime/Movement/States/DodgeState.cs` — top-level FSM state. Horizontal dodge with distance-based tracking. `OnEnter` zeroes vertical velocity, captures direction, consumes the dodge request. `IsFinished` signals the FSM to return to `WalkingState`. Depends on: `CharacterMover2D`, `DodgeConfig`.
- `IdleSubState` — `Runtime/Movement/States/IdleSubState.cs` — no input, grounded. Decelerates to zero via `GroundWalkParams.Apply(v.x, 0f, dt)`. Depends on: `CharacterMover2D`, `WalkingConfig`.
- `WalkSubState` — `Runtime/Movement/States/WalkSubState.cs` — horizontal input, no Shift. Accelerates to `WalkSpeed` via `GroundWalkParams.Apply()`. Depends on: `CharacterMover2D`, `WalkingConfig`.
- `RunSubState` — `Runtime/Movement/States/RunSubState.cs` — horizontal input + Shift. Accelerates to `RunSpeed` via `GroundRunParams.Apply()`. Depends on: `CharacterMover2D`, `WalkingConfig`.
- `JumpSubState` — `Runtime/Movement/States/JumpSubState.cs` — `OnEnter`: sets `velocity.y = JumpVelocity`, consumes jump request, clears timers. `Tick`: gravity, low-jump multiplier, air control via `AirParams.Apply()`. Depends on: `CharacterMover2D`, `WalkingConfig`.
- `FallSubState` — `Runtime/Movement/States/FallSubState.cs` — `Tick`: fall gravity × `FallMultiplier`, clamp to `MaxFallSpeed`, air control, jump buffer capture, timer decrement. Depends on: `CharacterMover2D`, `WalkingConfig`.

---

## FSM Hierarchy

Two-level HFSM implemented by reusing the same `StateMachine<TState>` class on both levels.

```
CharacterMover2D
└── StateMachine<IState>  _topFsm
        ├── WalkingState
        │       └── StateMachine<IState>  _subFsm
        │               ├── IdleSubState
        │               ├── WalkSubState
        │               ├── RunSubState
        │               ├── JumpSubState
        │               └── FallSubState
        └── DodgeState
```

Why two levels: `WalkingState→DodgeState` is one transition at the top level, covering all five sub-states. Without HFSM each sub-state would need its own `→DodgeState` transition.

### Top-level transitions (registered in CharacterMover2D.Awake)

- WalkingState → DodgeState: `IsDodgeRequested`
- DodgeState → WalkingState: `_dodgeState.IsFinished`

`CharacterMover2D` holds typed refs (`WalkingState _walkingState`, `DodgeState _dodgeState`) so transition lambdas can read state-specific properties. The FSM stores them as `IState` internally.

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

Called in `WalkingState.OnEnter()`. Picks the starting sub-state based on current conditions — not grounded → Fall; grounded + input + Shift → Run; grounded + input → Walk; otherwise → Idle. Critical after mid-air dodge: ensures return to Fall, not Idle.

### Coyote time

Timer set in `WalkingState.Tick()` when entering Fall from any state except Jump (prevents double-jump via coyote). Decremented in `FallSubState.Tick()`.

### Jump buffer

Captured in `FallSubState.Tick()` when `IsJumpRequested` is true while airborne. `Fall→Jump` buffer transition fires on landing before `Fall→Idle/Walk`.

---

## Data Flow

### Per-frame (Update → FixedUpdate)

```
PlayerInputReader.Update()
    ├── CharacterMover2D.Move(direction)    → HorizontalInput = direction.x
    ├── CharacterMover2D.IsRunRequested     ← Shift held
    ├── CharacterMover2D.IsJumpHeld         ← Space held
    ├── CharacterMover2D.Jump()             → IsJumpRequested = true  (Space pressed)
    ├── CharacterMover2D.DropThrough()      ← S held (chains through stacked platforms)
    └── CharacterMover2D.Dodge(direction)   → IsDodgeRequested = true  (Shift tapped)

CharacterMover2D.FixedUpdate()
    ├── IsGrounded = CheckGround()
    │       array-based OverlapBox → filters _dropThroughTarget and positional platform check
    ├── IsCeiling  = CheckCeiling() using CeilingLayerMask (Ground only, excludes Platform)
    ├── if IsCeiling && Velocity.y > 0 → Velocity.y = 0
    ├── _topFsm.EvaluateTransitions()
    │       WalkingState → DodgeState when IsDodgeRequested
    │       DodgeState → WalkingState when IsFinished
    ├── (_topFsm.CurrentState as ITickable).Tick(dt)
    │       WalkingState.Tick(dt):
    │           ├── _subFsm.EvaluateTransitions()
    │           ├── coyote timer start (if just entered Fall, not from Jump)
    │           └── (_subFsm.CurrentState as ITickable).Tick(dt)
    │                   └── sub-state modifies CharacterMover2D.Velocity
    │       DodgeState.Tick(dt):
    │           └── sets Velocity.x = DodgeSpeed * _direction, decrements _remainingDistance
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

`DropThrough()` — finds the first Platform-layer collider in `_groundCheckBuffer`, stores in `_dropThroughTarget`. Guard: `IsGrounded` must be true; no-op on solid Ground layer.

### Shared state on CharacterMover2D (read/written by sub-states)

- `Velocity` — written by all sub-states and DodgeState; read by `ApplyMovement`
- `HorizontalInput` — written by `Move()`; read by Walk/Run/Jump/Fall sub-states
- `IsGrounded` — written by `FixedUpdate` ground check; read by transition conditions
- `IsCeiling` — written by `FixedUpdate` ceiling check; read by `FixedUpdate` (zero velocity.y)
- `IsJumpRequested` — set by `Jump()`, cleared by `ConsumeJumpRequest()`; read by transitions
- `IsJumpHeld` — set by PlayerInputReader; read by `JumpSubState` (low-jump gravity)
- `IsRunRequested` — set by PlayerInputReader; read by transition conditions
- `IsDodgeRequested` — set by `Dodge()`, cleared by `ConsumeDodgeRequest()` in `DodgeState.OnEnter()`; read by top-level FSM transition
- `DodgeDirection` — set by `Dodge()`; read by `DodgeState.OnEnter()`
- `CoyoteTimer` — set in `WalkingState.Tick()`, decremented in `FallSubState.Tick()`; read by transitions
- `JumpBufferTimer` — set+decremented in `FallSubState.Tick()`, cleared in `JumpSubState.OnEnter()`; read by transitions
- `_dropThroughTarget` — set by `DropThrough()`, cleared by positional check in `FixedUpdate`; read by `ShouldIgnorePlatformHit`, `CheckGround`

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
Same `StateMachine<TState>` reused at two levels. Top: WalkingState ↔ DodgeState. Inner: five movement sub-states. Sub-states are unaware of the top level. Example: `WalkingState` owns `StateMachine<IState> _subFsm`.

**Strategy Pattern (hit-filtering predicate)**
`CollisionSlideResolver2D.CollideAndSlide()` accepts `Func<RaycastHit2D, bool> shouldIgnore = null`. Resolver stays generic; platform logic is injected from `CharacterMover2D.ShouldIgnorePlatformHit`. Same predicate applies at every recursion level.

**Request/Consume Pattern**
Both jump and dodge use the same pattern: the input reader calls `Jump()`/`Dodge()` setting a bool flag; the FSM transition fires; the entering state calls `ConsumeJumpRequest()`/`ConsumeDodgeRequest()` to clear the flag. Guarantees one activation per press, regardless of frame timing.

**Value Object (HorizontalMoveParams)**
`readonly struct` encapsulating the horizontal acceleration formula. All five sub-states call the same `Apply(velX, input, dt)` with different parameter sets from `WalkingConfig`. Created fresh on each property access — no caching issues with Inspector changes.

**ScriptableObject-driven configuration**
`WalkingConfig` and `DodgeConfig` hold all tunable parameters. Tweakable in Play Mode without recompilation. Shareable between player and enemies via separate asset instances.

**Input agnosticism**
`CharacterMover2D` has zero input imports. All input arrives via public methods (`Move`, `Jump`, `Dodge`, `DropThrough`). `PlayerInputReader` is the sole file referencing `Keyboard`/`Mouse`. The same component can drive AI agents.

**Separation of collision resolution**
`CollisionSlideResolver2D` only returns a displacement vector — it never calls `MovePosition`. `ApplyMovement()` applies it. Makes the resolver reusable by any future movement component (e.g. `FlyingState`).

---

## Open Decisions / Planned Work

- **FlyingState** (post-prototype): airborne movement without gravity, uses `direction.y` from `Move()`. Reuses `CollisionSlideResolver2D`.
- **Slope handling**: `OverlapBox` ground check doesn't reliably detect angled surfaces. Running downhill produces a staircase effect. No fix scheduled.
- **Input actions asset**: `Keyboard.current` is read directly (no `.inputactions`). Rebinding and gamepad support will require migrating to `InputActionAsset` in a later phase.
- **Dodge key binding**: Shift is currently used for both Run (hold) and Dodge (tap). Placeholder for prototype — final binding TBD during Phase 7 or when the input system is formalized.
- **Debug FSM display**: `OnGUI` overlay showing current top-level state + sub-state name planned for Phase 7.
- **Drop-through safety timer**: `_dropThroughTarget` cleared positionally. If the character gets stuck inside a platform (teleport, destroyed collider), the flag may never clear. Mitigation: add a 0.3–0.5s fallback timeout in `WalkingConfig`. See `CharacterMover2D_Notes.md`.

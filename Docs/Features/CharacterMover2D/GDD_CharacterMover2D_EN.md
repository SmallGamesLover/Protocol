# 2D Character Movement Component — Design Document
> Version: 4.2.0 | Project: Protocol

---

## Foundation

**Rigidbody Type:** Kinematic Rigidbody2D. The character moves via `MovePosition`; the physics engine handles collisions but does not control movement on its own. No depenetration, no tunneling, fully predictable behavior.

**Collision Detection Mode:** Discrete. Sufficient due to the capped maximum fall speed.

**Ground Check:** Ground contact is detected via `Physics2D.OverlapBox` just below the character's feet. Uses `GroundLayerMask` which includes both Ground and Platform layers. Results are collected into an array and filtered individually: one-way platforms count as ground only when the character's bottom edge is at or above the platform's top edge (minus `SKIN_WIDTH` tolerance). Platforms matching the active `_dropThroughTarget` are skipped entirely. The filtered result is used to switch sub-states (Jump/Fall/Idle/Walk) and for coyote time.

**Ceiling Check:** Ceiling contact is detected via `Physics2D.OverlapBox` just above the character's head. Uses a dedicated `CeilingLayerMask` that includes only the Ground layer (excludes Platform). This means one-way platforms are invisible to the ceiling check at the physics query level — no programmatic filtering needed. When the character hits a ceiling while moving upward (`velocity.y > 0`), vertical velocity is zeroed immediately. This causes gravity to pull `velocity.y` negative on the next frame, triggering the `Jump→Fall` transition. The check lives in `CharacterMover2D.FixedUpdate` (not in a specific state) so it applies universally — jumps, future dodge-up, or any other upward movement.

**One-way Platforms:** Unity's `PlatformEffector2D` only works out of the box with Dynamic Rigidbody. With Kinematic, manual handling is required via a hit-filtering predicate injected into `CollisionSlideResolver2D`. See the dedicated section below.

---

## One-way Platforms

### Core Principle

A one-way platform blocks the character only when falling onto it from above. In all other cases — jumping from below, horizontal movement, or actively dropping through — the platform is transparent. This is achieved through two complementary mechanisms working together in a single predicate.

### Hit Filtering — Two Mechanisms

The predicate `ShouldIgnorePlatformHit(RaycastHit2D hit)` is passed to `CollisionSlideResolver2D` and evaluated for every hit during collide-and-slide. A platform hit is ignored when any of the following is true:

**Mechanism 1 — Explicit drop-through override:** The hit collider matches `_dropThroughTarget`. This is set when the player holds "down" while standing on a platform, and cleared once the character's bottom edge passes below the platform's top edge. Exists solely to bridge the gap between holding "down" (feet still above platform) and the moment positional check takes over.

**Mechanism 2 — Positional check (always active):** The character's bottom edge is below the platform's top edge minus `SKIN_WIDTH`. Covers jumping through from below and continued falling after drop-through. This is the primary mechanism — it handles all cases except the initial frame of drop-through.

**Unconditional side/bottom ignore:** Hits with `normal.y < 0.5` on platform-layer colliders are always ignored. The 0.5 threshold (~60° from horizontal) ensures only top-face contacts are treated as blocking. For rectangular `BoxCollider2D` this is a safety margin against floating-point imprecision.

### Drop-through Mechanic

When the player holds "down" while grounded on a one-way platform:

1. `DropThrough()` identifies the platform collider via the ground check and stores it in `_dropThroughTarget`
2. Mechanism 1 in the predicate immediately ignores this specific collider — the character begins to fall
3. As the character descends, its bottom edge crosses below the platform's top edge — Mechanism 2 takes over
4. `_dropThroughTarget` is cleared (positional check in `FixedUpdate`)
5. The character continues falling through naturally — Mechanism 2 keeps ignoring the platform as long as the bottom edge is below the top edge

Only the specific platform the character was standing on is affected. All other platforms remain solid.

Holding "down" continuously chains drop-through across stacked platforms: on each frame where the key is held and `IsGrounded` is true on a Platform-layer collider, `DropThrough()` fires again — the character falls through the next platform immediately on landing.

### Positional Tolerance

All positional comparisons use `CollisionSlideResolver2D.SKIN_WIDTH` as the tolerance value. A separate constant is unnecessary because the tolerance operates in the same physical space as the collide-and-slide skin gap — when the character stands on a platform via the resolver, its bottom edge is approximately `platformTop + SKIN_WIDTH`.

---

## FSM Hierarchy (HFSM)

The movement component is built on a two-level hierarchical finite state machine. The HFSM is implemented by **reusing the same `StateMachine<TState>` class** on both levels — no dedicated HFSM class is needed.

### Why HFSM Instead of a Flat FSM

Five movement sub-states (Idle, Walk, Run, Jump, Fall) share a single common outward transition — dodge. In a flat FSM, dodge would need to be registered from each one: Idle→Dodge, Walk→Dodge, Run→Dodge, Jump→Dodge, Fall→Dodge. Every new sub-state adds yet another transition. HFSM solves this: dodge is defined once at the top level (WalkingState→DodgeState), and all sub-states inherit it automatically because the top-level FSM only sees "WalkingState", not its internals.

### Top Level
Switches between high-level states:

| State | Description | Scope |
|---|---|---|
| `WalkingState` | Ground and airborne character physics. Owns an internal sub-state FSM | Prototype |
| `DodgeState` | Horizontal dodge with distance-based tracking | Prototype |
| `FlyingState` | Airborne physics without gravity | Post-prototype |

### Top-Level Transitions

| From | To | Condition | Registration |
|---|---|---|---|
| `WalkingState` | `DodgeState` | `IsDodgeRequested` | `CharacterMover2D` setup |
| `DodgeState` | `WalkingState` | `DodgeState.IsFinished` | `CharacterMover2D` setup |

Transitions are registered in `CharacterMover2D` during initialization. `CharacterMover2D` holds typed references to both `WalkingState` and `DodgeState` (not just `IState`) so it can read state-specific properties like `DodgeState.IsFinished` in transition conditions. The `StateMachine<IState>` stores them as `IState` internally — the typed references are only used by the owner for transition registration.

### Dodge Request/Consume Pattern

Dodge uses the same request/consume pattern as jump:

1. `PlayerInputReader` calls `_mover.Dodge(direction)` — sets `IsDodgeRequested = true` and stores `DodgeDirection`
2. `_topFsm.EvaluateTransitions()` checks `IsDodgeRequested` — transitions to `DodgeState`
3. `DodgeState.OnEnter()` calls `_mover.ConsumeDodgeRequest()` — clears the flag

This ensures the request is consumed exactly once and only by the state that acts on it.

### Bottom Level — WalkingState's Internal FSM

WalkingState owns an instance of `StateMachine<IState>` and manages its sub-states independently. All transitions between sub-states are registered in the WalkingState constructor via `AddTransition()` — the entire transition map is visible in one place. Sub-states are unaware of the top level's existence.

| Sub-state | Entry Condition | Weapon Accuracy |
|---|---|---|
| `Idle` | No horizontal input, grounded | Maximum |
| `Walk` | Horizontal input without Shift, grounded | Medium |
| `Run` | Horizontal input + Shift, grounded | Minimum |
| `Jump` | After jump, `velocity.y ≥ 0` | — |
| `Fall` | `velocity.y < 0` or walked off an edge | — |

### Entering WalkingState and ResolveSubState

On entering `WalkingState` (first launch or return after DodgeState), `ResolveSubState()` is called — a method that determines the initial sub-state based on current conditions: ground contact, horizontal input presence, vertical velocity. **WalkingState does not default to Idle** — if a dodge ended mid-air, WalkingState enters Fall immediately.

### Coyote Time Transitions

Coyote time is available from `FallSubState` — a dedicated `Fall→Jump` transition checks `IsJumpRequested && CoyoteTimer > 0`. This is separate from the buffer-based `Fall→Jump` transition (which checks `IsGrounded && JumpBufferTimer > 0`). Both transitions are registered before `Fall→Idle/Walk/Run` to ensure jump takes priority over landing.

---

## HorizontalMoveParams (Value Object)

A `readonly struct` encapsulating horizontal acceleration logic. Eliminates duplication across all five movement sub-states — Idle, Walk, Run, Jump, and Fall all use the same formula with different parameters. `IdleSubState` calls `Apply` with `input = 0`, which produces `target = 0` and decelerates toward zero using `Deceleration` — identical to its previous inline logic.

```
target = maxSpeed * sign(input)
rate   = opposing ? deceleration : acceleration
vel.x  = MoveTowards(vel.x, target, rate * deltaTime)
```

### Fields

| Field | Type | Description |
|---|---|---|
| `MaxSpeed` | float | Target horizontal speed |
| `Acceleration` | float | Rate of reaching target speed |
| `Deceleration` | float | Rate of slowing when input opposes velocity |

### Method

`float Apply(float currentVelX, float input, float deltaTime)` — pure function, no side effects. Computes and returns the new horizontal velocity.

### Parameter Sets (provided by WalkingConfig)

| Property | MaxSpeed | Acceleration | Deceleration | Used by |
|---|---|---|---|---|
| `GroundWalkParams` | WalkSpeed | Acceleration | Deceleration | IdleSubState, WalkSubState |
| `GroundRunParams` | RunSpeed | Acceleration | Deceleration | RunSubState |
| `AirParams` | RunSpeed | AirAcceleration | AirDeceleration | JumpSubState, FallSubState |

Each property creates a fresh struct from current WalkingConfig values every time it is accessed — Inspector changes apply immediately without caching issues.

### Air Control

Airborne sub-states (Jump, Fall) apply horizontal acceleration via `AirParams`. The target speed is `RunSpeed` so that a running jump preserves full horizontal speed. `AirAcceleration` and `AirDeceleration` are typically lower than ground values, giving a sense of inertia — the character is controllable in the air but does not turn instantly.

---

## WalkingConfig (ScriptableObject)

Configuration for `WalkingState`. Used by both the player and enemies.

### Horizontal Movement

| Parameter | Type | Description |
|---|---|---|
| `walkSpeed` | float | Maximum walk speed |
| `runSpeed` | float | Maximum run speed |
| `acceleration` | float | Rate of reaching target speed |
| `deceleration` | float | Rate of slowing down (higher than acceleration for a sense of control) |
| `airAcceleration` | float | Horizontal acceleration in the air. Typically 30–60% of ground acceleration |
| `airDeceleration` | float | Horizontal deceleration in the air. Low values preserve momentum |

### Jump

| Parameter | Type | Description |
|---|---|---|
| `jumpHeight` | float | Desired jump height in units |
| `timeToApex` | float | Time to reach maximum height in seconds |
| `timeToDescent` | float | Time to fall from maximum height to the ground in seconds |
| `lowJumpMultiplier` | float | Gravity multiplier when the jump button is released before the apex |
| `maxFallSpeed` | float | Maximum fall speed cap |

### Computed Values

Computed from config parameters, not set manually:

```
gravity        = -2 * jumpHeight / timeToApex²
jumpVelocity   =  2 * jumpHeight / timeToApex
fallMultiplier = (timeToApex / timeToDescent)²
```

### Responsiveness

| Parameter | Type | Description |
|---|---|---|
| `coyoteTime` | float | Time after walking off an edge during which a jump is still available. Typical values: 0.08–0.15 sec |
| `jumpBufferTime` | float | Time during which a jump input is remembered before landing. Typical values: 0.1–0.15 sec |

> **Important:** Coyote time only activates on transition from a grounded state (Idle/Walk/Run) to Fall (walked off an edge). After a jump, coyote time is not active — otherwise the player gets an unintended "double jump": jumped → started falling → coyote time still active → jumped again.

### HorizontalMoveParams Properties

| Property | Composition | Used by |
|---|---|---|
| `GroundWalkParams` | WalkSpeed, Acceleration, Deceleration | IdleSubState, WalkSubState |
| `GroundRunParams` | RunSpeed, Acceleration, Deceleration | RunSubState |
| `AirParams` | RunSpeed, AirAcceleration, AirDeceleration | JumpSubState, FallSubState |

---

## DodgeConfig (ScriptableObject)

Configuration for `DodgeState`.

### Serialized Fields

| Parameter | Type | Description |
|---|---|---|
| `DodgeDistance` | float | Dodge distance in units. Designer controls exactly how far the dodge moves |
| `DodgeSpeed` | float | Dodge speed in units per second. Designer controls how fast the dodge feels |

### Computed Value

```
DodgeTime = DodgeDistance / DodgeSpeed
```

`DodgeTime` is a read-only computed property. It is not used internally by `DodgeState` for completion — the state tracks remaining distance, not elapsed time. `DodgeTime` exists for external systems that need the dodge duration (animation length, i-frames window, UI cooldown display).

### Why Distance + Speed Instead of Distance + Time

Two independent serialized knobs give direct control over the two most perceptible qualities of a dodge: *how far* and *how fast*. A designer can make a long slow roll or a short sharp dash without computing derived values. The time is a consequence, not a design input.

### DodgeState Behavior

**Distance-based tracking with last-frame clamping:**

`DodgeState` tracks `_remainingDistance` instead of a timer. On each tick, it computes `maxStep = DodgeSpeed * deltaTime`. If `maxStep >= _remainingDistance`, the velocity is clamped so the character covers exactly the remaining distance in this frame: `velocity.x = (_remainingDistance / deltaTime) * direction`. Otherwise, normal dodge speed applies. `_remainingDistance` is decremented by `maxStep` (not by actual displacement after collision resolution).

This guarantees the dodge covers exactly `DodgeDistance` in open space, regardless of `fixedDeltaTime` alignment. The last frame's velocity is adjusted to land precisely on target.

**Wall collision behavior:** `_remainingDistance` is decremented based on *intended* step (`maxStep`), not actual displacement after `CollideAndSlide`. This means a dodge into a wall "spends" its distance against the wall and ends on time. The alternative — tracking actual displacement — would cause the character to hover against the wall for the full dodge duration, which is worse for game feel.

**Entry behavior:**
- Vertical velocity is reset to zero — an airborne dodge acts as a "hover" with horizontal movement
- Dodge direction is captured from `DodgeDirection` on `CharacterMover2D`
- `_remainingDistance` is set to `DodgeDistance`
- `ConsumeDodgeRequest()` clears the request flag

**Completion:** `IsFinished` returns `true` when `_remainingDistance <= 0`. The top-level FSM transition `DodgeState→WalkingState` checks this property.

**General rules:**
- Horizontal movement is strictly in the dodge direction — player input is ignored during dodge
- Blocks shooting and reloading for the entire dodge duration
- Available from any `WalkingState` sub-state including `Jump` and `Fall` — the transition is defined at the top HFSM level
- On completion, returns control to `WalkingState`, which determines the correct sub-state via `ResolveSubState()`

---

## Class Architecture

```
CharacterMover2D (MonoBehaviour, top FSM owner)
    │
    ├── StateMachine<IState> _topFsm
    │       ├── WalkingState  (implements IState, ITickable)
    │       │       └── StateMachine<IState> _subFsm  ← same class
    │       │               ├── IdleSubState   (implements IState, ITickable)
    │       │               ├── WalkSubState   (implements IState, ITickable)
    │       │               ├── RunSubState    (implements IState, ITickable)
    │       │               ├── JumpSubState   (implements IState, ITickable)
    │       │               └── FallSubState   (implements IState, ITickable)
    │       ├── DodgeState    (implements IState, ITickable)
    │       └── FlyingState   (implements IState, ITickable)
    │
    ├── WalkingConfig (ScriptableObject)
    ├── DodgeConfig (ScriptableObject)
    ├── HorizontalMoveParams (readonly struct, value object)
    └── CollisionSlideResolver2D (utility class)

MovementDebugOverlay (MonoBehaviour, same GameObject)
    reads from CharacterMover2D via public properties
    editor-only logic, empty shell in builds
```

### Interfaces

| Interface | Contract | Description |
|---|---|---|
| `IState` | `OnEnter()`, `OnExit()` | State lifecycle. Implemented by all states |
| `ITickable` | `Tick(float deltaTime)` | Per-frame update. Does not inherit `IState` — an independent contract, applicable beyond FSM |

### StateMachine\<TState\>
Generic FSM responsible only for transitions. Unaware of `Tick` — ticking is the owner's responsibility:

```csharp
// CharacterMover2D (FixedUpdate):
_topFsm.EvaluateTransitions();
if (_topFsm.CurrentState is ITickable tickable)
    tickable.Tick(deltaTime);
```

### CharacterMover2D
An independent component, unaware of the input source. Whether it is driven by the player's FSM or enemy AI is none of its concern.

**Public Commands:**

| Method | Description |
|---|---|
| `Move(Vector2 direction)` | Horizontal movement. Vector2 allows using the component in `FlyingState`. WalkingState uses only `direction.x` and ignores `direction.y` |
| `Jump()` | Jump. Sets `IsJumpRequested = true` |
| `Dodge(Vector2 direction)` | Dodge in the specified direction. Sets `IsDodgeRequested = true` and stores `DodgeDirection` |
| `DropThrough()` | Drop through a one-way platform. Only works when grounded on a Platform-layer collider |
| `ConsumeDodgeRequest()` | Clears `IsDodgeRequested`. Called by `DodgeState.OnEnter()` |

**Environment Checks (FixedUpdate):**

| Property | Source | Description |
|---|---|---|
| `IsGrounded` | `OverlapBox` below feet (array-based, filtered) | Used by FSM for state transitions. Platform-layer colliders are validated by positional check and `_dropThroughTarget` filter |
| `IsCeiling` | `OverlapBox` above head (`CeilingLayerMask`, Ground only) | When true and `velocity.y > 0`, vertical velocity is zeroed. One-way platforms excluded at query level |

**Dodge State:**

| Field/Property | Type | Description |
|---|---|---|
| `IsDodgeRequested` | `bool` | Set by `Dodge()`, cleared by `ConsumeDodgeRequest()`. Checked by top-level FSM transition |
| `DodgeDirection` | `Vector2` | Direction captured from `Dodge()` call. Read by `DodgeState.OnEnter()` |

**One-way Platform State:**

| Field | Type | Description |
|---|---|---|
| `_dropThroughTarget` | `Collider2D` | The specific platform collider being dropped through. Set by `DropThrough()`, cleared when the character's bottom edge passes below the platform's top edge |
| `_platformLayer` | `int` | Platform layer index, cached in `Awake()` via `LayerMask.NameToLayer("Platform")`. Used in the hit-filtering predicate for int-to-int comparison with `gameObject.layer` |

**Debug Properties (editor-only):**

Properties prefixed with `Debug` to signal they are not for runtime game logic. Getter bodies wrapped in `#if UNITY_EDITOR`; `#else` returns inert values (`""`, `false`). The property declarations remain in builds to avoid missing-member compilation errors from accidental references.

| Property | Type | Description |
|---|---|---|
| `DebugStateName` | `string` | Composite FSM state name. `"Walking > {sub}"` when in WalkingState, otherwise top-level state type name |
| `DebugIsDropThroughActive` | `bool` | `_dropThroughTarget != null`. Exposes the flag without revealing the private collider reference |

### WalkingConfig
ScriptableObject with parameters for `WalkingState`. Tweakable in the Inspector during Play Mode without recompilation. Used by both the player and enemies — different instances with different values.

### HorizontalMoveParams
A `readonly struct` that encapsulates the horizontal acceleration formula. Created fresh each frame from `WalkingConfig` properties. See the dedicated section above.

### DodgeConfig
ScriptableObject with parameters for `DodgeState`. Two serialized fields (`DodgeDistance`, `DodgeSpeed`) give independent control over distance and speed. `DodgeTime` is a computed read-only property for external systems. Tweakable in the Inspector during Play Mode without recompilation.

### CollisionSlideResolver2D
A utility class responsible for one thing: resolving movement collisions via a recursive collide-and-slide algorithm. Does not move the rigidbody — returns a safe displacement vector for the caller to apply via `MovePosition`. Unaware of who calls it — used by both `WalkingState` and `FlyingState`.

**How it works:**
1. Casts the collider from its current position along the movement direction using `Rigidbody2D.Cast` with an explicit position parameter (the rigidbody itself never moves between recursion steps). Cast distance extends beyond velocity by `SKIN_WIDTH` to detect surfaces within the skin gap ahead.
2. **Direction-based hit filtering:** ignores hits where `dot(direction, normal) >= 0` — the character is moving away from or along the surface, so there is nothing to resolve. This is a cheap check (dot product) that eliminates most irrelevant hits before any external logic runs.
3. **External hit filtering (Strategy Pattern):** if a `Func<RaycastHit2D, bool> shouldIgnore` predicate was provided, each remaining hit is tested against it — ignored hits are skipped entirely. This is how one-way platform logic is injected without the resolver knowing about platforms. Runs after the direction check so the predicate only evaluates hits that would otherwise be resolved.
4. If no valid hit remains after both filters, the full velocity is returned as displacement.
5. On a valid hit: computes safe displacement (`Mathf.Max(0, hit.distance - SKIN_WIDTH)`) and remaining velocity (`velocity - direction * Min(hit.distance, |velocity|)`). Safe displacement is clamped to zero to prevent backward movement when already closer than SKIN_WIDTH. Remainder is clamped to prevent inflation when the hit is beyond velocity range (found via SKIN_WIDTH cast extension).
6. Projects the remaining velocity onto the surface axis via `Vector2Extensions.ProjectOnAxis`, preserving the projected magnitude for consistent slide speed on slopes.
7. Corner wedge detection: if the projected slide direction opposes the previous recursion's surface normal, the character is wedged — returns only the safe portion with no further sliding.
8. Recurses with the slide vector from the new contact point.
9. Base cases: no valid hit (return full velocity), or `MAX_BOUNCES` exceeded (return zero).

**Constants:** `MAX_BOUNCES = 3`, `SKIN_WIDTH = 0.03f` (public const).

**Usage:**
```csharp
// CharacterMover2D.ApplyMovement():
Vector2 displacement = _collisionResolver.CollideAndSlide(
    Velocity * deltaTime, _contactFilter, ShouldIgnorePlatformHit);
_rigidbody.MovePosition(_rigidbody.position + displacement);
```

### WalkingState — Debug Property

`WalkingState` exposes a single debug property for the overlay:

| Property | Type | Description |
|---|---|---|
| `DebugSubStateName` | `string` | `_subFsm.CurrentState?.GetType().Name ?? "None"`. Editor-only getter body, returns `""` in builds |

The sub-FSM itself remains private. Only the current sub-state's type name is readable from outside — enough for debug display, no control surface exposed.

### MovementDebugOverlay

A MonoBehaviour attached to the same GameObject as `CharacterMover2D`. Responsible for all runtime debug visualization — OnGUI text dashboard and velocity Gizmo. Reads data through public properties on the mover; the mover is unaware of the overlay's existence.

**Separation from CharacterMover2D:** ground/ceiling check Gizmos remain in `CharacterMover2D.OnDrawGizmos()` because they visualize *configuration* (box position, size, layer mask). The velocity Gizmo and OnGUI dashboard visualize *runtime behavior* — a different responsibility that belongs in a dedicated debug component.

**Conditional compilation:** the class shell exists in all builds. Method bodies and the `using UnityEngine.InputSystem` import are wrapped in `#if UNITY_EDITOR`. Serialized fields remain unwrapped. In builds, the component is an empty MonoBehaviour — no logic, no overhead, no "Missing script" errors.

**Visibility controls:**

| Control | Scope |
|---|---|
| `ShowOverlay` (serialized bool) | OnGUI text dashboard |
| `ShowVelocityGizmo` (serialized bool) | Velocity line Gizmo in Scene View |
| F1 key (Play Mode) | Master toggle — flips both bools simultaneously |
| Component enabled/disabled | Disables everything |

`OnDrawGizmos()` explicitly checks `!enabled` because Unity calls this method even on disabled MonoBehaviours — unlike `OnGUI()` which Unity skips automatically.

**Dashboard content (OnGUI):** FSM state name, velocity vector and magnitude, grounded/ceiling bools, coyote and jump buffer timers (as `current / max`), all request flags, horizontal and vertical input values. Positioned top-left with a semi-transparent black background.

**Velocity Gizmo (OnDrawGizmos):** yellow line from `transform.position` in the direction of `Velocity`, scaled by a serialized `VelocityGizmoScale` multiplier for visual clarity.

---

## Public API Behavioral Contract

`CharacterMover2D` is designed to be driven by any external caller — player input, AI agent, test harness, or replay system. The public API makes no assumptions about call frequency, ordering, or caller identity. This section documents the expected behavior under edge-case calling patterns that differ from typical human input.

### Request/Consume Guarantees

Both `Jump()` and `Dodge()` use the Request/Consume pattern: calling the method sets a boolean flag; the FSM transition consumes it via `ConsumeJumpRequest()` / `ConsumeDodgeRequest()` in the entering state's `OnEnter()`. Key behavioral properties:

- **Idempotent within a frame.** Calling `Jump()` multiple times before the next `FixedUpdate` is equivalent to calling it once — the flag is already `true`.
- **Persistent until consumed.** If the FSM cannot act on a request (e.g., `Jump()` called mid-air with no coyote/buffer), `IsJumpRequested` remains `true` until the next `EvaluateTransitions()` pass where a matching transition fires — or until the jump buffer timer expires and the flag would need external clearing. Note: `IsJumpRequested` is cleared by `ConsumeJumpRequest()` in `JumpSubState.OnEnter()`, but is NOT cleared on expiry — a stale flag may survive and cause a delayed jump on landing. This is a known edge case relevant to AI callers that set `IsJumpRequested` every frame.
- **Dodge during dodge.** If `Dodge()` is called while `DodgeState` is active, `IsDodgeRequested` is set to `true` but the transition `WalkingState → DodgeState` cannot fire (current state is `DodgeState`, not `WalkingState`). After the current dodge completes, the flag triggers an immediate second dodge with no gap. Callers that want cooldown between dodges must enforce it externally.

### Move() Input Handling

- `Move(Vector2 direction)` stores `direction.x` as `HorizontalInput`. `WalkingState` sub-states use only this value. `direction.y` is stored but ignored by all current states — reserved for future `FlyingState`.
- `HorizontalMoveParams.Apply()` uses `Mathf.Sign(input)` internally to determine direction. Non-normalized input (e.g., `direction.x = 5f`) produces the same result as `direction.x = 1f` — magnitude is irrelevant, only sign matters.
- Rapid alternation between `Move(Vector2.right)` and `Move(Vector2.zero)` (or opposing directions) causes sub-state transitions every `FixedUpdate`. This is functionally correct but produces high-frequency `OnEnter`/`OnExit` calls. Future systems that attach side effects to state transitions (sounds, particles) should debounce or guard against rapid toggling.

### Simultaneous Requests

- **Jump + Dodge in the same frame.** Top-level `EvaluateTransitions()` runs before `WalkingState.Tick()`. Dodge transition is checked at the top level; jump transitions are checked inside WalkingState's sub-FSM. Dodge wins — `DodgeState.OnEnter()` consumes the dodge request but `IsJumpRequested` is not cleared. After the dodge completes, the stale jump request may fire on the next `EvaluateTransitions()` pass inside WalkingState (phantom jump). AI callers should avoid setting both flags simultaneously, or accept the phantom jump as intended behavior.
- **DropThrough + Jump in the same frame.** `DropThrough()` sets `_dropThroughTarget`, which may cause the ground check to return `false` on the same frame. If `IsGrounded` becomes `false` before the sub-FSM evaluates the jump transition (`IsJumpRequested && IsGrounded`), the jump is lost. The character drops through without jumping. Callers that need "jump down through platform" should call `DropThrough()` first and `Jump()` on a subsequent frame after confirming the character is in `FallSubState`.

### DropThrough() Under Continuous Calling

`DropThrough()` has an `IsGrounded` guard — it is a no-op when airborne. Calling it every frame while grounded on stacked one-way platforms causes the character to chain-drop through all of them (identical to holding S with `PlayerInputReader`). This is by design.

### Takeoff when jumping is not instant

It takes a few frames to set `IsGrounded` to false due to `CheckGround()` method, which detects ground a few frames after firing jump action.

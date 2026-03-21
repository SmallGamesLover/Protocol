# 2D Character Movement Component — Design Document
> Version: 3.0.0 | Project: Protocol

---

## Foundation

**Rigidbody Type:** Kinematic Rigidbody2D. The character moves via `MovePosition`; the physics engine handles collisions but does not control movement on its own. No depenetration, no tunneling, fully predictable behavior.

**Collision Detection Mode:** Discrete. Sufficient due to the capped maximum fall speed.

**Ground Check:** Ground contact is detected via `Physics2D.OverlapBox` just below the character's feet. The result is used to switch sub-states (Jump/Fall/Idle/Walk) and for coyote time.

**Ceiling Check:** Ceiling contact is detected via `Physics2D.OverlapBox` just above the character's head. When the character hits a ceiling while moving upward (`velocity.y > 0`), vertical velocity is zeroed immediately. This causes gravity to pull `velocity.y` negative on the next frame, triggering the `Jump→Fall` transition. The check lives in `CharacterMover2D.FixedUpdate` (not in a specific state) so it applies universally — jumps, future dodge-up, or any other upward movement.

**One-way Platforms:** Unity's `PlatformEffector2D` only works out of the box with Dynamic Rigidbody. With Kinematic, manual handling is required: on collision with a one-way platform, check the vertical movement direction — if the character is moving upward, the collision is ignored; if moving downward or standing still, the collision stops movement. Allocate dedicated time for implementation and testing.

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
| `DodgeState` | Horizontal dodge | Prototype |
| `FlyingState` | Airborne physics without gravity | Post-prototype |

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

| Parameter | Type | Description |
|---|---|---|
| `dodgeDistance` | float | Dodge distance in units |
| `dodgeTime` | float | Time to cover the dodge distance in seconds |

### Computed Value

```
dodgeSpeed = dodgeDistance / dodgeTime
```

### DodgeState Behavior

- Vertical velocity is reset to zero on entry — an airborne dodge acts as a "hover" with horizontal movement
- Horizontal movement is strictly in the dodge direction
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
| `Jump()` | Jump |
| `Dodge(Vector2 direction)` | Dodge in the specified direction |

**Environment Checks (FixedUpdate):**

| Property | Source | Description |
|---|---|---|
| `IsGrounded` | `OverlapBox` below feet | Used by FSM for state transitions |
| `IsCeiling` | `OverlapBox` above head | When true and `velocity.y > 0`, vertical velocity is zeroed |

### WalkingConfig
ScriptableObject with parameters for `WalkingState`. Tweakable in the Inspector during Play Mode without recompilation. Used by both the player and enemies — different instances with different values.

### HorizontalMoveParams
A `readonly struct` that encapsulates the horizontal acceleration formula. Created fresh each frame from `WalkingConfig` properties. See the dedicated section above.

### DodgeConfig
ScriptableObject with parameters for `DodgeState`. Tweakable in the Inspector during Play Mode without recompilation.

### CollisionSlideResolver2D
A utility class responsible for one thing: resolving movement collisions via a recursive collide-and-slide algorithm. Does not move the rigidbody — returns a safe displacement vector for the caller to apply via `MovePosition`. Unaware of who calls it — used by both `WalkingState` and `FlyingState`.

**How it works:**
1. Casts the collider from its current position along the movement direction using `Rigidbody2D.Cast` with an explicit position parameter (the rigidbody itself never moves between recursion steps). Cast distance extends beyond velocity by `SKIN_WIDTH` to detect surfaces within the skin gap ahead.
2. **Hit filtering:** ignores hits where `dot(direction, normal) >= 0` — the character is moving away from or along the surface, so there is nothing to resolve. If no valid hit remains, the full velocity is returned as displacement.
3. On a valid hit: computes safe displacement (`Mathf.Max(0, hit.distance - SKIN_WIDTH)`) and remaining velocity (`velocity - direction * Min(hit.distance, |velocity|)`). Safe displacement is clamped to zero to prevent backward movement when already closer than SKIN_WIDTH. Remainder is clamped to prevent inflation when the hit is beyond velocity range (found via SKIN_WIDTH cast extension).
4. Projects the remaining velocity onto the surface axis via `Vector2Extensions.ProjectOnAxis`, preserving the projected magnitude for consistent slide speed on slopes.
5. Corner wedge detection: if the projected slide direction opposes the previous recursion's surface normal, the character is wedged — returns only the safe portion with no further sliding.
6. Recurses with the slide vector from the new contact point.
7. Base cases: no valid hit (return full velocity), or `MAX_BOUNCES` exceeded (return zero).

**Constants:** `MAX_BOUNCES = 3`, `SKIN_WIDTH = 0.03f` (public const).

**Usage:**
```csharp
// CharacterMover2D.ApplyMovement():
Vector2 displacement = _collisionResolver.CollideAndSlide(Velocity * deltaTime, _contactFilter);
_rigidbody.MovePosition(_rigidbody.position + displacement);
```

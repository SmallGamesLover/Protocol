# CharacterMover2D — Implementation Plan
> Project: Protocol | Based on GDD v4.1.0

---

## Phase 0 — Scene Setup

**Goal:** minimal environment for testing movement. No subsequent phase can be verified without this one.

### 0.1 Test Scene
- Create a scene with placeholder geometry: flat floor (BoxCollider2D), several platforms at different heights
- Platforms are regular (not one-way) for now — one-way platforms will be added separately in Phase 4
- Camera with a fixed position covering the entire test area

### 0.2 Character Object
- GameObject with a placeholder sprite (colored rectangle)
- Kinematic Rigidbody2D, Discrete collision detection
- BoxCollider2D fitted to the sprite size
- Set up Physics2D layers: Player, Ground, Platform (for future one-way platforms)

### 0.3 PlayerInputReader — Input for Testing

> **Uses Unity New Input System** (`UnityEngine.InputSystem`). The legacy `UnityEngine.Input` is not used anywhere in the project.
>
> **No InputAction asset at this stage.** `.inputactions` files with bindings, action maps, and code generation are infrastructure for a later stage when rebinding settings and gamepad support are needed. For now, devices are read directly via the New Input System API — lighter and faster for a prototype, but still uses the new package, not the legacy `Input.GetKey`.

A separate MonoBehaviour script `PlayerInputReader` that reads keyboard and mouse state every frame via `Keyboard.current` and `Mouse.current`, and calls CharacterMover2D's public methods:

```csharp
using UnityEngine.InputSystem;

// PlayerInputReader reads raw device state via New Input System API.
// No .inputactions asset, no PlayerInput component, no generated C# class.
// Just direct reads from Keyboard.current / Mouse.current.
var kb = Keyboard.current;

float horizontal = 0f;
if (kb.aKey.isPressed) horizontal -= 1f;
if (kb.dKey.isPressed) horizontal += 1f;

_mover.Move(new Vector2(horizontal, 0f));

if (kb.spaceKey.wasPressedThisFrame)
    _mover.Jump();

if (kb.leftShiftKey.wasPressedThisFrame)
    _mover.Dodge(dodgeDirection);
```

This script is created in Phase 0 and used for manual testing throughout all subsequent phases. Commands are added as mechanics appear: `Move` in Phase 2, `Jump` in Phase 3, `DropThrough` in Phase 4, `Dodge` in Phase 5.

### 0.4 CharacterMover2D — Stub

At this stage the MonoBehaviour `CharacterMover2D` itself is created with public methods `Move(Vector2 direction)`, `Jump()`, `Dodge(Vector2 direction)` — but with no implementation. Method bodies are empty. This is needed so that `PlayerInputReader` can reference `CharacterMover2D` and the project compiles without errors from day one. Actual logic (FSM, states, physics) is filled in during Phases 1–5.

---

## Phase 1 — FSM Infrastructure

**Goal:** a ready-to-use tool for managing states. Code in this phase does not depend on Unity physics — pure C#.

### 1.1 Interfaces
- `IState` — `OnEnter()`, `OnExit()`
- `ITickable` — `Tick(float deltaTime)`, a standalone interface that does not inherit from IState

### 1.2 StateMachine\<TState\>
- Generic class with constraint `TState : class, IState`
- `SetInitialState(TState state)` — sets the initial state, calls `OnEnter()`
- `AddTransition(TState from, TState to, Func<bool> condition)` — transition registration
- `EvaluateTransitions()` — checks transitions, executes the first match
- `CurrentState` — public accessor
- No Tick method in StateMachine — ticking is the owner's responsibility

### 1.3 Verification
- Write a minimal test: create a StateMachine with two stub states and one transition, verify that `EvaluateTransitions()` switches the state and calls `OnExit()` / `OnEnter()`

---

## Phase 2 — Horizontal Movement and Ground Check

**Goal:** the character walks, runs, and decelerates. Minimal vertical slice — input produces visible movement on screen.

### 2.1 HorizontalMoveParams (Value Object)

A `readonly struct` encapsulating horizontal acceleration parameters and the shared formula used by all movement sub-states (Idle, Walk, Run, Jump, Fall):

- Fields: `MaxSpeed`, `Acceleration`, `Deceleration`
- Method: `float Apply(float currentVelX, float input, float deltaTime)` — pure function computing new horizontal velocity
- Eliminates code duplication: Walk, Run, and airborne states all call the same `Apply` method with different parameter sets

### 2.2 WalkingConfig (ScriptableObject)
- Ground parameters: `walkSpeed`, `runSpeed`, `acceleration`, `deceleration`
- Air parameters: `airAcceleration`, `airDeceleration`
- HorizontalMoveParams properties: `GroundWalkParams`, `GroundRunParams`, `AirParams` — each creates a fresh struct from current config values on every access (no caching, Inspector changes apply immediately)
- Create an asset instance with initial values for tweaking in Play Mode

### 2.3 Ground Check
- Implement ground contact detection via `Physics2D.OverlapBox` slightly below the character's feet
- Ground check parameters (box size, offset, LayerMask) exposed as serialized fields for Inspector tuning
- Add visualization in `OnDrawGizmos` — visible box in Scene View (green when grounded, red when airborne)

### 2.4 CharacterMover2D (MonoBehaviour)
- Create the component that owns the top-level FSM
- At this stage: only `WalkingState` in the top FSM (DodgeState comes later)
- In `FixedUpdate`: call `_topFsm.EvaluateTransitions()`, then tick the current state via `ITickable`
- Apply the resulting velocity via `Rigidbody2D.MovePosition`

### 2.5 Sub-States: IdleSubState and WalkSubState
- `IdleSubState` — no horizontal input, grounded. Uses `_config.GroundWalkParams.Apply(v.x, 0f, deltaTime)` — passing 0 as input decelerates toward zero
- `WalkSubState` — horizontal input, grounded. Uses `_config.GroundWalkParams.Apply()` for acceleration
- Transitions: Idle↔Walk based on horizontal input presence

### 2.6 RunSubState
- Horizontal input + Shift. Uses `_config.GroundRunParams.Apply()` for acceleration toward `runSpeed`
- Transitions: Walk↔Run on Shift hold/release, Run→Idle when input is absent

### 2.7 Collision Handling — CollisionSlideResolver2D Integration

`CollisionSlideResolver2D` is already implemented and available. This step wires it into `CharacterMover2D`.

- Instantiate `CollisionSlideResolver2D` in `Awake()`, passing `_rigidbody` to its constructor. Store as `_collisionResolver`
- Initialize a `ContactFilter2D` in `Awake()`: `useLayerMask = true`, assign `GroundLayerMask`. This filter is shared between the ground check and the resolver
- Replace the raw `velocity * deltaTime` application in `FixedUpdate` with:
  ```csharp
  Vector2 displacement = _collisionResolver.CollideAndSlide(Velocity * deltaTime, _contactFilter);
  _rigidbody.MovePosition(_rigidbody.position + displacement);
  ```
- No manual sinking fix or multi-pass casts needed — the recursive algorithm handles corner wedges and surface slides internally

### 2.8 Verification
- Character moves left-right with acceleration/deceleration
- Running while Shift is held
- Sliding along walls, no penetration into colliders
- Ground check correctly displayed in Gizmos

---

## Phase 3 — Jump, Fall, and Air Control

**Goal:** full airborne physics with responsive controls and air control.

### 3.1 Jump Parameters in WalkingConfig
- Add: `jumpHeight`, `timeToApex`, `timeToDescent`, `lowJumpMultiplier`, `maxFallSpeed`
- Implement computed values: `gravity`, `jumpVelocity`, `fallMultiplier`
- Add: `coyoteTime`, `jumpBufferTime`
- Add: `airAcceleration`, `airDeceleration`

### 3.2 Ceiling Check
- Implement ceiling contact detection via `Physics2D.OverlapBox` slightly above the character's head
- Parameters (`CeilingCheckSize`, `CeilingCheckOffset`) exposed as serialized fields
- In `FixedUpdate`, after ground check: if `IsCeiling && Velocity.y > 0`, zero out `Velocity.y`
- Add visualization in `OnDrawGizmos` — magenta box above head
- This ensures the character stops rising on ceiling contact regardless of the active state

### 3.3 JumpSubState
- Entry: jump pressed while `isGrounded` (or within coyote time)
- Sets `velocity.y = jumpVelocity`
- Applies gravity every frame: `velocity.y += gravity * deltaTime`
- Low jump: if jump button released before apex (`velocity.y > 0`), gravity is multiplied by `lowJumpMultiplier`
- Air control: applies `_config.AirParams.Apply()` for horizontal movement each tick
- Transition to Fall: `velocity.y < 0`

### 3.4 FallSubState
- Entry: `velocity.y < 0` or walked off edge (from Idle/Walk/Run on losing ground contact)
- Applies gravity with `fallMultiplier`: `velocity.y += gravity * fallMultiplier * deltaTime`
- Clamped: `velocity.y` does not drop below `-maxFallSpeed`
- Air control: applies `_config.AirParams.Apply()` for horizontal movement each tick
- Transition to Idle or Walk: on ground contact (`isGrounded`)

### 3.5 Coyote Time
- Timer starts on transition from a grounded state (Idle/Walk/Run) to Fall
- Jump is still available within the timer window — via a dedicated `Fall→Jump` transition that checks `IsJumpRequested && CoyoteTimer > 0`
- **Not activated** after a jump — only when walking off an edge
- Implementation: `wasCoyote` flag is reset on JumpSubState entry

### 3.6 Jump Buffer
- Timer starts when jump is pressed in the air
- If the character lands within the timer window — jump executes automatically
- Implementation: `FallSubState` checks the buffer on landing before transitioning to Idle/Walk

### 3.7 Verification
- Jump reaches the specified height (measure with ruler in Scene View — must match `jumpHeight`)
- Rise and fall times are visually different (asymmetric jump)
- Low jump on short press
- Coyote time works when walking off edge, does not work after a jump
- Jump buffer works on landing
- Ceiling stops ascent (velocity.y zeroed, character falls)
- `maxFallSpeed` caps fall speed
- Air control: horizontal direction changeable mid-jump with noticeable inertia
- Running jump preserves horizontal speed

---

## Phase 4 — One-Way Platforms

**Goal:** character jumps up through platforms from below, stands on them from above, and can drop through them on command.

### 4.1 Scene Setup
- Add one-way platform GameObjects to the test scene: sprite + `BoxCollider2D`, layer `Platform`
- Ensure `GroundLayerMask` on `CharacterMover2D` includes both Ground and Platform layers (already set up in Phase 0)

### 4.2 Predicate Parameter in CollisionSlideResolver2D (Strategy Pattern)

Add an optional hit-filtering predicate to `CollideAndSlide`:

```csharp
public Vector2 CollideAndSlide(
    Vector2 velocity,
    ContactFilter2D filter,
    Func<RaycastHit2D, bool> shouldIgnore = null)
```

Inside the hit processing loop, **after** the existing `dot(direction, normal)` check (which is cheaper), add:

```csharp
if (shouldIgnore != null && shouldIgnore(hit))
    continue;
```

The direction-based check (`dot`) runs first because it is a simple dot product that eliminates most irrelevant hits. The external predicate runs second, only on hits that would otherwise be resolved — this avoids unnecessary calls to potentially expensive filtering logic.

This is the only change to `CollisionSlideResolver2D`. The resolver remains generic — it does not know what a platform is. The filtering decision is delegated to the caller via the injected predicate (Strategy Pattern).

The predicate is also passed through to recursive calls so that platform filtering applies at every bounce iteration.

### 4.3 Platform Hit Predicate in CharacterMover2D

Create a private method `ShouldIgnorePlatformHit(RaycastHit2D hit)` that returns `true` when a platform hit should be ignored:

1. **Not a platform** → return `false` (resolve normally)
2. **Active drop-through target** → `hit.collider == _dropThroughTarget` → return `true`
3. **Side or bottom hit** → `hit.normal.y < 0.5f` → return `true` (platforms only block from above)
4. **Positional check** → character's bottom edge is below the platform's top edge minus `SKIN_WIDTH` → return `true` (character is below the platform — jumping through or continuing to fall)

The 0.5 threshold for `normal.y` corresponds to ~60° from horizontal. For rectangular `BoxCollider2D` normals are always axis-aligned (0 or 1), so this is a safety margin against floating-point edge cases.

Cache `_platformLayer` (int) via `LayerMask.NameToLayer("Platform")` in `Awake()` to avoid per-frame string lookups. `colliderHalfHeight` is computed locally in each method that needs it (`_boxCollider.size.y * 0.5f`) rather than cached as a field — this supports dynamic collider size changes at runtime.

### 4.4 Wire Predicate into ApplyMovement

Update `ApplyMovement()` to pass the predicate:

```csharp
Vector2 displacement = _collisionResolver.CollideAndSlide(
    Velocity * deltaTime, _contactFilter, ShouldIgnorePlatformHit);
_rigidbody.MovePosition(_rigidbody.position + displacement);
```

### 4.5 Ground Check Refactor — Array-Based with Platform Filtering

The current `CheckGround()` uses a simple boolean `OverlapBox`. Replace with an array-based version that evaluates each collider individually:

- Use `Physics2D.OverlapBox` overload that writes results into a pre-allocated `Collider2D[]` array
- For each result:
  - Skip if `collider == _dropThroughTarget`
  - If collider is on Platform layer: check position — character's bottom edge must be at or above `collider.bounds.max.y - SKIN_WIDTH`. If below → skip (character is under the platform)
  - Otherwise → valid ground, return `true`
- If no valid collider found → return `false`

### 4.6 Ceiling Check — Dedicated CeilingLayerMask

One-way platforms should never act as ceilings. Instead of programmatic filtering, add a new serialized field `CeilingLayerMask` that includes only the Ground layer (excludes Platform). Replace the current `GroundLayerMask` reference in `CheckCeiling()` with `CeilingLayerMask`. Platforms are filtered out at the physics query level — no code changes to ceiling check logic.

### 4.7 Drop-Through Mechanic

Add a public method `DropThrough()` on `CharacterMover2D`:

- Guard: only works when `IsGrounded` is true
- Identify which platform the character is standing on: iterate the ground check results array, find the first collider on Platform layer
- If found: store it in `_dropThroughTarget` (Collider2D field)
- If no platform found (standing on solid ground): do nothing

Clearing `_dropThroughTarget` — positional check in `FixedUpdate`, after movement is applied:

```csharp
if (_dropThroughTarget != null)
{
    float colliderHalfHeight = _boxCollider.size.y * 0.5f;
    float charBottom = _rigidbody.position.y - colliderHalfHeight;
    float platformTop = _dropThroughTarget.bounds.max.y;
    if (charBottom < platformTop - CollisionSlideResolver2D.SKIN_WIDTH)
        _dropThroughTarget = null;
}
```

Once cleared, the standard positional check in the predicate continues ignoring the platform naturally (character is below it). This is not a timeout — it is a handoff from Mechanism 1 (explicit override) to Mechanism 2 (positional check).

### 4.8 PlayerInputReader — Drop-Through Binding

Add drop-through input:

```csharp
if (kb.sKey.isPressed)
    _mover.DropThrough();
```

Using `isPressed` instead of `wasPressedThisFrame` — holding S chains drop-through across multiple platforms consecutively. On each frame where S is held and the character is grounded on a platform, `DropThrough()` fires again, initiating another drop immediately on landing.

### 4.9 Verification
- Character jumps through one-way platform from below without being stopped
- Character lands on one-way platform when falling from above
- Ground check correctly detects one-way platform as ground when standing on top
- Jumping from a one-way platform works identically to regular floor (including coyote time)
- Ceiling check does not trigger when jumping through a one-way platform from below
- Drop-through: pressing S while standing on a platform causes character to fall through
- Drop-through only affects the specific platform — other platforms below remain solid
- Drop-through while standing on solid ground does nothing
- Walking off a one-way platform and re-landing works correctly
- Collide-and-slide: horizontal movement along a one-way platform surface works (no sticking)

---

## Phase 5 — Dodge

**Goal:** horizontal dodge available from any WalkingState sub-state, with precise distance control via distance-based tracking.

### 5.1 DodgeConfig (ScriptableObject)

Create `DodgeConfig` with two serialized fields and one computed property:

- `DodgeDistance` (float) — how far the dodge moves, in units
- `DodgeSpeed` (float) — how fast the dodge moves, in units per second
- `DodgeTime` (float, computed read-only property) — `DodgeDistance / DodgeSpeed`. Not used by `DodgeState` for completion — exists for external systems (animation length, i-frames, UI)

Create an asset instance with placeholder values (e.g., distance 3, speed 15).

### 5.2 Dodge State on CharacterMover2D — Request/Consume Pattern

Add dodge-related state to `CharacterMover2D`, following the same pattern as jump:

- `IsDodgeRequested` (bool, public property) — set by `Dodge()`, cleared by `ConsumeDodgeRequest()`
- `DodgeDirection` (Vector2, public property) — direction captured from `Dodge()` call
- `ConsumeDodgeRequest()` (public method) — sets `IsDodgeRequested = false`

Update the existing `Dodge(Vector2 direction)` method (currently an empty stub from Phase 0) to set `IsDodgeRequested = true` and store `DodgeDirection = direction`.

### 5.3 DodgeState (Top-Level FSM State)

Create `DodgeState` implementing `IState, ITickable`. Constructor receives `CharacterMover2D` reference and `DodgeConfig`.

**Fields:**
- `_remainingDistance` (float) — distance left to cover, decremented each tick
- `_direction` (float) — captured dodge direction sign (+1 or -1)

**OnEnter():**
1. Read `DodgeDirection` from `CharacterMover2D` and extract the horizontal sign (`Mathf.Sign(direction.x)`). If `direction.x == 0`, use the character's facing direction or default to +1
2. Call `_mover.ConsumeDodgeRequest()` — clear the request flag
3. Set `Velocity.y = 0` — airborne dodge acts as horizontal hover
4. Set `_remainingDistance = _config.DodgeDistance`

**Tick(float deltaTime):**
```
maxStep = DodgeSpeed * deltaTime

if (maxStep >= _remainingDistance)
    // Last frame: clamp velocity to cover exactly the remaining distance
    Velocity = new Vector2((_remainingDistance / deltaTime) * _direction, 0f)
    _remainingDistance = 0f
else
    Velocity = new Vector2(DodgeSpeed * _direction, 0f)
    _remainingDistance -= maxStep
```

Key detail: `_remainingDistance` is decremented by `maxStep` (the *intended* step), not by actual displacement after collision resolution. This means dodging into a wall spends distance against the wall — the dodge ends on schedule rather than hovering indefinitely.

**IsFinished** (public bool property): returns `_remainingDistance <= 0f`.

**OnExit():** no specific cleanup needed.

### 5.4 Top-Level FSM Transition Registration

Register two transitions in `CharacterMover2D` during initialization, after creating both states:

```csharp
// CharacterMover2D holds typed references:
// private WalkingState _walkingState;
// private DodgeState _dodgeState;

_topFsm.AddTransition(_walkingState, _dodgeState,
    () => IsDodgeRequested);

_topFsm.AddTransition(_dodgeState, _walkingState,
    () => _dodgeState.IsFinished);
```

`_walkingState` and `_dodgeState` are stored as their concrete types (not `IState`) so that transition conditions can read state-specific properties like `IsFinished`. The `StateMachine<IState>` stores them internally as `IState` — the concrete references are only used by the owner.

Order of evaluation: `WalkingState→DodgeState` is registered before `DodgeState→WalkingState`. When the current state is `WalkingState`, only the first transition is relevant (because `From == CurrentState`). When the current state is `DodgeState`, only the second is relevant. No ordering conflicts.

### 5.5 ResolveSubState Verification

`WalkingState.OnEnter()` already calls `ResolveSubState()`, which picks the correct sub-state based on current conditions. This was implemented in Phase 2 and extended in Phase 3. After a mid-air dodge:
- `IsGrounded` is `false` → `ResolveSubState()` enters `FallSubState`
- Character begins falling with gravity

After a grounded dodge:
- `IsGrounded` is `true` + input state → enters Idle, Walk, or Run accordingly

No new code is needed — only verification that the existing implementation handles post-dodge re-entry correctly.

### 5.6 PlayerInputReader — Dodge Binding

Add dodge input to `PlayerInputReader`:

```csharp
if (kb.leftShiftKey.wasPressedThisFrame)
{
    float horizontal = 0f;
    if (kb.aKey.isPressed) horizontal -= 1f;
    if (kb.dKey.isPressed) horizontal += 1f;

    // If no direction held, dodge in facing direction (default: right)
    if (horizontal == 0f) horizontal = 1f;

    _mover.Dodge(new Vector2(horizontal, 0f));
}
```

> **Note:** The dodge key binding (Shift) is a placeholder for testing. Shift is currently also used for Run detection (`IsRunRequested`). For the prototype, this is acceptable — the interaction is: tap Shift = dodge, hold Shift = run. `wasPressedThisFrame` fires on the first frame of the press, so dodge triggers before run. Final binding will be determined during Phase 7 polish or when the input system is formalized.

### 5.7 Verification

- Dodge from Idle, Walk, Run — character moves exactly `DodgeDistance` units horizontally (measure in Scene View with a ruler or debug Gizmo)
- Dodge from Jump and Fall — vertical velocity resets to zero, character moves horizontally then falls
- After a mid-air dodge — character enters FallSubState (not Idle)
- After a grounded dodge — character enters the correct sub-state based on input (Idle/Walk/Run)
- Dodge into a wall — no penetration, dodge ends on schedule (does not hover against wall)
- Dodge off a platform edge — character completes remaining dodge distance in the air, then falls
- `DodgeDistance` and `DodgeSpeed` are independently tweakable in Inspector during Play Mode
- `DodgeTime` computed property updates correctly when either serialized field changes
- Rapid dodge spam — only one dodge per press (`wasPressedThisFrame` + consume pattern prevents re-triggering)

---

## Phase 6 — Input/Movement Separation Verification

**Goal:** confirm that CharacterMover2D is fully independent of the input source and ready for AI integration.

> `PlayerInputReader` was created in Phase 0.3 and has been used for testing throughout all previous phases. This phase does not create new input code — it verifies that the separation works.

### 6.1 Input Source Independence Test
- Disable `PlayerInputReader` on the character object
- Attach a test script that calls `_mover.Move(Vector2.right)` every frame — character moves right without keyboard involvement
- Call `_mover.Jump()` on a timer — character jumps without key presses
- Verify that CharacterMover2D contains no `using UnityEngine.InputSystem` and no references to `Keyboard` / `Mouse`

### 6.2 AI Readiness Check
- The same test script simulates future AI enemy behavior: moves toward a point, jumps when necessary
- CharacterMover2D behaves identically regardless of whether methods are called by `PlayerInputReader` or the test script

### 6.3 Project Cleanliness
- No calls to `UnityEngine.Input` (legacy API) anywhere in the project
- All references to `Keyboard.current` / `Mouse.current` are only inside `PlayerInputReader`

---

## Phase 7 — Polish and Tweaking

**Goal:** bring movement feel to an acceptable level before integration with other systems.

### 7.1 Parameter Tweaking
- Tune `walkSpeed`, `runSpeed`, `acceleration`, `deceleration` using Play Mode + ScriptableObject
- Tune `airAcceleration`, `airDeceleration` — balance between responsive air control and momentum feel
- Tune `jumpHeight`, `timeToApex`, `timeToDescent`, `lowJumpMultiplier`
- Tune `coyoteTime`, `jumpBufferTime`
- Tune `dodgeDistance`, `dodgeSpeed`

### 7.2 Debug Visualization
- Display current FSM state (top-level + sub-state) in UI or Console
- Gizmos for ground check, ceiling check, velocity vector, dodge distance
- Debug Gizmos in CharacterMover2D: desired displacement (yellow), resolved displacement (cyan), both with wireframe boxes and SKIN_WIDTH zones

### 7.3 Edge Cases
- Dodge in a corner between wall and floor
- Jump into ceiling at close range
- Rapid direction switching
- Dodge at platform edge (character must not teleport through the floor)
- Multiple jump presses in a single frame
- Jump + hold direction into wall — must slide along wall, not stick
- Stand in 90° corner, hold into wall — no penetration

---

## Time Estimate

| Phase | Contents | Estimate (hours) |
|---|---|---|
| 0 | Scene setup + PlayerInputReader | 3–4 |
| 1 | FSM infrastructure | 3–5 |
| 2 | Horizontal movement and ground check | 8–12 |
| 3 | Jump, fall, ceiling check, and air control | 10–14 |
| 4 | One-way platforms + drop-through | 8–12 |
| 5 | Dodge | 5–8 |
| 6 | Input/movement separation verification | 1–2 |
| 7 | Polish and tweaking | 5–8 |
| **Total** | | **43–67** |

> The riskiest phases in terms of time are 2, 3, and 4. Kinematic Rigidbody requires manual collision handling, and one-way platforms without PlatformEffector2D are a separate challenge. If Phase 4 starts consuming disproportionate time — consider a temporary switch to Dynamic Rigidbody with direct velocity control to unblock other prototype systems.

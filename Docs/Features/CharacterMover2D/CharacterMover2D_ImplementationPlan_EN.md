# CharacterMover2D — Implementation Plan
> Project: Protocol | Based on GDD v3.0.0

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

This script is created in Phase 0 and used for manual testing throughout all subsequent phases. Commands are added as mechanics appear: `Move` in Phase 2, `Jump` in Phase 3, `Dodge` in Phase 5.

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

**Goal:** character jumps up through platforms from below and stands on them from above.

### 4.1 Tag or Layer for One-Way Platforms
- A way to distinguish a one-way platform from a regular collider

### 4.2 Handling Logic
- On collision with a one-way platform: if `velocity.y > 0` (moving up) — ignore the collision
- If `velocity.y <= 0` (falling or standing) and the character's bottom edge is above the platform's top edge — stop the fall
- Account for edge case: character stands on a platform and wants to drop down (optional mechanic, can be deferred)

### 4.3 Ground Check for One-Way Platforms
- `OverlapBox` must recognize one-way platforms as ground when the character is standing on top of them

### 4.4 Verification
- Jumping up through the platform from below
- Landing on the platform when falling
- Ground check correctly detects contact with a one-way platform
- Jumping from a one-way platform works the same as from a regular floor

---

## Phase 5 — Dodge

**Goal:** horizontal dodge available from any WalkingState sub-state.

### 5.1 DodgeConfig (ScriptableObject)
- Parameters: `dodgeDistance`, `dodgeTime`
- Computed: `dodgeSpeed = dodgeDistance / dodgeTime`

### 5.2 DodgeState (Top-Level FSM)
- Entry: dodge button pressed. Transition WalkingState→DodgeState at the top level
- `OnEnter()`: zero out `velocity.y`, lock dodge direction, start timer
- `Tick()`: move character horizontally at `dodgeSpeed` in the locked direction
- `OnExit()`: nothing specific
- Transition DodgeState→WalkingState: when `dodgeTime` expires

### 5.3 Top-Level FSM Transition Registration
- `WalkingState → DodgeState`: dodge input
- `DodgeState → WalkingState`: dodge timer expired
- Ensure `WalkingState.OnEnter()` calls `ResolveSubState()` — after a mid-air dodge it must enter Fall, not Idle

### 5.4 Verification
- Dodge from Idle, Walk, Run — horizontal displacement of `dodgeDistance`
- Dodge from Jump and Fall — vertical velocity resets, character hovers and moves horizontally
- After a mid-air dodge — character starts falling (Fall), not standing (Idle)
- Dodge into a wall — no penetration into collider

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
- Tune `dodgeDistance`, `dodgeTime`

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
| 4 | One-way platforms | 6–10 |
| 5 | Dodge | 5–8 |
| 6 | Input/movement separation verification | 1–2 |
| 7 | Polish and tweaking | 5–8 |
| **Total** | | **41–63** |

> The riskiest phases in terms of time are 2, 3, and 4. Kinematic Rigidbody requires manual collision handling, and one-way platforms without PlatformEffector2D are a separate challenge. If Phase 4 starts consuming disproportionate time — consider a temporary switch to Dynamic Rigidbody with direct velocity control to unblock other prototype systems.

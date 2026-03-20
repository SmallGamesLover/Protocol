# CharacterMover2D — Tasks
> Based on Implementation Plan v2.0.0
> Tasks are ordered chronologically. Complete each stage top-to-bottom before moving to the next.

- [x] Phase 0: Scene setup and stubs
  - [x] Create a new scene `TestMovement` with a flat floor GameObject (sprite + `BoxCollider2D`, layer `Ground`)
  - [x] Add 3–4 solid platform GameObjects at different heights (sprite + `BoxCollider2D`, layer `Ground`); no one-way platforms yet
  - [x] Add a `Camera` with `Orthographic` projection, fixed position, covering the entire test area
  - [x] Set up Physics2D layers in Project Settings: `Player`, `Ground`, `Platform`
  - [x] Create player GameObject: colored rectangle sprite, `Kinematic Rigidbody2D` (Discrete collision detection), `BoxCollider2D` fitted to sprite, layer `Player`
  - [x] Create `CharacterMover2D` MonoBehaviour stub with empty public methods: `Move(Vector2 direction)`, `Jump()`, `Dodge(Vector2 direction)`. No logic inside — just empty bodies so the project compiles
  - [x] Create `PlayerInputReader` MonoBehaviour: uses `UnityEngine.InputSystem` (NOT legacy `UnityEngine.Input`). Reads `Keyboard.current` directly — no `.inputactions` asset, no `PlayerInput` component. Calls `_mover.Move()` with A/D keys. Leave `Jump()` and `Dodge()` calls commented out until Phases 3 and 5
  - [x] Attach both `CharacterMover2D` and `PlayerInputReader` to the player GameObject. Verify the project compiles and runs without errors

- [x] Phase 1: FSM infrastructure (pure C#, no Unity dependencies)
  - [x] Create `IState` interface with methods: `OnEnter()`, `OnExit()`
  - [x] Create `ITickable` interface with method: `Tick(float deltaTime)`. It does NOT inherit from `IState` — it is a standalone interface
  - [x] Create `StateMachine<TState>` generic class with constraint `where TState : class, IState`
  - [x] Implement `CurrentState` as a public read-only property
  - [x] Implement `SetInitialState(TState state)` — stores the state and calls `state.OnEnter()`
  - [x] Implement private `Transition` struct with fields: `From` (TState), `To` (TState), `Condition` (Func\<bool\>)
  - [x] Implement `AddTransition(TState from, TState to, Func<bool> condition)` — adds to `List<Transition>`
  - [x] Implement `EvaluateTransitions()` — iterates the list, finds the first transition where `From == CurrentState` and `Condition()` is true, calls `CurrentState.OnExit()`, sets new state, calls `OnEnter()`, returns immediately after first match
  - [x] StateMachine has NO `Tick` method — ticking is the caller's responsibility
  - [x] Write a simple verification: create two stub `IState` classes, register one transition, call `EvaluateTransitions()`, confirm that state switched and `OnEnter()`/`OnExit()` were called

- [ ] Phase 2: Horizontal movement and ground check
  - [x] Create `WalkingConfig` ScriptableObject with serialized fields: `WalkSpeed` (float), `RunSpeed` (float), `Acceleration` (float), `Deceleration` (float)
  - [x] Create a `WalkingConfig` asset instance in the project with placeholder values (e.g., walk 5, run 8, accel 50, decel 70)
  - [x] Implement ground check in `CharacterMover2D`: `Physics2D.OverlapBox` slightly below the character's collider bottom edge. Expose `GroundCheckSize` (Vector2), `GroundCheckOffset` (Vector2), `GroundLayerMask` (LayerMask) as `[SerializeField]` fields
  - [x] Add `OnDrawGizmos()` to `CharacterMover2D` that draws the ground check box in Scene View (green when grounded, red when airborne)
  - [x] Expose `IsGrounded` as a public read-only property on `CharacterMover2D`
  - [ ] Create `WalkingState` class implementing `IState, ITickable`. It owns a `StateMachine<IState>` for sub-states. Constructor receives `WalkingConfig` and a shared context/reference to `CharacterMover2D`
  - [ ] Create `IdleSubState` implementing `IState, ITickable` — applies deceleration to `velocity.x` toward zero each `Tick`
  - [ ] Create `WalkSubState` implementing `IState, ITickable` — accelerates `velocity.x` toward `WalkSpeed` using `Acceleration`, decelerates using `Deceleration` when input direction opposes velocity
  - [ ] Register sub-state transitions in `WalkingState` constructor: `Idle→Walk` when horizontal input != 0, `Walk→Idle` when horizontal input == 0
  - [ ] Implement `WalkingState.OnEnter()` with `ResolveSubState()` — picks Idle or Walk based on current horizontal input
  - [ ] Implement `WalkingState.Tick(float deltaTime)` — calls `_subFsm.EvaluateTransitions()`, then ticks the current sub-state via `ITickable` cast
  - [ ] Implement `WalkingState.OnExit()` — calls `_subFsm.CurrentState.OnExit()`
  - [ ] Wire up `CharacterMover2D`: create top-level `StateMachine<IState>`, add `WalkingState` as the only state. In `FixedUpdate()`: run ground check, call `_topFsm.EvaluateTransitions()`, tick current state via `ITickable` cast, apply velocity via `Rigidbody2D.MovePosition(rb.position + velocity * deltaTime)`
  - [ ] Create `RunSubState` implementing `IState, ITickable` — same as WalkSubState but accelerates toward `RunSpeed`
  - [ ] Register transitions: `Walk→Run` when Shift held AND horizontal input != 0, `Run→Walk` when Shift released, `Run→Idle` when horizontal input == 0
  - [ ] Update `ResolveSubState()` to account for Run (Shift held + input → Run)
  - [ ] In `CharacterMover2D.Awake()`: initialize a `ContactFilter2D` field (`_contactFilter`) with `useLayerMask = true` and `GroundLayerMask` assigned. Instantiate `CollisionSlideResolver2D` as a private field `_collisionResolver`, passing `_rigidbody` to its constructor
  - [ ] Replace the raw `MovePosition(rb.position + velocity * deltaTime)` call with an `ApplyMovement(float deltaTime)` private method: compute `displacement = _collisionResolver.CollideAndSlide(Velocity * deltaTime, _contactFilter)`, then call `_rigidbody.MovePosition(_rigidbody.position + displacement)`
  - [ ] Uncomment `Move()` call in `PlayerInputReader`. Verify: character walks left/right, runs with Shift, decelerates to stop, slides along walls, ground check gizmo is visible

- [ ] Phase 3: Jump and fall
  - [ ] Add serialized fields to `WalkingConfig`: `JumpHeight` (float), `TimeToApex` (float), `TimeToDescent` (float), `LowJumpMultiplier` (float), `MaxFallSpeed` (float), `CoyoteTime` (float), `JumpBufferTime` (float)
  - [ ] Add computed read-only properties to `WalkingConfig`: `Gravity` = `-2 * JumpHeight / (TimeToApex * TimeToApex)`, `JumpVelocity` = `2 * JumpHeight / TimeToApex`, `FallMultiplier` = `(TimeToApex / TimeToDescent) * (TimeToApex / TimeToDescent)`
  - [ ] Create `JumpSubState` implementing `IState, ITickable`: `OnEnter()` sets `velocity.y = JumpVelocity`. `Tick()` applies gravity: `velocity.y += Gravity * deltaTime`. If jump button released and `velocity.y > 0`, multiply gravity by `LowJumpMultiplier`
  - [ ] Create `FallSubState` implementing `IState, ITickable`: `Tick()` applies gravity with fall multiplier: `velocity.y += Gravity * FallMultiplier * deltaTime`. Clamp `velocity.y` to no less than `-MaxFallSpeed`
  - [ ] Register transitions in `WalkingState` constructor: `Idle→Jump` / `Walk→Jump` / `Run→Jump` when jump requested AND (grounded OR coyote time active). `Jump→Fall` when `velocity.y < 0`. `Idle→Fall` / `Walk→Fall` / `Run→Fall` when NOT grounded (walked off edge). `Fall→Idle` when grounded AND no horizontal input. `Fall→Walk` when grounded AND horizontal input != 0
  - [ ] Implement coyote time: track a `_coyoteTimer` float. Start timer when transitioning from any grounded sub-state to `FallSubState` (walked off edge). Do NOT start timer when entering Fall from Jump. While timer > 0, jump transitions remain available. Decrement timer in `FallSubState.Tick()`
  - [ ] Implement jump buffer: track a `_jumpBufferTimer` float. Set to `JumpBufferTime` when jump is pressed while airborne. Decrement each frame. On landing (Fall→Idle or Fall→Walk), check if buffer > 0 — if so, transition to Jump immediately
  - [ ] Update `ResolveSubState()` to handle airborne entry: if not grounded → FallSubState
  - [ ] Uncomment `Jump()` call in `PlayerInputReader`. Verify: jump reaches expected height, asymmetric rise/fall, low jump on short press, coyote time works off edges but not after jump, jump buffer works on landing, ceiling stops ascent, `MaxFallSpeed` caps fall speed

- [ ] Phase 4: One-way platforms
  - [ ] Add one-way platform GameObjects to the test scene: sprite + `BoxCollider2D`, layer `Platform`
  - [ ] Implement one-way platform logic in collision handling: if character collides with a `Platform` layer collider AND `velocity.y > 0` (moving up) — ignore the collision entirely
  - [ ] If colliding with `Platform` layer AND `velocity.y <= 0` AND character's collider bottom edge is above the platform's collider top edge — treat as solid ground, stop the fall
  - [ ] Update ground check `GroundLayerMask` to include `Platform` layer — `OverlapBox` must recognize one-way platforms as ground when the character stands on top
  - [ ] Verify: character jumps through platform from below, lands on platform when falling, ground check detects one-way platform as ground, jumping from one-way platform works identically to regular floor

- [ ] Phase 5: Dodge
  - [ ] Create `DodgeConfig` ScriptableObject with serialized fields: `DodgeDistance` (float), `DodgeTime` (float)
  - [ ] Add computed read-only property: `DodgeSpeed` = `DodgeDistance / DodgeTime`
  - [ ] Create a `DodgeConfig` asset instance with placeholder values (e.g., distance 3, time 0.2)
  - [ ] Create `DodgeState` implementing `IState, ITickable`. `OnEnter()`: zero `velocity.y`, capture dodge direction, start `_dodgeTimer = DodgeTime`. `Tick()`: set `velocity.x = DodgeSpeed * direction`, keep `velocity.y = 0`, decrement `_dodgeTimer`. Expose `IsFinished` bool (true when timer <= 0)
  - [ ] Register top-level transitions in `CharacterMover2D`: `WalkingState→DodgeState` when dodge pressed, `DodgeState→WalkingState` when `DodgeState.IsFinished`
  - [ ] Verify `WalkingState.OnEnter()` calls `ResolveSubState()` — after mid-air dodge, must enter `FallSubState`, not `IdleSubState`
  - [ ] Handle dodge + wall collision via `CollisionSlideResolver2D` — decide whether dodge stops or slides (test both, pick one)
  - [ ] Uncomment `Dodge()` call in `PlayerInputReader`. Verify: dodge from Idle/Walk/Run covers `DodgeDistance`, dodge from Jump/Fall zeroes vertical velocity and moves horizontally, after mid-air dodge character falls (not idle), dodge into wall has no penetration

- [ ] Phase 6: Input/movement separation verification (no new code — only testing)
  - [ ] Disable `PlayerInputReader` on the player GameObject
  - [ ] Create a temporary test MonoBehaviour `AutoMoverTest` that calls `_mover.Move(Vector2.right)` in `Update()`. Attach to player — verify character moves right without keyboard
  - [ ] Add `_mover.Jump()` call on a timer in `AutoMoverTest` — verify character jumps without key presses
  - [ ] Confirm `CharacterMover2D.cs` has zero `using UnityEngine.InputSystem` imports and zero references to `Keyboard` or `Mouse`
  - [ ] Confirm the only file in the project that references `Keyboard.current` or `Mouse.current` is `PlayerInputReader.cs`
  - [ ] Confirm zero uses of legacy `UnityEngine.Input` anywhere in the project
  - [ ] Delete `AutoMoverTest`, re-enable `PlayerInputReader`

- [ ] Phase 7: Polish and tweaking
  - [ ] Tune `WalkSpeed`, `RunSpeed`, `Acceleration`, `Deceleration` via ScriptableObject in Play Mode
  - [ ] Tune `JumpHeight`, `TimeToApex`, `TimeToDescent`, `LowJumpMultiplier`, `MaxFallSpeed`
  - [ ] Tune `CoyoteTime`, `JumpBufferTime`
  - [ ] Tune `DodgeDistance`, `DodgeTime`
  - [ ] Add debug FSM display: show current top-level state name + sub-state name in `OnGUI()` or Console log
  - [ ] Add Gizmos: velocity vector, dodge distance preview
  - [ ] Test edge case: dodge in a corner between wall and floor
  - [ ] Test edge case: jump into ceiling at point-blank range
  - [ ] Test edge case: rapid direction switching (A→D→A quickly)
  - [ ] Test edge case: dodge at platform edge (no teleporting through floor)
  - [ ] Test edge case: multiple jump presses in a single frame

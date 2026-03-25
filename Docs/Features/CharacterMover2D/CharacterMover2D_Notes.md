# CharacterMover2D Notes

## Slope handling (deferred)

Ground check via a horizontal OverlapBox does not reliably detect angled surfaces. Running downhill produces a "staircase" effect because grounded sub-states only output horizontal velocity, causing the character to repeatedly walk off the slope, fall, land, and repeat.

## Drop-through safety timer (deferred)

The current drop-through implementation relies purely on positional
clearing: `_dropThroughTarget` is nulled as soon as the character's
bottom edge falls below the platform's top edge. This covers all
normal gameplay scenarios.

However, if the character somehow gets stuck inside the platform
collider (e.g., due to an unforeseen physics edge case, a destroyed
or repositioned collider at runtime, or a future mechanic that
teleports the character), `_dropThroughTarget` may never clear
because the positional condition is never met.

Mitigation if needed: add a safety timeout (0.3–0.5s) that
force-clears `_dropThroughTarget` regardless of position. This
requires a float field `_dropThroughTimer`, set alongside
`_dropThroughTarget` in `DropThrough()`, decremented in
`FixedUpdate`, and checked as an OR condition next to the
positional clear. Consider adding `DropThroughSafetyTime` to
`WalkingConfig` to make the value tweakable in the Inspector.

## Platform layer identification — hardcoded string trade-off

`CharacterMover2D` caches the Platform layer index in `Awake()` via
`LayerMask.NameToLayer("Platform")`. This hardcodes the layer name
as a string, which means renaming the layer in Project Settings will
silently break platform detection (`NameToLayer` returns -1 for
unknown names).

Alternatives considered:
- Serialized `LayerMask` field (e.g., `PlatformLayerMask`) assigned
  in Inspector — survives renames but adds another Inspector field
  and requires bitwise comparison (`1 << layer`) instead of direct
  int equality.
- Extracting the layer index from the existing `GroundLayerMask` by
  subtracting the Ground bits — fragile and obscure.

Current decision: hardcode is acceptable because the Platform layer
was set up in Phase 0, is referenced across the project (Physics2D
matrix, GroundLayerMask, CeilingLayerMask), and is unlikely to be
renamed. If the project grows to the point where multiple scripts
reference layer names, consider centralizing them in a static class
(e.g., `PhysicsLayers.Platform`) to have a single point of change.

## API Behavioral Notes — Hostile Input Patterns

Findings from Phase 6 verification (tasks 87–96). These describe how
`CharacterMover2D` behaves under calling patterns that differ from
typical human input. All results were derived from code inspection and
confirmed (or are intended to be confirmed) in Play Mode via
`AutoMoverTest`.

### 87 — Move() with non-normalized input magnitude

Calling `Move(new Vector2(5f, 0f))` produces the same result as
`Move(Vector2.right)`. `HorizontalMoveParams.Apply()` computes the
target as `MaxSpeed * Mathf.Sign(input)` — only the sign of `input`
matters, not its magnitude. A value of 5 gives `Sign = +1`, identical
to 1. **AI callers may pass any positive or negative value as
horizontal input without affecting speed.**

### 88 — Move() with direction.y != 0

`CharacterMover2D.Move()` stores only `direction.x` as
`HorizontalInput`. The `direction.y` component is not stored and is
not read by any current sub-state. Calling `Move(new Vector2(1f, 1f))`
is equivalent to `Move(Vector2.right)` — no vertical velocity change,
no errors. The `direction.y` parameter is reserved for future use by
`FlyingState`.

### 89 — IsJumpHeld = true permanently (full-height jump)

When `IsJumpHeld` is set to `true` and never changed, the
low-jump-multiplier branch in `JumpSubState.Tick()` never activates
(`if (!_mover.IsJumpHeld && _mover.Velocity.y > 0f)`). The character
always performs a full-height jump regardless of how briefly `Jump()`
was called. **This is the expected default behavior for AI callers**
that do not model button-hold duration. Document `IsJumpHeld = true`
as the correct setup for any AI agent controlling this character.

### 90 — Pogo-stick (Jump() every frame)

Calling `Jump()` every `Update()` causes the character to pogo-stick
indefinitely. Root cause: `FallSubState.Tick()` captures
`IsJumpRequested = true` into `JumpBufferTimer` on every `FixedUpdate`
while airborne, then clears the flag. Because the coroutine re-sets the
flag every `Update()`, the buffer is perpetually refreshed. On landing,
`Fall→Jump` fires via `IsGrounded && JumpBufferTimer > 0`. The cycle
repeats.

**AI callers** must call `Jump()` at most once per intended jump and
not call it again until the character has left the ground and landed.

### 91 — Infinite dodge chain (Dodge() every frame)

Calling `Dodge()` every `Update()` produces a continuous dodge chain
with no gap. After `DodgeState` completes and transitions back to
`WalkingState`, `IsDodgeRequested` is already `true` (re-set by the
caller in the preceding `Update()`). The very next
`EvaluateTransitions()` fires `WalkingState→DodgeState` again.

`CharacterMover2D` does not and should not enforce a dodge cooldown —
that is game-design policy. **AI callers must enforce a cooldown
externally** if continuous chaining is unacceptable.

### 92 — Jump + Dodge same frame (jump wins)

**Observed behavior: jump won. Low jump, because `IsJumpHeld` was
`false` at the time.**

When both `Jump()` and `Dodge()` are called in the same `Update()`,
the character performs a jump rather than a dodge. The jump is a
minimum-height arc because `IsJumpHeld = false` causes
`JumpSubState.Tick()` to apply `LowJumpMultiplier` immediately on every
tick while ascending.

Code analysis predicted dodge should win: `WalkingState→DodgeState`
is the first transition in the top-level FSM and `IsDodgeRequested`
is `true`, so `EvaluateTransitions()` should fire it before
`WalkingState.Tick()` reaches the sub-FSM jump check. The observed
inversion suggests one of the following:

- The dodge executed first but was visually brief; what dominated was
  the **phantom jump** that fires immediately after — `IsJumpRequested`
  is not cleared during `DodgeState` and fires via `CanJump()` on the
  first `WalkingState.Tick()` after dodge completes. In this reading
  dodge did win priority-wise, but the low jump was the visible outcome.
- There was a frame-timing difference in the test where `Jump()` and
  `Dodge()` landed in adjacent `FixedUpdate` evaluations rather than
  the same one, causing the sub-FSM to process the jump request before
  the top-level FSM saw `IsDodgeRequested`.

Regardless of the ordering, the **net observable result is a low jump**
(`IsJumpHeld = false`). `IsDodgeRequested` does not survive the
sequence regardless of who wins — it is consumed by `DodgeState.OnEnter()`
if dodge fires, or remains true and triggers a dodge on the next
`EvaluateTransitions()` if jump fired first.

**For AI callers:** avoid setting both `IsJumpRequested` and
`IsDodgeRequested` simultaneously. The outcome is a low jump, either
directly or as a phantom jump after a rapid dodge.

### 93 — Platform cascade (DropThrough() every frame)

Calling `DropThrough()` every `Update()` is identical in effect to
holding S in `PlayerInputReader`. The `IsGrounded` guard prevents calls
from firing while airborne. On each landing frame atop a Platform-layer
collider, `DropThrough()` fires and the character falls through. This
chains across stacked one-way platforms with no additional logic. This
is intentional and by design.

### 94 — DropThrough + Jump same frame (jump is lost)

When `DropThrough()` is called before `Jump()` in the same `Update()`:

1. `DropThrough()` sets `_dropThroughTarget` to the platform collider.
2. Next `FixedUpdate()`: `CheckGround()` skips `_dropThroughTarget` →
   `IsGrounded = false`.
3. `CanJump()` requires `IsGrounded = true` → condition fails → jump
   does not fire.
4. `FallSubState.Tick()` captures `IsJumpRequested` into
   `JumpBufferTimer` — the jump may fire from the landing surface below
   the platform (possibly unintended).

**For AI callers that need "jump down through a platform":** call
`DropThrough()`, wait for the character to enter `FallSubState`, then
call `Jump()` to use coyote time.

### 95 — Rapid run toggle (Walk↔Run every FixedUpdate)

Alternating `IsRunRequested` every `Update()` with constant horizontal
input causes `Walk→Run` and `Run→Walk` transitions to fire on
alternating `FixedUpdate()` calls. Movement is functionally correct —
horizontal speed oscillates between `WalkSpeed` and `RunSpeed`
convergence targets each tick.

**Concern for future systems:** rapid `OnEnter()`/`OnExit()` calls are
a hazard for sound, particle, or animation systems that trigger on state
transitions. Those systems must guard against high-frequency toggling
(e.g., minimum dwell-time before triggering a sound, or debounce
before starting an animation).

### 96 — Rapid move toggle (Idle↔Walk every FixedUpdate)

Alternating `Move(Vector2.right)` and `Move(Vector2.zero)` every
`Update()` causes `Idle→Walk` and `Walk→Idle` transitions to fire on
alternating `FixedUpdate()` calls. The character barely moves — each
tick applies partial acceleration from the alternating targets.

**Concern:** identical to task 95. Future side-effect systems must
debounce state-entry events.

## State coupling to CharacterMover2D (deferred)

All state classes (`WalkingState`, `DodgeState`, `IdleSubState`, `WalkSubState`,
`RunSubState`, `JumpSubState`, `FallSubState`) receive `CharacterMover2D` as a
constructor argument and access its properties directly. This couples every state
to a `MonoBehaviour`, making pure unit testing impractical without Unity's test
framework.

Introducing an `ICharacterMover` interface (exposing `Velocity`, `IsGrounded`,
`HorizontalInput`, etc.) would allow state logic to be tested with simple stub
implementations and would decouple the FSM layer from the Unity lifecycle.

The typed references to `WalkingState` and `DodgeState` in `CharacterMover2D`
(lines 31–32) are intentional — they are needed for transition condition lambdas
(`_dodgeState.IsFinished`). This coupling is acceptable and architecturally
justified given the current design.

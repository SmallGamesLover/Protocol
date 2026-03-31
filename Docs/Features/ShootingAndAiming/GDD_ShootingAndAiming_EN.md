# Shooting & Aiming — Design Document
> Version: 0.4.0 | Project: Protocol

---

## Foundation

**Combat model:** Hitscan-only. All weapons use instant raycasts — there are no physical projectiles in the game. Visual tracers are cosmetic effects with no gameplay impact.

**Aim model:** Free mouse aiming. The player aims at any point on the screen; the aim position is converted from screen space to world space via the camera. The direction from the character (or weapon muzzle) to the aim point determines the firing direction.

**Character facing:** Always controlled by the aim position. If the cursor is to the left of the character — the character faces left. If to the right — faces right. This rule applies at all times, regardless of movement direction or weapon state. Movement direction does not affect facing.

**Interaction with dodge:** Shooting and reloading are blocked for the entire duration of the dodge. A dodge in progress interrupts an active reload (progress is lost). After the dodge completes, the weapon returns to its pre-dodge state (minus any interrupted reload progress).

---

## Aim System

The aim system converts a screen-space mouse position into a world-space firing direction.

**Pipeline:**
1. `PlayerInputReader` reads `Mouse.current.position` every `Update()`
2. The screen position is passed to the aim controller, which converts it to a world-space point via `Camera.main.ScreenToWorldPoint`
3. The aim direction is computed as the normalized vector from the **fire origin** to the aim world point
4. This direction is the base firing direction before spread is applied

### Fire Origin vs Muzzle Point (Hybrid Approach)

The system uses two separate points with distinct responsibilities:

**Fire origin (logic):** A fixed point in the character's chest/shoulder area. This is where the hitscan raycast starts. The fire origin does not move when the aim angle changes — it is a stable child transform on the character, affected only by character position and flip. This stability eliminates edge cases with geometry clipping: the origin is always inside the character's body, never inside a wall or behind a platform.

**Muzzle point (visual):** The tip of the weapon barrel. This is where the tracer VFX and muzzle flash are drawn. The muzzle point moves with the weapon and arm rotation, reflecting the current aim angle. In the future, when the character has skeletal animation with arm aiming, the muzzle will follow the hand bone — the fire origin will remain at the shoulder.

**Why separate:** Casting the ray from the muzzle creates instability — the muzzle moves with aim angle and can end up inside walls, behind platforms, or outside the character's collider. Casting from the character center is too far from the weapon visually. The shoulder/chest area is a stable compromise: close enough to the weapon that the angular difference is negligible at gameplay distances, stable enough that geometry clipping is not an issue.

**For the prototype:** The character is a simple rectangle. The fire origin is a child transform offset to the upper body area. The muzzle point is either the same transform (if no arm rotation) or a separate offset at the edge of the character facing the aim direction. The visual difference between the two points is minimal at this stage — the architecture exists to support future arm animation without refactoring the shooting logic.

**Aim direction stability:** The aim direction is recalculated every frame. There is no smoothing or interpolation — the weapon always points directly at the current cursor position. This gives immediate, responsive aim feel appropriate for a 2D shooter.

---

## Character Facing (Flip)

Character facing is determined solely by the horizontal relationship between the cursor and the character.

**Rule:** `cursorWorldPosition.x >= character.position.x` → face right. Otherwise → face left.

**Implementation:** Flip is achieved by setting `transform.localScale.x` to `+1` (right) or `-1` (left). This flips the entire visual hierarchy including both the fire origin and the muzzle point, which ensures they stay on the correct side of the character.

**Relationship with movement:** The movement system (`CharacterMover2D`) currently does not control character facing. If it does contain flip logic, that logic must be removed — the aim system is the single source of truth for character direction. The character can walk left while aiming right (backpedaling).

**Edge case — cursor directly above/below character:** When `cursorWorldPosition.x` is exactly equal to `character.position.x`, the character maintains its current facing direction (no flip). In practice this is extremely rare with floating-point mouse positions.

---

## Firing Modes

Every weapon operates in one of two firing modes. The mode determines how LMB input translates into shot attempts. How often a weapon can actually fire is controlled by a separate parameter — `FireRate`.

### Fire Rate

**`FireRate`** (int, rounds per minute) — the single parameter controlling how fast any weapon can shoot. Defined per weapon in configuration.

**`FireInterval`** (computed, read-only) — `60f / FireRate`. The minimum time in seconds between two consecutive shots. Used internally by the fire timer. Not set by the designer — derived from `FireRate`, same pattern as `DodgeTime = DodgeDistance / DodgeSpeed`.

Examples:
- Sniper Rifle: `FireRate = 40` → `FireInterval = 1.5s` — slow, deliberate
- Assault Rifle: `FireRate = 600` → `FireInterval = 0.1s` — rapid sustained fire
- Pistol: `FireRate = 1200` → `FireInterval = 0.05s` — effectively as fast as the player can click

The fire timer applies universally to all weapons regardless of firing mode. Even semi-automatic weapons have a `FireInterval` — this prevents exploits via autoclickers or macros while being imperceptible to human input at high RPM values.

### Semi-automatic

**Input:** Each individual LMB press fires one shot. Holding LMB does not produce additional shots — the player must release and press again.

**Fire rate interaction:** If the player clicks faster than `FireInterval` allows, excess clicks are ignored. At high `FireRate` values (1000+ RPM) this limit is imperceptible to human players but prevents mechanical abuse.

**Trigger discipline:** A press during reload does not queue a shot — the reload must complete (or be interrupted) before the weapon can fire.

**Used by:** Pistol, Shotgun, Sniper Rifle.

### Automatic

**Input:** Holding LMB produces continuous fire at the weapon's `FireRate`. Releasing LMB stops firing immediately.

**Fire rate interaction:** The weapon tracks time since the last shot; if LMB is held and `FireInterval` has elapsed, the next shot fires. The first shot on LMB press fires immediately (no initial delay).

**Used by:** Assault Rifle, SMG, Machine Gun.

---

## Hitscan Model

All weapons use instant raycasts for hit detection. Three hitscan variants exist, differing in how many rays are cast and whether they stop on first contact.

### Single Hitscan

One shot = one ray from the fire origin along the spread-adjusted aim direction. The ray stops on the first valid target hit.

**Used by:** Pistol, Assault Rifle, SMG, Machine Gun.

### Spread Hitscan (Shotgun)

One shot = multiple rays, each aimed in a random direction within a cone centered on the aim direction. Each ray is an independent hitscan that checks for hits separately.

**Cone angle:** Defined per weapon. Each pellet's direction is the base aim direction rotated by a random angle within `[-ConeHalfAngle, +ConeHalfAngle]`.

**Pellet count:** Defined per weapon as a configurable parameter. The exact count is an open balance question — to be tuned during testing.

**Independent hits:** Each pellet can hit a different target. A single shotgun blast aimed at a group of enemies can damage multiple enemies simultaneously. Conversely, at point-blank range against a single target, all pellets may hit the same enemy for devastating total damage.

**Interaction with spread — two-stage calculation:** The shotgun extends the same accuracy system used by all weapons rather than replacing it. For each pellet:
1. **Cone offset:** A random angle within `[-ConeHalfAngle, +ConeHalfAngle]` determines the pellet's position within the inherent cone pattern
2. **Spread offset:** A random angle within `[-FinalSpread, +FinalSpread]` (same calculation as single-hitscan weapons) is added on top

The pellet's final direction = base aim direction rotated by `(coneOffset + spreadOffset)`.

`ConeHalfAngle` and `MinSpreadAngle` are separate parameters. `ConeHalfAngle` is the geometry of the pellet group — how wide the pattern is inherently. `MinSpreadAngle` is the baseline accuracy of each individual pellet. A shotgun with `MinSpreadAngle = 0` in Idle with no recoil produces pellets distributed strictly within the cone. A non-zero `MinSpreadAngle` adds per-pellet wobble on top of the cone pattern even under ideal conditions.

### Penetrating Hitscan (Sniper Rifle)

One shot = one ray that does not stop on the first enemy hit. The ray passes through all enemies along its path and checks for hits on each one. All hit enemies receive damage.

**Damage per target:** Fixed — no damage falloff through penetrating targets. Each enemy hit takes the full weapon damage.

**Layer interaction:** The penetrating ray passes through enemies but stops on solid geometry (Ground layer). One-way platforms do not block the ray.

**Used by:** Sniper Rifle.

---

### Hitscan Common Properties

**Raycast layers:** A single shared `HitscanLayerMask` (serialized, configured in Inspector) defines what all weapon raycasts interact with. The mask must include enemy colliders and solid geometry (Ground layer), and exclude the player's own collider. One-way platforms (Platform layer) do not block hitscan rays. All weapons use the same mask — no per-weapon overrides.

**Raycast range:** Each weapon has a maximum range (`MaxRange`). The raycast distance is capped to this value. Targets beyond `MaxRange` are not hit. For the prototype, range values can be generous — this parameter exists for future tuning rather than as an immediate balance lever.

---

## Spread & Accuracy

Accuracy is modeled as a **spread angle** — a cone of possible firing directions centered on the aim direction. Each shot's actual direction is the aim direction rotated by a random angle within the current spread. Larger spread = less predictable shots.

### Spread State Lifecycle

The spread system tracks a single value: `CurrentSpread` (in degrees). This value changes based on three factors: recoil from firing, time-based recovery, and movement state penalties.

**Recoil (on each shot):**
```
CurrentSpread = Min(CurrentSpread + RecoilPerShot, MaxSpreadAngle)
TimeSinceLastShot = 0
```
Each shot adds a fixed amount to the current spread, clamped to the weapon's maximum. The recovery timer resets.

**Recovery (continuous, when not firing):**
```
if TimeSinceLastShot >= RecoveryDelay:
    CurrentSpread = Max(CurrentSpread - RecoveryRate * deltaTime, MinSpreadAngle)
```
After a pause in shooting (`RecoveryDelay` seconds), spread decreases at a constant rate until it reaches the weapon's base minimum. The first shot after full recovery is the most accurate.

**Weapon switch reset:** When the player switches to a different weapon, the previous weapon's `CurrentSpread` resets to `MinSpreadAngle`. This means switching away from a heated weapon and back gives a fresh start.

> **Known trade-off (prototype):** This creates a potential exploit where rapid weapon switching resets recoil. Accepted for the prototype. If draw/holster time is introduced post-prototype, the animation delay naturally mitigates this. If instant switching remains, the reset may be replaced with per-weapon spread persistence.

**Spread distribution:** Shot deviation within the spread angle uses uniform random distribution (`Random.Range`). Each angle within `[-FinalSpread, +FinalSpread]` is equally likely. This is a deliberate prototype simplification — post-prototype, the system will migrate to a normal (Gaussian) distribution where most shots land near center and outliers are rare, improving game feel and reducing frustration from frequent max-deviation shots.

**Movement penalty (applied on each shot, not accumulated):**
```
FinalSpread = CurrentSpread + MovementPenalty(currentState)
```
The movement penalty is a flat additive bonus based on the character's current movement state. It is applied at the moment of firing to determine the final spread angle for that shot. It does not accumulate into `CurrentSpread` — if the character stops moving, the penalty disappears immediately.

### Movement Penalty Table

| Character State | Penalty |
|---|---|
| Idle (grounded, no input) | 0 |
| Walk | Small |
| Run | Medium |
| Airborne (Jump or Fall) | Maximum |

> Concrete values are per-weapon parameters, defined in configuration. "Small / Medium / Maximum" indicate relative magnitude — exact degrees are a balance question.

### Spread Application to Shot Direction

**Single hitscan weapons** (one ray per shot):
1. Compute `FinalSpread = CurrentSpread + MovementPenalty`
2. Generate a random angle: `spreadOffset = Random.Range(-FinalSpread, +FinalSpread)`
3. Rotate the base aim direction by `spreadOffset`
4. Cast the hitscan ray along the rotated direction

**Shotgun** (multiple rays per shot) — extends the same system with one additional step:
1. Compute `FinalSpread = CurrentSpread + MovementPenalty`
2. For each pellet:
   a. `coneOffset = Random.Range(-ConeHalfAngle, +ConeHalfAngle)` — position within the pellet cone
   b. `spreadOffset = Random.Range(-FinalSpread, +FinalSpread)` — accuracy deviation
   c. Rotate the base aim direction by `(coneOffset + spreadOffset)`
   d. Cast the hitscan ray along the rotated direction

### Spread Parameters (per weapon)

| Parameter | Description |
|---|---|
| `MinSpreadAngle` | Base spread with no recoil. 0° = perfect first-shot accuracy (Sniper). Higher for less precise weapons |
| `MaxSpreadAngle` | Maximum spread cap. Recoil cannot push spread beyond this |
| `RecoilPerShot` | Degrees added per shot fired |
| `RecoveryDelay` | Seconds of not firing before recovery begins |
| `RecoveryRate` | Degrees per second of spread reduction during recovery |
| `WalkSpreadPenalty` | Flat penalty when walking |
| `RunSpreadPenalty` | Flat penalty when running |
| `AirSpreadPenalty` | Flat penalty when airborne |
| `ConeHalfAngle` | Shotgun only. Half-angle of the pellet cone. 0 for all non-shotgun weapons |
| `PelletCount` | Shotgun only. Number of hitscan rays per shot. 1 for all non-shotgun weapons |

---

## Ammo & Reload

### Magazine + Reserve

Each weapon has two ammo pools:
- **Magazine** — rounds available for immediate firing. Size defined per weapon (`MagazineSize`)
- **Reserve** — backup ammo for the weapon's slot. Reloading transfers rounds from reserve to magazine

**Reserve limit:** Each slot has a maximum reserve capacity (`MaxReserve`). Ammo pickups that would exceed this limit are capped — excess rounds are lost. This prevents stockpiling in late waves and maintains resource pressure.

Ammo is tied to the **slot category**, not the specific weapon. Swapping one assault rifle for another in slot 4 preserves the slot's reserve ammo.

**Pickup into empty slots:** If the player picks up ammo for a slot that has no weapon equipped, the ammo is stored in the reserve for that slot. When a weapon is later purchased into that slot, the accumulated reserve is available immediately.

### Reload Behavior

**Trigger — manual:** Player presses R. Reload begins if the magazine is not full and the reserve has ammo.

**Trigger — auto on dry fire:** Player presses LMB with an empty magazine (0 rounds) but ammo exists in the reserve. The weapon begins reloading automatically instead of firing. No shot is produced on this press.

**No auto-reload on empty:** When the magazine empties during firing, the reload does NOT begin automatically. The player can continue switching weapons, dodging, or repositioning before choosing to reload.

**Duration:** Each weapon has a fixed `ReloadTime`. The reload is an uninterruptible-from-within timer — there is no reload "progress bar" that saves partial completion.

**Interruption:** Two actions interrupt a reload in progress:
- **Dodge** — the dodge takes priority; reload progress is lost
- **Weapon switch** — switching to another slot cancels the reload; progress is lost

After interruption, the magazine retains whatever count it had before the reload started. No rounds are transferred until the reload completes fully.

**Allowed states:** Reloading is permitted while walking, running, jumping, and falling. Movement does not interrupt or slow the reload. Only dodge and weapon switch interrupt it.

> **Hypothesis for post-prototype:** Running might interrupt reload to add tactical weight to the run/walk distinction. Not implemented in the prototype.

**Cannot fire during reload:** LMB input during an active reload is ignored. The reload must complete or be interrupted before the weapon can fire.

### Starter Pistol — Infinite Reserve

The starter pistol (slot 1, beginning of every run) has a special property: its reserve is infinite. The magazine still has a fixed size and must be reloaded manually via R — the infinite reserve simply means the player never runs out of ammo to reload with.

All other pistols purchased in the shop have finite reserves. The infinite reserve is exclusive to the starter pistol.

### Empty Weapon Behavior

When both magazine and reserve are at zero — the weapon is fully empty. The player continues holding the weapon. No automatic switch to another weapon occurs. Pressing LMB produces no effect (no shot, no reload).

> **Post-prototype:** a dry-fire sound effect (click of an empty chamber) for this situation, giving the player clear audio feedback that the weapon is empty.

---

## Damage Model

### Base Damage

Each weapon has a flat `Damage` value per hit. No distance-based falloff — a hit at point-blank and at maximum range deals the same damage.

### Critical Hits

A critical hit occurs when the hitscan ray hits an enemy's **weak spot** (for most enemies — the head). The weak spot is a separate collider or collider region on the enemy.

**Critical multiplier:** 2x base damage. This is a starting value for balance testing — may be adjusted.

**Applies per hit:** For the shotgun, each pellet that hits a weak spot gets the critical multiplier independently. A pellet hitting the body deals normal damage; a pellet hitting the head deals 2x.

### Shotgun Damage Calculation

The shotgun defines its damage through two parameters:
- `TotalDamage` — the total damage output if all pellets hit a single target
- `PelletCount` — the number of hitscan rays per shot

Individual pellet damage is computed: `PelletDamage = TotalDamage / PelletCount`.

This configuration approach lets the designer set the intended point-blank burst damage directly, without having to mentally multiply per-pellet damage by pellet count. Adjusting `PelletCount` automatically rebalances per-pellet damage to keep total output consistent.

---

## Weapon Switching

Switching between weapon slots is done via number keys (1–6). Each key corresponds to a fixed slot.

**Speed:** Instant for the prototype. No draw/holster animation or delay. The weapon changes immediately on key press.

**During reload:** Switching weapons during a reload cancels the reload (progress lost). The newly selected weapon is immediately ready.

**Spread reset:** Switching weapons resets the previous weapon's spread to its `MinSpreadAngle`. See the Spread & Accuracy section for the trade-off rationale.

**During dodge:** Weapon switching during a dodge is not restricted, but since shooting is blocked during dodge, the practical effect is limited to preparation.

**Switching to the same slot:** Pressing the key of the already-active slot has no effect.

**Empty slot:** Pressing a key for a slot with no weapon equipped has no effect.

> **Post-prototype consideration:** Draw/holster time may be introduced if testing shows that instant switching removes too much tactical weight from weapon choice. This would add a brief delay between pressing the key and the weapon being ready to fire.

---

## Visual Feedback

### Dynamic Crosshair

The crosshair is a UI element that visualizes the current effective spread angle.

**Behavior:** The crosshair lines spread outward as the spread angle increases and contract as it decreases. At minimum spread, the crosshair is tight. At maximum spread, it is wide. The crosshair reflects `FinalSpread` (including movement penalty), so it visually reacts to both firing and movement state changes.

**Position:** The crosshair follows the mouse cursor position. It replaces the system cursor.

> **Open question:** Exact crosshair visual style (lines, circle, dot + lines, etc.) is an art/UI decision for later. For the prototype, a simple four-line crosshair that expands/contracts is sufficient.

### Muzzle Flash

A visual effect at the fire origin (weapon muzzle) that plays on every shot. Brief, bright, scaled to weapon type (larger for shotgun, smaller for pistol).

> For the prototype, a simple sprite flash or particle burst is sufficient.

### Tracers

A visual line from the muzzle to the hit point (or maximum range if no hit) that appears briefly on each shot. The tracer is cosmetic — it does not represent a physical projectile and has no collision.

**Duration:** Very brief (a few frames), visible enough to give directional feedback without cluttering the screen during automatic fire.

> For the prototype, a simple `LineRenderer` or raycast debug line is sufficient.

### No Camera Shake

Camera shake on firing is explicitly excluded from the design. Recoil feel is communicated entirely through crosshair spread and visual effects.

---

## Weapon Archetypes

Six weapon archetypes, each designed for a specific tactical role against the enemy roster.

| Archetype | Slot | Fire Mode | Hitscan Type | Designed Against |
|---|---|---|---|---|
| **Pistol** | 1 | Semi-auto | Single | Universal fallback |
| **Shotgun** | 2 | Semi-auto | Spread | Breacher, close-range singles |
| **SMG** | 3 | Automatic | Single | Fodder swarms, Breacher |
| **Assault Rifle** | 4 | Automatic | Single | Mixed groups, Gunner at mid-range |
| **Sniper Rifle** | 5 | Semi-auto (low RPM) | Penetrating | Buffer in crowds, Gunner at range |
| **Machine Gun** | 6 | Automatic | Single | Tank, dense crowds |

### Archetype Spread Profiles (Relative)

| Archetype | Min Spread | Recoil/Shot | Max Spread | Recovery |
|---|---|---|---|---|
| **Pistol** | Medium | Medium | Medium-high | Fast |
| **Shotgun** | *(cone-based)* | Low | *(cone-based)* | Fast |
| **SMG** | Low | Medium | High | Fast |
| **Assault Rifle** | Low | Medium | Medium | Medium |
| **Sniper Rifle** | Minimal (≈0) | High | Medium | Slow |
| **Machine Gun** | Medium | Low | High | Slow |

> Concrete values are balance questions. This table defines the intended relative feel — not exact numbers.

---

## Interaction with Movement System

The shooting system reads state from `CharacterMover2D` but does not modify it.

**Data read from CharacterMover2D:**
- **Velocity** — used to determine movement state for spread penalties. `velocity == 0` → Idle, horizontal velocity within walk range → Walk, horizontal velocity within run range → Run
- **IsGrounded** — combined with velocity to determine airborne state for maximum spread penalty

> **Open question:** Should movement state for spread purposes be read from velocity magnitude, or from the actual FSM sub-state name (Idle/Walk/Run/Jump/Fall)? Reading velocity is decoupled from FSM internals. Reading sub-state is more precise but creates a dependency on the movement FSM's state names. Velocity-based approach is preferred for prototype — revisit if edge cases appear.

**Dodge blocks shooting:** When `DodgeState` is active, the shooting system must not fire. The movement system signals this either through a public `IsDodging` property or through the state name.

---

## Prototype Scope

### Included in Prototype

Three archetypes covering all core mechanics:

| Archetype | Covers |
|---|---|
| **Starter Pistol** (slot 1) | Semi-auto fire mode, infinite reserve, single hitscan |
| **Assault Rifle** (slot 4) | Automatic fire mode, magazine + reserve, spread buildup over sustained fire |
| **Shotgun** (slot 2) | Semi-auto fire mode, spread hitscan (multiple rays), high burst damage |

These three cover: both fire modes (semi-auto, automatic), two of three hitscan types (single, spread), and the full ammo/reload loop.

### Deferred

| Feature | Reason |
|---|---|
| Sniper Rifle | Requires penetrating hitscan — a unique mechanic not shared by other prototype weapons. Fire mode (semi-auto with low RPM) works with existing system |
| SMG, Machine Gun | Mechanically similar to Assault Rifle (automatic + single hitscan) — add when weapon variety is needed |
| Draw/holster time | Instant switching is simpler; tactical impact to be evaluated during playtesting |
| Partial reload | Reserved for revolvers and pump shotguns — not needed for magazine-based prototype weapons |
| Dry-fire sound | Audio polish, not core mechanic |
| Enemy hit reaction | Knockback/hitstun — gameplay impact unclear, add after core feel is established |
| Crosshair art | Prototype uses simple expanding lines; final art is a separate phase |
| Normal spread distribution | Uniform distribution for prototype; Gaussian distribution post-prototype for better game feel |

---

## Open Questions

> Collected from all sections. To be resolved during implementation or testing.

- [x] ~~Fire origin — weapon muzzle position vs character center~~ *(closed — hybrid: raycast from chest/shoulder, visuals from muzzle)*
- [ ] Shotgun pellet count — needs playtesting to find the right feel
- [x] ~~Shotgun cone + spread interaction — combined single angle vs two layered angles~~ *(closed — two-stage: cone offset + spread offset, ConeHalfAngle and MinSpreadAngle are separate parameters)*
- [ ] Movement state detection — velocity-based vs FSM sub-state name
- [x] ~~HitscanLayerMask — single shared mask vs per-weapon overrides~~ *(closed — single shared mask for all weapons)*
- [ ] Concrete spread/damage values for all archetypes — pure balance work
- [ ] Critical hit multiplier — starting at 2x, may need adjustment
- [ ] Crosshair visual style — functional placeholder for prototype, art pass later

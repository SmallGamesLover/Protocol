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


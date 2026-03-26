# Composition Root — Tasks
> Based on Composition Root Implementation Plan v1.0.0
> Tasks are ordered chronologically. Complete each phase top-to-bottom before moving to the next.

- [x] Phase 0: PlayerCompositionRoot stub
  - [x] 1. Create folder `Runtime/Core/` under the project scripts root
  - [x] 2. Create `PlayerCompositionRoot` MonoBehaviour in `Runtime/Core/` with namespace `SGL.Protocol.Runtime.Core`. Empty `Awake()` body, no fields
  - [x] 3. Attach `PlayerCompositionRoot` to the player GameObject in the test scene. Verify the project compiles and existing movement behavior is unchanged

- [x] Phase 1: Migrate CharacterMover2D
  - [x] 4. In `CharacterMover2D`: add `private bool _initialized` field (default `false`)
  - [x] 5. In `CharacterMover2D`: add early return guard `if (!_initialized) return;` at the top of `FixedUpdate()`. If any other Unity lifecycle methods contain logic (e.g., `OnDrawGizmos`), add the guard there too only if they reference `_initialized`-dependent state. `OnDrawGizmos` for ground/ceiling check boxes may work without initialization — evaluate per method
  - [x] 6. In `CharacterMover2D`: add public method `Initialize(WalkingConfig walkingConfig, DodgeConfig dodgeConfig)` with empty body. Set `_initialized = true` at the end
  - [x] 7. In `CharacterMover2D`: move all logic from `Awake()` into `Initialize()` body, before the `_initialized = true` line. Replace references to the old `[SerializeField]` config fields with the `Initialize` parameters. Remove `Awake()` method (or leave empty body)
  - [x] 8. In `CharacterMover2D`: change `[SerializeField] private WalkingConfig` and `[SerializeField] private DodgeConfig` to plain private fields: `private WalkingConfig _walkingConfig;` and `private DodgeConfig _dodgeConfig;`. Assign them from `Initialize` parameters at the top of the method body. Update all internal references from the old field names to the new ones
  - [x] 9. In `PlayerCompositionRoot`: add `[Header("Movement")]`, `[SerializeField] private WalkingConfig WalkingConfig`, `[SerializeField] private DodgeConfig DodgeConfig`
  - [x] 10. In `PlayerCompositionRoot.Awake()`: add `var mover = GetComponent<CharacterMover2D>();` and `mover.Initialize(WalkingConfig, DodgeConfig);`
  - [x] 11. In Inspector: assign `WalkingConfig` and `DodgeConfig` asset references on `PlayerCompositionRoot` (they were lost when `[SerializeField]` was removed from `CharacterMover2D`)
  - [x] 12. Verify: character moves, jumps, dodges, drops through platforms identically to before. Config tweaking in Play Mode still works. No `[SerializeField]` config fields remain on `CharacterMover2D`

- [x] Phase 2: Migrate PlayerInputReader
  - [x] 13. In `PlayerInputReader`: add `private bool _initialized` field (default `false`)
  - [x] 14. In `PlayerInputReader`: add early return guard `if (!_initialized) return;` at the top of `Update()`
  - [x] 15. In `PlayerInputReader`: add public method `Initialize(CharacterMover2D mover)`. Store in `private CharacterMover2D _mover;`. Set `_initialized = true` at the end
  - [x] 16. In `PlayerInputReader`: remove the existing mechanism that provides the `CharacterMover2D` reference — whether `[SerializeField]`, `GetComponent` in `Awake()`, or lookup in `Start()`. Remove `Awake()`/`Start()` if they become empty. All references to the mover now use the `_mover` field set by `Initialize`
  - [x] 17. In `PlayerCompositionRoot.Awake()`: add `var inputReader = GetComponent<PlayerInputReader>();` and `inputReader.Initialize(mover);` after the `mover.Initialize(...)` call
  - [x] 18. Verify: A/D movement, Space jump, Shift dodge, S drop-through all work. `PlayerInputReader` has no `[SerializeField]` or `GetComponent` reference to `CharacterMover2D`

- [x] Phase 3: Migrate MovementDebugOverlay
  - [x] 19. In `MovementDebugOverlay`: add `private bool _initialized` field (default `false`). Field is NOT wrapped in `#if UNITY_EDITOR` — it must exist in builds to avoid serialization mismatch
  - [x] 20. In `MovementDebugOverlay`: add public method `Initialize(CharacterMover2D mover)`. Inside `#if UNITY_EDITOR` block: store in `private CharacterMover2D _mover;`, set `_initialized = true`. In `#else`: empty body. Field declaration for `_mover` stays inside `#if UNITY_EDITOR`
  - [x] 21. In `MovementDebugOverlay`: add `!_initialized` to early return guards in `Update()`, `OnGUI()`, and `OnDrawGizmos()` method bodies (inside existing `#if UNITY_EDITOR` blocks)
  - [x] 22. In `MovementDebugOverlay`: remove the existing mechanism that provides the `CharacterMover2D` reference — whether `[SerializeField]` or `GetComponent` in `Awake()`. Remove `Awake()` if it becomes empty
  - [x] 23. In `PlayerCompositionRoot.Awake()`: add `var debugOverlay = GetComponent<MovementDebugOverlay>();` and `debugOverlay.Initialize(mover, WalkingConfig);` after the `inputReader.Initialize(...)` call. In `MovementDebugOverlay`: remove `[SerializeField] private WalkingConfig WalkingConfig` field and add `WalkingConfig walkingConfig` parameter to `Initialize()`. Move `_walkingConfig` field declaration inside `#if UNITY_EDITOR`. Assign from parameter inside the `#if UNITY_EDITOR` block. Update `RebuildOverlayLines()` to use `_walkingConfig` instead of the old field
  - [x] 24. Verify: debug overlay displays all data correctly, F1 toggle works, velocity gizmo visible in Scene View. `MovementDebugOverlay` has no `[SerializeField]` or `GetComponent` reference to `CharacterMover2D` or `WalkingConfig`.

- [x] Phase 4: Documentation
  - [x] 25. Add "Composition Root" section to `Architecture.md` — pattern description, scope, SerializeField vs Initialize rule, initialization guard, folder location. Place between "Active Patterns" and "Open Decisions / Planned Work"
  - [x] 26. Add "Composition Root (Dependency Wiring)" entry to the "Active Patterns" section in `Architecture.md`
  - [x] 27. Update "Folder & Assembly Structure" in `Architecture.md`: add `Runtime/Core/` with `PlayerCompositionRoot.cs`
  - [x] 28. Update "Class Map" in `Architecture.md`: add `PlayerCompositionRoot` entry, update `CharacterMover2D`, `PlayerInputReader`, `MovementDebugOverlay` entries to reflect `Initialize()` pattern

- [ ] Phase 5: Full regression verification
  - [ ] 29. Walk left/right with acceleration and deceleration, run with Shift
  - [ ] 30. Jump: full height, low jump (short press), coyote time off edge, jump buffer on landing
  - [ ] 31. Fall: asymmetric rise/fall, `MaxFallSpeed` cap, air control mid-air
  - [ ] 32. Dodge: from Idle, Walk, Run, Jump, Fall. Into wall. Off platform edge
  - [ ] 33. One-way platforms: jump through from below, land from above, drop-through on S, cascade through stacked platforms, drop-through on solid ground does nothing
  - [ ] 34. Ceiling check: stops ascent, does not trigger on one-way platforms from below
  - [ ] 35. Debug overlay: FSM state, velocity, timers, flags all display. F1 toggles both. Velocity gizmo matches movement
  - [ ] 36. Play Mode config tweaking: change ScriptableObject values during play — applies immediately
  - [ ] 37. Code audit: `CharacterMover2D` has no `Awake()`/`Start()` with logic. `PlayerInputReader` has no self-resolved mover reference. `MovementDebugOverlay` has no self-resolved mover reference. `PlayerCompositionRoot.Awake()` is the single initialization entry point

# Protocol — Unity 6.3 Project

## Project Overview
Project for developing 2d indie game. Main genre: side-scroller shooter.
Engine: Unity 6.3 | 2D URP | Language: C# | IDE: VS Code

## Key Directories
- `Assets/_Project/` folder is the top-level container for all project-owned content.
- `Docs/` folder contains all design and planning documents.
- `Docs/_GDD.md` file contains the main GDD document for the entire project.
- `Docs/_Index.md` file contains the current working status of the project. Here you can see what feature we are currently building.
- `Docs/Architecture.md` file contains important core information about the project architecture.
- `Docs/Features/<FeatureName>/*GDD*.md` file contains the GDD document for a feature.
- `Docs/Features/<FeatureName>/*Plan*.md` file contains the implementation plan for a feature.
- `Docs/Features/<FeatureName>/*Tasks*.md` file contains specific small tasks to complete when implementing a feature.
- `Docs/Features/<FeatureName>/*Notes*.md` file contains notes about a feature's GDD or implementation. Here you can write all the edits made along the way. Also feel free to write anything you think is worth noting: concerns about the current implementation, suggestions, comments, etc.

## Code Style (C#)
- Use PascalCase for classes and public members
- Use _camelCase for private fields
- Try to follow SOLID principles (that includes preferring interfaces (ITickable, IState) over abstract classes when possible)
- XML doc comments on all public API methods
- No magic numbers — use named constants or ScriptableObjects

## Folder Placement Rules
- `Runtime/` — gameplay code that runs only in the player build (MonoBehaviours, states, configs)
- `Shared/` — pure C# code reusable across Runtime AND Editor (design patterns, interfaces, generic utilities). No Unity engine dependencies. Example: `Shared/FSM/` contains `IState`, `ITickable`, `StateMachine<T>`
- `Editor/` — Unity Editor tooling only (custom inspectors, drawers, wizards)
- `Tests/` — unit and integration tests, mirroring the assembly they cover (`Tests/Shared/`, `Tests/Runtime/`)
- When in doubt whether code belongs in `Runtime/` or `Shared/`: if it has no `using UnityEngine` and could be used in an Editor script, it goes in `Shared/`

## Namespace Convention
Namespace must mirror the folder path under `Scripts/`.
Pattern: `SGL.Protocol.{Assembly}.{Subfolder}`.
Example: a file at `Scripts/SGL/Protocol/Runtime/Movement/CharacterMover2D.cs` → namespace `SGL.Protocol.Runtime.Movement`.
Do not hardcode namespaces in task descriptions — derive them from the file's folder location.

## Unity Conventions
- IMPORTANT: Never use Find(), FindObjectOfType() in Update()
- When private field has [SerializeField] you should use PascalCase
- Dependency wiring: see "Composition Root" section below
- ScriptableObjects for shared data (stats, config)
- Use UnityEvents sparingly — prefer C# events/Actions

## Composition Root

Components do not initialize themselves. All dependency wiring happens in Composition Root scripts.

### Adding a New Component with Dependencies

1. Add `public void Initialize(...)` method that receives all dependencies as parameters.
2. Add `private bool _initialized` field. Set `true` at the end of `Initialize()`.
3. Add `if (!_initialized) return;` guard at the top of every Unity lifecycle method (`Update`, `FixedUpdate`, `OnGUI`, etc.).
4. Do NOT add `Awake()` or `Start()` with initialization logic. If the component needs no dependencies, it does not need `Initialize()` or a Composition Root entry.
5. Register the component in the appropriate Composition Root (`PlayerCompositionRoot`, `SceneCompositionRoot`). Call `Initialize()` in the correct position based on the dependency graph.

### What Goes Where

- **Initialize parameters:** ScriptableObject configs (`WalkingConfig`, `DodgeConfig`), references to other components (`CharacterMover2D`). These are dependencies — things that differ by context or that the component cannot function without.
- **[SerializeField] on the component:** physics check geometry (`GroundCheckSize`, `GroundCheckOffset`), LayerMasks, visual/debug settings (`VelocityGizmoScale`). These are instance configuration — tuned per-GameObject in Inspector.
- **GetComponent inside Initialize:** `Rigidbody2D`, `BoxCollider2D`, and other components on the same GameObject. These are internal implementation details — the Composition Root does not pass them.
- **[SerializeField] on Composition Root:** config asset references that the root distributes to components via `Initialize()`.

### Config Ownership

ScriptableObject configs are `[SerializeField]` on the Composition Root, NOT on the components that use them. The Composition Root is the single point that knows which config goes where.

### Initialization Order

In `PlayerCompositionRoot.Awake()`, `Initialize()` calls are ordered by dependency graph — top to bottom. A component is never initialized before its dependencies. When adding a new component, insert its `Initialize()` call after all components it depends on.

### Do NOT

- Do not use `Awake()` or `Start()` for dependency resolution (`GetComponent` for cross-component refs, `Find`, `FindObjectOfType`).
- Do not add `[SerializeField]` config fields to components that receive configs via `Initialize()`.
- Do not pass `Rigidbody2D` or `BoxCollider2D` through `Initialize()` — use `GetComponent` inside the method.
- Do not use Script Execution Order to control initialization between components.

## What Claude Should NOT Do
- Do not create, modify, delete, or overwrite any non-code assets: scenes (`.unity`), prefabs (`.prefab`), ScriptableObject instances (`.asset`), sprites/textures, models, animations, audio, materials, or shader files
- Do not modify `.meta` files directly
- Do not modify ProjectSettings or any Editor configuration files
- Do not create files outside `Assets/` without asking
- Do not use deprecated Unity APIs (e.g. `GUI.*`, legacy `UnityEngine.Input`)
- If a task requires non-code changes (creating a scene, attaching components in Inspector, setting up layers, creating a ScriptableObject asset instance), STOP and ask the user to perform those steps manually — describe clearly what needs to be done, then continue with code once confirmed
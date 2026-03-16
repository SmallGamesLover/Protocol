# Protocol — Unity 6.3 Project

## Project Overview
Project for developing 2d indie game. Main genre: side-scroller shooter.
Engine: Unity 6.3 | 2D URP | Language: C# | IDE: VS Code

## Architecture
- 
-
-

## Key Directories
- `Assets/_Project/` folder is the top-level container for all project-owned content.
- `Docs/` folder contains all design and planning documents.
- `Docs/_GDD.md` file contains the main GDD document for the entire project.
- `Docs/_Index.md` file contains the current working status of the project. Here you can see what feature we are currently building.
- `Docs/Architecture.md` file contains important core information about the project architecture.
- `Docs/Features/<FeatureName>/*GDD*.md` file contains the GDD document for a feature.
- `Docs/Features/<FeatureName>/*Plan*.md` file contains the implementation plan for a feature.
- `Docs/Features/<FeatureName>/*Tasks*.md` file contains specific small tasks to complete when implementing a feature.
- `Docs/Features/<FeatureName>/Notes.md` file contains notes about a feature's GDD or implementation. Here you can write all the edits made along the way. Also feel free to write anything you think is worth noting: concerns about the current implementation, suggestions, comments, etc.

## Code Style (C#)
- Use PascalCase for classes and public members
- Use _camelCase for private fields
- Try to follow SOLID principles (that includes preferring interfaces (ITickable, IState) over abstract classes when possible)
- XML doc comments on all public API methods
- No magic numbers — use named constants or ScriptableObjects

## Namespace Convention
Namespace must mirror the folder path under `Scripts/`.
Pattern: `SGL.Protocol.{Assembly}.{Subfolder}`.
Example: a file at `Scripts/SGL/Protocol/Runtime/Movement/CharacterMover2D.cs` → namespace `SGL.Protocol.Runtime.Movement`.
Do not hardcode namespaces in task descriptions — derive them from the file's folder location.

## Unity Conventions
- IMPORTANT: Never use Find(), FindObjectOfType() in Update()
- When private field has [SerializeField] you should use PascalCase
- Prefer dependency injection via [SerializeField] or Initialize() methods
- ScriptableObjects for shared data (stats, config)
- Use UnityEvents sparingly — prefer C# events/Actions

## What Claude Should NOT Do
- Do not create, modify, delete, or overwrite any non-code assets: scenes (`.unity`), prefabs (`.prefab`), ScriptableObject instances (`.asset`), sprites/textures, models, animations, audio, materials, or shader files
- Do not modify `.meta` files directly
- Do not modify ProjectSettings or any Editor configuration files
- Do not create files outside `Assets/` without asking
- Do not use deprecated Unity APIs (e.g. `GUI.*`, legacy `UnityEngine.Input`)
- If a task requires non-code changes (creating a scene, attaching components in Inspector, setting up layers, creating a ScriptableObject asset instance), STOP and ask the user to perform those steps manually — describe clearly what needs to be done, then continue with code once confirmed
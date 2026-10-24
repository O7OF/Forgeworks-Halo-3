# ForgeWorks Studio - Halo 3 Blam! Engine Desktop Editor (v2.2.0)

An all-in-one desktop environment for Halo 3 modding and level creation combining:
- **Procedural Level Design & 3D Blockout**: Rapid CSG brush work, corridor/room generation, advanced terrain modifiers, and procedural rock/foliage scattering.
- **Universal Geometry Pipeline**: Multi-format 3D asset importer (`.obj`, `.fbx`, `.gltf`, `.glb`, `.ass`, `.jms`, `.rmf`, `.map`, `.dae`, `.stl`, `.ply`, `.3ds`) with $O(N)$ spatial grid linting, robust N-gon fan triangulation, and high-poly stability.
- **Tag Pipeline & Reflection**: Direct tag introspection, mutation, and MCC-compliant format export (`.ass`, `.jms`, `.scenario`).
- **Engine-Native Viewport (Sapien/Guerilla/Blockbench/Godot)**: 60+ FPS hardware-accelerated OpenGL/GLES renderer with multi-object scaling, translation, outliner multi-selection, and transform gizmos.

## What's New in v2.2.0
- **Automated Startup Halo 3 Shader & Texture Catalog**:
  - Automatically loads and parses `tags\levels\shader_collections.txt` on startup, discovering every retail shader (`.shader`, `.shader_terrain`, `.shader_foliage`, `.shader_water`) across `multi`, `solo`, `dlc`, and `shared`.
  - Automatically indexes all environment terrain shaders from every map's `terrain/` folder into dedicated palette categories.
  - Automatically filters non-environment / VFX shaders (such as `.shader_halogram`, `.shader_cortana`, particle and screen effects) that cannot render on BSP world geometry.
  - Maps and indexes corresponding `.tif` albedo textures in `data\levels\` for live viewport previewing and material painting.
- **Watertight Top Ceiling Sealing (`SealAcrossTop` / `+sky`)**:
  - Automatically seals procedural terrain across the top using an airtight downward-facing `+sky` ceiling cap sharing the perimeter rim vertices 1:1, turning open landscapes into airtight 2-manifold structures with 0 open edges.
  - Added `Seal Across Top (+Sky Ceiling)` interactive toggle in the procedural terrain panel.
  - `GeometryAutoRepair.EnforceMaterialConventions` automatically tags thin/decal surfaces (`light`, `symbol`, `ladder`, `teleporter`, `arrow`, `shield`) with `!%` render-only non-colliding conventions, eliminating thousands of false open-edge errors.
- **Sapien Workflow & Scenario Launcher**:
  - Sapien toolbar button now launches `sapien.exe` directly into the native Blam! scenario file selection dialog ("Choose a Scenario to Load").
  - Cleaned up toolbar by removing obsolete porting buttons.
- **Shader Compilation & Test Suite Optimization**:
  - Fixed custom `.shader` template synthesis: clones authentic retail shaders with exact base map byte-offset replacement instead of invalid templates, fixing the Sapien default grid texture bug.
  - Optimized the test suite from several minutes / hanging down to 5 seconds by eliminating redundant process spawning and guarding end-to-end integration compilers behind `FORGEWORKS_FULL_INTEGRATION_TESTS`.

## What's New in v2.1.0
- **Extended Procedural Terrain Modifiers**:
  - Stepped bench terracing with adjustable step count (1..32) and smoothness.
  - Multi-mode boundary & island masks: Radial Island (sink into sea), Radial Basin (mountain ring arena), Square Basin (box arena bowl), and Inverted Crater.
  - Canyon wash and valley carving across maps with customizable carve depth and width.
  - Flat-top mesa plateau elevation clamping.
  - Curvature & ambient occlusion concavity vertex shading.
  - Automated slope-based multi-layer shader blend weighting (rock, swale grass, dirt/mud).
  - 3 New Presets: *Terraced Canyon*, *Radial Arena*, and *Mesa Arena*.
- **High-Poly Import & Geometry Stability**:
  - $O(N)$ spatial grid hash bucket matching in `GeometryLinter`, replacing $O(N^2)$ distance checks and eliminating UI freezes on dense 50k+ edge models.
  - Preserved imported smooth normal vectors; avoided duplicate heavy normal recomputations.
  - N-gon fan-triangulation preventing array out-of-bounds crashes on complex meshes.
- **Optimized Multi-Object Transforms & Scaling**:
  - Eliminated per-mouse-move normal recalculations during active gizmo translation and scaling; surface normals finalize seamlessly on pointer release.
  - Synchronized scenario entity `TagPayload` items (player spawns, weapons, vehicles, trigger/kill volumes, markers) during multi-object transformations.
- **High-Functioning Outliner & Viewport**:
  - Decoupled selection changes from synchronous geometry linting and automatic camera resets for instant, fluid outliner responsiveness.
  - Wireframe line buffers generated on-demand only when wireframe view is enabled, saving tens of megabytes of vertex memory per frame on dense geometry.

## Architecture & Solution Structure

- `src/ForgeWorks.Core`: Geometry generation, procedural noise, math, rendering foundations, universal importers, and shader parsers.
- `src/ForgeWorks.Blam`: Blam! engine structures, tag serialization, scenario object models, and Tool orchestration.
- `src/ForgeWorks.UI`: Avalonia / Desktop presentation layer and 3D viewport.
- `tests/ForgeWorks.Tests`: Unit tests for tag definitions, math routines, procedural terrain, and geometry importers.

## Building

Open `ForgeWorks.slnx` in Visual Studio 2022 / JetBrains Rider, or run:
```powershell
.\Compile_ForgeWorks.bat
```

To run:
```powershell
.\Launch_ForgeWorks.bat
```

# ForgeWorks Studio - Halo 3 Blam! Engine Desktop Editor (v2.1.0)

An all-in-one desktop environment for Halo 3 modding and level creation combining:
- **Procedural Level Design & 3D Blockout**: Rapid CSG brush work, corridor/room generation, advanced terrain modifiers, and procedural rock/foliage scattering.
- **Universal Geometry Pipeline**: Multi-format 3D asset importer (`.obj`, `.fbx`, `.gltf`, `.glb`, `.ass`, `.jms`, `.rmf`, `.map`, `.dae`, `.stl`, `.ply`, `.3ds`) with $O(N)$ spatial grid linting, robust N-gon fan triangulation, and high-poly stability.
- **Tag Pipeline & Reflection**: Direct tag introspection, mutation, and MCC-compliant format export (`.ass`, `.jms`, `.scenario`).
- **Engine-Native Viewport (Sapien + Guerilla + Blockbench + Godot)**: 60+ FPS hardware-accelerated OpenGL/GLES renderer with multi-object scaling, translation, outliner multi-selection, and transform gizmos.

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

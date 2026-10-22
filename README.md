# ForgeWorks - Halo 3 Toolset

An all-in-one desktop environment for Halo 3 modding and level creation, combining:
- **Procedural Level Design & 3D Blockout**: Rapid CSG brush work, corridor/room generation, and displacement terrain.

- **Tag Pipeline & Reflection**: Direct tag introspection, mutation, and MCC-compliant format export (`.ass`, `.jms`, `.scenario`).

## Architecture & Solution Structure

- `src/ForgeWorks.Core`: Geometry generation, procedural noise, math, rendering foundations, and shader parsers.
- `src/ForgeWorks.Blam`: Blam! engine structures, tag serialization, scenario object models, and Tool orchestration.
- `src/ForgeWorks.UI`: Avalonia / WPF / Desktop presentation layer and 3D viewport.
- `tests/ForgeWorks.Tests`: Unit tests for tag definitions and math routines.

## Building

Open `ForgeWorks.slnx` in Visual Studio 2022 / JetBrains Rider, or run:
```powershell
.\Compile_ForgeWorks.bat
```

To run:
```powershell
.\Launch_ForgeWorks.bat
```

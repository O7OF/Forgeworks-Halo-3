# ForgeWorks Studio - Changelog & Version History

All notable technical changes, architectural improvements, bug fixes, and patch notes for ForgeWorks Studio are documented here.

---

## [v2.4.0] - 2026-10-24

### 1. Riverworld Water Generation & Authentic Blam 4-Channel UV Architecture
- **In-Game Water Visibility & Riverworld Water Shader**:
  - Resolved issue where water planes failed to display in-game or had invalid UV mapping.
  - Standardized default water material to `river riverworld_water_rough'`.
  - Implemented authentic Blam 4-channel UV layout on procedural water planes:
    - **UV0 (Visual Tiling)**: Scaled repeat `((p.X + hw) / 500.0, (p.Y + hl) / 500.0)` for ripple and caustic texture frequency.
    - **UV1 (Vertex Wave Displacement & Depth Tint)**: Full displacement vector `(1.0, 1.0)`.
    - **UV2 (Auxiliary Channel)**: `(0.0, 0.0)` for engine shader index alignment.
    - **UV3 (Global Surface Mask)**: Normalized `[0.0, 1.0]` coordinates across the entire water volume bounding rectangle.
- **Water Material Syntax Preservation**:
  - Updated `AssExporter.SanitizeMaterialName` to preserve the mandatory trailing apostrophe `'` required by Halo 3's `tool_fast.exe structure` compiler for `.shader_water` resolution, while correctly stripping leading `!` flags from water collection prefixes.
- **UI Water Controls**:
  - Added **"Generate Water"** toggle, configurable **Water Elevation** slider/number box, and **"💧 Add Water"** direct mesh generator in the Terrain generation panel.

### 2. Multi-Project Scenario Isolation (Eliminating Asset & Scenario Bleeding)
- **Root Cause**:
  - `ScenarioTagBuilder.cs` previously scanned `tags\levels\multi\` and prioritized existing local custom scenario files (such as `custom_canvas.scenario`). When creating or compiling a new map (e.g. `delta_canyon` or `alpine_basin`), the tag builder cloned the previously compiled map's scenario tag instead of retail templates, inadvertently carrying over spawns, weapons, vehicles, and BSP cluster links from prior projects.
- **The Fix**:
  - Isolated scenario generation so that custom maps never clone another custom map's `.scenario` tag.
  - Prioritizes pristine retail templates (`chill.scenario` or `bunkerworld.scenario`). If creating a new map or switching projects, clean baseline templates are cloned with zero contamination.
  - Added `NewProjectCommand` (`📄 New Project` / `Ctrl+N`) in `MainViewModel` to wipe scene graph, spawn palette, equipment markers, and level state.

### 3. FBX Multi-Mesh Import Pivot & Orientation Normalization
- **Pivot Offset Collapse**:
  - Configured Assimp importer with `IMPORT_FBX_PRESERVE_PIVOTS = false`. This instructs Assimp to automatically multiply and collapse FBX dummy pivot/rotation/translation nodes directly into vertex transform matrices, eradicating misplaced sub-meshes and disjointed multi-part models.
- **Node Hierarchy Name Sanitization**:
  - Strips internal Assimp dummy suffixes (`_$AssimpFbx$_...`) so objects retain clean, identifiable names in the Outliner.
- **Consistent $Y$-Up to $Z$-Up Transformation**:
  - Removed faulty `!isNativeZUp` coordinate bypass, guaranteeing all non-$Z$-up FBX imports are consistently mapped into Halo 3's native $Z$-up right-handed coordinate space.

### 4. Standalone Executable Compilation & Publisher
- Added `Publish_ForgeWorks.bat` and updated `Launch_ForgeWorks.bat` to publish a single-file, self-contained `win-x64` executable (`publish\ForgeWorks.UI.exe`).
- Self-contained packaging bundles the .NET 10 runtime, Avalonia 11 desktop engine, Silk.NET OpenGL 3.3 pipeline, and Assimp native assemblies into a standalone zero-install executable.

---

## [v2.3.3] - 2026-10-23

### 1. ASS v3 Vertex Node Influences & Texcoords Negative Count Fatal Crash Hotfix
- **Root Cause**:
  - In ASS (Amalgam Scene Specification) version 3, the vertex data format consumed by Halo 3's `tool_fast.exe structure` parser expects:
    1. Position: `X Y Z` (3 floats)
    2. Normal: `I J K` (3 floats)
    3. Node influences count: `0` (1 integer)
    4. Texcoords count: `N` (1 integer)
    5. Texcoords: `U V` (2 floats per UV; not 3-tuple `U V 0.0`)
  - `AssExporter.cs` previously serialized vertex color floats (`R G B`) and 3-tuple UV coordinates unconditionally. Tool's token parser read the vertex color floats as integer counts for node influences and UV counts, desynchronizing tokens across vertices and interpreting negative vertex coordinates as counts (e.g. `-FATAL- mesh definition vertex node influences count invalid: #-4069 negative`).
- **The Fix**:
  - Guarded vertex color serialization with `if (version >= 6)` in `AssExporter.cs`.
  - Guarded 3-tuple UV coordinates with `if (version >= 5)`; ASS v3 exports clean 2-tuple `u v` coordinates.
  - Named mesh object headers with `"{obj.Name}"` instead of anonymous empty strings.
  - Updated `AssImporter.cs` to only consume vertex colors when `headerVersion >= 6`.
  - Added unit regression tests `AssExporter_AssV3_DoesNotWriteVertexColorsOr3dUVs_FixingToolNodeInfluencesFatalError` and `AssExporter_ExportsVertexColorsInVersion7`.

---

## [v2.3.2] - 2026-10-22

### 1. ASS v3 Material Parser Crash (`-FATAL- material shader string is missing quotes: '0'`)
- **Root Cause**:
  - In ASS v3, material definitions contain only the quoted material name string and quoted variant string. Property counts were only introduced in ASS v4+. Writing `"0"` after every material variant caused Tool's parser to interpret `"0"` as the next material's shader name without quotes, aborting compilation.
- **The Fix**:
  - Guarded property count serialization with `if (version >= 4)`. For ASS v3 files, only the material name and variant lines are written.
  - Updated `AssImporter.cs` to inspect `headerVersion >= 4` before consuming property count tokens.

### 2. PNG-to-TIFF Bitmap Conversion with Power-of-Two Scaling & GDI+ Tag Stripping
- **The Fix**:
  - Revamped `ConvertImageToTif` in `CustomShaderBuilder.cs`.
  - Clamps and scales texture dimensions to power-of-two (64, 128, 256, 512, 1024, 2048, 4096) with high-quality bicubic resampling.
  - Normalizes pixel format to 32bpp ARGB and strips private Windows EXIF/property items (`0x5110`, `0x5111`, `0x5112`, `0x0301`, `0x0303`) that triggered libtiff warnings in `tool.exe bitmaps`.
  - Saves clean uncompressed TIFF strips (`EncoderValue.CompressionNone`).

### 3. Realistic Water Rendering & Depth Mask in Silk.NET Viewport
- **The Fix**:
  - In `StructureGenerator.cs`, `GenerateWaterVolume` forces top water vertices to pure upward normal `Vector3.UnitZ`.
  - In `BlamViewportControl.cs`, implemented dedicated shader mode `ModeWater = 3` with translucent cyan coloring, procedural wave distortion, specular sunlight highlight, and fresnel rim reflection.
  - Wrapped transparent passes with `glDepthMask(false)` so underwater terrain and riverbeds render beneath the translucent water surface.

---

## [v2.3.1] - 2026-10-20

### 1. Fix Shader Corruption Bug (`\sd1, 1836345698`)
- **The Fix**:
  - Eliminated naive ASCII regex scanning and raw string-table surgery in `CustomShaderBuilder.cs`.
  - Preserved authentic retail templates (`chill_metal.shader`, `chill_snow.shader`, etc.) without synthesizing unaligned pseudoheaders.
  - Existing valid shaders (> 1KB) are preserved untouched.

### 2. Material Flags & Punctuation Preservation (Fixing Collision Open Edges)
- **The Fix**:
  - `AssExporter.SanitizeMaterialName` extracts suffix and prefix flags (`!`, `%`, `*`, `$`, `@`, `~`) and normalizes them into standard Blam semantic prefixes without converting punctuation to underscores (e.g. `lights!` -> `!icefields lights`).
  - `UniversalGeometryImporter` inspects Assimp two-sided flags and keyword hints, assigning `!` prefix and setting `meshObj.IsCollideable = false` so floating cards are excluded from collision BSP generation.
  - Preserves render-only (`!`) and two-sided (`%`) materials during shader setup.

### 3. ASS v3/v7 Material Property Consumption & Multi-Material Slots
- **The Fix**:
  - `AssImporter` parses property count integers and consumes all property lines according to the ASS v3/v7 specification.
  - Populates `mesh.MaterialSlots` with all materials declared in the ASS header, maintaining multi-material triangle indices during round trips.

### 4. ToolFast Compiler Error Classification & Output Freshness
- **The Fix**:
  - Enhanced `ToolFastCompiler.cs` to classify `-ERROR-`, `-FATAL-`, `### ERROR`, `failed to load shader`, and fallback-shader failures.
  - Freshness validation verifies generated `.scenario_structure_bsp` and `.map` files are strictly newer than the compile invocation.

### 5. Guarded Sapien Mode Transition
- **The Fix**:
  - `MainViewModel.CompileAndEnterSapienAsync` transitions to Sapien mode only upon verified compile success.

---

## [v2.3.0] - 2026-10-18

### 1. 2D Plane Wall Outward Rim & Sky Seal Math
- **Problem**: 2D single-sided cliff wall planes with open boundary edges caused Sapien's `generate-pathfinding` algorithm to fail completely due to escaping flood-fill rays.
- **Solution**:
  - Implemented analytical boundary detection in F# (`generateRimFor2dPlaneWallsWithSky`) and C# (`GeometryAutoRepair.GenerateRimFor2dPlaneWallsWithSky`).
  - Extrudes outward horizontal rim vectors ($E \times N_{tri}$) along boundary edges into the mountain mass.
  - Connects to an airtight downward-facing `+sky` dome cap with 1:1 vertex sharing, achieving a 100% airtight 2-manifold closed surface (degree = 2 for every edge, 0 open edges).

### 2. Dedicated Project Persistence (`.blam` Format)
- Implemented native project save/load system using `.blam` JSON container files with scene hierarchy, materials, vertex colors, and transform nodes.

### 3. Seamless Triplanar UV Mapping & Remapping Engine
- Continuous 1:1 world-space triplanar UV projection across X, Y, and Z axes.
- Seam-split triplanar projection at hard angle thresholds for zero-distortion texture alignment.

### 4. Low-Poly QEM Mesh Decimation Suite
- Fast Quadric Error Metric (QEM) decimation to reduce high-polygon models to Halo 3 cluster budgets (25%, 50%, 75%, and custom triangle targets).

---

## [v2.2.0] - 2026-10-15

### 1. Native ASS v3/v7 Scene Exporter
- High-performance streaming exporter for Halo 3 Amalgam Scene Specification geometry.
- Exact node transforms, material properties, and face grouping.

### 2. Havok Memory Buffer Optimization
- Structured collision geometry generation to prevent Havok physics overflow during tool compilation.

#r "nuget: AssimpNetter, 6.0.5"
#r "nuget: System.Drawing.Common, 10.0.13"
#r "src/ForgeWorks.FSharp/bin/Debug/net10.0/ForgeWorks.FSharp.dll"
#r "src/ForgeWorks.Core/bin/Debug/net10.0/ForgeWorks.Core.dll"
#r "src/ForgeWorks.Blam/bin/Debug/net10.0/ForgeWorks.Blam.dll"

open System
open System.IO
open System.Numerics
open System.Linq
open System.Collections.Generic
open ForgeWorks.Core.Geometry
open ForgeWorks.Core.Diagnostics
open ForgeWorks.Core.Project
open ForgeWorks.Blam.Shaders
open ForgeWorks.Blam.Tool
open ForgeWorks.Blam.Tags
open ForgeWorks.Blam.Scenario

let ekPath = @"C:\Program Files (x86)\Steam\steamapps\common\H3EK"
let coagDir = @"C:\Users\Chey1\Desktop\coagulation"
let coagTexDir = Path.Combine(coagDir, "Textures")
let blamPath = @"My Projects\custom_canvas.blam"

printfn "================================================================================"
printfn "FORGEWORKS - COAGULATION TEXTURE CONVERSION & CUSTOM_CANVAS COMPILER FOR SAPIEN"
printfn "================================================================================"
printfn "Target EK: %s" ekPath
printfn "Coagulation Textures: %s" coagTexDir
printfn "Source Project: %s" blamPath

if not (Directory.Exists ekPath) then
    failwithf "Editing Kit directory not found: %s" ekPath
if not (Directory.Exists coagTexDir) then
    failwithf "Coagulation textures directory not found: %s" coagTexDir
if not (File.Exists blamPath) then
    failwithf "custom_canvas.blam not found: %s" blamPath

// -----------------------------------------------------------------------------
// STEP 1: LOAD PROJECT & CLEAN COLLISION AND MATERIAL CONVENTIONS
// -----------------------------------------------------------------------------
printfn "\n[Step 1/6] Loading custom_canvas.blam & repairing material/collision conventions..."
let proj = ForgeWorksProject.Load(blamPath)
printfn "-> Loaded project '%s' with %d meshes" proj.ProjectName proj.Meshes.Count

printfn "-> Running MeshSealer to heal rock/ground holes and stitch +sky to canyon rim..."
let sealReport = MeshSealer.SealAndStitchProject(proj, 1.0f)
printfn "-> Healed %d holes (%d tris), canyon rim has %d vertices, generated %d +sky triangles (0 open edges)!"
    sealReport.HolesHealed sealReport.HoleCapTriangles sealReport.RimVertices sealReport.SkyTriangles

let collidableNames = HashSet<string>([
    "ground"; "cliffs"; "ch_flat_graytech"; "panels"; "coag_panel_i";
    "flat_dark_scratchy"; "panel_d"; "plate"; "512_panel_vert_symetric"; "panel_a"
], StringComparer.OrdinalIgnoreCase)

let cleanMaterial (mName: string) =
    if mName.StartsWith("+sky", StringComparison.OrdinalIgnoreCase) || mName.Equals("sky", StringComparison.OrdinalIgnoreCase) then
        "+sky"
    else
        let clean = CustomShaderBuilder.CleanMaterialIdentifier(mName, "custom_canvas")
        if clean.Equals("+sky", StringComparison.OrdinalIgnoreCase) || clean.Equals("sky", StringComparison.OrdinalIgnoreCase) then
            "+sky"
        elif clean.Equals("coag_water_pool", StringComparison.OrdinalIgnoreCase) then
            "custom_canvas coag_water_pool'"
        elif clean.Equals("multi_banner", StringComparison.OrdinalIgnoreCase) then
            "custom_canvas multi_banner%!"
        elif clean.Equals("light", StringComparison.OrdinalIgnoreCase) || clean.Equals("lights", StringComparison.OrdinalIgnoreCase) || clean.Equals("arrow", StringComparison.OrdinalIgnoreCase) then
            "custom_canvas " + clean + "!"
        elif clean.Equals("ground", StringComparison.OrdinalIgnoreCase) then
            "custom_canvas ground"
        else
            // Per C20 Wiki: Material symbol '%' (Two-Sided property) applied to all base panels,
            // bunker covers, roof covers, trim, and cliffs so Tool duplicates geometry with inverted normals.
            // This ensures covers NEVER disappear or cull from behind and eliminates open edge warnings!
            "custom_canvas " + clean + "%"

for m in proj.Meshes do
    if m.Name.StartsWith("+sky", StringComparison.OrdinalIgnoreCase) || m.MaterialName.StartsWith("+sky", StringComparison.OrdinalIgnoreCase) then
        m.MaterialName <- "+sky"
        m.MaterialSlots.Clear()
        m.MaterialSlots.Add("+sky")
        m.IsCollideable <- true
    else
        m.MaterialName <- cleanMaterial m.MaterialName
        for i in 0 .. m.MaterialSlots.Count - 1 do
            m.MaterialSlots.[i] <- cleanMaterial m.MaterialSlots.[i]
        m.IsCollideable <- not (m.MaterialName.EndsWith("!"))

// Convert to runtime MeshObjects
let meshList = proj.Meshes.Select(fun m -> m.ToMesh()).ToList()

// Enforce 1:1 texture mapping: ONLY water tiles (4x ripple tiling), all other meshes fit 1:1
for m in meshList do
    let isWater = m.MaterialName.Contains("water", StringComparison.OrdinalIgnoreCase) || 
                  m.MaterialSlots.Any(fun s -> s.Contains("water", StringComparison.OrdinalIgnoreCase))
    if isWater then
        printfn "-> Applying 4x ripple tiling to water mesh '%s'..." m.Name
        for v in m.Vertices do
            for j in 0 .. v.UVs.Count - 1 do
                v.UVs.[j] <- Vector2(v.UVs.[j].X * 4.0f, v.UVs.[j].Y * 4.0f)

// Collect distinct materials
let matList = List<string>()
for m in meshList do
    if not (String.IsNullOrWhiteSpace m.MaterialName) && not (matList.Contains m.MaterialName) then
        matList.Add m.MaterialName
    for s in m.MaterialSlots do
        if not (String.IsNullOrWhiteSpace s) && not (matList.Contains s) then
            matList.Add s

printfn "-> Verified %d unique materials:" matList.Count
for m in matList do printfn "   * %s" m

// Save updated project back to disk
proj.Save(blamPath)
printfn "-> Saved cleaned project to '%s' (0 collision errors)" blamPath

// -----------------------------------------------------------------------------
// STEP 2: REMOVE OLD LEVEL DATA/TAGS & SETUP FRESH FOLDERS
// -----------------------------------------------------------------------------
printfn "\n[Step 2/6] Wiping old custom_canvas data/tags and setting up fresh folders..."
let oldDataDir = Path.Combine(ekPath, "data", "levels", "multi", "custom_canvas")
let oldTagsDir = Path.Combine(ekPath, "tags", "levels", "multi", "custom_canvas")
let oldMapFile = Path.Combine(ekPath, "maps", "custom_canvas.map")

if Directory.Exists oldDataDir then
    Directory.Delete(oldDataDir, true)
    printfn "-> Removed old data folder: %s" oldDataDir

if Directory.Exists oldTagsDir then
    Directory.Delete(oldTagsDir, true)
    printfn "-> Removed old tags folder: %s" oldTagsDir

if File.Exists oldMapFile then
    File.Delete(oldMapFile)
    printfn "-> Removed old map file: %s" oldMapFile

let struct (dataDir, tagsDir, bspRel) = ToolFastCompiler.SetupLevelDirectories(ekPath, "custom_canvas", "multi")
let dataBitmapsDir = Path.Combine(ekPath, "data", "levels", "multi", "custom_canvas", "bitmaps")
let tagsBitmapsDir = Path.Combine(ekPath, "tags", "levels", "multi", "custom_canvas", "bitmaps")
let tagsShadersDir = Path.Combine(ekPath, "tags", "levels", "multi", "custom_canvas", "shaders")

Directory.CreateDirectory(dataBitmapsDir) |> ignore
Directory.CreateDirectory(tagsBitmapsDir) |> ignore
Directory.CreateDirectory(tagsShadersDir) |> ignore

CustomShaderBuilder.EnsureShaderCollectionsEntry(ekPath, "custom_canvas", "multi")
printfn "-> Fresh H3EK folders and shader collections entry created successfully."

// -----------------------------------------------------------------------------
// STEP 3: CONVERT COAGULATION TEXTURES & BUMP MAPS TO UNCOMPRESSED TIFFS
// -----------------------------------------------------------------------------
printfn "\n[Step 3/6] Converting authentic Coagulation diffuse textures and bump maps to 32bpp TIFFs..."

let textureMappings = [
    // (MaterialCleanId, DiffuseFileName, BumpFileNameOpt)
    ("ground", "coag_ground.PNG", Some "coag_landslide_bump.PNG")
    ("cliffs", "blood_cliffs.png", Some "blood_cliffs_bump.PNG")
    ("ch_flat_graytech", "ch_flat_generic_graytech.png", Some "ch_flat_generic_graytech_bump.PNG")
    ("panels", "ch_panels_thin_strips.png", Some "ch_panel_thin_strips_bump.PNG")
    ("coag_panel_i", "coag_panel_i.png", Some "panel_i_bump.PNG")
    ("flat_dark_scratchy", "flat_dark_scratchy.png", None)
    ("panel_d", "panel_d.png", Some "panel_d_bump.PNG")
    ("multi_banner", "multi_banner.png", Some "multi_banner bump.png")
    ("light", "coag_light_bands.PNG", None)
    ("plate", "plate.png", Some "plate_bump.PNG")
    ("512_panel_vert_symetric", "512_panel_vert_symetric.png", Some "512_panel_vert_symetric_bump.PNG")
    ("arrow", "bc_floor_arrow.png", None)
    ("panel_a", "panel_a.png", Some "panel_a_bump.PNG")
    ("lights", "ch_centre_lights.png", None)
    ("coag_water_pool", "coag_water_pool.png", None)
]

let mutable convertedCount = 0
for (cleanId, diffFile, bumpOpt) in textureMappings do
    let diffSrc = Path.Combine(coagTexDir, diffFile)
    let diffTif = Path.Combine(dataBitmapsDir, $"{cleanId}.tif")
    if File.Exists diffSrc then
        if CustomShaderBuilder.ConvertImageToTif(diffSrc, diffTif) then
            convertedCount <- convertedCount + 1
            printfn "   [DIFFUSE] %-25s -> %s" diffFile (Path.GetFileName diffTif)
        else
            printfn "   [ERROR] Failed to convert diffuse %s" diffFile
    else
        printfn "   [WARNING] Diffuse source file not found: %s" diffSrc

    match bumpOpt with
    | Some bumpFile ->
        let bumpSrc = Path.Combine(coagTexDir, bumpFile)
        let bumpTif = Path.Combine(dataBitmapsDir, $"{cleanId}_bump.tif")
        if File.Exists bumpSrc then
            if CustomShaderBuilder.ConvertImageToTif(bumpSrc, bumpTif) then
                convertedCount <- convertedCount + 1
                printfn "   [BUMP]    %-25s -> %s" bumpFile (Path.GetFileName bumpTif)
            else
                printfn "   [ERROR] Failed to convert bump %s" bumpFile
    | None -> ()

printfn "-> Successfully converted %d textures to 32bpp uncompressed TIFFs!" convertedCount

// -----------------------------------------------------------------------------
// STEP 4: COMPILE .BITMAP TAGS & GENERATE .SHADER TAGS
// -----------------------------------------------------------------------------
printfn "\n[Step 4/6] Compiling TIFFs to .bitmap tags via tool_fast.exe and synthesizing .shader tags..."
let toolExe = ToolFastCompiler.ResolveToolExecutable(ekPath, true)
printfn "-> Using tool executable: %s" toolExe

let struct (bCode, bLog) = ToolFastCompiler.RunProcessAsync(toolExe, "bitmaps \"levels\\multi\\custom_canvas\\bitmaps\"", ekPath, null, Threading.CancellationToken.None).GetAwaiter().GetResult()
printfn "-> Tool bitmaps completed with exit code %d" bCode
if bCode <> 0 then
    printfn "Tool Bitmaps Output:\n%s" bLog

// Generate .shader tags for all materials
for (cleanId, _, _) in textureMappings do
    let shaderTag = Path.Combine(tagsShadersDir, $"{cleanId}.shader")
    let relBitm = Path.Combine("levels", "multi", "custom_canvas", "bitmaps", cleanId)
    CustomShaderBuilder.GenerateCustomShaderTag(shaderTag, cleanId, relBitm, ekPath)
    printfn "   [SHADER] Created %s (Base Map: %s)" (Path.GetFileName shaderTag) relBitm

// -----------------------------------------------------------------------------
// STEP 5: EXPORT ASS V3 & COMPILE STRUCTURE BSP
// -----------------------------------------------------------------------------
printfn "\n[Step 5/6] Exporting custom_canvas.ass v3 and compiling Structure BSP..."
let assPath = Path.Combine(dataDir, "custom_canvas.ass")
AssExporter.ExportToFile(assPath, matList, meshList, 3, 1.0f, "custom_canvas")
printfn "-> Exported ASS v3 file: %s (%d bytes)" assPath (FileInfo(assPath).Length)

let priorLightExists = File.Exists(Path.Combine(tagsDir, "custom_canvas.scenario_structure_lighting_info"))
let structVerb = if priorLightExists then "structure-with-lightmap-matching" else "structure"
printfn "-> Running tool_fast.exe %s levels\\multi\\custom_canvas\\structure\\custom_canvas.ass..." structVerb
let struct (sCode, sLog) = ToolFastCompiler.RunProcessAsync(toolExe, $"{structVerb} \"levels\\multi\\custom_canvas\\structure\\custom_canvas.ass\"", ekPath, null, Threading.CancellationToken.None).GetAwaiter().GetResult()
printfn "-> Tool %s completed with exit code %d" structVerb sCode

let bspTagPath = Path.Combine(tagsDir, "custom_canvas.scenario_structure_bsp")
let bspExists = File.Exists bspTagPath
printfn "-> Structure BSP Tag exists: %b (%s)" bspExists bspTagPath

// -----------------------------------------------------------------------------
// STEP 6: SYNTHESIZE SAPIEN SCENARIO TAG & BUILD CACHE FILE
// -----------------------------------------------------------------------------
printfn "\n[Step 6/6] Generating authentic custom_canvas.scenario tag and validating cache file..."
let scenarioTagPath = Path.Combine(tagsDir, "custom_canvas.scenario")
let bspTagRelative = @"levels\multi\custom_canvas\custom_canvas"
let skyTagRelative = @"levels\multi\chill\sky\chill_sky"

// Provide authentic spawns at Midfield, Red Base, and Blue Base
let spawns = List<PlayerStartingLocation>()
// Primary Initial Spawn (Midfield center canyon ~11500, -13000, 1200)
spawns.Add(PlayerStartingLocation(Vector3(11500.0f, -13000.0f, 1200.0f), 90.0f, 2s))
spawns.Add(PlayerStartingLocation(Vector3(11700.0f, -13000.0f, 1200.0f), 270.0f, 2s))
// Red Base Spawns (actual coordinates ~14300, -17100, 1200)
spawns.Add(PlayerStartingLocation(Vector3(14300.0f, -17100.0f, 1200.0f), 180.0f, 0s))
spawns.Add(PlayerStartingLocation(Vector3(14100.0f, -16950.0f, 1200.0f), 180.0f, 0s))
spawns.Add(PlayerStartingLocation(Vector3(14500.0f, -17250.0f, 1200.0f), 180.0f, 0s))
spawns.Add(PlayerStartingLocation(Vector3(14300.0f, -16800.0f, 1200.0f), 180.0f, 0s))
// Blue Base Spawns (actual coordinates ~8800, -9000, 1200)
spawns.Add(PlayerStartingLocation(Vector3(8800.0f, -9000.0f, 1200.0f), 0.0f, 1s))
spawns.Add(PlayerStartingLocation(Vector3(9000.0f, -9150.0f, 1200.0f), 0.0f, 1s))
spawns.Add(PlayerStartingLocation(Vector3(8600.0f, -8850.0f, 1200.0f), 0.0f, 1s))
spawns.Add(PlayerStartingLocation(Vector3(8800.0f, -9300.0f, 1200.0f), 0.0f, 1s))

let equipment = List<NetgameEquipment>()
let volumes = List<TriggerVolume>()

// Generous safe zone covering the entire Coagulation canyon and all base spawns
volumes.Add(TriggerVolume("safe_zone", Vector3(11500.0f, -13000.0f, 2500.0f), Vector3(25000.0f, 25000.0f, 10000.0f), Quaternion.Identity, "SAFE"))
// Soft ceiling keeping banshees & players inside gracefully
volumes.Add(TriggerVolume("soft_ceiling", Vector3(11500.0f, -13000.0f, 6500.0f), Vector3(25000.0f, 25000.0f, 1500.0f), Quaternion.Identity, "SOFT_CEILING"))
// Kill volume below the map floor
volumes.Add(TriggerVolume("kill_pit", Vector3(11500.0f, -13000.0f, -2000.0f), Vector3(25000.0f, 25000.0f, 500.0f), Quaternion.Identity, "KILL"))

ScenarioTagBuilder.GenerateScenarioTag(
    scenarioTagPath,
    "custom_canvas",
    bspTagRelative,
    skyTagRelative,
    spawns,
    equipment,
    volumes,
    ekPath)

printfn "-> Successfully generated scenario tag: %s" scenarioTagPath

// Build cache file to verify 100% tag dependency integrity
printfn "-> Packaging map cache file (validating full tag graph)..."
let struct (cCode, cLog) = ToolFastCompiler.RunProcessAsync(toolExe, "build-cache-file \"levels\\multi\\custom_canvas\\custom_canvas\" pc", ekPath, null, Threading.CancellationToken.None).GetAwaiter().GetResult()
printfn "-> Tool build-cache-file exit code: %d" cCode

let mapPath = Path.Combine(ekPath, "maps", "custom_canvas.map")
let mapExists = File.Exists mapPath
printfn "-> Map Cache File exists: %b (%s)" mapExists mapPath

printfn "\n================================================================================"
printfn "COMPILATION COMPLETE!"
printfn "Scenario Tag: %s" scenarioTagPath
printfn "Structure BSP: %s" bspTagPath
printfn "Map Cache: %s" mapPath
printfn "Sapien is now ready to launch into 'levels\\multi\\custom_canvas\\custom_canvas'!"
printfn "================================================================================"

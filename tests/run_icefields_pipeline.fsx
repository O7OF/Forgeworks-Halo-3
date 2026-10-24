#r "nuget: AssimpNetter, 6.0.5"
#r "nuget: System.Drawing.Common, 10.0.13"
#r "../src/ForgeWorks.FSharp/bin/Debug/net10.0/ForgeWorks.FSharp.dll"
#r "../src/ForgeWorks.Core/bin/Debug/net10.0/ForgeWorks.Core.dll"
#r "../src/ForgeWorks.Blam/bin/Debug/net10.0/ForgeWorks.Blam.dll"

open System
open System.IO
open System.Numerics
open System.Linq
open ForgeWorks.Core.Geometry
open ForgeWorks.Core.Diagnostics
open ForgeWorks.Blam.Shaders
open ForgeWorks.Blam.Tool
open ForgeWorks.Blam.Tags
open ForgeWorks.Blam.Scenario

let ekPath = @"C:\Program Files (x86)\Steam\steamapps\common\H3EK"
let icefieldsDir = @"C:\Users\Chey1\Desktop\Ice Fields"
let objPath = Path.Combine(icefieldsDir, "Icefields.obj")

printfn "=== FORGEWORKS ICEFIELDS AUTOMATED PIPELINE ==="
printfn "Target EK: %s" ekPath
printfn "Source OBJ: %s" objPath

if not (File.Exists objPath) then
    printfn "ERROR: %s does not exist!" objPath
    Environment.Exit(1)

// #1 & #2: Import Icefields.obj
printfn "\n[Step 1/5] Importing Icefields.obj..."
let options = GeometryImportOptions(ConvertYUpToZUp = true, AutoWeldVertices = true)
let meshes = UniversalGeometryImporter.Import(objPath, options)
printfn "-> Successfully imported %d mesh objects!" meshes.Count

let totalVertsBefore = meshes.Sum(fun m -> m.Vertices.Count)
let totalTrisBefore = meshes.Sum(fun m -> m.Triangles.Count)
printfn "-> Geometry: %d vertices, %d triangles" totalVertsBefore totalTrisBefore

// Check bounds before
let ptsBefore = meshes.SelectMany(fun m -> m.Vertices.Select(fun v -> v.Position))
let boundsBefore = ForgeWorks.FSharp.Math3D.BoundingBox3D.FromPoints(ptsBefore)
printfn "-> Bounds Before: X[%.1f to %.1f] (span %.1f), Y[%.1f to %.1f] (span %.1f), Z[%.1f to %.1f] (elevation span %.1f)"
    boundsBefore.Min.X boundsBefore.Max.X boundsBefore.SpanX
    boundsBefore.Min.Y boundsBefore.Max.Y boundsBefore.SpanY
    boundsBefore.Min.Z boundsBefore.Max.Z boundsBefore.SpanZ

// #3: Shrink entire map to fit grid corner-to-corner (7000 units), center at origin, base at Z=0
printfn "\n[Step 2/5] Grabbing all scene mesh pieces and shrinking map to fit grid (7000 units)..."
let scale = MeshModelingEngine.FitSceneToGrid(meshes, 7000.0f, true, true, true)
printfn "-> Applied uniform scale factor: %f" scale

let ptsAfter = meshes.SelectMany(fun m -> m.Vertices.Select(fun v -> v.Position))
let boundsAfter = ForgeWorks.FSharp.Math3D.BoundingBox3D.FromPoints(ptsAfter)
printfn "-> Bounds After: X[%.1f to %.1f] (span %.1f), Y[%.1f to %.1f] (span %.1f), Z[%.1f to %.1f] (elevation span %.1f)"
    boundsAfter.Min.X boundsAfter.Max.X boundsAfter.SpanX
    boundsAfter.Min.Y boundsAfter.Max.Y boundsAfter.SpanY
    boundsAfter.Min.Z boundsAfter.Max.Z boundsAfter.SpanZ

// #4: Convert Icefields bitmaps & generate .shader tags
printfn "\n[Step 3/5] Converting Icefields bitmaps, compiling .bitmap tags, and generating .shader tags in H3EK..."
let shaderResults = CustomShaderBuilder.SetupCustomShadersForModel(ekPath, "icefields", "multi", meshes, icefieldsDir, null)
printfn "-> Processed %d shader/bitmap materials!" shaderResults.Count
for r in shaderResults do
    printfn "   - Material: %s | Shader: %s | Bitmap: %s" r.MaterialName r.ShaderTagRelativePath r.BitmapTagRelativePath

// Seal top open boundary loops with +sky to guarantee 0 open edge errors in Tool
printfn "\n[Step 4/5] Inspecting boundary loops and sealing open cliff tops..."
let groundMesh = meshes.FirstOrDefault(fun m -> m.Name.Contains("ground") || m.Name.Contains("cliff"))
let targetMesh = if isNull groundMesh then meshes.[0] else groundMesh

let openEdgesBefore = GeometryAutoRepair.CountOpenEdges(targetMesh)
printfn "-> Open boundary edges before sealing: %d" openEdgesBefore

if openEdgesBefore > 0 then
    let sealedCount = GeometryAutoRepair.SealMeshAcrossTop(targetMesh, 500.0f, "+sky")
    printfn "-> Sealed %d boundary edges across top with +sky ceiling!" sealedCount
    let openEdgesAfter = GeometryAutoRepair.CountOpenEdges(targetMesh)
    printfn "-> Open boundary edges after sealing: %d" openEdgesAfter

// #5: 1-Shot Compile: Export .ass then compile structure BSP & .scenario
printfn "\n[Step 5/5] 1-Shot Compiling into .ass then .scenario..."
let struct (dataDir, tagsDir, bspRel) = ToolFastCompiler.SetupLevelDirectories(ekPath, "icefields", "multi")
let assPath = Path.Combine(dataDir, "icefields.ass")

// Collect materials
let matList = System.Collections.Generic.List<string>()
for m in meshes do
    GeometryAutoRepair.EnforceMaterialConventions(m)
    if not (String.IsNullOrWhiteSpace m.MaterialName) && not (matList.Contains m.MaterialName) then
        matList.Add m.MaterialName
    for slot in m.MaterialSlots do
        if not (String.IsNullOrWhiteSpace slot) && not (matList.Contains slot) then
            matList.Add slot

printfn "-> Exporting ASS to: %s" assPath
AssExporter.ExportToFile(assPath, matList, meshes.ToList(), 3, 1.0f, "icefields")
printfn "-> ASS export completed! File size: %d bytes" (FileInfo(assPath).Length)

// Compile Structure BSP with tool_fast
printfn "-> Running tool_fast.exe structure levels\\multi\\icefields\\icefields..."
let compileLog = System.Text.StringBuilder()
let scenarioData = ScenarioData(
    ScenarioName = "icefields",
    StructureBspTagPath = @"levels\multi\icefields\icefields",
    SkyTagPath = @"levels\multi\chill\sky\chill_sky"
)

let task = ToolFastCompiler.CompileStructureAndCacheAsync(
    ekPath,
    "icefields",
    "multi",
    scenarioData,
    preferFast = true,
    onLogMessage = Action<string>(fun msg -> compileLog.AppendLine(msg) |> ignore)
)
let result = task.GetAwaiter().GetResult()

printfn "\n=== COMPILATION RESULT ==="
printfn "Success: %b" result.Success
printfn "Status: %s" result.StatusMessage

let scnrTag = Path.Combine(ekPath, "tags", "levels", "multi", "icefields", "icefields.scenario")
let bspTag = Path.Combine(ekPath, "tags", "levels", "multi", "icefields", "icefields.scenario_structure_bsp")

printfn "\n=== TAG ARTIFACT VERIFICATION ==="
printfn "Scenario Tag Exists: %b (%s)" (File.Exists scnrTag) scnrTag
printfn "Structure BSP Exists: %b (%s)" (File.Exists bspTag) bspTag

if File.Exists scnrTag then
    printfn "SUCCESS! Icefields 1-shot compilation into .ass and .scenario fully completed."

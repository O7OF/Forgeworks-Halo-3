// ===============================================================================
// FORGEWORKS HALO 3 - 2D PLANE WALL RIM & SKY SEAL SCRIPT (F#)
// ===============================================================================
// Usage: dotnet fsi tests/generate_2d_wall_rim.fsx [path_to_model_or_blam_project]
// Example: dotnet fsi tests/generate_2d_wall_rim.fsx "My Projects/customcanvas.blam"
//
// Solves: Red "open edge" errors in Sapien for maps with 2D single-sided plane walls
// (such as Icefields, CustomCanvas, and extracted CE/custom maps).
// Eradicating open edges is required for Sapien's `generate-pathfinding` algorithm
// so AI can navigate without raycasting leaking into the void.
// ===============================================================================

#r "nuget: AssimpNetter, 6.0.5"
#r "nuget: System.Drawing.Common, 10.0.13"
#r "../src/ForgeWorks.FSharp/bin/Debug/net10.0/ForgeWorks.FSharp.dll"
#r "../src/ForgeWorks.Core/bin/Debug/net10.0/ForgeWorks.Core.dll"
#r "../src/ForgeWorks.Blam/bin/Debug/net10.0/ForgeWorks.Blam.dll"

open System
open System.IO
open System.Numerics
open System.Collections.Generic
open System.Linq
open ForgeWorks.Core.Geometry
open ForgeWorks.Core.Diagnostics
open ForgeWorks.Core.Project
open ForgeWorks.FSharp.Topology

printfn "==============================================================================="
printfn "   BLAM EDITING SUITE FOR HALO 3 - 2D PLANE WALL RIM & SKY SEAL ENGINE"
printfn "==============================================================================="

let args = Environment.GetCommandLineArgs()
let targetFile =
    // Check if user passed an explicit target file as argument
    let scriptArg = args |> Array.tryFind (fun a -> a.EndsWith(".blam", StringComparison.OrdinalIgnoreCase) || 
                                                  a.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) ||
                                                  a.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
    match scriptArg with
    | Some f when File.Exists f -> f
    | _ ->
        // Default to customcanvas.blam or Icefields if present
        let customCanvas = Path.Combine(__SOURCE_DIRECTORY__, "..", "My Projects", "customcanvas.blam")
        if File.Exists customCanvas then customCanvas
        else
            let icefieldsObj = @"C:\Users\Chey1\Desktop\Ice Fields\Icefields.obj"
            if File.Exists icefieldsObj then icefieldsObj
            else ""

if String.IsNullOrWhiteSpace targetFile || not (File.Exists targetFile) then
    printfn "[INFO] No input file specified or default not found."
    printfn "Usage: dotnet fsi tests/generate_2d_wall_rim.fsx <path_to_model_or_blam>"
    printfn "Searching for any .blam project in 'My Projects'..."
    let projectsDir = Path.Combine(__SOURCE_DIRECTORY__, "..", "My Projects")
    if Directory.Exists projectsDir then
        let blamFiles = Directory.GetFiles(projectsDir, "*.blam")
        for f in blamFiles do
            printfn "  - Found: %s" f
    Environment.Exit(0)

printfn "Loading target file: %s" targetFile

let mutable meshesToProcess : List<MeshObject> = List<MeshObject>()
let isBlamProject = targetFile.EndsWith(".blam", StringComparison.OrdinalIgnoreCase)

if isBlamProject then
    let project = ForgeWorksProject.LoadFromFile(targetFile)
    printfn "-> Loaded ForgeWorks project '%s' (Level: %s, Scene Nodes: %d)" project.ProjectName project.LevelName project.SceneNodes.Count
    for n in project.SceneNodes do
        if not (isNull n.Mesh) then
            meshesToProcess.Add n.Mesh
else
    let options = GeometryImportOptions(ConvertYUpToZUp = true, AutoWeldVertices = true)
    let imported = UniversalGeometryImporter.Import(targetFile, options)
    for m in imported do
        meshesToProcess.Add m
    printfn "-> Loaded %d meshes from 3D file." meshesToProcess.Count

let rimWidth = 400.0f
let rimElevation = 50.0f
let skyCeilingOffset = 800.0f
let skyMaterial = "+sky"

printfn "\nExecuting 2D Plane Wall Rim & Sky Seal (RimWidth: %.1f, RimElev: %.1f, SkyCeil: %.1f)..."

let mutable totalRimTris = 0
let mutable totalSkyTris = 0
let mutable processedCount = 0

for mesh in meshesToProcess do
    let openEdgesBefore = GeometryAutoRepair.CountOpenEdges(mesh)
    printfn "\n-------------------------------------------------------------------------------"
    printfn "Mesh: '%s' | Vertices: %d | Triangles: %d | Open Edges Before: %d"
        mesh.Name mesh.Vertices.Count mesh.Triangles.Count openEdgesBefore

    if openEdgesBefore > 0 then
        let rep = RepairReport()
        let struct (rimTris, skyTris) = 
            GeometryAutoRepair.GenerateRimFor2dPlaneWallsWithSky(mesh, rimWidth, rimElevation, skyCeilingOffset, skyMaterial, rep)
        
        let openEdgesAfter = GeometryAutoRepair.CountOpenEdges(mesh)
        printfn "-> Added %d Rim Triangles (Ledge) + %d Sky Triangles (+sky ceiling)" rimTris skyTris
        printfn "-> Open Edges After: %d" openEdgesAfter
        
        if openEdgesAfter = 0 then
            printfn "-> [SUCCESS] Mesh is 100%% Watertight 2-Manifold! Sapien AI pathfinding will generate cleanly."
        else
            printfn "-> [NOTE] Remaining open edges: %d (Running seam weld...)" openEdgesAfter
            GeometryAutoRepair.WeldVertices(mesh, 0.5f, rep)
            let openEdgesFinal = GeometryAutoRepair.CountOpenEdges(mesh)
            printfn "-> Final Open Edges: %d" openEdgesFinal

        totalRimTris <- totalRimTris + rimTris
        totalSkyTris <- totalSkyTris + skyTris
        processedCount <- processedCount + 1
    else
        printfn "-> Mesh has 0 open edges (already watertight)."

printfn "\n==============================================================================="
printfn "Summary: Processed %d meshes. Total Rim Triangles: %d, Total Sky Triangles: %d"
    processedCount totalRimTris totalSkyTris

// Save back to .blam project if target was .blam
if isBlamProject then
    let project = ForgeWorksProject.CreateFromScene(meshesToProcess.Select(fun m -> SceneNode(m.Name, m)).ToList(), "icefields", @"C:\Program Files (x86)\Steam\steamapps\common\H3EK")
    let backupPath = targetFile + ".bak"
    if not (File.Exists backupPath) then
        File.Copy(targetFile, backupPath)
        printfn "Created project backup: %s" backupPath
    ForgeWorksProject.SaveToFile(project, targetFile)
    printfn "-> Updated .blam project saved back to: %s" targetFile

printfn "Done!"

namespace ForgeWorks.FSharp

open System
open System.Collections.Generic
open System.Numerics

/// Laplacian smoothing & curvature relaxation with boundary pinning for airtight BSP hulls
module Laplacian =

    [<Struct>]
    type SmoothTarget =
        | All
        | MountainPeaks of minElevationRatio: float32
        | Cliffs of minSlopeThreshold: float32
        | Floors of maxSlopeThreshold: float32

    /// Relaxes mesh vertex positions using neighbor-weighted Laplacian operator
    /// If lockBoundaries is true, open perimeter vertices are strictly preserved to maintain airtight BSP sealing.
    let smoothMeshVertices
        (positions: Vector3[])
        (triangles: (int * int * int)[])
        (passes: int)
        (strength: float32)
        (target: SmoothTarget)
        (lockBoundaries: bool) : int =

        if positions.Length < 3 || triangles.Length = 0 then 0
        else
            // 1. Build adjacency graph
            let adjacency = Array.init positions.Length (fun _ -> HashSet<int>())
            let edgeCounts = Dictionary<int64, int>()

            let makeEdgeKey (a: int) (b: int) =
                let mi = Math.Min(a, b)
                let ma = Math.Max(a, b)
                (int64 mi <<< 32) ||| int64 ma

            for (v0, v1, v2) in triangles do
                adjacency.[v0].Add(v1) |> ignore
                adjacency.[v0].Add(v2) |> ignore
                adjacency.[v1].Add(v0) |> ignore
                adjacency.[v1].Add(v2) |> ignore
                adjacency.[v2].Add(v0) |> ignore
                adjacency.[v2].Add(v1) |> ignore

                let e1 = makeEdgeKey v0 v1
                let e2 = makeEdgeKey v1 v2
                let e3 = makeEdgeKey v2 v0

                edgeCounts.[e1] <- edgeCounts.GetValueOrDefault(e1, 0) + 1
                edgeCounts.[e2] <- edgeCounts.GetValueOrDefault(e2, 0) + 1
                edgeCounts.[e3] <- edgeCounts.GetValueOrDefault(e3, 0) + 1

            // 2. Identify boundary vertices (edges referenced by exactly 1 triangle)
            let boundaryVerts = HashSet<int>()
            if lockBoundaries then
                for kvp in edgeCounts do
                    if kvp.Value = 1 then
                        let vA = int (kvp.Key >>> 32)
                        let vB = int (kvp.Key &&& 0xFFFFFFFFL)
                        boundaryVerts.Add(vA) |> ignore
                        boundaryVerts.Add(vB) |> ignore

            // 3. Elevation analysis for target filtering
            let mutable minZ = Single.MaxValue
            let mutable maxZ = Single.MinValue
            for p in positions do
                if p.Z < minZ then minZ <- p.Z
                if p.Z > maxZ then maxZ <- p.Z
            let spanZ = MathF.Max(1.0f, maxZ - minZ)

            let isTarget (idx: int) =
                if lockBoundaries && boundaryVerts.Contains(idx) then false
                else
                    match target with
                    | All -> true
                    | MountainPeaks ratio ->
                        (positions.[idx].Z - minZ) / spanZ >= ratio
                    | Cliffs _ -> true
                    | Floors _ -> true

            let mutable smoothedCount = 0
            let factor = Math.Clamp(strength, 0.01f, 0.95f)

            for _ in 1 .. Math.Max(1, passes) do
                let nextPositions = Array.copy positions
                for i in 0 .. positions.Length - 1 do
                    if isTarget i && adjacency.[i].Count > 0 then
                        let mutable sum = Vector3.Zero
                        for neighbor in adjacency.[i] do
                            sum <- sum + positions.[neighbor]
                        let avg = sum / float32 adjacency.[i].Count
                        nextPositions.[i] <- Vector3.Lerp(positions.[i], avg, factor)
                        smoothedCount <- smoothedCount + 1

                Array.Copy(nextPositions, positions, positions.Length)

            smoothedCount

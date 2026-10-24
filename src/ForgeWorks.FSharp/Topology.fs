namespace ForgeWorks.FSharp

open System
open System.Collections.Generic
open System.Numerics

/// Mesh boundary topology analysis: Boundary loops, open edge detection, and airtight +sky ceiling synthesis
module Topology =

    [<Struct>]
    type DirectedEdge =
        { V0: int
          V1: int }

    [<Struct>]
    type BoundaryLoop =
        { Vertices: int[]
          IsClosed: bool }

    /// Extracts directed open boundary half-edges (edges with no matching twin)
    let findOpenBoundaryEdges (triangles: (int * int * int)[]) : DirectedEdge[] =
        let edgeMap = Dictionary<int64, int>()

        let makeDirectedKey (a: int) (b: int) =
            (int64 a <<< 32) ||| (int64 b &&& 0xFFFFFFFFL)

        // Count directed occurrences
        for (v0, v1, v2) in triangles do
            let e1 = makeDirectedKey v0 v1
            let e2 = makeDirectedKey v1 v2
            let e3 = makeDirectedKey v2 v0
            edgeMap.[e1] <- edgeMap.GetValueOrDefault(e1, 0) + 1
            edgeMap.[e2] <- edgeMap.GetValueOrDefault(e2, 0) + 1
            edgeMap.[e3] <- edgeMap.GetValueOrDefault(e3, 0) + 1

        let openEdges = List<DirectedEdge>()
        for kvp in edgeMap do
            let v0 = int (kvp.Key >>> 32)
            let v1 = int (kvp.Key &&& 0xFFFFFFFFL)
            let twinKey = makeDirectedKey v1 v0
            // If the opposite direction does not exist in any triangle, it is an open boundary edge!
            if not (edgeMap.ContainsKey(twinKey)) then
                openEdges.Add({ V0 = v0; V1 = v1 })

        openEdges.ToArray()

    /// Chains directed boundary edges into continuous sequential loops
    let chainBoundaryLoops (edges: DirectedEdge[]) : BoundaryLoop[] =
        if edges.Length = 0 then Array.empty
        else
            let adj = Dictionary<int, int>()
            for e in edges do
                adj.[e.V0] <- e.V1

            let visited = HashSet<int>()
            let loops = List<BoundaryLoop>()

            for e in edges do
                if not (visited.Contains(e.V0)) then
                    let loopVerts = List<int>()
                    let mutable curr = e.V0
                    let mutable closed = false
                    let mutable doneLoop = false

                    while not doneLoop && adj.ContainsKey(curr) do
                        visited.Add(curr) |> ignore
                        loopVerts.Add(curr)
                        let next = adj.[curr]
                        if next = e.V0 then
                            closed <- true
                            doneLoop <- true
                        elif visited.Contains(next) then
                            doneLoop <- true
                        else
                            curr <- next

                    if loopVerts.Count >= 3 then
                        loops.Add({ Vertices = loopVerts.ToArray(); IsClosed = closed })

            loops.ToArray()

    /// Synthesizes an airtight top ceiling fan (e.g. +sky) connecting a top perimeter loop to a central ceiling apex
    let generateTopCeilingCap
        (positions: Vector3[])
        (loop: BoundaryLoop)
        (ceilingElevation: float32) : (Vector3 * (int * int * int)[]) =

        if loop.Vertices.Length < 3 then
            (Vector3.Zero, Array.empty)
        else
            // Calculate 2D centroid of boundary loop
            let mutable sumX = 0.0f
            let mutable sumY = 0.0f
            for vIdx in loop.Vertices do
                sumX <- sumX + positions.[vIdx].X
                sumY <- sumY + positions.[vIdx].Y

            let n = float32 loop.Vertices.Length
            let apex = Vector3(sumX / n, sumY / n, ceilingElevation)

            // Triangles from boundary loop to apex (downward facing for +sky ceiling)
            let tris = Array.zeroCreate loop.Vertices.Length
            for i in 0 .. loop.Vertices.Length - 1 do
                let curr = loop.Vertices.[i]
                let next = loop.Vertices.[(i + 1) % loop.Vertices.Length]
                // Downward-facing normal
                tris.[i] <- (curr, next, -1) // -1 denotes apex vertex index

            (apex, tris)

namespace ForgeWorks.FSharp

open System
open System.Numerics

/// Cellular & Voronoi mathematical algorithms for basalt columns, calderas, and cracked canyons
module Cellular =

    [<Struct>]
    type VoronoiResult =
        { DistanceF1: float32
          DistanceF2: float32
          CellId: int }
        member this.EdgeDistance = this.DistanceF2 - this.DistanceF1

    let private hashPoint (xi: int) (yi: int) (seed: int) : Vector2 =
        let n = xi * 374761393 + yi * 668265263 + seed * 962592769
        let n1 = (n ^^^ (n >>> 13)) * 1274126177
        let n2 = (n1 ^^^ (n1 >>> 15)) * 1698745241
        let fx = (float32 (n1 &&& 0xFFFF)) / 65535.0f
        let fy = (float32 (n2 &&& 0xFFFF)) / 65535.0f
        Vector2(fx, fy)

    /// Computes 2D Voronoi F1 and F2 Euclidean cell distances
    let voronoi2D (x: float32) (y: float32) (seed: int) : VoronoiResult =
        let xi = int (MathF.Floor x)
        let yi = int (MathF.Floor y)

        let mutable f1 = Single.MaxValue
        let mutable f2 = Single.MaxValue
        let mutable bestCell = 0

        for dy in -1 .. 1 do
            for dx in -1 .. 1 do
                let cx = xi + dx
                let cy = yi + dy
                let randOffset = hashPoint cx cy seed
                let cellPos = Vector2(float32 cx + randOffset.X, float32 cy + randOffset.Y)
                let samplePos = Vector2(x, y)
                let dist = Vector2.Distance(samplePos, cellPos)

                if dist < f1 then
                    f2 <- f1
                    f1 <- dist
                    bestCell <- cx * 31 + cy
                elif dist < f2 then
                    f2 <- dist

        { DistanceF1 = f1; DistanceF2 = f2; CellId = bestCell }

    /// Basalt Column generator: regular hexagonal honeycomb lattice with stepped pillar height
    let basaltHexagon (x: float32) (y: float32) (columnRadius: float32) : float32 =
        let r = MathF.Max(1.0f, columnRadius)
        let h = r * MathF.Sqrt(3.0f)

        let q = (2.0f / 3.0f * x) / r
        let rx = (-1.0f / 3.0f * x + MathF.Sqrt(3.0f) / 3.0f * y) / r

        // Axial round
        let qRound = MathF.Round q
        let rRound = MathF.Round rx
        let sRound = MathF.Round (-q - rx)

        let qDiff = MathF.Abs(qRound - q)
        let rDiff = MathF.Abs(rRound - rx)
        let sDiff = MathF.Abs(sRound - (-q - rx))

        let finalQ =
            if qDiff > rDiff && qDiff > sDiff then -rRound - sRound
            elif rDiff > sDiff then -qRound - sRound
            else qRound

        let finalR =
            if qDiff > rDiff && qDiff > sDiff then rRound
            elif rDiff > sDiff then -qRound - sRound
            else rRound

        // Hash column coordinates for stepped heights
        let cellHash = int (finalQ * 73856093.0f) ^^^ int (finalR * 19349663.0f)
        float32 (Math.Abs(cellHash % 100)) / 100.0f

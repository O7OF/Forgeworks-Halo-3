namespace ForgeWorks.FSharp

open System
open System.Numerics

/// High-performance 3D spatial mathematics and grid fitting functions
module Math3D =

    [<Struct>]
    type BoundingBox3D =
        { Min: Vector3
          Max: Vector3 }
        member this.Center = (this.Min + this.Max) * 0.5f
        member this.SpanX = this.Max.X - this.Min.X
        member this.SpanY = this.Max.Y - this.Min.Y
        member this.SpanZ = this.Max.Z - this.Min.Z
        member this.MaxHorizontalSpan = MathF.Max(this.SpanX, this.SpanY)
        member this.IsEmpty = this.Min.X > this.Max.X

        static member Empty =
            { Min = Vector3(Single.MaxValue, Single.MaxValue, Single.MaxValue)
              Max = Vector3(Single.MinValue, Single.MinValue, Single.MinValue) }

        static member FromPoints(points: seq<Vector3>) =
            let mutable minX = Single.MaxValue
            let mutable minY = Single.MaxValue
            let mutable minZ = Single.MaxValue
            let mutable maxX = Single.MinValue
            let mutable maxY = Single.MinValue
            let mutable maxZ = Single.MinValue
            let mutable count = 0

            for p in points do
                count <- count + 1
                if p.X < minX then minX <- p.X
                if p.X > maxX then maxX <- p.X
                if p.Y < minY then minY <- p.Y
                if p.Y > maxY then maxY <- p.Y
                if p.Z < minZ then minZ <- p.Z
                if p.Z > maxZ then maxZ <- p.Z

            if count = 0 then
                BoundingBox3D.Empty
            else
                { Min = Vector3(minX, minY, minZ)
                  Max = Vector3(maxX, maxY, maxZ) }

        member this.Include(other: BoundingBox3D) =
            if other.IsEmpty then this
            elif this.IsEmpty then other
            else
                { Min = Vector3(MathF.Min(this.Min.X, other.Min.X),
                                MathF.Min(this.Min.Y, other.Min.Y),
                                MathF.Min(this.Min.Z, other.Min.Z))
                  Max = Vector3(MathF.Max(this.Max.X, other.Max.X),
                                MathF.Max(this.Max.Y, other.Max.Y),
                                MathF.Max(this.Max.Z, other.Max.Z)) }

    [<Struct>]
    type GridFitTransform =
        { Scale: float32
          OffsetX: float32
          OffsetY: float32
          OffsetZ: float32 }
        member this.TransformPoint(p: Vector3) : Vector3 =
            Vector3((p.X + this.OffsetX) * this.Scale,
                    (p.Y + this.OffsetY) * this.Scale,
                    (p.Z + this.OffsetZ) * this.Scale)

    /// Computes uniform scale factor and translation offsets to fit an arbitrary bounding box
    /// cleanly into targetGridExtent (e.g. 7000 units), centering at (0, 0) and aligning base at Z = 0.
    let calculateGridFit (bounds: BoundingBox3D) (targetExtent: float32) (centerAtOrigin: bool) (groundBase: bool) : GridFitTransform =
        if bounds.IsEmpty || bounds.MaxHorizontalSpan <= 1e-4f then
            { Scale = 1.0f; OffsetX = 0.0f; OffsetY = 0.0f; OffsetZ = 0.0f }
        else
            let scale = targetExtent / bounds.MaxHorizontalSpan
            let midX = (bounds.Min.X + bounds.Max.X) * 0.5f
            let midY = (bounds.Min.Y + bounds.Max.Y) * 0.5f
            let offX = if centerAtOrigin then -midX else 0.0f
            let offY = if centerAtOrigin then -midY else 0.0f
            let offZ = if groundBase then -bounds.Min.Z else 0.0f
            { Scale = scale; OffsetX = offX; OffsetY = offY; OffsetZ = offZ }

    /// Calculates distortion-free triplanar UV coordinates for a 3D position and normal
    let calculateTriplanarUV (pos: Vector3) (normal: Vector3) (tileScale: float32) : Vector2 =
        let scale = if tileScale > 0.0f then 1.0f / tileScale else 1.0f / 300.0f
        let nx = MathF.Abs(normal.X)
        let ny = MathF.Abs(normal.Y)
        let nz = MathF.Abs(normal.Z)

        if nz >= nx && nz >= ny then
            // Dominant horizontal surface (ground / floor) -> Top-down XY projection
            Vector2(pos.X * scale, pos.Y * scale)
        elif nx >= ny then
            // Dominant X-facing vertical cliff/wall -> YZ projection
            Vector2(pos.Y * scale, pos.Z * scale)
        else
            // Dominant Y-facing vertical cliff/wall -> XZ projection
            Vector2(pos.X * scale, pos.Z * scale)

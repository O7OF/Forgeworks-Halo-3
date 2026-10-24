namespace ForgeWorks.FSharp

open System
open System.Numerics

/// Advanced geomorphic terrain erosion: Hydraulic droplet simulations and Thermal talus slumping
module Erosion =

    [<CLIMutable>]
    type HydraulicParams =
        { DropletCount: int
          MaxLifetime: int
          Inertia: float32
          SedimentCapacityFactor: float32
          MinSedimentCapacity: float32
          DepositSpeed: float32
          ErodeSpeed: float32
          EvaporateSpeed: float32
          Gravity: float32 }

        static member Default =
            { DropletCount = 10000
              MaxLifetime = 30
              Inertia = 0.05f
              SedimentCapacityFactor = 4.0f
              MinSedimentCapacity = 0.01f
              DepositSpeed = 0.3f
              ErodeSpeed = 0.3f
              EvaporateSpeed = 0.01f
              Gravity = 4.0f }

    /// Applies fast hydraulic droplet erosion directly to a 2D heightfield
    let applyHydraulicErosion (heights: float32[,]) (p: HydraulicParams) (seed: int) : unit =
        let width = heights.GetLength(0)
        let height = heights.GetLength(1)
        if width < 3 || height < 3 then ()
        else
            let rng = Random(seed)

            for _ in 1 .. p.DropletCount do
                let mutable posX = float32 (rng.Next(1, width - 2))
                let mutable posY = float32 (rng.Next(1, height - 2))
                let mutable dirX = 0.0f
                let mutable dirY = 0.0f
                let mutable speed = 1.0f
                let mutable water = 1.0f
                let mutable sediment = 0.0f
                let mutable alive = true
                let mutable steps = 0

                while alive && steps < p.MaxLifetime do
                    steps <- steps + 1
                    let nodeX = int posX
                    let nodeY = int posY
                    let cellOffsetX = posX - float32 nodeX
                    let cellOffsetY = posY - float32 nodeY

                    // Bilinear height and gradient
                    let h00 = heights.[nodeX, nodeY]
                    let h10 = heights.[nodeX + 1, nodeY]
                    let h01 = heights.[nodeX, nodeY + 1]
                    let h11 = heights.[nodeX + 1, nodeY + 1]

                    let gradX = (h10 - h00) * (1.0f - cellOffsetY) + (h11 - h01) * cellOffsetY
                    let gradY = (h01 - h00) * (1.0f - cellOffsetX) + (h11 - h10) * cellOffsetX

                    dirX <- dirX * p.Inertia - gradX * (1.0f - p.Inertia)
                    dirY <- dirY * p.Inertia - gradY * (1.0f - p.Inertia)

                    let len = MathF.Sqrt(dirX * dirX + dirY * dirY)
                    if len < 1e-5f then
                        alive <- false
                    else
                        dirX <- dirX / len
                        dirY <- dirY / len

                        let newPosX = posX + dirX
                        let newPosY = posY + dirY

                        if newPosX < 1.0f || newPosX >= float32 (width - 2) ||
                           newPosY < 1.0f || newPosY >= float32 (height - 2) then
                            alive <- false
                        else
                            let newH00 = heights.[int newPosX, int newPosY]
                            let deltaHeight = newH00 - h00

                            let capacity = MathF.Max(-deltaHeight * speed * water * p.SedimentCapacityFactor, p.MinSedimentCapacity)

                            if sediment > capacity || deltaHeight > 0.0f then
                                let amountToDeposit =
                                    if deltaHeight > 0.0f then MathF.Min(deltaHeight, sediment)
                                    else (sediment - capacity) * p.DepositSpeed
                                sediment <- sediment - amountToDeposit
                                heights.[nodeX, nodeY] <- heights.[nodeX, nodeY] + amountToDeposit
                            else
                                let amountToErode = MathF.Min((capacity - sediment) * p.ErodeSpeed, -deltaHeight)
                                sediment <- sediment + amountToErode
                                heights.[nodeX, nodeY] <- heights.[nodeX, nodeY] - amountToErode

                            speed <- MathF.Sqrt(MathF.Max(0.0f, speed * speed + deltaHeight * p.Gravity))
                            water <- water * (1.0f - p.EvaporateSpeed)
                            posX <- newPosX
                            posY <- newPosY

    /// Thermal erosion / Talus Slump: relaxes steep cliffs exceeding critical angle of repose
    let applyThermalErosion (heights: float32[,]) (talusAngleDeg: float32) (strength: float32) (passes: int) : unit =
        let width = heights.GetLength(0)
        let height = heights.GetLength(1)
        if width < 3 || height < 3 then ()
        else
            let threshold = MathF.Tan(talusAngleDeg * MathF.PI / 180.0f)

            for _ in 1 .. Math.Max(1, passes) do
                for x in 1 .. width - 2 do
                    for y in 1 .. height - 2 do
                        let h = heights.[x, y]
                        let neighbors = [|
                            (x - 1, y); (x + 1, y); (x, y - 1); (x, y + 1)
                        |]

                        for (nx, ny) in neighbors do
                            let diff = h - heights.[nx, ny]
                            if diff > threshold then
                                let transfer = (diff - threshold) * 0.5f * strength
                                heights.[x, y] <- heights.[x, y] - transfer
                                heights.[nx, ny] <- heights.[nx, ny] + transfer

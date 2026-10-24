open System
open System.IO

let path = @"C:\Users\Chey1\Desktop\Ice Fields\Icefields.obj"
let lines = File.ReadLines(path)
let mutable minX = Single.MaxValue
let mutable maxX = Single.MinValue
let mutable minY = Single.MaxValue
let mutable maxY = Single.MinValue
let mutable minZ = Single.MaxValue
let mutable maxZ = Single.MinValue
let mutable vCount = 0

for line in lines do
    if line.StartsWith("v ") then
        let parts = line.Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
        if parts.Length >= 4 then
            let x = Single.Parse(parts.[1])
            let y = Single.Parse(parts.[2])
            let z = Single.Parse(parts.[3])
            minX <- min minX x
            maxX <- max maxX x
            minY <- min minY y
            maxY <- max maxY y
            minZ <- min minZ z
            maxZ <- max maxZ z
            vCount <- vCount + 1

printfn "Vertices: %d" vCount
printfn "X: [%f, %f] Span: %f" minX maxX (maxX - minX)
printfn "Y: [%f, %f] Span: %f" minY maxY (maxY - minY)
printfn "Z: [%f, %f] Span: %f" minZ maxZ (maxZ - minZ)

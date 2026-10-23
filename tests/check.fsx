let ek = @"C:\program files (x86)\steam\steamapps\common\H3EK"
let chill = System.IO.Path.Combine(ek, "tags", "levels", "multi", "chill", "shaders", "chill_metal.shader")
let b = System.IO.File.ReadAllBytes(chill)

let oldTarget = @"levels\multi\riverworld\bitmaps\riverworld_metal"
let newTarget = @"levels\multi\icefields\bitmaps\boulder_snow_large"

let oldBytes = System.Text.Encoding.ASCII.GetBytes(oldTarget)
let newBytes = System.Text.Encoding.ASCII.GetBytes(newTarget)

// Replace ONLY occurrences that are exact match (not followed by '_bump')
for i in 0 .. (b.Length - oldBytes.Length) do
    let mutable matchFound = true
    for j in 0 .. (oldBytes.Length - 1) do
        if b.[i + j] <> oldBytes.[j] then matchFound <- false
    if matchFound then
        // Check if followed by '_bump'
        let isBump = (i + oldBytes.Length + 5 <= b.Length) && 
                     (System.Text.Encoding.ASCII.GetString(b, i + oldBytes.Length, 5) = "_bump")
        if not isBump then
            printfn "Replacing base_map occurrence at 0x%X (%d)" i i
            for j in 0 .. (newBytes.Length - 1) do
                b.[i + j] <- newBytes.[j]

let outPath = System.IO.Path.Combine(ek, "tags", "levels", "multi", "icefields", "shaders", "material_icefieldsicefields_ground.shader")
System.IO.File.WriteAllBytes(outPath, b)

let proc = new System.Diagnostics.Process()
proc.StartInfo.FileName <- System.IO.Path.Combine(ek, "tool_fast.exe")
proc.StartInfo.Arguments <- "structure \"levels\\multi\\icefields\\structure\\icefields.ass\""
proc.StartInfo.WorkingDirectory <- ek
proc.StartInfo.RedirectStandardOutput <- true
proc.StartInfo.RedirectStandardError <- true
proc.StartInfo.UseShellExecute <- false
proc.Start() |> ignore
let out = proc.StandardOutput.ReadToEnd()
proc.WaitForExit()

let lines = out.Split('\n') |> Array.filter (fun l -> l.Contains("ground.shader"))
for l in lines do
    printfn "Line: %s" l

if lines.Length = 0 then
    printfn "SUCCESS! Zero errors or warnings for material_icefieldsicefields_ground.shader!"

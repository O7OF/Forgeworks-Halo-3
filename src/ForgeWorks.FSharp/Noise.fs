namespace ForgeWorks.FSharp

open System
open System.Numerics

/// High-performance procedural noise generation: Simplex, fBm, Domain Warping, Ridge Multifractal
module Noise =

    // Permutation table for fast noise lookup
    let private perm =
        let p = [|
            151;160;137;91;90;15;131;13;201;95;96;53;194;233;7;225;140;36;103;30;69;142;
            8;99;37;240;21;10;23;190;6;148;247;120;234;75;0;26;197;62;94;252;219;203;117;
            35;11;32;57;177;33;88;237;149;56;87;174;20;125;136;171;168;68;175;74;165;71;
            134;139;48;27;166;77;146;158;231;83;111;229;122;60;211;133;230;220;105;92;41;
            55;46;245;40;244;102;143;54;65;25;63;161;1;216;80;73;209;76;132;187;208;89;
            18;169;200;196;135;130;116;188;159;86;164;100;109;198;173;186;3;64;52;217;226;
            250;124;123;5;202;38;147;118;126;255;82;85;212;207;206;59;227;47;16;58;17;182;
            189;28;42;223;183;170;213;119;248;152;2;44;154;163;70;221;153;101;155;167;43;
            172;9;129;22;39;253;19;98;108;110;79;113;224;232;178;185;112;104;218;246;97;
            228;251;34;242;193;238;210;144;12;191;179;162;241;81;51;145;235;249;14;239;
            107;49;192;214;31;181;199;106;157;184;84;204;176;115;121;50;45;127;4;150;254;
            138;236;205;93;222;114;67;29;24;72;243;141;128;195;78;66;215;61;156;180
        |]
        let table = Array.zeroCreate 512
        for i in 0 .. 255 do
            table.[i] <- p.[i]
            table.[256 + i] <- p.[i]
        table

    let inline private fade (t: float32) = t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f)
    let inline private lerp (t: float32) (a: float32) (b: float32) = a + t * (b - a)

    let private grad2D (hash: int) (x: float32) (y: float32) =
        let h = hash &&& 7
        let u = if h < 4 then x else y
        let v = if h < 4 then y else x
        (if (h &&& 1) = 0 then u else -u) + (if (h &&& 2) = 0 then v else -v)

    /// Computes 2D Perlin gradient noise in [-1, 1]
    let perlin2D (x: float32) (y: float32) : float32 =
        let xi = int (MathF.Floor x) &&& 255
        let yi = int (MathF.Floor y) &&& 255
        let xf = x - MathF.Floor x
        let yf = y - MathF.Floor y

        let u = fade xf
        let v = fade yf

        let aa = perm.[perm.[xi] + yi]
        let ab = perm.[perm.[xi] + yi + 1]
        let ba = perm.[perm.[xi + 1] + yi]
        let bb = perm.[perm.[xi + 1] + yi + 1]

        let x1 = lerp u (grad2D aa xf yf) (grad2D ba (xf - 1.0f) yf)
        let x2 = lerp u (grad2D ab xf (yf - 1.0f)) (grad2D bb (xf - 1.0f) (yf - 1.0f))
        lerp v x1 x2

    /// Fractional Brownian Motion (fBm) accumulating multiple octaves of Perlin noise
    let fbm2D (x: float32) (y: float32) (octaves: int) (lacunarity: float32) (gain: float32) : float32 =
        let mutable total = 0.0f
        let mutable frequency = 1.0f
        let mutable amplitude = 1.0f
        let mutable maxAmp = 0.0f

        for _ in 1 .. Math.Max(1, octaves) do
            total <- total + perlin2D (x * frequency) (y * frequency) * amplitude
            maxAmp <- maxAmp + amplitude
            frequency <- frequency * lacunarity
            amplitude <- amplitude * gain

        if maxAmp > 0.0f then total / maxAmp else 0.0f

    /// Domain-warped Perlin noise: f(p + warp * noise(p)) for natural river valleys & canyon ridges
    let domainWarp2D (x: float32) (y: float32) (warpStrength: float32) (octaves: int) : float32 =
        let qx = fbm2D (x + 0.0f) (y + 0.0f) octaves 2.0f 0.5f
        let qy = fbm2D (x + 5.2f) (y + 1.3f) octaves 2.0f 0.5f
        fbm2D (x + warpStrength * qx) (y + warpStrength * qy) octaves 2.0f 0.5f

    /// Ridged multifractal noise for sharp alpine mountain crests and ridges: 1.0 - abs(noise)
    let ridgedMultifractal2D (x: float32) (y: float32) (octaves: int) (lacunarity: float32) (gain: float32) : float32 =
        let mutable total = 0.0f
        let mutable frequency = 1.0f
        let mutable amplitude = 1.0f
        let mutable weight = 1.0f
        let mutable maxAmp = 0.0f

        for _ in 1 .. Math.Max(1, octaves) do
            let signal = 1.0f - MathF.Abs(perlin2D (x * frequency) (y * frequency))
            let s2 = signal * signal * weight
            weight <- Math.Clamp(s2 * 2.0f, 0.0f, 1.0f)
            total <- total + s2 * amplitude
            maxAmp <- maxAmp + amplitude
            frequency <- frequency * lacunarity
            amplitude <- amplitude * gain

        if maxAmp > 0.0f then total / maxAmp else 0.0f

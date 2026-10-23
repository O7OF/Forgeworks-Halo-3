using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering;
using ForgeWorks.Core.Diagnostics;
using ForgeWorks.Core.Geometry;
using ForgeWorks.Core.Rendering;
using ForgeWorks.UI.ViewModels;
using Silk.NET.OpenGL;

namespace ForgeWorks.UI.Views;

/// <summary>
/// Hardware-accelerated Blam! viewport. Works on both desktop OpenGL and OpenGL ES
/// (Avalonia on Windows renders through ANGLE/GLES 3 by default, so shaders are
/// emitted with a version header chosen at runtime).
/// </summary>
public sealed class BlamViewportControl : OpenGlControlBase, ICustomHitTest
{
    private const int FloatsPerVertex = 10; // pos3 + normal3 + color4
    private const int ModeTerrainBlend = 0;
    private const int ModeUnlit = 1;
    private const int ModeLitSolid = 2;

    private GL? _gl;
    private bool _isGles;
    private bool _glReady;
    private uint _program;
    private int _locMvp, _locLightDir, _locBaseColor, _locMode;

    // Active mesh buffers
    private uint _meshVao, _meshVbo, _meshTriEbo, _meshLineEbo;
    private int _meshTriIndexCount, _meshLineIndexCount;
    private int _uploadedMeshVersion = -1;
    private MeshObject? _uploadedMesh;

    private readonly struct SubMeshBatch
    {
        public readonly int Mode;
        public readonly Vector4 BaseColor;
        public readonly uint StartIndex;
        public readonly uint IndexCount;
        public readonly bool IsSky;
        public readonly bool IsTransparent;

        public SubMeshBatch(int mode, Vector4 baseColor, uint startIndex, uint indexCount, bool isSky, bool isTransparent)
        {
            Mode = mode;
            BaseColor = baseColor;
            StartIndex = startIndex;
            IndexCount = indexCount;
            IsSky = isSky;
            IsTransparent = isTransparent;
        }
    }
    private readonly List<SubMeshBatch> _subBatches = new();

    // Grid
    private uint _gridVao, _gridVbo;
    private int _gridVertexCount;

    // Overlays (lint, scenario, WRL) - depth-tested and x-ray layers
    private uint _overlayVao, _overlayVbo;
    private int _overlayDepthCount, _overlayXrayCount;
    private int _builtOverlayVersion = -1;

    // Brush cursor
    private uint _cursorVao, _cursorVbo;
    private int _cursorVertexCount;

    // Depth renderbuffer attached to Avalonia's FBO
    private uint _depthRbo;
    private int _depthW, _depthH;

    // 3D Transform Gizmo (Sapien)
    private uint _gizmoVao, _gizmoVbo;
    private int _gizmoVertexCount;
    private int _hoveredGizmoAxis; // 0=none, 1=X, 2=Y, 3=Z
    private int _draggedGizmoAxis;
    private bool _isDraggingGizmo;

    private int _handledFrameRequest = -1;
    private int _frameCounter;

    private readonly BlamCamera _camera = new();
    private Point _lastPointerPos;
    private bool _isOrbiting, _isPanning, _isStroking;
    private Vector3? _brushHit;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForgeWorks", "viewport.log");

    private static readonly bool SelfTest =
        Environment.GetEnvironmentVariable("FORGEWORKS_SELFTEST") == "1";

    public BlamCamera Camera => _camera;

    public BlamViewportControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    // OpenGlControlBase draws nothing in Avalonia's scene graph, so it isn't hit-testable by default.
    public bool HitTest(Point point) => true;

    private MainViewModel? Vm => DataContext as MainViewModel;

    #region GL lifecycle

    protected override void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.WriteAllText(LogPath, $"[{DateTime.Now:O}] ForgeWorks viewport init\n");

            _gl = GL.GetApi(gl.GetProcAddress);
            string version = _gl.GetStringS(StringName.Version) ?? "unknown";
            string renderer = _gl.GetStringS(StringName.Renderer) ?? "unknown";
            _isGles = version.Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase);
            Log($"GL_VERSION  = {version}");
            Log($"GL_RENDERER = {renderer}");
            Log($"Profile     = {(_isGles ? "OpenGL ES (ANGLE)" : "Desktop OpenGL")}");

            _program = BuildProgram();
            _locMvp = _gl.GetUniformLocation(_program, "uMVP");
            _locLightDir = _gl.GetUniformLocation(_program, "uLightDir");
            _locBaseColor = _gl.GetUniformLocation(_program, "uBaseColor");
            _locMode = _gl.GetUniformLocation(_program, "uMode");

            _meshVao = _gl.GenVertexArray();
            _meshVbo = _gl.GenBuffer();
            _meshTriEbo = _gl.GenBuffer();
            _meshLineEbo = _gl.GenBuffer();
            (_gridVao, _gridVbo) = CreateLineBuffer();
            (_overlayVao, _overlayVbo) = CreateLineBuffer();
            (_cursorVao, _cursorVbo) = CreateLineBuffer();
            (_gizmoVao, _gizmoVbo) = CreateLineBuffer();

            BuildGrid(extent: 20000.0f, step: 200.0f);

            _glReady = _program != 0;
            SetInfo(_glReady
                ? $"{(_isGles ? "GLES" : "GL")} | {renderer}"
                : $"Shader build FAILED - see {LogPath}");
        }
        catch (Exception ex)
        {
            Log("INIT EXCEPTION: " + ex);
            SetInfo("OpenGL init failed - see " + LogPath);
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_gl != null)
        {
            foreach (var b in new[] { _meshVbo, _meshTriEbo, _meshLineEbo, _gridVbo, _overlayVbo, _cursorVbo, _gizmoVbo })
                _gl.DeleteBuffer(b);
            foreach (var a in new[] { _meshVao, _gridVao, _overlayVao, _cursorVao, _gizmoVao })
                _gl.DeleteVertexArray(a);
            if (_depthRbo != 0)
            {
                _gl.DeleteRenderbuffer(_depthRbo);
                _depthRbo = 0;
            }
            _gl.DeleteProgram(_program);
        }
        _glReady = false;
        base.OnOpenGlDeinit(gl);
    }

    private uint BuildProgram()
    {
        string header = _isGles ? "#version 300 es\nprecision highp float;\n" : "#version 330 core\n";

        const string vsBody = @"
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec4 aColor;
uniform mat4 uMVP;
out vec3 vNormal;
out vec4 vColor;
void main()
{
    vNormal = aNormal;
    vColor = aColor;
    gl_Position = uMVP * vec4(aPos, 1.0);
}";

        // uMode: 0 = lit 4-layer terrain blend, 1 = unlit vertex color, 2 = lit solid uBaseColor
        const string fsBody = @"
in vec3 vNormal;
in vec4 vColor;
uniform vec3 uLightDir;
uniform vec4 uBaseColor;
uniform int uMode;
out vec4 FragColor;
void main()
{
    if (uMode == 1) { FragColor = vColor; return; }

    vec3 n = normalize(vNormal);
    if (!gl_FrontFacing) n = -n; // Two-sided lighting (sky/BSP normals face inward)
    float diff = max(dot(n, normalize(uLightDir)), 0.0);
    float hemi = 0.5 + 0.5 * n.z;   // Sky/ground ambient
    float lighting = 0.18 + 0.22 * hemi + 0.65 * diff;

    vec3 baseCol = uBaseColor.rgb;
    if (uMode == 0)
    {
        // Halo 3 shader_terrain style 4-way blend driven by Terrain_Layers vertex color.
        vec3 colBase = vec3(0.47, 0.50, 0.33); // grass / sand
        vec3 colRock = vec3(0.42, 0.39, 0.36); // cliff rock     (R)
        vec3 colSnow = vec3(0.88, 0.90, 0.94); // alpine snow    (G)
        vec3 colMud  = vec3(0.30, 0.24, 0.18); // wet mud        (B)
        float wBase = clamp(1.0 - (vColor.r + vColor.g + vColor.b), 0.0, 1.0);
        baseCol = colBase * wBase + colRock * vColor.r + colSnow * vColor.g + colMud * vColor.b;
    }
    FragColor = vec4(baseCol * lighting, uBaseColor.a);
}";

        uint vs = CompileShader(ShaderType.VertexShader, header + vsBody);
        uint fs = CompileShader(ShaderType.FragmentShader, header + fsBody);
        if (vs == 0 || fs == 0) return 0;

        uint prog = _gl!.CreateProgram();
        _gl.AttachShader(prog, vs);
        _gl.AttachShader(prog, fs);
        _gl.BindAttribLocation(prog, 0, "aPos");
        _gl.BindAttribLocation(prog, 1, "aNormal");
        _gl.BindAttribLocation(prog, 2, "aColor");
        _gl.LinkProgram(prog);
        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);

        _gl.GetProgram(prog, ProgramPropertyARB.LinkStatus, out int linked);
        if (linked == 0)
        {
            Log("PROGRAM LINK FAILED:\n" + _gl.GetProgramInfoLog(prog));
            _gl.DeleteProgram(prog);
            return 0;
        }
        Log("Shader program linked OK.");
        return prog;
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl!.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int ok);
        if (ok == 0)
        {
            Log($"{type} COMPILE FAILED:\n{_gl.GetShaderInfoLog(shader)}");
            _gl.DeleteShader(shader);
            return 0;
        }
        return shader;
    }

    private unsafe (uint vao, uint vbo) CreateLineBuffer()
    {
        uint vao = _gl!.GenVertexArray();
        uint vbo = _gl.GenBuffer();
        _gl.BindVertexArray(vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        SetupVertexLayout();
        _gl.BindVertexArray(0);
        return (vao, vbo);
    }

    private unsafe void SetupVertexLayout()
    {
        uint stride = FloatsPerVertex * sizeof(float);
        _gl!.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
        _gl.EnableVertexAttribArray(2);
    }

    private unsafe void UploadLines(uint vbo, List<float> data)
    {
        _gl!.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        if (data.Count == 0)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, 0, null, BufferUsageARB.DynamicDraw);
            return;
        }
        var arr = data.ToArray();
        fixed (float* p = arr)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(arr.Length * sizeof(float)), p, BufferUsageARB.DynamicDraw);
        }
    }

    #endregion

    #region Geometry builders

    private static void AddLineVertex(List<float> buf, Vector3 p, Vector4 c)
    {
        buf.Add(p.X); buf.Add(p.Y); buf.Add(p.Z);
        buf.Add(0); buf.Add(0); buf.Add(1);
        buf.Add(c.X); buf.Add(c.Y); buf.Add(c.Z); buf.Add(c.W);
    }

    private static void AddLine(List<float> buf, Vector3 a, Vector3 b, Vector4 c)
    {
        AddLineVertex(buf, a, c);
        AddLineVertex(buf, b, c);
    }

    private static void AddCircle(List<float> buf, Vector3 center, float radius, Vector4 c, int segments = 32)
    {
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * MathF.Tau / segments;
            float a1 = (i + 1) * MathF.Tau / segments;
            AddLine(buf,
                center + new Vector3(MathF.Cos(a0) * radius, MathF.Sin(a0) * radius, 0),
                center + new Vector3(MathF.Cos(a1) * radius, MathF.Sin(a1) * radius, 0), c);
        }
    }

    private static void AddCross(List<float> buf, Vector3 p, float s, Vector4 c)
    {
        AddLine(buf, p - new Vector3(s, 0, 0), p + new Vector3(s, 0, 0), c);
        AddLine(buf, p - new Vector3(0, s, 0), p + new Vector3(0, s, 0), c);
        AddLine(buf, p - new Vector3(0, 0, s), p + new Vector3(0, 0, s), c);
    }

    private static void AddWireBox(List<float> buf, Vector3 center, Vector3 size, Vector4 c)
    {
        var h = size * 0.5f;
        var corners = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            corners[i] = center + new Vector3(
                (i & 1) == 0 ? -h.X : h.X,
                (i & 2) == 0 ? -h.Y : h.Y,
                (i & 4) == 0 ? -h.Z : h.Z);
        }
        int[] edges = { 0,1, 2,3, 4,5, 6,7, 0,2, 1,3, 4,6, 5,7, 0,4, 1,5, 2,6, 3,7 };
        for (int i = 0; i < edges.Length; i += 2)
            AddLine(buf, corners[edges[i]], corners[edges[i + 1]], c);
    }

    private unsafe void BuildGrid(float extent, float step)
    {
        var buf = new List<float>();
        var minor = new Vector4(0.28f, 0.32f, 0.38f, 1f);
        var major = new Vector4(0.48f, 0.52f, 0.60f, 1f);

        int lineIndex = 0;
        for (float i = -extent; i <= extent + 0.01f; i += step, lineIndex++)
        {
            var c = (lineIndex % 5 == 0) ? major : minor;
            AddLine(buf, new Vector3(i, -extent, 0), new Vector3(i, extent, 0), c);
            AddLine(buf, new Vector3(-extent, i, 0), new Vector3(extent, i, 0), c);
        }

        // Blam axes: +X forward (red), +Y left (green), +Z up (blue)
        AddLine(buf, Vector3.Zero, new Vector3(2500, 0, 0), new Vector4(1f, 0.25f, 0.25f, 1f));
        AddLine(buf, Vector3.Zero, new Vector3(0, 2500, 0), new Vector4(0.25f, 1f, 0.35f, 1f));
        AddLine(buf, Vector3.Zero, new Vector3(0, 0, 2500), new Vector4(0.35f, 0.65f, 1f, 1f));

        _gridVertexCount = buf.Count / FloatsPerVertex;
        UploadLines(_gridVbo, buf);
    }

    private unsafe void UploadSceneMeshes(MainViewModel vm)
    {
        _subBatches.Clear();
        var allMeshes = vm.SceneGraph.GetLevelMeshes();
        allMeshes.AddRange(vm.SceneGraph.GetPortalMeshes());

        // Only fall back to ActiveMesh if the scene graph hierarchy is completely empty
        if (!vm.SceneGraph.HasAnyGeometryNodes && vm.ActiveMesh != null)
        {
            allMeshes.Add(vm.ActiveMesh);
        }

        if (allMeshes.Count == 0)
        {
            _meshTriIndexCount = 0;
            _meshLineIndexCount = 0;
            return;
        }

        var allVerts = new List<float>();
        var allTris = new List<uint>();
        var allLines = new List<uint>();

        foreach (var mesh in allMeshes)
        {
            if (mesh.Vertices.Count == 0 || mesh.Triangles.Count == 0)
                continue;

            uint baseVertIdx = (uint)(allVerts.Count / FloatsPerVertex);
            uint triStart = (uint)allTris.Count;

            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                var v = mesh.Vertices[i];
                allVerts.Add(v.Position.X);
                allVerts.Add(v.Position.Y);
                allVerts.Add(v.Position.Z);
                allVerts.Add(v.Normal.X);
                allVerts.Add(v.Normal.Y);
                allVerts.Add(v.Normal.Z);
                allVerts.Add(v.Color.X);
                allVerts.Add(v.Color.Y);
                allVerts.Add(v.Color.Z);
                allVerts.Add(v.Color.W);
            }

            if (mesh.MaterialSlots.Count > 1)
            {
                // Multi-material mesh (e.g. tree with bark trunk + foliage cards, or structure with wall + trim)
                var groups = new Dictionary<int, List<Triangle>>();
                foreach (var t in mesh.Triangles)
                {
                    if (!groups.TryGetValue(t.MaterialIndex, out var gList))
                    {
                        gList = new List<Triangle>();
                        groups[t.MaterialIndex] = gList;
                    }
                    gList.Add(t);
                }

                foreach (var kvp in groups)
                {
                    uint subTriStart = (uint)allTris.Count;
                    int matIdx = kvp.Key;
                    foreach (var t in kvp.Value)
                    {
                        uint v0 = baseVertIdx + (uint)t.V0;
                        uint v1 = baseVertIdx + (uint)t.V1;
                        uint v2 = baseVertIdx + (uint)t.V2;

                        allTris.Add(v0);
                        allTris.Add(v1);
                        allTris.Add(v2);

                        allLines.Add(v0); allLines.Add(v1);
                        allLines.Add(v1); allLines.Add(v2);
                        allLines.Add(v2); allLines.Add(v0);
                    }

                    uint subCount = (uint)allTris.Count - subTriStart;
                    var (mode, color) = ClassifyMaterial(mesh, matIdx);
                    string matName = (matIdx >= 0 && matIdx < mesh.MaterialSlots.Count) ? mesh.MaterialSlots[matIdx] : mesh.MaterialName;
                    bool isSky = mesh.Name.StartsWith("+sky", StringComparison.OrdinalIgnoreCase) || matName.StartsWith("+sky", StringComparison.OrdinalIgnoreCase);
                    bool isTransparent = isSky || mesh.Name.Contains("water", StringComparison.OrdinalIgnoreCase) || matName.Contains("water", StringComparison.OrdinalIgnoreCase) || matName.StartsWith("+portal", StringComparison.OrdinalIgnoreCase);

                    _subBatches.Add(new SubMeshBatch(mode, color, subTriStart, subCount, isSky, isTransparent));
                }
            }
            else
            {
                for (int i = 0; i < mesh.Triangles.Count; i++)
                {
                    var t = mesh.Triangles[i];
                    uint v0 = baseVertIdx + (uint)t.V0;
                    uint v1 = baseVertIdx + (uint)t.V1;
                    uint v2 = baseVertIdx + (uint)t.V2;

                    allTris.Add(v0);
                    allTris.Add(v1);
                    allTris.Add(v2);

                    allLines.Add(v0); allLines.Add(v1);
                    allLines.Add(v1); allLines.Add(v2);
                    allLines.Add(v2); allLines.Add(v0);
                }

                uint triCount = (uint)allTris.Count - triStart;
                var (mode, color) = ClassifyMaterial(mesh, 0);
                bool isSky = mesh.Name.StartsWith("+sky", StringComparison.OrdinalIgnoreCase) || mesh.MaterialName.StartsWith("+sky", StringComparison.OrdinalIgnoreCase);
                bool isTransparent = isSky || mesh.Name.Contains("water", StringComparison.OrdinalIgnoreCase) || mesh.MaterialName.Contains("water", StringComparison.OrdinalIgnoreCase) || mesh.MaterialName.StartsWith("+portal", StringComparison.OrdinalIgnoreCase);

                _subBatches.Add(new SubMeshBatch(mode, color, triStart, triCount, isSky, isTransparent));
            }
        }

        _meshTriIndexCount = allTris.Count;
        _meshLineIndexCount = allLines.Count;

        if (_meshTriIndexCount == 0) return;

        var vertsArray = allVerts.ToArray();
        var trisArray = allTris.ToArray();
        var linesArray = allLines.ToArray();

        _gl!.BindVertexArray(_meshVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _meshVbo);
        fixed (float* p = vertsArray)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertsArray.Length * sizeof(float)), p, BufferUsageARB.DynamicDraw);
        SetupVertexLayout();

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _meshLineEbo);
        fixed (uint* p = linesArray)
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(linesArray.Length * sizeof(uint)), p, BufferUsageARB.DynamicDraw);

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _meshTriEbo);
        fixed (uint* p = trisArray)
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(trisArray.Length * sizeof(uint)), p, BufferUsageARB.DynamicDraw);

        _gl.BindVertexArray(0);
    }

    private void BuildOverlays(MainViewModel vm)
    {
        var depthBuf = new List<float>();
        var xrayBuf = new List<float>();

        float markerScale = 5.0f;
        if (vm.ActiveMesh != null && vm.ActiveMesh.Vertices.Count > 0)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var v in vm.ActiveMesh.Vertices)
            {
                min = Vector3.Min(min, v.Position);
                max = Vector3.Max(max, v.Position);
            }
            markerScale = MathF.Max((max - min).Length() * 0.004f, 3.0f);
        }

        // Lint issues: exact failing edges where known, crosses otherwise (WRL-style colors)
        if (vm.ShowLintOverlay)
        {
            foreach (var issue in vm.LintIssues)
            {
                var c = issue.Severity switch
                {
                    LintSeverity.Error => new Vector4(1.0f, 0.15f, 0.15f, 1f),
                    LintSeverity.Warning => new Vector4(1.0f, 0.75f, 0.1f, 1f),
                    _ => new Vector4(0.3f, 0.8f, 1.0f, 1f)
                };
                if (issue.SegmentA.HasValue && issue.SegmentB.HasValue)
                {
                    var lift = new Vector3(0, 0, markerScale * 0.15f);
                    AddLine(depthBuf, issue.SegmentA.Value + lift, issue.SegmentB.Value + lift, c);
                }
                else if (issue.Position != Vector3.Zero)
                {
                    AddCross(xrayBuf, issue.Position, markerScale, c);
                }
            }
        }

        // Scenario entities (Sapien-style markers)
        if (vm.ShowScenarioOverlay && vm.SceneGraph.EntitiesRoot.IsVisible)
        {
            foreach (var s in vm.Spawns)
            {
                var sNode = vm.SceneGraph.EntitiesRoot.Children.FirstOrDefault(c => ReferenceEquals(c.TagPayload, s));
                if (sNode != null && !sNode.IsVisible) continue;

                var c = s.TeamDesignator == 0 ? new Vector4(1f, 0.35f, 0.3f, 1f) : new Vector4(0.35f, 0.6f, 1f, 1f);
                const float height = 70.3f; // Master Chief reference height in JMS units
                float yaw = s.FacingAngle * MathF.PI / 180.0f;
                var dir = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0);
                AddCircle(xrayBuf, s.Position, 16.0f, c, 20);
                AddLine(xrayBuf, s.Position, s.Position + new Vector3(0, 0, height), c);
                var head = s.Position + new Vector3(0, 0, height * 0.6f);
                var tip = head + dir * 45.0f;
                AddLine(xrayBuf, head, tip, c);
                var side = new Vector3(-dir.Y, dir.X, 0) * 12.0f;
                AddLine(xrayBuf, tip, tip - dir * 15.0f + side, c);
                AddLine(xrayBuf, tip, tip - dir * 15.0f - side, c);
            }

            var eqCol = new Vector4(1f, 0.85f, 0.2f, 1f);
            foreach (var e in vm.Equipment)
            {
                var eqNode = vm.SceneGraph.EntitiesRoot.Children.FirstOrDefault(c => ReferenceEquals(c.TagPayload, e));
                if (eqNode != null && !eqNode.IsVisible) continue;

                const float r = 18.0f;
                var p = e.Position;
                Vector3[] pts = { p + new Vector3(r, 0, 0), p + new Vector3(0, r, 0), p + new Vector3(-r, 0, 0), p + new Vector3(0, -r, 0) };
                var top = p + new Vector3(0, 0, r * 1.4f);
                var bottom = p - new Vector3(0, 0, r * 1.4f);
                for (int i = 0; i < 4; i++)
                {
                    AddLine(xrayBuf, pts[i], pts[(i + 1) % 4], eqCol);
                    AddLine(xrayBuf, pts[i], top, eqCol);
                    AddLine(xrayBuf, pts[i], bottom, eqCol);
                }
            }

            foreach (var vol in vm.Volumes)
            {
                var vNode = vm.SceneGraph.EntitiesRoot.Children.FirstOrDefault(c => ReferenceEquals(c.TagPayload, vol));
                if (vNode != null && !vNode.IsVisible) continue;

                var c = vol.VolumeType == "KILL" ? new Vector4(1f, 0.2f, 0.2f, 1f) : new Vector4(0.3f, 1f, 0.5f, 1f);
                AddWireBox(depthBuf, vol.Position, vol.Extents, c);
            }
        }

        // Model / Scenario Markers (#primary_trigger, $weapon_origin, #sight, #barrel, etc.)
        if (vm.SceneGraph.MarkersRoot.IsVisible)
        {
            foreach (var m in vm.Markers)
            {
                var mNode = vm.SceneGraph.MarkersRoot.Children.FirstOrDefault(c => ReferenceEquals(c.TagPayload, m));
                if (mNode != null && !mNode.IsVisible) continue;

                var markerCol = m.Name.StartsWith("#") ? new Vector4(1f, 0.5f, 0.1f, 1f) : new Vector4(0.2f, 0.8f, 1f, 1f);
                AddCross(xrayBuf, m.Position, MathF.Max(m.Radius * 1.5f, 6.0f), markerCol);
                AddCircle(xrayBuf, m.Position, MathF.Max(m.Radius, 4.0f), markerCol, 16);
            }
        }

        // Tool-emitted WRL error geometry (always x-ray: errors are often inside the BSP)
        if (vm.ShowWrlOverlay)
        {
            foreach (var w in vm.WrlLines)
            {
                AddLine(xrayBuf, w.PointA, w.PointB, new Vector4(w.Color, 1f));
            }
        }

        // Sub-element selections (Faces)
        if (vm.ActiveMesh != null && vm.SelectedFaceIndices.Count > 0)
        {
            var faceGold = new Vector4(1.0f, 0.78f, 0.15f, 1.0f);
            var faceCenterCol = new Vector4(1.0f, 0.95f, 0.3f, 1.0f);
            foreach (var faceIdx in vm.SelectedFaceIndices)
            {
                if (faceIdx >= 0 && faceIdx < vm.ActiveMesh.Triangles.Count)
                {
                    var tri = vm.ActiveMesh.Triangles[faceIdx];
                    var v0 = vm.ActiveMesh.Vertices[tri.V0].Position;
                    var v1 = vm.ActiveMesh.Vertices[tri.V1].Position;
                    var v2 = vm.ActiveMesh.Vertices[tri.V2].Position;
                    var center = (v0 + v1 + v2) / 3.0f;
                    var n = tri.Normal;
                    if (n.LengthSquared() > 0.001f) n = Vector3.Normalize(n);
                    var offset = n * 0.15f;

                    // Outline of face
                    AddLine(depthBuf, v0 + offset, v1 + offset, faceGold);
                    AddLine(depthBuf, v1 + offset, v2 + offset, faceGold);
                    AddLine(depthBuf, v2 + offset, v0 + offset, faceGold);

                    // Cross at center (depth tested and xray)
                    AddCross(depthBuf, center + offset, markerScale * 0.4f, faceCenterCol);
                    AddCross(xrayBuf, center + offset, markerScale * 0.25f, faceGold);
                }
            }
        }

        _overlayDepthCount = depthBuf.Count / FloatsPerVertex;
        _overlayXrayCount = xrayBuf.Count / FloatsPerVertex;
        depthBuf.AddRange(xrayBuf);
        UploadLines(_overlayVbo, depthBuf);
    }

    private void BuildCursor(MainViewModel vm)
    {
        var buf = new List<float>();
        if (_brushHit.HasValue && vm.ToolMode != ViewportTool.Navigate)
        {
            var c = vm.ToolMode == ViewportTool.Sculpt
                ? new Vector4(0.3f, 1f, 0.6f, 1f)
                : vm.PaintLayerIndex switch
                {
                    1 => new Vector4(1f, 0.4f, 0.3f, 1f),
                    2 => new Vector4(0.9f, 0.95f, 1f, 1f),
                    3 => new Vector4(0.75f, 0.5f, 0.25f, 1f),
                    _ => new Vector4(0.6f, 0.9f, 0.4f, 1f)
                };
            float r = (float)vm.BrushRadius;
            AddCircle(buf, _brushHit.Value, r, c, 48);
            AddCircle(buf, _brushHit.Value, r * 0.5f, c * new Vector4(1, 1, 1, 0.5f), 32);
            AddLine(buf, _brushHit.Value, _brushHit.Value + new Vector3(0, 0, r * 0.3f), c);
        }
        _cursorVertexCount = buf.Count / FloatsPerVertex;
        UploadLines(_cursorVbo, buf);
    }

    private void BuildGizmo(MainViewModel vm)
    {
        var buf = new List<float>();
        if (vm.IsGizmoActive && vm.ActiveGizmoPosition.HasValue)
        {
            var p = vm.ActiveGizmoPosition.Value;
            float len = Math.Clamp(_camera.Distance * 0.15f, 40.0f, 600.0f);
            float arrow = len * 0.2f;

            var colX = _hoveredGizmoAxis == 1 || _draggedGizmoAxis == 1 ? new Vector4(1f, 1f, 0.2f, 1f) : new Vector4(1f, 0.2f, 0.2f, 1f);
            var colY = _hoveredGizmoAxis == 2 || _draggedGizmoAxis == 2 ? new Vector4(1f, 1f, 0.2f, 1f) : new Vector4(0.2f, 1f, 0.3f, 1f);
            var colZ = _hoveredGizmoAxis == 3 || _draggedGizmoAxis == 3 ? new Vector4(1f, 1f, 0.2f, 1f) : new Vector4(0.3f, 0.6f, 1f, 1f);
            var colCenter = _hoveredGizmoAxis == 4 || _draggedGizmoAxis == 4 ? new Vector4(1f, 1f, 0.2f, 1f) : new Vector4(0.9f, 0.9f, 0.9f, 1f);

            if (vm.GizmoMode == GizmoMode.Scale)
            {
                // Scale handles (3 axes ending with cubes + center cube for uniform scale)
                var endX = p + new Vector3(len, 0, 0);
                AddLine(buf, p, endX, colX);
                AddWireBox(buf, endX, new Vector3(arrow * 0.8f), colX);

                var endY = p + new Vector3(0, len, 0);
                AddLine(buf, p, endY, colY);
                AddWireBox(buf, endY, new Vector3(arrow * 0.8f), colY);

                var endZ = p + new Vector3(0, 0, len);
                AddLine(buf, p, endZ, colZ);
                AddWireBox(buf, endZ, new Vector3(arrow * 0.8f), colZ);

                // Center handle (Uniform Scale)
                AddWireBox(buf, p, new Vector3(arrow * 0.8f), colCenter);
            }
            else if (vm.GizmoMode == GizmoMode.Rotate)
            {
                // Rotation rings (24 segments each)
                const int segs = 24;
                for (int i = 0; i < segs; i++)
                {
                    float a0 = (i / (float)segs) * MathF.PI * 2.0f;
                    float a1 = ((i + 1) / (float)segs) * MathF.PI * 2.0f;

                    // Ring X (around X axis in YZ plane)
                    AddLine(buf, p + new Vector3(0, MathF.Cos(a0) * len, MathF.Sin(a0) * len),
                                 p + new Vector3(0, MathF.Cos(a1) * len, MathF.Sin(a1) * len), colX);

                    // Ring Y (around Y axis in XZ plane)
                    AddLine(buf, p + new Vector3(MathF.Cos(a0) * len, 0, MathF.Sin(a0) * len),
                                 p + new Vector3(MathF.Cos(a1) * len, 0, MathF.Sin(a1) * len), colY);

                    // Ring Z (around Z axis in XY plane)
                    AddLine(buf, p + new Vector3(MathF.Cos(a0) * len, MathF.Sin(a0) * len, 0),
                                 p + new Vector3(MathF.Cos(a1) * len, MathF.Sin(a1) * len, 0), colZ);
                }
                AddWireBox(buf, p, new Vector3(arrow * 0.5f), colCenter);
            }
            else // Translate (Default)
            {
                // X Axis (+X forward)
                var endX = p + new Vector3(len, 0, 0);
                AddLine(buf, p, endX, colX);
                AddWireBox(buf, endX, new Vector3(arrow, arrow * 0.5f, arrow * 0.5f), colX);

                // Y Axis (+Y left)
                var endY = p + new Vector3(0, len, 0);
                AddLine(buf, p, endY, colY);
                AddWireBox(buf, endY, new Vector3(arrow * 0.5f, arrow, arrow * 0.5f), colY);

                // Z Axis (+Z up)
                var endZ = p + new Vector3(0, 0, len);
                AddLine(buf, p, endZ, colZ);
                AddWireBox(buf, endZ, new Vector3(arrow * 0.5f, arrow * 0.5f, arrow), colZ);

                // Center handle
                AddWireBox(buf, p, new Vector3(arrow * 0.6f), colCenter);
            }
        }

        _gizmoVertexCount = buf.Count / FloatsPerVertex;
        UploadLines(_gizmoVbo, buf);
    }

    private int HitTestGizmo(Point screenPoint)
    {
        var vm = Vm;
        if (vm == null || !vm.IsGizmoActive || !vm.ActiveGizmoPosition.HasValue) return 0;

        var p = vm.ActiveGizmoPosition.Value;
        float len = Math.Clamp(_camera.Distance * 0.15f, 40.0f, 600.0f);
        float arrow = len * 0.2f;

        var ray = MeshSculptEngine.ScreenPointToRay(
            new Vector2((float)screenPoint.X, (float)screenPoint.Y),
            (float)Bounds.Width, (float)Bounds.Height, _camera);

        // Center handle hit test
        var toCenter = p - ray.Origin;
        float proj = Vector3.Dot(toCenter, ray.Direction);
        if (proj > 0)
        {
            float distCenter = (ray.Origin + ray.Direction * proj - p).Length();
            if (distCenter < arrow * 0.9f) return 4; // Center handle (uniform scale / XY translate)
        }

        float DistToSegment(Vector3 a, Vector3 b)
        {
            var ab = b - a;
            var ao = ray.Origin - a;
            var cross = Vector3.Cross(ray.Direction, ab);
            float cLen = cross.Length();
            if (cLen < 1e-4f) return float.MaxValue;
            return MathF.Abs(Vector3.Dot(ao, cross)) / cLen;
        }

        float threshold = len * 0.30f;
        float dX = DistToSegment(p, p + new Vector3(len, 0, 0));
        float dY = DistToSegment(p, p + new Vector3(0, len, 0));
        float dZ = DistToSegment(p, p + new Vector3(0, 0, len));

        if (dX < threshold && dX <= dY && dX <= dZ) return 1;
        if (dY < threshold && dY <= dX && dY <= dZ) return 2;
        if (dZ < threshold && dZ <= dX && dZ <= dY) return 3;
        return 0;
    }

    #endregion

    #region Rendering

    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_gl == null || !_glReady)
        {
            RequestNextFrameRendering();
            return;
        }

        try
        {
            double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
            int pxW = Math.Max(1, (int)(Bounds.Width * scaling));
            int pxH = Math.Max(1, (int)(Bounds.Height * scaling));

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);

            // Ensure depth renderbuffer is attached to Avalonia's framebuffer
            if (_depthRbo == 0)
            {
                _depthRbo = _gl.GenRenderbuffer();
            }
            if (pxW != _depthW || pxH != _depthH)
            {
                _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthRbo);
                _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, _isGles ? InternalFormat.DepthComponent16 : InternalFormat.DepthComponent24, (uint)pxW, (uint)pxH);
                _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _depthRbo);
                _depthW = pxW;
                _depthH = pxH;
            }

            _gl.Viewport(0, 0, (uint)pxW, (uint)pxH);
            _gl.ClearColor(0.08f, 0.09f, 0.12f, 1.0f);
            _gl.ClearDepth(1.0f);
            _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Lequal);
            _gl.Disable(EnableCap.CullFace);
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            var vm = Vm;
            SyncWithViewModel(vm);

            _camera.UpdateMatrices((float)Bounds.Width, (float)Bounds.Height);
            var mvp = _camera.ViewMatrix * _camera.ProjectionMatrix;

            _gl.UseProgram(_program);
            _gl.UniformMatrix4(_locMvp, 1, false, (float*)&mvp);
            var light = Vector3.Normalize(new Vector3(0.45f, -0.55f, 0.75f));
            _gl.Uniform3(_locLightDir, light.X, light.Y, light.Z);

            // 1. Grid
            _gl.Uniform1(_locMode, ModeUnlit);
            _gl.Uniform4(_locBaseColor, 1f, 1f, 1f, 1f);
            _gl.BindVertexArray(_gridVao);
            _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_gridVertexCount);

            // 2. Scene Meshes (pushed back slightly so edge overlays don't z-fight)
            if (_meshTriIndexCount > 0 && _subBatches.Count > 0)
            {
                _gl.BindVertexArray(_meshVao);
                _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _meshTriEbo);

                _gl.Enable(EnableCap.PolygonOffsetFill);
                _gl.PolygonOffset(1.0f, 1.0f);

                // Pass 1: Opaque geometry (Terrain, Bunkers, CSG Brushes, Tag Models)
                foreach (var batch in _subBatches)
                {
                    if (batch.IsTransparent || batch.IsSky) continue;

                    _gl.Uniform1(_locMode, batch.Mode);
                    _gl.Uniform4(_locBaseColor, batch.BaseColor.X, batch.BaseColor.Y, batch.BaseColor.Z, batch.BaseColor.W);

                    void* offset = (void*)(batch.StartIndex * sizeof(uint));
                    _gl.DrawElements(PrimitiveType.Triangles, batch.IndexCount, DrawElementsType.UnsignedInt, offset);
                }

                // Pass 2: Transparent geometry (Water, Portals)
                foreach (var batch in _subBatches)
                {
                    if (!batch.IsTransparent || batch.IsSky) continue;

                    _gl.Uniform1(_locMode, batch.Mode);
                    _gl.Uniform4(_locBaseColor, batch.BaseColor.X, batch.BaseColor.Y, batch.BaseColor.Z, batch.BaseColor.W);

                    void* offset = (void*)(batch.StartIndex * sizeof(uint));
                    _gl.DrawElements(PrimitiveType.Triangles, batch.IndexCount, DrawElementsType.UnsignedInt, offset);
                }

                // Pass 3: Watertight Sky Dome (+sky)
                // Front faces are culled so looking from outside the box lets you see straight through into the terrain!
                foreach (var batch in _subBatches)
                {
                    if (!batch.IsSky) continue;

                    _gl.Enable(EnableCap.CullFace);
                    _gl.CullFace(TriangleFace.Front); // Cull exterior facing walls so camera outside sees inside

                    _gl.Uniform1(_locMode, ModeLitSolid);
                    _gl.Uniform4(_locBaseColor, 0.28f, 0.45f, 0.72f, 0.28f);

                    void* offset = (void*)(batch.StartIndex * sizeof(uint));
                    _gl.DrawElements(PrimitiveType.Triangles, batch.IndexCount, DrawElementsType.UnsignedInt, offset);

                    _gl.Disable(EnableCap.CullFace);
                }

                _gl.Disable(EnableCap.PolygonOffsetFill);

                // Wireframe overlay across all scene geometry
                if (vm != null && vm.ShowWireframe)
                {
                    _gl.Uniform1(_locMode, ModeLitSolid);
                    _gl.Uniform4(_locBaseColor, 0.08f, 0.08f, 0.08f, 0.65f);
                    _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _meshLineEbo);
                    _gl.DrawElements(PrimitiveType.Lines, (uint)_meshLineIndexCount, DrawElementsType.UnsignedInt, (void*)0);
                }
            }

            // 3. Overlays: depth-tested layer, then x-ray layer on top
            _gl.Uniform1(_locMode, ModeUnlit);
            _gl.BindVertexArray(_overlayVao);
            if (_overlayDepthCount > 0)
                _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_overlayDepthCount);
            if (_overlayXrayCount > 0)
            {
                _gl.Disable(EnableCap.DepthTest);
                _gl.DrawArrays(PrimitiveType.Lines, _overlayDepthCount, (uint)_overlayXrayCount);
                _gl.Enable(EnableCap.DepthTest);
            }

            // 4. Brush cursor (x-ray)
            if (_cursorVertexCount > 0)
            {
                _gl.Disable(EnableCap.DepthTest);
                _gl.BindVertexArray(_cursorVao);
                _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_cursorVertexCount);
                _gl.Enable(EnableCap.DepthTest);
            }

            // 5. 3D Transform Gizmo (x-ray)
            if (_gizmoVertexCount > 0 && vm?.IsGizmoActive == true)
            {
                _gl.Disable(EnableCap.DepthTest);
                _gl.BindVertexArray(_gizmoVao);
                _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_gizmoVertexCount);
                _gl.Enable(EnableCap.DepthTest);
            }

            _gl.BindVertexArray(0);
            _frameCounter++;

            if (_frameCounter == 1)
            {
                Log($"First frame rendered: {pxW}x{pxH}px (scaling {scaling:F2}), mesh tris={_meshTriIndexCount / 3}");
            }

            if (SelfTest && _frameCounter == 30)
            {
                RunSelfTestCapture(pxW, pxH);
            }
        }
        catch (Exception ex)
        {
            Log("RENDER EXCEPTION: " + ex);
            _glReady = false;
            SetInfo("Render failed - see " + LogPath);
        }

        // Continuous redraw keeps the viewport live while sculpting / orbiting / editing.
        RequestNextFrameRendering();
    }

    private void SyncWithViewModel(MainViewModel? vm)
    {
        if (vm == null) return;

        if (vm.MeshVersion != _uploadedMeshVersion)
        {
            UploadSceneMeshes(vm);
            _uploadedMeshVersion = vm.MeshVersion;
            _uploadedMesh = vm.ActiveMesh;
            _builtOverlayVersion = -1; // Marker scale depends on mesh bounds
        }

        if (vm.FrameRequestId != _handledFrameRequest && vm.ActiveMesh != null)
        {
            _camera.FrameBounds(EnumeratePositions(vm));
            _handledFrameRequest = vm.FrameRequestId;
        }

        if (vm.OverlayVersion != _builtOverlayVersion)
        {
            BuildOverlays(vm);
            _builtOverlayVersion = vm.OverlayVersion;
        }

        if (vm.IsTestPlayActive != _camera.IsFirstPerson)
        {
            _camera.IsFirstPerson = vm.IsTestPlayActive;
            if (vm.IsTestPlayActive)
            {
                var spawn = vm.Spawns.FirstOrDefault();
                _camera.PlayerPosition = spawn != null ? spawn.Position + new Vector3(0, 0, 70.3f) : new Vector3(0, 0, 70.3f);
                _camera.Yaw = spawn?.FacingAngle ?? 45.0f;
                _camera.Pitch = 0;
            }
        }

        BuildCursor(vm);
        BuildGizmo(vm);
    }

    private static IEnumerable<Vector3> EnumeratePositions(MainViewModel vm)
    {
        var meshes = vm.SceneGraph.GetLevelMeshes();
        if (meshes.Count == 0 && vm.ActiveMesh != null) meshes.Add(vm.ActiveMesh);
        foreach (var m in meshes)
        {
            if (m.Name.StartsWith("+sky", StringComparison.OrdinalIgnoreCase)) continue; // Don't frame around giant skybox
            foreach (var v in m.Vertices) yield return v.Position;
        }
    }

    private static (int mode, Vector4 color) ClassifyMaterial(MeshObject mesh, int materialIndex)
    {
        string n = mesh.Name.ToLowerInvariant();
        string mat = (materialIndex >= 0 && materialIndex < mesh.MaterialSlots.Count)
            ? mesh.MaterialSlots[materialIndex].ToLowerInvariant()
            : mesh.MaterialName.ToLowerInvariant();

        if (n.Contains("water") || mat.Contains("water") || (mat.Contains("river") && !mat.Contains("riverworld") && !mat.Contains("tree") && !mat.Contains("bark") && !mat.Contains("leaf") && !mat.Contains("rock") && !mat.Contains("ground")) || mat.Contains("swamp_water") || mat.Contains("ocean") || mat.Contains("lake") || mat.Contains("waterfall"))
            return (ModeLitSolid, new Vector4(0.18f, 0.48f, 0.75f, 0.65f));

        if (n.StartsWith("+sky") || mat.StartsWith("+sky"))
            return (ModeLitSolid, new Vector4(0.28f, 0.45f, 0.72f, 0.35f));

        if (n.StartsWith("+portal") || mat.StartsWith("+portal"))
            return (ModeLitSolid, new Vector4(0.20f, 0.85f, 1.0f, 0.45f));

        if (n.Contains("terrain") || mat.Contains("terrain"))
            return (ModeTerrainBlend, new Vector4(1f, 1f, 1f, 1f));

        if (mat.Contains("needle") || mat.Contains("leaf") || mat.Contains("leaves") || mat.Contains("foliage") || mat.Contains("bush") || mat.Contains("fern"))
            return (ModeLitSolid, new Vector4(0.16f, 0.42f, 0.16f, 0.95f)); // Dense vegetation green

        if (mat.Contains("bark") || mat.Contains("wood") || mat.Contains("trunk"))
            return (ModeLitSolid, new Vector4(0.36f, 0.25f, 0.16f, 1.0f)); // Tree bark brown

        if (mat.Contains("rock") || mat.Contains("cliff") || mat.Contains("basalt") || mat.Contains("boulder") || mat.Contains("stone") || mat.Contains("crag"))
            return (ModeLitSolid, new Vector4(0.38f, 0.35f, 0.32f, 1.0f)); // Basalt / rock grey

        if (mat.Contains("covenant") || mat.Contains("purple") || mat.Contains("hull"))
            return (ModeLitSolid, new Vector4(0.42f, 0.28f, 0.65f, 1.0f)); // Covenant violet

        if (mat.Contains("sand") || mat.Contains("dirt") || mat.Contains("mud") || mat.Contains("earth"))
            return (ModeLitSolid, new Vector4(0.74f, 0.66f, 0.48f, 1.0f)); // Warm sand

        if (n.Contains("preview") || n.Contains("tag"))
            return (ModeLitSolid, new Vector4(0.35f, 0.52f, 0.45f, 1f));

        return (ModeLitSolid, new Vector4(0.60f, 0.58f, 0.54f, 1.0f)); // Concrete / UNSC structural metal
    }

    private unsafe void RunSelfTestCapture(int w, int h)
    {
        var pixels = new byte[w * h * 4];
        fixed (byte* p = pixels)
        {
            _gl!.ReadPixels(0, 0, (uint)w, (uint)h, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }

        int nonBackground = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            // Clear color is ~ (20, 23, 31)
            if (Math.Abs(pixels[i] - 20) > 8 || Math.Abs(pixels[i + 1] - 23) > 8 || Math.Abs(pixels[i + 2] - 31) > 8)
                nonBackground++;
        }

        float coverage = 100.0f * nonBackground / (w * h);
        Log($"SELFTEST: {nonBackground}/{w * h} pixels differ from clear color ({coverage:F1}% coverage)");
        Log(coverage > 5.0f ? "SELFTEST RESULT: PASS (scene visible)" : "SELFTEST RESULT: FAIL (viewport mostly empty)");

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    #endregion

    #region Input

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pt = e.GetCurrentPoint(this);
        _lastPointerPos = pt.Position;
        var props = pt.Properties;
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (props.IsLeftButtonPressed && Vm?.IsGizmoActive == true)
        {
            int hitAxis = HitTestGizmo(_lastPointerPos);
            if (hitAxis > 0)
            {
                string action = Vm?.GizmoMode switch
                {
                    GizmoMode.Scale => "Scale Entity",
                    GizmoMode.Rotate => "Rotate Entity",
                    _ => "Move Entity"
                };
                Vm?.PushHistoryState(action);
                _isDraggingGizmo = true;
                _draggedGizmoAxis = hitAxis;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
        }

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        // Sub-Element Face Selection in Face Mode (Blockbench / Blender style)
        if (props.IsLeftButtonPressed && Vm is { } vmFace && vmFace.SelectionMode == SubElementSelectionMode.Face)
        {
            var ray = MeshSculptEngine.ScreenPointToRay(
                new Vector2((float)_lastPointerPos.X, (float)_lastPointerPos.Y),
                (float)Bounds.Width, (float)Bounds.Height, _camera);
            if (vmFace.ActiveMesh != null && MeshSculptEngine.RayIntersectsMesh(ray, vmFace.ActiveMesh, out var hitPt, out _, out int hitTri))
            {
                if (shift || ctrl)
                {
                    if (vmFace.SelectedFaceIndices.Contains(hitTri))
                        vmFace.SelectedFaceIndices.Remove(hitTri);
                    else
                        vmFace.SelectedFaceIndices.Add(hitTri);
                }
                else
                {
                    vmFace.SelectedFaceIndices.Clear();
                    vmFace.SelectedFaceIndices.Add(hitTri);
                }
                vmFace.UpdateFaceSelectionGizmo();
                RequestNextFrameRendering();
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
            else if (!shift && !ctrl)
            {
                vmFace.SelectedFaceIndices.Clear();
                vmFace.UpdateFaceSelectionGizmo();
                RequestNextFrameRendering();
            }
        }

        if (props.IsMiddleButtonPressed || (props.IsRightButtonPressed && shift))
        {
            _isPanning = true;
        }
        else if (props.IsRightButtonPressed || (props.IsLeftButtonPressed && alt))
        {
            _isOrbiting = true;
        }
        else if (props.IsLeftButtonPressed && Vm is { } vm && vm.ToolMode != ViewportTool.Navigate)
        {
            vm.PushHistoryState(vm.ToolMode == ViewportTool.Sculpt ? "Sculpt Terrain" : "Paint Texture");
            _isStroking = true;
            ApplyBrushAt(_lastPointerPos);
        }
        else if (props.IsLeftButtonPressed)
        {
            _isOrbiting = true; // Navigate tool: left-drag orbits
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetCurrentPoint(this).Position;
        var delta = pos - _lastPointerPos;
        _lastPointerPos = pos;

        if (_isDraggingGizmo && Vm is { } vmGizmo && vmGizmo.IsGizmoActive && vmGizmo.ActiveGizmoPosition.HasValue)
        {
            if (vmGizmo.GizmoMode == GizmoMode.Scale)
            {
                float scaleFactor = 1.0f + (float)(delta.X - delta.Y) * 0.015f;
                scaleFactor = Math.Clamp(scaleFactor, 0.70f, 1.30f);
                vmGizmo.ScaleSelectedEntity(scaleFactor, _draggedGizmoAxis);
                BuildGizmo(vmGizmo);
                return;
            }
            else if (vmGizmo.GizmoMode == GizmoMode.Rotate)
            {
                float rotAngle = (float)(delta.X - delta.Y) * 0.025f;
                vmGizmo.RotateSelectedEntity(rotAngle, _draggedGizmoAxis);
                BuildGizmo(vmGizmo);
                return;
            }
            else
            {
                float speed = _camera.Distance * 0.002f;
                Vector3 worldDelta = _draggedGizmoAxis switch
                {
                    1 => new Vector3((float)delta.X * speed, 0, 0),
                    2 => new Vector3(0, -(float)delta.Y * speed, 0),
                    3 => new Vector3(0, 0, -(float)delta.Y * speed),
                    4 => new Vector3((float)delta.X * speed, -(float)delta.Y * speed, 0),
                    _ => Vector3.Zero
                };
                vmGizmo.TranslateSelectedEntity(worldDelta);
                BuildGizmo(vmGizmo);
                return;
            }
        }
        else if (Vm?.IsGizmoActive == true)
        {
            int hoverAxis = HitTestGizmo(pos);
            if (hoverAxis != _hoveredGizmoAxis)
            {
                _hoveredGizmoAxis = hoverAxis;
                if (Vm is { } vmG) BuildGizmo(vmG);
            }
        }
        else
        {
            _hoveredGizmoAxis = 0;
        }

        if (_isOrbiting)
        {
            _camera.Orbit(-(float)delta.X * 0.35f, (float)delta.Y * 0.35f);
        }
        else if (Vm?.IsTestPlayActive == true)
        {
            // First-person free look
            _camera.Orbit(-(float)delta.X * 0.35f, (float)delta.Y * 0.35f);
        }
        else if (_isPanning)
        {
            _camera.Pan((float)delta.X, (float)delta.Y);
        }
        else if (_isStroking)
        {
            ApplyBrushAt(pos);
        }
        else if (Vm is { } vm && vm.ToolMode != ViewportTool.Navigate)
        {
            _brushHit = RaycastMesh(pos, out var hit) ? hit : null;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isDraggingGizmo)
        {
            _isDraggingGizmo = false;
            _draggedGizmoAxis = 0;
            if (Vm is { } vmG) BuildGizmo(vmG);
        }
        if (_isStroking)
        {
            Vm?.NotifyStrokeFinished();
        }
        _isOrbiting = _isPanning = _isStroking = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!_isStroking) _brushHit = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Vm?.IsTestPlayActive != true)
        {
            _camera.Zoom((float)e.Delta.Y);
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Vm is not { } vm) return;

        if (vm.IsTestPlayActive)
        {
            float speed = 35.0f;
            switch (e.Key)
            {
                case Key.W: _camera.Walk(1.0f, 0, speed); GroundClampPlayer(vm); e.Handled = true; break;
                case Key.S: _camera.Walk(-1.0f, 0, speed); GroundClampPlayer(vm); e.Handled = true; break;
                case Key.A: _camera.Walk(0, 1.0f, speed); GroundClampPlayer(vm); e.Handled = true; break;
                case Key.D: _camera.Walk(0, -1.0f, speed); GroundClampPlayer(vm); e.Handled = true; break;
                case Key.Space: _camera.PlayerPosition += new Vector3(0, 0, 50.0f); e.Handled = true; break;
                case Key.Escape: vm.IsTestPlayActive = false; e.Handled = true; break;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.D1:
                vm.SelectionMode = SubElementSelectionMode.Object;
                vm.StatusMessage = "Selection Mode: Object (1)";
                e.Handled = true;
                break;
            case Key.D2:
                vm.SelectionMode = SubElementSelectionMode.Vertex;
                vm.StatusMessage = "Selection Mode: Vertex (2)";
                e.Handled = true;
                break;
            case Key.D3:
                vm.SelectionMode = SubElementSelectionMode.Edge;
                vm.StatusMessage = "Selection Mode: Edge (3)";
                e.Handled = true;
                break;
            case Key.D4:
                vm.SelectionMode = SubElementSelectionMode.Face;
                vm.StatusMessage = "Selection Mode: Face (4)";
                e.Handled = true;
                break;
            case Key.F: vm.FrameView(); e.Handled = true; break;
            case Key.W: 
                if (vm.IsSculptOrPaintMode) vm.SetEditMode();
                vm.GizmoMode = GizmoMode.Translate; 
                vm.StatusMessage = "Gizmo Mode: Move (Translate)"; 
                e.Handled = true; 
                break;
            case Key.E: 
                if (vm.SelectionMode == SubElementSelectionMode.Face && vm.SelectedFaceIndices.Count > 0)
                {
                    vm.ExtrudeSelectedFaces();
                    e.Handled = true;
                }
                else
                {
                    if (vm.IsSculptOrPaintMode) vm.SetEditMode();
                    vm.GizmoMode = GizmoMode.Rotate; 
                    vm.StatusMessage = "Gizmo Mode: Rotate"; 
                    e.Handled = true; 
                }
                break;
            case Key.I:
                if (vm.SelectionMode == SubElementSelectionMode.Face && vm.SelectedFaceIndices.Count > 0)
                {
                    vm.InsetSelectedFaces();
                    e.Handled = true;
                }
                break;
            case Key.K:
                vm.LoopCutActiveMesh();
                e.Handled = true;
                break;
            case Key.X:
                if (vm.SelectionMode == SubElementSelectionMode.Face && vm.SelectedFaceIndices.Count > 0)
                {
                    vm.DissolveSelectedFaces();
                    e.Handled = true;
                }
                break;
            case Key.Delete:
                if (vm.SelectionMode == SubElementSelectionMode.Face && vm.SelectedFaceIndices.Count > 0)
                {
                    vm.DissolveSelectedFaces();
                    e.Handled = true;
                }
                else
                {
                    vm.DeleteSelectedNode();
                    e.Handled = true;
                }
                break;
            case Key.R: 
                if (vm.IsSculptOrPaintMode) vm.SetEditMode();
                vm.GizmoMode = GizmoMode.Scale; 
                vm.StatusMessage = "Gizmo Mode: Scale"; 
                e.Handled = true; 
                break;
            case Key.Q: vm.SetEditMode(); e.Handled = true; break;
            case Key.S: vm.SetSculptMode(); e.Handled = true; break;
            case Key.P: vm.SetPaintMode(); e.Handled = true; break;
            case Key.Z: vm.ShowWireframe = !vm.ShowWireframe; e.Handled = true; break;
            case Key.OemOpenBrackets: vm.BrushRadius = Math.Max(25, vm.BrushRadius * 0.85); e.Handled = true; break;
            case Key.OemCloseBrackets: vm.BrushRadius = Math.Min(5000, vm.BrushRadius * 1.15); e.Handled = true; break;
        }
    }

    private void GroundClampPlayer(MainViewModel vm)
    {
        var groundRay = new Ray(new Vector3(_camera.PlayerPosition.X, _camera.PlayerPosition.Y, 1_000_000f), -Vector3.UnitZ);
        if (vm.ActiveMesh != null && MeshSculptEngine.RayIntersectsMesh(groundRay, vm.ActiveMesh, out var hit, out _, out _))
        {
            _camera.PlayerPosition = new Vector3(_camera.PlayerPosition.X, _camera.PlayerPosition.Y, hit.Z + 70.3f);
        }
    }

    private bool RaycastMesh(Point screen, out Vector3 hit)
    {
        hit = default;
        var vm = Vm;
        if (vm?.ActiveMesh == null) return false;

        var ray = MeshSculptEngine.ScreenPointToRay(
            new Vector2((float)screen.X, (float)screen.Y),
            (float)Bounds.Width, (float)Bounds.Height, _camera);
        return MeshSculptEngine.RayIntersectsMesh(ray, vm.ActiveMesh, out hit, out _, out _);
    }

    private void ApplyBrushAt(Point screen)
    {
        var vm = Vm;
        if (vm?.ActiveMesh == null || !RaycastMesh(screen, out var hit))
        {
            _brushHit = null;
            return;
        }

        _brushHit = hit;
        float radius = (float)vm.BrushRadius;

        if (vm.ToolMode == ViewportTool.Sculpt)
        {
            MeshSculptEngine.ApplySculpt(vm.ActiveMesh, hit, radius, (float)vm.BrushStrength, vm.BrushMode, vm.TerrainUvTileScale);
        }
        else if (vm.ToolMode == ViewportTool.Paint)
        {
            MeshSculptEngine.ApplyPaint(vm.ActiveMesh, hit, radius, (float)vm.BrushStrength / 100.0f, vm.PaintLayerIndex);

            string targetMat = !string.IsNullOrWhiteSpace(vm.PaintMaterialName)
                ? vm.PaintMaterialName
                : (vm.SelectedShader?.MaterialName ?? "");

            if (!string.IsNullOrEmpty(targetMat))
            {
                int matIdx = vm.ActiveMesh.MaterialSlots.IndexOf(targetMat);
                if (matIdx == -1)
                {
                    matIdx = vm.ActiveMesh.MaterialSlots.Count;
                    vm.ActiveMesh.MaterialSlots.Add(targetMat);
                }
                MeshSculptEngine.PaintTrianglesMaterial(vm.ActiveMesh, hit, radius, matIdx, vm.TerrainUvTileScale, vm.TerrainNoTiling);
            }
        }

        vm.NotifyMeshEdited();
    }

    #endregion

    private void SetInfo(string text)
    {
        if (Vm is { } vm) vm.ViewportInfo = text;
    }

    private static void Log(string line)
    {
        try { File.AppendAllText(LogPath, line + "\n"); } catch { /* logging must never crash rendering */ }
    }
}

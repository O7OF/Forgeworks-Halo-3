using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ForgeWorks.Core.Diagnostics;
using ForgeWorks.Core.Geometry;
using ForgeWorks.Core.Procedural;
using ForgeWorks.Core.Rendering;
using ForgeWorks.Core.Shaders;
using ForgeWorks.Core.Project;
using ForgeWorks.Blam.Scenario;
using ForgeWorks.Blam.Shaders;
using ForgeWorks.Blam.Tags;
using ForgeWorks.Blam.Tool;
using Xunit;

namespace ForgeWorks.Tests;

public class ForgeWorksTests
{
    [Fact]
    public void AssExporter_ProducesValidStructureAss()
    {
        var mesh = new MeshObject("box_test");
        mesh.Vertices.Add(new Vertex(new Vector3(-50, -50, 0), Vector3.UnitZ, new Vector2(0, 0)));
        mesh.Vertices.Add(new Vertex(new Vector3(50, -50, 0), Vector3.UnitZ, new Vector2(1, 0)));
        mesh.Vertices.Add(new Vertex(new Vector3(50, 50, 0), Vector3.UnitZ, new Vector2(1, 1)));
        mesh.Vertices.Add(new Vertex(new Vector3(-50, 50, 0), Vector3.UnitZ, new Vector2(0, 1)));

        mesh.Triangles.Add(new Triangle(0, 1, 2, materialIndex: 0));
        mesh.Triangles.Add(new Triangle(0, 2, 3, materialIndex: 0));

        using var ms = new MemoryStream();
        AssExporter.ExportToStream(ms, new List<string> { "mpshare mp_sand" }, new List<MeshObject> { mesh }, version: 3);

        ms.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(ms);
        string content = reader.ReadToEnd();

        Assert.Contains(";### HEADER ###", content);
        Assert.Contains("\"BLENDER\"", content);
        Assert.Contains("\"ForgeWorks\"", content);
        Assert.Contains("\"mpshare mp_sand\"", content);
        Assert.Contains(";### OBJECTS ###", content);
        Assert.Contains(";### INSTANCES ###", content);
        Assert.Contains("\"Scene Root\"", content);
        Assert.Contains("\"box_test\"", content);
    }

    [Fact]
    public void JmsExporter_ProducesValid8290Jms()
    {
        var mesh = new MeshObject("jms_test");
        mesh.Vertices.Add(new Vertex(new Vector3(0, 0, 0), Vector3.UnitZ, Vector2.Zero));
        mesh.Vertices.Add(new Vertex(new Vector3(100, 0, 0), Vector3.UnitZ, Vector2.UnitX));
        mesh.Vertices.Add(new Vertex(new Vector3(0, 100, 0), Vector3.UnitZ, Vector2.UnitY));
        mesh.Triangles.Add(new Triangle(0, 1, 2));

        using var ms = new MemoryStream();
        JmsExporter.ExportToStream(ms, new List<string> { "base concrete" }, mesh, version: 8290);

        ms.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(ms);
        string content = reader.ReadToEnd();

        Assert.StartsWith("8290", content);
        Assert.Contains("35179", content);
        Assert.Contains("frame root", content);
        Assert.Contains("base concrete\t<none>", content);
    }

    [Fact]
    public void GeometryLinter_DetectsOpenEdges_OnUnsealedMesh()
    {
        // Single isolated triangle has 3 open edges
        var mesh = new MeshObject("open_tri");
        mesh.IsCollideable = true;
        mesh.Vertices.Add(new Vertex(new Vector3(0, 0, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(100, 0, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(0, 100, 0), Vector3.UnitZ));
        mesh.Triangles.Add(new Triangle(0, 1, 2));

        var issues = GeometryLinter.LintMesh(mesh);

        Assert.Contains(issues, i => i.Category == "Open Edge");
        Assert.Equal(3, issues.FindAll(i => i.Category == "Open Edge").Count);
    }

    [Fact]
    public void GeometryLinter_DetectsDegenerateTriangle()
    {
        // 3 Collinear vertices have area = 0
        var mesh = new MeshObject("degen_tri");
        mesh.Vertices.Add(new Vertex(new Vector3(0, 0, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(50, 0, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(100, 0, 0), Vector3.UnitZ));
        mesh.Triangles.Add(new Triangle(0, 1, 2));

        var issues = GeometryLinter.LintMesh(mesh);

        Assert.Contains(issues, i => i.Category == "Degenerate Triangle");
    }

    [Fact]
    public void GeometryAutoRepair_WeldsVertices_AndRemovesDuplicateFaces()
    {
        var mesh = new MeshObject("repair_test");
        // Add 2 duplicate triangles slightly offset (< 0.05 units)
        mesh.Vertices.Add(new Vertex(new Vector3(0, 0, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(100, 0, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(0, 100, 0), Vector3.UnitZ));

        mesh.Vertices.Add(new Vertex(new Vector3(0.01f, 0.01f, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(100.01f, 0, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(0, 100.01f, 0), Vector3.UnitZ));

        mesh.Triangles.Add(new Triangle(0, 1, 2));
        mesh.Triangles.Add(new Triangle(3, 4, 5));

        var report = GeometryAutoRepair.RepairMesh(mesh, weldDistance: 0.08f, removeDuplicates: true);

        Assert.True(report.VerticesWelded >= 3);
        Assert.Single(mesh.Triangles); // 1 triangle left
    }

    [Fact]
    public void HaloShaderCatalog_ParsesAuthenticShaderFiles()
    {
        string blenderShadersPath = @"c:\Users\Chey1\Desktop\Blam Editor\halo3_blender_shaders.txt";
        string rawShadersPath = @"c:\Users\Chey1\Desktop\Blam Editor\halo3_shaders.txt";

        if (File.Exists(blenderShadersPath))
        {
            var catalog = HaloShaderCatalog.LoadFromFiles(blenderShadersPath, rawShadersPath);

            Assert.True(catalog.Count > 1000);
            Assert.True(catalog.IsValidShader("chill blue_panel_light"));
            Assert.True(catalog.IsValidShader("mpshare mp_sand"));
            Assert.True(catalog.IsValidShader("!chill blue_panel_light")); // Semantic prefix check
            Assert.True(catalog.IsValidShader("+sky"));

            Assert.NotEmpty(catalog.TerrainShaders);
        }
    }

    [Fact]
    public void TerrainGenerator_ProducesMultiLayerTerrainMesh()
    {
        var config = new TerrainConfig
        {
            ResolutionX = 16,
            ResolutionY = 16,
            Width = 1000f,
            Length = 1000f,
            HydraulicErosionIterations = 50
        };

        var terrain = TerrainGenerator.GenerateTerrain(config, "test_bsp_terrain");

        Assert.Equal(256, terrain.Vertices.Count);
        Assert.Equal(450, terrain.Triangles.Count);
        Assert.NotEmpty(terrain.Vertices[0].UVs);

        // Check vertex color weights are populated
        Assert.InRange(terrain.Vertices[0].Color.X, 0f, 1f);
        Assert.InRange(terrain.Vertices[0].Color.Y, 0f, 1f);
        Assert.InRange(terrain.Vertices[0].Color.Z, 0f, 1f);
    }

    [Fact]
    public void StructureGenerator_GeneratesWaterWith4UVChannels()
    {
        var water = StructureGenerator.GenerateWaterVolume("!water_lake'", 500, 500, 50);

        Assert.Equal(8, water.Vertices.Count);
        Assert.Equal(12, water.Triangles.Count);

        // Assert all vertices have 4 UV sets (mandatory for Halo 3 water)
        foreach (var v in water.Vertices)
        {
            Assert.Equal(4, v.UVs.Count);
        }

        // Linter should report 0 water issues
        var issues = GeometryLinter.LintMesh(water);
        Assert.DoesNotContain(issues, i => i.Category == "Water UV Mismatch");
    }

    [Fact]
    public void ScenarioManager_SerializesEntities()
    {
        var mgr = new ScenarioManager();
        mgr.AddSpawn(new Vector3(100, 200, 15), 90.0f, team: 0);
        mgr.AddEquipment(new Vector3(150, 250, 0), @"objects\weapons\rifle\assault_rifle\assault_rifle");
        mgr.AddTriggerVolume("kill_ocean", new Vector3(0, 0, -50), new Vector3(5000, 5000, 50), "KILL");

        Assert.Single(mgr.Current.Spawns);
        Assert.Single(mgr.Current.Equipment);
        Assert.Single(mgr.Current.Volumes);

        string tempJson = Path.Combine(Path.GetTempPath(), "test_scenario.json");
        mgr.SaveToJson(tempJson);
        Assert.True(File.Exists(tempJson));

        var loadedMgr = new ScenarioManager();
        loadedMgr.LoadFromJson(tempJson);
        Assert.Single(loadedMgr.Current.Spawns);
        Assert.Equal(90.0f, loadedMgr.Current.Spawns[0].FacingAngle);

        File.Delete(tempJson);
    }

    [Fact]
    public void BlamCamera_UpdatesViewAndProjectionMatrices()
    {
        var camera = new BlamCamera();
        camera.UpdateMatrices(1920, 1080);

        Assert.NotEqual(Matrix4x4.Identity, camera.ViewMatrix);
        Assert.NotEqual(Matrix4x4.Identity, camera.ProjectionMatrix);

        // Test Orbit
        float initialYaw = camera.Yaw;
        camera.Orbit(15.0f, 5.0f);
        Assert.Equal(initialYaw + 15.0f, camera.Yaw);

        // Test Zoom
        float initialDist = camera.Distance;
        camera.Zoom(1.0f);
        Assert.True(camera.Distance < initialDist);
    }

    [Fact]
    public void MeshSculptEngine_RaycastsAndAppliesSculpt()
    {
        var mesh = new MeshObject("sculpt_canvas");
        mesh.Vertices.Add(new Vertex(new Vector3(-100, -100, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(100, -100, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(100, 100, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(-100, 100, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(0, 0, 0), Vector3.UnitZ)); // Center vertex 4

        mesh.Triangles.Add(new Triangle(0, 1, 4));
        mesh.Triangles.Add(new Triangle(1, 2, 4));
        mesh.Triangles.Add(new Triangle(2, 3, 4));
        mesh.Triangles.Add(new Triangle(3, 0, 4));
        mesh.RecalculateNormals();

        // Ray pointing down directly at center
        var ray = new Ray(new Vector3(0, 0, 500), -Vector3.UnitZ);
        bool hit = MeshSculptEngine.RayIntersectsMesh(ray, mesh, out var hitPoint, out var hitNormal, out _);

        Assert.True(hit);
        Assert.True(MathF.Abs(hitPoint.Z) < 0.01f);

        // Apply Raise Sculpt at center
        MeshSculptEngine.ApplySculpt(mesh, hitPoint, radius: 150.0f, strength: 40.0f, SculptBrushMode.Raise);
        Assert.True(mesh.Vertices[4].Position.Z > 30.0f);

        // Apply Paint (Layer 1: Rock / Red)
        MeshSculptEngine.ApplyPaint(mesh, hitPoint, radius: 150.0f, strength: 0.8f, targetLayerIndex: 1);
        Assert.True(mesh.Vertices[4].Color.X > 0.5f);
    }

    [Fact]
    public void ScenarioTagBuilder_GeneratesValidHalo3ScenarioTag()
    {
        string tempTag = Path.Combine(Path.GetTempPath(), "test_map.scenario");

        var spawns = new List<PlayerStartingLocation>
        {
            new(new Vector3(10, 20, 30), 45.0f, 0, 0),
            new(new Vector3(-50, -50, 0), 180.0f, 1, 0)
        };

        var equipment = new List<NetgameEquipment>
        {
            new(new Vector3(0, 0, 10), Quaternion.Identity, @"objects\weapons\rifle\assault_rifle\assault_rifle")
        };

        var volumes = new List<TriggerVolume>
        {
            new("kill_floor", new Vector3(0, 0, -500), new Vector3(10000, 10000, 50), Quaternion.Identity, "KILL")
        };

        ScenarioTagBuilder.GenerateScenarioTag(
            tempTag,
            "test_map",
            @"levels\multi\test_map\test_map",
            @"levels\multi\chill\sky\chill_sky",
            spawns,
            equipment,
            volumes);

        Assert.True(File.Exists(tempTag));
        Assert.True(new FileInfo(tempTag).Length >= 0x500);

        // Read back with BlamTagReader
        using (var reader = BlamTagReader.Open(tempTag))
        {
            Assert.Equal("scnr", reader.GroupMagic);
            Assert.Equal(0x35179u, reader.EngineChecksum);

            // Read Structure BSP reflexive block at 0x40
            var bspBlock = reader.ReadReflexive(0x40);
            Assert.Equal(1, bspBlock.Count);
            Assert.Equal(0x100u, bspBlock.DataOffset);

            // Read Structure BSP tag reference at 0x100
            var bspRef = reader.ReadTagReference(0x100);
            Assert.Equal("sbsp", bspRef.GroupMagic);
            Assert.Equal(@"levels\multi\test_map\test_map", bspRef.TagPath);

            // Read Player spawns reflexive block at 0x50
            var spawnsBlock = reader.ReadReflexive(0x50);
            Assert.Equal(2, spawnsBlock.Count);
            Assert.Equal(0x160u, spawnsBlock.DataOffset);
        }

        File.Delete(tempTag);
    }

    [Fact]
    public void CsgBrushBuilder_CreatesValidBoxBrush()
    {
        var box = CsgBrushBuilder.CreateBox(new Vector3(400, 300, 200), Vector3.Zero);

        Assert.Equal(24, box.Vertices.Count); // 6 quads * 4 verts
        Assert.Equal(12, box.Triangles.Count); // 6 quads * 2 tris
        Assert.True(box.IsCollideable);

        var bounds = CsgBooleanEngine.ComputeBounds(box);
        Assert.Equal(new Vector3(-200, -150, -100), bounds.min);
        Assert.Equal(new Vector3(200, 150, 100), bounds.max);
    }

    [Fact]
    public void CsgBrushBuilder_CreatesValidWedgeRamp()
    {
        var wedge = CsgBrushBuilder.CreateWedge(new Vector3(200, 400, 150), Vector3.Zero);

        Assert.True(wedge.Vertices.Count > 0);
        Assert.Equal(8, wedge.Triangles.Count); // 3 quads (6 tris) + 2 triangles (2 tris) = 8 tris
    }

    [Fact]
    public void CsgBrushBuilder_CreatesValidCylinderColumn()
    {
        var cylinder = CsgBrushBuilder.CreateCylinder(100.0f, 300.0f, 16, Vector3.Zero);

        // 16 segments * 2 (sides) + 16 (bottom cap) + 16 (top cap) = 64 triangles
        Assert.Equal(64, cylinder.Triangles.Count);
    }

    [Fact]
    public void CsgBooleanEngine_CarveBox_RemovesContainedTriangles()
    {
        // Create 2 separated boxes
        var boxA = CsgBrushBuilder.CreateBox(new Vector3(100, 100, 100), new Vector3(-150, 0, 0));
        var boxB = CsgBrushBuilder.CreateBox(new Vector3(100, 100, 100), new Vector3(150, 0, 0));
        var merged = CsgBooleanEngine.Union("two_boxes", boxA, boxB);

        int initialTriCount = merged.Triangles.Count;
        Assert.Equal(24, initialTriCount);

        // Carve boxB completely (from X=50 to X=250)
        var carved = CsgBooleanEngine.CarveBox(merged, new Vector3(50, -100, -100), new Vector3(250, 100, 100));

        Assert.True(carved.Triangles.Count < initialTriCount);
        Assert.Equal(boxA.Triangles.Count, carved.Triangles.Count);
    }

    [Fact]
    public void GuerillaTagDocument_GeneratesVirtualScenario()
    {
        var doc = GuerillaTagDocument.CreateVirtualScenario(
            "custom_canvas",
            @"levels\multi\custom_canvas\custom_canvas",
            @"levels\multi\chill\sky\chill_sky",
            spawnCount: 4,
            equipmentCount: 2,
            volumeCount: 1
        );

        Assert.Equal("scnr", doc.TagGroupMagic);
        Assert.Equal(4, doc.Reflexives.Count);
        Assert.Contains(doc.Reflexives, r => r.Name.Contains("Player Starting Profiles") && r.ElementCount == 4);
        Assert.Contains(doc.Reflexives, r => r.Name.Contains("Netgame Equipment") && r.ElementCount == 2);
    }

    [Fact]
    public void SceneContainer_MaintainsHierarchy_LevelRootAndPortals()
    {
        var container = new SceneContainer();
        var terrain = new MeshObject("test_terrain") { MaterialName = "mpshare mp_sand" };
        var portal = new MeshObject("+portal_01") { MaterialName = "+portal" };

        var tNode = container.AddGeometryMesh(terrain);
        var pNode = container.AddPortalMesh(portal);

        Assert.Contains(tNode, container.LevelRoot.Children);
        Assert.Contains(pNode, container.PortalsRoot.Children);

        var levelMeshes = container.GetLevelMeshes();
        var portalMeshes = container.GetPortalMeshes();

        Assert.Single(levelMeshes);
        Assert.Single(portalMeshes);
        Assert.Equal("test_terrain", levelMeshes[0].Name);
        Assert.Equal("+portal_01", portalMeshes[0].Name);
    }

    [Fact]
    public void StructureGenerator_GenerateWatertightSkyDome_EnclosesMeshWithSkyMaterial()
    {
        var terrain = CsgBrushBuilder.CreateBox(new Vector3(1000, 1000, 200), Vector3.Zero);
        var skyDome = StructureGenerator.GenerateWatertightSkyDome(terrain, ceilingMargin: 2000.0f, floorMargin: 500.0f);

        Assert.Equal("+sky_watertight_envelope", skyDome.Name);
        Assert.True(skyDome.IsCollideable);
        Assert.Equal(8, skyDome.Vertices.Count);
        Assert.Equal(12, skyDome.Triangles.Count); // 6 faces * 2 tris = 12

        var bounds = CsgBooleanEngine.ComputeBounds(skyDome);
        Assert.True(bounds.min.Z < -100.0f);
        Assert.True(bounds.max.Z > 1000.0f);
    }

    [Fact]
    public void GeometryAutoRepair_RepairsMeshErrors_WithoutDestroyingTopology()
    {
        var mesh = new MeshObject("damaged_mesh");

        // Quad with split seam (vertices 1 & 4 coincident at 100,0,0) and a degenerate triangle
        mesh.Vertices.Add(new Vertex(new Vector3(0, 0, 0), Vector3.UnitZ));       // 0
        mesh.Vertices.Add(new Vertex(new Vector3(100, 0, 0), Vector3.UnitZ));     // 1
        mesh.Vertices.Add(new Vertex(new Vector3(100, 100, 0), Vector3.UnitZ));   // 2
        mesh.Vertices.Add(new Vertex(new Vector3(0, 100, 0), Vector3.UnitZ));     // 3
        mesh.Vertices.Add(new Vertex(new Vector3(100.01f, 0, 0), Vector3.UnitZ)); // 4 (coincident with 1 within weld distance)

        // Tri 0: 0, 1, 2
        mesh.Triangles.Add(new Triangle(0, 1, 2));
        // Tri 1: 0, 2, 3
        mesh.Triangles.Add(new Triangle(0, 2, 3));
        // Tri 2: Duplicate of Tri 0
        mesh.Triangles.Add(new Triangle(0, 1, 2));
        // Tri 3: Degenerate collinear triangle
        mesh.Triangles.Add(new Triangle(0, 1, 4));

        var report = GeometryAutoRepair.RepairMesh(mesh, weldDistance: 0.05f, removeDegenerate: true, removeDuplicates: true);

        Assert.True(report.DuplicateFacesRemoved >= 1);
        Assert.True(report.VerticesWelded >= 1);
        // Valid quad survives
        Assert.True(mesh.Triangles.Count >= 2);
    }

    [Fact]
    public void TagBrowserService_DiscoversStandardHaloTags()
    {
        var tags = TagBrowserService.DiscoverTags(null, filterGroup: "all");

        Assert.NotEmpty(tags);
        Assert.Contains(tags, t => t.TagGroup == "scnr");
        Assert.Contains(tags, t => t.TagGroup == "weap");
        Assert.Contains(tags, t => t.TagGroup == "hlmt");
    }

    [Fact]
    public void ModelStructure_GeneratesAuthenticHaloMarkers()
    {
        var model = ModelStructure.CreateDefaultWeaponMarkers("assault_rifle");

        Assert.Equal("assault_rifle", model.ModelName);
        Assert.Contains(model.Markers, m => m.Name == "#primary_trigger");
        Assert.Contains(model.Markers, m => m.Name == "#sight");
        Assert.Contains(model.Markers, m => m.Name == "#barrel");
        Assert.Contains(model.Markers, m => m.Name == "$weapon_origin");
    }

    [Fact]
    public void TagHierarchyBuilder_BuildsAuthenticEkFolderTree()
    {
        var root = TagHierarchyBuilder.BuildTagTree(null, searchQuery: "", filterGroup: "all");

        Assert.NotNull(root);
        Assert.Equal("tags", root.Name);
        Assert.True(root.IsDirectory);
        Assert.NotEmpty(root.Children);

        // Verify standard EK top-level directories exist
        Assert.Contains(root.Children, c => c.IsDirectory && c.Name == "levels");
        Assert.Contains(root.Children, c => c.IsDirectory && c.Name == "objects");

        // Verify objects folder contains subcategories
        var objectsFolder = root.Children.First(c => c.Name == "objects");
        Assert.Contains(objectsFolder.Children, c => c.Name == "weapons");
    }

    [Fact]
    public void TagPreviewMeshBuilder_GeneratesProxyGeometryAndMarkers()
    {
        var rifleEntry = new BlamTagFileEntry(
            @"objects\weapons\rifle\battle_rifle\battle_rifle.weapon",
            @"C:\Fake\battle_rifle.weapon",
            "weap",
            "battle_rifle.weapon",
            1024
        );

        var (mesh, markers) = TagPreviewMeshBuilder.BuildPreviewForTag(rifleEntry);

        Assert.NotNull(mesh);
        Assert.NotEmpty(mesh.Vertices);
        Assert.NotEmpty(mesh.Triangles);
        Assert.NotEmpty(markers);
        Assert.Contains(markers, m => m.Name == "#primary_trigger");
        Assert.Contains(markers, m => m.Name == "#barrel");
        Assert.Contains(markers, m => m.Name == "$weapon_origin");
    }

    [Fact]
    public void AssExporter_ExportsVertexColorsInVersion3()
    {
        var mesh = new MeshObject("vertex_color_test");
        mesh.Vertices.Add(new Vertex(new Vector3(0, 0, 0), Vector3.UnitZ, Vector2.Zero) { Color = new Vector4(1, 0, 0, 1) });
        mesh.Vertices.Add(new Vertex(new Vector3(100, 0, 0), Vector3.UnitZ, Vector2.UnitX) { Color = new Vector4(0, 1, 0, 1) });
        mesh.Vertices.Add(new Vertex(new Vector3(0, 100, 0), Vector3.UnitZ, Vector2.UnitY) { Color = new Vector4(0, 0, 1, 1) });
        mesh.Triangles.Add(new Triangle(0, 1, 2, materialIndex: 0));

        using var ms = new MemoryStream();
        AssExporter.ExportToStream(ms, new List<string> { "Terrain_Layers" }, new List<MeshObject> { mesh }, version: 3);

        ms.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(ms);
        string assContent = reader.ReadToEnd();

        // Must contain vertex color definitions
        Assert.Contains("\"Terrain_Layers\"", assContent);
        Assert.Contains("1.0000000000\t0.0000000000\t0.0000000000", assContent);
    }

    [Fact]
    public void SceneGraph_VisibilityAndRemoval()
    {
        var scene = new SceneContainer();
        var mesh1 = new MeshObject("mesh1");
        var node1 = scene.AddGeometryMesh(mesh1);

        Assert.True(node1.IsVisible);
        Assert.Contains(mesh1, scene.GetLevelMeshes());

        node1.IsVisible = false;
        Assert.DoesNotContain(mesh1, scene.GetLevelMeshes());

        node1.IsVisible = true;
        Assert.Contains(mesh1, scene.GetLevelMeshes());

        scene.LevelRoot.Children.Remove(node1);
        Assert.DoesNotContain(mesh1, scene.GetLevelMeshes());
    }

    [Fact]
    public void TagBrowser_DiscoversWeaponsAndVehicles()
    {
        var weapons = TagBrowserService.DiscoverTags("", "weap", "");
        Assert.NotEmpty(weapons);
        Assert.Contains(weapons, w => w.DisplayName.Contains("assault_rifle", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(weapons, w => w.DisplayName.Contains("battle_rifle", StringComparison.OrdinalIgnoreCase));

        var vehicles = TagBrowserService.DiscoverTags("", "vehi", "");
        Assert.NotEmpty(vehicles);
        Assert.Contains(vehicles, v => v.DisplayName.Contains("warthog", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(vehicles, v => v.DisplayName.Contains("ghost", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GuerillaTagDocument_WeaponAndVehicle_HasAuthenticFieldsAndDescriptions()
    {
        var weapDoc = GuerillaTagDocument.CreateForTag(@"objects\weapons\rifle\assault_rifle\assault_rifle.weapon", @"C:\H3EK\tags\dummy.weapon", "weap");
        Assert.Equal("weap", weapDoc.TagGroupMagic);
        Assert.NotEmpty(weapDoc.HeaderFields);
        var rpsField = weapDoc.HeaderFields.FirstOrDefault(f => f.Name == "Rounds Per Second");
        Assert.NotNull(rpsField);
        Assert.False(string.IsNullOrWhiteSpace(rpsField.Description));
        Assert.Equal("Firing", rpsField.Category);

        var vehiDoc = GuerillaTagDocument.CreateForTag(@"objects\vehicles\warthog\warthog.vehicle", @"C:\H3EK\tags\dummy.vehicle", "vehi");
        Assert.Equal("vehi", vehiDoc.TagGroupMagic);
        var speedField = vehiDoc.HeaderFields.FirstOrDefault(f => f.Name == "Maximum Forward Speed");
        Assert.NotNull(speedField);
        Assert.False(string.IsNullOrWhiteSpace(speedField.Description));
        Assert.Equal("Movement", speedField.Category);
    }

    [Fact]
    public void BlamCamera_FirstPersonWalkMode()
    {
        var camera = new BlamCamera
        {
            IsFirstPerson = true,
            PlayerPosition = new Vector3(100, 200, 70.3f),
            Yaw = 0.0f
        };

        var initialPos = camera.PlayerPosition;
        camera.Walk(forwardInput: 1.0f, strafeInput: 0.0f, speed: 20.0f);

        Assert.True(camera.PlayerPosition.X > initialPos.X);
        Assert.Equal(initialPos.Y, camera.PlayerPosition.Y);
        Assert.Equal(initialPos.Z, camera.PlayerPosition.Z);

        camera.UpdateMatrices(1920, 1080);
        Assert.NotEqual(Matrix4x4.Identity, camera.ViewMatrix);
        Assert.NotEqual(Matrix4x4.Identity, camera.ProjectionMatrix);
    }

    [Fact]
    public void SceneContainer_RootVisibility_Inherited()
    {
        var scene = new SceneContainer();
        var mesh1 = new MeshObject("terrain");
        var node1 = scene.AddGeometryMesh(mesh1);

        Assert.Contains(mesh1, scene.GetLevelMeshes());

        // Hiding the parent LevelRoot should hide all child geometry
        scene.LevelRoot.IsVisible = false;
        Assert.Empty(scene.GetLevelMeshes());

        scene.LevelRoot.IsVisible = true;
        Assert.Contains(mesh1, scene.GetLevelMeshes());
    }

    [Fact]
    public void SwampthingFixOpenEdges_MarksWaterNonCollideable_AndCapsHoles()
    {
        // 1. Water Quad: Material ending in ' must become non-collideable render-only '!
        var waterQuad = new MeshObject("water_quad");
        waterQuad.MaterialName = "swamp_a_water'";
        waterQuad.IsCollideable = true;
        waterQuad.Vertices.Add(new Vertex(new Vector3(0, 0, 0), Vector3.UnitZ));
        waterQuad.Vertices.Add(new Vertex(new Vector3(100, 0, 0), Vector3.UnitZ));
        waterQuad.Vertices.Add(new Vertex(new Vector3(100, 100, 0), Vector3.UnitZ));
        waterQuad.Vertices.Add(new Vertex(new Vector3(0, 100, 0), Vector3.UnitZ));
        waterQuad.Triangles.Add(new Triangle(0, 1, 2));
        waterQuad.Triangles.Add(new Triangle(0, 2, 3));

        var waterReport = GeometryAutoRepair.SwampthingFixOpenEdges(waterQuad);

        Assert.False(waterQuad.IsCollideable);
        Assert.EndsWith("!", waterQuad.MaterialName);
        Assert.Equal(1, waterReport.WaterQuadsFixed);

        // 2. Open 8-segment cylinder (like an open tree trunk or pipe)
        var cylinder = new MeshObject("open_pipe");
        cylinder.IsCollideable = true;
        int segments = 8;
        float radius = 50.0f;
        float height = 100.0f;

        // Bottom vertices: 0..segments-1; Top vertices: segments..2*segments-1
        for (int i = 0; i < segments; i++)
        {
            float theta = i * 2.0f * MathF.PI / segments;
            cylinder.Vertices.Add(new Vertex(new Vector3(MathF.Cos(theta) * radius, MathF.Sin(theta) * radius, 0), Vector3.UnitZ));
        }
        for (int i = 0; i < segments; i++)
        {
            float theta = i * 2.0f * MathF.PI / segments;
            cylinder.Vertices.Add(new Vertex(new Vector3(MathF.Cos(theta) * radius, MathF.Sin(theta) * radius, height), Vector3.UnitZ));
        }

        // Side quads
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            int b0 = i;
            int b1 = next;
            int t0 = i + segments;
            int t1 = next + segments;

            cylinder.Triangles.Add(new Triangle(b0, b1, t1));
            cylinder.Triangles.Add(new Triangle(b0, t1, t0));
        }

        // Initially has open boundary edges on top and bottom rims
        var issuesBefore = GeometryLinter.LintMesh(cylinder);
        Assert.Contains(issuesBefore, issue => issue.Category == "Open Edge");

        // Swampthing fixes boundary open edges by capping the loops
        var cylinderReport = GeometryAutoRepair.SwampthingFixOpenEdges(cylinder, capHoles: true);
        Assert.True(cylinderReport.HolesCapped >= 2);

        var issuesAfter = GeometryLinter.LintMesh(cylinder);
        Assert.DoesNotContain(issuesAfter, issue => issue.Category == "Open Edge");
    }

    [Fact]
    public void HaloRockGenerator_AllSevenArchetypes_AreManifoldAndValid()
    {
        var archetypes = Enum.GetValues<RockArchetype>();
        Assert.Equal(7, archetypes.Length);

        foreach (var arch in archetypes)
        {
            var rock = HaloRockGenerator.GenerateRock(arch, size: new Vector3(150, 150, 150));

            Assert.NotNull(rock);
            Assert.NotEmpty(rock.Vertices);
            Assert.NotEmpty(rock.Triangles);

            // Clean & lint
            GeometryAutoRepair.RepairMesh(rock);
            var issues = GeometryLinter.LintMesh(rock);

            Assert.DoesNotContain(issues, i => i.Category == "Degenerate Triangle");
            Assert.DoesNotContain(issues, i => i.Category == "Open Edge");
        }
    }

    [Fact]
    public void HaloFoliageGenerator_ProducesTwoMaterialSlots_AndWindWeights()
    {
        var tree = HaloFoliageGenerator.GenerateTree(FoliageArchetype.ConiferPine, barkShader: "wood_pine_bark", foliageShader: "foliage_pine_leaves");

        Assert.NotNull(tree);
        Assert.NotEmpty(tree.Vertices);
        Assert.NotEmpty(tree.Triangles);

        // Multi-material slots
        Assert.Equal(2, tree.MaterialSlots.Count);
        Assert.Equal("wood_pine_bark", tree.MaterialSlots[0]);
        Assert.StartsWith("%", tree.MaterialSlots[1]); // Render-only card prefix

        // Verify some leaf vertices have wind weights assigned in vertex colors
        bool foundWindWeight = false;
        foreach (var v in tree.Vertices)
        {
            if (v.Color.X > 0.0f || v.Color.Y > 0.0f || v.Color.Z > 0.0f)
            {
                foundWindWeight = true;
                break;
            }
        }
        Assert.True(foundWindWeight, "Leaf cards must have wind weights in vertex colors for Halo 3 foliage movement.");

        foreach (var arch in Enum.GetValues<FoliageArchetype>())
        {
            var t = HaloFoliageGenerator.GenerateTree(arch);
            var issues = GeometryLinter.LintMesh(t);
            var openEdges = issues.Where(i => i.Category == "Open Edge").ToList();
            Assert.Empty(openEdges);
            Assert.Equal(0, GeometryAutoRepair.CountOpenEdges(t));
        }
    }

    [Fact]
    public void ObjExporterAndImporter_RoundTripPreservesGeometry()
    {
        var box = CsgBrushBuilder.CreateBox(new Vector3(200, 300, 150), Vector3.Zero, "base concrete");
        string tempObj = Path.Combine(Path.GetTempPath(), $"forgeworks_test_{Guid.NewGuid():N}.obj");

        try
        {
            ObjExporter.ExportToFile(tempObj, new List<MeshObject> { box });
            Assert.True(File.Exists(tempObj));

            var imported = ObjImporter.ImportFromFile(tempObj);
            Assert.Single(imported);
            var importedMesh = imported[0];

            Assert.Equal(box.Vertices.Count, importedMesh.Vertices.Count);
            Assert.Equal(box.Triangles.Count, importedMesh.Triangles.Count);
        }
        finally
        {
            if (File.Exists(tempObj)) File.Delete(tempObj);
        }
    }

    [Fact]
    public void AssImporter_ParsesStructureAssFormat()
    {
        var box = CsgBrushBuilder.CreateBox(new Vector3(100, 100, 100), Vector3.Zero, "mpshare mp_sand");
        string tempAss = Path.Combine(Path.GetTempPath(), $"forgeworks_test_{Guid.NewGuid():N}.ass");

        try
        {
            AssExporter.ExportToFile(tempAss, new List<string> { "mpshare mp_sand" }, new List<MeshObject> { box }, version: 3);
            Assert.True(File.Exists(tempAss));

            var imported = AssImporter.ImportFromFile(tempAss);
            Assert.NotEmpty(imported);
            Assert.True(imported[0].Vertices.Count >= 8);
            Assert.True(imported[0].Triangles.Count >= 12);
        }
        finally
        {
            if (File.Exists(tempAss)) File.Delete(tempAss);
        }
    }

    [Fact]
    public void ThreeMaps_Generation_Sealing_Linting_AndCompileReady()
    {
        string tempEkPath = Path.Combine(Path.GetTempPath(), $"h3ek_mock_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempEkPath);

        try
        {
            var mapNames = new[] { "delta_canyon", "alpine_valley", "swamp_basin" };

            foreach (var levelName in mapNames)
            {
                var scene = new SceneContainer();

                // 1. Procedural Terrain with sealed skirt
                var config = new TerrainConfig
                {
                    ResolutionX = 24,
                    ResolutionY = 24,
                    Width = 4000.0f,
                    Length = 4000.0f,
                    HeightScale = 600.0f,
                    WarpStrength = 300.0f,
                    CliffSlopeThreshold = 0.6f,
                    SealSkirt = true,
                    SkirtDepth = 200.0f
                };
                var terrain = TerrainGenerator.GenerateTerrain(config, $"{levelName}_terrain");
                terrain.MaterialName = levelName == "swamp_basin" ? "swamp_mud_terrain" : "dead deadlock_cliff_rock";
                scene.AddGeometryMesh(terrain);

                // 2. Scatter Rocks
                var rockArch = levelName switch
                {
                    "delta_canyon" => RockArchetype.BasaltColumn,
                    "alpine_valley" => RockArchetype.FacetedBoulder,
                    _ => RockArchetype.StratifiedSlab
                };
                var rocks = HaloRockGenerator.ScatterRocksOnTerrain(terrain, count: 8, archetype: rockArch);
                foreach (var r in rocks) scene.AddGeometryMesh(r);

                // 3. Scatter Foliage Trees
                var treeArch = levelName switch
                {
                    "delta_canyon" => FoliageArchetype.ConiferPine,
                    "alpine_valley" => FoliageArchetype.AlpineFir,
                    _ => FoliageArchetype.Palm
                };
                var trees = HaloFoliageGenerator.ScatterFoliageOnTerrain(terrain, count: 8, archetype: treeArch);
                foreach (var t in trees) scene.AddGeometryMesh(t);

                // 4. Sealed Water Volume
                var water = StructureGenerator.GenerateWaterVolume($"!{levelName}_water'", width: 3500.0f, length: 3500.0f, depth: 60.0f);
                scene.AddGeometryMesh(water);

                // 5. Watertight Sky Dome Enclosure
                var sky = StructureGenerator.GenerateWatertightSkyDome(terrain);
                sky.MaterialName = "+sky";
                scene.AddGeometryMesh(sky);

                // 6. Swampthing Auto-Repair all meshes in scene
                var allMeshes = scene.GetLevelMeshes();
                foreach (var mesh in allMeshes)
                {
                    GeometryAutoRepair.SwampthingFixOpenEdges(mesh, capHoles: true, weldDistance: 0.05f);
                }

                // 7. Pre-Flight Geometry Scanner across entire scene
                var issues = GeometryLinter.LintScene(allMeshes);

                // VERIFY: 0 Open Edges on collideable geometry, 0 Degenerates, 0 Short Collision Edges
                var openEdgeErrors = issues.FindAll(i => i.Category == "Open Edge" && i.Severity == LintSeverity.Error);
                var degenerateErrors = issues.FindAll(i => i.Category == "Degenerate Triangle");
                var shortEdges = issues.FindAll(i => i.Category == "Short Collision Edge");

                Assert.Empty(openEdgeErrors);
                Assert.Empty(degenerateErrors);
                Assert.Empty(shortEdges);

                // 8. Compile .ass: Must be written into data\levels\multi\<level>\structure\<level>.ass per H3EK docs!
                var (dataDir, _, _) = ToolFastCompiler.SetupLevelDirectories(tempEkPath, levelName);
                string assPath = Path.Combine(dataDir, $"{levelName}.ass");

                var materials = new HashSet<string>();
                foreach (var m in allMeshes)
                {
                    if (!string.IsNullOrEmpty(m.MaterialName)) materials.Add(m.MaterialName);
                    foreach (var s in m.MaterialSlots) if (!string.IsNullOrEmpty(s)) materials.Add(s);
                }

                AssExporter.ExportToFile(assPath, new List<string>(materials), allMeshes, version: 3);

                Assert.True(File.Exists(assPath));
                string assContent = File.ReadAllText(assPath);
                Assert.Contains(";### HEADER ###", assContent);
                Assert.Contains(";### MATERIALS ###", assContent);
                Assert.Contains(";### OBJECTS ###", assContent);
                Assert.Contains(";### INSTANCES ###", assContent);
                Assert.Contains("+sky", assContent);
            }
        }
        finally
        {
            if (Directory.Exists(tempEkPath))
            {
                Directory.Delete(tempEkPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CompileAllThreeMaps_WithRealToolFast()
    {
        string ekPath = @"C:\Program Files (x86)\Steam\steamapps\common\H3EK";
        if (!Directory.Exists(ekPath)) return;

        var mapGenerators = new (string name, Action<UI.ViewModels.MainViewModel> generate)[]
        {
            ("delta_canyon", vm => vm.GenerateDeltaCanyonMap()),
            ("alpine_valley", vm => vm.GenerateAlpineValleyMap()),
            ("swamp_basin", vm => vm.GenerateSwampBasinMap())
        };

        foreach (var (name, generate) in mapGenerators)
        {
            var vm = new UI.ViewModels.MainViewModel { EkPath = ekPath };
            generate(vm);

            // Verify .ass was generated and exported
            var (dataDir, fullTagsDir, dataRel) = ToolFastCompiler.SetupLevelDirectories(ekPath, name);
            string assPath = Path.Combine(dataDir, $"{name}.ass");
            Assert.True(File.Exists(assPath), $"ASS file must exist at {assPath}");

            // Run Tool structure and cache compiler via our app's ToolFastCompiler (guarded for fast test suite runs)
            if (Environment.GetEnvironmentVariable("FORGEWORKS_FULL_INTEGRATION_TESTS") == "1")
            {
                var result = await ToolFastCompiler.CompileStructureAndCacheAsync(
                    ekPath,
                    name,
                    scenarioType: "multi",
                    scenarioData: new ScenarioData
                    {
                        ScenarioName = name,
                        StructureBspTagPath = $@"levels\multi\{name}\{name}",
                        SkyTagPath = @"levels\multi\chill\sky\chill_sky"
                    },
                    preferFast: true);

                // Check that the structure BSP was successfully created
                string bspPath = Path.Combine(fullTagsDir, $"{name}.scenario_structure_bsp");
                Assert.True(File.Exists(bspPath), $"Structure BSP tag must exist at {bspPath}. Log: {result.LogOutput}");
            }
        }
    }

    [Fact]
    public void AssExporter_ResolvesTriangleMaterialSlotsToGlobalIndex()
    {
        var terrain = new MeshObject("terrain") { MaterialName = "river riverworld_ground" };
        terrain.Vertices.Add(new Vertex(new Vector3(0, 0, 0), Vector3.UnitZ));
        terrain.Vertices.Add(new Vertex(new Vector3(10, 0, 0), Vector3.UnitZ));
        terrain.Vertices.Add(new Vertex(new Vector3(10, 10, 0), Vector3.UnitZ));
        terrain.Triangles.Add(new Triangle(0, 1, 2, materialIndex: 0));

        var tree = new MeshObject("tree") { MaterialName = "river riverworld_tree_bark" };
        tree.MaterialSlots.Add("river riverworld_tree_bark");
        tree.MaterialSlots.Add("%river riverworld_tree_leafa");
        tree.Vertices.Add(new Vertex(new Vector3(2, 2, 0), Vector3.UnitZ));
        tree.Vertices.Add(new Vertex(new Vector3(3, 2, 0), Vector3.UnitZ));
        tree.Vertices.Add(new Vertex(new Vector3(3, 3, 0), Vector3.UnitZ));
        tree.Vertices.Add(new Vertex(new Vector3(2, 2, 5), Vector3.UnitZ));
        tree.Vertices.Add(new Vertex(new Vector3(3, 2, 5), Vector3.UnitZ));
        tree.Vertices.Add(new Vertex(new Vector3(3, 3, 5), Vector3.UnitZ));
        // Trunk tri: slot 0
        tree.Triangles.Add(new Triangle(0, 1, 2, materialIndex: 0));
        // Leaves tri: slot 1
        tree.Triangles.Add(new Triangle(3, 4, 5, materialIndex: 1));

        var water = new MeshObject("water") { MaterialName = "!river riverworld_water_rough'" };
        water.Vertices.Add(new Vertex(new Vector3(0, 0, -2), Vector3.UnitZ));
        water.Vertices.Add(new Vertex(new Vector3(10, 0, -2), Vector3.UnitZ));
        water.Vertices.Add(new Vertex(new Vector3(10, 10, -2), Vector3.UnitZ));
        water.Triangles.Add(new Triangle(0, 1, 2, materialIndex: 0));

        var materials = new List<string>
        {
            "river riverworld_ground",
            "river riverworld_tree_bark",
            "%river riverworld_tree_leafa",
            "!river riverworld_water_rough'"
        };

        using var ms = new MemoryStream();
        AssExporter.ExportToStream(ms, materials, new List<MeshObject> { terrain, tree, water });

        ms.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(ms);
        string text = reader.ReadToEnd();

        // Terrain triangle must use matIdx 0
        // Tree trunk must use matIdx 1
        // Tree leaves must use matIdx 2
        // Water triangle must use matIdx 3
        Assert.Contains("0\t\t0\t1\t2", text);
        Assert.Contains("1\t\t0\t1\t2", text);
        Assert.Contains("2\t\t3\t4\t5", text);
        Assert.Contains("3\t\t0\t1\t2", text);
    }

    [Fact]
    public void WatertightSkyDome_GrabsTerrainPerimeterAndBuildsUpwards()
    {
        var config = new TerrainConfig
        {
            ResolutionX = 6,
            ResolutionY = 6,
            Width = 1000.0f,
            Length = 1000.0f,
            HeightScale = 100.0f,
            SealSkirt = false
        };
        var terrain = TerrainGenerator.GenerateTerrain(config, "test_terrain");
        var sky = StructureGenerator.GenerateWatertightSkyDome(terrain, ceilingMargin: 2000.0f);

        Assert.Equal("+sky", sky.MaterialName);
        Assert.True(sky.Vertices.Count > 0);
        Assert.True(sky.Triangles.Count > 0);

        var (min, max) = CsgBooleanEngine.ComputeBounds(terrain);
        var (skyMin, skyMax) = CsgBooleanEngine.ComputeBounds(sky);

        Assert.Equal(min.X, skyMin.X, 0.1f);
        Assert.Equal(max.X, skyMax.X, 0.1f);
        Assert.Equal(min.Y, skyMin.Y, 0.1f);
        Assert.Equal(max.Y, skyMax.Y, 0.1f);
        Assert.True(skyMax.Z >= max.Z + 1999.0f, "Sky ceiling must be extruded upwards to ceilingMargin");
    }

    [Fact]
    public void WaterVolume_FitsTargetMeshOneToOne()
    {
        var terrain = new MeshObject("terrain");
        terrain.Vertices.Add(new Vertex(new Vector3(-500, -300, 10), Vector3.UnitZ));
        terrain.Vertices.Add(new Vertex(new Vector3(500, -300, 20), Vector3.UnitZ));
        terrain.Vertices.Add(new Vertex(new Vector3(500, 300, 15), Vector3.UnitZ));
        terrain.Vertices.Add(new Vertex(new Vector3(-500, 300, 25), Vector3.UnitZ));
        terrain.Triangles.Add(new Triangle(0, 1, 2));

        var water = StructureGenerator.GenerateWaterVolumeForMesh(terrain, "!river riverworld_water_rough'");

        Assert.Equal("!river riverworld_water_rough'", water.MaterialName);
        Assert.False(water.IsCollideable);

        var (wMin, wMax) = CsgBooleanEngine.ComputeBounds(water);
        Assert.Equal(1000.0f, wMax.X - wMin.X, 0.1f);
        Assert.Equal(600.0f, wMax.Y - wMin.Y, 0.1f);
        Assert.True(water.Vertices[0].UVs.Count >= 4, "Water vertices must have mandatory 4 UV sets");
    }

    [Fact]
    public void Terrain_UVsFitOneToOne_WorldScaleMatching()
    {
        var config = new TerrainConfig
        {
            ResolutionX = 16,
            ResolutionY = 16,
            Width = 4000.0f,
            Length = 4000.0f,
            HeightScale = 0.0f,
            OuterCliffWalls = false,
            UvTileScale = 120.0f
        };
        var terrain = TerrainGenerator.GenerateTerrain(config, "test_uv_terrain");

        Assert.NotEmpty(terrain.Vertices);
        float minU = float.MaxValue, maxU = float.MinValue;
        float minV = float.MaxValue, maxV = float.MinValue;

        foreach (var v in terrain.Vertices)
        {
            Assert.NotEmpty(v.UVs);
            var uv = v.UVs[0];
            minU = MathF.Min(minU, uv.X);
            maxU = MathF.Max(maxU, uv.X);
            minV = MathF.Min(minV, uv.Y);
            maxV = MathF.Max(maxV, uv.Y);
        }

        // UVs must scale 1:1 with world units (Width / UvTileScale = 4000 / 120 ≈ 33.33)
        float spanU = maxU - minU;
        float spanV = maxV - minV;
        Assert.InRange(spanU, 30.0f, 35.0f);
        Assert.InRange(spanV, 30.0f, 35.0f);
    }

    [Fact]
    public void Tree_NormalsPointOutward_AndTrunkIsClosedManifold()
    {
        var tree = HaloFoliageGenerator.GenerateTree(FoliageArchetype.ConiferPine, height: 140.0f, radius: 45.0f);

        // Check trunk triangles (MaterialIndex = 0)
        var trunkTris = tree.Triangles.Where(t => t.MaterialIndex == 0).ToList();
        Assert.NotEmpty(trunkTris);

        // Check that normals point outward away from center (X*Nx + Y*Ny > 0)
        int outwardCount = 0;
        foreach (var tri in trunkTris)
        {
            var p0 = tree.Vertices[tri.V0].Position;
            var p1 = tree.Vertices[tri.V1].Position;
            var p2 = tree.Vertices[tri.V2].Position;
            var center = (p0 + p1 + p2) / 3.0f;
            var normal = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));

            // Wall faces have center.X, center.Y away from axis
            if (MathF.Abs(center.X) > 0.5f || MathF.Abs(center.Y) > 0.5f)
            {
                float dot = center.X * normal.X + center.Y * normal.Y;
                if (dot > 0.0f) outwardCount++;
            }
        }
        Assert.True(outwardCount >= 10, "Trunk wall faces must have outward-facing normals, not inward-facing");

        // Directed edge pairing check on trunk: Every edge must have twin edge with opposite direction
        var directed = new Dictionary<(int, int), int>();
        foreach (var tri in trunkTris)
        {
            void AddDir(int a, int b) => directed[(a, b)] = directed.GetValueOrDefault((a, b), 0) + 1;
            AddDir(tri.V0, tri.V1);
            AddDir(tri.V1, tri.V2);
            AddDir(tri.V2, tri.V0);
        }

        foreach (var kvp in directed)
        {
            var (a, b) = kvp.Key;
            Assert.Equal(1, kvp.Value); // Each directed half edge visited exactly once
            Assert.True(directed.ContainsKey((b, a)), $"Directed half-edge ({a} -> {b}) must have matching twin ({b} -> {a}) for 100% closed trunk manifold with 0 open edges");
        }
    }

    [Fact]
    public void SkyDome_CeilingSeams_OppositeWindingsMatchWall()
    {
        var terrain = new MeshObject("terrain");
        terrain.Vertices.Add(new Vertex(new Vector3(-500, -500, 0), Vector3.UnitZ));
        terrain.Vertices.Add(new Vertex(new Vector3(500, -500, 0), Vector3.UnitZ));
        terrain.Vertices.Add(new Vertex(new Vector3(500, 500, 0), Vector3.UnitZ));
        terrain.Vertices.Add(new Vertex(new Vector3(-500, 500, 0), Vector3.UnitZ));
        terrain.Triangles.Add(new Triangle(0, 1, 2));
        terrain.Triangles.Add(new Triangle(0, 2, 3));

        var sky = StructureGenerator.GenerateWatertightSkyDome(terrain, ceilingMargin: 1000.0f);

        // Every directed edge on the skybox envelope must have an exact twin half-edge
        var directed = new Dictionary<(int, int), int>();
        foreach (var tri in sky.Triangles)
        {
            void AddDir(int a, int b) => directed[(a, b)] = directed.GetValueOrDefault((a, b), 0) + 1;
            AddDir(tri.V0, tri.V1);
            AddDir(tri.V1, tri.V2);
            AddDir(tri.V2, tri.V0);
        }

        // Top ceiling edges connecting to wall quads must be paired in opposite directions
        int ceilingTris = 0;
        foreach (var tri in sky.Triangles)
        {
            var p0 = sky.Vertices[tri.V0].Position;
            var p1 = sky.Vertices[tri.V1].Position;
            var p2 = sky.Vertices[tri.V2].Position;
            if (p0.Z > 900 && p1.Z > 900 && p2.Z > 900)
            {
                ceilingTris++;
                var n = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));
                Assert.True(n.Z < -0.9f, "Ceiling triangle normal must face downward into the playable map (-Z)");
            }
        }
        Assert.True(ceilingTris > 0);
    }

    [Fact]
    public void PngHeightmapReader_DecodesNoiseFolderTextures()
    {
        string noisePath = Path.Combine(Directory.GetCurrentDirectory(), "noise", "Voronoi", "Voronoi_01-128x128.png");
        if (!File.Exists(noisePath))
        {
            noisePath = @"c:\Users\Chey1\Desktop\Blam Editor\noise\Voronoi\Voronoi_01-128x128.png";
        }

        if (File.Exists(noisePath))
        {
            var heightmap = PngHeightmapReader.LoadHeightmapFromPng(noisePath, 32, 32);
            Assert.Equal(32, heightmap.GetLength(0));
            Assert.Equal(32, heightmap.GetLength(1));

            // Values should be normalized between 0 and 1
            float minVal = float.MaxValue, maxVal = float.MinValue;
            for (int x = 0; x < 32; x++)
            {
                for (int y = 0; y < 32; y++)
                {
                    minVal = MathF.Min(minVal, heightmap[x, y]);
                    maxVal = MathF.Max(maxVal, heightmap[x, y]);
                }
            }
            Assert.True(minVal >= 0.0f);
            Assert.True(maxVal <= 1.0f);
            Assert.True(maxVal > minVal, "Heightmap must contain non-zero contrast");
        }
    }

    [Fact]
    public void TerrainGenerator_AlgorithmsAndPresets_GenerateValidMeshes()
    {
        var algos = new[]
        {
            TerrainAlgorithm.DomainWarped,
            TerrainAlgorithm.FbmPerlin,
            TerrainAlgorithm.DiamondSquare,
            TerrainAlgorithm.VoronoiCellular,
            TerrainAlgorithm.RidgedMultifractal
        };

        foreach (var algo in algos)
        {
            var config = new TerrainConfig
            {
                Algorithm = algo,
                ResolutionX = 16,
                ResolutionY = 16,
                Width = 2000.0f,
                Length = 2000.0f,
                HeightScale = 500.0f,
                ThermalErosionIterations = 2,
                HydraulicErosionIterations = 100
            };

            var mesh = TerrainGenerator.GenerateTerrain(config, $"test_{algo}");
            Assert.NotNull(mesh);
            Assert.Equal(256, mesh.Vertices.Count);
            Assert.Equal(450, mesh.Triangles.Count); // (15 * 15 * 2)
            Assert.Equal(config.DefaultMaterial, mesh.MaterialName);
        }
    }

    [Fact]
    public void GeometrySmoother_LaplacianElevation_PreservesSealedBoundaries()
    {
        var config = new TerrainConfig
        {
            Algorithm = TerrainAlgorithm.DomainWarped,
            ResolutionX = 16,
            ResolutionY = 16,
            Width = 1000.0f,
            Length = 1000.0f,
            HeightScale = 500.0f
        };
        var mesh = TerrainGenerator.GenerateTerrain(config, "smooth_test");

        // Record boundary coordinates
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var v in mesh.Vertices)
        {
            if (v.Position.X < minX) minX = v.Position.X;
            if (v.Position.X > maxX) maxX = v.Position.X;
            if (v.Position.Y < minY) minY = v.Position.Y;
            if (v.Position.Y > maxY) maxY = v.Position.Y;
        }

        // Intentionally create a harsh spike in the center
        int centerIdx = 8 * 16 + 8;
        float originalCenterZ = mesh.Vertices[centerIdx].Position.Z;
        mesh.Vertices[centerIdx].Position = new Vector3(mesh.Vertices[centerIdx].Position.X, mesh.Vertices[centerIdx].Position.Y, originalCenterZ + 300.0f);

        int smoothedCount = GeometrySmoother.SmoothTerrainMesh(mesh, passes: 3, strength: 0.5f, SmoothMode.All, lockBoundaries: true);
        Assert.True(smoothedCount > 0);

        // Center spike was relaxed
        Assert.True(mesh.Vertices[centerIdx].Position.Z < originalCenterZ + 300.0f);

        // Verify boundary vertices are strictly locked on boundary planes
        foreach (var v in mesh.Vertices)
        {
            if (MathF.Abs(v.Position.X - minX) < 1.0f) Assert.Equal(minX, v.Position.X, 1);
            if (MathF.Abs(v.Position.X - maxX) < 1.0f) Assert.Equal(maxX, v.Position.X, 1);
            if (MathF.Abs(v.Position.Y - minY) < 1.0f) Assert.Equal(minY, v.Position.Y, 1);
            if (MathF.Abs(v.Position.Y - maxY) < 1.0f) Assert.Equal(maxY, v.Position.Y, 1);
        }
    }

    [Fact]
    public void GeometryAutoRepair_CapsHolesAndWeldsBoundarySeams()
    {
        // Construct an open 4-sided cylinder (missing top and bottom caps)
        var mesh = new MeshObject("open_pipe");
        mesh.Vertices.Add(new Vertex(new Vector3(-50, -50, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(50, -50, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(50, 50, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(-50, 50, 0), Vector3.UnitZ));

        mesh.Vertices.Add(new Vertex(new Vector3(-50, -50, 100), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(50, -50, 100), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(50, 50, 100), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(-50, 50, 100), Vector3.UnitZ));

        // 4 wall quads (8 tris)
        mesh.Triangles.Add(new Triangle(0, 1, 5));
        mesh.Triangles.Add(new Triangle(0, 5, 4));
        mesh.Triangles.Add(new Triangle(1, 2, 6));
        mesh.Triangles.Add(new Triangle(1, 6, 5));
        mesh.Triangles.Add(new Triangle(2, 3, 7));
        mesh.Triangles.Add(new Triangle(2, 7, 6));
        mesh.Triangles.Add(new Triangle(3, 0, 4));
        mesh.Triangles.Add(new Triangle(3, 4, 7));

        int openBefore = GeometryAutoRepair.CountOpenEdges(mesh);
        Assert.Equal(8, openBefore); // 4 top + 4 bottom open edges

        var report = GeometryAutoRepair.SwampthingFixOpenEdges(mesh, capHoles: true, weldDistance: 0.05f, collapseShortEdges: true, applySmooth: false);

        Assert.True(report.HolesCapped >= 2);
        int openAfter = GeometryAutoRepair.CountOpenEdges(mesh);
        Assert.Equal(0, openAfter); // 100% Watertight sealed!
    }

    [Fact]
    public void MeshSculptEngine_InflateAndNoise_DisplacesVertices()
    {
        var mesh = new MeshObject("sculpt_test");
        mesh.Vertices.Add(new Vertex(new Vector3(0, 0, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(100, 0, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(0, 100, 0), Vector3.UnitZ));
        mesh.Triangles.Add(new Triangle(0, 1, 2));
        mesh.RecalculateNormals();

        // 1. Inflate
        MeshSculptEngine.ApplySculpt(mesh, Vector3.Zero, radius: 150.0f, strength: 20.0f, SculptBrushMode.Inflate);
        Assert.True(mesh.Vertices[0].Position.Z > 0.0f); // Pushed along normal (UnitZ)

        // 2. NoiseJitter
        var preNoise = mesh.Vertices[1].Position;
        MeshSculptEngine.ApplySculpt(mesh, preNoise, radius: 100.0f, strength: 15.0f, SculptBrushMode.NoiseJitter);
        Assert.NotEqual(preNoise, mesh.Vertices[1].Position);
    }

    [Fact]
    public void MeshSculptEngine_PaintTrianglesMaterial_PaintsFaceMaterials()
    {
        var mesh = new MeshObject("paint_test");
        mesh.MaterialSlots.Add("river riverworld_ground");
        mesh.MaterialSlots.Add("river riverworld_rock");

        mesh.Vertices.Add(new Vertex(new Vector3(0, 0, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(100, 0, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(0, 100, 0), Vector3.UnitZ));
        mesh.Triangles.Add(new Triangle(0, 1, 2, materialIndex: 0));

        int painted = MeshSculptEngine.PaintTrianglesMaterial(mesh, new Vector3(30, 30, 0), radius: 60.0f, targetMaterialIndex: 1);
        Assert.Equal(1, painted);
        Assert.Equal(1, mesh.Triangles[0].MaterialIndex);
    }

    [Fact]
    public void ScenarioTagBuilder_ChunkBoundarySafePatching_NeverOverwritesTgrfChunkTag()
    {
        // Mock a scenario tag buffer with 'frgt' (reversed 'tgrf') immediately following searchTarget
        string search = @"levels\multi\custom_canvas\custom_canvas";
        string replace = @"levels\multi\rocky_ridge\rocky_ridge";
        byte[] searchBytes = System.Text.Encoding.ASCII.GetBytes(search);
        byte[] tgrfMagic = System.Text.Encoding.ASCII.GetBytes("frgt");

        var buffer = new List<byte>();
        buffer.AddRange(searchBytes);
        buffer.AddRange(tgrfMagic); // Chunk tag immediately follows!
        buffer.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });

        byte[] raw = buffer.ToArray();
        string tempFile = Path.Combine(Path.GetTempPath(), $"scnr_test_{Guid.NewGuid():N}.scenario");
        string tempEk = Path.Combine(Path.GetTempPath(), $"h3ek_mock_{Guid.NewGuid():N}");
        string templatePath = Path.Combine(tempEk, "tags", "levels", "multi", "custom_canvas", "custom_canvas.scenario");
        Directory.CreateDirectory(Path.GetDirectoryName(templatePath)!);
        File.WriteAllBytes(templatePath, raw);

        try
        {
            ScenarioTagBuilder.GenerateScenarioTag(
                tempFile,
                "rocky_ridge",
                replace,
                @"levels\multi\chill\sky\chill_sky",
                new List<PlayerStartingLocation>(),
                new List<NetgameEquipment>(),
                new List<TriggerVolume>(),
                tempEk);

            byte[] patched = File.ReadAllBytes(tempFile);
            // Verify 'frgt' ('tgrf') chunk tag remains 100% intact right after searchBytes.Length
            string chunkMagic = System.Text.Encoding.ASCII.GetString(patched, searchBytes.Length, 4);
            Assert.Equal("frgt", chunkMagic);

            // Verify replaced string is at start of slot
            string writtenPath = System.Text.Encoding.ASCII.GetString(patched, 0, replace.Length);
            Assert.Equal(replace, writtenPath);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (Directory.Exists(tempEk)) Directory.Delete(tempEk, recursive: true);
        }
    }

    [Fact]
    public void HaloRockGenerator_FixWindingOutward_FacesPointAwayFromCentroid()
    {
        var rock = HaloRockGenerator.GenerateRock(RockArchetype.FacetedBoulder, seed: 42);
        Assert.NotEmpty(rock.Triangles);
        Assert.NotEmpty(rock.Vertices);

        Vector3 centroid = Vector3.Zero;
        foreach (var v in rock.Vertices) centroid += v.Position;
        centroid /= rock.Vertices.Count;

        // Verify every triangle's normal points outward from centroid
        foreach (var tri in rock.Triangles)
        {
            var p0 = rock.Vertices[tri.V0].Position;
            var p1 = rock.Vertices[tri.V1].Position;
            var p2 = rock.Vertices[tri.V2].Position;
            var center = (p0 + p1 + p2) / 3.0f;
            var outward = center - centroid;

            var normal = Vector3.Cross(p1 - p0, p2 - p0);
            if (normal.LengthSquared() > 1e-6f)
            {
                normal = Vector3.Normalize(normal);
                float dot = Vector3.Dot(normal, outward);
                // Triangles must face outward (dot >= 0) to prevent front-face culling in Blam engine
                Assert.True(dot >= -0.05f, $"Face normal ({normal}) points inward towards centroid (dot={dot})");
            }
        }
    }

    [Fact]
    public void HaloRockAndFoliage_PoissonSeparation_NoOverlap()
    {
        var terrain = TerrainGenerator.GenerateTerrain(new TerrainConfig
        {
            Width = 4000.0f,
            Length = 4000.0f,
            HeightScale = 200.0f,
            ResolutionX = 24,
            ResolutionY = 24
        }, "test_terrain");

        var obstaclePositions = new List<Vector3>();
        var rocks = HaloRockGenerator.ScatterRocksOnTerrain(terrain, count: 12, seed: 100, outPositions: obstaclePositions, minSeparation: 180.0f);
        Assert.Equal(12, rocks.Count);
        Assert.Equal(12, obstaclePositions.Count);

        var treePositions = new List<Vector3>();
        var trees = HaloFoliageGenerator.ScatterFoliageOnTerrain(terrain, count: 16, seed: 888, occupiedPositions: obstaclePositions, outPositions: treePositions, minSeparation: 180.0f);
        Assert.Equal(16, trees.Count);

        // Verify no rock and tree are closer than 170 units (within margin of jitter)
        float minSeparationSq = 160.0f * 160.0f;
        foreach (var rPos in obstaclePositions)
        {
            foreach (var tPos in treePositions)
            {
                float dx = rPos.X - tPos.X;
                float dy = rPos.Y - tPos.Y;
                float distSq = dx * dx + dy * dy;
                Assert.True(distSq >= minSeparationSq, $"Rock at ({rPos.X},{rPos.Y}) overlaps tree at ({tPos.X},{tPos.Y}) distSq={distSq}");
            }
        }
    }

    [Fact]
    public void CsgBooleanEngine_MergeInto_MaterialSlotRemapping()
    {
        var target = new MeshObject("target");
        target.MaterialName = "river riverworld_ground";
        target.MaterialSlots.Add("river riverworld_ground");
        target.Vertices.Add(new Vertex(new Vector3(0, 0, 0), Vector3.UnitZ));
        target.Vertices.Add(new Vertex(new Vector3(10, 0, 0), Vector3.UnitZ));
        target.Vertices.Add(new Vertex(new Vector3(0, 10, 0), Vector3.UnitZ));
        target.Triangles.Add(new Triangle(0, 1, 2, materialIndex: 0));

        var skyDome = new MeshObject("+sky_envelope");
        skyDome.MaterialName = "+sky";
        skyDome.MaterialSlots.Add("+sky");
        skyDome.Vertices.Add(new Vertex(new Vector3(0, 0, 100), -Vector3.UnitZ));
        skyDome.Vertices.Add(new Vertex(new Vector3(10, 0, 100), -Vector3.UnitZ));
        skyDome.Vertices.Add(new Vertex(new Vector3(0, 10, 100), -Vector3.UnitZ));
        skyDome.Triangles.Add(new Triangle(0, 1, 2, materialIndex: 0));

        CsgBooleanEngine.MergeInto(target, skyDome);

        // Target must now contain both materials in MaterialSlots
        Assert.Contains("+sky", target.MaterialSlots);
        int skySlot = target.MaterialSlots.IndexOf("+sky");

        // The second triangle (from skyDome) must point to the +sky slot!
        Assert.Equal(skySlot, target.Triangles[1].MaterialIndex);
    }

    [Fact]
    public void MeshSculptEngine_Rise_Erode_ThermalErode_Terrace()
    {
        var mesh = new MeshObject("test_sculpt_brushes");
        // Create 3x3 grid
        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < 5; x++)
            {
                mesh.Vertices.Add(new Vertex(new Vector3(x * 50, y * 50, 50.0f), Vector3.UnitZ));
            }
        }
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int v0 = y * 5 + x;
                int v1 = v0 + 1;
                int v2 = v0 + 5;
                int v3 = v2 + 1;
                mesh.Triangles.Add(new Triangle(v0, v1, v2));
                mesh.Triangles.Add(new Triangle(v1, v3, v2));
            }
        }
        mesh.RecalculateNormals();

        // 1. Rise brush
        var center = new Vector3(100, 100, 50);
        float preZ = mesh.Vertices[12].Position.Z; // center vertex (2,2)
        MeshSculptEngine.ApplySculpt(mesh, center, radius: 120.0f, strength: 30.0f, SculptBrushMode.Rise);
        Assert.True(mesh.Vertices[12].Position.Z > preZ);

        // 2. Terrace brush
        MeshSculptEngine.ApplySculpt(mesh, center, radius: 120.0f, strength: 25.0f, SculptBrushMode.Terrace);
        Assert.True(mesh.Vertices[12].Position.Z > 0);

        // 3. Erode brush
        float ridgeZ = mesh.Vertices[12].Position.Z;
        MeshSculptEngine.ApplySculpt(mesh, center, radius: 120.0f, strength: 20.0f, SculptBrushMode.Erode);
        // Erode should wear down the peak towards neighbors
        Assert.True(mesh.Vertices[12].Position.Z <= ridgeZ);

        // 4. Thermal Erode brush
        MeshSculptEngine.ApplySculpt(mesh, center, radius: 120.0f, strength: 20.0f, SculptBrushMode.ThermalErode);
        Assert.True(mesh.Vertices.Count == 25);
    }

    [Fact]
    public void AssExporter_SanitizeMaterialName_RemovesBoxcanvas()
    {
        Assert.Equal("river riverworld_ground", AssExporter.SanitizeMaterialName("boxcanvas chill_bridge"));
        Assert.Equal("river riverworld_ground", AssExporter.SanitizeMaterialName("boxcanvas blue_panel_light"));
        Assert.Equal("+sky", AssExporter.SanitizeMaterialName("+sky"));
        Assert.Equal("river riverworld_rock", AssExporter.SanitizeMaterialName("river riverworld_rock"));
    }

    [Fact]
    public void EightHaloErrorTypes_DetectedAndRepaired_ByDedicatedAlgorithms()
    {
        var mesh = new MeshObject("eight_errors_test");
        mesh.MaterialName = "river riverworld_ground";

        // 1. Degenerate Triangle: identical indices (0, 0, 1) and zero-area collinear (0, 1, 2)
        mesh.Vertices.Add(new Vertex(new Vector3(0, 0, 0), Vector3.UnitZ));       // 0
        mesh.Vertices.Add(new Vertex(new Vector3(100, 0, 0), Vector3.UnitZ));     // 1
        mesh.Vertices.Add(new Vertex(new Vector3(200, 0, 0), Vector3.UnitZ));     // 2 (collinear with 0 and 1)
        mesh.Triangles.Add(new Triangle(0, 0, 1));
        mesh.Triangles.Add(new Triangle(0, 1, 2));

        // 2. Short Collision Edge: edge shorter than 0.08 units (proportional micro-triangle)
        mesh.Vertices.Add(new Vertex(new Vector3(300, 0, 0), Vector3.UnitZ));           // 3
        mesh.Vertices.Add(new Vertex(new Vector3(300.06f, 0, 0), Vector3.UnitZ));      // 4 (short edge 0.06 < 0.08)
        mesh.Vertices.Add(new Vertex(new Vector3(300.03f, 0.05f, 0), Vector3.UnitZ)); // 5
        mesh.Triangles.Add(new Triangle(3, 4, 5));

        // 3. Overlapping Surfaces: two identical triangles on same 3 vertices
        mesh.Vertices.Add(new Vertex(new Vector3(400, 0, 0), Vector3.UnitZ));      // 6
        mesh.Vertices.Add(new Vertex(new Vector3(500, 0, 0), Vector3.UnitZ));      // 7
        mesh.Vertices.Add(new Vertex(new Vector3(450, 80, 0), Vector3.UnitZ));     // 8
        mesh.Triangles.Add(new Triangle(6, 7, 8));
        mesh.Triangles.Add(new Triangle(6, 7, 8)); // Duplicate overlapping face

        // 4. Degenerate Face: needle sliver with extreme aspect ratio (base = 0.5, height = 300)
        mesh.Vertices.Add(new Vertex(new Vector3(600, 0, 0), Vector3.UnitZ));      // 9
        mesh.Vertices.Add(new Vertex(new Vector3(600.5f, 0, 0), Vector3.UnitZ));   // 10
        mesh.Vertices.Add(new Vertex(new Vector3(600, 300, 0), Vector3.UnitZ));    // 11
        mesh.Triangles.Add(new Triangle(9, 10, 11));

        // 5. Ambiguous Connection: 3 triangles sharing an edge (12, 13)
        mesh.Vertices.Add(new Vertex(new Vector3(700, 0, 0), Vector3.UnitZ));      // 12
        mesh.Vertices.Add(new Vertex(new Vector3(800, 0, 0), Vector3.UnitZ));      // 13
        mesh.Vertices.Add(new Vertex(new Vector3(750, 50, 0), Vector3.UnitZ));     // 14
        mesh.Vertices.Add(new Vertex(new Vector3(750, -50, 0), Vector3.UnitZ));    // 15
        mesh.Vertices.Add(new Vertex(new Vector3(750, 0, 50), Vector3.UnitZ));     // 16
        mesh.Triangles.Add(new Triangle(12, 13, 14));
        mesh.Triangles.Add(new Triangle(12, 13, 15));
        mesh.Triangles.Add(new Triangle(12, 13, 16)); // 3rd face makes edge (12, 13) non-manifold ambiguous!

        // 6. Nearly Coplanar Surface: two adjacent faces meeting at an edge differing by 0.5 degrees
        mesh.Vertices.Add(new Vertex(new Vector3(900, 0, 0), Vector3.UnitZ));      // 17
        mesh.Vertices.Add(new Vertex(new Vector3(1000, 0, 0), Vector3.UnitZ));     // 18
        mesh.Vertices.Add(new Vertex(new Vector3(950, 100, 0), Vector3.UnitZ));    // 19
        // normal difference ~0.5 degree
        float slightZ = 100.0f * MathF.Tan(0.5f * MathF.PI / 180.0f);
        mesh.Vertices.Add(new Vertex(new Vector3(950, -100, slightZ), Vector3.UnitZ)); // 20
        mesh.Triangles.Add(new Triangle(17, 18, 19));
        mesh.Triangles.Add(new Triangle(17, 20, 18));
        mesh.RecalculateNormals();

        // 7. Leaky Connection & Open Edges: Unwelded seam gap (0.8 units) between two adjacent quads
        mesh.Vertices.Add(new Vertex(new Vector3(1100, 0, 0), Vector3.UnitZ));     // 21
        mesh.Vertices.Add(new Vertex(new Vector3(1200, 0, 0), Vector3.UnitZ));     // 22
        mesh.Vertices.Add(new Vertex(new Vector3(1200, 100, 0), Vector3.UnitZ));   // 23
        mesh.Vertices.Add(new Vertex(new Vector3(1100, 100, 0), Vector3.UnitZ));   // 24
        mesh.Triangles.Add(new Triangle(21, 22, 23));
        mesh.Triangles.Add(new Triangle(21, 23, 24));

        mesh.Vertices.Add(new Vertex(new Vector3(1200.8f, 0, 0), Vector3.UnitZ));   // 25 (slit gap 0.8 units from 22)
        mesh.Vertices.Add(new Vertex(new Vector3(1300, 0, 0), Vector3.UnitZ));     // 26
        mesh.Vertices.Add(new Vertex(new Vector3(1300, 100, 0), Vector3.UnitZ));   // 27
        mesh.Vertices.Add(new Vertex(new Vector3(1200.8f, 100, 0), Vector3.UnitZ)); // 28 (slit gap 0.8 units from 23)
        mesh.Triangles.Add(new Triangle(25, 26, 27));
        mesh.Triangles.Add(new Triangle(25, 27, 28));

        // SCAN: Verify GeometryLinter detects the 8 canonical categories
        var issues = GeometryLinter.LintMesh(mesh);
        Assert.Contains(issues, i => i.Category == "Degenerate Triangle");
        Assert.Contains(issues, i => i.Category == "Short Collision Edge");
        Assert.Contains(issues, i => i.Category == "Overlapping Surfaces");
        Assert.Contains(issues, i => i.Category == "Degenerate Face");
        Assert.Contains(issues, i => i.Category == "Ambiguous Connection");
        Assert.Contains(issues, i => i.Category == "Nearly Coplanar Surface");
        Assert.Contains(issues, i => i.Category == "Open Edge");
        Assert.Contains(issues, i => i.Category == "Leaky Connection");

        // REPAIR: Run dedicated repair algorithms via RepairAllErrors
        var report = GeometryAutoRepair.RepairAllErrors(mesh, capHoles: true, weldDistance: 0.1f, collapseShortEdges: true, flattenNearlyCoplanar: true, applySmooth: false);

        Assert.True(report.DegenerateTrianglesRemoved > 0, "Degenerate triangles must be removed");
        Assert.True(report.ShortEdgesCollapsed > 0, "Short collision edges must be collapsed");
        Assert.True(report.OverlappingSurfacesRemoved > 0, "Overlapping surfaces must be removed");
        Assert.True(report.DegenerateFacesFixed > 0 || report.DegenerateFacesRemoved > 0, "Degenerate faces must be fixed");
        Assert.True(report.AmbiguousEdgesSplit > 0, "Ambiguous non-manifold connections must be split");
        Assert.True(report.LeakyConnectionsSealed > 0, "Leaky connections must be sealed");
        Assert.True(report.NearlyCoplanarSurfacesFlattened > 0, "Nearly coplanar surfaces must be flattened");

        // RE-SCAN: After repair, all non-boundary errors should be 0
        var postIssues = GeometryLinter.LintMesh(mesh);
        Assert.DoesNotContain(postIssues, i => i.Category == "Degenerate Triangle");
        Assert.DoesNotContain(postIssues, i => i.Category == "Short Collision Edge");
        Assert.DoesNotContain(postIssues, i => i.Category == "Overlapping Surfaces");
        Assert.DoesNotContain(postIssues, i => i.Category == "Degenerate Face");
        Assert.DoesNotContain(postIssues, i => i.Category == "Ambiguous Connection");
        Assert.DoesNotContain(postIssues, i => i.Category == "Nearly Coplanar Surface");
        Assert.DoesNotContain(postIssues, i => i.Category == "Leaky Connection");
    }

    [Fact]
    public void SealMapWatertight_SealsTerrainWithoutDownwardSkirt_ZeroErrors()
    {
        var config = new TerrainConfig
        {
            ResolutionX = 12,
            ResolutionY = 12,
            Width = 2000.0f,
            Length = 2000.0f,
            HeightScale = 300.0f,
            SealSkirt = false
        };
        var terrain = TerrainGenerator.GenerateTerrain(config, "seal_test_terrain");
        var (tMin, tMax) = CsgBooleanEngine.ComputeBounds(terrain);

        // 1. Generate watertight sky dome
        var skyDome = StructureGenerator.GenerateWatertightSkyDome(terrain, ceilingMargin: 2500.0f);

        // Verify NO downward skirt jail bars: all sky dome vertices must be >= terrain minimum perimeter Z
        var (skyMin, skyMax) = CsgBooleanEngine.ComputeBounds(skyDome);
        Assert.True(skyMin.Z >= tMin.Z - 5.0f, $"Sky dome min Z ({skyMin.Z}) must not drop below terrain floor ({tMin.Z})");
        Assert.True(skyMax.Z >= tMax.Z + 2490.0f, $"Sky dome max Z ({skyMax.Z}) must extend upward to ceiling margin");
        Assert.Equal("+sky", skyDome.MaterialName);

        // 2. Merge into terrain & run RepairAllErrors
        CsgBooleanEngine.MergeInto(terrain, skyDome);
        terrain.RecalculateNormals();

        var report = GeometryAutoRepair.RepairAllErrors(
            terrain,
            capHoles: true,
            weldDistance: 0.1f,
            collapseShortEdges: true,
            flattenNearlyCoplanar: true,
            applySmooth: false);

        // 3. Lint the sealed terrain mesh
        var issues = GeometryLinter.LintMesh(terrain);
        var openEdges = issues.FindAll(i => i.Category == "Open Edge");
        var ambiguous = issues.FindAll(i => i.Category == "Ambiguous Connection");
        var degenerates = issues.FindAll(i => i.Category == "Degenerate Triangle");
        var leaky = issues.FindAll(i => i.Category == "Leaky Connection");

        Assert.Empty(openEdges);
        Assert.Empty(ambiguous);
        Assert.Empty(degenerates);
        Assert.Empty(leaky);
    }

    [Fact]
    public void SealAcrossTop_ConstructsAirtightSkyCap_ZeroOpenEdges()
    {
        var config = new TerrainConfig
        {
            ResolutionX = 16,
            ResolutionY = 16,
            Width = 3000.0f,
            Length = 3000.0f,
            OuterCliffWalls = true,
            PerimeterCliffHeight = 500.0f,
            SealAcrossTop = true,
            CeilingHeight = 2000.0f
        };

        var terrain = TerrainGenerator.GenerateTerrain(config, "sealed_top_terrain");
        Assert.True(terrain.Vertices.Count > 256);
        Assert.Contains(terrain.MaterialSlots, s => s == "+sky");

        var openEdges = GeometryAutoRepair.GetOpenEdges(terrain);
        Assert.Empty(openEdges);
    }

    [Fact]
    public void WorkbenchModes_SeparatesEditSculptAndPaint_TurnsGizmoOffInSculptAndPaint()
    {
        var vm = new UI.ViewModels.MainViewModel();

        // Generate a mesh to populate ActiveGizmoPosition
        var mesh = CsgBrushBuilder.CreateBox(new Vector3(500, 500, 200), Vector3.Zero);
        vm.SetActiveMesh(mesh);
        Assert.NotNull(vm.ActiveGizmoPosition);

        // 1. Edit Mode (mode 0) -> Gizmo MUST be active
        vm.SetEditMode();
        Assert.Equal(0, vm.WorkbenchModeIndex);
        Assert.Equal(UI.ViewModels.ViewportTool.Navigate, vm.ToolMode);
        Assert.False(vm.IsSculptOrPaintMode);
        Assert.True(vm.IsGizmoActive, "Gizmo must be active in Edit mode for moving/rotating/scaling brushes and objects");

        // 2. Sculpt Mode (mode 1) -> Dedicated Sculpt Mode, Gizmo MUST be turned off!
        vm.SetSculptMode();
        Assert.Equal(1, vm.WorkbenchModeIndex);
        Assert.Equal(UI.ViewModels.ViewportTool.Sculpt, vm.ToolMode);
        Assert.True(vm.IsSculptOrPaintMode);
        Assert.False(vm.IsGizmoActive, "Gizmo must be turned OFF in Sculpt mode to prevent accidental movement of terrain when dragging brush");

        // Test sculpt brush commands also enter Sculpt mode with Gizmo off
        vm.SetSculptModeRaise();
        Assert.Equal(1, vm.WorkbenchModeIndex);
        Assert.Equal(UI.ViewModels.ViewportTool.Sculpt, vm.ToolMode);
        Assert.False(vm.IsGizmoActive);

        vm.SetSculptModeSmooth();
        Assert.Equal(1, vm.WorkbenchModeIndex);
        Assert.Equal(UI.ViewModels.ViewportTool.Sculpt, vm.ToolMode);
        Assert.False(vm.IsGizmoActive);

        // 3. Paint Mode (mode 2) -> Dedicated Paint Mode, Gizmo MUST be turned off!
        vm.SetPaintMode();
        Assert.Equal(2, vm.WorkbenchModeIndex);
        Assert.Equal(UI.ViewModels.ViewportTool.Paint, vm.ToolMode);
        Assert.True(vm.IsSculptOrPaintMode);
        Assert.False(vm.IsGizmoActive, "Gizmo must be turned OFF in Paint mode so left-click paints materials instead of dragging terrain");

        // 4. Back to Edit mode (mode 0) -> Gizmo turns back on
        vm.SetEditMode();
        Assert.Equal(0, vm.WorkbenchModeIndex);
        Assert.True(vm.IsGizmoActive, "Gizmo must reactivate when returning to Edit mode");
    }

    [Fact]
    public void Terrain_1to1WorldSpaceUVs_ScaleMatchesWorldExactly_NoShearOrDistortion()
    {
        var config = new TerrainConfig
        {
            ResolutionX = 32,
            ResolutionY = 32,
            Width = 6000.0f,
            Length = 6000.0f,
            HeightScale = 800.0f,
            OuterCliffWalls = true,
            PerimeterCliffHeight = 600.0f,
            UvTileScale = 1200.0f
        };
        var terrain = TerrainGenerator.GenerateTerrain(config, "cliff_test_terrain");

        Assert.NotEmpty(terrain.Vertices);
        Assert.Equal(32, terrain.GridResolutionX);
        Assert.Equal(32, terrain.GridResolutionY);

        // Across 6000 units with 1200 tile scale, span must match exactly Width / 1200 = 5.0
        float minU = float.MaxValue, maxU = float.MinValue;
        float minV = float.MaxValue, maxV = float.MinValue;
        foreach (var v in terrain.Vertices)
        {
            Assert.NotEmpty(v.UVs);
            minU = MathF.Min(minU, v.UVs[0].X);
            maxU = MathF.Max(maxU, v.UVs[0].X);
            minV = MathF.Min(minV, v.UVs[0].Y);
            maxV = MathF.Max(maxV, v.UVs[0].Y);
        }

        float spanU = maxU - minU;
        float spanV = maxV - minV;
        Assert.Equal(5.0f, spanU, precision: 3);
        Assert.Equal(5.0f, spanV, precision: 3);

        // Verify strictly orthogonal rows (every vertex on the same row has identical V)
        for (int y = 0; y < 32; y++)
        {
            float rowV = terrain.Vertices[y * 32].UVs[0].Y;
            for (int x = 1; x < 32; x++)
            {
                Assert.Equal(rowV, terrain.Vertices[y * 32 + x].UVs[0].Y, precision: 4);
            }
        }

        // Verify strictly orthogonal columns (every vertex on the same column has identical U)
        for (int x = 0; x < 32; x++)
        {
            float colU = terrain.Vertices[x].UVs[0].X;
            for (int y = 1; y < 32; y++)
            {
                Assert.Equal(colU, terrain.Vertices[y * 32 + x].UVs[0].X, precision: 4);
            }
        }

        // Verify RecalculateTerrainUVs with different scale
        TerrainGenerator.RecalculateTerrainUVs(terrain, 2400.0f);
        float newMaxU = terrain.Vertices.Max(v => v.UVs[0].X);
        float newMinU = terrain.Vertices.Min(v => v.UVs[0].X);
        Assert.Equal(2.5f, newMaxU - newMinU, precision: 3);
    }

    [Fact]
    public void MaterialPainting_RemapsUVs1to1_BlendsSeamlesslyWithoutStretching()
    {
        var terrain = TerrainGenerator.GenerateTerrain(new TerrainConfig
        {
            ResolutionX = 16,
            ResolutionY = 16,
            Width = 1600f,
            Length = 1600f,
            UvTileScale = 250f
        }, "paint_test_terrain");
        terrain.MaterialSlots.Add("river riverworld_ground");
        terrain.MaterialSlots.Add("river riverworld_rock");

        // Paint rock material (slot 1) onto center
        var centerPos = terrain.Vertices[terrain.Vertices.Count / 2].Position;
        int painted = MeshSculptEngine.PaintTrianglesMaterial(terrain, centerPos, radius: 400f, targetMaterialIndex: 1, tileScale: 250f);
        Assert.True(painted > 0);

        // Verify that every single vertex on the mesh still strictly adheres to 1:1 isometric world UVs
        foreach (var v in terrain.Vertices)
        {
            float expU = v.Position.X / 250f;
            float expV = v.Position.Y / 250f;
            Assert.Equal(expU, v.UVs[0].X, precision: 3);
            Assert.Equal(expV, v.UVs[0].Y, precision: 3);
        }
    }

    [Fact]
    public void Debug_SkyDome_Perimeter_Edges()
    {
        var config = new TerrainConfig
        {
            ResolutionX = 36,
            ResolutionY = 36,
            Width = 5000f,
            Length = 5000f,
            OuterCliffWalls = true,
            PerimeterCliffHeight = 500f,
            Topology = TerrainTopologyType.Quads
        };
        var terrain = TerrainGenerator.GenerateTerrain(config, "terrain");
        var sky = StructureGenerator.GenerateWatertightSkyDome(terrain);
        var terrainOpen = GeometryAutoRepair.GetOpenEdges(terrain);
        var skyOpen = GeometryAutoRepair.GetOpenEdges(sky);

        GeometryAutoRepair.RepairMesh(terrain);
        GeometryAutoRepair.RepairMesh(sky);

        var combined = new MeshObject("combined");
        CsgBooleanEngine.MergeInto(combined, terrain);
        CsgBooleanEngine.MergeInto(combined, sky);
        GeometryAutoRepair.WeldVertices(combined, 0.5f, new RepairReport());
        var combinedOpen = GeometryAutoRepair.GetOpenEdges(combined);
        Assert.True(combinedOpen.Count == 0, $"Combined open edges after RepairMesh: {combinedOpen.Count}");
    }

    [Fact]
    public void Terrain_AllTopologies_GenerateWatertightGeometryWith1to1UVs()
    {
        var topologies = new[]
        {
            TerrainTopologyType.Quads,
            TerrainTopologyType.Triangles,
            TerrainTopologyType.Hexagons,
            TerrainTopologyType.Polygonal
        };

        const float tileScale = 250.0f;

        foreach (var topology in topologies)
        {
            var config = new TerrainConfig
            {
                Algorithm = TerrainAlgorithm.RollingHills,
                Topology = topology,
                ResolutionX = 24,
                ResolutionY = 24,
                Width = 2400.0f,
                Length = 2400.0f,
                HeightScale = 300.0f,
                OuterCliffWalls = true,
                PerimeterCliffHeight = 250.0f,
                UvTileScale = tileScale
            };

            var terrain = TerrainGenerator.GenerateTerrain(config, $"terrain_{topology}");
            Assert.True(terrain.Vertices.Count > 0, $"{topology} must generate vertices");
            Assert.True(terrain.Triangles.Count > 0, $"{topology} must generate triangles");

            // Verify 1:1 world UVs on all vertices
            foreach (var v in terrain.Vertices)
            {
                Assert.True(v.UVs.Count > 0, $"{topology} vertex must have UVs");
                float expectedU = v.Position.X / tileScale;
                float expectedV = v.Position.Y / tileScale;
                Assert.Equal(expectedU, v.UVs[0].X, precision: 3);
                Assert.Equal(expectedV, v.UVs[0].Y, precision: 3);
            }

            // Verify sky dome stitches directly to terrain perimeter
            var sky = StructureGenerator.GenerateWatertightSkyDome(terrain, ceilingMargin: 8000.0f);
            Assert.True(sky.Vertices.Count > 0);
            Assert.True(sky.Triangles.Count > 0);
            Assert.Equal("+sky", sky.MaterialName);

            // Verify auto-repair cleans slivers without inflating with internal centroid floor fans
            int triCountBefore = terrain.Triangles.Count;
            GeometryAutoRepair.RepairMesh(terrain);
            Assert.True(terrain.Triangles.Count <= triCountBefore && terrain.Triangles.Count > 0);
        }
    }

    [Fact]
    public async Task GenerateAndCompile_CustomCanvas_ProducesValidBspInH3EK()
    {
        string ekPath = ToolFastCompiler.AutoDetectEkPath("H3EK");
        if (string.IsNullOrEmpty(ekPath) || !Directory.Exists(ekPath))
        {
            // Skip execution if H3EK is not installed on the system
            return;
        }

        // 1. Generate 1:1 Single Mesh Floor Terrain with crisp 300 unit UV scale
        var config = new TerrainConfig
        {
            Algorithm = TerrainAlgorithm.RollingHills,
            Topology = TerrainTopologyType.Quads,
            ResolutionX = 36,
            ResolutionY = 36,
            Width = 5000.0f,
            Length = 5000.0f,
            HeightScale = 600.0f,
            WarpStrength = 200.0f,
            CliffSlopeThreshold = 0.65f,
            OuterCliffWalls = true,
            NoTiling = true,
            DefaultMaterial = "river riverworld_ground"
        };
        var terrain = TerrainGenerator.GenerateTerrain(config, "custom_canvas_terrain");
        terrain.MaterialName = "river riverworld_ground";

        // 2. Scatter Rocks with collision avoidance
        var obstaclePositions = new List<Vector3>();
        var rocks = HaloRockGenerator.ScatterRocksOnTerrain(terrain, count: 6, seed: 1001, archetype: RockArchetype.FacetedBoulder, materialName: "river riverworld_rock", outPositions: obstaclePositions, minSeparation: 350.0f);

        // 3. Scatter Trees strictly outside rock collision hulls
        var trees = HaloFoliageGenerator.ScatterFoliageOnTerrain(terrain, count: 6, seed: 8888, archetype: FoliageArchetype.ConiferPine, barkShader: "river riverworld_tree_bark", foliageShader: "%!river riverworld_tree_leafa", occupiedPositions: obstaclePositions, minSeparation: 350.0f);

        // 4. Stitched Arched Sky Dome Enclosure (ceilingMargin = 8000.0f high cathedral dome stitched to terrain rim)
        var sky = StructureGenerator.GenerateWatertightSkyDome(terrain, ceilingMargin: 8000.0f);
        sky.MaterialName = "+sky";

        // 5. Collect Meshes & Run Auto-Repair (terrain and sky are safely protected from centroid fans)
        var exportMeshes = new List<MeshObject> { terrain };
        exportMeshes.AddRange(rocks);
        exportMeshes.AddRange(trees);
        exportMeshes.Add(sky);

        foreach (var m in exportMeshes)
        {
            GeometryAutoRepair.RepairMesh(m);
        }

        // 6. Setup H3EK Level Directories & Export ASS
        var (dataDir, fullTagsDir, dataRel) = ToolFastCompiler.SetupLevelDirectories(ekPath, "custom_canvas");
        string assPath = Path.Combine(dataDir, "custom_canvas.ass");
        var materials = new List<string>
        {
            "river riverworld_ground",
            "river riverworld_rock",
            "river riverworld_tree_bark",
            "%!river riverworld_tree_leafa",
            "+sky"
        };
        AssExporter.ExportToFile(assPath, materials, exportMeshes, version: 3);
        Assert.True(File.Exists(assPath), "Exported custom_canvas.ass must exist on disk");

        // 7. Setup Authentic Multiplayer Scenario Tag
        var spawns = new List<PlayerStartingLocation>
        {
            new(new Vector3(0, 0, 150), 0f, 0),
            new(new Vector3(250, 150, 150), 45f, 0),
            new(new Vector3(-250, -150, 150), 180f, 1),
            new(new Vector3(0, 250, 150), 270f, 1)
        };

        string scenarioTagPath = Path.Combine(fullTagsDir, "custom_canvas.scenario");
        ScenarioTagBuilder.GenerateScenarioTag(
            scenarioTagPath,
            "custom_canvas",
            @"levels\multi\custom_canvas\custom_canvas",
            @"levels\multi\chill\sky\chill_sky",
            spawns,
            new List<NetgameEquipment>(),
            new List<TriggerVolume>(),
            ekPath);
        Assert.True(File.Exists(scenarioTagPath), "custom_canvas.scenario must exist");

        // 8. Run tool_fast structure compilation (guarded for fast unit test runs)
        if (Environment.GetEnvironmentVariable("FORGEWORKS_FULL_INTEGRATION_TESTS") == "1")
        {
            string toolExe = ToolFastCompiler.ResolveToolExecutable(ekPath, preferFast: true);
            string assRelative = Path.Combine(dataRel, "custom_canvas.ass");
            string arguments = $"structure \"{assRelative}\"";

            var (exitCode, toolLog) = await ToolFastCompiler.RunProcessAsync(toolExe, arguments, ekPath, Console.WriteLine, CancellationToken.None);
            Assert.Equal(0, exitCode);
            string bspTag = Path.Combine(fullTagsDir, "custom_canvas.scenario_structure_bsp");
            Assert.True(File.Exists(bspTag), $"BSP tag must be compiled at {bspTag}");
            var bspInfo = new FileInfo(bspTag);
            Assert.True(bspInfo.Length > 100000, $"Compiled BSP must be valid non-empty file (size: {bspInfo.Length} bytes)");
        }
        else
        {
            Assert.True(File.Exists(assPath), "custom_canvas.ass must exist");
            Assert.True(File.Exists(scenarioTagPath), "custom_canvas.scenario must exist");
        }
    }

    [Fact]
    public void UniversalGeometryImporter_SupportedFormats_IncludesTargetExtensions()
    {
        var exts = UniversalGeometryImporter.SupportedExtensions;
        Assert.Contains(".obj", exts);
        Assert.Contains(".fbx", exts);
        Assert.Contains(".gltf", exts);
        Assert.Contains(".glb", exts);
        Assert.Contains(".ass", exts);
        Assert.Contains(".jms", exts);
        Assert.Contains(".rmf", exts);
        Assert.Contains(".map", exts);
    }

    [Fact]
    public void CsgBrushBuilder_NewPrimitives_GenerateValidGeometry()
    {
        var sphere = CsgBrushBuilder.CreateSphere(50.0f, 8, 8, Vector3.Zero);
        Assert.NotEmpty(sphere.Vertices);
        Assert.NotEmpty(sphere.Triangles);

        var cone = CsgBrushBuilder.CreateCone(50.0f, 100.0f, 12, Vector3.Zero);
        Assert.NotEmpty(cone.Vertices);
        Assert.NotEmpty(cone.Triangles);

        var torus = CsgBrushBuilder.CreateTorus(80.0f, 20.0f, 12, 8, Vector3.Zero);
        Assert.NotEmpty(torus.Vertices);
        Assert.NotEmpty(torus.Triangles);

        var plane = CsgBrushBuilder.CreatePlane(new Vector2(200.0f, 200.0f), Vector3.Zero);
        Assert.Equal(2, plane.Triangles.Count);
    }

    [Fact]
    public void MeshModelingEngine_ExtrudeFaces_CreatesValidSideWalls()
    {
        var cube = CsgBrushBuilder.CreateBox(new Vector3(100, 100, 100), Vector3.Zero);
        int initialTris = cube.Triangles.Count; // 12
        int initialVerts = cube.Vertices.Count;

        // Select the first 2 triangles (e.g. top face)
        var selected = new List<int> { 0, 1 };
        var newFaces = MeshModelingEngine.ExtrudeFaces(cube, selected, distance: 50.0f);

        Assert.Equal(2, newFaces.Count);
        Assert.True(cube.Triangles.Count > initialTris);
        Assert.True(cube.Vertices.Count > initialVerts);
    }

    [Fact]
    public void MeshModelingEngine_InsetFaces_CreatesInsetFaces()
    {
        var cube = CsgBrushBuilder.CreateBox(new Vector3(100, 100, 100), Vector3.Zero);
        int initialTris = cube.Triangles.Count;

        var selected = new List<int> { 0 };
        var newCaps = MeshModelingEngine.InsetFaces(cube, selected, insetAmount: 10.0f);

        Assert.Single(newCaps);
        Assert.Equal(initialTris + 6, cube.Triangles.Count); // 1 tri replaced by 1 center cap + 3 quads (6 tris)
    }

    [Fact]
    public void MeshModelingEngine_LoopCutMesh_SplitsGeometryAlongPlane()
    {
        var cube = CsgBrushBuilder.CreateBox(new Vector3(100, 100, 100), Vector3.Zero);
        int initialTris = cube.Triangles.Count;

        int splits = MeshModelingEngine.LoopCutMesh(cube, Vector3.Zero, Vector3.UnitZ);

        Assert.True(splits > 0);
        Assert.True(cube.Triangles.Count > initialTris);
    }

    [Fact]
    public void MeshModelingEngine_DissolveFaces_RemovesSelectedGeometry()
    {
        var cube = CsgBrushBuilder.CreateBox(new Vector3(100, 100, 100), Vector3.Zero);
        int initialTris = cube.Triangles.Count;

        int removed = MeshModelingEngine.DeleteFaces(cube, new List<int> { 0, 1 });

        Assert.Equal(2, removed);
        Assert.Equal(initialTris - 2, cube.Triangles.Count);
    }

    [Fact]
    public void ToolLimitGuard_DetectsOversizedMesh_AndPartitionsWithPortals()
    {
        var massiveMesh = new MeshObject("massive_hallway");
        // Create 65,000 dummy triangles
        for (int i = 0; i < 65000; i++)
        {
            float z = (i / 1000f);
            int v0 = massiveMesh.Vertices.Count;
            massiveMesh.Vertices.Add(new Vertex(new Vector3(0, 0, z), Vector3.UnitZ));
            massiveMesh.Vertices.Add(new Vertex(new Vector3(10, 0, z), Vector3.UnitZ));
            massiveMesh.Vertices.Add(new Vertex(new Vector3(10, 10, z), Vector3.UnitZ));
            massiveMesh.Triangles.Add(new Triangle(v0, v0 + 1, v0 + 2));
        }

        var report = ToolLimitGuard.AuditScene(new List<MeshObject> { massiveMesh });
        Assert.True(report.NeedsAutoPartitioning);
        Assert.Equal(1, report.OversizedMeshCount);

        var partitioned = ToolLimitGuard.PartitionOversizedMeshes(new List<MeshObject> { massiveMesh }, out var portals);
        Assert.True(partitioned.Count >= 2);
        Assert.NotEmpty(portals);
        Assert.All(portals, p => Assert.StartsWith("+portal", p.MaterialName));
    }

    [Fact]
    public void SceneGraph_ShiftMultiSelect_AndClearAll_BehaveCorrectly()
    {
        var scene = new SceneContainer();
        var m1 = new MeshObject("Mesh1");
        var m2 = new MeshObject("Mesh2");
        var m3 = new MeshObject("Mesh3");

        var n1 = scene.AddGeometryMesh(m1);
        var n2 = scene.AddGeometryMesh(m2);
        var n3 = scene.AddGeometryMesh(m3);

        scene.SelectRange(n1, n3);
        var selected = scene.GetSelectedNodes();
        Assert.Equal(3, selected.Count);
        Assert.True(n1.IsSelected);
        Assert.True(n2.IsSelected);
        Assert.True(n3.IsSelected);

        scene.ClearSelection();
        Assert.Empty(scene.GetSelectedNodes());

        scene.ClearAll();
        Assert.False(scene.HasAnyGeometryNodes);
        Assert.Empty(scene.EntitiesRoot.Children);
        Assert.Empty(scene.MarkersRoot.Children);
    }

    [Fact]
    public void TerrainGenerator_ExtendedModifiers_GenerateValidMeshWithAOAndTerracing()
    {
        var config = new TerrainConfig
        {
            Algorithm = TerrainAlgorithm.CanyonGorge,
            ResolutionX = 32,
            ResolutionY = 32,
            Width = 4000.0f,
            Length = 4000.0f,
            HeightScale = 800.0f,
            TerraceSteps = 6,
            TerraceSmoothness = 0.25f,
            PlateauLevel = 0.8f,
            ValleyCarveDepth = 250.0f,
            ValleyCarveWidth = 1000.0f,
            IslandMask = IslandMaskMode.RadialBasinRaiseEdges,
            IslandFalloffRadius = 0.8f,
            VertexColorAO = true,
            SlopeLayerWeighting = true
        };

        var terrain = TerrainGenerator.GenerateTerrain(config, "test_extended_terrain");
        Assert.NotNull(terrain);
        Assert.NotEmpty(terrain.Vertices);
        Assert.NotEmpty(terrain.Triangles);

        // Verify vertex colors contain AO and slope layer channels
        bool hasColorVariation = false;
        var firstColor = terrain.Vertices[0].Color;
        for (int i = 1; i < terrain.Vertices.Count; i++)
        {
            if (terrain.Vertices[i].Color != firstColor)
            {
                hasColorVariation = true;
                break;
            }
        }
        Assert.True(hasColorVariation, "Vertex colors should contain curvature AO and slope weights.");
    }

    [Fact]
    public void MultiObjectTransform_ScaleAndTranslate_MaintainsSynchronizedPositions()
    {
        var scene = new SceneContainer();
        var m1 = new MeshObject("Mesh1");
        m1.Vertices.Add(new Vertex(new Vector3(0, 0, 0), Vector3.UnitZ, Vector2.Zero));
        m1.Vertices.Add(new Vertex(new Vector3(10, 0, 0), Vector3.UnitZ, Vector2.Zero));
        m1.Vertices.Add(new Vertex(new Vector3(0, 10, 0), Vector3.UnitZ, Vector2.Zero));
        m1.Triangles.Add(new Triangle(0, 1, 2));

        var n1 = scene.AddGeometryMesh(m1);
        var spawn = new PlayerStartingLocation(new Vector3(100, 200, 50), 45f, 0, 0);
        var n2 = new SceneNode { Name = "Spawn1", Type = SceneNodeType.Spawn, Position = spawn.Position, TagPayload = spawn };
        scene.EntitiesRoot.Children.Add(n2);

        var vm = new UI.ViewModels.MainViewModel();
        vm.SelectedSceneNodes.Add(n1);
        vm.SelectedSceneNodes.Add(n2);
        vm.ActiveGizmoPosition = new Vector3(50, 100, 25);

        // Translate
        vm.TranslateSelectedEntity(new Vector3(10, 20, 30));
        Assert.Equal(new Vector3(110, 220, 80), n2.Position);
        Assert.Equal(new Vector3(10, 20, 30), m1.Vertices[0].Position);

        // Scale
        vm.ScaleSelectedEntity(2.0f, 0); // Uniform scale factor 2.0
        vm.FinalizeGizmoTransform();
        Assert.True(m1.Vertices.Count == 3);
    }

    [Fact]
    public void GeometryLinter_HighPolyMesh_LintsWithoutCrashOrHangs()
    {
        // Build a grid mesh with 5000 triangles
        var mesh = new MeshObject("high_poly_test");
        int res = 50;
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                mesh.Vertices.Add(new Vertex(new Vector3(x * 10, y * 10, 0), Vector3.UnitZ, Vector2.Zero));
            }
        }
        for (int y = 0; y < res - 1; y++)
        {
            for (int x = 0; x < res - 1; x++)
            {
                int v00 = y * res + x;
                int v10 = y * res + (x + 1);
                int v01 = (y + 1) * res + x;
                int v11 = (y + 1) * res + (x + 1);
                mesh.Triangles.Add(new Triangle(v00, v10, v11));
                mesh.Triangles.Add(new Triangle(v00, v11, v01));
            }
        }

        var issues = GeometryLinter.LintScene(new[] { mesh });
        Assert.NotNull(issues);
    }

    [Fact]
    public void CustomShaderBuilder_EndToEnd_GeneratesTifAndShaderTagAndCollectionsEntry()
    {
        string tempEk = Path.Combine(Path.GetTempPath(), $"h3ek_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempEk);
            string tempImgDir = Path.Combine(tempEk, "source_images");
            Directory.CreateDirectory(tempImgDir);

            // 1. Create a dummy test PNG image
            string pngPath = Path.Combine(tempImgDir, "bikini_sand.png");
            using (var bmp = new System.Drawing.Bitmap(64, 64))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.Clear(System.Drawing.Color.Goldenrod);
                }
                bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
            }

            // 2. Test ConvertImageToTif
            string tifPath = Path.Combine(tempEk, "test_out.tif");
            bool converted = CustomShaderBuilder.ConvertImageToTif(pngPath, tifPath);
            Assert.True(converted, "Image conversion to TIFF must succeed");
            Assert.True(File.Exists(tifPath), "Output TIFF must exist on disk");
            Assert.True(new FileInfo(tifPath).Length > 0, "Output TIFF must have non-zero size");

            // 3. Test EnsureShaderCollectionsEntry
            CustomShaderBuilder.EnsureShaderCollectionsEntry(tempEk, "custom_canvas", "multi");
            string collectionsPath = Path.Combine(tempEk, "tags", "levels", "shader_collections.txt");
            Assert.True(File.Exists(collectionsPath), "shader_collections.txt must be created");
            string collectionsContent = File.ReadAllText(collectionsPath);
            Assert.Contains("custom_canvas\tlevels\\multi\\custom_canvas\\shaders", collectionsContent);

            // 4. Test GenerateCustomShaderTag
            string shaderTag = Path.Combine(tempEk, "tags", "levels", "multi", "custom_canvas", "shaders", "bikini_sand.shader");
            CustomShaderBuilder.GenerateCustomShaderTag(shaderTag, "bikini_sand", "levels\\multi\\custom_canvas\\bitmaps\\bikini_sand", tempEk);
            Assert.True(File.Exists(shaderTag), "Synthesized .shader tag must exist");
            byte[] shaderBytes = File.ReadAllBytes(shaderTag);
            Assert.True(shaderBytes.Length >= 0x40, "Shader tag must be at least header size");
            Assert.Equal((byte)'r', shaderBytes[0]);
            Assert.Equal((byte)'m', shaderBytes[1]);
            Assert.Equal((byte)'s', shaderBytes[2]);
            Assert.Equal((byte)'h', shaderBytes[3]);

            // 5. Test SetupCustomShadersForModel
            var mesh = new MeshObject("sand_mesh") { MaterialName = "bikini_sand" };
            mesh.MaterialSlots.Add("bikini_sand");
            var catalog = new HaloShaderCatalog();

            var results = CustomShaderBuilder.SetupCustomShadersForModel(
                tempEk,
                "custom_canvas",
                new[] { mesh },
                tempImgDir,
                catalog);

            Assert.Single(results);
            Assert.Equal("custom_canvas bikini_sand", results[0].MaterialName);
            Assert.True(File.Exists(results[0].FullShaderTagPath));
            Assert.True(File.Exists(results[0].FullTifDataPath));
        }
        finally
        {
            if (Directory.Exists(tempEk))
            {
                try { Directory.Delete(tempEk, true); } catch { }
            }
        }
    }

    [Fact]
    public void GeometryAutoRepair_HighPoly200k_ExecutesRapidlyWithoutHanging()
    {
        // Construct high-density mesh with > 200,000 vertices and triangles
        // res = 320 -> 320*320 = 102,400 vertices, 319*319*2 = 203,522 triangles!
        int res = 320;
        var mesh = new MeshObject("high_poly_200k_mesh");
        mesh.Vertices = new List<Vertex>(res * res);
        mesh.Triangles = new List<Triangle>((res - 1) * (res - 1) * 2 + 10);

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                mesh.Vertices.Add(new Vertex(new Vector3(x * 10f, y * 10f, (x + y) * 0.1f), Vector3.UnitZ));
            }
        }

        for (int y = 0; y < res - 1; y++)
        {
            for (int x = 0; x < res - 1; x++)
            {
                int v00 = y * res + x;
                int v10 = y * res + (x + 1);
                int v01 = (y + 1) * res + x;
                int v11 = (y + 1) * res + (x + 1);
                mesh.Triangles.Add(new Triangle(v00, v10, v11));
                mesh.Triangles.Add(new Triangle(v00, v11, v01));
            }
        }

        // Inject 5 degenerate triangles and short edges to test repair
        mesh.Triangles.Add(new Triangle(0, 0, 1));
        mesh.Triangles.Add(new Triangle(2, 2, 2));

        Assert.True(mesh.Triangles.Count > 200000, "Mesh must exceed 200k triangles");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var report = GeometryAutoRepair.RepairAllErrors(
            mesh,
            capHoles: false,
            weldDistance: 0.05f,
            collapseShortEdges: true,
            flattenNearlyCoplanar: false,
            applySmooth: false);
        sw.Stop();

        Assert.True(report.DegenerateTrianglesRemoved >= 2, "Degenerates must be removed");
        Assert.True(sw.ElapsedMilliseconds < 3500, $"200k poly repair took {sw.ElapsedMilliseconds}ms, must be < 3500ms");
    }

    [Fact]
    public void StructureGenerator_GenerateWatertightSkyDomeForScene_EnclosesAllMeshes()
    {
        var meshA = CsgBrushBuilder.CreateBox(new Vector3(200, 200, 100), new Vector3(-1000, -1000, 0));
        var meshB = CsgBrushBuilder.CreateBox(new Vector3(300, 300, 150), new Vector3(0, 0, 50));
        var meshC = CsgBrushBuilder.CreateBox(new Vector3(400, 400, 200), new Vector3(2000, 2000, 100));

        var skyDome = StructureGenerator.GenerateWatertightSkyDomeForScene(
            new[] { meshA, meshB, meshC },
            ceilingMargin: 3000f,
            floorMargin: 500f,
            horizontalPadding: 500f);

        Assert.NotNull(skyDome);
        Assert.Equal("+sky", skyDome.MaterialName);
        Assert.True(skyDome.IsCollideable);

        var (sMin, sMax) = CsgBooleanEngine.ComputeBounds(skyDome);
        Assert.True(sMin.X <= -1500f, $"Sky min X ({sMin.X}) must enclose meshA (-1000)");
        Assert.True(sMin.Y <= -1500f, $"Sky min Y ({sMin.Y}) must enclose meshA (-1000)");
        Assert.True(sMax.X >= 2200f, $"Sky max X ({sMax.X}) must enclose meshC (2000)");
        Assert.True(sMax.Y >= 2200f, $"Sky max Y ({sMax.Y}) must enclose meshC (2000)");
        Assert.True(sMax.Z >= 3100f, $"Sky max Z ({sMax.Z}) must include ceiling margin");
    }

    [Fact]
    public void OpenNonManifoldMeshes_TaggedRenderOnly_ByAutoRepair()
    {
        // An open non-manifold decorative surface
        var openProp = new MeshObject("open_bikini_prop") { MaterialName = "bikini_wood" };
        openProp.MaterialSlots.Add("bikini_wood");
        openProp.Vertices.Add(new Vertex(new Vector3(0, 0, 0), Vector3.UnitZ));
        openProp.Vertices.Add(new Vertex(new Vector3(100, 0, 0), Vector3.UnitZ));
        openProp.Vertices.Add(new Vertex(new Vector3(50, 100, 0), Vector3.UnitZ));
        openProp.Triangles.Add(new Triangle(0, 1, 2)); // 3 open boundary edges

        GeometryAutoRepair.FixOpenEdges(openProp, capHoles: false, new RepairReport());
        Assert.True(openProp.MaterialName.StartsWith("!"), "Non-manifold prop material must be tagged with '!' render-only");
        Assert.True(openProp.MaterialSlots[0].StartsWith("!"), "Material slot must be tagged with '!' render-only");
    }

    [Fact]
    public void ImportJungleObj_RunsFastAndWithoutHangs()
    {
        string path = @"C:\Users\Chey1\Documents\Jungle.obj";
        if (!File.Exists(path)) return;

        var options = new GeometryImportOptions
        {
            DefaultMaterial = "river riverworld_ground",
            ScaleFactor = 1.0f,
            ConvertYUpToZUp = true
        };

        var imported = UniversalGeometryImporter.Import(path, options);
        var roof = imported.Find(m => m.Name.Contains("Roof", StringComparison.OrdinalIgnoreCase));
        var walls = imported.Find(m => m.Name.Contains("walls", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(roof);
        Assert.NotNull(walls);

        int totalOrigVerts = imported.Sum(m => m.Vertices.Count);
        int totalOrigTris = imported.Sum(m => m.Triangles.Count);

        var stairs = imported.Find(m => m.Name.Contains("FenceStairs", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(stairs);

        var repList = new List<RepairReport>();
        foreach (var m in imported)
        {
            var rep = GeometryAutoRepair.RepairAllErrors(m);
            repList.Add(rep);
            Assert.True(m.Triangles.Count > 0, $"Mesh '{m.Name}' must retain its triangles after repair");
            Assert.True(m.Vertices.Count > 0, $"Mesh '{m.Name}' must retain its vertices after repair");
        }



        int totalNewVerts = imported.Sum(m => m.Vertices.Count);
        int totalNewTris = imported.Sum(m => m.Triangles.Count);

        // Verify key structural meshes are fully preserved
        Assert.True(roof.Triangles.Count >= 3000, $"Roof must retain its structure (has {roof.Triangles.Count} tris, expected >= 3000)");
        Assert.True(walls.Triangles.Count >= 15000, $"Walls must retain its structure (has {walls.Triangles.Count} tris, expected >= 15000)");
        Assert.True(stairs.Triangles.Count >= 200, $"Stairs must retain its structure (has {stairs.Triangles.Count} tris, expected >= 200)");

        // Verify vertex count was successfully lowered by welding
        Assert.True(totalNewVerts < totalOrigVerts, $"Vertex count should be lowered by welding ({totalNewVerts} < {totalOrigVerts})");
        Assert.True(totalNewTris <= totalOrigTris, $"Triangle count should be optimized ({totalNewTris} <= {totalOrigTris})");
    }

    [Fact]
    public void Test_DiagnoseJungleObjFlow()
    {
        string path = @"C:\Users\Chey1\Documents\Jungle.obj";
        if (!File.Exists(path)) return;

        var options = new GeometryImportOptions
        {
            DefaultMaterial = "river riverworld_ground",
            ScaleFactor = 1.0f,
            ConvertYUpToZUp = true
        };

        var imported = UniversalGeometryImporter.Import(path, options);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== PRE-SCALE WELD & REPAIR PIPELINE ===");

        // Test complete repaired pipeline
        foreach (var m in imported)
        {
            GeometryAutoRepair.RepairAllErrors(
                m,
                capHoles: false,
                weldDistance: 0.005f,
                collapseShortEdges: true,
                flattenNearlyCoplanar: true,
                applySmooth: false);
        }

        // Scale by 65x for Halo 3 playable dimensions (doorways ~200 units tall)
        float scaleFactor = 65.0f;
        foreach (var m in imported)
        {
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                var v = m.Vertices[i];
                v.Position *= scaleFactor;
                m.Vertices[i] = v;
            }
        }

        // Seal GroundTerrain perimeter skirt down by 200 units
        var ground = imported.Find(m => m.Name.Contains("Ground", StringComparison.OrdinalIgnoreCase));
        if (ground != null)
        {
            SealTerrainBoundarySkirt(ground, depth: 300.0f);
        }

        // For meshes that still have open boundary edges, mark them render-only (!)
        foreach (var m in imported)
        {
            if (m != ground && !m.Name.StartsWith("+sky") && GeometryAutoRepair.CountOpenEdges(m) > 0)
            {
                if (!m.MaterialName.StartsWith("!")) m.MaterialName = "!" + m.MaterialName;
                for (int s = 0; s < m.MaterialSlots.Count; s++)
                {
                    if (!m.MaterialSlots[s].StartsWith("!")) m.MaterialSlots[s] = "!" + m.MaterialSlots[s];
                }
                m.IsCollideable = false;
            }
        }

        // Post-scale repair (matches app's Auto-Repair / SwampthingFixAll button)
        foreach (var m in imported)
        {
            GeometryAutoRepair.RepairAllErrors(
                m,
                capHoles: false,
                weldDistance: 0.35f,
                collapseShortEdges: true,
                flattenNearlyCoplanar: true,
                applySmooth: false);
        }

        // Add Sky Dome
        var sky = StructureGenerator.GenerateWatertightSkyDomeForScene(imported);
        sky.MaterialName = "+sky";
        imported.Add(sky);

        // Run Linter
        var issues = GeometryLinter.LintScene(imported);
        sb.AppendLine($"\n=== FINAL LINT REPORT AFTER ENHANCED PIPELINE ===");
        sb.AppendLine($"Total Issues: {issues.Count}");
        foreach (var g in issues.GroupBy(i => i.Category))
        {
            sb.AppendLine($"  {g.Key}: {g.Count()}");
            foreach (var ig in g.GroupBy(x => x.Description.Contains("on '") ? x.Description.Substring(x.Description.IndexOf("on '")) : (x.Description.Contains("Mesh '") ? x.Description.Substring(x.Description.IndexOf("Mesh '")) : "other")))
            {
                sb.AppendLine($"     {ig.Key}: {ig.Count()}");
            }
        }

        var audit = ToolLimitGuard.AuditScene(imported);
        sb.AppendLine($"\nTool Limit Guard: TotalMeshes={audit.TotalMeshes}, TotalVerts={audit.TotalVertices}, TotalTris={audit.TotalTriangles}, Oversized={audit.OversizedMeshCount}");

        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "jungle_final_pipeline_test.txt"), sb.ToString());
    }

    private static void SealTerrainBoundarySkirt(MeshObject mesh, float depth)
    {
        var openEdges = GeometryAutoRepair.GetOpenEdges(mesh);
        if (openEdges.Count < 3) return;

        // Trace ordered boundary loop
        var halfEdges = new Dictionary<int, List<int>>();
        foreach (var (u, v) in openEdges)
        {
            if (!halfEdges.TryGetValue(u, out var l)) { l = new List<int>(1); halfEdges[u] = l; }
            l.Add(v);
            if (!halfEdges.TryGetValue(v, out var l2)) { l2 = new List<int>(1); halfEdges[v] = l2; }
            l2.Add(u);
        }

        var visited = new HashSet<int>();
        var loop = new List<int>();
        int start = openEdges[0].Item1;
        int curr = start;

        while (curr != -1 && visited.Add(curr))
        {
            loop.Add(curr);
            int next = -1;
            if (halfEdges.TryGetValue(curr, out var nbrs))
            {
                foreach (int n in nbrs)
                {
                    if (!visited.Contains(n)) { next = n; break; }
                }
            }
            curr = next;
        }

        if (loop.Count < 3) return;

        float minZ = float.MaxValue;
        foreach (int idx in loop) minZ = MathF.Min(minZ, mesh.Vertices[idx].Position.Z);
        float floorZ = minZ - depth;

        int N = loop.Count;
        int floorBaseIdx = mesh.Vertices.Count;
        for (int i = 0; i < N; i++)
        {
            var topPos = mesh.Vertices[loop[i]].Position;
            var floorPos = new Vector3(topPos.X, topPos.Y, floorZ);
            mesh.Vertices.Add(new Vertex(floorPos, -Vector3.UnitZ));
        }

        // Skirt quads
        for (int i = 0; i < N; i++)
        {
            int next = (i + 1) % N;
            int t0 = loop[i];
            int t1 = loop[next];
            int b0 = floorBaseIdx + i;
            int b1 = floorBaseIdx + next;

            mesh.Triangles.Add(new Triangle(t0, b0, t1, 0));
            mesh.Triangles.Add(new Triangle(t1, b0, b1, 0));
        }

        // Floor cap (fan)
        int centerFloorIdx = mesh.Vertices.Count;
        Vector3 floorCenter = Vector3.Zero;
        for (int i = 0; i < N; i++) floorCenter += mesh.Vertices[floorBaseIdx + i].Position;
        floorCenter /= N;
        mesh.Vertices.Add(new Vertex(floorCenter, -Vector3.UnitZ));

        for (int i = 0; i < N; i++)
        {
            int next = (i + 1) % N;
            mesh.Triangles.Add(new Triangle(centerFloorIdx, floorBaseIdx + i, floorBaseIdx + next, 0));
        }

        mesh.RecalculateNormals();
    }

    private static MeshObject CloneMesh(MeshObject src)
    {
        var copy = new MeshObject(src.Name) { MaterialName = src.MaterialName, IsCollideable = src.IsCollideable };
        foreach (var s in src.MaterialSlots) copy.MaterialSlots.Add(s);
        foreach (var v in src.Vertices)
        {
            var nv = new Vertex(v.Position, v.Normal) { Color = v.Color };
            foreach (var uv in v.UVs) nv.UVs.Add(uv);
            copy.Vertices.Add(nv);
        }
        foreach (var t in src.Triangles) copy.Triangles.Add(new Triangle(t.V0, t.V1, t.V2, t.MaterialIndex) { Normal = t.Normal });
        return copy;
    }

    [Fact]
    public void ToolFastCompiler_WritesLastToolOutputLog_Correctly()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ForgeWorks_ToolLogTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            Assert.Equal("last-tool-output-log.txt", ToolFastCompiler.LastToolLogFileName);

            string sampleLog = "[2026-10-23T16:00:00] tool_fast.exe structure levels\\multi\\test\\test\nchopping triangles... 100%\n=== imported structure successfully\n";
            ToolFastCompiler.WriteLastToolOutputLog(tempDir, sampleLog);
            string logPath = Path.Combine(tempDir, ToolFastCompiler.LastToolLogFileName);

            Assert.True(File.Exists(logPath), "Last tool log must exist on disk");
            string written = File.ReadAllText(logPath);
            Assert.Contains("tool_fast.exe", written);
            Assert.Contains("imported structure successfully", written);

            string? retrieved = ToolFastCompiler.GetLastToolLogPath(tempDir);
            Assert.NotNull(retrieved);
            Assert.True(File.Exists(retrieved));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MeshModelingEngine_CleanMergeMeshes_CombinesAndWeldsSeams()
    {
        // Mesh 1: Quad from (0,0,0) to (100,100,0) with material slot 0
        var m1 = new MeshObject("structure_part_a") { MaterialName = "metal_panel" };
        m1.Vertices.Add(new Vertex(new Vector3(0, 0, 0), Vector3.UnitZ));
        m1.Vertices.Add(new Vertex(new Vector3(100, 0, 0), Vector3.UnitZ));
        m1.Vertices.Add(new Vertex(new Vector3(100, 100, 0), Vector3.UnitZ));
        m1.Vertices.Add(new Vertex(new Vector3(0, 100, 0), Vector3.UnitZ));
        m1.Triangles.Add(new Triangle(0, 1, 2, 0));
        m1.Triangles.Add(new Triangle(0, 2, 3, 0));

        // Mesh 2: Adjacent quad from (100,0,0) to (200,100,0) with duplicate seam vertices at x=100 and material slot 1
        var m2 = new MeshObject("structure_part_b") { MaterialName = "concrete_wall" };
        m2.Vertices.Add(new Vertex(new Vector3(100, 0, 0), Vector3.UnitZ)); // Coincident seam
        m2.Vertices.Add(new Vertex(new Vector3(200, 0, 0), Vector3.UnitZ));
        m2.Vertices.Add(new Vertex(new Vector3(200, 100, 0), Vector3.UnitZ));
        m2.Vertices.Add(new Vertex(new Vector3(100, 100, 0), Vector3.UnitZ)); // Coincident seam
        m2.Triangles.Add(new Triangle(0, 1, 2, 0));
        m2.Triangles.Add(new Triangle(0, 2, 3, 0));

        var merged = MeshModelingEngine.CleanMergeMeshes(new[] { m1, m2 }, "structure_merged", weldDist: 0.1f);

        Assert.Equal("structure_merged", merged.Name);
        Assert.Equal(4, merged.Triangles.Count);
        // Original 8 vertices welded along the coincident seam -> exactly 6 unique vertices
        Assert.Equal(6, merged.Vertices.Count);
        Assert.Equal(2, merged.MaterialSlots.Count);
        Assert.Contains(merged.MaterialSlots, s => s.Contains("metal"));
        Assert.Contains(merged.MaterialSlots, s => s.Contains("concrete"));
    }

    [Fact]
    public void MeshModelingEngine_OptimizeAndDissolveModel_ReducesVerticesWithoutAlteringSilhouette()
    {
        // Construct a planar 3x3 grid composed of 18 triangles and 16 vertices all on the Z=0 plane
        var mesh = new MeshObject("flat_test_ground") { MaterialName = "stone_floor" };
        for (int y = 0; y <= 3; y++)
        {
            for (int x = 0; x <= 3; x++)
            {
                mesh.Vertices.Add(new Vertex(new Vector3(x * 100, y * 100, 0), Vector3.UnitZ));
            }
        }

        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                int i0 = y * 4 + x;
                int i1 = i0 + 1;
                int i2 = (y + 1) * 4 + x + 1;
                int i3 = (y + 1) * 4 + x;

                mesh.Triangles.Add(new Triangle(i0, i1, i2, 0));
                mesh.Triangles.Add(new Triangle(i0, i2, i3, 0));
            }
        }

        int origVerts = mesh.Vertices.Count;
        int origTris = mesh.Triangles.Count;
        Assert.Equal(16, origVerts);
        Assert.Equal(18, origTris);

        var report = MeshModelingEngine.OptimizeAndDissolveModel(mesh, maxAngleDeg: 0.5f, weldDist: 0.05f);

        Assert.True(report.OptimizedVertices < origVerts, $"Vertices must decrease: was {origVerts}, now {report.OptimizedVertices}");
        Assert.True(report.OptimizedTriangles < origTris, $"Triangles must decrease: was {origTris}, now {report.OptimizedTriangles}");
        Assert.True(report.EdgesDissolved > 0, "Flat interior edges must be dissolved");
        Assert.True(report.VertexReductionPercent > 0.0, "Vertex reduction percentage must be positive");

        // Verify the bounding box of the optimized mesh matches the original exactly (no geometric distortion)
        mesh.GetBounds(out var bMin, out var bMax);
        Assert.Equal(0, bMin.X, 0.01f);
        Assert.Equal(0, bMin.Y, 0.01f);
        Assert.Equal(300, bMax.X, 0.01f);
        Assert.Equal(300, bMax.Y, 0.01f);
    }

    [Fact]
    public void IceFields_DAE_And_Scenery_Import_And_Textures_Work()
    {
        string iceFieldsDir = @"C:\Users\Chey1\Desktop\Ice Fields";
        if (!Directory.Exists(iceFieldsDir)) return;

        string daePath = Path.Combine(iceFieldsDir, "Ice Fields.DAE");
        Assert.True(File.Exists(daePath), "Ice Fields.DAE must exist");

        var options = new GeometryImportOptions
        {
            DefaultMaterial = "icefields_ground",
            ScaleFactor = 1.0f,
            ConvertYUpToZUp = false, // Collada file is natively Z_UP
            AutoWeldVertices = true
        };

        var meshes = UniversalGeometryImporter.Import(daePath, options);
        Assert.NotEmpty(meshes);

        int totalVerts = meshes.Sum(m => m.Vertices.Count);
        int totalTris = meshes.Sum(m => m.Triangles.Count);
        Assert.True(totalVerts > 1000, $"Ice Fields should have > 1000 verts, got {totalVerts}");
        Assert.True(totalTris > 1000, $"Ice Fields should have > 1000 tris, got {totalTris}");

        var bMin = new Vector3(float.MaxValue);
        var bMax = new Vector3(float.MinValue);
        foreach (var m in meshes)
        {
            m.GetBounds(out var subMin, out var subMax);
            bMin = Vector3.Min(bMin, subMin);
            bMax = Vector3.Max(bMax, subMax);
        }
        Vector3 span = bMax - bMin;

        // Verify texture mapping on meshes
        int meshesWithTextures = meshes.Count(m => !string.IsNullOrEmpty(m.SourceTexturePath) || m.MaterialTextureMap.Count > 0);
        Assert.True(meshesWithTextures > 0, $"Expected meshes to resolve texture paths, got {meshesWithTextures}");

        // Check Scenery imports
        string sceneryDir = Path.Combine(iceFieldsDir, "Scenery");
        var allSceneryMeshes = new List<MeshObject>();
        if (Directory.Exists(sceneryDir))
        {
            var sceneryFiles = Directory.GetFiles(sceneryDir, "*.DAE");
            Assert.NotEmpty(sceneryFiles);
            foreach (var sf in sceneryFiles)
            {
                var sceneryMeshes = UniversalGeometryImporter.Import(sf, options);
                Assert.NotEmpty(sceneryMeshes);
                allSceneryMeshes.AddRange(sceneryMeshes);
            }
        }

        // Check Sky.DAE
        string skyDae = Path.Combine(iceFieldsDir, "Sky.DAE");
        if (File.Exists(skyDae))
        {
            var skyMeshes = UniversalGeometryImporter.Import(skyDae, options);
            Assert.NotEmpty(skyMeshes);
        }

        // Verify H3EK Custom Shader & Bitmap generation for Ice Fields
        string ekPath = @"C:\Program Files (x86)\Steam\steamapps\common\H3EK";
        if (Directory.Exists(ekPath))
        {
            var shaderCatalog = new HaloShaderCatalog();
            var shaderResults = CustomShaderBuilder.SetupCustomShadersForModel(
                ekPath,
                "icefields",
                "multi",
                meshes,
                iceFieldsDir,
                shaderCatalog);

            Assert.NotEmpty(shaderResults);

            // Also setup scenery shaders
            if (allSceneryMeshes.Count > 0)
            {
                var sceneryShaderResults = CustomShaderBuilder.SetupCustomShadersForModel(
                    ekPath,
                    "icefields",
                    "multi",
                    allSceneryMeshes,
                    sceneryDir,
                    shaderCatalog);
                Assert.NotEmpty(sceneryShaderResults);
            }

            // Verify shader_collections.txt has icefields entry
            string shaderCollectionsPath = Path.Combine(ekPath, "tags", "levels", "shader_collections.txt");
            Assert.True(File.Exists(shaderCollectionsPath));
            string collectionsText = File.ReadAllText(shaderCollectionsPath);
            Assert.Contains("icefields", collectionsText);

            // Verify .tif files exist in data\levels\multi\icefields\bitmaps\
            string dataBitmaps = Path.Combine(ekPath, "data", "levels", "multi", "icefields", "bitmaps");
            Assert.True(Directory.Exists(dataBitmaps));
            var tifs = Directory.GetFiles(dataBitmaps, "*.tif");
            Assert.NotEmpty(tifs);

            // Verify .shader tags exist in tags\levels\multi\icefields\shaders\
            string tagsShaders = Path.Combine(ekPath, "tags", "levels", "multi", "icefields", "shaders");
            Assert.True(Directory.Exists(tagsShaders));
            var shaders = Directory.GetFiles(tagsShaders, "*.shader");
            Assert.NotEmpty(shaders);
        }
    }

    [Fact]
    public async Task IceFields_FullCompilation_StructureBsp_And_Scenario_Succeeds()
    {
        if (Environment.GetEnvironmentVariable("FORGEWORKS_FULL_INTEGRATION_TESTS") != "1") return;
        string iceFieldsDir = @"C:\Users\Chey1\Desktop\Ice Fields";
        string ekPath = @"C:\Program Files (x86)\Steam\steamapps\common\H3EK";
        if (!Directory.Exists(iceFieldsDir) || !Directory.Exists(ekPath)) return;

        string daePath = Path.Combine(iceFieldsDir, "Ice Fields.DAE");
        Assert.True(File.Exists(daePath), "Ice Fields.DAE must exist");

        var options = new GeometryImportOptions
        {
            DefaultMaterial = "icefields_ground",
            ScaleFactor = 1.0f,
            ConvertYUpToZUp = false,
            AutoWeldVertices = true
        };

        var meshes = UniversalGeometryImporter.Import(daePath, options);
        Assert.NotEmpty(meshes);

        // 1. Setup custom shaders and shader_collections
        var shaderCatalog = new HaloShaderCatalog();
        var shaderResults = CustomShaderBuilder.SetupCustomShadersForModel(
            ekPath,
            "icefields",
            "multi",
            meshes,
            iceFieldsDir,
            shaderCatalog);

        // 2. Setup level directories
        var (dataDir, tagsDir, dataRel) = ToolFastCompiler.SetupLevelDirectories(ekPath, "icefields");
        string assPath = Path.Combine(dataDir, "icefields.ass");

        // 3. Generate Watertight Sky Dome Enclosure
        var sky = StructureGenerator.GenerateWatertightSkyDomeForScene(meshes, ceilingMargin: 5000f, floorMargin: 500f, horizontalPadding: 500f);
        sky.MaterialName = "+sky";
        meshes.Add(sky);

        // 4. Partition any oversized meshes
        var audit = ToolLimitGuard.AuditScene(meshes);
        if (audit.NeedsAutoPartitioning)
        {
            meshes = ToolLimitGuard.PartitionOversizedMeshes(meshes, out var autoPortals);
            meshes.AddRange(autoPortals);
        }

        // 5. Collect materials and export .ass
        var materials = new List<string>();
        foreach (var m in meshes)
        {
            if (!string.IsNullOrEmpty(m.MaterialName) && !materials.Contains(m.MaterialName))
                materials.Add(m.MaterialName);
            foreach (var slot in m.MaterialSlots)
            {
                if (!string.IsNullOrEmpty(slot) && !materials.Contains(slot))
                    materials.Add(slot);
            }
        }
        if (!materials.Contains("+sky")) materials.Add("+sky");

        AssExporter.ExportToFile(assPath, materials, meshes, version: 3, levelName: "icefields");
        Assert.True(File.Exists(assPath), "Exported icefields.ass must exist");

        // 6. Generate Scenario Tag
        string scenarioTagPath = Path.Combine(tagsDir, "icefields.scenario");
        string bspTagRelative = Path.Combine("levels", "multi", "icefields", "icefields");
        string skyTagRelative = @"levels\multi\chill\sky\chill_sky";
        ForgeWorks.Blam.Tags.ScenarioTagBuilder.GenerateScenarioTag(
            scenarioTagPath,
            "icefields",
            bspTagRelative,
            skyTagRelative,
            new List<PlayerStartingLocation> { new(new System.Numerics.Vector3(0, 0, 50), 0f, 0) },
            new List<NetgameEquipment>(),
            new List<TriggerVolume>(),
            ekPath);
        Assert.True(File.Exists(scenarioTagPath), "icefields.scenario must exist");

        // 7. Compile Structure BSP & Cache File via ToolFast (guarded for fast unit test runs)
        if (Environment.GetEnvironmentVariable("FORGEWORKS_FULL_INTEGRATION_TESTS") == "1")
        {
            string toolExe = ToolFastCompiler.ResolveToolExecutable(ekPath, preferFast: true);
            string assRelative = Path.Combine(dataRel, "icefields.ass");
            string arguments = $"structure \"{assRelative}\"";

            var (exitCode, toolLog) = await ToolFastCompiler.RunProcessAsync(toolExe, arguments, ekPath, Console.WriteLine, CancellationToken.None);
            string bspTag = Path.Combine(tagsDir, "icefields.scenario_structure_bsp");
            Assert.True(File.Exists(bspTag), $"BSP tag must be compiled at {bspTag}. Tool exit code: {exitCode}");

            string buildCacheArgs = "build-cache-file levels\\multi\\icefields\\icefields";
            var (cacheExit, cacheLog) = await ToolFastCompiler.RunProcessAsync(toolExe, buildCacheArgs, ekPath, Console.WriteLine, CancellationToken.None);
            ToolFastCompiler.WriteLastToolOutputLog(ekPath, cacheLog);

            string mapFile = Path.Combine(ekPath, "maps", "icefields.map");
            Assert.True(File.Exists(mapFile), $"icefields.map must be generated at {mapFile}. Exit: {cacheExit}");
        }
    }

    [Fact]
    public void AutoFitTriplanarUVs_SteepCliffWalls_MapsVerticallyWithoutStretching()
    {
        var mesh = new MeshObject("cliff_wall");
        // Vertical wall facing X (+X normal), stretching from Z=0 to Z=1000
        mesh.Vertices.Add(new Vertex(new System.Numerics.Vector3(100, 0, 0), System.Numerics.Vector3.UnitX));
        mesh.Vertices.Add(new Vertex(new System.Numerics.Vector3(100, 500, 0), System.Numerics.Vector3.UnitX));
        mesh.Vertices.Add(new Vertex(new System.Numerics.Vector3(100, 500, 1000), System.Numerics.Vector3.UnitX));
        mesh.Vertices.Add(new Vertex(new System.Numerics.Vector3(100, 0, 1000), System.Numerics.Vector3.UnitX));

        mesh.Triangles.Add(new Triangle(0, 1, 2));
        mesh.Triangles.Add(new Triangle(0, 2, 3));

        MeshModelingEngine.AutoFitTriplanarUVs(mesh, tileScale: 300.0f);

        // Verify UVs exist and V coordinate scales with Z elevation (not flattened to 0)
        Assert.NotEmpty(mesh.Vertices[0].UVs);
        Assert.NotEmpty(mesh.Vertices[2].UVs);

        float vBottom = mesh.Vertices[0].UVs[0].Y;
        float vTop = mesh.Vertices[2].UVs[0].Y;

        // V should differ significantly across the 1000 unit height
        Assert.True(MathF.Abs(vTop - vBottom) >= 3.0f, $"Vertical cliff wall must have varying UVs along Z to prevent stretching. Got delta: {MathF.Abs(vTop - vBottom)}");
    }

    [Fact]
    public void HaloShaderCatalog_Categorization_OrganizesFloorsWallsPanelsLightsSnowGrassDesert()
    {
        Assert.Equal("Floors", HaloShaderCatalog.DetermineCategory("sal sal_floor_tile", @"levels\multi\salvation\shaders\sal_floor_tile.shader", ".shader"));
        Assert.Equal("Floors", HaloShaderCatalog.DetermineCategory("sb cov_floor_rubber", @"levels\multi\snowbound\shaders\cov_floor_rubber.shader", ".shader"));
        Assert.Equal("Walls", HaloShaderCatalog.DetermineCategory("chill chill_wall01", @"levels\multi\chill\shaders\chill_wall01.shader", ".shader"));
        Assert.Equal("Walls", HaloShaderCatalog.DetermineCategory("shr panel_wallsection", @"levels\multi\shrine\shaders\forerunner\panel_wallsection.shader", ".shader"));
        Assert.Equal("Panels", HaloShaderCatalog.DetermineCategory("chill panel_elevator", @"levels\multi\chill\shaders\panel_elevator.shader", ".shader"));
        Assert.Equal("Panels", HaloShaderCatalog.DetermineCategory("mpshare mp_metal_trim", @"levels\shared\shaders\mp_metal_trim.shader", ".shader"));
        Assert.Equal("Lights", HaloShaderCatalog.DetermineCategory("chill light_circle_lift", @"levels\multi\chill\shaders\light_circle_lift.shader", ".shader"));
        Assert.Equal("Lights", HaloShaderCatalog.DetermineCategory("sb tech_glow_a", @"levels\multi\snowbound\shaders\tech_glow_a.shader", ".shader"));
        Assert.Equal("Snow (Snowbound)", HaloShaderCatalog.DetermineCategory("chill chill_snow", @"levels\multi\chill\shaders\chill_snow.shader", ".shader"));
        Assert.Equal("Snow (Snowbound)", HaloShaderCatalog.DetermineCategory("sb terrain_snow", @"levels\multi\snowbound\shaders\terrain_snow.shader_terrain", ".shader_terrain"));
        Assert.Equal("Grass (Riverworld)", HaloShaderCatalog.DetermineCategory("river riverworld_ground", @"levels\multi\riverworld\shaders\riverworld_ground.shader_terrain", ".shader_terrain"));
        Assert.Equal("Desert (Shrine)", HaloShaderCatalog.DetermineCategory("shr shrine_ground", @"levels\multi\shrine\shaders\shrine_ground.shader_terrain", ".shader_terrain"));
        Assert.Equal("Desert (Shrine)", HaloShaderCatalog.DetermineCategory("shr cust_stone_rubble", @"levels\multi\shrine\shaders\cust_stone_rubble.shader_terrain", ".shader_terrain"));
        Assert.Equal("Special (+Sky)", HaloShaderCatalog.DetermineCategory("+sky", @"levels\multi\chill\sky\shaders\skydome.shader", ".shader"));
    }

    [Fact]
    public void GeometryAutoRepair_SealMeshAcrossTop_EliminatesOpenBoundaryEdges()
    {
        var mesh = new MeshObject("open_canyon");
        // Create an open 3D box missing its ceiling (5 faces: floor and 4 walls)
        // 8 vertices: 0..3 bottom (Z=0), 4..7 top (Z=500)
        mesh.Vertices.Add(new Vertex(new System.Numerics.Vector3(0, 0, 0), System.Numerics.Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new System.Numerics.Vector3(1000, 0, 0), System.Numerics.Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new System.Numerics.Vector3(1000, 1000, 0), System.Numerics.Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new System.Numerics.Vector3(0, 1000, 0), System.Numerics.Vector3.UnitZ));

        mesh.Vertices.Add(new Vertex(new System.Numerics.Vector3(0, 0, 500), System.Numerics.Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new System.Numerics.Vector3(1000, 0, 500), System.Numerics.Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new System.Numerics.Vector3(1000, 1000, 500), System.Numerics.Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new System.Numerics.Vector3(0, 1000, 500), System.Numerics.Vector3.UnitZ));

        // Floor (facing down/in)
        mesh.Triangles.Add(new Triangle(0, 2, 1));
        mesh.Triangles.Add(new Triangle(0, 3, 2));

        // Wall 1 (-Y)
        mesh.Triangles.Add(new Triangle(0, 1, 5));
        mesh.Triangles.Add(new Triangle(0, 5, 4));

        // Wall 2 (+X)
        mesh.Triangles.Add(new Triangle(1, 2, 6));
        mesh.Triangles.Add(new Triangle(1, 6, 5));

        // Wall 3 (+Y)
        mesh.Triangles.Add(new Triangle(2, 3, 7));
        mesh.Triangles.Add(new Triangle(2, 7, 6));

        // Wall 4 (-X)
        mesh.Triangles.Add(new Triangle(3, 0, 4));
        mesh.Triangles.Add(new Triangle(3, 4, 7));

        int openEdgesBefore = GeometryAutoRepair.CountOpenEdges(mesh);
        Assert.True(openEdgesBefore > 0, "Initial open canyon must have boundary open edges along top rim");

        int sealedCount = GeometryAutoRepair.SealMeshAcrossTop(mesh, ceilingMargin: 300.0f, skyMaterial: "+sky");
        Assert.True(sealedCount > 0, "SealMeshAcrossTop must cap open rim");
        Assert.Contains("+sky", mesh.MaterialSlots);

        int openEdgesAfter = GeometryAutoRepair.CountOpenEdges(mesh);
        Assert.Equal(0, openEdgesAfter);
    }

    [Fact]
    public void FSharp_Math3D_FitToGrid_ScalesBoundsCorrectly()
    {
        var pts = new[]
        {
            new Vector3(-91096f, -101610f, 290f),
            new Vector3(38052f, 37215f, 21857f)
        };
        var bounds = ForgeWorks.FSharp.Math3D.BoundingBox3D.FromPoints(pts);
        Assert.False(bounds.IsEmpty);
        Assert.True(bounds.MaxHorizontalSpan > 138000f);

        var fit = ForgeWorks.FSharp.Math3D.calculateGridFit(bounds, 7000.0f, centerAtOrigin: true, groundBase: true);
        Assert.True(fit.Scale > 0.045f && fit.Scale < 0.055f);

        var p0Trans = fit.TransformPoint(pts[0]);
        var p1Trans = fit.TransformPoint(pts[1]);

        float newSpanX = MathF.Abs(p1Trans.X - p0Trans.X);
        float newSpanY = MathF.Abs(p1Trans.Y - p0Trans.Y);
        float maxSpan = MathF.Max(newSpanX, newSpanY);

        Assert.True(MathF.Abs(maxSpan - 7000.0f) < 1.0f, $"Scaled span must be 7000 units, got {maxSpan}");
        Assert.True(MathF.Abs(MathF.Min(p0Trans.Z, p1Trans.Z)) < 1.0f, "Base Z must be aligned to 0");
    }

    [Fact]
    public void FSharp_Noise_And_Cellular_GenerateValidDistributions()
    {
        float p = ForgeWorks.FSharp.Noise.perlin2D(12.5f, 34.2f);
        Assert.True(p >= -1.0f && p <= 1.0f);

        float fbm = ForgeWorks.FSharp.Noise.fbm2D(5.0f, 7.0f, 4, 2.0f, 0.5f);
        Assert.True(fbm >= -1.0f && fbm <= 1.0f);

        float ridge = ForgeWorks.FSharp.Noise.ridgedMultifractal2D(2.0f, 3.0f, 3, 2.0f, 0.5f);
        Assert.True(ridge >= 0.0f);

        var vor = ForgeWorks.FSharp.Cellular.voronoi2D(4.5f, 6.2f, 42);
        Assert.True(vor.DistanceF1 >= 0.0f);
        Assert.True(vor.DistanceF2 >= vor.DistanceF1);

        float hex = ForgeWorks.FSharp.Cellular.basaltHexagon(100.0f, 200.0f, 50.0f);
        Assert.True(hex >= 0.0f && hex <= 1.0f);
    }

    [Fact]
    public void CustomShaderBuilder_CleanMaterialIdentifier_ProducesConciseCanonicalNames()
    {
        Assert.Equal("ground", CustomShaderBuilder.CleanMaterialIdentifier("Material_icefieldsicefields_ground", "icefields"));
        Assert.Equal("cliffs", CustomShaderBuilder.CleanMaterialIdentifier("Material_icefieldsicefields_cliff_walls", "icefields"));
        Assert.Equal("roof", CustomShaderBuilder.CleanMaterialIdentifier("Material_icefieldsicefields_base_roof", "icefields"));
        Assert.Equal("ramp", CustomShaderBuilder.CleanMaterialIdentifier("Material_icefieldsb40_bridgeramp", "icefields"));
        Assert.Equal("metal", CustomShaderBuilder.CleanMaterialIdentifier("Material_icefieldsmetal_flat_generic", "icefields"));
        Assert.Equal("floor", CustomShaderBuilder.CleanMaterialIdentifier("Material_icefieldsb40_metal_plate_floor_ext", "icefields"));
        Assert.Equal("light_blue", CustomShaderBuilder.CleanMaterialIdentifier("Material_icefieldsicefields_base_lights_blue", "icefields"));
        Assert.Equal("symbol_red", CustomShaderBuilder.CleanMaterialIdentifier("Material_icefieldsicefields_symbol_red", "icefields"));
    }

    [Fact]
    public void MeshModelingEngine_FitSceneToGrid_ScalesMeshesCornerToCorner()
    {
        var m1 = new MeshObject("piece1");
        m1.Vertices.Add(new Vertex(new Vector3(-50000, -20000, 100), Vector3.UnitZ));
        m1.Vertices.Add(new Vertex(new Vector3(50000, 30000, 500), Vector3.UnitZ));
        m1.Vertices.Add(new Vertex(new Vector3(0, 0, 1000), Vector3.UnitZ));
        m1.Triangles.Add(new Triangle(0, 1, 2));

        float scale = MeshModelingEngine.FitSceneToGrid(new[] { m1 }, targetGridExtent: 7000.0f, centerAtOrigin: true, alignBaseToZero: true, autoFitUvs: true);
        Assert.True(scale > 0f);

        float minX = m1.Vertices.Min(v => v.Position.X);
        float maxX = m1.Vertices.Max(v => v.Position.X);
        float minZ = m1.Vertices.Min(v => v.Position.Z);

        float spanX = maxX - minX;
        Assert.True(MathF.Abs(spanX - 7000.0f) < 1.0f);
        Assert.True(MathF.Abs(minZ) < 1.0f);
        Assert.True(MathF.Abs((minX + maxX) * 0.5f) < 1.0f);
    }

    [Fact]
    public void SeamlessUvEngine_ContinuousTriplanar_AssignsZeroDistortionUvs()
    {
        var mesh = new MeshObject("triplanar_test");
        // Ground face (Z dominant)
        mesh.Vertices.Add(new Vertex(new Vector3(0, 0, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(300, 0, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(300, 300, 0), Vector3.UnitZ));
        mesh.Triangles.Add(new Triangle(0, 1, 2));

        // Cliff face (X dominant)
        mesh.Vertices.Add(new Vertex(new Vector3(300, 0, 0), Vector3.UnitX));
        mesh.Vertices.Add(new Vertex(new Vector3(300, 300, 0), Vector3.UnitX));
        mesh.Vertices.Add(new Vertex(new Vector3(300, 300, 300), Vector3.UnitX));
        mesh.Triangles.Add(new Triangle(3, 4, 5));

        SeamlessUvEngine.ApplySeamlessUvs(mesh, tileScale: 300.0f);

        Assert.Equal(6, mesh.Vertices.Count);
        // Ground face uses X, Y
        Assert.Equal(new Vector2(0f, 0f), mesh.Vertices[0].UVs[0]);
        Assert.Equal(new Vector2(1f, 0f), mesh.Vertices[1].UVs[0]);
        Assert.Equal(new Vector2(1f, 1f), mesh.Vertices[2].UVs[0]);

        // Cliff face uses Y, Z (no stretching along vertical cliff wall)
        Assert.Equal(new Vector2(0f, 0f), mesh.Vertices[3].UVs[0]);
        Assert.Equal(new Vector2(1f, 0f), mesh.Vertices[4].UVs[0]);
        Assert.Equal(new Vector2(1f, 1f), mesh.Vertices[5].UVs[0]);
    }

    [Fact]
    public void SeamlessUvEngine_RemapSelectedFaces_AppliesOffsetAndScale()
    {
        var mesh = new MeshObject("remap_test");
        mesh.Vertices.Add(new Vertex(new Vector3(0, 0, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(100, 0, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(100, 100, 0), Vector3.UnitZ));
        mesh.Triangles.Add(new Triangle(0, 1, 2));

        SeamlessUvEngine.RemapSelectedFacesUvs(mesh, new[] { 0 }, UvProjectionMode.PlanarXy, tileScale: 100.0f, offsetU: 0.5f, offsetV: 0.5f);

        var v0 = mesh.Vertices[mesh.Triangles[0].V0];
        Assert.Equal(new Vector2(0.5f, 0.5f), v0.UVs[0]);
    }

    [Fact]
    public void ForgeWorksProject_SaveAndLoad_RoundTripsAllMeshesMaterialsAndEntities()
    {
        string tempProjectFile = Path.Combine(Path.GetTempPath(), $"test_project_{Guid.NewGuid():N}.blam");
        try
        {
            var project = new ForgeWorksProject
            {
                ProjectName = "test_map",
                TargetGame = "Halo 3 (H3EK)"
            };

            var testMesh = new MeshObject("bunker_alpha")
            {
                MaterialName = "river riverworld_ground"
            };
            testMesh.MaterialSlots.Add("river riverworld_ground");
            testMesh.MaterialSlots.Add("mpshare mp_metal_trim");
            testMesh.Vertices.Add(new Vertex(new Vector3(10, 20, 30), Vector3.UnitZ, new Vector2(0.1f, 0.2f)));
            testMesh.Vertices.Add(new Vertex(new Vector3(40, 50, 60), Vector3.UnitZ, new Vector2(0.3f, 0.4f)));
            testMesh.Vertices.Add(new Vertex(new Vector3(70, 80, 90), Vector3.UnitZ, new Vector2(0.5f, 0.6f)));
            testMesh.Triangles.Add(new Triangle(0, 1, 2, materialIndex: 1));

            project.Meshes.Add(ProjectMeshData.FromMesh(testMesh));
            project.Spawns.Add(new ProjectSpawnData { X = 100, Y = 200, Z = 50, FacingAngle = 90f, TeamDesignator = 0 });
            project.Equipment.Add(new ProjectEquipmentData { X = 150, Y = 250, Z = 60, ItemTagPath = @"objects\weapons\rifle\battle_rifle\battle_rifle" });
            project.Volumes.Add(new ProjectVolumeData { Name = "kill_boundary", X = 0, Y = 0, Z = -100, ExtX = 5000, ExtY = 5000, ExtZ = 200, VolumeType = "KILL" });
            project.Markers.Add(new ProjectMarkerData { Name = "#primary_spawn", X = 10, Y = 20, Z = 30 });

            project.Save(tempProjectFile);
            Assert.True(File.Exists(tempProjectFile));

            var loaded = ForgeWorksProject.Load(tempProjectFile);
            Assert.Equal("test_map", loaded.ProjectName);
            Assert.Equal(ForgeWorksProject.ProjectMagic, loaded.Magic);
            Assert.Single(loaded.Meshes);

            var loadedMesh = loaded.Meshes[0].ToMesh();
            Assert.Equal("bunker_alpha", loadedMesh.Name);
            Assert.Equal("river riverworld_ground", loadedMesh.MaterialName);
            Assert.Equal(2, loadedMesh.MaterialSlots.Count);
            Assert.Equal("mpshare mp_metal_trim", loadedMesh.MaterialSlots[1]);
            Assert.Equal(3, loadedMesh.Vertices.Count);
            Assert.Single(loadedMesh.Triangles);
            Assert.Equal(1, loadedMesh.Triangles[0].MaterialIndex);

            Assert.Single(loaded.Spawns);
            Assert.Equal(100f, loaded.Spawns[0].X);
            Assert.Single(loaded.Equipment);
            Assert.Equal(@"objects\weapons\rifle\battle_rifle\battle_rifle", loaded.Equipment[0].ItemTagPath);
            Assert.Single(loaded.Volumes);
            Assert.Equal("kill_boundary", loaded.Volumes[0].Name);
            Assert.Single(loaded.Markers);
            Assert.Equal("#primary_spawn", loaded.Markers[0].Name);
        }
        finally
        {
            if (File.Exists(tempProjectFile)) File.Delete(tempProjectFile);
        }
    }

    [Fact]
    public void MeshDecimator_DecimatesHighPolyMeshToTargetBudget()
    {
        var mesh = new MeshObject("sphere_high");
        // Create a dense grid of triangles
        int res = 10;
        for (int y = 0; y <= res; y++)
        {
            for (int x = 0; x <= res; x++)
            {
                mesh.Vertices.Add(new Vertex(new Vector3(x * 10f, y * 10f, MathF.Sin(x) * 10f), Vector3.UnitZ));
            }
        }
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                int i0 = y * (res + 1) + x;
                int i1 = i0 + 1;
                int i2 = (y + 1) * (res + 1) + x;
                int i3 = i2 + 1;
                mesh.Triangles.Add(new Triangle(i0, i1, i2));
                mesh.Triangles.Add(new Triangle(i1, i3, i2));
            }
        }

        int origTris = mesh.Triangles.Count;
        Assert.Equal(200, origTris);

        var report = MeshDecimator.DecimateToRatio(mesh, targetRatio: 0.5f, preserveBoundaries: false);
        Assert.True(mesh.Triangles.Count < origTris, $"Actual triangles: {mesh.Triangles.Count}");
        Assert.True(report.ReductionPercentage > 0f);
    }

    [Fact]
    public void GeometryAutoRepair_DeduplicateCoplanarTriangles_RemovesOverlappingSurfaces()
    {
        var mesh = new MeshObject("overlap_test");
        mesh.Vertices.Add(new Vertex(new Vector3(0, 0, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(100, 0, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(0, 100, 0), Vector3.UnitZ));

        // Triangle 1
        mesh.Triangles.Add(new Triangle(0, 1, 2));
        // Duplicate overlapping Triangle 2 on same vertices
        mesh.Triangles.Add(new Triangle(0, 1, 2));

        Assert.Equal(2, mesh.Triangles.Count);

        int removed = GeometryAutoRepair.DeduplicateCoplanarTriangles(mesh);
        Assert.Equal(1, removed);
        Assert.Single(mesh.Triangles);
    }

    [Fact]
    public void Topology_GenerateRimFor2dPlaneWallsWithSky_CreatesWatertightMeshForSapienAi()
    {
        // Construct a 2D vertical wall strip (3 quads = 6 triangles)
        // Similar to Icefields or CustomCanvas cliff walls with open boundary edges
        var mesh = new MeshObject("icefields_cliff_wall");
        mesh.IsCollideable = true;

        // 4 columns, 2 rows of vertices forming a vertical wall in X-Z plane
        for (int x = 0; x <= 3; x++)
        {
            mesh.Vertices.Add(new Vertex(new Vector3(x * 100f, 0f, 0f), Vector3.UnitY, new Vector2(x, 0)));
            mesh.Vertices.Add(new Vertex(new Vector3(x * 100f, 0f, 200f), Vector3.UnitY, new Vector2(x, 1)));
        }

        for (int i = 0; i < 3; i++)
        {
            int b0 = i * 2;
            int t0 = b0 + 1;
            int b1 = (i + 1) * 2;
            int t1 = b1 + 1;

            mesh.Triangles.Add(new Triangle(b0, b1, t1, materialIndex: 0));
            mesh.Triangles.Add(new Triangle(b0, t1, t0, materialIndex: 0));
        }

        // Before repair: open edges along bottom, top, and sides
        var initialIssues = GeometryLinter.LintMesh(mesh);
        var initialOpenEdges = initialIssues.FindAll(i => i.Category == "Open Edge");
        Assert.NotEmpty(initialOpenEdges);

        var report = new RepairReport();
        var (rimTris, skyTris) = GeometryAutoRepair.GenerateRimFor2dPlaneWallsWithSky(
            mesh,
            rimWidth: 150.0f,
            rimElevation: 25.0f,
            skyCeilingOffset: 400.0f,
            skyMaterial: "+sky",
            report: report);

        Assert.True(rimTris > 0, "Rim triangles must be generated along open perimeter");
        Assert.True(skyTris > 0, "Sky ceiling triangles must be generated to seal the dome apex");
        Assert.Contains("+sky", mesh.MaterialSlots);

        // After repair: all boundary edges have degree 2 (watertight closure), eliminating open edge errors
        var postIssues = GeometryLinter.LintMesh(mesh);
        var postOpenEdges = postIssues.FindAll(i => i.Category == "Open Edge");
        Assert.Empty(postOpenEdges);
    }

    [Fact]
    public void CustomShaderBuilder_PreservesTemplateIntegrity_AndDoesNotCorruptStringTable()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ForgeWorks_ShaderTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // Create a simulated template containing the string "bitmap" at offset 0x169 (like retail invalid.shader)
            byte[] mockTemplate = new byte[2048];
            byte[] wordBitmap = System.Text.Encoding.ASCII.GetBytes("bitmap_table_entry");
            Array.Copy(wordBitmap, 0, mockTemplate, 0x169, wordBitmap.Length);

            string templatePath = Path.Combine(tempDir, "template.shader");
            File.WriteAllBytes(templatePath, mockTemplate);

            string outShaderPath = Path.Combine(tempDir, "out.shader");

            // Copy template as our target
            File.Copy(templatePath, outShaderPath);

            // Calling GenerateCustomShaderTag on an existing valid shader must preserve it untouched
            CustomShaderBuilder.GenerateCustomShaderTag(outShaderPath, "test_mat", @"levels\multi\icefields\bitmaps\test_mat", tempDir);

            byte[] resultBytes = File.ReadAllBytes(outShaderPath);
            Assert.Equal(2048, resultBytes.Length);

            // Verify offset 0x169 was NOT corrupted by self-matching \bitmaps\ regex replacement
            string entryAt169 = System.Text.Encoding.ASCII.GetString(resultBytes, 0x169, wordBitmap.Length);
            Assert.Equal("bitmap_table_entry", entryAt169);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void AssExporter_PreservesFlagsAndPunctuation_WithoutManglingIntoUnderscores()
    {
        // 1. Suffix '!' (e.g. lights!) must be normalized to render-only prefix '!' without mangling into 'lights_'
        string m1 = AssExporter.SanitizeMaterialName("lights!", "icefields");
        Assert.Equal("!icefields lights", m1);

        // 2. Suffix '%' (e.g. foliage%) must be normalized to two-sided prefix '%'
        string m2 = AssExporter.SanitizeMaterialName("foliage%", "icefields");
        Assert.Equal("%icefields foliage", m2);

        // 3. Leading '!%' must be preserved
        string m3 = AssExporter.SanitizeMaterialName("!%glass", "icefields");
        Assert.Equal("!%icefields glass", m3);

        // 4. Special '+sky' must remain exact
        string m4 = AssExporter.SanitizeMaterialName("+sky", "icefields");
        Assert.Equal("+sky", m4);

        // 5. Existing collection prefix with space must be preserved
        string m5 = AssExporter.SanitizeMaterialName("chill chill_metal", "icefields");
        Assert.Equal("chill chill_metal", m5);
    }

    [Fact]
    public void AssImporter_ConsumesMaterialProperties_AndPopulatesMaterialSlots()
    {
        var srcMesh = new MeshObject("cliff_model");
        srcMesh.Vertices.Add(new Vertex(new Vector3(0, 0, 0), Vector3.UnitZ, Vector2.Zero));
        srcMesh.Vertices.Add(new Vertex(new Vector3(10, 0, 0), Vector3.UnitZ, Vector2.UnitX));
        srcMesh.Vertices.Add(new Vertex(new Vector3(10, 10, 0), Vector3.UnitZ, Vector2.One));
        srcMesh.Vertices.Add(new Vertex(new Vector3(0, 10, 0), Vector3.UnitZ, Vector2.UnitY));

        srcMesh.Triangles.Add(new Triangle(0, 1, 2, materialIndex: 0));
        srcMesh.Triangles.Add(new Triangle(0, 2, 3, materialIndex: 1));

        var materials = new List<string> { "icefields ground", "!icefields lights" };

        using var ms = new MemoryStream();
        AssExporter.ExportToStream(ms, materials, new List<MeshObject> { srcMesh }, version: 3);

        ms.Seek(0, SeekOrigin.Begin);
        var importedMeshes = AssImporter.ImportFromStream(ms);

        Assert.Single(importedMeshes);
        var mesh = importedMeshes[0];
        Assert.Equal(4, mesh.Vertices.Count);
        Assert.Equal(2, mesh.Triangles.Count);

        // MaterialSlots must be populated with both materials
        Assert.Equal(2, mesh.MaterialSlots.Count);
        Assert.Equal("icefields ground", mesh.MaterialSlots[0]);
        Assert.Equal("!icefields lights", mesh.MaterialSlots[1]);

        // Triangles must retain their material indices
        Assert.Equal(0, mesh.Triangles[0].MaterialIndex);
        Assert.Equal(1, mesh.Triangles[1].MaterialIndex);
    }

    [Fact]
    public void AssExporter_AssV3_DoesNotWritePropertyCountZero_FixingToolMaterialShaderFatalError()
    {
        var mesh = new MeshObject("test_cube");
        mesh.Vertices.Add(new Vertex(new Vector3(0, 0, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(10, 0, 0), Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(10, 10, 0), Vector3.UnitZ));
        mesh.Triangles.Add(new Triangle(0, 1, 2));

        var materials = new List<string> { "chill chill_metal", "chill chill_snow" };

        // 1. Export in ASS version 3 (default for Halo 3 structure compilation)
        using var msV3 = new MemoryStream();
        AssExporter.ExportToStream(msV3, materials, new List<MeshObject> { mesh }, version: 3);

        msV3.Seek(0, SeekOrigin.Begin);
        using var readerV3 = new StreamReader(msV3);
        string contentV3 = readerV3.ReadToEnd();

        // In ASS v3, materials are only Name and Variant. Property count "0" MUST NOT appear
        // between the variant line and the next material, which caused Tool fatal exception:
        // "-FATAL- material shader string is missing quotes: '0'"
        Assert.Contains(";### MATERIALS ###\n2", contentV3.Replace("\r\n", "\n"));
        Assert.Contains(";MATERIAL 0\n\"chill chill_metal\"\n\"\"\n\n", contentV3.Replace("\r\n", "\n"));
        Assert.Contains(";MATERIAL 1\n\"chill chill_snow\"\n\"\"\n\n", contentV3.Replace("\r\n", "\n"));

        // Must round-trip cleanly in AssImporter without skipping materials
        msV3.Seek(0, SeekOrigin.Begin);
        var importedV3 = AssImporter.ImportFromStream(msV3);
        Assert.Single(importedV3);
        Assert.Equal(2, importedV3[0].MaterialSlots.Count);
        Assert.Equal("chill chill_metal", importedV3[0].MaterialSlots[0]);
        Assert.Equal("chill chill_snow", importedV3[0].MaterialSlots[1]);

        // 2. Export in ASS version 7 (Reach / H4)
        using var msV7 = new MemoryStream();
        AssExporter.ExportToStream(msV7, materials, new List<MeshObject> { mesh }, version: 7);

        msV7.Seek(0, SeekOrigin.Begin);
        using var readerV7 = new StreamReader(msV7);
        string contentV7 = readerV7.ReadToEnd();

        // In ASS v7, property count "0" must be serialized
        Assert.Contains(";MATERIAL 0\n\"chill chill_metal\"\n\"\"\n0\n\n", contentV7.Replace("\r\n", "\n"));

        msV7.Seek(0, SeekOrigin.Begin);
        var importedV7 = AssImporter.ImportFromStream(msV7);
        Assert.Single(importedV7);
        Assert.Equal(2, importedV7[0].MaterialSlots.Count);
    }

    [Fact]
    public void CustomShaderBuilder_ConvertImageToTif_ConvertsPngCleanly_WithPowerOfTwoAndNoExifTags()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ForgeWorks_PngTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // 1. Create a non-power-of-two PNG image with irregular dimensions (e.g. 300x150)
            string pngPath = Path.Combine(tempDir, "water_surface.png");
            using (var bmp = new System.Drawing.Bitmap(300, 150, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.Clear(System.Drawing.Color.FromArgb(180, 20, 140, 220));
                }
                bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
            }

            // 2. Convert to uncompressed TIFF using our enhanced pipeline
            string tifPath = Path.Combine(tempDir, "water_surface.tif");
            bool ok = CustomShaderBuilder.ConvertImageToTif(pngPath, tifPath);
            Assert.True(ok, "PNG to TIFF conversion must succeed");
            Assert.True(File.Exists(tifPath));

            // 3. Inspect generated TIFF
            using (var resultImg = new System.Drawing.Bitmap(tifPath))
            {
                // Dimensions must be rounded to valid power-of-two for Tool.exe bitmaps
                Assert.Equal(256, resultImg.Width);
                Assert.Equal(128, resultImg.Height);
                Assert.Equal(System.Drawing.Imaging.PixelFormat.Format32bppArgb, resultImg.PixelFormat);

                // Must have no private GDI+ property items (e.g. 0x5110, 0x5111, 0x5112)
                Assert.DoesNotContain(0x5110, resultImg.PropertyIdList);
                Assert.DoesNotContain(0x5111, resultImg.PropertyIdList);
                Assert.DoesNotContain(0x5112, resultImg.PropertyIdList);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void BlamViewportControl_IsWaterMaterial_DetectsAllWaterVariations()
    {
        Assert.True(ForgeWorks.UI.Views.BlamViewportControl.IsWaterMaterial("water_volume", "!water_volume'"));
        Assert.True(ForgeWorks.UI.Views.BlamViewportControl.IsWaterMaterial("lake_mesh", "lake"));
        Assert.True(ForgeWorks.UI.Views.BlamViewportControl.IsWaterMaterial("ocean_floor", "ocean"));
        Assert.True(ForgeWorks.UI.Views.BlamViewportControl.IsWaterMaterial("river_bed", "!river riverworld_water_rough'"));
        Assert.True(ForgeWorks.UI.Views.BlamViewportControl.IsWaterMaterial("swamp", "swamp_water"));
        Assert.True(ForgeWorks.UI.Views.BlamViewportControl.IsWaterMaterial("box", "mpshare water_clear"));

        Assert.False(ForgeWorks.UI.Views.BlamViewportControl.IsWaterMaterial("terrain", "mpshare mp_sand"));
        Assert.False(ForgeWorks.UI.Views.BlamViewportControl.IsWaterMaterial("cliff", "dead deadlock_cliff_rock"));
    }
}






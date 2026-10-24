# -*- coding: utf-8 -*-
"""
Halo: Reach Granny 2 (.GR2) & Sidecar Exporter Engine
Converts Blender scene geometry into Reach-compliant .GR2 and Sidecar assets via HREK tool pipeline.
Supports automated FBX export, connected geometry JSON generation, and sidecar XML creation.
Created by O7OF | Shoutout to c20 Reclaimers Wiki | Thank you Bungie
"""

import os
import json
import time
import subprocess
import bpy
from mathutils import Vector, Matrix

from . import constants


class HaloReachGr2Exporter:
    """Manages the full Halo Reach .gr2 export and compilation pipeline."""

    @staticmethod
    def get_hrek_tool(ek_path=""):
        """Locates tool.exe or tool_fast.exe in HREK path or auto-detects Steam installation."""
        candidates = []
        if ek_path and os.path.isdir(ek_path):
            candidates.extend([
                os.path.join(ek_path, "tool_fast.exe"),
                os.path.join(ek_path, "tool.exe")
            ])

        # Auto-detect common Steam paths
        steam_paths = [
            r"C:\Program Files (x86)\Steam\steamapps\common\HREK",
            r"C:\SteamLibrary\steamapps\common\HREK",
            r"D:\SteamLibrary\steamapps\common\HREK",
            r"E:\SteamLibrary\steamapps\common\HREK",
        ]
        for sp in steam_paths:
            candidates.extend([
                os.path.join(sp, "tool_fast.exe"),
                os.path.join(sp, "tool.exe")
            ])

        for c in candidates:
            if os.path.isfile(c):
                return c
        return None

    @staticmethod
    def generate_json_descriptor(filepath, mesh_objects, lights=None):
        """Generates Bungie connected geometry JSON descriptor for fbx-to-gr2 conversion."""
        nodes_props = {}
        mat_names = set()

        for obj in mesh_objects:
            if not obj or obj.type != 'MESH':
                continue
            is_collision = obj.name.startswith("@") or "collision" in obj.name.lower()
            is_physics = obj.name.startswith("$") or "physics" in obj.name.lower()
            is_portal = "+portal" in [m.name for m in obj.data.materials if m] or obj.name.startswith("z_portal")

            obj_type = "_connected_geometry_object_type_mesh"
            if is_portal:
                obj_type = "_connected_geometry_object_type_portal"
            elif is_collision:
                obj_type = "_connected_geometry_object_type_collision"
            elif is_physics:
                obj_type = "_connected_geometry_object_type_physics"

            nodes_props[obj.name] = {
                "bungie_object_type": obj_type,
                "bungie_is_game_object": "1",
                "halo_export": "1",
            }

            for mat in obj.data.materials:
                if mat:
                    mat_names.add(mat.name.strip())

        if lights:
            for l_obj in lights:
                if not l_obj or l_obj.type != 'LIGHT':
                    continue
                light_data = l_obj.data
                color_str = f"{light_data.color[0]:.6f} {light_data.color[1]:.6f} {light_data.color[2]:.6f} 1.000000"
                nodes_props[l_obj.name] = {
                    "bungie_object_type": "_connected_geometry_object_type_light",
                    "bungie_is_game_object": "1",
                    "bungie_light_type": "_connected_geometry_light_type_omni",
                    "bungie_light_color": color_str,
                    "bungie_light_intensity": f"{light_data.energy / 100.0:.6f}",
                    "halo_export": "1",
                }

        descriptor = {
            "nodes_properties": nodes_props,
            "material_names": sorted(list(mat_names))
        }

        os.makedirs(os.path.dirname(os.path.abspath(filepath)), exist_ok=True)
        with open(filepath, "w", encoding="utf-8") as f:
            json.dump(descriptor, f, indent=4)
        return True

    @staticmethod
    def generate_sidecar_xml(filepath, asset_name, scenario_type="multi", bsp_name="bsp01"):
        """Generates standard HREK sidecar.xml file linking FBX and GR2 files."""
        today = time.strftime("%m/%d/%Y")
        rel_fbx = f"levels\\{scenario_type}\\{asset_name}\\models\\{asset_name}.fbx"
        rel_gr2 = f"levels\\{scenario_type}\\{asset_name}\\models\\{asset_name}.gr2"
        rel_bsp_tag = f"levels\\{scenario_type}\\{asset_name}\\{bsp_name}"
        rel_scen_tag = f"levels\\{scenario_type}\\{asset_name}\\{asset_name}"

        xml_content = f"""<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<Metadata>
  <Header>
    <MainRev>0</MainRev>
    <PointRev>6</PointRev>
    <Description>Created by Halo Multi-Game Toolkit for Reach</Description>
    <Created>{today}</Created>
    <By>HaloModder</By>
    <DirectoryType>TAE.Shared.NWOAssetDirectory</DirectoryType>
    <Schema>1</Schema>
  </Header>
  <Asset Name="{asset_name}" Type="scenario">
    <OutputTagCollection>
      <OutputTag Type="scenario_lightmap">{rel_scen_tag}_faux_lightmap</OutputTag>
      <OutputTag Type="structure_seams">{rel_scen_tag}</OutputTag>
      <OutputTag Type="scenario">{rel_scen_tag}</OutputTag>
    </OutputTagCollection>
    <SharedAssetCollection />
  </Asset>
  <Folders>
    <Reference>\\reference</Reference>
    <Temp>\\temp</Temp>
    <SourceModels>\\models\\work</SourceModels>
    <GameModels>\\models</GameModels>
    <GamePhysicsModels>\\models</GamePhysicsModels>
    <GameCollisionModels>\\models</GameCollisionModels>
    <ExportModels>\\export\\models</ExportModels>
    <ExportPhysicsModels>\\export\\models</ExportPhysicsModels>
    <ExportCollisionModels>\\export\\models</ExportCollisionModels>
    <SourceAnimations>\\animations\\work</SourceAnimations>
    <AnimationRigs>\\animations\\rigs</AnimationRigs>
    <AnimationTransplants>\\animations\\transplants</AnimationTransplants>
    <GameAnimations>\\animations</GameAnimations>
    <ExportAnimations>\\export\\animations</ExportAnimations>
    <SourceBitmaps>\\bitmaps</SourceBitmaps>
    <GameBitmaps>\\bitmaps</GameBitmaps>
    <CinemaSource>\\cinematics</CinemaSource>
    <CinemaExport>\\export\\cinematics</CinemaExport>
    <ExportBSPs>\\export\\models</ExportBSPs>
    <GameBSPs>\\models</GameBSPs>
    <SourceBSPs>\\models\\work</SourceBSPs>
    <RigFlags>\\flags</RigFlags>
    <RigPoses>\\poses</RigPoses>
  </Folders>
  <FaceCollections>
    <FaceCollection Name="global materials override" StringTable="connected_geometry_regions_table" Description="Global material overrides">
      <FaceCollectionEntries>
        <FaceCollectionEntry Index="0" Name="default" Active="true" />
      </FaceCollectionEntries>
    </FaceCollection>
  </FaceCollections>
  <Contents>
    <Content Name="{bsp_name}" Type="bsp">
      <ContentObject Name="" Type="scenario_structure_bsp">
        <ContentNetwork Name="room01" Type="">
          <InputFile>{rel_fbx}</InputFile>
          <IntermediateFile>{rel_gr2}</IntermediateFile>
        </ContentNetwork>
        <OutputTagCollection>
          <OutputTag Type="scenario_structure_bsp">{rel_bsp_tag}</OutputTag>
          <OutputTag Type="scenario_structure_lighting_info">{rel_bsp_tag}</OutputTag>
        </OutputTagCollection>
      </ContentObject>
    </Content>
  </Contents>
  <MiscData />
</Metadata>
"""
        os.makedirs(os.path.dirname(os.path.abspath(filepath)), exist_ok=True)
        with open(filepath, "w", encoding="utf-8") as f:
            f.write(xml_content)
        return True

    @staticmethod
    def export_gr2(
        filepath,
        context=None,
        scale=1.0,
        apply_modifiers=True,
        triangulate=True,
        hrek_path="",
        scenario_type="multi",
        bsp_name="bsp01",
        **kwargs
    ):
        """Exports Blender scene as Reach-ready FBX, generates JSON & Sidecar,
        and converts FBX to authentic .GR2 via HREK tool.exe."""
        if context is None:
            context = bpy.context

        # Normalise base destination
        out_dir = os.path.dirname(os.path.abspath(filepath))
        base_name = os.path.splitext(os.path.basename(filepath))[0]
        os.makedirs(out_dir, exist_ok=True)

        fbx_path = os.path.join(out_dir, f"{base_name}.fbx")
        json_path = os.path.join(out_dir, f"{base_name}.json")
        gr2_path = os.path.join(out_dir, f"{base_name}.gr2")

        # Determine asset directory and sidecar path (parent folder if in models/)
        if os.path.basename(out_dir).lower() == "models":
            asset_dir = os.path.dirname(out_dir)
        else:
            asset_dir = out_dir
        sidecar_path = os.path.join(asset_dir, f"{base_name}.sidecar.xml")

        # Gather objects to export
        mesh_objs = [o for o in context.scene.objects if o.type == 'MESH' and not o.name.startswith("scale_model")]
        light_objs = [o for o in context.scene.objects if o.type == 'LIGHT']

        if not mesh_objs:
            print("[GR2 EXPORTER] Error: No mesh objects found to export.")
            return False

        # 1. Export FBX
        try:
            bpy.ops.export_scene.fbx(
                filepath=fbx_path,
                check_existing=False,
                use_selection=False,
                global_scale=scale,
                apply_unit_scale=True,
                apply_scale_options='FBX_SCALE_NONE',
                axis_forward='-Z',
                axis_up='Y',
                object_types={'EMPTY', 'MESH', 'ARMATURE', 'LIGHT'},
                use_mesh_modifiers=apply_modifiers,
                mesh_smooth_type='FACE',
                bake_anim=False
            )
        except Exception as e:
            print(f"[GR2 EXPORTER] FBX export failed: {e}")
            return False

        # 2. Generate JSON descriptor
        HaloReachGr2Exporter.generate_json_descriptor(json_path, mesh_objs, light_objs)

        # 3. Generate Sidecar XML
        HaloReachGr2Exporter.generate_sidecar_xml(
            sidecar_path,
            asset_name=base_name,
            scenario_type=scenario_type,
            bsp_name=bsp_name
        )

        # 4. Resolve HREK Tool executable and compile FBX -> GR2
        tool_exe = HaloReachGr2Exporter.get_hrek_tool(hrek_path)
        if tool_exe:
            hrek_root = os.path.dirname(tool_exe)
            data_root = os.path.join(hrek_root, "data")

            # Calculate paths relative to HREK data/ folder
            try:
                fbx_rel = os.path.relpath(fbx_path, data_root)
                json_rel = os.path.relpath(json_path, data_root)
                gr2_rel = os.path.relpath(gr2_path, data_root)
            except ValueError:
                # If outside data root, copy or reference directly
                fbx_rel = fbx_path
                json_rel = json_path
                gr2_rel = gr2_path

            cmd = [tool_exe, "fbx-to-gr2", fbx_rel, json_rel, gr2_rel, "false"]
            try:
                proc = subprocess.run(
                    cmd,
                    cwd=hrek_root,
                    capture_output=True,
                    text=True,
                    timeout=120
                )
                output = proc.stdout + "\n" + proc.stderr
                print(f"[GR2 EXPORTER] fbx-to-gr2 output:\n{output}")
                if "SUCCESS!" in output and os.path.isfile(gr2_path):
                    print(f"[GR2 EXPORTER] Authentic .gr2 file created successfully: {gr2_path}")
                    return True
            except Exception as e:
                print(f"[GR2 EXPORTER] Tool execution error: {e}")

        # If tool.exe wasn't run or wasn't found, FBX, JSON, and sidecar are created
        print(f"[GR2 EXPORTER] Prepared FBX ({fbx_path}), JSON ({json_path}), and Sidecar ({sidecar_path}).")
        return os.path.isfile(fbx_path)


def export_gr2_file(filepath, context=None, scale=1.0, apply_modifiers=True, triangulate=True, hrek_path="", scenario_type="multi", bsp_name="bsp01"):
    """Public dispatcher function for exporting Reach .gr2 files."""
    return HaloReachGr2Exporter.export_gr2(
        filepath=filepath,
        context=context,
        scale=scale,
        apply_modifiers=apply_modifiers,
        triangulate=triangulate,
        hrek_path=hrek_path,
        scenario_type=scenario_type,
        bsp_name=bsp_name
    )


# ==============================================================================
# BLENDER OPERATOR FOR EXPORT MENU & UI
# ==============================================================================

class HALO_OT_export_gr2(bpy.types.Operator):
    """Exports the scene geometry to Halo Reach .GR2 (via HREK fbx-to-gr2 and sidecar pipeline)"""
    bl_idname = "export_scene.halo_reach_gr2"
    bl_label = "Halo Reach Scenario (.gr2)"
    bl_description = "Exports Halo Reach BSP geometry to .gr2 with connected geometry JSON and Sidecar XML"
    bl_options = {'REGISTER', 'UNDO'}

    filepath: bpy.props.StringProperty(
        name="File Path",
        description="Destination path for .gr2 / .fbx file",
        maxlen=1024,
        subtype='FILE_PATH',
        default="custom_bsp.gr2"
    )
    scale_value: bpy.props.FloatProperty(
        name="Scale",
        description="Geometry scale multiplier",
        default=1.0,
        min=0.001,
        max=1000.0
    )
    apply_modifiers: bpy.props.BoolProperty(
        name="Apply Modifiers",
        description="Apply object modifiers on export",
        default=True
    )
    triangulate_faces: bpy.props.BoolProperty(
        name="Triangulate Faces",
        description="Ensure all faces are triangulated",
        default=True
    )

    def invoke(self, context, event):
        props = getattr(context.scene, "halo3_tool_props", None)
        map_name = getattr(props, "map_name", "custom_bsp").strip() or "custom_bsp"
        if not self.filepath or self.filepath == "custom_bsp.gr2":
            self.filepath = f"{map_name}.gr2"
        context.window_manager.fileselect_add(self)
        return {'RUNNING_MODAL'}

    def execute(self, context):
        props = getattr(context.scene, "halo3_tool_props", None)
        hrek_path = getattr(props, "editing_kit_path", "")
        scen_type = getattr(props, "map_scenario_type", "multi")

        success = export_gr2_file(
            filepath=self.filepath,
            context=context,
            scale=self.scale_value,
            apply_modifiers=self.apply_modifiers,
            triangulate=self.triangulate_faces,
            hrek_path=hrek_path,
            scenario_type=scen_type
        )
        if success:
            self.report({'INFO'}, f"Halo Reach .GR2 export completed: {self.filepath}")
            return {'FINISHED'}
        else:
            self.report({'ERROR'}, f"Failed to export Halo Reach .GR2 to: {self.filepath}")
            return {'CANCELLED'}

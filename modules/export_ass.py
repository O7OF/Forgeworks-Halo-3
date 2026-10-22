# -*- coding: utf-8 -*-
"""
Halo Amalgam Scene Specification (.ASS) Exporter
Compatible with H2EK, H3EK, and HREK.
Integrates battle-tested io_scene_halo pipeline with standalone pure-Python fallback.
Created by O7OF | Shoutout to c20 Reclaimers Wiki | Thank you Bungie
"""

import os
import math
import bpy
import bmesh
from mathutils import Matrix, Vector, Quaternion

from . import constants


def _clean_float(val, fallback=0.0):
    try:
        f = float(val)
        if math.isnan(f) or math.isinf(f) or abs(f) > 1e12:
            return fallback
        return f
    except Exception:
        return fallback


class HaloNativeAssExporter:
    """Standalone pure-Python exporter for Halo .ASS files.
    Ensures 100% compliance with tool.exe structure parser specification.
    """

    @staticmethod
    def export_ass(filepath, context=None, version=3, game_title="halo3", scale=1.0, hidden_geo=False):
        if context is None:
            context = bpy.context

        os.makedirs(os.path.dirname(os.path.abspath(filepath)), exist_ok=True)

        # Collect mesh objects to export
        export_objs = []
        for obj in context.scene.objects:
            if obj.type != 'MESH':
                continue
            # Skip non-render spartan scale reference and collision/physics objects
            if obj.name.startswith("scale_model") or obj.name.startswith("b_spartan"):
                continue
            if obj.name.startswith("@") or obj.name.startswith("$"):
                continue
            if not hidden_geo and not obj.visible_get():
                continue
            export_objs.append(obj)

        if not export_objs:
            return False

        # Build material dictionary
        materials = []
        mat_map = {}
        for obj in export_objs:
            for slot in obj.data.materials:
                if slot and slot.name not in mat_map:
                    mat_map[slot.name] = len(materials)
                    materials.append((slot.name, ""))
        if not materials:
            materials.append(("default", ""))
            mat_map["default"] = 0

        objects_data = []
        instances_data = []

        # Create root instance
        instances_data.append({
            "object_index": -1,
            "name": "Scene Root",
            "unique_id": -1,
            "parent_id": -1,
            "rot": (0.0, 0.0, 0.0, 1.0),
            "trans": (0.0, 0.0, 0.0),
            "scale": 1.0,
            "bone_groups": []
        })

        unique_counter = 1000

        for obj_idx, obj in enumerate(export_objs):
            mw = obj.matrix_world
            bm = bmesh.new()
            bm.from_mesh(obj.data)
            non_tris = [f for f in bm.faces if len(f.verts) > 3]
            if non_tris:
                bmesh.ops.triangulate(bm, faces=non_tris)
            bm.verts.index_update()
            bm.faces.index_update()
            bm.normal_update()

            # Discover UV layers
            uv_layer_names = ["UV_0", "UV_1", "UV_2", "UV_3"]
            uv_layers = [bm.loops.layers.uv.get(n) for n in uv_layer_names if bm.loops.layers.uv.get(n)]
            if not uv_layers and bm.loops.layers.uv.active:
                uv_layers = [bm.loops.layers.uv.active]

            # Discover vertex color layers
            color_layer = bm.loops.layers.color.get("Terrain_Layers")
            if not color_layer and bm.loops.layers.color.active:
                color_layer = bm.loops.layers.color.active

            verts_out = []
            for v in bm.verts:
                co = v.co
                norm = v.normal
                uvs = []
                col = (1.0, 1.0, 1.0)
                if v.link_loops:
                    l0 = v.link_loops[0]
                    for uv_l in uv_layers:
                        raw_uv = l0[uv_l].uv
                        u_clean = _clean_float(raw_uv[0], 0.0)
                        v_clean = _clean_float(raw_uv[1], 0.0)
                        uvs.append((u_clean, v_clean))
                    if color_layer:
                        col = tuple(_clean_float(c, 1.0) for c in l0[color_layer][:3])
                if not uvs:
                    uvs = [(0.0, 0.0)]
                verts_out.append((
                    _clean_float(co.x * scale),
                    _clean_float(co.y * scale),
                    _clean_float(co.z * scale),
                    _clean_float(norm.x),
                    _clean_float(norm.y),
                    _clean_float(norm.z),
                    col,
                    uvs
                ))

            tris_out = []
            for f in bm.faces:
                mat_name = "default"
                if f.material_index < len(obj.data.materials) and obj.data.materials[f.material_index]:
                    mat_name = obj.data.materials[f.material_index].name
                m_idx = mat_map.get(mat_name, 0)
                tris_out.append((m_idx, f.verts[0].index, f.verts[1].index, f.verts[2].index))

            objects_data.append((obj.name, verts_out, tris_out))

            loc = mw.to_translation()
            rot = mw.to_quaternion()
            sc = mw.to_scale()
            uniform_scale = (sc.x + sc.y + sc.z) / 3.0

            unique_counter += 1
            instances_data.append({
                "object_index": obj_idx,
                "name": obj.name,
                "unique_id": unique_counter,
                "parent_id": 0, # Child of Scene Root
                "rot": (rot.x, rot.y, rot.z, rot.w),
                "trans": (loc.x * scale, loc.y * scale, loc.z * scale),
                "scale": uniform_scale,
                "bone_groups": []
            })

            bm.free()

        # Write ASS file
        with open(filepath, "w", encoding="utf-8") as f:
            # 1. HEADER
            f.write(";### HEADER ###\n")
            f.write(f"{version}\n")
            f.write('"O7OF_HALO_TOOL"\n')
            f.write(f'"{bpy.app.version[0]}.{bpy.app.version[1]}"\n')
            f.write('"O7OF"\n')
            f.write('"HALO_BLAM_CORE"\n\n')

            # 2. MATERIALS
            f.write(f";### MATERIALS ###\n{len(materials)}\n\n")
            for m_idx, (m_name, variant) in enumerate(materials):
                f.write(f";MATERIAL {m_idx}\n")
                f.write(f'"{m_name}"\n')
                f.write(f'"{variant}"\n')
                if version >= 4:
                    f.write("0\n")
                f.write("\n")

            # 3. OBJECTS
            f.write(f";### OBJECTS ###\n{len(objects_data)}\n\n")
            for o_idx, (o_name, verts, tris) in enumerate(objects_data):
                f.write(f";OBJECT {o_idx}\n")
                f.write('"MESH"\n""\n')
                f.write(f'"{o_name}"\n')
                f.write(f"{len(verts)}\n")
                for vx, vy, vz, nx, ny, nz, col, uvs in verts:
                    f.write(f"{vx:.10f}\t{vy:.10f}\t{vz:.10f}\n")
                    f.write(f"{nx:.10f}\t{ny:.10f}\t{nz:.10f}\n")
                    if version >= 6:
                        f.write(f"{col[0]:.10f}\t{col[1]:.10f}\t{col[2]:.10f}\n")
                    # Rigid structure mesh has 0 node influences (unskinned)
                    f.write("0\n")
                    f.write(f"{len(uvs)}\n")
                    for uv in uvs:
                        u_w = _clean_float(uv[0], 0.0)
                        v_w = _clean_float(uv[1], 0.0)
                        if version >= 5:
                            f.write(f"{u_w:.10f}\t{v_w:.10f}\t0.0000000000\n")
                        else:
                            f.write(f"{u_w:.10f}\t{v_w:.10f}\n")

                f.write(f"{len(tris)}\n")
                for m_idx, v0, v1, v2 in tris:
                    if version >= 3:
                        f.write(f"{m_idx}\t\t{v0}\t{v1}\t{v2}\n")
                    else:
                        f.write(f"{m_idx}\n{v0}\n{v1}\n{v2}\n")
                f.write("\n")

            # 4. INSTANCES - Battle-tested specification
            f.write(f";### INSTANCES ###\n{len(instances_data)}\n\n")
            for idx, inst in enumerate(instances_data):
                f.write(f";INSTANCE {idx}\n")
                f.write(f"{inst['object_index']}\n")
                f.write(f'"{inst["name"]}"\n')
                f.write(f"{inst['unique_id']}\n")
                f.write(f"{inst['parent_id']}\n")
                f.write("0\n") # inheritance_flag
                f.write(f"{inst['rot'][0]:.10f}\t{inst['rot'][1]:.10f}\t{inst['rot'][2]:.10f}\t{inst['rot'][3]:.10f}\n")
                f.write(f"{inst['trans'][0]:.10f}\t{inst['trans'][1]:.10f}\t{inst['trans'][2]:.10f}\n")
                if version <= 1:
                    f.write(f"{inst['scale']:.10f}\t{inst['scale']:.10f}\t{inst['scale']:.10f}\n")
                else:
                    f.write(f"{inst['scale']:.10f}\n")
                # Pivot transform
                f.write(f"{inst['rot'][0]:.10f}\t{inst['rot'][1]:.10f}\t{inst['rot'][2]:.10f}\t{inst['rot'][3]:.10f}\n")
                f.write(f"{inst['trans'][0]:.10f}\t{inst['trans'][1]:.10f}\t{inst['trans'][2]:.10f}\n")
                if version <= 1:
                    f.write(f"{inst['scale']:.10f}\t{inst['scale']:.10f}\t{inst['scale']:.10f}\n")
                else:
                    f.write(f"{inst['scale']:.10f}\n")
                for node_idx in inst.get("bone_groups", []):
                    f.write(f"{node_idx}\n")

        return os.path.isfile(filepath) and os.path.getsize(filepath) > 0


def export_ass_file(filepath, context=None, version=3, game_title="halo3", scale=1.0, use_toolkit=True):
    """Universal ASS export dispatcher.
    Prefers io_scene_halo when available and requested, with automatic fallback to native engine.
    """
    if context is None:
        context = bpy.context

    os.makedirs(os.path.dirname(os.path.abspath(filepath)), exist_ok=True)

    if use_toolkit and hasattr(bpy.ops.export_scene, "ass"):
        try:
            # Map game title to toolkit format
            gt = "reach" if version >= 7 or game_title == "reach" else "halo3"
            res = bpy.ops.export_scene.ass(filepath=filepath, game_title=gt)
            if res == {'FINISHED'} and os.path.isfile(filepath) and os.path.getsize(filepath) > 0:
                return True
        except Exception as e:
            print(f"[ASS EXPORTER] io_scene_halo exporter reported warning: {e}. Falling back to native engine...")

    # Fallback to pure-Python native exporter
    return HaloNativeAssExporter.export_ass(
        filepath=filepath,
        context=context,
        version=version,
        game_title=game_title,
        scale=scale
    )

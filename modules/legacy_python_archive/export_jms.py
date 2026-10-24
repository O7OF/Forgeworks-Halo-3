# -*- coding: utf-8 -*-
"""
Halo Jointed Model Skeleton (.JMS) Exporter
Compatible with HCEK, H2EK, and H3EK (versions 8197, 8200, 8205, 8209, 8210, 8213).
Integrates io_scene_halo pipeline with standalone pure-Python fallback.
Created by O7OF | Shoutout to c20 Reclaimers Wiki | Thank you Bungie
"""

import os
import bpy
import bmesh
from mathutils import Matrix, Vector, Quaternion

from . import constants


class HaloNativeJmsExporter:
    """Standalone pure-Python exporter for Halo .JMS files."""

    @staticmethod
    def export_jms(filepath, context=None, version=8213, game_title="halo3", scale=1.0):
        if context is None:
            context = bpy.context

        os.makedirs(os.path.dirname(os.path.abspath(filepath)), exist_ok=True)

        export_objs = [o for o in context.scene.objects if o.type == 'MESH' and not o.name.startswith("scale_model")]
        if not export_objs:
            return False

        # Gather armature or generate default frame node
        nodes = []
        armature = None
        for o in context.scene.objects:
            if o.type == 'ARMATURE':
                armature = o
                break

        if armature:
            for b_idx, bone in enumerate(armature.data.bones):
                p_idx = -1
                if bone.parent:
                    p_idx = armature.data.bones.values().index(bone.parent)
                nodes.append({
                    "name": bone.name,
                    "parent": p_idx,
                    "child": -1,
                    "sibling": -1,
                    "rot": (0.0, 0.0, 0.0, 1.0),
                    "trans": (bone.head_local.x * scale, bone.head_local.y * scale, bone.head_local.z * scale)
                })
        else:
            # Default root frame
            nodes.append({
                "name": "b_levelroot",
                "parent": -1,
                "child": -1,
                "sibling": -1,
                "rot": (0.0, 0.0, 0.0, 1.0),
                "trans": (0.0, 0.0, 0.0)
            })

        # Materials
        materials = []
        mat_map = {}
        for obj in export_objs:
            for slot in obj.data.materials:
                if slot and slot.name not in mat_map:
                    mat_map[slot.name] = len(materials)
                    materials.append((slot.name, "<none>"))
        if not materials:
            materials.append(("default", "<none>"))
            mat_map["default"] = 0

        # Vertices & Triangles with loop-based split normals and seam preservation
        all_verts = []
        all_tris = []
        vert_cache = {}

        for obj in export_objs:
            mw = obj.matrix_world
            norm_mw = mw.to_3x3().inverted().transposed()

            bm = bmesh.new()
            bm.from_mesh(obj.data)
            bmesh.ops.triangulate(bm, faces=bm.faces[:])
            bm.normal_update()

            uv_l = bm.loops.layers.uv.active

            for f in bm.faces:
                mat_name = "default"
                if f.material_index < len(obj.data.materials) and obj.data.materials[f.material_index]:
                    mat_name = obj.data.materials[f.material_index].name
                m_idx = mat_map.get(mat_name, 0)

                tri_indices = []
                for loop in f.loops:
                    v = loop.vert
                    world_co = mw @ v.co
                    if f.smooth:
                        norm = norm_mw @ (loop.calc_normal() if hasattr(loop, 'calc_normal') else v.normal)
                    else:
                        norm = norm_mw @ f.normal
                    norm.normalize()

                    u, v_coord = 0.0, 0.0
                    if uv_l:
                        loop_uv = loop[uv_l].uv
                        u, v_coord = loop_uv.x, loop_uv.y

                    key = (
                        round(world_co.x * scale, 5),
                        round(world_co.y * scale, 5),
                        round(world_co.z * scale, 5),
                        round(norm.x, 5),
                        round(norm.y, 5),
                        round(norm.z, 5),
                        round(u, 6),
                        round(v_coord, 6)
                    )

                    if key in vert_cache:
                        idx_out = vert_cache[key]
                    else:
                        idx_out = len(all_verts)
                        vert_cache[key] = idx_out
                        all_verts.append((
                            world_co.x * scale,
                            world_co.y * scale,
                            world_co.z * scale,
                            norm.x,
                            norm.y,
                            norm.z,
                            u,
                            v_coord
                        ))
                    tri_indices.append(idx_out)

                if len(tri_indices) == 3:
                    all_tris.append((0, m_idx, tri_indices[0], tri_indices[1], tri_indices[2]))

            bm.free()

        with open(filepath, "w", encoding="utf-8") as f:
            if version <= 8200:
                # Authentic Halo CE (HCEEK) format: strictly no comments or headers
                f.write(f"{version}\n0\n")
                f.write(f"{len(nodes)}\n")
                for n in nodes:
                    f.write(f"{n['name']}\n{n['child']}\n{n['sibling']}\n")
                    f.write(f"{n['rot'][0]:.6f}\t{n['rot'][1]:.6f}\t{n['rot'][2]:.6f}\t{n['rot'][3]:.6f}\n")
                    f.write(f"{n['trans'][0]:.6f}\t{n['trans'][1]:.6f}\t{n['trans'][2]:.6f}\n")
                f.write(f"{len(materials)}\n")
                for m_name, tex_path in materials:
                    f.write(f"{m_name}\n{tex_path}\n")
                f.write("0\n")  # markers
                f.write("1\ndefault\n")  # regions
                f.write(f"{len(all_verts)}\n")
                for vx, vy, vz, nx, ny, nz, u, v_coord in all_verts:
                    f.write(f"0\n{vx:.6f}\t{vy:.6f}\t{vz:.6f}\n{nx:.6f}\t{ny:.6f}\t{nz:.6f}\n-1\n0.000000\n{u:.6f}\t{v_coord:.6f}\n0\n")
                f.write(f"{len(all_tris)}\n")
                for reg, m_idx, v0, v1, v2 in all_tris:
                    f.write(f"{reg}\n{m_idx}\n{v0}\t{v1}\t{v2}\n")
            else:
                f.write(f";### VERSION ###\n{version}\n;\t<8197-8213>\n\n")
                f.write(";### CHECKSUM ###\n0\n\n")

                # NODES
                f.write(f";### NODES ###\n{len(nodes)}\n")
                for idx, n in enumerate(nodes):
                    f.write(f";NODE {idx}\n")
                    f.write(f"{n['name']}\n")
                    if version >= 8205:
                        f.write(f"{n['parent']}\n")
                    else:
                        f.write(f"{n['child']}\n{n['sibling']}\n")
                    f.write(f"{n['rot'][0]:.6f}\t{n['rot'][1]:.6f}\t{n['rot'][2]:.6f}\t{n['rot'][3]:.6f}\n")
                    f.write(f"{n['trans'][0]:.6f}\t{n['trans'][1]:.6f}\t{n['trans'][2]:.6f}\n\n")

                # MATERIALS
                f.write(f";### MATERIALS ###\n{len(materials)}\n")
                for idx, (m_name, tex_path) in enumerate(materials):
                    f.write(f";MATERIAL {idx}\n{m_name}\n{tex_path}\n\n")

                # MARKERS
                f.write(";### MARKERS ###\n0\n\n")

                # REGIONS
                f.write(";### REGIONS ###\n1\n;REGION 0\ndefault\n\n")

                # VERTICES
                f.write(f";### VERTICES ###\n{len(all_verts)}\n")
                for idx, (vx, vy, vz, nx, ny, nz, u, v_coord) in enumerate(all_verts):
                    f.write(f"0\n")  # Node index 0
                    f.write(f"{vx:.6f}\t{vy:.6f}\t{vz:.6f}\n")
                    f.write(f"{nx:.6f}\t{ny:.6f}\t{nz:.6f}\n")
                    f.write(f"-1\n") # Node 1 index
                    f.write("0\n")   # Node 1 weight
                    f.write(f"{u:.6f}\t{v_coord:.6f}\n")
                    if version >= 8210:
                        f.write("0\n") # Region index

                # TRIANGLES
                f.write(f"\n;### TRIANGLES ###\n{len(all_tris)}\n")
                for reg, m_idx, v0, v1, v2 in all_tris:
                    if version >= 8200:
                        f.write(f"{reg}\t{m_idx}\t{v0}\t{v1}\t{v2}\n")
                    else:
                        f.write(f"{m_idx}\n{v0}\n{v1}\n{v2}\n")

                # COLLISION EXTENSIONS (v8209+)
                if version >= 8209:
                    f.write("\n;### SPHERES ###\n0\n")
                    f.write("\n;### BOXES ###\n0\n")
                    f.write("\n;### CAPSULES ###\n0\n")
                    f.write("\n;### CONVEX SHAPES ###\n0\n")
                    f.write("\n;### RAGDOLLS ###\n0\n")
                    f.write("\n;### HINGES ###\n0\n")

        return os.path.isfile(filepath) and os.path.getsize(filepath) > 0


def export_jms_file(filepath, context=None, version=8213, game_title="halo3", scale=1.0, use_toolkit=True):
    """Universal JMS export dispatcher."""
    if context is None:
        context = bpy.context

    os.makedirs(os.path.dirname(os.path.abspath(filepath)), exist_ok=True)

    if use_toolkit and hasattr(bpy.ops.export_scene, "jms"):
        try:
            gt = "halo1" if version == 8200 or game_title == "halo1" else ("halo2" if version == 8210 or game_title == "halo2" else "halo3")
            res = bpy.ops.export_scene.jms(filepath=filepath, game_title=gt)
            if res == {'FINISHED'} and os.path.isfile(filepath) and os.path.getsize(filepath) > 0:
                return True
        except Exception as e:
            print(f"[JMS EXPORTER] io_scene_halo exporter reported warning: {e}. Falling back to native engine...")

    return HaloNativeJmsExporter.export_jms(
        filepath=filepath,
        context=context,
        version=version,
        game_title=game_title,
        scale=scale
    )

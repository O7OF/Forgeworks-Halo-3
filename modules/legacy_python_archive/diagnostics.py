# -*- coding: utf-8 -*-
"""
Halo BSP Diagnostics & 1-Click Geometry Auto-Repair Engine
Detects and resolves open edges, ambiguous connections, degenerate triangles,
duplicate coincident faces, short edges, coplanar ridges, and portal leaks.
Created by O7OF | Shoutout to c20 Reclaimers Wiki | Thank you Bungie
"""

import os
import re
import math
import bpy
import bmesh
from mathutils import Vector, Matrix
from bpy_extras.io_utils import ImportHelper

from . import constants
from .shaders import Halo3ShaderManager


CANONICAL_SHADER_REPLACEMENTS = {
    "river sand_wet": "river riverworld_ground_water",
    "dead dead_tree_bark": "dead deadlock_tree_bark",
}


class HaloBspDiagnostics:
    """Diagnostic scanner and geometry auto-repair engine for Halo BSP compliance."""

    @staticmethod
    def scan_mesh(obj):
        """Scans a mesh object for critical Halo BSP compiler errors."""
        results = {
            "vert_count": 0,
            "face_count": 0,
            "vertex_count": 0,
            "open_edges": 0,
            "non_manifold_edges": 0,
            "ambiguous_edges": 0,
            "degenerate_faces": 0,
            "duplicate_faces": 0,
            "overlapping_faces": 0,
            "short_edges": 0,
            "is_watertight": False,
            "invalid_shaders": 0,
            "invalid_shader_names": [],
            "water_uv_issues": 0,
            "portal_leak_risks": 0,
            "out_of_bounds_verts": 0,
        }
        if not obj or obj.type != 'MESH' or not obj.data:
            return results


        # Quick bypass for non-geometry objects
        if not obj.data.vertices or not obj.data.polygons:
            return results

        bm = bmesh.new()
        bm.from_mesh(obj.data)

        results["vert_count"] = len(bm.verts)
        results["vertex_count"] = len(bm.verts)
        results["face_count"] = len(bm.faces)

        # Edges
        min_edge_len_sq = constants.BSP_MIN_EDGE_LENGTH ** 2
        for e in bm.edges:
            lf = len(e.link_faces)
            if lf == 1:
                results["open_edges"] += 1
            elif lf > 2:
                results["ambiguous_edges"] += 1
                results["non_manifold_edges"] += 1
            elif lf == 0:
                results["non_manifold_edges"] += 1

            if (e.verts[0].co - e.verts[1].co).length_squared < min_edge_len_sq:
                results["short_edges"] += 1

        # Degenerate & Duplicate Faces (Optimized integer quantization)
        seen_face_coords = set()
        for f in bm.faces:
            if f.calc_area() < 1e-4:
                results["degenerate_faces"] += 1
            # Quantize coordinates to 5cm grid avoiding float formatting overhead
            v_coords = tuple(sorted((int(v.co.x * 20), int(v.co.y * 20), int(v.co.z * 20)) for v in f.verts))
            if v_coords in seen_face_coords:
                results["duplicate_faces"] += 1
                results["overlapping_faces"] += 1
            else:
                seen_face_coords.add(v_coords)

        # Bounds compliance (-1500 to +1500 world units / 150,000 JMS units)
        max_extent = 150000.0
        for v in bm.verts:
            if abs(v.co.x) > max_extent or abs(v.co.y) > max_extent or abs(v.co.z) > max_extent:
                results["out_of_bounds_verts"] += 1

        results["is_watertight"] = (results["open_edges"] == 0 and results["non_manifold_edges"] == 0)


        # Shader Validation (Fast cached lookup)
        sm = Halo3ShaderManager.get()
        sm.load_shaders()
        for mat in obj.data.materials:
            if not mat:
                results["invalid_shaders"] += 1
                results["invalid_shader_names"].append("<None>")
                continue
            name = mat.name.strip()
            base_name = name.rsplit('.', 1)[0] if ('.' in name and name.rsplit('.', 1)[1].isdigit()) else name
            if base_name.startswith(("+", "%", "$", "@", "!", "#", "*")) or base_name.endswith("'"):
                continue
            if not sm.is_valid_shader(base_name):
                results["invalid_shaders"] += 1
                results["invalid_shader_names"].append(name)

        # Water UV & UV Hard Ceiling Compliance
        is_water = ("water" in obj.name.lower() or obj.name.startswith("!water") or obj.name.startswith("%water")) and not ("terrain" in obj.name.lower() or "ground" in obj.name.lower())
        uv_count = len(obj.data.uv_layers)
        if is_water:
            uv_names = [uv.name for uv in obj.data.uv_layers]
            if uv_count != 4 or uv_names != ["UV_0", "UV_1", "UV_2", "UV_3"]:
                results["water_uv_issues"] += 1
        else:
            if uv_count > 4:
                results["water_uv_issues"] += 1

        # Portal Leak Risks
        is_portal = "+portal" in [m.name for m in obj.data.materials if m] or "portal" in obj.name.lower()
        if is_portal:
            leak = 0
            if results["ambiguous_edges"] > 0:
                leak += 1
            if len(bm.faces) > 0:
                first_f = next(iter(bm.faces))
                ref_norm = first_f.normal
                ref_center = first_f.calc_center_median()
                for v in bm.verts:
                    if abs((v.co - ref_center).dot(ref_norm)) > 0.05:
                        leak += 1
                        break
            results["portal_leak_risks"] = leak

        bm.free()
        return results

    @staticmethod
    def scan_scene_duplicates(objects=None):
        """Scans for identical or coincident duplicate objects across scene."""
        if objects is None:
            objects = [o for o in bpy.context.scene.objects if o.type == 'MESH']

        duplicates = []
        loc_map = {}
        for obj in objects:
            if not obj or obj.type != 'MESH' or not obj.data.vertices:
                continue
            bb_w = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
            min_c = tuple(int(min(pt[i] for pt in bb_w) * 10) for i in range(3))
            max_c = tuple(int(max(pt[i] for pt in bb_w) * 10) for i in range(3))
            key = (min_c, max_c, len(obj.data.vertices))
            if key in loc_map:
                duplicates.append((loc_map[key], obj))
            else:
                loc_map[key] = obj
        return duplicates

    @staticmethod
    def fix_mesh_geometry(
        obj,
        weld_dist=0.08,
        collapse_short=True,
        remove_degen=True,
        remove_duplicates=True,
        fix_ambiguous=True,
        dissolve_coplanar=False,
        coplanar_deg=1.0,
        seal_open_edges=False,
        triangulate=True,
        recalc_normals=True,
        normals_inward=False,
        target_game="HALO_3"
    ):
        """Executes pure geometry repair on target mesh without modifying materials or shaders."""
        report = {
            "ambiguous_fixed": 0,
            "duplicate_removed": 0,
            "overlapping_fixed": 0,
            "holes_sealed": 0,
            "collapsed_edges": 0,
            "degenerate_removed": 0,
            "coplanar_dissolved": 0,
            "normals_recalculated": False,
        }
        if not obj or obj.type != 'MESH' or not obj.data or not obj.data.vertices:
            return report

        bm = bmesh.new()
        bm.from_mesh(obj.data)

        # 1. Fast duplicate face elimination using quantized vertex coordinates (before welding)
        if remove_duplicates and len(bm.faces) > 0:
            bm.verts.index_update()
            seen_face_verts = set()
            dup_f = []
            for f in bm.faces:
                f_key = tuple(sorted((int(round(v.co.x * 50)), int(round(v.co.y * 50)), int(round(v.co.z * 50))) for v in f.verts))
                if f_key in seen_face_verts:
                    dup_f.append(f)
                else:
                    seen_face_verts.add(f_key)
            if dup_f:
                report["duplicate_removed"] += len(dup_f)
                report["overlapping_fixed"] += len(dup_f)
                bmesh.ops.delete(bm, geom=dup_f, context='FACES')
                bm.verts.ensure_lookup_table()
                bm.edges.ensure_lookup_table()
                bm.faces.ensure_lookup_table()

        # 2. Weld close vertices
        if weld_dist > 0.0:
            bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=weld_dist)
            bm.verts.ensure_lookup_table()
            bm.edges.ensure_lookup_table()
            bm.faces.ensure_lookup_table()

        # 3. Post-weld duplicate face check (catches faces merged by vertex welding)
        if remove_duplicates and len(bm.faces) > 0:
            seen_face_verts = set()
            dup_f = []
            for f in bm.faces:
                f_key = tuple(sorted((int(round(v.co.x * 50)), int(round(v.co.y * 50)), int(round(v.co.z * 50))) for v in f.verts))
                if f_key in seen_face_verts:
                    dup_f.append(f)
                else:
                    seen_face_verts.add(f_key)
            if dup_f:
                report["duplicate_removed"] += len(dup_f)
                report["overlapping_fixed"] += len(dup_f)
                bmesh.ops.delete(bm, geom=dup_f, context='FACES')
                bm.verts.ensure_lookup_table()
                bm.edges.ensure_lookup_table()
                bm.faces.ensure_lookup_table()

        # 3. Resolve Ambiguous Connections (> 2 faces sharing an edge)
        if fix_ambiguous and len(bm.edges) > 0:
            amb_fixed = 0
            bad_edges = [e for e in bm.edges if len(e.link_faces) > 2]
            uv_layers = list(bm.loops.layers.uv.values())
            col_layers = list(bm.loops.layers.color.values())

            for e in bad_edges:
                if not e.is_valid:
                    continue
                extra_faces = list(e.link_faces)[2:]
                for f in extra_faces:
                    if not f.is_valid:
                        continue
                    v0, v1 = e.verts[0], e.verts[1]
                    new_v0 = bm.verts.new(v0.co)
                    new_v1 = bm.verts.new(v1.co)
                    old_verts = list(f.verts)
                    new_verts = [new_v0 if v == v0 else (new_v1 if v == v1 else v) for v in old_verts]
                    mat_idx = f.material_index
                    smooth = f.smooth

                    # Cache loop UVs and vertex colors
                    loop_uvs = {lay: [l[lay].uv.copy() for l in f.loops] for lay in uv_layers}
                    loop_cols = {lay: [tuple(l[lay]) for l in f.loops] for lay in col_layers}

                    try:
                        nf = bm.faces.new(new_verts)
                        nf.material_index = mat_idx
                        nf.smooth = smooth
                        for lay, uvs in loop_uvs.items():
                            for i, l in enumerate(nf.loops):
                                if i < len(uvs):
                                    l[lay].uv = uvs[i]
                        for lay, cols in loop_cols.items():
                            for i, l in enumerate(nf.loops):
                                if i < len(cols):
                                    l[lay] = cols[i]
                        bm.faces.remove(f)
                        amb_fixed += 1
                    except Exception:
                        bm.verts.remove(new_v0)
                        bm.verts.remove(new_v1)
            report["ambiguous_fixed"] = amb_fixed

        # 4. Collapse zero-area degenerate triangles safely (preserves airtight manifold skin, no holes)
        if remove_degen and len(bm.faces) > 0:
            degen_f = [f for f in bm.faces if f.is_valid and f.calc_area() < 1e-4]
            if degen_f:
                report["degenerate_removed"] = len(degen_f)
                edges_to_collapse = set()
                for f in degen_f:
                    if f.is_valid and len(f.edges) > 0:
                        shortest_edge = min(f.edges, key=lambda e: e.calc_length())
                        edges_to_collapse.add(shortest_edge)
                if edges_to_collapse:
                    try:
                        bmesh.ops.collapse(bm, edges=list(edges_to_collapse))
                    except Exception:
                        pass
                try:
                    bmesh.ops.dissolve_degenerate(bm, dist=1e-4, edges=bm.edges[:])
                except Exception:
                    pass
                bm.verts.ensure_lookup_table()
                bm.edges.ensure_lookup_table()
                bm.faces.ensure_lookup_table()

        # 5. Collapse short edges safely (no face-deleting)
        if collapse_short and len(bm.edges) > 0:
            min_len_sq = constants.BSP_MIN_EDGE_LENGTH ** 2
            short_e = [e for e in bm.edges if e.is_valid and (e.verts[0].co - e.verts[1].co).length_squared < min_len_sq]
            if short_e:
                report["collapsed_edges"] = len(short_e)
                try:
                    bmesh.ops.collapse(bm, edges=short_e)
                except Exception:
                    pass
                bm.verts.ensure_lookup_table()
                bm.edges.ensure_lookup_table()
                bm.faces.ensure_lookup_table()

        # 6. Dissolve coplanar faces (strictly for architectural meshes, matching materials)
        if dissolve_coplanar and len(bm.edges) > 0:
            rad_limit = math.radians(coplanar_deg)
            coplanar_e = [
                e for e in bm.edges
                if e.is_valid and len(e.link_faces) == 2
                and e.link_faces[0].material_index == e.link_faces[1].material_index
                and e.calc_face_angle() < rad_limit
            ]
            if coplanar_e:
                report["coplanar_dissolved"] = len(coplanar_e)
                try:
                    bmesh.ops.dissolve_edges(bm, edges=coplanar_e, use_verts=True)
                except Exception:
                    pass
                bm.verts.ensure_lookup_table()
                bm.edges.ensure_lookup_table()
                bm.faces.ensure_lookup_table()

        # 7. Seal open boundary holes
        if seal_open_edges and len(bm.edges) > 0:
            bound_e = [e for e in bm.edges if e.is_valid and e.is_boundary]
            if bound_e:
                report["holes_sealed"] = len(bound_e)
                try:
                    bmesh.ops.holes_fill(bm, edges=bound_e)
                except Exception:
                    pass
                bm.verts.ensure_lookup_table()
                bm.edges.ensure_lookup_table()
                bm.faces.ensure_lookup_table()

        # 8. Triangulate
        if triangulate and len(bm.faces) > 0:
            bmesh.ops.triangulate(bm, faces=bm.faces[:])

        # 9. Recalculate normals with inward sealed room compliance
        if recalc_normals and len(bm.faces) > 0:
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
            if normals_inward:
                is_closed = not any(e.is_boundary for e in bm.edges)
                if is_closed:
                    # Enclosed level volume: invert all faces uniformly so normals face inward towards playable space
                    # (Ceiling points DOWN -Z into room, Floor points UP +Z towards player, Walls point INWARD)
                    # Never flip individual faces separately - that causes broken winding and inverted clusters
                    bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
                else:
                    # Open terrain plane: ensure ground faces point UP (+Z) towards sky/player
                    if sum(f.normal.z for f in bm.faces) < 0.0:
                        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
            for f in bm.faces:
                f.smooth = True
            report["normals_recalculated"] = True

        bm.to_mesh(obj.data)
        bm.free()

        # 10. Enforce Blam! UV hard ceiling (max 4 UV layers allowed by engine)
        while len(obj.data.uv_layers) > 4:
            obj.data.uv_layers.remove(obj.data.uv_layers[-1])

        obj.data.update()
        return report

    @staticmethod
    def repair_shaders_and_materials(
        obj,
        default_shader="river riverworld_ground",
        target_game="HALO_3"
    ):
        """Validates and fixes materials and shaders against catalog, preserving +sky ceiling/walls."""
        report = {
            "materials_fixed": 0,
            "invalid_shaders_fixed": 0,
            "water_uvs_fixed": 0,
        }
        if not obj or obj.type != 'MESH' or not obj.data:
            return report

        sm = Halo3ShaderManager.get()
        sm.load_shaders()

        if not obj.data.materials:
            valid_mat = Halo3ShaderManager.get_or_create_material(default_shader)
            obj.data.materials.append(valid_mat)
            report["materials_fixed"] += 1
        else:
            for idx, mat in enumerate(list(obj.data.materials)):
                if not mat:
                    valid_mat = Halo3ShaderManager.get_or_create_material(default_shader)
                    obj.data.materials[idx] = valid_mat
                    report["materials_fixed"] += 1
                    report["invalid_shaders_fixed"] += 1
                    continue
                m_name = mat.name.strip()
                base_name = m_name.rsplit('.', 1)[0] if ('.' in m_name and m_name.rsplit('.', 1)[1].isdigit()) else m_name
                # Protect special system tags (+sky, +portal, %water, etc.)
                if base_name.startswith(("+", "%", "$", "@", "!", "#", "*")) or base_name.endswith("'"):
                    continue
                if base_name in CANONICAL_SHADER_REPLACEMENTS:
                    repl_name = CANONICAL_SHADER_REPLACEMENTS[base_name]
                    valid_mat = Halo3ShaderManager.get_or_create_material(repl_name)
                    obj.data.materials[idx] = valid_mat
                    report["materials_fixed"] += 1
                    report["invalid_shaders_fixed"] += 1
                elif not sm.is_valid_shader(base_name):
                    valid_mat = Halo3ShaderManager.get_or_create_material(default_shader)
                    obj.data.materials[idx] = valid_mat
                    report["materials_fixed"] += 1
                    report["invalid_shaders_fixed"] += 1

        # Check water 4-UV channel specification
        is_water = ("water" in obj.name.lower() or obj.name.startswith("!water") or obj.name.startswith("%water")) and not ("terrain" in obj.name.lower() or "ground" in obj.name.lower())
        if is_water:
            req_uvs = ["UV_0", "UV_1", "UV_2", "UV_3"]
            for req in req_uvs:
                if req not in obj.data.uv_layers:
                    obj.data.uv_layers.new(name=req)
                    report["water_uvs_fixed"] += 1

        while len(obj.data.uv_layers) > 4:
            obj.data.uv_layers.remove(obj.data.uv_layers[-1])

        obj.data.update()
        return report

    @staticmethod
    def cleanup_mesh(
        obj,
        weld_dist=0.08,
        collapse_short=True,
        remove_degen=True,
        remove_duplicates=True,
        fix_ambiguous=True,
        dissolve_coplanar=True,
        coplanar_deg=1.0,
        seal_open_edges=True,
        fix_materials=True,
        recalc_normals=True,
        normals_inward=False,
        target_game="HALO_3"
    ):
        """Unified compatibility wrapper executing geometry repair and optional shader validation."""
        report = HaloBspDiagnostics.fix_mesh_geometry(
            obj,
            weld_dist=weld_dist,
            collapse_short=collapse_short,
            remove_degen=remove_degen,
            remove_duplicates=remove_duplicates,
            fix_ambiguous=fix_ambiguous,
            dissolve_coplanar=dissolve_coplanar,
            coplanar_deg=coplanar_deg,
            seal_open_edges=seal_open_edges,
            triangulate=True,
            recalc_normals=recalc_normals,
            normals_inward=normals_inward,
            target_game=target_game
        )
        if fix_materials:
            sh_rep = HaloBspDiagnostics.repair_shaders_and_materials(obj, target_game=target_game)
            report.update(sh_rep)
        else:
            report.setdefault("materials_fixed", 0)
            report.setdefault("invalid_shaders_fixed", 0)
            report.setdefault("water_uvs_fixed", 0)
        return report

    @staticmethod
    def stitch_and_seal_mesh(obj_or_objs, seal_holes=True, apply_transforms=True, apply_uvs=True, weld_dist=0.08, uv_scale=150.0, flip_v=False):
        """Universal imported mesh stitcher: joins meshes, eliminates contact faces, welds seams, caps holes, and unwraps."""
        fixes = {
            "holes_sealed": 0,
            "transforms_applied": False,
            "uvs_generated": False,
            "doubles_welded": 0,
            "contact_faces_removed": 0,
            "target_object": None,
        }
        if not obj_or_objs:
            return fixes

        # Multi-object handling: join into a single mesh if list provided
        if isinstance(obj_or_objs, (list, tuple, set)):
            mesh_objs = [o for o in obj_or_objs if o and o.type == 'MESH']
            if not mesh_objs:
                return fixes
            if len(mesh_objs) > 1:
                if bpy.context.mode != 'OBJECT':
                    try:
                        bpy.ops.object.mode_set(mode='OBJECT')
                    except Exception:
                        pass
                bpy.ops.object.select_all(action='DESELECT')
                for o in mesh_objs:
                    o.select_set(True)
                bpy.context.view_layer.objects.active = mesh_objs[0]
                bpy.ops.object.join()
                target_obj = bpy.context.view_layer.objects.active
            else:
                target_obj = mesh_objs[0]
        else:
            target_obj = obj_or_objs

        if not target_obj or target_obj.type != 'MESH':
            return fixes

        fixes["target_object"] = target_obj

        if apply_transforms:
            mw = target_obj.matrix_world.copy()
            target_obj.data.transform(mw)
            target_obj.matrix_world = Matrix.Identity(4)
            target_obj.scale = Vector((1.0, 1.0, 1.0))
            fixes["transforms_applied"] = True

        bm = bmesh.new()
        bm.from_mesh(target_obj.data)
        bm.verts.ensure_lookup_table()
        bm.edges.ensure_lookup_table()
        bm.faces.ensure_lookup_table()

        # 1. Eliminate contact faces / duplicates between joined chunks
        seen_face_verts = {}
        faces_to_delete = set()
        for f in bm.faces:
            v_key = tuple(sorted((int(round(v.co.x * 20)), int(round(v.co.y * 20)), int(round(v.co.z * 20))) for v in f.verts))
            if v_key in seen_face_verts:
                prev_f = seen_face_verts[v_key]
                # If normals point in opposite directions, these are facing contact faces between joined solids!
                if f.normal.dot(prev_f.normal) < -0.3:
                    faces_to_delete.add(f)
                    faces_to_delete.add(prev_f)
                else:
                    # Same direction: true duplicate, keep one
                    faces_to_delete.add(f)
            else:
                seen_face_verts[v_key] = f

        if faces_to_delete:
            fixes["contact_faces_removed"] = len(faces_to_delete)
            bmesh.ops.delete(bm, geom=list(faces_to_delete), context='FACES')
            bm.verts.ensure_lookup_table()
            bm.edges.ensure_lookup_table()
            bm.faces.ensure_lookup_table()

        # 2. Weld seam boundary vertices
        if weld_dist > 0.0:
            orig_v_count = len(bm.verts)
            bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=weld_dist)
            fixes["doubles_welded"] = max(0, orig_v_count - len(bm.verts))
            bm.verts.ensure_lookup_table()
            bm.edges.ensure_lookup_table()
            bm.faces.ensure_lookup_table()

        # 3. Resolve Ambiguous Connections (> 2 faces on an edge)
        for e in list(bm.edges):
            if not e.is_valid:
                continue
            if len(e.link_faces) > 2:
                extra_faces = list(e.link_faces)[2:]
                for f in extra_faces:
                    if not f.is_valid:
                        continue
                    v0, v1 = e.verts[0], e.verts[1]
                    new_v0 = bm.verts.new(v0.co)
                    new_v1 = bm.verts.new(v1.co)
                    old_verts = list(f.verts)
                    new_verts = [new_v0 if v == v0 else (new_v1 if v == v1 else v) for v in old_verts]
                    mat_idx = f.material_index
                    smooth = f.smooth
                    try:
                        nf = bm.faces.new(new_verts)
                        nf.material_index = mat_idx
                        nf.smooth = smooth
                        bm.faces.remove(f)
                    except Exception:
                        bm.verts.remove(new_v0)
                        bm.verts.remove(new_v1)
        bm.verts.ensure_lookup_table()
        bm.edges.ensure_lookup_table()
        bm.faces.ensure_lookup_table()

        # 4. Collapse zero-area degenerate triangles safely (preserves airtight manifold skin)
        degen_f = [f for f in bm.faces if f.is_valid and f.calc_area() < 1e-4]
        if degen_f:
            edges_to_collapse = set()
            for f in degen_f:
                if f.is_valid and len(f.edges) > 0:
                    shortest_edge = min(f.edges, key=lambda e: e.calc_length())
                    edges_to_collapse.add(shortest_edge)
            if edges_to_collapse:
                try:
                    bmesh.ops.collapse(bm, edges=list(edges_to_collapse))
                except Exception:
                    pass
            try:
                bmesh.ops.dissolve_degenerate(bm, dist=1e-4, edges=bm.edges[:])
            except Exception:
                pass
            bm.verts.ensure_lookup_table()
            bm.edges.ensure_lookup_table()
            bm.faces.ensure_lookup_table()

        # 5. Seal boundary holes if requested
        if seal_holes:
            bound_e = [e for e in bm.edges if e.is_valid and e.is_boundary]
            if bound_e:
                try:
                    bmesh.ops.holes_fill(bm, edges=bound_e)
                    fixes["holes_sealed"] = len(bound_e)
                except Exception:
                    pass

        # 6. Triangulate with beauty ear-clipping
        bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method='BEAUTY', ngon_method='BEAUTY')
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        for f in bm.faces:
            f.smooth = True

        bm.to_mesh(target_obj.data)
        bm.free()

        # 7. Apply seamless world-space UV mapping with zero cut lines
        if apply_uvs:
            from .terrain import HaloTerrainGenerator
            HaloTerrainGenerator.apply_world_space_uvs(target_obj, uv_scale=uv_scale, flip_v=flip_v)
            fixes["uvs_generated"] = True

        target_obj.data.update()
        return fixes


class HaloPortalFixer:
    """Seals portal geometry, eliminates coincident duplicate portals, and ensures airtight level boundaries."""

    @staticmethod
    def fix_portal_leakage(target_portal=None, target_terrain=None, seal_overhang=80.0):
        """Cleans duplicate portals, makes portal geometry strictly planar, and parents to z_portal."""
        res = {
            "portals_fixed": 0,
            "leaks_sealed": 0,
            "ambiguous_split": 0,
            "overlapping_fixed": 0,
            "planar_corrected": 0,
            "parented": 0
        }

        # Ensure z_portal empty root exists
        z_portal = bpy.data.objects.get("z_portal")
        if not z_portal:
            z_portal = bpy.data.objects.new("z_portal", None)
            bpy.context.scene.collection.objects.link(z_portal)
        root_obj = bpy.data.objects.get("b_levelroot")
        if root_obj and z_portal.parent != root_obj:
            z_portal.parent = root_obj

        # Identify portal objects
        if target_portal:
            portal_objs = [target_portal]
        else:
            portal_objs = [
                o for o in bpy.context.scene.objects
                if o.type == 'MESH' and (
                    "+portal" in [m.name for m in o.data.materials if m] or
                    "portal" in o.name.lower()
                )
            ]

        res["portals_fixed"] = len(portal_objs)

        # 1. Process each portal: make strictly planar, resolve ambiguous connections and duplicates
        props = getattr(bpy.context.scene, "halo3_tool_props", None)
        ceiling_z = getattr(props, "terrain_ceiling_z", 600.0) if props else 600.0
        if target_terrain and target_terrain.type == 'MESH':
            bb_w = [target_terrain.matrix_world @ Vector(c) for c in target_terrain.bound_box]
            ceiling_z = max(pt.z for pt in bb_w) + seal_overhang

        for p in portal_objs:
            if p.parent != z_portal:
                p.parent = z_portal
                res["parented"] += 1

            bm = bmesh.new()
            bm.from_mesh(p.data)
            bm.verts.ensure_lookup_table()
            bm.edges.ensure_lookup_table()
            bm.faces.ensure_lookup_table()

            # Remove duplicate faces
            seen_coords = set()
            dup_f = []
            for f in bm.faces:
                coords = tuple(sorted(tuple(round(c, 3) for c in v.co) for v in f.verts))
                if coords in seen_coords:
                    dup_f.append(f)
                else:
                    seen_coords.add(coords)
            if dup_f:
                bmesh.ops.delete(bm, geom=dup_f, context='FACES')
                res["overlapping_fixed"] += len(dup_f)
                bm.verts.ensure_lookup_table()
                bm.edges.ensure_lookup_table()
                bm.faces.ensure_lookup_table()

            # Split ambiguous edges
            for e in list(bm.edges):
                if not e.is_valid:
                    continue
                if len(e.link_faces) > 2:
                    extra_faces = list(e.link_faces)[2:]
                    for f in extra_faces:
                        if not f.is_valid:
                            continue
                        v0, v1 = e.verts[0], e.verts[1]
                        new_v0 = bm.verts.new(v0.co)
                        new_v1 = bm.verts.new(v1.co)
                        old_verts = list(f.verts)
                        new_verts = [new_v0 if v == v0 else (new_v1 if v == v1 else v) for v in old_verts]
                        mat_idx = f.material_index
                        smooth = f.smooth
                        try:
                            nf = bm.faces.new(new_verts)
                            nf.material_index = mat_idx
                            nf.smooth = smooth
                            bm.faces.remove(f)
                            res["ambiguous_split"] += 1
                        except Exception:
                            bm.verts.remove(new_v0)
                            bm.verts.remove(new_v1)

            # Make strictly planar: project every vertex onto the median face plane
            if len(bm.faces) > 0:
                ref_norm = bm.faces[0].normal.copy()
                ref_center = bm.faces[0].calc_center_median().copy()
                for v in bm.verts:
                    diff = v.co - ref_center
                    v.co -= diff.dot(ref_norm) * ref_norm
                res["planar_corrected"] += 1

            # Ensure bounds seal ceiling if requested
            if target_terrain or target_portal:
                top_verts = sorted(bm.verts, key=lambda v: (p.matrix_world @ v.co).z, reverse=True)
                if top_verts:
                    top_world_z = (p.matrix_world @ top_verts[0].co).z
                    if top_world_z < ceiling_z:
                        delta_z = ceiling_z - top_world_z + 10.0
                        for tv in top_verts[:2]:
                            tv.co.z += delta_z
                        res["leaks_sealed"] += 1

            bm.to_mesh(p.data)
            bm.free()
            p.data.update()

        # 2. Eliminate scene duplicate coincident portals
        seen_bounds = {}
        for p in portal_objs:
            if not p or not p.name in bpy.data.objects or not p.data.vertices:
                continue
            bb_w = [p.matrix_world @ Vector(c) for c in p.bound_box]
            min_c = tuple(round(min(pt[i] for pt in bb_w), 1) for i in range(3))
            max_c = tuple(round(max(pt[i] for pt in bb_w), 1) for i in range(3))
            key = (min_c, max_c)
            if key in seen_bounds:
                bpy.data.objects.remove(p, do_unlink=True)
                res["overlapping_fixed"] += 1
            else:
                seen_bounds[key] = p
                res["leaks_sealed"] += 1

        return res


# =============================================================================
# WRL (VRML 1.0) TOOL ERROR PARSER & VISUALIZER
# =============================================================================

class HaloWrlImporter:
    """Parses Tool .WRL error geometry files and renders brightly highlighted error markers in Blender."""

    @staticmethod
    def infer_error_info(rgb, is_face=False):
        r, g, b = round(rgb[0], 2), round(rgb[1], 2), round(rgb[2], 2)
        if r >= 0.8 and g <= 0.2 and b <= 0.2:
            return ("Red", "Bad Edge / Degenerate Triangle" if is_face else "Open Edge / Short Edge", (1.0, 0.05, 0.05, 1.0))
        elif r <= 0.2 and g >= 0.8 and b <= 0.2:
            return ("Green", "Nearly Coplanar Surface", (0.05, 1.0, 0.05, 1.0))
        elif r >= 0.8 and 0.3 <= g <= 0.7 and b <= 0.2:
            return ("Orange", "Overlapping Surface / Duplicate Triangle", (1.0, 0.5, 0.0, 1.0))
        elif r <= 0.2 and g >= 0.8 and b >= 0.8:
            return ("Cyan", "Surface Clipped Outside BSP", (0.0, 0.9, 1.0, 1.0))
        elif r >= 0.8 and g >= 0.8 and b <= 0.2:
            return ("Yellow", "Portal Does Not Define 2 Closed Spaces", (1.0, 0.95, 0.05, 1.0))
        elif r >= 0.8 and b >= 0.8 and g <= 0.2:
            return ("Magenta", "Portal Outside BSP / Unearthed Edge", (1.0, 0.05, 0.95, 1.0))
        elif r >= 0.8 and 0.2 <= g <= 0.6 and 0.5 <= b <= 0.85:
            return ("Pink", "Possible T-Junction", (1.0, 0.4, 0.7, 1.0))
        elif r <= 0.2 and g <= 0.2 and b >= 0.8:
            return ("Blue", "Degenerate Triangle or Bad UVs", (0.1, 0.3, 1.0, 1.0))
        elif r <= 0.15 and g <= 0.15 and b <= 0.15:
            return ("Black", "Intersecting Fog Planes", (0.1, 0.1, 0.1, 1.0))
        return ("White", "Tool Geometry Warning", (1.0, 1.0, 1.0, 1.0))

    @staticmethod
    def import_wrl(filepath, context=None):
        if context is None:
            context = bpy.context

        if not os.path.isfile(filepath):
            return 0

        with open(filepath, "r", encoding="utf-8", errors="ignore") as f:
            content = f.read()

        # Target Collection
        col_name = "Tool BSP Errors"
        col = bpy.data.collections.get(col_name)
        if not col:
            col = bpy.data.collections.new(col_name)
            context.scene.collection.children.link(col)

        # Parse Coordinate3 points
        pts_match = re.search(r'Coordinate3\s*\{\s*point\s*\[([^\]]+)\]', content)
        if not pts_match:
            pts_match = re.search(r'point\s*\[([^\]]+)\]', content)
        if not pts_match:
            return 0

        raw_pts = pts_match.group(1).replace(',', ' ').split()
        points = []
        for i in range(0, len(raw_pts) - 2, 3):
            try:
                points.append((float(raw_pts[i]), float(raw_pts[i+1]), float(raw_pts[i+2])))
            except ValueError:
                pass

        if not points:
            return 0

        # Parse diffuseColor
        colors = []
        col_match = re.search(r'diffuseColor\s*\[([^\]]+)\]', content)
        if col_match:
            raw_c = col_match.group(1).replace(',', ' ').split()
            for i in range(0, len(raw_c) - 2, 3):
                try:
                    colors.append((float(raw_c[i]), float(raw_c[i+1]), float(raw_c[i+2])))
                except ValueError:
                    pass

        default_color = colors[0] if colors else (1.0, 0.0, 0.0)

        created_count = 0

        # Parse IndexedLineSet
        line_matches = re.finditer(r'IndexedLineSet\s*\{[^}]*coordIndex\s*\[([^\]]+)\]', content)
        for lm in line_matches:
            indices = [int(x) for x in lm.group(1).replace(',', ' ').split() if x != '-1']
            edges = []
            for i in range(0, len(indices) - 1, 2):
                if indices[i] < len(points) and indices[i+1] < len(points):
                    edges.append((indices[i], indices[i+1]))

            if edges:
                color_name, desc, rgba = HaloWrlImporter.infer_error_info(default_color, is_face=False)
                mesh = bpy.data.meshes.new(f"WRL_Lines_{color_name}")
                mesh.from_pydata(points, edges, [])
                mesh.update()

                obj = bpy.data.objects.new(f"Error_{color_name}_{desc}", mesh)
                col.objects.link(obj)

                # Bright glowing debug material
                mat = bpy.data.materials.new(name=f"Mat_Error_{color_name}")
                mat.use_nodes = True
                mat.diffuse_color = rgba
                nodes = mat.node_tree.nodes
                nodes.clear()
                out_n = nodes.new('ShaderNodeOutputMaterial')
                emis = nodes.new('ShaderNodeEmission')
                emis.inputs['Color'].default_value = rgba
                emis.inputs['Strength'].default_value = 3.0
                mat.node_tree.links.new(emis.outputs['Emission'], out_n.inputs['Surface'])
                mesh.materials.append(mat)
                created_count += 1

        # Parse IndexedFaceSet
        face_matches = re.finditer(r'IndexedFaceSet\s*\{[^}]*coordIndex\s*\[([^\]]+)\]', content)
        for fm in face_matches:
            raw_idx = fm.group(1).replace(',', ' ').split()
            faces = []
            cur_face = []
            for tok in raw_idx:
                if tok == '-1':
                    if len(cur_face) >= 3:
                        faces.append(tuple(cur_face))
                    cur_face = []
                else:
                    cur_face.append(int(tok))
            if cur_face and len(cur_face) >= 3:
                faces.append(tuple(cur_face))

            if faces:
                color_name, desc, rgba = HaloWrlImporter.infer_error_info(default_color, is_face=True)
                mesh = bpy.data.meshes.new(f"WRL_Faces_{color_name}")
                mesh.from_pydata(points, [], faces)
                mesh.update()

                obj = bpy.data.objects.new(f"Error_{color_name}_{desc}", mesh)
                col.objects.link(obj)

                mat = bpy.data.materials.new(name=f"Mat_Error_{color_name}_Face")
                mat.use_nodes = True
                mat.diffuse_color = rgba
                nodes = mat.node_tree.nodes
                nodes.clear()
                out_n = nodes.new('ShaderNodeOutputMaterial')
                emis = nodes.new('ShaderNodeEmission')
                emis.inputs['Color'].default_value = rgba
                emis.inputs['Strength'].default_value = 2.0
                mat.node_tree.links.new(emis.outputs['Emission'], out_n.inputs['Surface'])
                mesh.materials.append(mat)
                created_count += 1

        return created_count


# =============================================================================
# BLENDER OPERATORS FOR DIAGNOSTICS & VIEWPORT SELECTION
# =============================================================================

class HALO3_OT_select_bsp_errors(bpy.types.Operator):
    """Enters Edit Mode and selects all non-compliant BSP geometry elements (open edges, degenerate triangles, short edges, bad bounds)"""
    bl_idname = "halo3.select_bsp_errors"
    bl_label = "Select BSP Errors in Viewport"
    bl_description = "Highlights open edges, non-manifold geometry, short edges, and degenerate faces in 3D viewport"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        obj = context.active_object
        if not obj or obj.type != 'MESH':
            self.report({'WARNING'}, "Select a mesh object first")
            return {'CANCELLED'}

        # Ensure Edit Mode
        if context.mode != 'EDIT_MESH':
            bpy.ops.object.mode_set(mode='EDIT')

        bm = bmesh.from_edit_mesh(obj.data)
        bm.verts.ensure_lookup_table()
        bm.edges.ensure_lookup_table()
        bm.faces.ensure_lookup_table()

        # Deselect all
        for v in bm.verts: v.select = False
        for e in bm.edges: e.select = False
        for f in bm.faces: f.select = False

        sel_open = 0
        sel_amb = 0
        sel_short = 0
        sel_degen = 0
        sel_bounds = 0

        min_len_sq = constants.BSP_MIN_EDGE_LENGTH ** 2
        max_extent = 150000.0

        for e in bm.edges:
            lf = len(e.link_faces)
            if lf == 1:
                e.select = True
                e.verts[0].select = True
                e.verts[1].select = True
                sel_open += 1
            elif lf > 2 or lf == 0:
                e.select = True
                e.verts[0].select = True
                e.verts[1].select = True
                sel_amb += 1

            if (e.verts[0].co - e.verts[1].co).length_squared < min_len_sq:
                e.select = True
                sel_short += 1

        for f in bm.faces:
            if f.calc_area() < 1e-4:
                f.select = True
                for v in f.verts:
                    v.select = True
                sel_degen += 1

        for v in bm.verts:
            if abs(v.co.x) > max_extent or abs(v.co.y) > max_extent or abs(v.co.z) > max_extent:
                v.select = True
                sel_bounds += 1

        bmesh.update_edit_mesh(obj.data)

        msg = f"Highlighted: {sel_open} open edges, {sel_amb} non-manifold edges, {sel_short} short edges, {sel_degen} degenerate faces"
        if sel_bounds > 0:
            msg += f", {sel_bounds} out-of-bounds verts"
        self.report({'INFO'}, msg)
        return {'FINISHED'}


class HALO3_OT_import_wrl_errors(bpy.types.Operator, ImportHelper):
    """Imports and visualizes Tool compiler .WRL error geometry in the 3D viewport"""
    bl_idname = "halo3.import_wrl_errors"
    bl_label = "Import Tool .WRL Errors"
    bl_description = "Imports Tool VRML .wrl error files and color-codes compiler geometry issues"
    bl_options = {'REGISTER', 'UNDO'}

    filename_ext = ".wrl"
    filter_glob: bpy.props.StringProperty(default="*.wrl", options={'HIDDEN'})

    def execute(self, context):
        count = HaloWrlImporter.import_wrl(self.filepath, context)
        if count > 0:
            self.report({'INFO'}, f"Imported {count} Tool error objects into 'Tool BSP Errors' collection")
            return {'FINISHED'}
        self.report({'WARNING'}, "No error lines or faces found in WRL file")
        return {'CANCELLED'}


DIAGNOSTIC_OPERATORS = [
    HALO3_OT_select_bsp_errors,
    HALO3_OT_import_wrl_errors,
]


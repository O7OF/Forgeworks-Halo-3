# -*- coding: utf-8 -*-
"""
Halo Structures, Facilities, Portals, Water, and Sky Generator Suite
Contains:
- HaloBunkerGenerator (Fortresses, ramps, parapets, splitter walls)
- HaloHallwayGenerator (UNSC corridors, airlocks, doors)
- HaloPortalGenerator (Planar portals, chokepoint rock arches, z_portal hierarchy)
- HaloSkyGenerator (Sky box, ceiling dome, stars)
- HaloWaterGenerator (Water volume boxes, stream beds, 4 UV channels)
- HaloAssetPrepper (Scene setup for scenery, crates, vehicles, weapons, Spartan scale)
Created by O7OF | Shoutout to c20 Reclaimers Wiki | Thank you Bungie
"""

import os
import math
import bpy
import bmesh
from mathutils import Vector, Matrix, Euler

from . import constants
from .compiler import HaloCompiler
from .shaders import Halo3ShaderManager

HALO_CHIEF_HEIGHT = 70.3


class HaloBunkerGenerator:
    """Generates authentic Blood Gulch fortress bases with ramps, cover parapets, and splitter walls."""

    @staticmethod
    def generate_bunker(
        name="h3_base_bunker",
        radius_out=140.0,
        radius_in=118.0,
        height=85.0,
        door_height=65.0,
        floor_z=4.0,
        num_segments=16,
        ramp_width=35.0,
        cover_height=22.0,
        wall_shader="base hb_concrete_wall",
        trim_shader="epilogue 130_hall_girder",
        loc_x=0.0,
        loc_y=0.0,
        loc_z=0.0,
        rot_z=0.0,
        ramp_style="INTEGRATED",
        include_splitter_wall=True,
        include_splitter=None
    ):
        if include_splitter is not None:
            include_splitter_wall = include_splitter

        mesh = bpy.data.meshes.new(name)
        bm = bmesh.new()

        hw = radius_out * 0.85
        hl = radius_out * 1.05
        hw_in = radius_in * 0.82
        hl_in = radius_in * 0.98
        h = height
        dh = door_height
        dw = 30.0

        ext_pts = [
            (hw, hl), (dw, hl), (-dw, hl), (-hw, hl),
            (-hw, -hl), (-dw, -hl), (dw, -hl), (hw, -hl)
        ]
        int_pts = [
            (hw_in, hl_in), (dw, hl_in), (-dw, hl_in), (-hw_in, hl_in),
            (-hw_in, -hl_in), (-dw, -hl_in), (dw, -hl_in), (hw_in, -hl_in)
        ]

        v_ext_0 = [bm.verts.new(Vector((p[0], p[1], 0.0))) for p in ext_pts]
        v_ext_d = [bm.verts.new(Vector((p[0], p[1], dh))) for p in ext_pts]
        v_ext_h = [bm.verts.new(Vector((p[0], p[1], h))) for p in ext_pts]

        v_int_0 = [bm.verts.new(Vector((p[0], p[1], 0.0))) for p in int_pts]
        v_int_d = [bm.verts.new(Vector((p[0], p[1], dh))) for p in int_pts]
        v_int_h = [bm.verts.new(Vector((p[0], p[1], h - 10.0))) for p in int_pts]

        # Base bottom (facing down)
        bm.faces.new(v_ext_0[::-1])
        # Inner ceiling (facing down)
        bm.faces.new(v_int_h[::-1])
        # Inner floor (facing up)
        bm.faces.new(v_int_0)

        door_segs = [1, 5]

        for i in range(8):
            nxt = (i + 1) % 8
            bm.faces.new((v_ext_d[i], v_ext_d[nxt], v_ext_h[nxt], v_ext_h[i]))
            bm.faces.new((v_int_d[nxt], v_int_d[i], v_int_h[i], v_int_h[nxt]))
            
            if i not in door_segs:
                bm.faces.new((v_ext_0[i], v_ext_0[nxt], v_ext_d[nxt], v_ext_d[i]))
                bm.faces.new((v_int_0[nxt], v_int_0[i], v_int_d[i], v_int_d[nxt]))
            else:
                bm.faces.new((v_ext_d[i], v_int_d[i], v_int_d[nxt], v_ext_d[nxt]))
                bm.faces.new((v_ext_0[i], v_ext_d[i], v_int_d[i], v_int_0[i]))
                bm.faces.new((v_ext_0[nxt], v_int_0[nxt], v_int_d[nxt], v_ext_d[nxt]))
                bm.faces.new((v_ext_0[i], v_int_0[i], v_int_0[nxt], v_ext_0[nxt]))

        # Parapet
        if cover_height > 0.0:
            v_par_out = [bm.verts.new(Vector((p[0], p[1], h + cover_height))) for p in ext_pts]
            v_par_in  = [bm.verts.new(Vector((p[0] * 0.88, p[1] * 0.88, h + cover_height))) for p in ext_pts]
            v_par_bot = [bm.verts.new(Vector((p[0] * 0.88, p[1] * 0.88, h))) for p in ext_pts]
            
            for i in range(8):
                nxt = (i + 1) % 8
                bm.faces.new((v_ext_h[i], v_ext_h[nxt], v_par_out[nxt], v_par_out[i]))
                bm.faces.new((v_par_out[i], v_par_out[nxt], v_par_in[nxt], v_par_in[i]))
                bm.faces.new((v_par_in[nxt], v_par_in[i], v_par_bot[i], v_par_bot[nxt]))
            bm.faces.new(v_par_bot)
        else:
            bm.faces.new(v_ext_h)

        # Integrated side ramps (closed solid wedges)
        if ramp_style == "INTEGRATED":
            for side, sx in [('LEFT', -hw), ('RIGHT', hw)]:
                dir_x = -1.0 if side == 'LEFT' else 1.0
                x_in = sx + dir_x * 0.1
                x_out = sx + dir_x * (ramp_width + 0.1)
                b0 = bm.verts.new(Vector((x_out, -hl, 0.0)))
                b1 = bm.verts.new(Vector((x_in, -hl, 0.0)))
                b2 = bm.verts.new(Vector((x_in, hl, 0.0)))
                b3 = bm.verts.new(Vector((x_out, hl, 0.0)))
                
                t2 = bm.verts.new(Vector((x_in, hl, h)))
                t3 = bm.verts.new(Vector((x_out, hl, h)))
                
                bm.faces.new((b0, b3, b2, b1) if side == 'LEFT' else (b0, b1, b2, b3))
                bm.faces.new((b0, b1, t2, t3) if side == 'LEFT' else (b0, t3, t2, b1))
                bm.faces.new((b3, t3, t2, b2) if side == 'LEFT' else (b2, t2, t3, b3))
                bm.faces.new((b0, t3, b3) if side == 'LEFT' else (b0, b3, t3))
                bm.faces.new((b1, b2, t2) if side == 'LEFT' else (b1, t2, b2))

        # Splitter wall
        if include_splitter_wall:
            sw_th = 6.0
            sw_l = hl_in * 0.5
            b_c = bmesh.ops.create_cube(bm, size=1.0)
            bmesh.ops.scale(bm, vec=Vector((sw_th * 2.0, sw_l * 2.0, dh - 4.0)), verts=b_c['verts'])
            bmesh.ops.translate(bm, vec=Vector((0.0, 0.0, (dh - 4.0) * 0.5 + 2.0)), verts=b_c['verts'])

        # Triangulate and finalize
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bm.normal_update()

        # World UV
        uv_l = bm.loops.layers.uv.new("UV_0")
        for f in bm.faces:
            for l in f.loops:
                l[uv_l].uv = Vector((l.vert.co.x / 120.0, l.vert.co.z / 120.0))

        bm.to_mesh(mesh)
        bm.free()

        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        obj.location = Vector((loc_x, loc_y, loc_z))
        obj.rotation_euler = Euler((0.0, 0.0, rot_z), 'XYZ')

        # Shaders
        mat_w = Halo3ShaderManager.get_or_create_material(wall_shader)
        obj.data.materials.append(mat_w)

        # Parent under b_levelroot
        HaloCompiler.parent_to_level_root(obj)

        return obj


class HaloHallwayGenerator:
    """Generates modular UNSC corridors, corridors with ribs, doors, and airlocks."""

    CORRIDOR_PROFILE = [
        (-58.0, 0.0),    # 0 floor L
        (58.0, 0.0),     # 1 floor R
        (70.0, 8.0),     # 2 kickplate R
        (70.0, 75.0),    # 3 wall R
        (42.0, 100.0),   # 4 chamfer R
        (26.0, 100.0),   # 5 light ledge R
        (26.0, 110.0),   # 6 light bay R
        (-26.0, 110.0),  # 7 light bay L
        (-26.0, 100.0),  # 8 light ledge L
        (-42.0, 100.0),  # 9 chamfer L
        (-70.0, 75.0),   # 10 wall L
        (-70.0, 8.0),    # 11 kickplate L
    ]

    @classmethod
    def generate_hallway(
        cls,
        layout_mode="STRAIGHT",
        width=140.0,
        height=110.0,
        length=240.0,
        turns=2,
        intersections=1,
        include_doorways=True,
        include_room=True,
        room_w=260.0,
        room_l=220.0,
        room_h=130.0,
        wall_shader="base hb_concrete_wall",
        floor_shader="base base_floor",
        name="unsc_corridor"
    ):
        bm = bmesh.new()
        hw = width * 0.5
        hrw = room_w * 0.5
        hrl = room_l * 0.5

        if layout_mode == 'STRAIGHT':
            quads = [[(-hw, 0.0), (hw, 0.0), (hw, length), (-hw, length)]]
            heights = [height]
        elif layout_mode == 'ELBOW_TURN':
            quads = [
                [(-hw, 0.0), (hw, 0.0), (hw, length - width), (-hw, length - width)],
                [(-hw, length - width), (hw, length - width), (hw, length), (-hw, length)],
                [(hw, length - width), (length, length - width), (length, length), (hw, length)]
            ]
            heights = [height, height, height]
        elif layout_mode == 'CROSS_INTERSECTION':
            quads = [
                [(-hw, -hw), (hw, -hw), (hw, hw), (-hw, hw)],
                [(-hw, hw), (hw, hw), (hw, length), (-hw, length)],
                [(-hw, -length), (hw, -length), (hw, -hw), (-hw, -hw)],
                [(hw, -hw), (length, -hw), (length, hw), (hw, hw)],
                [(-length, -hw), (-hw, -hw), (-hw, hw), (-length, hw)]
            ]
            heights = [height] * 5
        elif layout_mode == 'UNSC_ROOM':
            quads = [[(-hrw, -hrl), (hrw, -hrl), (hrw, hrl), (-hrw, hrl)]]
            heights = [room_h]
        else: # LAYOUT_CHAIN
            quads = [
                [(-hw, 0.0), (hw, 0.0), (hw, length), (-hw, length)],
                [(-hrw, length), (hrw, length), (hrw, length + room_l), (-hrw, length + room_l)]
            ]
            heights = [height, room_h]

        for q, ch in zip(quads, heights):
            vf = [bm.verts.new(Vector((p[0], p[1], 0.0))) for p in q]
            vc = [bm.verts.new(Vector((p[0], p[1], ch))) for p in q]
            
            ff = bm.faces.new(vf)
            ff.material_index = 1
            
            fc = bm.faces.new(vc[::-1])
            fc.material_index = 0

        bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.08)

        floor_boundary_edges = [e for e in bm.edges if sum(1 for f in e.link_faces if f.material_index == 1) == 1]
        for e in floor_boundary_edges:
            fl_face = [f for f in e.link_faces if f.material_index == 1][0]
            loop = [l_item for l_item in fl_face.loops if l_item.edge == e][0]
            v1 = loop.vert
            v2 = loop.link_loop_next.vert
            
            top_candidates1 = [v for v in bm.verts if abs(v.co.x - v1.co.x) < 0.1 and abs(v.co.y - v1.co.y) < 0.1 and v.co.z > 1.0]
            top_candidates2 = [v for v in bm.verts if abs(v.co.x - v2.co.x) < 0.1 and abs(v.co.y - v2.co.y) < 0.1 and v.co.z > 1.0]
            if top_candidates1 and top_candidates2:
                vt1 = top_candidates1[0]
                vt2 = top_candidates2[0]
                fw = bm.faces.new((v1, v2, vt2, vt1))
                fw.material_index = 0

        bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.08)
        bound_e = [e for e in bm.edges if e.is_boundary]
        if bound_e:
            try:
                bmesh.ops.holes_fill(bm, edges=bound_e)
            except Exception:
                pass

        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bm.normal_update()

        mesh = bpy.data.meshes.new(name)
        bm.to_mesh(mesh)
        bm.free()
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        HaloCompiler.parent_to_level_root(obj)

        mat_wall = Halo3ShaderManager.get_or_create_material(wall_shader)
        mat_floor = Halo3ShaderManager.get_or_create_material(floor_shader)
        obj.data.materials.append(mat_wall)
        obj.data.materials.append(mat_floor)
        return obj

    generate_straight_corridor = generate_hallway


class HaloPortalGenerator:
    """Generates airtight canyon chokepoint rock arches and planar portals with z_portal parent."""

    @staticmethod
    def generate_portal(
        y_pos=0.0,
        width=400.0,
        height=200.0,
        z_bottom=0.0,
        z_top=None,
        portal_type="PLANAR_QUAD",
        portal_shader="+portal",
        target_terrain=None,
        name=None
    ):
        props = getattr(bpy.context.scene, "halo3_tool_props", None)
        ceiling_z = getattr(props, "terrain_ceiling_z", 600.0) if props else 600.0
        if target_terrain and target_terrain.type == 'MESH':
            bb_w = [target_terrain.matrix_world @ Vector(c) for c in target_terrain.bound_box]
            ceiling_z = max(pt.z for pt in bb_w)

        if z_top is not None:
            top_z = float(z_top)
            bot_z = float(z_bottom)
        elif target_terrain is not None:
            top_z = ceiling_z + 10.0
            bot_z = z_bottom
        else:
            top_z = z_bottom + height
            bot_z = z_bottom

        p_name = name or f"portal_{int(y_pos)}"
        mesh = bpy.data.meshes.new(p_name)
        bm = bmesh.new()

        hw = width * 0.5
        v0 = bm.verts.new(Vector((-hw, y_pos, bot_z)))
        v1 = bm.verts.new(Vector((hw, y_pos, bot_z)))
        v2 = bm.verts.new(Vector((hw, y_pos, top_z)))
        v3 = bm.verts.new(Vector((-hw, y_pos, top_z)))
        bm.faces.new([v0, v1, v2, v3])

        bm.to_mesh(mesh)
        bm.free()

        obj = bpy.data.objects.new(p_name, mesh)
        bpy.context.scene.collection.objects.link(obj)

        # Ensure z_portal empty exists
        z_portal = bpy.data.objects.get("z_portal")
        if not z_portal:
            z_portal = bpy.data.objects.new("z_portal", None)
            bpy.context.scene.collection.objects.link(z_portal)
        root_obj = bpy.data.objects.get("b_levelroot")
        if root_obj and z_portal.parent != root_obj:
            z_portal.parent = root_obj

        obj.parent = z_portal

        mat = Halo3ShaderManager.get_or_create_material(portal_shader)
        obj.data.materials.append(mat)
        return obj

    @staticmethod
    def generate_airtight_chokepoint_cluster(
        y_pos=0.0,
        arch_width=240.0,
        arch_height=140.0,
        wall_thickness=60.0,
        canyon_span=1600.0,
        portal_shader="+portal",
        rock_shader="dead deadlock_cliff_rock"
    ):
        """Creates an airtight rock arch and planar portal centered at y_pos."""
        mesh_arch = bpy.data.meshes.new("h3_chokepoint_arch")
        bm_arch = bmesh.new()

        span_half = canyon_span * 0.5
        th_half = wall_thickness * 0.5
        aw_half = arch_width * 0.5
        ah = arch_height

        # Left wall
        v_l0 = bm_arch.verts.new(Vector((-span_half, y_pos - th_half, 0.0)))
        v_l1 = bm_arch.verts.new(Vector((-aw_half, y_pos - th_half, 0.0)))
        v_l2 = bm_arch.verts.new(Vector((-aw_half, y_pos + th_half, 0.0)))
        v_l3 = bm_arch.verts.new(Vector((-span_half, y_pos + th_half, 0.0)))

        v_l4 = bm_arch.verts.new(Vector((-span_half, y_pos - th_half, ah * 1.5)))
        v_l5 = bm_arch.verts.new(Vector((-aw_half, y_pos - th_half, ah)))
        v_l6 = bm_arch.verts.new(Vector((-aw_half, y_pos + th_half, ah)))
        v_l7 = bm_arch.verts.new(Vector((-span_half, y_pos + th_half, ah * 1.5)))

        bm_arch.faces.new([v_l0, v_l1, v_l5, v_l4])
        bm_arch.faces.new([v_l1, v_l2, v_l6, v_l5])
        bm_arch.faces.new([v_l2, v_l3, v_l7, v_l6])
        bm_arch.faces.new([v_l3, v_l0, v_l4, v_l7])
        bm_arch.faces.new([v_l4, v_l5, v_l6, v_l7])
        bm_arch.faces.new([v_l0, v_l3, v_l2, v_l1])

        # Right wall
        v_r0 = bm_arch.verts.new(Vector((aw_half, y_pos - th_half, 0.0)))
        v_r1 = bm_arch.verts.new(Vector((span_half, y_pos - th_half, 0.0)))
        v_r2 = bm_arch.verts.new(Vector((span_half, y_pos + th_half, 0.0)))
        v_r3 = bm_arch.verts.new(Vector((aw_half, y_pos + th_half, 0.0)))

        v_r4 = bm_arch.verts.new(Vector((aw_half, y_pos - th_half, ah)))
        v_r5 = bm_arch.verts.new(Vector((span_half, y_pos - th_half, ah * 1.5)))
        v_r6 = bm_arch.verts.new(Vector((span_half, y_pos + th_half, ah * 1.5)))
        v_r7 = bm_arch.verts.new(Vector((aw_half, y_pos + th_half, ah)))

        bm_arch.faces.new([v_r0, v_r1, v_r5, v_r4])
        bm_arch.faces.new([v_r1, v_r2, v_r6, v_r5])
        bm_arch.faces.new([v_r2, v_r3, v_r7, v_r6])
        bm_arch.faces.new([v_r3, v_r0, v_r4, v_r7])
        bm_arch.faces.new([v_r4, v_r5, v_r6, v_r7])
        bm_arch.faces.new([v_r0, v_r3, v_r2, v_r1])

        # Top spanning arch keystone
        bm_arch.faces.new([v_l5, v_r4, v_r7, v_l6])

        bmesh.ops.triangulate(bm_arch, faces=bm_arch.faces[:])
        bmesh.ops.recalc_face_normals(bm_arch, faces=bm_arch.faces[:])

        bm_arch.to_mesh(mesh_arch)
        bm_arch.free()

        arch_obj = bpy.data.objects.new("h3_chokepoint_arch", mesh_arch)
        bpy.context.scene.collection.objects.link(arch_obj)
        mat_rock = Halo3ShaderManager.get_or_create_material(rock_shader)
        arch_obj.data.materials.append(mat_rock)

        # Planar portal sealing the opening
        portal_obj = HaloPortalGenerator.generate_portal(
            y_pos=y_pos,
            width=arch_width * 0.98,
            height=ah,
            portal_shader=portal_shader,
            name="h3_chokepoint_portal"
        )
        return arch_obj, portal_obj

    @staticmethod
    def make_selected_faces_planar_portal(mesh_obj):
        """Flattens selected faces (or all faces if none selected) to exact coplanarity and assigns +portal in Edit or Object mode."""
        if not mesh_obj or mesh_obj.type != 'MESH':
            return False

        is_edit_mode = (mesh_obj.mode == 'EDIT')
        if is_edit_mode:
            bm = bmesh.from_edit_mesh(mesh_obj.data)
            sel_faces = [f for f in bm.faces if f.select]
            if not sel_faces:
                sel_faces = list(bm.faces)
            if not sel_faces:
                return False

            center = sum((f.calc_center_median() for f in sel_faces), Vector((0, 0, 0))) / len(sel_faces)
            avg_normal = sum((f.normal for f in sel_faces), Vector((0, 0, 0)))
            if avg_normal.length < 1e-4:
                avg_normal = Vector((0.0, 1.0, 0.0))
            else:
                avg_normal.normalize()

            verts = set(v for f in sel_faces for v in f.verts)
            for v in verts:
                dist = (v.co - center).dot(avg_normal)
                v.co -= avg_normal * dist

            bmesh.update_edit_mesh(mesh_obj.data)
        else:
            bm = bmesh.new()
            bm.from_mesh(mesh_obj.data)
            sel_faces = [f for f in bm.faces if f.select]
            if not sel_faces:
                sel_faces = list(bm.faces)
            if not sel_faces:
                bm.free()
                return False

            center = sum((f.calc_center_median() for f in sel_faces), Vector((0, 0, 0))) / len(sel_faces)
            avg_normal = sum((f.normal for f in sel_faces), Vector((0, 0, 0)))
            if avg_normal.length < 1e-4:
                avg_normal = Vector((0.0, 1.0, 0.0))
            else:
                avg_normal.normalize()

            verts = set(v for f in sel_faces for v in f.verts)
            for v in verts:
                dist = (v.co - center).dot(avg_normal)
                v.co -= avg_normal * dist

            bm.to_mesh(mesh_obj.data)
            bm.free()
            mesh_obj.data.update()

        mat_portal = Halo3ShaderManager.get_or_create_material("+portal")
        if mat_portal.name not in [m.name for m in mesh_obj.data.materials if m]:
            mesh_obj.data.materials.append(mat_portal)
        p_idx = mesh_obj.data.materials.find(mat_portal.name)
        for poly in mesh_obj.data.polygons:
            poly.material_index = p_idx

        z_portal = bpy.data.objects.get("z_portal")
        if not z_portal:
            z_portal = bpy.data.objects.new("z_portal", None)
            bpy.context.scene.collection.objects.link(z_portal)

        if mesh_obj.parent != z_portal:
            mesh_obj.parent = z_portal
            mesh_obj.matrix_parent_inverse = z_portal.matrix_world.inverted()

        return True

    # Method alias for operator & external script compatibility
    make_selected_face_planar = make_selected_faces_planar_portal


class HaloSkyGenerator:
    """Generates celestial skybox domes, +sky volumes, and imports authentic Halo skybox models & atmospheres."""

    PRESETS = {
        "BLOOD_GULCH": {
            "zenith": (0.20, 0.45, 0.85, 1.0),
            "horizon": (0.65, 0.78, 0.92, 1.0),
            "ground": (0.35, 0.32, 0.28, 1.0),
            "sun_color": (1.0, 0.95, 0.85, 1.0),
            "sun_strength": 4.5,
            "has_ring": True,
            "description": "Blood Gulch Azure Blue Sky with Iconic Overhead Halo Ring Arch"
        },
        "VALHALLA": {
            "zenith": (0.12, 0.32, 0.65, 1.0),
            "horizon": (0.55, 0.68, 0.82, 1.0),
            "ground": (0.28, 0.30, 0.25, 1.0),
            "sun_color": (0.95, 0.92, 0.80, 1.0),
            "sun_strength": 3.8,
            "has_ring": True,
            "description": "Valhalla Nordic Alpine Sky with Distant Ring Peak"
        },
        "SANDTRAP": {
            "zenith": (0.30, 0.48, 0.75, 1.0),
            "horizon": (0.85, 0.75, 0.58, 1.0),
            "ground": (0.68, 0.58, 0.42, 1.0),
            "sun_color": (1.0, 0.98, 0.90, 1.0),
            "sun_strength": 5.5,
            "has_ring": False,
            "description": "Sandtrap Golden Desert Haze with Intense Solar Glare"
        },
        "FORGE_WORLD": {
            "zenith": (0.15, 0.40, 0.78, 1.0),
            "horizon": (0.62, 0.75, 0.88, 1.0),
            "ground": (0.25, 0.28, 0.22, 1.0),
            "sun_color": (1.0, 0.96, 0.88, 1.0),
            "sun_strength": 4.0,
            "has_ring": True,
            "description": "Reach Forge World Alpine Mountain Sky with Atmospheric Scattering"
        },
        "LONG_NIGHT_OF_SOLACE": {
            "zenith": (0.01, 0.02, 0.05, 1.0),
            "horizon": (0.05, 0.12, 0.25, 1.0),
            "ground": (0.02, 0.05, 0.10, 1.0),
            "sun_color": (0.8, 0.85, 1.0, 1.0),
            "sun_strength": 2.5,
            "has_ring": False,
            "description": "High Orbit Deep Space Skybox with Starfield & Reach Planetary Glow"
        },
        "HIGH_GROUND": {
            "zenith": (0.22, 0.42, 0.72, 1.0),
            "horizon": (0.72, 0.80, 0.88, 1.0),
            "ground": (0.32, 0.30, 0.26, 1.0),
            "sun_color": (1.0, 0.94, 0.82, 1.0),
            "sun_strength": 4.2,
            "has_ring": True,
            "description": "High Ground Coastal Dawn Sky with Soft Morning Sun"
        },
        "NEW_ALEXANDRIA": {
            "zenith": (0.08, 0.14, 0.35, 1.0),
            "horizon": (0.85, 0.42, 0.20, 1.0),
            "ground": (0.15, 0.12, 0.14, 1.0),
            "sun_color": (1.0, 0.60, 0.30, 1.0),
            "sun_strength": 3.5,
            "has_ring": False,
            "description": "New Alexandria Dusk Metropolis Sunset with Burning Horizon"
        },
        "HALO_CELESTIAL": {
            "zenith": (0.10, 0.30, 0.70, 1.0),
            "horizon": (0.50, 0.65, 0.85, 1.0),
            "ground": (0.20, 0.25, 0.20, 1.0),
            "sun_color": (1.0, 1.0, 0.95, 1.0),
            "sun_strength": 5.0,
            "has_ring": True,
            "description": "Dramatic Interior Curve of the Sacred Ring Installation"
        }
    }

    @staticmethod
    def generate_sky(sky_shader="+sky", width=2000.0, length=4000.0, height=1000.0,
                      sky_type="DOME", radius=2500.0, seal_base=True, n_rings=12, n_segs=24,
                      preset="BLOOD_GULCH"):
        mesh = bpy.data.meshes.new("h3_sky_dome")
        bm = bmesh.new()

        if sky_type == "DOME":
            grid = []
            for i in range(n_rings):
                theta = (i / n_rings) * (math.pi * 0.5)
                r = radius * math.cos(theta)
                z = height * math.sin(theta)
                ring = []
                for j in range(n_segs):
                    phi = (j / n_segs) * (math.pi * 2.0)
                    x = r * math.cos(phi)
                    y = r * math.sin(phi)
                    v = bm.verts.new(Vector((x, y, z)))
                    ring.append(v)
                grid.append(ring)

            v_zenith = bm.verts.new(Vector((0.0, 0.0, height)))

            for i in range(n_rings - 1):
                for j in range(n_segs):
                    j_next = (j + 1) % n_segs
                    v1 = grid[i][j]
                    v2 = grid[i][j_next]
                    v3 = grid[i + 1][j_next]
                    v4 = grid[i + 1][j]
                    bm.faces.new((v1, v4, v3, v2))

            for j in range(n_segs):
                j_next = (j + 1) % n_segs
                v1 = grid[n_rings - 1][j]
                v2 = grid[n_rings - 1][j_next]
                bm.faces.new((v1, v_zenith, v2))

            if seal_base:
                v_base_center = bm.verts.new(Vector((0.0, 0.0, 0.0)))
                for j in range(n_segs):
                    j_next = (j + 1) % n_segs
                    v1 = grid[0][j]
                    v2 = grid[0][j_next]
                    bm.faces.new((v1, v2, v_base_center))
        else:
            # Inverted box facing inward
            bmesh.ops.create_cube(bm, size=1.0)
            bmesh.ops.scale(bm, vec=Vector((width, length, height)), verts=bm.verts[:])
            bmesh.ops.translate(bm, vec=Vector((0.0, 0.0, height * 0.5)), verts=bm.verts[:])
            for f in bm.faces:
                f.normal_flip()

        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bm.normal_update()

        for f in bm.faces:
            f.material_index = 0

        # Create UV_0 mapping fitted 1:1 into the [0, 1] UV box
        uv_layer0 = bm.loops.layers.uv.new("UV_0")
        min_x = min(v.co.x for v in bm.verts) if bm.verts else 0.0
        max_x = max(v.co.x for v in bm.verts) if bm.verts else 1.0
        min_y = min(v.co.y for v in bm.verts) if bm.verts else 0.0
        max_y = max(v.co.y for v in bm.verts) if bm.verts else 1.0
        inv_w = 1.0 / max(1.0, max_x - min_x)
        inv_h = 1.0 / max(1.0, max_y - min_y)

        for f in bm.faces:
            for loop in f.loops:
                p = loop.vert.co
                loop[uv_layer0].uv = Vector(((p.x - min_x) * inv_w, (p.y - min_y) * inv_h))

        bm.to_mesh(mesh)
        bm.free()

        obj = bpy.data.objects.new("h3_sky_dome", mesh)
        bpy.context.scene.collection.objects.link(obj)

        # Setup authentic preset material with node tree
        mat = Halo3ShaderManager.get_or_create_material(sky_shader, setup_nodes=False)
        mat.use_nodes = True
        nt = mat.node_tree
        nodes = nt.nodes
        links = nt.links
        nodes.clear()

        preset_data = HaloSkyGenerator.PRESETS.get(preset, HaloSkyGenerator.PRESETS["BLOOD_GULCH"])
        zenith = preset_data["zenith"]
        horizon = preset_data["horizon"]
        sun_color = preset_data["sun_color"]
        sun_str = preset_data["sun_strength"]

        out_node = nodes.new('ShaderNodeOutputMaterial')
        out_node.location = (450, 0)

        bsdf = nodes.new('ShaderNodeBsdfPrincipled')
        bsdf.location = (100, 0)
        links.new(bsdf.outputs['BSDF'], out_node.inputs['Surface'])

        tex_coord = nodes.new('ShaderNodeTexCoord')
        tex_coord.location = (-650, 0)

        mapping = nodes.new('ShaderNodeMapping')
        mapping.location = (-450, 0)
        links.new(tex_coord.outputs['Generated'], mapping.inputs['Vector'])

        # ColorRamp for atmospheric gradient
        ramp = nodes.new('ShaderNodeValToRGB')
        ramp.location = (-200, 0)
        ramp.color_ramp.elements[0].position = 0.05
        ramp.color_ramp.elements[0].color = horizon
        ramp.color_ramp.elements[1].position = 0.85
        ramp.color_ramp.elements[1].color = zenith
        links.new(mapping.outputs['Vector'], ramp.inputs['Fac'])

        if 'Base Color' in bsdf.inputs:
            links.new(ramp.outputs['Color'], bsdf.inputs['Base Color'])
        if 'Emission Color' in bsdf.inputs:
            links.new(ramp.outputs['Color'], bsdf.inputs['Emission Color'])
        if 'Emission Strength' in bsdf.inputs:
            bsdf.inputs['Emission Strength'].default_value = sun_str * 0.4
        bsdf.inputs['Roughness'].default_value = 1.0

        mat.diffuse_color = zenith
        obj.data.materials.append(mat)

        # Update Blender World background color to match scenario
        world = bpy.context.scene.world
        if world:
            world.use_nodes = True
            w_nodes = world.node_tree.nodes
            bg_node = w_nodes.get("Background")
            if bg_node:
                bg_node.inputs['Color'].default_value = horizon
                bg_node.inputs['Strength'].default_value = 1.2

        HaloCompiler.parent_to_level_root(obj)
        return obj

    @staticmethod
    def import_skybox(filepath=None, context=None):
        """Imports a Halo skybox model (.blend, .fbx, .obj, .jms, .ass) or panoramic HDR/texture into scene."""
        if not filepath or not os.path.isfile(filepath):
            return None, "File does not exist"

        ext = os.path.splitext(filepath)[1].lower()
        imported_objs = []

        if ext == ".blend":
            with bpy.data.libraries.load(filepath, link=False) as (data_from, data_to):
                data_to.objects = [name for name in data_from.objects if "sky" in name.lower() or name.startswith("+sky")]
                if not data_to.objects:
                    data_to.objects = data_from.objects[:1]
            for obj in data_to.objects:
                if obj:
                    bpy.context.scene.collection.objects.link(obj)
                    imported_objs.append(obj)

        elif ext == ".obj":
            if hasattr(bpy.ops.wm, "obj_import"):
                bpy.ops.wm.obj_import(filepath=filepath)
            elif hasattr(bpy.ops.import_scene, "obj"):
                bpy.ops.import_scene.obj(filepath=filepath)
            imported_objs = [o for o in bpy.context.selected_objects]

        elif ext == ".fbx":
            if hasattr(bpy.ops.import_scene, "fbx"):
                bpy.ops.import_scene.fbx(filepath=filepath)
            imported_objs = [o for o in bpy.context.selected_objects]

        elif ext in (".hdr", ".exr", ".png", ".jpg", ".tif", ".dds"):
            # Panoramic image skybox: apply to world environment
            world = bpy.context.scene.world
            if not world:
                world = bpy.data.worlds.new("Halo_Skybox_World")
                bpy.context.scene.world = world
            world.use_nodes = True
            w_nodes = world.node_tree.nodes
            w_links = world.node_tree.links
            bg_node = w_nodes.get("Background")
            if not bg_node:
                bg_node = w_nodes.new('ShaderNodeBackground')

            # Create Environment Texture node
            env_tex = w_nodes.new('ShaderNodeTexEnvironment')
            try:
                img = bpy.data.images.load(filepath)
                env_tex.image = img
                w_links.new(env_tex.outputs['Color'], bg_node.inputs['Color'])
            except Exception as e:
                print(f"Error loading sky image: {e}")
            return None, f"Applied skybox texture to World: {os.path.basename(filepath)}"

        # Configure imported mesh objects
        for obj in imported_objs:
            if obj.type == 'MESH':
                obj.name = f"h3_sky_{obj.name}"
                # Ensure faces have +sky material
                mat_sky = Halo3ShaderManager.get_or_create_material("+sky")
                if mat_sky.name not in [m.name for m in obj.data.materials if m]:
                    obj.data.materials.append(mat_sky)
                # Inward normal check
                bm = bmesh.new()
                bm.from_mesh(obj.data)
                # Invert if normals face outward
                avg_nz = sum(f.normal.z for f in bm.faces) / max(1, len(bm.faces))
                if avg_nz > 0.0:
                    for f in bm.faces:
                        f.normal_flip()
                bm.to_mesh(obj.data)
                bm.free()

        return imported_objs[0] if imported_objs else None, f"Imported {len(imported_objs)} sky object(s)"


class HaloWaterGenerator:
    """Generates authentic Halo water surfaces and physics volumes with 4 UV channels and proper Sapien rendering."""

    @staticmethod
    def generate_water(water_type="SURFACE_RENDER", water_level_z=0.0, width=1600.0,
                       length=3200.0, depth=120.0, target_terrain=None,
                       water_shader="river riverworld_water_rough'",
                       subdivisions_x=8, subdivisions_y=16):
        """
        Creates authentic Halo water geometry.
        - SURFACE_RENDER: Planar water surface with normal facing +Z, 4 UV sets (180, 90, 360, 45),
          and render-only / water collision flags for flawless Sapien and in-game rendering.
        - BOX_VOLUME / PHYSICS_VOLUME: 3D extruded volume box named '~water' for structure_design physics and sound.
        """
        # Format water shader tag for Tool.exe:
        # Preserve trailing single quote (') which marks water collision surface tag in Blam!
        clean_shader = water_shader.strip() if water_shader else ""
        if not clean_shader:
            clean_shader = "river riverworld_water_rough'"

        if water_type == "BOTH":
            surf = HaloWaterGenerator.generate_water(
                water_type="SURFACE_RENDER",
                water_level_z=water_level_z,
                width=width,
                length=length,
                depth=depth,
                target_terrain=target_terrain,
                water_shader=clean_shader,
                subdivisions_x=subdivisions_x,
                subdivisions_y=subdivisions_y
            )
            _ = HaloWaterGenerator.generate_water(
                water_type="PHYSICS_VOLUME",
                water_level_z=water_level_z,
                width=width,
                length=length,
                depth=depth,
                target_terrain=target_terrain,
                water_shader=clean_shader,
                subdivisions_x=subdivisions_x,
                subdivisions_y=subdivisions_y
            )
            return surf

        is_physics = water_type in ("BOX_VOLUME", "PHYSICS_VOLUME")
        obj_name = "~water_physics" if is_physics else "water_surface"
        mesh = bpy.data.meshes.new(obj_name)
        bm = bmesh.new()

        if target_terrain and target_terrain.type == 'MESH':
            mw = target_terrain.matrix_world
            bb = [mw @ Vector(c) for c in target_terrain.bound_box]
            min_x = min(c.x for c in bb)
            max_x = max(c.x for c in bb)
            min_y = min(c.y for c in bb)
            max_y = max(c.y for c in bb)
            width = max(100.0, max_x - min_x)
            length = max(100.0, max_y - min_y)
            center_x = (min_x + max_x) * 0.5
            center_y = (min_y + max_y) * 0.5
        else:
            center_x = 0.0
            center_y = 0.0

        half_w = width * 0.5
        half_l = length * 0.5

        if not is_physics:
            # 1. RENDERABLE WATER SURFACE (Tessellated horizontal plane at water_level_z facing +Z into playable space)
            # Tessellated grid gives smooth specular curvature, vertex lighting, and water shader wave vertex animation
            sub_x = max(2, int(subdivisions_x))
            sub_y = max(2, int(subdivisions_y))
            dx = width / sub_x
            dy = length / sub_y

            grid_verts = []
            for j in range(sub_y + 1):
                row = []
                vy = (center_y - half_l) + j * dy
                for i in range(sub_x + 1):
                    vx = (center_x - half_w) + i * dx
                    row.append(bm.verts.new(Vector((vx, vy, water_level_z))))
                grid_verts.append(row)

            # Create quad faces with normal pointing straight UP (+Z) into sky/camera
            for j in range(sub_y):
                for i in range(sub_x):
                    v1 = grid_verts[j][i]
                    v2 = grid_verts[j][i + 1]
                    v3 = grid_verts[j + 1][i + 1]
                    v4 = grid_verts[j + 1][i]
                    bm.faces.new((v1, v2, v3, v4))
        else:
            # 2. 3D CLOSED BOX WATER PHYSICS & SOUND VOLUME (for structure_design, as documented in PDF p. 172-174)
            z_top = water_level_z
            z_bot = water_level_z - max(20.0, depth)

            # Top 4 vertices (+Z)
            t1 = bm.verts.new(Vector((center_x - half_w, center_y - half_l, z_top)))
            t2 = bm.verts.new(Vector((center_x + half_w, center_y - half_l, z_top)))
            t3 = bm.verts.new(Vector((center_x + half_w, center_y + half_l, z_top)))
            t4 = bm.verts.new(Vector((center_x - half_w, center_y + half_l, z_top)))

            # Bottom 4 vertices (-Z)
            b1 = bm.verts.new(Vector((center_x - half_w, center_y - half_l, z_bot)))
            b2 = bm.verts.new(Vector((center_x + half_w, center_y - half_l, z_bot)))
            b3 = bm.verts.new(Vector((center_x + half_w, center_y + half_l, z_bot)))
            b4 = bm.verts.new(Vector((center_x - half_w, center_y + half_l, z_bot)))

            # Outward-facing faces for physics volume (PDF Page 173)
            bm.faces.new((t1, t2, t3, t4))
            bm.faces.new((b4, b3, b2, b1))
            bm.faces.new((t1, b1, b2, t2)) # South
            bm.faces.new((t2, b2, b3, t3)) # East
            bm.faces.new((t3, b3, b4, t4)) # North
            bm.faces.new((t4, b4, b1, t1)) # West

        # Create exact 4 UV channels required by Blam! .shader_water (UV_0, UV_1, UV_2, UV_3)
        scales = [180.0, 90.0, 360.0, 45.0]
        for uv_i in range(4):
            uv_layer = bm.loops.layers.uv.new(f"UV_{uv_i}")
            scale = scales[uv_i]
            for f in bm.faces:
                for loop in f.loops:
                    p = loop.vert.co
                    loop[uv_layer].uv = Vector((p.x / scale, p.y / scale))

        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])

        bm.to_mesh(mesh)
        bm.free()

        obj = bpy.data.objects.new(obj_name, mesh)
        bpy.context.scene.collection.objects.link(obj)

        HaloCompiler.parent_to_level_root(obj)

        # Apply authentic water shader material with transparent cyan BSDF
        mat_water = Halo3ShaderManager.get_or_create_material(clean_shader)
        obj.data.materials.append(mat_water)

        return obj


class HaloAssetPrepper:
    """Prepares Editing Kit scene structure for props, scenery, crates, weapons, and vehicles."""

    @staticmethod
    def setup_asset_scene(asset_name="unsc_crate_01", asset_type="CRATE",
                          clear_scene=True, add_spartan=True, base_dim=(60.0, 60.0, 50.0)):
        scene = bpy.context.scene

        if clear_scene:
            for obj in list(scene.objects):
                bpy.data.objects.remove(obj, do_unlink=True)

        scene.unit_settings.system = 'NONE'
        scene.unit_settings.scale_length = 1.0

        # Armature b_root
        arm_data = bpy.data.armatures.new(f"{asset_name}_armature")
        arm_obj = bpy.data.objects.new(f"armature_{asset_name}", arm_data)
        scene.collection.objects.link(arm_obj)
        bpy.context.view_layer.objects.active = arm_obj
        bpy.ops.object.mode_set(mode='EDIT')
        root_bone = arm_data.edit_bones.new("b_root")
        root_bone.head = Vector((0.0, 0.0, 0.0))
        root_bone.tail = Vector((0.0, 0.0, 20.0))
        bpy.ops.object.mode_set(mode='OBJECT')

        # Collections
        col_render = bpy.data.collections.get("Render") or bpy.data.collections.new("Render")
        if col_render.name not in scene.collection.children:
            scene.collection.children.link(col_render)

        col_collision = bpy.data.collections.get("Collision") or bpy.data.collections.new("Collision")
        if col_collision.name not in scene.collection.children:
            scene.collection.children.link(col_collision)

        col_physics = bpy.data.collections.get("Physics") or bpy.data.collections.new("Physics")
        if col_physics.name not in scene.collection.children:
            scene.collection.children.link(col_physics)

        col_markers = bpy.data.collections.get("Markers") or bpy.data.collections.new("Markers")
        if col_markers.name not in scene.collection.children:
            scene.collection.children.link(col_markers)

        # Create collision hull @<asset_name>_collision (100% watertight)
        coll_mesh = bpy.data.meshes.new(f"@{asset_name}_collision")
        bm_c = bmesh.new()
        bmesh.ops.create_cube(bm_c, size=1.0)
        bmesh.ops.scale(bm_c, vec=Vector(base_dim), verts=bm_c.verts[:])
        bmesh.ops.translate(bm_c, vec=Vector((0.0, 0.0, base_dim[2] * 0.5)), verts=bm_c.verts[:])
        bmesh.ops.triangulate(bm_c, faces=bm_c.faces[:])
        bmesh.ops.recalc_face_normals(bm_c, faces=bm_c.faces[:])
        bm_c.to_mesh(coll_mesh)
        bm_c.free()
        coll_obj = bpy.data.objects.new(f"@{asset_name}_collision", coll_mesh)
        col_collision.objects.link(coll_obj)
        coll_obj.parent = arm_obj

        # Create physics hull $<asset_name>_physics (100% watertight)
        phys_mesh = bpy.data.meshes.new(f"${asset_name}_physics")
        bm_p = bmesh.new()
        bmesh.ops.create_cube(bm_p, size=1.0)
        bmesh.ops.scale(bm_p, vec=Vector((base_dim[0] * 0.9, base_dim[1] * 0.9, base_dim[2] * 0.9)), verts=bm_p.verts[:])
        bmesh.ops.translate(bm_p, vec=Vector((0.0, 0.0, base_dim[2] * 0.45)), verts=bm_p.verts[:])
        bmesh.ops.triangulate(bm_p, faces=bm_p.faces[:])
        bmesh.ops.recalc_face_normals(bm_p, faces=bm_p.faces[:])
        bm_p.to_mesh(phys_mesh)
        bm_p.free()
        phys_obj = bpy.data.objects.new(f"${asset_name}_physics", phys_mesh)
        col_physics.objects.link(phys_obj)
        phys_obj.parent = arm_obj

        # Spartan scale reference model
        if add_spartan:
            bm_sp = bmesh.new()
            bmesh.ops.create_cone(bm_sp, cap_ends=True, cap_tris=False, segments=12, radius1=12.0, radius2=8.0, depth=HALO_CHIEF_HEIGHT)
            bm_sp.transform(Matrix.Translation(Vector((0.0, 0.0, HALO_CHIEF_HEIGHT * 0.5))))
            sp_mesh = bpy.data.meshes.new("scale_model_spartan")
            bm_sp.to_mesh(sp_mesh)
            bm_sp.free()

            sp_obj = bpy.data.objects.new("scale_model_spartan", sp_mesh)
            scene.collection.objects.link(sp_obj)
            mat_sp = Halo3ShaderManager.get_or_create_material("%spartan_scale")
            sp_obj.data.materials.append(mat_sp)

        return arm_obj

    @staticmethod
    def generate_tag_template(asset_name, asset_type, output_dir):
        """Generates standard Editing Kit folder tree and tool.exe model compilation batch file."""
        tag_path = os.path.join(output_dir, asset_name)
        os.makedirs(os.path.join(tag_path, "render"), exist_ok=True)
        os.makedirs(os.path.join(tag_path, "collision"), exist_ok=True)
        os.makedirs(os.path.join(tag_path, "physics"), exist_ok=True)

        bat_content = f"""@echo off
REM Halo Editing Kit Model Import Batch Script for {asset_name} ({asset_type})
REM Generated by O7OF Halo Tool Suite
echo Importing model: {asset_name}...
tool.exe model "objects\\{asset_type.lower()}\\{asset_name}"
echo Done!
pause
"""
        bat_file = os.path.join(tag_path, f"import_{asset_name}.bat")
        with open(bat_file, "w", encoding="utf-8") as f:
            f.write(bat_content)

        return tag_path

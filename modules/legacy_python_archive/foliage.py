# -*- coding: utf-8 -*-
"""
Halo Foliage Generator & Multi-Tier Vegetation Scatterer
Realistic Pine, Broadleaf, Palm, Cypress, Acacia, Alpine Fir, and Undergrowth Bushes
Tailored specifically for Halo CE, Halo 2, Halo 3, and Halo Reach.
Includes Wind Sway Vertex Colors (Wind_Weights) and authentic '%' instancing shaders.
Created by O7OF | Shoutout to c20 Reclaimers Wiki | Thank you Bungie
"""

import math
import random
import bpy
import bmesh
from mathutils import Vector, Euler

from . import constants
from .compiler import HaloCompiler
from .shaders import Halo3ShaderManager


class HaloFoliageGenerator:
    """Generates procedural Halo trees and vegetation with authentic wind sway and size variations."""

    @staticmethod
    def get_foliage_collection_name(tree_type):
        return "Foliage_Bushes" if "BUSH" in str(tree_type).upper() else "Foliage_Trees"

    @staticmethod
    def get_default_shaders(target_game="HALO_3"):
        """Returns the canonical bark and foliage leaf shaders for the specified Halo game."""
        game_map = {
            "HALO_CE": ("wood", "leaves"),
            "HALO_2": ("coagulation tree_bark", "coagulation tree_leaves"),
            "HALO_3": ("dead deadlock_tree_bark", "dead deadlock_pine_needles"),
            "HALO_REACH": ("settlement settlement_tree_bark", "settlement settlement_tree_leaves"),
        }
        return game_map.get(target_game, ("dead deadlock_tree_bark", "dead deadlock_pine_needles"))

    @staticmethod
    def generate_tree(
        tree_type="CONIFER_PINE",
        height=140.0,
        radius=45.0,
        foliage_shader=None,
        bark_shader=None,
        target_game="HALO_3",
        loc=(0.0, 0.0, 0.0),
        rot_z=0.0,
        size_tier="MATURE",
        seed=None,
        custom_scale=None,
        **kwargs
    ):
        """Generates an authentic Halo tree: solid 6-sided cylinder trunk base + crossed foliage quads
        with % material prefix and Wind_Weights vertex colors."""
        def_bark, def_foliage = HaloFoliageGenerator.get_default_shaders(target_game)
        if not bark_shader:
            bark_shader = def_bark
        if not foliage_shader:
            foliage_shader = def_foliage

        # Enforce % prefix on foliage card material
        if not foliage_shader.startswith("%") and not foliage_shader.startswith("+"):
            foliage_shader = f"%{foliage_shader}"

        mesh_name = f"h3_tree_{str(tree_type).lower()}"
        mesh = bpy.data.meshes.new(mesh_name)
        bm = bmesh.new()

        # Size tier scaling factor
        if custom_scale is not None:
            scale_fac = custom_scale
        elif size_tier in constants.FOLIAGE_TIERS and (height == 140.0 and radius == 45.0):
            scale_fac = constants.FOLIAGE_TIERS[size_tier]["scale_min"]
            height *= scale_fac
            radius *= scale_fac

        foliage_faces = []
        vert_uv_map = {}

        # ----------------------------------------------------------------------
        # Archetype 1: CE Palm Tree (Silent Cartographer coastal palm)
        # ----------------------------------------------------------------------
        if tree_type in ("CE_PALM_TREE", "PALM"):
            trunk_segs = 6
            r_b = radius * 0.12
            r_t = radius * 0.06
            prev_ring = []
            curve_dir = Vector((1.0, 0.5, 0.0)).normalized()

            for s in range(trunk_segs + 1):
                t = s / trunk_segs
                z = t * height
                lean = math.sin(t * math.pi * 0.5) * (height * 0.22)
                cx = curve_dir.x * lean
                cy = curve_dir.y * lean
                r = r_b * (1.0 - t * 0.5)
                ring = []
                for i in range(6):
                    ang = (i / 6.0) * 2.0 * math.pi
                    vx = cx + math.cos(ang) * r
                    vy = cy + math.sin(ang) * r
                    ring.append(bm.verts.new(Vector((vx, vy, z))))
                if prev_ring:
                    for i in range(6):
                        ni = (i + 1) % 6
                        bm.faces.new((prev_ring[i], prev_ring[ni], ring[ni], ring[i]))
                else:
                    bm.faces.new(list(reversed(ring)))
                prev_ring = ring
            bm.faces.new(prev_ring)

            # Palm Frond Cards at Crown
            crown_center = prev_ring[0].co.copy()
            crown_center.z = height
            num_fronds = 8
            for i in range(num_fronds):
                ang = (i / num_fronds) * 2.0 * math.pi
                dx = math.cos(ang) * radius * 0.9
                dy = math.sin(ang) * radius * 0.9
                tip_z = crown_center.z - height * 0.15
                v1 = bm.verts.new(crown_center + Vector((-dy * 0.15, dx * 0.15, 0.0)))
                v2 = bm.verts.new(crown_center + Vector((dy * 0.15, -dx * 0.15, 0.0)))
                v3 = bm.verts.new(Vector((crown_center.x + dx, crown_center.y + dy, tip_z)))
                v4 = bm.verts.new(Vector((crown_center.x + dx * 0.9, crown_center.y + dy * 0.9, tip_z + 10.0)))
                vert_uv_map[v1] = (0.0, 0.0)
                vert_uv_map[v2] = (1.0, 0.0)
                vert_uv_map[v3] = (1.0, 1.0)
                vert_uv_map[v4] = (0.0, 1.0)
                f = bm.faces.new((v1, v2, v3, v4))
                foliage_faces.append(f)

        # ----------------------------------------------------------------------
        # Archetype 2: H3 Acacia Tree (African Savanna umbrella acacia)
        # ----------------------------------------------------------------------
        elif tree_type in ("H3_ACACIA", "ACACIA"):
            # Solid bifurcated trunk
            trunk_sides = 6
            r_base = radius * 0.15
            h_fork = height * 0.45
            base_v = [bm.verts.new(Vector((math.cos(i*math.pi/3)*r_base, math.sin(i*math.pi/3)*r_base, 0.0))) for i in range(6)]
            fork_v = [bm.verts.new(Vector((math.cos(i*math.pi/3)*r_base*0.7, math.sin(i*math.pi/3)*r_base*0.7, h_fork))) for i in range(6)]
            for i in range(6):
                ni = (i + 1) % 6
                bm.faces.new((base_v[i], base_v[ni], fork_v[ni], fork_v[i]))
            bm.faces.new(list(reversed(base_v)))
            bm.faces.new(fork_v)

            # Flat-topped umbrella foliage canopy disks
            num_pads = 5
            for p in range(num_pads):
                ang = (p / num_pads) * 2.0 * math.pi
                cx = math.cos(ang) * (radius * 0.6)
                cy = math.sin(ang) * (radius * 0.6)
                cz = height * (0.85 + (p % 2) * 0.10)
                r_disk = radius * 0.55
                for c in range(3):
                    c_ang = c * (math.pi / 3.0)
                    cdx = math.cos(c_ang) * r_disk
                    cdy = math.sin(c_ang) * r_disk
                    v1 = bm.verts.new(Vector((cx - cdx, cy - cdy, cz - 10.0)))
                    v2 = bm.verts.new(Vector((cx + cdx, cy + cdy, cz - 10.0)))
                    v3 = bm.verts.new(Vector((cx + cdx * 0.8, cy + cdy * 0.8, cz + 15.0)))
                    v4 = bm.verts.new(Vector((cx - cdx * 0.8, cy - cdy * 0.8, cz + 15.0)))
                    vert_uv_map[v1] = (0.0, 0.0)
                    vert_uv_map[v2] = (1.0, 0.0)
                    vert_uv_map[v3] = (1.0, 1.0)
                    vert_uv_map[v4] = (0.0, 1.0)
                    f = bm.faces.new((v1, v2, v3, v4))
                    foliage_faces.append(f)

        # ----------------------------------------------------------------------
        # Archetype 3: Reach Alpine Fir (Dense mountain conifer)
        # ----------------------------------------------------------------------
        elif tree_type in ("REACH_ALPINE_FIR", "ALPINE_FIR", "CONIFER_PINE", "PINE"):
            trunk_sides = 6
            r_base = radius * 0.16
            r_top = radius * 0.04
            h_trunk = height * 0.85

            base_v = [bm.verts.new(Vector((math.cos(i*math.pi/3)*r_base, math.sin(i*math.pi/3)*r_base, 0.0))) for i in range(6)]
            top_v = [bm.verts.new(Vector((math.cos(i*math.pi/3)*r_top, math.sin(i*math.pi/3)*r_top, h_trunk))) for i in range(6)]
            for i in range(6):
                ni = (i + 1) % 6
                bm.faces.new((base_v[i], base_v[ni], top_v[ni], top_v[i]))
            bm.faces.new(list(reversed(base_v)))
            bm.faces.new(top_v)

            # Layered conical skirt cards
            num_tiers = 5
            for t_idx in range(num_tiers):
                t_frac = t_idx / max(1, num_tiers - 1)
                z_bot = height * (0.20 + t_frac * 0.55)
                z_top = z_bot + height * 0.28
                r_tier = radius * (1.0 - t_frac * 0.40)

                for p_idx in range(3):
                    ang = p_idx * (math.pi / 3.0) + (t_idx * 0.45)
                    dx = math.cos(ang) * r_tier
                    dy = math.sin(ang) * r_tier

                    v1 = bm.verts.new(Vector((-dx, -dy, z_bot)))
                    v2 = bm.verts.new(Vector((dx, dy, z_bot)))
                    v3 = bm.verts.new(Vector((dx * 0.60, dy * 0.60, z_top)))
                    v4 = bm.verts.new(Vector((-dx * 0.60, -dy * 0.60, z_top)))
                    vert_uv_map[v1] = (0.0, 0.0)
                    vert_uv_map[v2] = (1.0, 0.0)
                    vert_uv_map[v3] = (1.0, 1.0)
                    vert_uv_map[v4] = (0.0, 1.0)
                    f = bm.faces.new((v1, v2, v3, v4))
                    foliage_faces.append(f)

        # ----------------------------------------------------------------------
        # Archetype 4: Undergrowth Shrub / Bush (Low ground cover)
        # ----------------------------------------------------------------------
        elif tree_type in ("FOLIAGE_BUSH", "UNDER_GROWTH_BUSH", "BUSH"):
            b_height = height if height != 140.0 else 35.0
            b_radius = radius if radius != 45.0 else 30.0
            num_cards = 4
            for c in range(num_cards):
                ang = (c / num_cards) * math.pi
                dx = math.cos(ang) * b_radius
                dy = math.sin(ang) * b_radius
                v1 = bm.verts.new(Vector((-dx, -dy, 0.0)))
                v2 = bm.verts.new(Vector((dx, dy, 0.0)))
                v3 = bm.verts.new(Vector((dx * 0.7, dy * 0.7, b_height)))
                v4 = bm.verts.new(Vector((-dx * 0.7, -dy * 0.7, b_height)))
                vert_uv_map[v1] = (0.0, 0.0)
                vert_uv_map[v2] = (1.0, 0.0)
                vert_uv_map[v3] = (1.0, 1.0)
                vert_uv_map[v4] = (0.0, 1.0)
                f = bm.faces.new((v1, v2, v3, v4))
                foliage_faces.append(f)

        # ----------------------------------------------------------------------
        # Archetype 5: Deciduous Broadleaf Tree / Aspen
        # ----------------------------------------------------------------------
        else:
            trunk_sides = 6
            r_base = radius * 0.14
            r_top = radius * 0.05
            h_trunk = height * 0.70

            base_v = [bm.verts.new(Vector((math.cos(i*math.pi/3)*r_base, math.sin(i*math.pi/3)*r_base, 0.0))) for i in range(6)]
            top_v = [bm.verts.new(Vector((math.cos(i*math.pi/3)*r_top, math.sin(i*math.pi/3)*r_top, h_trunk))) for i in range(6)]
            for i in range(6):
                ni = (i + 1) % 6
                bm.faces.new((base_v[i], base_v[ni], top_v[ni], top_v[i]))
            bm.faces.new(list(reversed(base_v)))
            bm.faces.new(top_v)

            # Crossed foliage quads in upper dome
            num_cards = 6
            canopy_z = height * 0.55
            for c in range(num_cards):
                ang = (c / num_cards) * math.pi
                dx = math.cos(ang) * radius * 0.8
                dy = math.sin(ang) * radius * 0.8
                v1 = bm.verts.new(Vector((-dx, -dy, canopy_z)))
                v2 = bm.verts.new(Vector((dx, dy, canopy_z)))
                v3 = bm.verts.new(Vector((dx * 0.7, dy * 0.7, height)))
                v4 = bm.verts.new(Vector((-dx * 0.7, -dy * 0.7, height)))
                vert_uv_map[v1] = (0.0, 0.0)
                vert_uv_map[v2] = (1.0, 0.0)
                vert_uv_map[v3] = (1.0, 1.0)
                vert_uv_map[v4] = (0.0, 1.0)
                f = bm.faces.new((v1, v2, v3, v4))
                foliage_faces.append(f)

        # ----------------------------------------------------------------------
        # Materials, UVs, and Wind Weights
        # ----------------------------------------------------------------------
        mat_bark = Halo3ShaderManager.get_or_create_material(bark_shader)
        mat_foliage = Halo3ShaderManager.get_or_create_material(foliage_shader)

        mesh.materials.append(mat_bark)
        mesh.materials.append(mat_foliage)
        idx_bark = 0
        idx_foliage = 1

        for f in bm.faces:
            if f in foliage_faces:
                f.material_index = idx_foliage
                f.smooth = True
            else:
                f.material_index = idx_bark
                f.smooth = False

        uv_l = bm.loops.layers.uv.new("UV_0")
        for f in bm.faces:
            for l in f.loops:
                if l.vert in vert_uv_map:
                    l[uv_l].uv = Vector(vert_uv_map[l.vert])
                else:
                    l[uv_l].uv = Vector((l.vert.co.x / 40.0, l.vert.co.z / 60.0))

        # Wind_Weights vertex colors for engine procedural sway
        wind_layer = bm.loops.layers.color.new("Wind_Weights")
        for f in bm.faces:
            for l in f.loops:
                z_norm = max(0.0, min(1.0, l.vert.co.z / max(1.0, height)))
                sway = z_norm ** 1.8
                l[wind_layer] = (sway, sway, sway, 1.0)

        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])

        bm.to_mesh(mesh)
        bm.free()
        mesh.update()

        obj = bpy.data.objects.new(mesh_name, mesh)
        obj.location = Vector(loc)
        obj.rotation_euler = Euler((0.0, 0.0, rot_z))

        col_name = HaloFoliageGenerator.get_foliage_collection_name(tree_type)
        col = bpy.data.collections.get(col_name)
        if not col:
            col = bpy.data.collections.new(col_name)
            bpy.context.scene.collection.children.link(col)
        col.objects.link(obj)

        HaloCompiler.parent_to_level_root(obj)
        return obj

    @classmethod
    def scatter_foliage_on_terrain(
        cls,
        terrain_obj,
        count=36,
        seed=1337,
        tree_type="CONIFER_PINE",
        min_distance=35.0,
        scatter_mode="REPLACE",
        target_game="HALO_3",
        **kwargs
    ):
        """Scatters authentic vegetation across terrain with clearance checks and collection isolation."""
        if not terrain_obj or terrain_obj.type != 'MESH':
            return []

        col_name = cls.get_foliage_collection_name(tree_type)
        col = bpy.data.collections.get(col_name)
        if not col:
            col = bpy.data.collections.new(col_name)
            bpy.context.scene.collection.children.link(col)

        if str(scatter_mode).upper() == "REPLACE":
            for o in list(col.objects):
                bpy.data.objects.remove(o, do_unlink=True)

        rng = random.Random(seed)
        verts = terrain_obj.data.vertices
        if not verts:
            return []

        # Find habitable ground (slope < 40 degrees, above water)
        ground_verts = [v for v in verts if v.normal.z > 0.70 and v.co.z > -100.0]
        if not ground_verts:
            ground_verts = list(verts)

        placed_locs = []
        if str(scatter_mode).upper() == "APPEND":
            for o in col.objects:
                placed_locs.append(o.location.copy())

        indices = list(range(len(ground_verts)))
        rng.shuffle(indices)
        scattered = []

        for idx in indices:
            if len(scattered) >= count:
                break
            v = ground_verts[idx]
            loc = terrain_obj.matrix_world @ v.co
            cand_pos = Vector((
                loc.x + rng.uniform(-10.0, 10.0),
                loc.y + rng.uniform(-10.0, 10.0),
                loc.z
            ))

            if min_distance > 0.0 and any((cand_pos.xy - p.xy).length < min_distance for p in placed_locs):
                continue

            tier = rng.choice(["SAPLING", "MEDIUM", "MATURE", "MATURE", "OLD_GROWTH"])
            tree_obj = cls.generate_tree(
                tree_type=tree_type,
                size_tier=tier,
                target_game=target_game,
                seed=seed + len(scattered) * 43,
                loc=(cand_pos.x, cand_pos.y, cand_pos.z),
                rot_z=rng.uniform(0.0, math.pi * 2.0)
            )
            tree_obj.name = f"%h3_foliage_{tier.lower()}_{len(scattered):03d}"
            placed_locs.append(cand_pos)
            scattered.append(tree_obj)

        # Fallback relaxation if density requires slightly tighter packing
        if len(scattered) < count:
            relaxed_dist = min_distance * 0.70
            for idx in indices:
                if len(scattered) >= count:
                    break
                v = ground_verts[idx]
                loc = terrain_obj.matrix_world @ v.co
                cand_pos = Vector((loc.x + rng.uniform(-5.0, 5.0), loc.y + rng.uniform(-5.0, 5.0), loc.z))
                if any((cand_pos.xy - p.xy).length < relaxed_dist for p in placed_locs):
                    continue
                tier = rng.choice(["SAPLING", "MEDIUM", "MATURE"])
                tree_obj = cls.generate_tree(
                    tree_type=tree_type,
                    size_tier=tier,
                    target_game=target_game,
                    seed=seed + len(scattered) * 43,
                    loc=(cand_pos.x, cand_pos.y, cand_pos.z),
                    rot_z=rng.uniform(0.0, math.pi * 2.0)
                )
                tree_obj.name = f"%h3_foliage_{tier.lower()}_{len(scattered):03d}"
                placed_locs.append(cand_pos)
                scattered.append(tree_obj)

        return scattered

    @classmethod
    def scatter_foliage(cls, terrain_obj, count=36, seed=1337, tree_type="CONIFER_PINE", target_game="HALO_3", **kwargs):
        """Backward compatible alias for foliage scattering."""
        return cls.scatter_foliage_on_terrain(terrain_obj, count=count, seed=seed, tree_type=tree_type, target_game=target_game, **kwargs)

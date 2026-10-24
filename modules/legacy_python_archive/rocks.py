# -*- coding: utf-8 -*-
"""
Halo Angular Crag, Basalt Column, Rubble & Geologic Rock Generator & Scatterer
Generates authentic game-specific rocks for Halo CE, Halo 2, Halo 3, and Halo Reach.
Features 7 Geologic Archetypes:
- Faceted Boulder (Halo CE Blood Gulch)
- Monolith Crag (High spire needles)
- Stratified Slab (Sedimentary bedding planes)
- Basalt Column (Hexagonal volcanic columns for Reach / Forge World)
- Erosion Wedge (Wind-sheared desert sandstone)
- Rubble Block (Urban architectural debris for Halo 2)
- Natural Rock Arch (Carved geological pass with hollow opening)
Created by O7OF | Shoutout to c20 Reclaimers Wiki | Thank you Bungie
"""

import os
import math
import random
import bpy
import bmesh
from mathutils import Vector, Matrix, Euler, Quaternion, noise

from . import constants
from .compiler import HaloCompiler
from .shaders import Halo3ShaderManager


class HaloRockGenerator:
    """Generates unique, faceted geological rocks with game-type specific geometry and shaders."""

    ARCHETYPES = [
        "FACETED_BOULDER",
        "MONOLITH_CRAG",
        "STRATIFIED_SLAB",
        "BASALT_COLUMN",
        "EROSION_WEDGE",
        "RUBBLE_BLOCK",
        "NATURAL_ARCH",
    ]

    @staticmethod
    def get_default_rock_shader(target_game="HALO_3"):
        """Returns the canonical cliff/rock shader for the specified Halo game."""
        game_map = {
            "HALO_CE": "rock",
            "HALO_2": "coagulation rock_cliff",
            "HALO_3": "dead deadlock_cliff_rock",
            "HALO_REACH": "forge_halo halo_cliff_01",
        }
        return game_map.get(target_game, "dead deadlock_cliff_rock")

    @staticmethod
    def generate_rock(
        rock_type="FACETED_BOULDER",
        size=(80.0, 60.0, 50.0),
        size_tier="MEDIUM",
        archetype="RANDOM",
        seed=None,
        rock_shader=None,
        material_name=None,
        target_game="HALO_3",
        size_x=None, size_y=None, size_z=None,
        smooth_passes=2,
        noise_amp=0.18,
        flatten_base=True,
        subdivs=2,
        subdivisions=2,
        tunnel_w=140.0, tunnel_h=80.0,
        loc_x=0.0, loc_y=0.0, loc_z=0.0,
        rot_z=0.0,
        custom_scale=None,
        **kwargs
    ):
        """Generates an authentic geological rock or natural rock arch bridge."""
        if material_name:
            rock_shader = material_name
        if not rock_shader:
            rock_shader = HaloRockGenerator.get_default_rock_shader(target_game)

        if seed is not None:
            rng = random.Random(seed)
        else:
            rng = random.Random()

        # Handle size overrides
        if size_x is not None and size_y is not None and size_z is not None:
            sx, sy, sz = size_x, size_y, size_z
        else:
            tier_data = constants.ROCK_TIERS.get(size_tier, constants.ROCK_TIERS["MEDIUM"])
            base_size = custom_scale if custom_scale is not None else rng.uniform(tier_data["scale_min"], tier_data["scale_max"])
            sx = size[0] if (size and size != (80.0, 60.0, 50.0)) else base_size
            sy = size[1] if (size and size != (80.0, 60.0, 50.0)) else base_size * 0.85
            sz = size[2] if (size and size != (80.0, 60.0, 50.0)) else base_size * 0.75

        # Determine actual archetype
        arch = rock_type if rock_type != "RANDOM" else rng.choice([
            "FACETED_BOULDER", "MONOLITH_CRAG", "STRATIFIED_SLAB", "BASALT_COLUMN", "EROSION_WEDGE"
        ])

        mesh_name = f"h3_rock_{arch.lower()}"
        mesh = bpy.data.meshes.new(mesh_name)
        bm = bmesh.new()

        # Archetype A: Natural Rock Arch
        if arch == "NATURAL_ARCH":
            span = max(100.0, sx)
            depth = max(60.0, sy)
            height = max(60.0, sz)
            t_w = min(span * 0.75, max(40.0, tunnel_w))
            t_h = min(height * 0.75, max(25.0, tunnel_h))

            nu, nv = 20, 10
            half_span = span * 0.5
            half_depth = depth * 0.5
            half_tw = t_w * 0.5

            top_grid = []
            for iv in range(nv):
                row = []
                v_t = (iv / (nv - 1)) * 2.0 - 1.0
                y = v_t * half_depth
                y_factor = 1.0 - 0.25 * (v_t ** 2)
                for iu in range(nu):
                    u_t = (iu / (nu - 1)) * 2.0 - 1.0
                    x = u_t * half_span
                    arch_shape = math.cos(u_t * (math.pi * 0.42))
                    z_top = height * arch_shape * y_factor + 15.0
                    n = noise.noise(Vector((x * 0.02, y * 0.02, z_top * 0.02))) * (height * noise_amp)
                    v = bm.verts.new(Vector((x, y, max(5.0, z_top + n))))
                    row.append(v)
                top_grid.append(row)

            bot_grid = []
            for iv in range(nv):
                row = []
                v_t = (iv / (nv - 1)) * 2.0 - 1.0
                y = v_t * half_depth
                y_factor = 1.0 - 0.20 * (v_t ** 2)
                for iu in range(nu):
                    u_t = (iu / (nu - 1)) * 2.0 - 1.0
                    x = u_t * half_span
                    dist_c = abs(x)
                    if dist_c < half_tw:
                        t_ratio = dist_c / half_tw
                        tunnel_curve = math.cos(t_ratio * (math.pi * 0.5)) ** 1.3
                        z_bot = t_h * tunnel_curve * y_factor
                        n = noise.noise(Vector((x * 0.025, y * 0.025, z_bot * 0.025))) * (t_h * noise_amp * 0.5)
                        z_bot = max(0.0, z_bot + n)
                    else:
                        z_bot = 0.0
                    v = bm.verts.new(Vector((x, y, z_bot)))
                    row.append(v)
                bot_grid.append(row)

            for iv in range(nv - 1):
                for iu in range(nu - 1):
                    bm.faces.new((top_grid[iv][iu], top_grid[iv][iu + 1], top_grid[iv + 1][iu + 1], top_grid[iv + 1][iu]))
                    bm.faces.new((bot_grid[iv][iu], bot_grid[iv + 1][iu], bot_grid[iv + 1][iu + 1], bot_grid[iv][iu + 1]))

            for iv in range(nv - 1):
                bm.faces.new((top_grid[iv][0], bot_grid[iv][0], bot_grid[iv + 1][0], top_grid[iv + 1][0]))
                bm.faces.new((top_grid[iv][nu - 1], top_grid[iv + 1][nu - 1], bot_grid[iv + 1][nu - 1], bot_grid[iv][nu - 1]))

            for iu in range(nu - 1):
                bm.faces.new((top_grid[0][iu], top_grid[0][iu + 1], bot_grid[0][iu + 1], bot_grid[0][iu]))
                bm.faces.new((top_grid[nv - 1][iu], bot_grid[nv - 1][iu], bot_grid[nv - 1][iu + 1], top_grid[nv - 1][iu + 1]))

        # Archetype B: Hexagonal Basalt Column (Reach / Spire style)
        elif arch == "BASALT_COLUMN":
            num_sides = 6
            col_h = sz * 1.4
            r_top = sx * 0.5
            r_bot = sx * 0.55
            top_v = []
            bot_v = []
            for i in range(num_sides):
                ang = (i / num_sides) * 2.0 * math.pi
                bx = math.cos(ang) * r_bot + rng.uniform(-sx * 0.05, sx * 0.05)
                by = math.sin(ang) * r_bot + rng.uniform(-sy * 0.05, sy * 0.05)
                tx = math.cos(ang) * r_top + rng.uniform(-sx * 0.05, sx * 0.05)
                ty = math.sin(ang) * r_top + rng.uniform(-sy * 0.05, sy * 0.05)
                tz = col_h + rng.uniform(-sz * 0.08, sz * 0.08)
                bot_v.append(bm.verts.new(Vector((bx, by, 0.0))))
                top_v.append(bm.verts.new(Vector((tx, ty, tz))))

            for i in range(num_sides):
                next_i = (i + 1) % num_sides
                bm.faces.new((bot_v[i], bot_v[next_i], top_v[next_i], top_v[i]))
            bm.faces.new(list(reversed(bot_v)))
            bm.faces.new(top_v)

        # Archetype C: Fractured Rubble Block (Halo 2 Urban / Architectural Debris)
        elif arch == "RUBBLE_BLOCK":
            bmesh.ops.create_cube(bm, size=1.0)
            for v in bm.verts:
                v.co.x *= sx * (1.0 + rng.uniform(-0.15, 0.15))
                v.co.y *= sy * (1.0 + rng.uniform(-0.15, 0.15))
                v.co.z *= sz * (1.0 + rng.uniform(-0.15, 0.15))
                if flatten_base and v.co.z < 0.0:
                    v.co.z = 0.0
            # Bevel edges for realistic fractured masonry
            bmesh.ops.bevel(bm, geom=bm.edges[:], offset=min(sx, sy, sz) * 0.12, segments=2)

        # Archetype D: Monolith Crag / Tall Spire
        elif arch == "MONOLITH_CRAG":
            bmesh.ops.create_icosphere(bm, subdivisions=2, radius=1.0)
            for v in bm.verts:
                # Elongate vertically and add sharp cleavage
                v.co.x *= sx * 0.6
                v.co.y *= sy * 0.6
                v.co.z = max(0.0, v.co.z * sz * 1.8) if flatten_base else v.co.z * sz * 1.8
                n = noise.noise(Vector((v.co.x * 0.03, v.co.y * 0.03, v.co.z * 0.03)))
                v.co += v.co.normalized() * (n * sz * noise_amp)

        # Archetype E: Stratified Sedimentary Slab (Tsavo Highway style)
        elif arch == "STRATIFIED_SLAB":
            bmesh.ops.create_cube(bm, size=1.0)
            bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=3)
            for v in bm.verts:
                v.co.x *= sx * 1.2
                v.co.y *= sy * 1.0
                v.co.z *= sz * 0.45
                strata_step = math.sin(v.co.z * 0.25) * (sz * 0.10)
                v.co.x += strata_step
                if flatten_base and v.co.z < 0.0:
                    v.co.z = 0.0

        # Archetype F: Faceted Boulder (Classic Halo CE & Reach Boulder)
        else:
            bmesh.ops.create_icosphere(bm, subdivisions=2, radius=1.0)
            for v in bm.verts:
                v.co.x *= sx * 0.5
                v.co.y *= sy * 0.5
                v.co.z *= sz * 0.5
                n = noise.noise(Vector((v.co.x * 0.02, v.co.y * 0.02, v.co.z * 0.02)))
                v.co += v.co.normalized() * (n * sz * noise_amp)
                if flatten_base and v.co.z < -sz * 0.2:
                    v.co.z = -sz * 0.2

        # Final Polish: Triangulate & Recalculate Normals
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])

        # Generate Continuous UVs
        uv_l = bm.loops.layers.uv.new("UV_0")
        for f in bm.faces:
            f.smooth = True
            for l in f.loops:
                l[uv_l].uv = Vector((l.vert.co.x / 120.0, l.vert.co.z / 120.0))

        bm.to_mesh(mesh)
        bm.free()
        mesh.update()

        # Apply Authentic Material
        mat = Halo3ShaderManager.get_or_create_material(rock_shader)
        mesh.materials.append(mat)

        obj = bpy.data.objects.new(mesh_name, mesh)
        obj.location = Vector((loc_x, loc_y, loc_z))
        obj.rotation_euler = Euler((0.0, 0.0, rot_z))

        # Link to Rock_Scatter collection
        col = bpy.data.collections.get("Rock_Scatter")
        if not col:
            col = bpy.data.collections.new("Rock_Scatter")
            bpy.context.scene.collection.children.link(col)
        col.objects.link(obj)

        HaloCompiler.parent_to_level_root(obj)
        return obj

    @classmethod
    def scatter_rocks_on_terrain(
        cls,
        terrain_obj,
        passes=3,
        huge_count=2,
        med_count=6,
        small_count=14,
        micro_count=20,
        rock_shader=None,
        spread_scalar=1.8,
        min_distance=35.0,
        seed=4242,
        target_game="HALO_3",
        count=None,
        clear_existing=True,
        **kwargs
    ):
        """Scatters authentic geological rocks across terrain surface organized by tier passes with collision avoidance."""
        if not terrain_obj or terrain_obj.type != 'MESH':
            return []

        col = bpy.data.collections.get("Rock_Scatter")
        if col and clear_existing:
            for o in list(col.objects):
                bpy.data.objects.remove(o, do_unlink=True)

        rng = random.Random(seed)
        verts = terrain_obj.data.vertices
        if not verts:
            return []

        floor_verts = [v for v in verts if v.normal.z > 0.65 and v.co.z < 600.0]
        if not floor_verts:
            floor_verts = list(verts)

        scattered = []
        placed_info = []

        if count is not None:
            h = max(1, int(count * 0.10))
            m = max(1, int(count * 0.25))
            s = max(1, int(count * 0.45))
            u = max(0, count - (h + m + s))
            plan = [("HUGE", h), ("MEDIUM", m), ("SMALL", s), ("MICRO", u)]
        else:
            plan = [("HUGE", huge_count), ("MEDIUM", med_count), ("SMALL", small_count), ("MICRO", micro_count)]

        scatter_scales = {
            "HUGE": (220.0, 360.0),
            "MEDIUM": (90.0, 160.0),
            "SMALL": (35.0, 75.0),
            "MICRO": (12.0, 28.0)
        }
        archetypes = ["FACETED_BOULDER", "MONOLITH_CRAG", "STRATIFIED_SLAB", "BASALT_COLUMN", "EROSION_WEDGE"]
        active_plan = plan[:passes] if (passes and passes < len(plan)) else plan

        for tier, tier_cnt in active_plan:
            if tier_cnt <= 0:
                continue
            s_min, s_max = scatter_scales.get(tier, (80.0, 150.0))
            attempts = 0
            placed_for_tier = 0
            while placed_for_tier < tier_cnt and attempts < tier_cnt * 60:
                attempts += 1
                v = rng.choice(floor_verts)
                loc = terrain_obj.matrix_world @ v.co
                jitter_r = 35.0 * spread_scalar
                cand_pos = Vector((
                    loc.x + rng.uniform(-jitter_r, jitter_r),
                    loc.y + rng.uniform(-jitter_r, jitter_r),
                    loc.z - 4.0
                ))

                cand_size = rng.uniform(s_min, s_max)
                cand_radius = cand_size * 0.5

                if any((cand_pos.xy - p_pos.xy).length < (cand_radius + p_rad) * 0.98 for p_pos, p_rad in placed_info):
                    continue

                arch = rng.choice(archetypes)
                rock_obj = cls.generate_rock(
                    rock_type=arch,
                    size_tier=tier,
                    custom_scale=cand_size,
                    rock_shader=rock_shader,
                    seed=seed + len(scattered) * 31,
                    target_game=target_game,
                    loc_x=cand_pos.x,
                    loc_y=cand_pos.y,
                    loc_z=cand_pos.z,
                    rot_z=rng.uniform(0.0, math.pi * 2.0)
                )
                rock_obj.name = f"%scatter_rock_{tier.lower()}_{len(scattered) + 1}"
                actual_rad = max(rock_obj.dimensions.x, rock_obj.dimensions.y) * 0.5
                placed_info.append((cand_pos, actual_rad))
                scattered.append(rock_obj)
                placed_for_tier += 1

        return scattered

    @classmethod
    def scatter_rocks(cls, terrain_obj, count=24, seed=1337, target_game="HALO_3", **kwargs):
        """Backward compatible alias for rock scattering."""
        return cls.scatter_rocks_on_terrain(terrain_obj, count=count, seed=seed, target_game=target_game, **kwargs)

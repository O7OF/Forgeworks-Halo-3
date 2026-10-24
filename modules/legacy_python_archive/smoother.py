# -*- coding: utf-8 -*-
"""
Halo Geometry Smoother & Laplacian Elevation Filter
Smooths terrain elevation, mountain peaks, and cliff walls to eliminate jagged
triangular facets, harsh stepped terraces, and jagged sawteeth while preserving airtight sealed boundaries.
Created by O7OF | Shoutout to c20 Reclaimers Wiki | Thank you Bungie
"""

import math
import bpy
import bmesh
from mathutils import Vector


class HaloGeometrySmoother:
    """Relaxes terrain mesh vertices to produce smooth rolling contours, organic mountain crests,
    and natural cliff faces without breaking sealed boundaries or unsealing BSP hulls.
    """

    @staticmethod
    def smooth_terrain_mesh(obj, passes=3, strength=0.45, smooth_mode="ALL", lock_boundaries=True):
        """Applies Laplacian smoothing to terrain elevation and mountain tops.
        
        Args:
            obj: Target Blender mesh object.
            passes: Number of relaxation iterations (default: 3).
            strength: Smoothing blending factor per pass (default: 0.45).
            smooth_mode: 'ALL', 'MOUNTAIN_TOPS', 'CLIFFS', or 'FLOOR'.
            lock_boundaries: If True, preserves airtight perimeter sealing while smoothing Z spikes.
        
        Returns:
            Number of smoothed vertices.
        """
        if not obj or obj.type != 'MESH' or not obj.data or not obj.data.vertices:
            return 0

        bm = bmesh.new()
        bm.from_mesh(obj.data)
        count = HaloGeometrySmoother.smooth_terrain_bmesh(
            bm, passes=passes, strength=strength, smooth_mode=smooth_mode, lock_boundaries=lock_boundaries, obj=obj
        )
        bm.to_mesh(obj.data)
        bm.free()
        obj.data.update()
        return count

    @staticmethod
    def smooth_mountain_tops(obj, passes=4, strength=0.55, lock_boundaries=True):
        """Specifically targets mountain crests, peaks, and upper perimeter rims to eliminate
        harsh cutoffs, stepped shelves, and jagged triangular teeth.
        """
        return HaloGeometrySmoother.smooth_terrain_mesh(
            obj, passes=passes, strength=strength, smooth_mode="MOUNTAIN_TOPS", lock_boundaries=lock_boundaries
        )

    @staticmethod
    def smooth_cliff_walls(obj, passes=4, strength=0.50, lock_boundaries=True):
        """Specifically targets steep cliff faces and rock walls to remove accordion/pleated
        hexagonal lattice striations and jagged terraces.
        """
        return HaloGeometrySmoother.smooth_terrain_mesh(
            obj, passes=passes, strength=strength, smooth_mode="CLIFFS", lock_boundaries=lock_boundaries
        )

    @staticmethod
    def smooth_terrain_bmesh(bm, passes=3, strength=0.45, smooth_mode="ALL", lock_boundaries=True, obj=None):
        """Applies Laplacian smoothing directly to a bmesh instance while preserving airtightness."""
        bm.verts.ensure_lookup_table()
        bm.edges.ensure_lookup_table()
        bm.faces.ensure_lookup_table()
        bm.normal_update()

        if not bm.verts:
            return 0

        # Detect horizontal ceiling vertices in SEALED_ROOM mode to avoid pulling ceiling down
        ceiling_verts = set()
        max_all_z = max(v.co.z for v in bm.verts)
        sky_mat_indices = set()
        if obj and obj.data and obj.data.materials:
            for idx, mat in enumerate(obj.data.materials):
                if mat and "+sky" in mat.name:
                    sky_mat_indices.add(idx)

        for f in bm.faces:
            if f.material_index in sky_mat_indices:
                if f.normal.z < -0.85 and abs(f.calc_center_median().z - max_all_z) < 5.0:
                    for v in f.verts:
                        ceiling_verts.add(v)

        # Non-ceiling terrain vertices
        terrain_verts = [v for v in bm.verts if v not in ceiling_verts]
        if not terrain_verts:
            return 0

        all_x = [v.co.x for v in terrain_verts]
        all_y = [v.co.y for v in terrain_verts]
        all_z = [v.co.z for v in terrain_verts]

        min_x, max_x = min(all_x), max(all_x)
        min_y, max_y = min(all_y), max(all_y)
        min_z, max_z = min(all_z), max(all_z)
        z_span = max(1.0, max_z - min_z)

        # Determine target vertex subset based on mode
        target_set = set()
        for v in terrain_verts:
            rel_z = (v.co.z - min_z) / z_span
            slope_deg = math.degrees(math.acos(max(-1.0, min(1.0, v.normal.z))))

            if smooth_mode == "MOUNTAIN_TOPS":
                # Vertices in upper 38% elevation or near ridge tops
                if rel_z >= 0.38 or (rel_z >= 0.25 and slope_deg > 35.0):
                    target_set.add(v)
            elif smooth_mode == "CLIFFS":
                # Steep slopes: rock palisades, terraces, cliff faces
                if slope_deg >= 22.0:
                    target_set.add(v)
            elif smooth_mode == "FLOOR":
                # Low valley floor and riverbanks
                if rel_z <= 0.35 and slope_deg < 25.0:
                    target_set.add(v)
            else: # "ALL"
                target_set.add(v)

        if not target_set:
            return 0

        bound_margin = 0.5
        for _ in range(passes):
            new_pos = {}
            for v in target_set:
                neighbors = [e.other_vert(v) for e in v.link_edges if e.other_vert(v) not in ceiling_verts]
                if not neighbors:
                    continue

                avg_co = sum((n.co for n in neighbors), Vector((0, 0, 0))) / len(neighbors)

                # Check if vertex lies on outer bounding box plane
                on_min_x = abs(v.co.x - min_x) < bound_margin
                on_max_x = abs(v.co.x - max_x) < bound_margin
                on_min_y = abs(v.co.y - min_y) < bound_margin
                on_max_y = abs(v.co.y - max_y) < bound_margin
                is_boundary = on_min_x or on_max_x or on_min_y or on_max_y or v.is_boundary

                # Elevation relaxation: smoothly harmonizes Z towards neighbor average
                weight = strength
                if smooth_mode == "MOUNTAIN_TOPS":
                    # Increase relaxation weight towards the mountain peak
                    rel_z = (v.co.z - min_z) / z_span
                    weight = min(0.85, strength * (1.0 + rel_z * 0.5))

                target_z = v.co.z + weight * (avg_co.z - v.co.z)

                # Planar constraint enforcement:
                # If vertex is on a bounding plane, lock its coordinate on that plane so the sealed box remains 100% airtight!
                if is_boundary and lock_boundaries:
                    target_x = min_x if on_min_x else (max_x if on_max_x else v.co.x)
                    target_y = min_y if on_min_y else (max_y if on_max_y else v.co.y)
                else:
                    # Subtle tangential relaxation removes zigzag triangular pleats on steep walls
                    tangent_factor = strength * 0.35
                    target_x = v.co.x + tangent_factor * (avg_co.x - v.co.x)
                    target_y = v.co.y + tangent_factor * (avg_co.y - v.co.y)

                new_pos[v] = Vector((target_x, target_y, target_z))

            for v, pos in new_pos.items():
                v.co = pos

        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        return len(target_set)

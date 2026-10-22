# -*- coding: utf-8 -*-
"""
Halo Reach (HREK) Dedicated Terrain Generation Engine
Clean, standalone module tailored for Halo Reach Blam Gen 3 BSP rules:
- 131,072 vertex allocation ceiling (64-bit engine)
- .GR2 geometry pipeline (FBX + sidecar XML + 'tool.exe fbx-to-gr2')
- Sidecar XML schema generation for 'tool.exe import <sidecar> force farm_bsp'
- Automatic sizing vs Master Chief / Spartan scale model (guaranteed >= 2x combat envelope)
Created by O7OF | Shoutout to c20 Reclaimers Wiki | Thank you Bungie
"""

import math
import bpy
import bmesh
from mathutils import Vector

from .terrain import HaloTerrainGenerator
from .scale_models import HaloScaleModelManager
from .compiler import HaloCompiler


class HaloReachTerrainEngine:
    """Specialized Halo Reach Terrain Generator."""

    GAME_TITLE = "reach"
    MAX_VERTS = 131072
    DEFAULT_WIDTH = 3500.0
    DEFAULT_LENGTH = 7000.0

    @classmethod
    def generate_reach_terrain(
        cls,
        map_name="reach_pass",
        terrain_type="VALHALLA",
        width=3500.0,
        length=7000.0,
        cliff_height=850.0,
        res_x=64,
        res_y=128,
        seal_mode="SEALED_BLOCK",
        material_mode="UNIFIED_BLENDED",
        enforce_2x_chief_scale=True,
        seed=1337,
    ):
        """Generates an authentic Halo Reach level mesh formatted for .GR2 export."""
        if enforce_2x_chief_scale:
            min_w, min_l = HaloScaleModelManager.calculate_min_playable_terrain_size(cls.GAME_TITLE, multiplier=2.0)
            width = max(width, min_w)
            length = max(length, min_l)

        t_obj = HaloTerrainGenerator.generate_terrain(
            algorithm="HYBRID_ADVANCED",
            terrain_type=terrain_type,
            width=width,
            length=length,
            cliff_height=cliff_height,
            floor_flatness=0.35,
            cliff_steepness=2.2,
            meander_amount=0.15,
            noise_roughness=35.0,
            noise_scale=0.003,
            res_x=res_x,
            res_y=res_y,
            seal_mode=seal_mode,
            terrain_shader="forge_halo halo_grass_01",
            cliff_shader="forge_halo halo_cliff_rock_01",
            water_shader="river riverworld_water_rough'",
            generate_water=False,
            generate_foliage=False,
            scatter_rocks=False,
            generate_portals=False,
            seed=seed,
            material_mode=material_mode,
        )

        if t_obj:
            t_obj.name = f"bsp_{map_name}"
            HaloCompiler.parent_to_level_root(t_obj)

        return t_obj

# -*- coding: utf-8 -*-
"""
Halo 2 (H2EK) Dedicated Terrain Generation Engine
Clean, standalone module tailored for Halo 2 Blam Gen 2 BSP rules:
- 32,768 vertex allocation ceiling
- Dual UV channels
- Watertight sealed geometry for H2EK tool.exe structure
- Automatic sizing vs Master Chief scale model (guaranteed >= 2x combat envelope)
Created by O7OF | Shoutout to c20 Reclaimers Wiki | Thank you Bungie
"""

import math
import bpy
import bmesh
from mathutils import Vector

from .terrain import HaloTerrainGenerator
from .scale_models import HaloScaleModelManager
from .compiler import HaloCompiler


class Halo2TerrainEngine:
    """Specialized Halo 2 Terrain Generator."""

    GAME_TITLE = "halo2"
    MAX_VERTS = 32768
    DEFAULT_WIDTH = 3000.0
    DEFAULT_LENGTH = 6000.0

    @classmethod
    def generate_h2_terrain(
        cls,
        map_name="h2_valley",
        terrain_type="VALHALLA",
        width=3000.0,
        length=6000.0,
        cliff_height=650.0,
        res_x=48,
        res_y=96,
        enforce_2x_chief_scale=True,
        seed=1337,
    ):
        """Generates an authentic Halo 2 level mesh with dual UV channels."""
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
            meander_amount=0.18,
            noise_roughness=30.0,
            noise_scale=0.003,
            res_x=min(res_x, 64),
            res_y=min(res_y, 128),
            seal_mode="SEALED_BLOCK",
            terrain_shader="coagulation grass",
            cliff_shader="coagulation rock",
            water_shader="coagulation water",
            generate_water=False,
            generate_foliage=False,
            scatter_rocks=False,
            generate_portals=False,
            seed=seed,
            material_mode="DISCRETE_SLOPES",
        )

        if t_obj:
            t_obj.name = f"bsp_{map_name}"
            HaloCompiler.parent_to_level_root(t_obj)

        return t_obj

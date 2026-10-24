# -*- coding: utf-8 -*-
"""
Halo 3 & Reach Modular Tool Suite - UI PropertyGroup and 3D View Panels
Category: scenario:game_engine_prepper
Provides list picker dropdowns for all 13,700+ authentic Halo shaders,
grand landscape environment presets, full noise and mountain customization,
3D cave controls, 1-click import/export, and in-depth modder guides.
Created by O7OF | Shoutout to c20 Reclaimers Wiki | Thank you Bungie
"""

import bpy
from . import constants
from .shaders import (
    get_ground_shader_enum,
    get_cliff_shader_enum,
    get_water_shader_enum,
    get_foliage_shader_enum,
    get_bark_shader_enum,
    get_active_shader_enum,
    get_custom_shaders_enum,
    Halo3ShaderManager,
)



def update_target_game(self, context):
    preset = constants.HALO_GAME_PRESETS.get(self.target_game, constants.HALO_GAME_PRESETS["HALO_3"])
    self.terrain_res_x = preset.get("default_terrain_res_x", 48)
    self.terrain_res_y = preset.get("default_terrain_res_y", 96)
    jms_v = str(preset.get("default_jms_ver", 8213))
    if jms_v in ("8200", "8210", "8213"):
        self.export_jms_version = jms_v
    ass_v = str(preset.get("default_ass_ver", 3))
    if ass_v in ("3", "5", "7"):
        self.export_ass_version = ass_v


def update_ground_shader_picker(self, context):
    if self.terrain_ground_shader_picker:
        self.terrain_shader_name = self.terrain_ground_shader_picker
        obj = context.active_object
        if obj and obj.type == 'MESH':
            Halo3ShaderManager.apply_shader_to_slot(obj.data, self.terrain_ground_shader_picker, slot_index=0)


def update_cliff_shader_picker(self, context):
    if self.terrain_cliff_shader_picker:
        self.cliff_shader_name = self.terrain_cliff_shader_picker
        obj = context.active_object
        if obj and obj.type == 'MESH':
            Halo3ShaderManager.apply_shader_to_slot(obj.data, self.terrain_cliff_shader_picker, slot_index=1)


def update_water_shader_picker(self, context):
    if self.terrain_water_shader_picker:
        val = self.terrain_water_shader_picker.strip()
        if not val.endswith("'"):
            val += "'"
        self.water_shader_name = val


def update_foliage_shader_picker(self, context):
    if self.foliage_leaf_shader_picker:
        self.tree_foliage_shader = self.foliage_leaf_shader_picker


def update_bark_shader_picker(self, context):
    if self.foliage_bark_shader_picker:
        self.tree_bark_shader = self.foliage_bark_shader_picker


def update_browser_shader_picker(self, context):
    if self.browser_active_shader_picker:
        self.shader_selected = self.browser_active_shader_picker
        obj = context.active_object
        if obj and obj.type == 'MESH':
            Halo3ShaderManager.apply_shader_to_slot(obj.data, self.browser_active_shader_picker)


def update_environment_preset(self, context):
    preset_key = self.environment_preset
    if preset_key == "CUSTOM":
        return
    preset = constants.ENVIRONMENT_PRESETS.get(preset_key)
    if not preset:
        return

    self.terrain_type = preset_key
    self.terrain_width = preset.get("width", 6000.0)
    self.terrain_length = preset.get("length", 12000.0)
    self.terrain_cliff_height = preset.get("cliff_height", 850.0)
    self.terrain_mountain_height = preset.get("mountain_height", 1200.0)
    self.terrain_floor_flatness = preset.get("floor_flatness", 0.35)
    self.terrain_cliff_steepness = preset.get("cliff_steepness", 2.2)
    self.terrain_meander = preset.get("meander", 0.15)
    self.terrain_noise_scale = 1.0 / max(1.0, preset.get("noise_scale", 200.0))
    self.terrain_noise_roughness = preset.get("noise_roughness", 35.0)
    self.terrain_water_z = preset.get("water_level_z", 0.0)
    self.terrain_gen_water = preset.get("generate_water", True)
    self.water_depth = preset.get("water_depth", 120.0)
    self.terrain_gen_caves = preset.get("generate_caves", False)
    self.terrain_cave_frequency = preset.get("cave_frequency", 0.35)
    self.terrain_cave_size = preset.get("cave_size", 240.0)

    is_reach = (self.target_game == "HALO_REACH")
    g_sh = preset.get("reach_ground" if is_reach else "h3_ground", "river riverworld_ground")
    c_sh = preset.get("reach_cliff" if is_reach else "h3_cliff", "dead deadlock_cliff_rock")
    w_sh = preset.get("reach_water" if is_reach else "h3_water", "river riverworld_water_rough'")
    s_sh = preset.get("reach_shore" if is_reach else "h3_shore", "sand sand_dune_ripples")

    self.terrain_shader_name = g_sh
    self.cliff_shader_name = c_sh
    self.water_shader_name = w_sh
    self.terrain_shore_shader = s_sh
    if preset.get("sky_preset"):
        try:
            self.sky_preset = preset.get("sky_preset")
        except Exception:
            pass


class Halo3ToolProperties(bpy.types.PropertyGroup):
    """Central configuration property group for all Halo engine prepper and generation tools."""

    # Target Game Selection
    target_game: bpy.props.EnumProperty(
        name="Target Game",
        items=[
            ("HALO_REACH", "Halo: Reach", "Highest allocation budget (64-bit engine), ASS v7, up to 4 UV channels"),
            ("HALO_3", "Halo 3 / ODST", "Standard Blam Gen 2, ASS v3, max 65,536 verts/cluster, 4 UV channels"),
            ("HALO_2", "Halo 2 (Vista / MCC)", "Medium poly Blam Gen 2, ASS v3 / JMS v8210, 2 UV channels"),
            ("HALO_CE", "Halo CE", "Strict low poly Blam Gen 1, JMS v8200, 16,384 vert ceiling, 1 UV channel"),
        ],
        default="HALO_3",
        update=update_target_game
    )

    # UI Category Switcher
    ui_category: bpy.props.EnumProperty(
        name="Category",
        items=[
            ("TERRAIN", "Terrain & Nature", "Grand landscapes, environments, caves, water, and skies", "MESH_GRID", 0),
            ("STRUCTURES", "Bases & Facilities", "Base levelroot, Blood Gulch fortress, UNSC hallways, and portals", "COMMUNITY", 1),
            ("REPAIR", "Geometry Repair & BSP", "1-Click master fix, error scanner, hole sealer, ambiguous edge splitter", "TOOL_SETTINGS", 2),
            ("SHADERS", "Shaders & UVs", "13,700+ Halo shader list picker, quick palette, and non-stretching world UVs", "MATERIAL", 3),
            ("ASSET_PREPPER", "Asset Prepper & Tags", "Editing Kit scene setup for scenery, crates, weapons, vehicles", "SCENE_DATA", 4),
            ("IMPORT_EXPORT", "Import & Export", "Import/Export .ASS, .JMS, .JMI, .JMF, .RMF, and 1-Click Tool compiler", "EXPORT", 5),
            ("RIGGING_ANIM", "Rigging & Bone Safe", "Animation-safe bone renamer (F-curves + vertex groups + constraints) & reference rigs", "ARMATURE_DATA", 6),
            ("SCALE_MODELS", "Scale Models", "Character and vehicle scale models matching Halo Blender Toolkit", "USER", 7),
            ("ABOUT_CREDITS", "About & Credits", "Credits to O7OF, c20 Reclaimers Wiki, and Bungie", "INFO", 8),
            ("ALL", "All Panels", "Show all tool panels simultaneously", "PREFERENCES", 9),
        ],
        default="TERRAIN"
    )

    # Grand Environment Presets
    environment_preset: bpy.props.EnumProperty(
        name="Environment Preset",
        items=[
            ("GRASSY_VALLEY", "Grassy Valley & Savanna (The Ark / Sierra 117 / Valhalla)", "Expansive rolling hills, winding rivers, granite bluffs"),
            ("DESERT_CANYON", "Desert Dunes & Mesa Canyons (The Ark / Sandtrap / Forge World)", "Undulating desert dunes with sharp crests and sheer sandstone bluffs"),
            ("SNOWY_MOUNTAIN", "Snowy Alpine & Glacier (Assault on the Control Room / Two Betrayals)", "Monumental glacial canyons flanked by sheer razorback Alpine peaks"),
            ("ROCKY_BADLANDS", "Rocky Badlands & Monoliths (Tsavo Highway / Boneyard)", "Stepped sedimentary rock benches, scree slopes, and monolithic pillars"),
            ("ISLAND_ARCHIPELAGO", "Ocean Atolls & Tropical Bluffs (The Covenant / Silent Cartographer)", "Towering volcanic atolls, coastal sand beach shelves, surrounded by deep ocean"),
            ("VOLCANIC_CRATER", "Basalt Caldera & Ash Plains", "Circular basalt caldera rim with stepped lava shelves and volcanic tubes"),
            ("SWAMP_LOWLANDS", "Murky Wetlands & Bayous (343 Guilty Spark)", "Low mist-shrouded wetlands with shallow murky pools and cypress knolls"),
            ("FORERUNNER_CHASM", "Monumental Chasm Trench (Two Betrayals / Construct)", "Kilometer-deep stepped bedrock canyon flanked by sheer brutalist mountain walls"),
            ("CUSTOM", "Custom / Freehand", "Fully custom parameter controls"),
        ],
        default="GRASSY_VALLEY",
        update=update_environment_preset
    )

    # Terrain Type / Classic Layouts
    terrain_type: bpy.props.EnumProperty(
        name="Terrain Type / Layout",
        items=[
            ("GRASSY_VALLEY", "Grassy Valley & Savanna (The Ark / Sierra 117)", "Expansive rolling hills, winding rivers, granite bluffs"),
            ("DESERT_CANYON", "Desert Dunes & Mesa Canyons (The Ark / Sandtrap)", "Undulating desert dunes with sharp crests and sheer sandstone bluffs"),
            ("SNOWY_MOUNTAIN", "Snowy Alpine & Glacier (Assault on the Control Room)", "Monumental glacial canyons flanked by sheer razorback Alpine peaks"),
            ("ROCKY_BADLANDS", "Rocky Badlands & Monoliths (Tsavo Highway / Boneyard)", "Stepped sedimentary rock benches, scree slopes, and monolithic pillars"),
            ("ISLAND_ARCHIPELAGO", "Ocean Atolls & Tropical Bluffs (The Covenant)", "Towering volcanic atolls, coastal sand beach shelves, surrounded by deep ocean"),
            ("VOLCANIC_CRATER", "Basalt Caldera & Ash Plains", "Circular basalt caldera rim with stepped lava shelves and volcanic tubes"),
            ("SWAMP_LOWLANDS", "Murky Wetlands & Bayous (343 Guilty Spark)", "Low mist-shrouded wetlands with shallow murky pools and cypress knolls"),
            ("FORERUNNER_CHASM", "Monumental Chasm Trench (Two Betrayals / Construct)", "Kilometer-deep stepped bedrock canyon flanked by sheer brutalist mountain walls"),
            ("BLOOD_GULCH", "Blood Gulch (Classic Red vs Blue Canyon)", "Wide Box Canyon with natural red bluffs"),
            ("VALHALLA", "Valhalla (Nordic River Valley)", "Sprawling river basin flanked by steep stepped crags"),
            ("VALHALLA_COAST", "Valhalla Coast (River Valley to Ocean Beach)", "Expansive river valley ending in coastal ocean beach with water volume"),
            ("HORSESHOE_VALLEY", "Horseshoe Valley (U-Shaped Bifurcated Basin)", "Continuous U-shaped valley looping around a central towering mountain ridge"),
            ("FORGE_WORLD_CANYON", "Forge World Canyon (Alpine High-Poly)", "High-poly canyon inspired by Halo: Reach Forge World"),
            ("SIERRA_VALLEY", "Sierra 117 Valley (Jungle River Chasm)", "Steep winding jungle river gorge"),
            ("WINDING_VALLEY", "Winding Valley (S-Bend Chokepoint)", "Multi-bend tactical corridor with natural arch chokepoint"),
            ("WINDY_GULCH", "Windy Gulch (Sharply Meandering Ravine)", "High-frequency meandering canyon with razor edges"),
            ("BATTLE_CREEK", "Battle Creek (Compact Canyon Stream)", "Tight tactical stream canyon"),
            ("STANDOFF", "Standoff (Circular Crater Basin)", "Bowl-shaped perimeter with defensive rim"),
            ("SANDTRAP", "Sandtrap (Wide Open Dune Basin)", "Vast expansive desert sand bowl"),
            ("MESA_PLATEAU", "Mesa Plateau (Elevated Flat Top)", "Steep cliff walls rising to flat fighting plateaus"),
            ("MOUNTAIN_PASS", "Mountain Pass (Narrow Alpine Gorge)", "Steep sheer rock walls with narrow road bed"),
            ("BIG_ISLAND", "Big Island (Surrounded by Ocean Water)", "Central landmass surrounded by water planes"),
            ("FLATBOX_ISLAND", "Flatbox Island (Custom Box Canvas)", "Flat fighting canvas with ocean perimeter"),
        ],
        default="GRASSY_VALLEY"
    )

    # Algorithmic Terrain Generators & Physical Erosion
    terrain_algorithm: bpy.props.EnumProperty(
        name="Terrain Algorithm",
        items=constants.TERRAIN_ALGORITHMS,
        default="HYBRID_ADVANCED",
        description="Procedural mathematical and physical simulation algorithm (fBm, Domain Warp, Diamond-Square, Voronoi, Ridged Multifractal)"
    )
    terrain_apply_hydraulic_erosion: bpy.props.BoolProperty(
        name="Hydraulic Erosion",
        default=True,
        description="Simulates virtual rainfall, downhill sediment transport, and alluvial fans"
    )
    terrain_hydraulic_droplets: bpy.props.IntProperty(
        name="Rainfall Droplets",
        default=8000,
        min=500,
        max=50000
    )
    terrain_apply_thermal_erosion: bpy.props.BoolProperty(
        name="Thermal Erosion",
        default=True,
        description="Simulates rockfall weathering along slopes exceeding angle of repose"
    )
    terrain_thermal_iterations: bpy.props.IntProperty(
        name="Thermal Passes",
        default=4,
        min=1,
        max=20
    )
    terrain_thermal_talus_angle: bpy.props.FloatProperty(
        name="Talus Angle (°)",
        default=38.0,
        min=15.0,
        max=60.0
    )

    # Topology
    terrain_topology: bpy.props.EnumProperty(
        name="Mesh Topology",
        items=[
            ("HEX_TRIANGULAR", "Hexagonal Triangular Lattice", "Staggered isotropic triangular grid formed from regular hexagons"),
            ("ISOTROPIC_TRI", "Isotropic Triangulated", "Alternating diagonal quad triangulation"),
            ("QUAD_GRID", "Classic Quad Grid", "Standard rectangular quad grid"),
        ],
        default="HEX_TRIANGULAR"
    )

    # Terrain Dimensions & Noise Parameters (Full User Modification)
    terrain_width: bpy.props.FloatProperty(name="Width", default=6000.0, min=200.0, max=48000.0)

    terrain_length: bpy.props.FloatProperty(name="Length", default=12000.0, min=400.0, max=48000.0)
    terrain_cliff_height: bpy.props.FloatProperty(name="Cliff Height", default=850.0, min=50.0, max=8000.0)
    terrain_mountain_height: bpy.props.FloatProperty(name="Mountain Height", default=1400.0, min=50.0, max=16000.0, description="Height of flanking Alpine mountain peaks")
    terrain_mountain_ridge_sharpness: bpy.props.FloatProperty(name="Ridge Sharpness", default=2.2, min=1.0, max=5.0, description="Multifractal power curve for sharp Alpine mountain arêtes")
    terrain_floor_flatness: bpy.props.FloatProperty(name="Floor Width %", default=0.35, min=0.05, max=0.90)
    terrain_cliff_steepness: bpy.props.FloatProperty(name="Steepness", default=2.2, min=1.0, max=6.0)
    terrain_meander: bpy.props.FloatProperty(name="Meander S-Curve", default=0.15, min=0.0, max=0.60)
    terrain_noise_roughness: bpy.props.FloatProperty(name="Noise Amplitude", default=35.0, min=0.0, max=300.0)
    terrain_noise_scale: bpy.props.FloatProperty(name="Noise Frequency", default=0.003, min=0.0001, max=0.05)
    terrain_res_x: bpy.props.IntProperty(name="Res X", default=64, min=8, max=512)
    terrain_res_y: bpy.props.IntProperty(name="Res Y", default=128, min=8, max=1024)
    terrain_mountain_crest_rounding: bpy.props.FloatProperty(
        name="Mountain Crest Pos",
        default=0.80,
        min=0.50,
        max=0.98,
        description="Profile distance (0.50-0.98) where mountain wall peaks and organically rounds over into summit shoulder"
    )
    terrain_crest_falloff: bpy.props.FloatProperty(
        name="Summit Shoulder Roll-off",
        default=0.12,
        min=0.0,
        max=0.40,
        description="Roll-off drop percentage (0.0-0.40) of mountain shoulder past crest before boundary"
    )
    terrain_smooth_cliff_tops: bpy.props.BoolProperty(
        name="Smooth Crest & Rim",
        default=True,
        description="Attenuates harsh stepped tiers and high-frequency noise near mountain crests to eliminate jagged teeth"
    )
    terrain_smooth_passes: bpy.props.IntProperty(
        name="Smooth Passes",
        default=4,
        min=1,
        max=20,
        description="Number of Laplacian relaxation iterations"
    )
    terrain_smooth_strength: bpy.props.FloatProperty(
        name="Smooth Strength",
        default=0.50,
        min=0.05,
        max=1.0,
        description="Laplacian smoothing blending factor"
    )

    # Sealing & BSP Boundaries
    terrain_seal_mode: bpy.props.EnumProperty(
        name="Sealing Mode",
        items=[
            ("SEALED_ROOM", "Sealed Room (+sky walls & ceiling)", "Enclosed watertight room with +sky ceiling"),
            ("SEALED_BLOCK", "Sealed Solid Block", "Solid terrain slab with vertical skirt down to flat base"),
            ("OPEN_SHEET", "Open Terrain Sheet", "Open heightmap grid"),
        ],
        default="SEALED_ROOM"
    )
    terrain_ceiling_z: bpy.props.FloatProperty(name="Sky Ceiling Z", default=1800.0, min=100.0, max=16000.0)
    terrain_uv_scale: bpy.props.FloatProperty(name="World UV Scale", default=150.0, min=20.0, max=2000.0)
    terrain_flip_uv_v: bpy.props.BoolProperty(name="Flip UV V-Axis", default=False)
    terrain_uv_fit_box: bpy.props.BoolProperty(name="Fit UV Box 1:1", default=True, description="Scale mesh UVs to fit 1:1 in UV box to eliminate repeating textures")

    # 3D Natural Cave Formations
    terrain_gen_caves: bpy.props.BoolProperty(name="Generate 3D Caves", default=False, description="Sculpt authentic 3D subterranean and mountain cave tunnels with airtight portals")
    terrain_cave_size: bpy.props.FloatProperty(name="Cave Tunnel Diameter", default=240.0, min=40.0, max=1200.0)
    terrain_cave_frequency: bpy.props.FloatProperty(name="Cave Tunnel Density", default=0.35, min=0.05, max=1.0)
    terrain_cave_roughness: bpy.props.FloatProperty(name="Cavern Wall Roughness", default=35.0, min=0.0, max=150.0)

    # Water Body & Immersion Physics
    terrain_gen_water: bpy.props.BoolProperty(name="Generate Water Surface & Volume", default=True, description="Creates dual water: visible top-facing render plane (') for Sapien and 3D immersion volume (~water) for physics")
    terrain_water_z: bpy.props.FloatProperty(name="Water Level Z", default=0.0)
    water_depth: bpy.props.FloatProperty(name="Water Depth", default=120.0, min=10.0, max=2000.0)

    # Dynamic List Pickers for Shaders (No Typing Required!)
    terrain_ground_shader_picker: bpy.props.EnumProperty(
        name="Ground Shader",
        items=get_ground_shader_enum,
        update=update_ground_shader_picker,
        description="Select ground shader from authentic Halo tag catalog"
    )
    terrain_cliff_shader_picker: bpy.props.EnumProperty(
        name="Cliff Shader",
        items=get_cliff_shader_enum,
        update=update_cliff_shader_picker,
        description="Select cliff / rock shader from authentic Halo tag catalog"
    )
    terrain_water_shader_picker: bpy.props.EnumProperty(
        name="Water Shader",
        items=get_water_shader_enum,
        update=update_water_shader_picker,
        description="Select water shader from authentic Halo tag catalog"
    )
    foliage_leaf_shader_picker: bpy.props.EnumProperty(
        name="Foliage Shader",
        items=get_foliage_shader_enum,
        update=update_foliage_shader_picker,
        description="Select leaf / needle shader from authentic Halo tag catalog"
    )
    foliage_bark_shader_picker: bpy.props.EnumProperty(
        name="Bark Shader",
        items=get_bark_shader_enum,
        update=update_bark_shader_picker,
        description="Select bark / wood shader from authentic Halo tag catalog"
    )
    browser_active_shader_picker: bpy.props.EnumProperty(
        name="Active Shader",
        items=get_active_shader_enum,
        update=update_browser_shader_picker,
        description="Active shader picked from browser"
    )

    # String properties synced with pickers (maintains backwards compatibility)
    terrain_shader_name: bpy.props.StringProperty(name="Ground Shader Name", default="river riverworld_ground")
    cliff_shader_name: bpy.props.StringProperty(name="Cliff Shader Name", default="dead deadlock_cliff_rock")
    water_shader_name: bpy.props.StringProperty(name="Water Shader Name", default="river riverworld_water_rough'")
    terrain_shore_shader: bpy.props.StringProperty(name="Shore Shader", default="sand sand_dune_ripples")
    terrain_mid_shader: bpy.props.StringProperty(name="Mid-Slope Shader", default="out out_dirt")
    terrain_mid_slope_angle: bpy.props.FloatProperty(name="Mid Slope Angle", default=20.0, min=5.0, max=60.0)
    terrain_cliff_slope_angle: bpy.props.FloatProperty(name="Cliff Slope Angle", default=45.0, min=15.0, max=85.0)

    # Terrain Toggles
    terrain_gen_foliage: bpy.props.BoolProperty(name="Include Trees & Foliage", default=True)
    terrain_gen_rocks: bpy.props.BoolProperty(name="Include Cover Rocks", default=True)
    terrain_gen_portals: bpy.props.BoolProperty(name="Auto-Generate Airtight Portals", default=True)
    terrain_seed: bpy.props.IntProperty(name="Terrain Seed", default=1337)
    terrain_material_mode: bpy.props.EnumProperty(
        name="Material Blending",
        items=[
            ("UNIFIED_BLENDED", "Unified Blended (.shader_terrain)", "Single shader with Terrain_Layers vertex colors"),
            ("MULTI_SLOT", "Multi-Material Slots", "Separate materials per slope angle"),
        ],
        default="MULTI_SLOT"
    )
    terrain_use_material_layering: bpy.props.BoolProperty(name="5-Slot Material Layering", default=False)
    terrain_type: bpy.props.StringProperty(name="Valley Preset Alias", default="GRASSY_VALLEY")
    terrain_horseshoe_passage: bpy.props.EnumProperty(
        name="Horseshoe Passage",
        items=[
            ("NATURAL_ARCH", "Natural Rock Arch", "Arch chokepoint through ridge"),
            ("TUNNEL", "Subterranean Tunnel", "Airtight cavern through ridge"),
            ("OPEN_CUT", "Open Pass", "Carved road cut"),
        ],
        default="NATURAL_ARCH"
    )
    terrain_place_bases: bpy.props.BoolProperty(name="Place Bases", default=False)

    # Rock Generation Properties
    rock_archetype: bpy.props.EnumProperty(
        name="Rock Archetype",
        items=[("RANDOM", "Random Archetype", "Randomly pick from geologic archetypes")] + constants.ROCK_ARCHETYPES,
        default="RANDOM"
    )
    rock_size_tier: bpy.props.EnumProperty(
        name="Size Tier",
        items=[
            ("HUGE", "Huge Monolith (1200-2800)", "Massive canyon wall anchors and monoliths"),
            ("MEDIUM", "Medium Boulder (400-900)", "Strategic cover boulders and combat points"),
            ("SMALL", "Small Rock (120-300)", "Mid-scale scatter rocks and trail barriers"),
            ("MICRO", "Micro Scree (25-80)", "Cliff foot talus skirts and scree slope piles"),
        ],
        default="MEDIUM"
    )
    rock_count_huge: bpy.props.IntProperty(name="Huge Monoliths", default=2, min=0, max=10)
    rock_count_medium: bpy.props.IntProperty(name="Medium Boulders", default=6, min=0, max=30)
    rock_count_small: bpy.props.IntProperty(name="Small Rocks", default=14, min=0, max=60)
    rock_count_micro: bpy.props.IntProperty(name="Micro Scree", default=20, min=0, max=100)
    rock_seed: bpy.props.IntProperty(name="Rock Seed", default=4242)
    rock_clearance: bpy.props.FloatProperty(name="Clearance Distance", default=50.0, min=10.0, max=300.0)
    scatter_spread_scalar: bpy.props.FloatProperty(name="Spread Scalar", default=1.8, min=0.5, max=5.0)

    # Foliage Generation Properties
    tree_type: bpy.props.EnumProperty(
        name="Foliage Archetype",
        items=constants.FOLIAGE_ARCHETYPES,
        default="CONIFER_PINE"
    )

    foliage_size_tier: bpy.props.EnumProperty(
        name="Size Tier",
        items=[
            ("OLD_GROWTH", "Old-Growth Giant (160%-240%)", "Towering ancient canopy giants"),
            ("MATURE", "Mature Tree (100%-140%)", "Standard adult trees forming the main forest body"),
            ("MEDIUM", "Medium Tree (65%-95%)", "Sub-canopy trees filling intermediate groves"),
            ("SAPLING", "Young Sapling (35%-60%)", "Juvenile trees near clearings and trails"),
            ("SHRUB", "Undergrowth Shrub (15%-30%)", "Low foliage bushes, ferns, and ground skirts"),
        ],
        default="MATURE"
    )
    foliage_scatter_count: bpy.props.IntProperty(name="Scatter Count", default=30, min=1, max=300)
    foliage_seed: bpy.props.IntProperty(name="Foliage Seed", default=8888)
    foliage_min_dist: bpy.props.FloatProperty(name="Min Spacing", default=35.0, min=5.0, max=200.0)
    foliage_min_distance: bpy.props.FloatProperty(name="Min Spacing", default=35.0, min=5.0, max=200.0)
    foliage_spread_scalar: bpy.props.FloatProperty(name="Spread", default=1.8, min=0.5, max=5.0)
    tree_foliage_shader: bpy.props.StringProperty(name="Foliage Shader", default="river riverworld_tree_leafa")
    tree_bark_shader: bpy.props.StringProperty(name="Bark Shader", default="river riverworld_tree_bark")
    foliage_scatter_mode: bpy.props.EnumProperty(
        name="Scatter Mode",
        items=[
            ("REPLACE", "Replace Existing", "Clears previous foliage in collection before scattering"),
            ("APPEND", "Append (Coexist)", "Adds new foliage alongside existing trees and bushes"),
        ],
        default="REPLACE"
    )

    # Skybox Properties
    sky_preset: bpy.props.EnumProperty(
        name="Skybox Preset",
        items=[
            ("BLOOD_GULCH", "Blood Gulch (Azure & Ring)", "Classic azure sky with overhead Halo ring arch"),
            ("VALHALLA", "Valhalla (Nordic Alpine)", "Nordic mountain atmosphere with ring slice"),
            ("SANDTRAP", "Sandtrap (Desert Solar Haze)", "Golden desert atmosphere with intense solar glare"),
            ("FORGE_WORLD", "Forge World (Reach Alpine)", "Towering alpine mountain Reach sky"),
            ("LONG_NIGHT_OF_SOLACE", "High Orbit (Deep Space)", "Deep space starfield and Reach planetary horizon"),
            ("HIGH_GROUND", "High Ground (Coastal Dawn)", "Coastal dawn sky with soft morning sun"),
            ("NEW_ALEXANDRIA", "New Alexandria (Dusk Sunset)", "Metropolis dusk twilight with burning horizon"),
            ("HALO_CELESTIAL", "Halo Celestial (Interior Curve)", "Dramatic interior curvature of the Halo Installation"),
        ],
        default="BLOOD_GULCH"
    )

    # Bunker & Hallways
    bunker_radius: bpy.props.FloatProperty(name="Bunker Radius", default=140.0, min=40.0, max=1000.0)
    bunker_height: bpy.props.FloatProperty(name="Bunker Height", default=85.0, min=20.0, max=500.0)
    bunker_door_h: bpy.props.FloatProperty(name="Door Height", default=65.0, min=20.0, max=300.0)
    bunker_ramp_w: bpy.props.FloatProperty(name="Ramp Width", default=40.0, min=10.0, max=200.0)
    bunker_ramp_style: bpy.props.EnumProperty(
        name="Ramps",
        items=[
            ("INTEGRATED", "Integrated Side Ramps", "Side ramps leading to roof deck"),
            ("NONE", "None (Flush Walls)", "Flush exterior walls without ramps"),
        ],
        default="INTEGRATED"
    )
    bunker_include_splitter: bpy.props.BoolProperty(name="Center Splitter Wall", default=True)

    hallway_layout_mode: bpy.props.EnumProperty(
        name="Layout Mode",
        items=[
            ("STRAIGHT", "Straight Corridor", "Modular straight hallway"),
            ("ELBOW_TURN", "Elbow Turn", "90-degree hallway turn"),
            ("CROSS_INTERSECTION", "Cross Intersection", "4-way intersection"),
            ("UNSC_ROOM", "UNSC Room", "Modular enclosed room"),
            ("LAYOUT_CHAIN", "Layout Chain", "Connected corridor and facility room"),
        ],
        default="STRAIGHT"
    )
    hallway_width: bpy.props.FloatProperty(name="Corridor Width", default=140.0, min=40.0, max=1000.0)
    hallway_height: bpy.props.FloatProperty(name="Corridor Height", default=110.0, min=40.0, max=800.0)
    hallway_length: bpy.props.FloatProperty(name="Corridor Length", default=240.0, min=40.0, max=5000.0)
    hallway_turns: bpy.props.IntProperty(name="Turns", default=2, min=1, max=10)
    hallway_include_doorways: bpy.props.BoolProperty(name="Include Doorways", default=True)
    hallway_include_room: bpy.props.BoolProperty(name="Include Room", default=True)
    hallway_room_w: bpy.props.FloatProperty(name="Room Width", default=260.0, min=60.0, max=3000.0)
    hallway_room_l: bpy.props.FloatProperty(name="Room Length", default=220.0, min=60.0, max=3000.0)
    hallway_room_h: bpy.props.FloatProperty(name="Room Height", default=130.0, min=60.0, max=2000.0)

    # Geometry Diagnostics & Repair
    bsp_weld_dist: bpy.props.FloatProperty(name="Weld Dist", default=0.08, min=0.0, max=1.0)
    bsp_remove_duplicates: bpy.props.BoolProperty(name="Remove Duplicates", default=True)
    bsp_fix_ambiguous: bpy.props.BoolProperty(name="Fix Ambiguous Edges", default=True)
    bsp_remove_degenerate: bpy.props.BoolProperty(name="Remove Degenerate", default=True)
    bsp_collapse_short: bpy.props.BoolProperty(name="Collapse Short Edges", default=True)
    bsp_dissolve_coplanar: bpy.props.BoolProperty(name="Dissolve Coplanar", default=False)
    bsp_seal_holes: bpy.props.BoolProperty(name="Seal Open Holes", default=False)
    bsp_recalc_normals: bpy.props.BoolProperty(name="Recalculate Normals", default=True)
    diag_scope: bpy.props.EnumProperty(
        name="Scan & Repair Scope",
        items=[
            ("ALL", "All Scene Meshes", "Repair / scan all mesh objects in the scene"),
            ("SELECTED", "Selected Objects Only", "Repair / scan only selected mesh objects"),
        ],
        default="ALL"
    )
    diag_normals_dir: bpy.props.EnumProperty(
        name="Normals Direction",
        items=[
            ("INWARD", "Inward (Sealed Halo BSP)", "Points normals inward for enclosed indoor/skyroom BSPs"),
            ("OUTWARD", "Outward (Props & Objects)", "Points normals outward for scenery, crates, and props"),
        ],
        default="INWARD"
    )
    auto_fix_shaders: bpy.props.BoolProperty(
        name="Auto-Fix Shaders",
        default=False,
        description="Automatically verifies and fixes invalid shaders and materials during geometry repair"
    )
    diag_coplanar_angle: bpy.props.FloatProperty(name="Coplanar Angle (°)", default=1.0, min=0.03, max=5.0)
    diag_total_open: bpy.props.IntProperty(default=0)
    diag_total_ambiguous: bpy.props.IntProperty(default=0)
    diag_total_degenerate: bpy.props.IntProperty(default=0)
    diag_total_degen: bpy.props.IntProperty(default=0)
    diag_total_duplicate_objects: bpy.props.IntProperty(default=0)
    diag_total_duplicate: bpy.props.IntProperty(default=0)
    diag_total_short: bpy.props.IntProperty(default=0)
    diag_total_missing_mat: bpy.props.IntProperty(default=0)
    diag_total_coplanar: bpy.props.IntProperty(default=0)
    diag_total_bounds: bpy.props.IntProperty(default=0)
    diag_has_comparison: bpy.props.BoolProperty(default=False)
    diag_fixed_dup: bpy.props.IntProperty(default=0)
    diag_fixed_degen: bpy.props.IntProperty(default=0)
    diag_fixed_mat: bpy.props.IntProperty(default=0)

    # Shaders Browser Filter & Clean Categorization
    shader_search_filter: bpy.props.StringProperty(name="Search", default="")
    shader_category_filter: bpy.props.EnumProperty(
        name="Category Filter",
        items=constants.SHADER_CATEGORIES + [("CUSTOM", "Custom User Shaders", "User modified and saved shaders")],
        default="ALL"
    )
    shader_selected: bpy.props.StringProperty(name="Selected Shader", default="river riverworld_ground")

    # Custom Shader Palette & Modification Controls
    selected_custom_shader: bpy.props.EnumProperty(
        name="Saved Custom Shaders",
        items=get_custom_shaders_enum,
        description="Select from persistent saved custom shaders"
    )
    custom_shader_name: bpy.props.StringProperty(
        name="Custom Shader Name",
        default="Custom_Shader_01",
        description="Name for new custom shader tag"
    )
    custom_shader_tint: bpy.props.FloatVectorProperty(
        name="Tint Color",
        subtype='COLOR',
        size=4,
        default=(1.0, 1.0, 1.0, 1.0),
        min=0.0,
        max=1.0,
        description="Color multiplier tint applied to base shader"
    )
    custom_shader_roughness: bpy.props.FloatProperty(
        name="Roughness",
        default=0.85,
        min=0.0,
        max=1.0,
        description="Surface roughness"
    )
    custom_shader_metallic: bpy.props.FloatProperty(
        name="Metallic",
        default=0.0,
        min=0.0,
        max=1.0,
        description="Metallic reflectiveness"
    )
    custom_shader_bump: bpy.props.FloatProperty(
        name="Bump Strength",
        default=0.35,
        min=0.0,
        max=2.0,
        description="Normal bump intensity"
    )
    custom_shader_uv_scale: bpy.props.FloatVectorProperty(
        name="UV Scale",
        size=3,
        default=(1.0, 1.0, 1.0),
        description="1:1 UV Mapping scale factor"
    )


    # 3D Viewport Shader Brush & Painter Properties
    shader_brush_mode: bpy.props.EnumProperty(
        name="Brush Mode",
        items=[
            ("SHADERS", "Shader Tags", "Paint full authentic Halo shader tags directly onto faces"),
            ("BLEND_LAYERS", "Blend Layers", "Paint multi-texture terrain vertex color weights (Terrain_Layers)"),
        ],
        default="SHADERS"
    )
    shader_brush_radius: bpy.props.FloatProperty(name="Brush Radius", default=200.0, min=5.0, max=3000.0)
    shader_brush_strength: bpy.props.FloatProperty(name="Brush Strength", default=1.0, min=0.0, max=1.0)
    shader_brush_blend_layer: bpy.props.EnumProperty(
        name="Blend Layer",
        items=[
            ("LAYER_1", "Base Layer (Red)", "Ground / primary terrain texture"),
            ("LAYER_2", "Mid Layer (Green)", "Cliff / steep rock slope layer"),
            ("LAYER_3", "Accent Layer (Blue)", "Grass / detail accent layer"),
            ("LAYER_4", "Erosion Layer (Alpha)", "Wet dirt / shore erosion layer"),
        ],
        default="LAYER_1"
    )
    shader_brush_fit_uv: bpy.props.BoolProperty(name="Fit UV 1:1 to Geometry", default=True, description="Aligns UVs seamlessly without repetition")
    shader_game_filter: bpy.props.EnumProperty(
        name="Game Filter",
        items=[
            ("ALL", "All Games (13,763+)", "All authentic Halo shaders"),
            ("HALO_3", "Halo 3 / ODST", "Halo 3 multiplayer and solo shaders"),
            ("HALO_REACH", "Halo: Reach", "Halo: Reach shaders and Forge World textures"),
        ],
        default="ALL"
    )

    # Compatibility Aliases for Scripts & Test Suite
    terrain_generate_water: bpy.props.BoolProperty(name="Generate Water", default=True)
    terrain_scatter_rocks: bpy.props.BoolProperty(name="Scatter Rocks", default=True)
    mountain_height: bpy.props.FloatProperty(name="Mountain Height", default=1200.0, min=0.0, max=10000.0)
    mountain_ridge_sharpness: bpy.props.FloatProperty(name="Ridge Sharpness", default=2.2, min=0.5, max=10.0)
    generate_caves: bpy.props.BoolProperty(name="Generate Caves", default=False)
    cave_size: bpy.props.FloatProperty(name="Cave Size", default=240.0, min=20.0, max=1200.0)
    cave_roughness: bpy.props.FloatProperty(name="Cave Roughness", default=35.0, min=0.0, max=150.0)
    bunker_cover_h: bpy.props.FloatProperty(name="Cover Height", default=40.0, min=10.0, max=200.0)
    hallway_intersections: bpy.props.IntProperty(name="Intersections", default=1, min=0, max=5)
    rock_type: bpy.props.StringProperty(name="Rock Type", default="FACETED_BOULDER")
    rock_shader_name: bpy.props.StringProperty(name="Rock Shader Name", default="dead deadlock_cliff_rock")
    rock_size_x: bpy.props.FloatProperty(name="Rock Size X", default=120.0)
    rock_size_y: bpy.props.FloatProperty(name="Rock Size Y", default=100.0)
    rock_size_z: bpy.props.FloatProperty(name="Rock Size Z", default=90.0)
    rock_subdivs: bpy.props.IntProperty(name="Rock Subdivisions", default=2)
    rock_smooth_passes: bpy.props.IntProperty(name="Rock Smooth Passes", default=2)
    rock_flatten_base: bpy.props.BoolProperty(name="Flatten Base", default=True)
    rock_tunnel_w: bpy.props.FloatProperty(name="Arch Width", default=60.0)
    rock_tunnel_h: bpy.props.FloatProperty(name="Arch Height", default=80.0)
    scatter_passes: bpy.props.IntProperty(name="Scatter Passes", default=3, min=1, max=5)
    scatter_huge_count: bpy.props.IntProperty(name="Huge Count", default=6, min=0, max=50)
    scatter_med_count: bpy.props.IntProperty(name="Medium Count", default=12, min=0, max=100)
    scatter_small_count: bpy.props.IntProperty(name="Small Count", default=20, min=0, max=200)
    scatter_min_distance: bpy.props.FloatProperty(name="Min Distance", default=35.0, min=5.0, max=200.0)
    portal_y_pos: bpy.props.FloatProperty(name="Portal Y Position", default=0.0)
    portal_width: bpy.props.FloatProperty(name="Portal Width", default=400.0, min=10.0, max=5000.0)
    portal_height: bpy.props.FloatProperty(name="Portal Height", default=200.0, min=10.0, max=3000.0)
    portal_z_bottom: bpy.props.FloatProperty(name="Portal Z Bottom", default=0.0)
    portal_z_top: bpy.props.FloatProperty(name="Portal Z Top", default=300.0)
    portal_type: bpy.props.EnumProperty(
        name="Portal Type",
        items=[
            ("PLANAR_QUAD", "Planar Quad", "Single planar polygon quad"),
            ("CHOKEPOINT_ENCLOSURE", "Chokepoint Enclosure", "Archway and planar portal"),
            ("VALLEY_CHOKEPOINT", "Valley Chokepoint", "Archway and planar portal fitted to valley cross-section"),
        ],
        default="PLANAR_QUAD"
    )
    portal_shader_name: bpy.props.StringProperty(name="Portal Shader", default="+portal")
    sky_height: bpy.props.FloatProperty(name="Sky Height", default=1800.0, min=100.0, max=20000.0)
    sky_type: bpy.props.EnumProperty(
        name="Sky Type",
        items=[
            ("DOME", "Celestial Dome", "Hemispherical sky dome"),
            ("BOX", "Sky Box", "Airtight cubic sky box"),
        ],
        default="DOME"
    )
    sky_radius: bpy.props.FloatProperty(name="Sky Radius", default=2500.0, min=200.0, max=50000.0)
    sky_seal_base: bpy.props.BoolProperty(name="Seal Sky Base", default=True)
    sky_auto_fit: bpy.props.BoolProperty(name="Auto Fit to Terrain", default=True)

    # Import / Export & Compilation Properties
    export_asset_type: bpy.props.EnumProperty(
        name="Asset Type",
        items=constants.ASSET_TYPES,
        default="MAP_STRUCTURE",
        description="Choose whether compiling a full map structure BSP or an object"
    )
    export_format: bpy.props.EnumProperty(
        name="Export Format",
        items=[
            ("GR2", ".GR2 (Halo: Reach)", "Halo: Reach native geometry format via tool.exe fbx-to-gr2"),
            ("ASS", ".ASS (Amalgam Scene)", "Halo 2, 3, and Reach structure and render models"),
            ("JMS", ".JMS (Jointed Model Skeleton)", "Halo CE, 2, 3 models, collision, and physics"),
            ("JMI", ".JMI (Jointed Model Instance)", "Jointed model instances and particle models"),
        ],
        default="ASS"
    )

    export_sub_folder: bpy.props.StringProperty(name="Subfolder", default="scenery")
    object_compile_mode: bpy.props.EnumProperty(
        name="Compile Mode",
        items=[
            ("RENDER_ALL", "Render + Collision + Physics", "Compiles render model, collision model, and physics model"),
            ("RENDER_ONLY", "Render Model Only", "Compiles only the visible render model"),
            ("COLLISION_ONLY", "Collision Model Only", "Compiles only the collision model (.collision_model)"),
            ("PHYSICS_ONLY", "Physics Model Only", "Compiles only the physics model (.physics_model)"),
        ],
        default="RENDER_ALL"
    )
    map_name: bpy.props.StringProperty(name="Asset / Map Name", default="custom_map")
    map_scenario_type: bpy.props.EnumProperty(
        name="Scenario Type",
        items=[
            ("multi", "Multiplayer (multi)", "Multiplayer map (data/levels/multi/...)"),
            ("solo", "Solo / Campaign (solo)", "Singleplayer campaign mission (data/levels/solo/...)"),
            ("firefight", "Firefight (firefight)", "Firefight survival scenario (data/levels/firefight/...)"),
        ],
        default="multi"
    )
    editing_kit_path: bpy.props.StringProperty(
        name="Editing Kit Path",
        default=r"C:\Program Files (x86)\Steam\steamapps\common\H3EK",
        subtype='DIR_PATH'
    )
    tool_fast_mode: bpy.props.BoolProperty(name="Use tool_fast.exe", default=True)
    auto_repair_before_export: bpy.props.BoolProperty(name="Auto-Repair Before Export", default=True)
    last_compile_status: bpy.props.StringProperty(name="Status", default="Not Run")
    last_compile_output: bpy.props.StringProperty(name="Tool Log", default="")
    last_export_filepath: bpy.props.StringProperty(name="Last Export File", default="")
    last_tags_directory: bpy.props.StringProperty(name="Last Tags Directory", default="")
    export_ass_version: bpy.props.EnumProperty(
        name="ASS Version",
        items=[
            ("3", "Version 3 (Halo 3 / ODST)", "Standard Halo 3 ASS format"),
            ("5", "Version 5 (Halo 3 / Reach)", "Halo 3 3D UV format"),
            ("7", "Version 7 (Halo: Reach)", "Halo: Reach with vertex colors and multi-UVs"),
        ],
        default="3"
    )
    export_jms_version: bpy.props.EnumProperty(
        name="JMS Version",
        items=[
            ("8200", "v8200 (Halo CE)", "Halo 1 CE Editing Kit model format"),
            ("8210", "v8210 (Halo 2)", "Halo 2 Editing Kit format"),
            ("8213", "v8213 (Halo 3 / Reach)", "Halo 3 & Reach JMS format"),
        ],
        default="8213"
    )
    export_use_toolkit: bpy.props.BoolProperty(name="Prefer Installed Toolkit", default=True)
    export_use_native: bpy.props.BoolProperty(name="Force Standalone Exporter", default=False)

    # Rigging & Bone Safe Properties
    bone_rename_target_style: bpy.props.EnumProperty(
        name="Naming Style",
        items=[
            ("BUNGIE_CANON", "Bungie Canon (b_root, b_pelvis)", "Standard Halo engine bone hierarchy"),
            ("MAX_BIP01", "3ds Max (bip01, bip01 pelvis)", "Standard 3ds Max Character Studio biped"),
            ("HALO_SIDE", "Halo Side (root, pelvis, l clavicle)", "Halo side nomenclature"),
            ("BLENDER_LR", "Blender .L/.R (root, pelvis, clavicle.L)", "Standard Blender symmetry"),
        ],
        default="BUNGIE_CANON"
    )
    bone_rename_update_actions: bpy.props.BoolProperty(name="Update Action F-Curves", default=True)
    bone_rename_update_vg: bpy.props.BoolProperty(name="Update Vertex Groups", default=True)
    bone_rename_update_constraints: bpy.props.BoolProperty(name="Update Constraints", default=True)

    # Asset Prepper Properties
    asset_name: bpy.props.StringProperty(name="Asset Name", default="unsc_crate_01")
    asset_type: bpy.props.EnumProperty(
        name="Asset Type",
        items=[
            ("CRATE", "Crate", "Crate / Container prop"),
            ("SCENERY", "Scenery", "Static scenery prop"),
            ("VEHICLE", "Vehicle", "Vehicle model"),
            ("WEAPON", "Weapon", "Weapon model"),
            ("EQUIPMENT", "Equipment", "Equipment prop"),
        ],
        default="CRATE"
    )
    asset_clear_scene: bpy.props.BoolProperty(name="Clear Scene", default=True)
    asset_add_spartan: bpy.props.BoolProperty(name="Add Spartan Scale Model", default=True)


# ==============================================================================
# UI PANELS
# ==============================================================================

class HALO3_PT_category_header(bpy.types.Panel):
    """Category selector header panel in 3D View sidebar."""
    bl_label = "Halo 3 & Reach Prepper Suite"
    bl_idname = "HALO3_PT_category_header"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'scenario:game_engine_prepper'

    def draw(self, context):
        layout = self.layout
        props = context.scene.halo3_tool_props

        # Target Game Selector
        row = layout.row(align=True)
        row.prop(props, "target_game", text="Game")

        # Game Budget Box
        preset = constants.HALO_GAME_PRESETS.get(props.target_game, constants.HALO_GAME_PRESETS["HALO_3"])
        b_box = layout.box()
        b_box.label(text=f"{preset.get('short_name', props.target_game)} Engine Budget:", icon='SOLO_ON')
        col_b = b_box.column(align=True)
        col_b.label(text=f"Max Verts: {preset['max_verts']:,} | Max Tris: {preset['max_faces']:,} | UVs: {preset['max_uvs']}")

        obj = context.active_object
        if obj and obj.type == 'MESH':
            v_cnt = len(obj.data.vertices)
            p_cnt = len(obj.data.polygons)
            v_pct = int((v_cnt / max(1, preset['max_verts'])) * 100)
            p_pct = int((p_cnt / max(1, preset['max_faces'])) * 100)
            stat_icon = 'CHECKMARK' if (v_pct <= 85 and p_pct <= 85) else ('ERROR' if (v_pct > 100 or p_pct > 100) else 'QUESTION')
            b_box.label(text=f"Active '{obj.name}': {v_cnt:,}v ({v_pct}%), {p_cnt:,}f ({p_pct}%)", icon=stat_icon)

        # Category Switcher Buttons
        col = layout.column(align=True)
        row1 = col.row(align=True)
        row1.prop_enum(props, "ui_category", "TERRAIN", text="Terrain")
        row1.prop_enum(props, "ui_category", "STRUCTURES", text="Bases")
        row1.prop_enum(props, "ui_category", "REPAIR", text="Repair")

        row2 = col.row(align=True)
        row2.prop_enum(props, "ui_category", "SHADERS", text="Shaders")
        row2.prop_enum(props, "ui_category", "IMPORT_EXPORT", text="Import/Export")
        row2.prop_enum(props, "ui_category", "RIGGING_ANIM", text="Rigging")

        row3 = col.row(align=True)
        row3.prop_enum(props, "ui_category", "ASSET_PREPPER", text="Assets")
        row3.prop_enum(props, "ui_category", "SCALE_MODELS", text="Scale Models")
        row3.prop_enum(props, "ui_category", "ABOUT_CREDITS", text="About")

        row4 = col.row(align=True)
        row4.prop_enum(props, "ui_category", "ALL", text="All Panels")


class HALO3_PT_terrain_panel(bpy.types.Panel):
    """Grand Landscape & Natural Environment Generator."""
    bl_label = "Terrain & Landscapes"
    bl_idname = "HALO3_PT_terrain_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'scenario:game_engine_prepper'

    @classmethod
    def poll(cls, context):
        props = getattr(context.scene, "halo3_tool_props", None)
        return props and props.ui_category in ("TERRAIN", "ALL")

    def draw(self, context):
        layout = self.layout
        props = context.scene.halo3_tool_props

        box = layout.box()
        box.label(text="Grand Landscape Generator (Minecraft Scale):", icon='MESH_GRID')
        box.prop(props, "terrain_algorithm", text="Algorithm")
        box.prop(props, "environment_preset", text="Environment")
        box.prop(props, "terrain_topology", text="Topology")

        # Physical Erosion Simulation Box
        box_ero = box.box()
        box_ero.label(text="Physical Erosion Simulation (BspTroubleShooting):", icon='MOD_OCEAN')
        r_ero1 = box_ero.row(align=True)
        r_ero1.prop(props, "terrain_apply_hydraulic_erosion", text="Hydraulic Erosion")
        if props.terrain_apply_hydraulic_erosion:
            r_ero1.prop(props, "terrain_hydraulic_droplets", text="Droplets")
        r_ero2 = box_ero.row(align=True)
        r_ero2.prop(props, "terrain_apply_thermal_erosion", text="Thermal Erosion")
        if props.terrain_apply_thermal_erosion:
            r_ero2.prop(props, "terrain_thermal_iterations", text="Passes")
            r_ero2.prop(props, "terrain_thermal_talus_angle", text="Talus Angle")


        # Dimensions & Mountain Ridges
        box_dims = box.box()
        box_dims.label(text="Scale & Mountain Elevation Controls:", icon='ARROW_LEFTRIGHT')
        r_d1 = box_dims.row(align=True)
        r_d1.prop(props, "terrain_width", text="Width")
        r_d1.prop(props, "terrain_length", text="Length")
        r_d2 = box_dims.row(align=True)
        r_d2.prop(props, "terrain_cliff_height", text="Cliff Wall H")
        r_d2.prop(props, "terrain_mountain_height", text="Mountain Peak H")
        r_d3 = box_dims.row(align=True)
        r_d3.prop(props, "terrain_mountain_ridge_sharpness", text="Alpine Sharpness")
        r_d3.prop(props, "terrain_floor_flatness", text="Floor Width %")
        r_d4 = box_dims.row(align=True)
        r_d4.prop(props, "terrain_cliff_steepness", text="Wall Steepness")
        r_d4.prop(props, "terrain_meander", text="Winding Meander")
        r_d5 = box_dims.row(align=True)
        r_d5.prop(props, "terrain_noise_scale", text="Noise Frequency")
        r_d5.prop(props, "terrain_noise_roughness", text="Noise Amplitude")
        r_d6 = box_dims.row(align=True)
        r_d6.prop(props, "terrain_res_x", text="Res X")
        r_d6.prop(props, "terrain_res_y", text="Res Y")
        r_d7 = box_dims.row(align=True)
        r_d7.prop(props, "terrain_mountain_crest_rounding", text="Crest Pos")
        r_d7.prop(props, "terrain_crest_falloff", text="Shoulder Roll-off")
        box_dims.prop(props, "terrain_smooth_cliff_tops", text="Smooth Mountain Tops & Rim")

        # 3D Natural Cave Formations
        box_caves = box.box()
        box_caves.label(text="Natural 3D Caves & Arches (The Ark / Covenant):", icon='FORCE_VORTEX')
        box_caves.prop(props, "terrain_gen_caves", text="Sculpt 3D Caves")
        if props.terrain_gen_caves:
            r_c1 = box_caves.row(align=True)
            r_c1.prop(props, "terrain_cave_size", text="Diameter")
            r_c1.prop(props, "terrain_cave_frequency", text="Density")
            box_caves.prop(props, "terrain_cave_roughness", text="Cavern Wall Roughness")

        # Water Body & Immersion Physics (Sapien Surface Render + Physics Box)
        box_w = box.box()
        box_w.label(text="Water Body (Dual Surface Render + Physics):", icon='FORCE_BOID')
        row_w = box_w.row(align=True)
        row_w.prop(props, "terrain_gen_water", text="Include Water")
        row_w.prop(props, "terrain_water_z", text="Water Level Z")
        row_w2 = box_w.row(align=True)
        row_w2.prop(props, "water_depth", text="Water Depth")
        box_w.prop(props, "terrain_water_shader_picker", text="Water Shader (Picker)")

        # Shaders Section (Dynamic List Pickers)
        box_mat = box.box()
        box_mat.label(text="Authentic Shader Pickers (No Typing!):", icon='MATERIAL')
        box_mat.prop(props, "terrain_ground_shader_picker", text="Ground Shader")
        box_mat.prop(props, "terrain_cliff_shader_picker", text="Cliff Shader")
        box_mat.prop(props, "terrain_material_mode", text="Blending Mode")
        box_mat.prop(props, "terrain_use_material_layering", text="5-Slot Material Layering")

        # Sealing & BSP Boundaries
        box_seal = box.box()
        box_seal.label(text="Airtight Sealing & World UVs:", icon='STICKY_UVS_LOC')
        box_seal.prop(props, "terrain_seal_mode", text="Sealing")
        if props.terrain_seal_mode == 'SEALED_ROOM':
            box_seal.prop(props, "terrain_ceiling_z", text="Sky Ceiling Z")
        r_uv = box_seal.row(align=True)
        r_uv.prop(props, "terrain_uv_scale", text="World UV Scale")
        r_uv.prop(props, "terrain_uv_fit_box", text="Fit 1:1 in UV Box")

        # Natural Features Toggles
        r_feat = box.row(align=True)
        r_feat.prop(props, "terrain_gen_foliage", text="Include Trees")
        r_feat.prop(props, "terrain_gen_rocks", text="Include Rocks")
        r_feat.prop(props, "terrain_gen_portals", text="Airtight Portals")

        # Seed & Execute
        row_seed = box.row(align=True)
        row_seed.prop(props, "terrain_seed", text="Seed")
        row_seed.operator("halo3.randomize_seed", text="New Seed", icon='FILE_REFRESH').target = "TERRAIN"

        r_gen = box.row()
        r_gen.scale_y = 1.4
        r_gen.operator("halo3.generate_terrain", text="Generate Grand Landscape", icon='PLAY')

        # Terrain & Mountain Smoothing Tools
        box_rel = layout.box()
        box_rel.label(text="Mountain Top & Elevation Smoothing:", icon='SMOOTHSURF')
        r_sm1 = box_rel.row(align=True)
        r_sm1.operator("halo3.smooth_mountain_tops", text="Smooth Mountain Tops", icon='PROP_CON')
        r_sm1.operator("halo3.smooth_cliff_walls", text="Smooth Cliff Walls", icon='EDGESEL')
        r_sm2 = box_rel.row(align=True)
        r_sm2.operator("halo3.smooth_terrain", text="Smooth Elevation", icon='SMOOTHSURF')
        r_sm2.operator("halo3.subdivide_terrain", text="Subdivide & Smooth", icon='MOD_SUBSURF')

        # BSP Sealing Tools
        r_tools2 = layout.row(align=True)
        r_tools2.operator("halo3.stitch_and_seal", text="Stitch & Seal", icon='AUTOMERGE')
        r_tools2.operator("halo3.seal_imported_terrain", text="Seal Mesh (+sky Room)", icon='STICKY_UVS_LOC')

        # Modder Guide & How-To Box
        box_guide = layout.box()
        box_guide.label(text="Modder Guide: Water & BSP Rules:", icon='HELP')
        col_g = box_guide.column(align=True)
        col_g.label(text="1. Water in Sapien: Render surface must be a top-facing plane with (') tag suffix.")
        col_g.label(text="2. Immersion: 3D physics box (~water) belongs in structure_design for splash sounds.")
        col_g.label(text="3. Sealed World: tool.exe requires all structure BSPs to be 100% enclosed by geometry.")
        col_g.label(text="4. UVs: 4 UV sets generated automatically (Lightmap, Detail, Wall/Height, Macro).")


class HALO3_PT_structures_panel(bpy.types.Panel):
    """Bases, UNSC Corridors, Celestial Skies & Portals."""
    bl_label = "Bases, Corridors & Skies"
    bl_idname = "HALO3_PT_structures_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'scenario:game_engine_prepper'

    @classmethod
    def poll(cls, context):
        props = getattr(context.scene, "halo3_tool_props", None)
        return props and props.ui_category in ("STRUCTURES", "ALL")

    def draw(self, context):
        layout = self.layout
        props = context.scene.halo3_tool_props

        # Base level root
        box_root = layout.box()
        box_root.operator("halo3.generate_base_level", text="Initialize b_levelroot Frame", icon='EMPTY_AXIS')

        # Celestial Skybox & Atmosphere
        box_sky = layout.box()
        box_sky.label(text="Halo Skybox & Atmosphere:", icon='LIGHT_SUN')
        box_sky.prop(props, "sky_preset", text="Preset")
        row_sky = box_sky.row(align=True)
        row_sky.operator("halo3.generate_sky", text="Generate Celestial Sky", icon='LIGHT_SUN')
        row_sky.operator("halo3.import_skybox", text="Import Skybox...", icon='IMPORT')

        # Blood Gulch Fortress Bunker
        box_bunker = layout.box()
        box_bunker.label(text="Blood Gulch Fortress Bunker:", icon='COMMUNITY')
        r_b1 = box_bunker.row(align=True)
        r_b1.prop(props, "bunker_radius", text="Radius")
        r_b1.prop(props, "bunker_height", text="Height")
        r_b2 = box_bunker.row(align=True)
        r_b2.prop(props, "bunker_door_h", text="Door Height")
        r_b2.prop(props, "bunker_ramp_w", text="Ramp Width")
        box_bunker.prop(props, "bunker_ramp_style", text="Ramps")
        box_bunker.prop(props, "bunker_include_splitter", text="Center Splitter Wall")
        box_bunker.operator("halo3.generate_base_bunker", text="Generate Base Bunker", icon='ADD')

        # UNSC Hallways & Portals
        box_hall = layout.box()
        box_hall.label(text="UNSC Corridors & Portals:", icon='MOD_BUILD')
        box_hall.prop(props, "hallway_layout_mode", text="Layout")
        r_h1 = box_hall.row(align=True)
        r_h1.prop(props, "hallway_width", text="Width")
        r_h1.prop(props, "hallway_height", text="Height")
        box_hall.prop(props, "hallway_length", text="Length")
        box_hall.operator("halo3.generate_hallway", text="Generate Corridors", icon='ADD')


class HALO3_PT_nature_panel(bpy.types.Panel):
    """Natural Rock Crags & Foliage with Multi-Tier Size Variations."""
    bl_label = "Rocks & Foliage (Size Tiers)"
    bl_idname = "HALO3_PT_nature_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'scenario:game_engine_prepper'

    @classmethod
    def poll(cls, context):
        props = getattr(context.scene, "halo3_tool_props", None)
        return props and props.ui_category in ("TERRAIN", "ALL")

    def draw(self, context):
        layout = self.layout
        props = context.scene.halo3_tool_props

        # Angular Crag Rocks Section
        box_rock = layout.box()
        box_rock.label(text="Angular Crag & Faceted Rocks (No Ovals!):", icon='MESH_ICOSPHERE')
        box_rock.prop(props, "rock_archetype", text="Archetype")
        box_rock.prop(props, "rock_size_tier", text="Size Tier")

        row_rgen = box_rock.row(align=True)
        row_rgen.operator("halo3.generate_rock", text="Generate Rock", icon='ADD')
        row_rgen.operator("halo3.randomize_seed", text="New Seed", icon='FILE_REFRESH').target = "ROCK"

        # 4-Tier Rock Scattering
        box_scat_r = box_rock.box()
        box_scat_r.label(text="4-Tier Rock Distribution Counts:")
        r1 = box_scat_r.row(align=True)
        r1.prop(props, "rock_count_huge", text="Huge")
        r1.prop(props, "rock_count_medium", text="Medium")
        r2 = box_scat_r.row(align=True)
        r2.prop(props, "rock_count_small", text="Small")
        r2.prop(props, "rock_count_micro", text="Micro")

        row_rsp = box_scat_r.row(align=True)
        row_rsp.prop(props, "rock_clearance", text="Clearance")
        row_rsp.prop(props, "scatter_spread_scalar", text="Spread")
        box_scat_r.operator("halo3.scatter_rocks", text="Scatter 4-Tier Rocks on Terrain", icon='PARTICLES')

        # Foliage & Trees Section
        box_tree = layout.box()
        box_tree.label(text="Procedural Trees & Undergrowth (5 Tiers):", icon='COMMUNITY')
        box_tree.prop(props, "tree_type", text="Type")
        box_tree.prop(props, "foliage_size_tier", text="Size Tier")
        box_tree.prop(props, "foliage_leaf_shader_picker", text="Foliage Shader (Picker)")
        box_tree.prop(props, "foliage_bark_shader_picker", text="Bark Shader (Picker)")

        row_tgen = box_tree.row(align=True)
        row_tgen.operator("halo3.generate_tree", text="Generate Tree", icon='ADD')
        row_tgen.operator("halo3.randomize_seed", text="New Seed", icon='FILE_REFRESH').target = "FOLIAGE"

        box_scat_t = box_tree.box()
        box_scat_t.label(text="Multi-Tier Foliage Scattering:")
        row_fc = box_scat_t.row(align=True)
        row_fc.prop(props, "foliage_scatter_count", text="Count")
        row_fc.prop(props, "foliage_spread_scalar", text="Spread")
        row_fm = box_scat_t.row(align=True)
        row_fm.prop(props, "foliage_min_dist", text="Min Spacing")
        row_fm.prop(props, "foliage_scatter_mode", text="Mode")

        r_tree_ops = box_scat_t.row(align=True)
        r_tree_ops.operator("halo3.scatter_foliage", text="Scatter Foliage", icon='PARTICLES')
        r_tree_ops.operator("halo3.clear_foliage", text="Clear Foliage", icon='TRASH')


class HALO3_PT_repair_panel(bpy.types.Panel):
    """1-Click Master Geometry Repair & BSP Diagnostics."""
    bl_label = "Geometry Repair & BSP Validator"
    bl_idname = "HALO3_PT_repair_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'scenario:game_engine_prepper'

    @classmethod
    def poll(cls, context):
        props = getattr(context.scene, "halo3_tool_props", None)
        return props and props.ui_category in ("REPAIR", "ALL")

    def draw(self, context):
        layout = self.layout
        props = context.scene.halo3_tool_props

        # Scope selector: All Scene Meshes vs Selected Only
        box_scope = layout.box()
        box_scope.label(text="Fix & Scan Target Scope:", icon='OBJECT_DATAMODE')
        box_scope.prop(props, "diag_scope", expand=True)

        is_all = (props.diag_scope == "ALL")
        fix_text = "1-Click Auto-Repair Entire Scene" if is_all else "1-Click Auto-Repair Selected Object(s)"
        scan_text = "Scan Entire Scene for Errors" if is_all else "Scan Selected Object(s) for Errors"

        # Primary Auto-Repair Operator Button (Prominent)
        col_m = layout.column(align=True)
        col_m.scale_y = 1.5
        op_fix = col_m.operator("halo3.cleanup_all", text=fix_text, icon='TOOL_SETTINGS')
        op_fix.scope = props.diag_scope

        # Primary Scan Button
        col_s = layout.column(align=True)
        col_s.scale_y = 1.15
        col_s.operator("halo3.check_errors", text=scan_text, icon='ZOOM_ALL')

        # Detected Issues Badges
        total_issues = (
            props.diag_total_open + props.diag_total_ambiguous +
            props.diag_total_degenerate + props.diag_total_duplicate +
            props.diag_total_short + props.diag_total_missing_mat
        )
        if total_issues > 0:
            box_err = layout.box()
            box_err.label(text=f"Detected Issues in Scan ({total_issues} Total):", icon='ERROR')
            if props.diag_total_ambiguous > 0:
                box_err.label(text=f"• Ambiguous 3+ Way Edges: {props.diag_total_ambiguous} (Click 'Split Ambiguous' below!)")
            if props.diag_total_open > 0:
                box_err.label(text=f"• Open Boundary Edges: {props.diag_total_open}")
            if props.diag_total_duplicate > 0:
                box_err.label(text=f"• Duplicate / Overlapping Faces: {props.diag_total_duplicate}")
            if props.diag_total_degenerate > 0:
                box_err.label(text=f"• Degenerate Zero-Area Faces: {props.diag_total_degenerate}")
            if props.diag_total_short > 0:
                box_err.label(text=f"• Micro Short Edges: {props.diag_total_short}")
            if props.diag_total_missing_mat > 0:
                box_err.label(text=f"• Invalid / Missing Shaders: {props.diag_total_missing_mat}")

        # Dedicated Targeted Fixes Box
        box_fixes = layout.box()
        box_fixes.label(text="Dedicated Geometry Fixes:", icon='MODIFIER')
        r1 = box_fixes.row(align=True)
        r1.operator("halo3.fix_ambiguous_edges", text="Split Ambiguous", icon='AUTOMERGE')
        r1.operator("halo3.remove_duplicates", text="Remove Dupes", icon='TRASH')
        r2 = box_fixes.row(align=True)
        r2.operator("halo3.seal_holes", text="Seal Open Holes", icon='STICKY_UVS_LOC')
        r2.operator("halo3.collapse_short_edges", text="Collapse Short", icon='COLLAPSEMENU')
        r3 = box_fixes.row(align=True)
        r3.operator("halo3.dissolve_coplanar", text="Dissolve Flat", icon='FACESEL')
        r3.operator("halo3.fix_normals", text="Recalc Normals", icon='NORMALS_FACE')
        r4 = box_fixes.row(align=True)
        r4.operator("halo3.fix_materials", text="Fix Shaders", icon='MATERIAL')
        r4.operator("halo3.fix_portal_leakage", text="Fix Portals", icon='MOD_SOLIDIFY')
        r5 = box_fixes.row(align=True)
        r5.operator("halo3.select_bsp_errors", text="Select BSP Errors", icon='RESTRICT_SELECT_OFF')
        r5.operator("halo3.import_wrl_errors", text="Import Tool .WRL", icon='FILE')
        r6 = box_fixes.row(align=True)
        r6.operator("halo3.select_error_geometry", text="Select Error Faces", icon='FACESEL')
        r6.operator("halo3.stitch_and_seal", text="Stitch & Seal", icon='AUTOMERGE')


        # Elevation & Mountain Relaxation Box
        box_smooth = layout.box()
        box_smooth.label(text="Mountain Top & Elevation Smoothing:", icon='SMOOTHSURF')
        r_sm1 = box_smooth.row(align=True)
        r_sm1.operator("halo3.smooth_mountain_tops", text="Smooth Mountain Tops", icon='PROP_CON')
        r_sm1.operator("halo3.smooth_cliff_walls", text="Smooth Cliff Walls", icon='EDGESEL')
        r_sm2 = box_smooth.row(align=True)
        r_sm2.operator("halo3.smooth_terrain", text="Smooth Elevation", icon='SMOOTHSURF')

        # Auto-Repair Settings Configuration Box
        box_cfg = layout.box()
        box_cfg.label(text="Auto-Repair Configuration Settings:", icon='PREFERENCES')
        col_c = box_cfg.column(align=True)
        col_c.prop(props, "bsp_fix_ambiguous", text="Fix Ambiguous Edges")
        col_c.prop(props, "bsp_remove_duplicates", text="Remove Duplicate Faces")
        col_c.prop(props, "bsp_remove_degenerate", text="Remove Degenerate Triangles")
        col_c.prop(props, "bsp_collapse_short", text="Collapse Short Edges")
        col_c.prop(props, "bsp_dissolve_coplanar", text="Dissolve Coplanar Faces")
        col_c.prop(props, "bsp_seal_holes", text="Seal Open Holes")
        col_c.prop(props, "bsp_recalc_normals", text="Recalculate Normals")
        col_c.prop(props, "diag_normals_dir", text="Normals Direction")
        col_c.prop(props, "auto_fix_shaders", text="Auto-Fix Shaders")
        col_c.prop(props, "bsp_weld_dist", text="Weld Distance")
        col_c.prop(props, "diag_coplanar_angle", text="Coplanar Angle (°)")


class HALO3_PT_shaders_panel(bpy.types.Panel):
    """13,700+ Authentic Halo Shader Library & List Pickers."""
    bl_label = "Shaders & Material Preview"
    bl_idname = "HALO3_PT_shaders_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'scenario:game_engine_prepper'

    @classmethod
    def poll(cls, context):
        props = getattr(context.scene, "halo3_tool_props", None)
        return props and props.ui_category in ("SHADERS", "ALL")

    def draw(self, context):
        layout = self.layout
        props = context.scene.halo3_tool_props

        box = layout.box()
        box.label(text="Authentic Shader Catalog Browser:", icon='MATERIAL')
        box.prop(props, "shader_category_filter", text="Category")
        box.prop(props, "browser_active_shader_picker", text="Shader List Picker")

        r_apply = box.row(align=True)
        r_apply.operator("halo3.apply_shader_to_active", text="Apply to Active Mesh", icon='MATERIAL')
        r_apply.operator("halo3.paint_selection", text="Apply to Selected Faces", icon='FACESEL')

        # Custom Shaders & Modifier Box
        box_csh = layout.box()
        box_csh.label(text="Custom Shader Library & Modifier:", icon='COLOR')
        box_csh.prop(props, "selected_custom_shader", text="Custom Shaders")
        r_csh_ops = box_csh.row(align=True)
        r_csh_ops.operator("halo3.apply_custom_shader", text="Apply to Mesh", icon='MATERIAL')
        r_csh_ops.operator("halo3.delete_custom_shader", text="Delete", icon='TRASH')

        box_mod = box_csh.box()
        box_mod.label(text="Modify Active Shader & Save Custom:")
        box_mod.prop(props, "custom_shader_name", text="Custom Name")
        box_mod.prop(props, "custom_shader_tint", text="Tint Color")
        r_mod1 = box_mod.row(align=True)
        r_mod1.prop(props, "custom_shader_roughness", text="Roughness")
        r_mod1.prop(props, "custom_shader_metallic", text="Metallic")
        r_mod2 = box_mod.row(align=True)
        r_mod2.prop(props, "custom_shader_bump", text="Bump")
        r_mod2.prop(props, "custom_shader_uv_scale", text="UV Scale")
        box_mod.operator("halo3.save_custom_shader", text="Save Modified Shader to Custom Palette", icon='FILE_TICK')


        # Quick Palettes
        box_pal = layout.box()
        box_pal.label(text="Quick Halo Palettes (1-Click Apply):", icon='COLOR')
        r_p1 = box_pal.row(align=True)
        r_p1.operator("halo3.apply_quick_shader", text="Grass").shader_name = "river riverworld_ground"
        r_p1.operator("halo3.apply_quick_shader", text="Cliff Rock").shader_name = "dead deadlock_cliff_rock"
        r_p1.operator("halo3.apply_quick_shader", text="Sand").shader_name = "sand sand_dune_ripples"
        r_p2 = box_pal.row(align=True)
        r_p2.operator("halo3.apply_quick_shader", text="Water Surface").shader_name = "river riverworld_water_rough'"
        r_p2.operator("halo3.apply_quick_shader", text="+sky").shader_name = "+sky"
        r_p2.operator("halo3.apply_quick_shader", text="!portal").shader_name = "!portal"

        # 3D Viewport Shader Brush & Painter
        box_brush = layout.box()
        box_brush.label(text="Interactive Shader Brush & Painter:", icon='BRUSH_DATA')
        box_brush.prop(props, "shader_brush_mode", text="Mode")
        if props.shader_brush_mode == 'SHADERS':
            box_brush.prop(props, "shader_brush_fit_uv", text="Fit UV 1:1 in Box")
        else:
            box_brush.prop(props, "shader_brush_blend_layer", text="Blend Layer")
            box_brush.prop(props, "shader_brush_strength", text="Strength")
        row_br = box_brush.row(align=True)
        row_br.prop(props, "shader_brush_radius", text="Radius")
        box_brush.operator("halo3.shader_paint_brush", text="Launch Shader Paint Brush", icon='BRUSH_DATA')


class HALO3_PT_asset_prepper_panel(bpy.types.Panel):
    """Editing Kit Asset Prepper & Spartan Scale Calibration."""
    bl_label = "Asset Prepper & Markers"
    bl_idname = "HALO3_PT_asset_prepper_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'scenario:game_engine_prepper'

    @classmethod
    def poll(cls, context):
        props = getattr(context.scene, "halo3_tool_props", None)
        return props and props.ui_category in ("ASSET_PREPPER", "ALL")

    def draw(self, context):
        layout = self.layout
        props = context.scene.halo3_tool_props

        box = layout.box()
        box.label(text="Asset Scene Prepper:", icon='SCENE_DATA')
        box.prop(props, "asset_name", text="Asset Name")
        box.prop(props, "asset_type", text="Type")
        row_prep = box.row(align=True)
        row_prep.prop(props, "asset_clear_scene", text="Clear Scene")
        row_prep.prop(props, "asset_add_spartan", text="Add Spartan Scale")
        r_prep = box.row(align=True)
        r_prep.operator("halo3.prep_object_scene", text="Setup Asset Scene", icon='SOLO_ON')
        r_prep.operator("halo3.generate_tag_template", text="Generate Tag Template", icon='FILE_SCRIPT')

        box_col = layout.box()
        box_col.label(text="Collision & Physics Setup:", icon='PHYSICS')
        r_col = box_col.row(align=True)
        r_col.operator("halo3.make_collision_object", text="Make @collision", icon='MOD_PHYSICS')
        r_col.operator("halo3.make_physics_object", text="Make $physics", icon='RIGID_BODY')


class HALO3_PT_import_export_panel(bpy.types.Panel):
    """Universal File Importer & Exporter (.ASS, .JMS, .JMI, .JMF, .RMF) & 1-Click Compiler."""
    bl_label = "Import / Export & Tool Compiler"
    bl_idname = "HALO3_PT_import_export_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'scenario:game_engine_prepper'

    @classmethod
    def poll(cls, context):
        props = getattr(context.scene, "halo3_tool_props", None)
        return props and props.ui_category in ("IMPORT_EXPORT", "ALL")

    def draw(self, context):
        layout = self.layout
        props = context.scene.halo3_tool_props

        # Universal File Importers Section
        box_imp = layout.box()
        box_imp.label(text="Universal File Importers (.ASS, .JMS, .JMI, .JMF, .RMF):", icon='IMPORT')
        r_imp1 = box_imp.row(align=True)
        r_imp1.operator("halo3.import_ass", text="Import .ASS", icon='FILE')
        r_imp1.operator("halo3.import_jms", text="Import .JMS", icon='FILE')
        r_imp1.operator("halo3.import_jmi", text="Import .JMI", icon='FILE')
        r_imp2 = box_imp.row(align=True)
        r_imp2.operator("halo3.import_jmf", text="Import .JMF", icon='FILE')
        r_imp2.operator("halo3.import_rmf", text="Import .RMF (Reclaimer)", icon='FILE')

        # Compilation Target Box
        box = layout.box()
        box.label(text="Compilation Target:", icon='OBJECT_DATA')
        box.prop(props, "export_asset_type", text="Target")
        box.prop(props, "map_name", text="Asset / Map Name")

        if props.export_asset_type == "MAP_STRUCTURE":
            box.prop(props, "map_scenario_type", text="Scenario")
        else:
            box.prop(props, "export_sub_folder", text="Subfolder")
            box.prop(props, "object_compile_mode", text="Objects Mode")

        # Editing Kit Paths & Tool Fast
        box_ek = layout.box()
        box_ek.label(text="Editing Kit Setup:", icon='FILE_FOLDER')
        box_ek.prop(props, "editing_kit_path", text="Path")
        row_fast = box_ek.row(align=True)
        row_fast.prop(props, "tool_fast_mode", text="Use tool_fast.exe")
        row_fast.prop(props, "auto_repair_before_export", text="Auto-Repair")

        row_tk = box_ek.row(align=True)
        row_tk.prop(props, "export_use_toolkit", text="Use Installed Toolkit")
        row_tk.prop(props, "export_use_native", text="Force Standalone")

        # 1-Click Master Compilation Button
        col_exec = layout.column(align=True)
        col_exec.scale_y = 1.4
        btn_text = "1-Click Export & Compile (Tool)"
        if props.export_asset_type != "MAP_STRUCTURE":
            btn_text = f"1-Click Export & Compile {props.export_asset_type.title()} (Tool)"
        col_exec.operator("halo3.export_and_compile", text=btn_text, icon='PLAY')

        row_setup = layout.row(align=True)
        row_setup.operator("halo3.setup_map_folders", text="Setup Folders", icon='NEWFOLDER')
        row_setup.operator("halo3.export_addon_zip", text="Package Addon .ZIP", icon='PACKAGE')

        # Standalone File Exporters
        box_exp = layout.box()
        box_exp.label(text="Standalone File Exporters:", icon='EXPORT')
        row_fmt = box_exp.row(align=True)
        row_fmt.prop(props, "export_format", text="Format")
        if props.export_format == "ASS":
            row_fmt.prop(props, "export_ass_version", text="Version")
        elif props.export_format == "JMS":
            row_fmt.prop(props, "export_jms_version", text="Version")
        r_exp = box_exp.row(align=True)
        r_exp.operator("halo3.export_ass_native", text="Export .ASS", icon='FILE')
        r_exp.operator("halo3.export_jms_native", text="Export .JMS", icon='FILE')
        r_exp.operator("halo3.export_jmi_native", text="Export .JMI", icon='FILE')
        if props.export_format == "GR2" or props.target_game == "HALO_REACH":
            box_exp.operator("export_scene.halo_reach_gr2", text="Export Halo Reach .GR2 (HREK)", icon='EXPORT')


        # Compiler Destination & Status Box
        box_log = layout.box()
        status_color = 'INFO' if "PASSED" in props.last_compile_status else ('ERROR' if "FAILED" in props.last_compile_status else 'NONE')
        box_log.label(text=f"Status: {props.last_compile_status}", icon=status_color)

        if props.last_export_filepath:
            box_dest = box_log.box()
            box_dest.label(text="Export Destination:", icon='FILE_TICK')
            box_dest.label(text=f"File: {props.last_export_filepath}")
            if props.last_tags_directory:
                box_dest.label(text=f"Tags: {props.last_tags_directory}")
            r_paths = box_dest.row(align=True)
            r_paths.operator("halo3.open_export_folder", text="Open Export Folder", icon='FILE_FOLDER')
            r_paths.operator("halo3.open_tags_folder", text="Open Tags Folder", icon='FILE_FOLDER')

        if props.last_compile_output:
            box_log.label(text="Compiler Output Log:")
            for line in props.last_compile_output.split("\n")[-15:]:
                if line.strip():
                    box_log.label(text=line[:75])


class HALO3_PT_rigging_panel(bpy.types.Panel):
    """Animation-Safe Bone Renaming & Reference Rigs."""
    bl_label = "Rigging & Bone Safe"
    bl_idname = "HALO3_PT_rigging_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'scenario:game_engine_prepper'

    @classmethod
    def poll(cls, context):
        props = getattr(context.scene, "halo3_tool_props", None)
        return props and props.ui_category in ("RIGGING_ANIM", "ALL")

    def draw(self, context):
        layout = self.layout
        props = context.scene.halo3_tool_props

        box = layout.box()
        box.label(text="Animation-Safe Bone Renamer:", icon='ARMATURE_DATA')
        box.prop(props, "bone_rename_target_style", text="Target Style")
        r_safe = box.row(align=True)
        r_safe.prop(props, "bone_rename_update_actions", text="F-Curves")
        r_safe.prop(props, "bone_rename_update_vg", text="Vertex Groups")
        box.prop(props, "bone_rename_update_constraints", text="Update Constraints")
        box.operator("halo3.rename_bones_safe", text="Rename Bones (Action-Safe)", icon='PLAY')


class HALO_PT_ScaleModelHelper(bpy.types.Panel):
    """Scale Model Helper matching Halo Blender Toolkit."""
    bl_label = "Scale Models & Reference Units"
    bl_idname = "HALO_PT_ScaleModelHelper"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'scenario:game_engine_prepper'

    @classmethod
    def poll(cls, context):
        props = getattr(context.scene, "halo3_tool_props", None)
        return props and props.ui_category in ("SCALE_MODELS", "ALL")

    def draw(self, context):
        layout = self.layout
        scene_scale_model = getattr(context.scene, "halo_scale_model", None) or getattr(context.scene, "scale_model", None)
        if not scene_scale_model:
            layout.label(text="Scale Model system active.")
            return

        box = layout.box()
        box.label(text="Halo Scale Models (Characters & Vehicles):", icon='USER')
        row = box.row(align=True)
        row.prop(scene_scale_model, "game_version", text="Game")
        row.prop(scene_scale_model, "unit_type", text="Type")

        row_pick = box.row()
        if scene_scale_model.game_version == "halo1":
            if scene_scale_model.unit_type == "character":
                row_pick.prop(scene_scale_model, "halo_one_scale_model_char", text="Unit")
            else:
                row_pick.prop(scene_scale_model, "halo_one_scale_model_vehi", text="Unit")
        elif scene_scale_model.game_version == "halo2":
            if scene_scale_model.unit_type == "character":
                row_pick.prop(scene_scale_model, "halo_two_scale_model_char", text="Unit")
            else:
                row_pick.prop(scene_scale_model, "halo_two_scale_model_vehi", text="Unit")
        elif scene_scale_model.game_version == "reach":
            if scene_scale_model.unit_type == "character":
                row_pick.prop(scene_scale_model, "reach_scale_model_char", text="Unit")
            else:
                row_pick.prop(scene_scale_model, "reach_scale_model_vehi", text="Unit")
        else:
            if scene_scale_model.unit_type == "character":
                row_pick.prop(scene_scale_model, "halo_three_scale_model_char", text="Unit")
            else:
                row_pick.prop(scene_scale_model, "halo_three_scale_model_vehi", text="Unit")

        box.operator("halo_bulk.scale_model", text="Generate Scale Model at Cursor", icon='SNAP_VERTEX')

        # Sizing vs Master Chief Rule (Sapien Rules)
        box_scale = layout.box()
        box_scale.label(text="Level Sizing vs Master Chief (Sapien Rules):", icon='ORIENTATION_GIMBAL')
        col_s = box_scale.column(align=True)
        col_s.label(text="* Master Chief Height: ~70.3 BU (~2.14m).")
        col_s.label(text="* Golden Rule: Map bounds must be >= 2x minimum combat size!")
        col_s.label(text="* Prevents Tool vertex collapsing & Sapien radiosity failure.")
        col_s.label(text="* Recommended Min Playable Bounds: 1200 x 2400 BU.")


class HALO3_PT_about_panel(bpy.types.Panel):
    """Credits and Complete Step-by-Step Halo Modder Guide."""
    bl_label = "About & How To Guide"
    bl_idname = "HALO3_PT_about_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'scenario:game_engine_prepper'

    @classmethod
    def poll(cls, context):
        props = getattr(context.scene, "halo3_tool_props", None)
        return props and props.ui_category in ("ABOUT_CREDITS", "ALL")

    def draw(self, context):
        layout = self.layout

        # Suite Overview
        box = layout.box()
        box.label(text="Halo Multi-Game Modding & Prepper Suite", icon='SOLO_ON')
        box.label(text=f"Version: 2.8.0 | Author: {constants.AUTHOR}")
        box.label(text="Shoutout to c20 Reclaimers Wiki!")
        box.label(text="Special Thanks to Bungie for the Blam! Engine")

        # Step-by-Step Modding Guide
        box_guide = layout.box()
        box_guide.label(text="How-To: Compile Map to Playable Sapien Scenario:", icon='HELP')
        col_g = box_guide.column(align=True)
        col_g.label(text="1. Setup Folders: Click 'Setup Folders' in Import/Export tab.")
        col_g.label(text="2. Pick Target Game: CE (JMS), H2 (JMS/ASS), H3 (ASS), Reach (.GR2).")
        col_g.label(text="3. Scale Reference: Use Scale Models tab to verify Master Chief scale.")
        col_g.label(text="   * Rule: Levels must be >= 2x scale to prevent vertex merging!")
        col_g.label(text="4. Generate Landscape: Use algorithmic terrain with sealed boundaries.")
        col_g.label(text="5. BSP Check & Fix: Click 'Auto-Repair & Clean Geometry' (0 errors).")
        col_g.label(text="6. Export & Compile: Click '1-Click Export & Compile (Tool)'.")
        col_g.label(text="7. Launch Sapien: Open your level's .scenario in Sapien.")

        # Per-Game Formats Guide
        box_formats = layout.box()
        box_formats.label(text="Supported Formats per Halo Title:", icon='FILE')
        col_f = box_formats.column(align=True)
        col_f.label(text="* Halo CE: .JMS format -> 'tool.exe structure'")
        col_f.label(text="* Halo 2: .JMS / .ASS format -> 'tool.exe structure'")
        col_f.label(text="* Halo 3: .ASS format -> 'tool.exe structure <path>.ass'")
        col_f.label(text="* Halo Reach: .GR2 format (FBX + Sidecar XML -> 'tool.exe fbx-to-gr2')")
        col_f.label(text="  Reach maps import with: 'tool.exe import <sidecar> force farm_bsp'")

        # BSP Troubleshooting Tips
        box_tips = layout.box()
        box_tips.label(text="BSP Troubleshooting Quick Tips (BspTroubleshooting):", icon='ERROR')
        col_t = box_tips.column(align=True)
        col_t.label(text="* 'Couldn't update edge': Check for open edges or scale < 2x.")
        col_t.label(text="* 'Degenerate triangle': Near-zero area triangle collapsed by Tool.")
        col_t.label(text="* 'Vertex too far away': Keep all geometry within [-1500, 1500] coords.")
        col_t.label(text="* 'Portal leakage': Use 'Fix Portals' to ensure planar + z_portal parent.")

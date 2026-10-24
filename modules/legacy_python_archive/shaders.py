# -*- coding: utf-8 -*-
"""
Halo 3 & Reach Comprehensive Shader Catalog & Material Processor
Parses 14,000+ authentic shaders from halo3_blender_shaders.txt (including all Reach
and Solo Campaign Terrain shaders). Builds node-based materials with 1:1 UV box mapping.
Supports custom modifiable shaders with JSON persistence across sessions.
Created by O7OF | Shoutout to c20 Reclaimers Wiki | Thank you Bungie
"""

import os
import re
import json
import bpy
from mathutils import Vector
from . import constants


def _safe_mix_color_node(nodes, links, loc, fac_sock, col_a, col_b):
    """Creates a rock-solid Color Mix node compatible across Blender 3.x, 4.x, and 5.x."""
    try:
        mix = nodes.new('ShaderNodeMix')
        mix.data_type = 'RGBA'
        mix.location = loc
        if fac_sock:
            links.new(fac_sock, mix.inputs[0])
        col_in = [i for i in mix.inputs if i.type == 'RGBA']
        if len(col_in) >= 2:
            col_in[0].default_value = col_a
            col_in[1].default_value = col_b
        col_out = [o for o in mix.outputs if o.type == 'RGBA']
        return mix, col_out[0]
    except Exception:
        mix = nodes.new('ShaderNodeMixRGB')
        mix.location = loc
        if fac_sock:
            links.new(fac_sock, mix.inputs['Fac'])
        mix.inputs['Color1'].default_value = col_a
        mix.inputs['Color2'].default_value = col_b
        return mix, mix.outputs['Color']


def get_custom_shaders_filepath():
    """Returns persistent filepath for user custom shader library."""
    base_dirs = [
        os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
        r"c:\Users\Chey1\Desktop\BlenderHalomapBase",
        os.path.dirname(os.path.abspath(__file__))
    ]
    for bd in base_dirs:
        if os.path.isdir(bd):
            return os.path.join(bd, "custom_shaders.json")
    return os.path.join(os.path.dirname(os.path.abspath(__file__)), "custom_shaders.json")


class Halo3ShaderManager:
    """Manages Halo shader tag database, custom user shaders, node trees, and material assignments."""

    _instance = None

    @classmethod
    def get(cls):
        if cls._instance is None:
            cls._instance = cls()
        return cls._instance

    def __init__(self):
        self.all_shaders = []
        self.shader_set = set()
        self.solo_terrain_shaders = []
        self.reach_shaders = []
        self.h3_shaders = []
        self.special_shaders = []
        self.levels = {}
        self.categories = {}
        self.tag_paths = {}
        self.custom_shaders = {}
        self._loaded_path = ""
        self._enum_cache = {}
        self._init_defaults()
        self.load_custom_shaders()

    def _init_defaults(self):
        defaults = [
            "river riverworld_ground",
            "river riverworld_water_rough'",
            "dead deadlock_cliff_rock",
            "dead deadlock_tree_bark",
            "dead deadlock_pine_needles",
            "forge_halo halo_grass_01",
            "forge_halo halo_cliff_01",
            "forge_halo halo_water_rough'",
            "m10 008_terrain_outpost",
            "m20 m20_snowpack_cliffs",
            "m30 m30_010_ground_new",
            "boneyard boneyard_cliff",
            "boneyard boneyard_ground",
            "chill chill_snow",
            "sand sand_dune_ripples",
            "mpshare mp_sand",
            "mpshare mp_rock",
            "mpshare mp_cliff",
            "mpshare mp_terrain",
            "out out_dirt",
            "+sky",
            "+portal",
            "!portal",
            "+seam",
            "+weatherpoly",
            "+sound",
            "%water",
            "%spartan_scale",
        ]
        for s in defaults:
            if s not in self.all_shaders:
                self.all_shaders.append(s)
                self.shader_set.add(s)
        self.special_shaders = [
            "+sky", "+portal", "!portal", "+seam", "+weatherpoly", "+sound", "%water", "%spartan_scale"
        ]

    # =========================================================================
    # CUSTOM SHADER PERSISTENCE & MANAGEMENT
    # =========================================================================

    def load_custom_shaders(self):
        """Loads user-saved custom shaders from custom_shaders.json."""
        fp = get_custom_shaders_filepath()
        if os.path.isfile(fp):
            try:
                with open(fp, "r", encoding="utf-8") as f:
                    self.custom_shaders = json.load(f)
                return True
            except Exception as e:
                print(f"[SHADER MANAGER] Failed loading custom shaders from {fp}: {e}")
                self.custom_shaders = {}
        return False

    def save_custom_shaders(self):
        """Saves current custom shaders to disk."""
        fp = get_custom_shaders_filepath()
        try:
            os.makedirs(os.path.dirname(os.path.abspath(fp)), exist_ok=True)
            with open(fp, "w", encoding="utf-8") as f:
                json.dump(self.custom_shaders, f, indent=2)
            self._enum_cache.clear()
            return True
        except Exception as e:
            print(f"[SHADER MANAGER] Failed saving custom shaders to {fp}: {e}")
            return False

    def register_custom_shader(self, name, base_shader, target_game="HALO_3", category="CLIFFS_ROCKS",
                               tint_color=(1.0, 1.0, 1.0, 1.0), roughness=0.85, metallic=0.0,
                               bump_strength=0.35, uv_scale=(1.0, 1.0, 1.0), emission_color=(0.0, 0.0, 0.0, 1.0),
                               emission_strength=0.0, alpha=1.0):
        """Registers and persists a new custom modified shader."""
        clean_name = name.strip()
        if not clean_name:
            clean_name = f"Custom_{base_shader.replace(' ', '_')}"

        self.custom_shaders[clean_name] = {
            "name": clean_name,
            "base_shader": base_shader.strip(),
            "target_game": target_game,
            "category": category,
            "tint_color": list(tint_color),
            "roughness": float(roughness),
            "metallic": float(metallic),
            "bump_strength": float(bump_strength),
            "uv_scale": list(uv_scale),
            "emission_color": list(emission_color),
            "emission_strength": float(emission_strength),
            "alpha": float(alpha)
        }
        self.save_custom_shaders()
        return clean_name

    def delete_custom_shader(self, name):
        """Deletes a custom shader definition."""
        if name in self.custom_shaders:
            del self.custom_shaders[name]
            self.save_custom_shaders()
            return True
        return False

    def get_custom_shader_names(self, target_game=None):
        """Returns list of custom shader names, optionally filtered by target game."""
        if not target_game or target_game == "ALL":
            return list(self.custom_shaders.keys())
        tg = target_game.upper()
        return [
            name for name, data in self.custom_shaders.items()
            if data.get("target_game", "HALO_3").upper() in (tg, "ALL")
        ]

    # =========================================================================
    # CORE SHADER CATALOG PARSER & CATEGORIZATION
    # =========================================================================

    def load_shaders(self, filepath=None):
        """Loads and indexes all shaders from halo3_blender_shaders.txt (cached)."""
        if self.shader_set and len(self.shader_set) > 100 and (filepath is None or filepath == self._loaded_path):
            return True

        if filepath is None:
            base_dirs = [
                os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                os.path.dirname(os.path.abspath(__file__)),
                r"c:\Users\Chey1\Desktop\BlenderHalomapBase"
            ]
            for bd in base_dirs:
                candidate = os.path.join(bd, "halo3_blender_shaders.txt")
                if os.path.isfile(candidate):
                    filepath = candidate
                    break

        if not filepath or not os.path.isfile(filepath):
            return False

        if self._loaded_path == filepath and len(self.shader_set) > 100:
            return True

        self.all_shaders = []
        self.shader_set = set()
        self.solo_terrain_shaders = []
        self.reach_shaders = []
        self.h3_shaders = []
        self.levels = {}
        self.categories = {
            'ALL': [],
            'TERRAIN_GROUND': [],
            'CLIFFS_ROCKS': [],
            'SANDS_DESERT': [],
            'SNOW_ICE': [],
            'WATER_FLUIDS': [],
            'METALS_TECH': [],
            'COVENANT_ALIEN': [],
            'GLASS_TRANSPARENT': [],
            'SKIES_FX': [],
            'SPECIAL_SYMBOLS': [],
            'CUSTOM': [],
            # Legacy aliases
            'TERRAIN': [],
            'ROCK': [],
            'GRASS': [],
            'SAND': [],
            'SNOW': [],
            'WATER': [],
            'METAL': [],
            'COVENANT': [],
            'FORERUNNER': [],
            'SKY': [],
            'SPECIAL': []
        }
        self.tag_paths = {}
        self._enum_cache = {}

        self._init_defaults()

        prefix_to_level = {
            'RIVER': 'RIVERWORLD',
            'DEAD': 'DEADLOCK',
            'BOX': 'BOXCANVAS',
            'BOXCANVAS': 'BOXCANVAS',
            'CHILL': 'CHILL',
            'CONSTRUCT': 'CONSTRUCT',
            'CYBER': 'CYBERDYNE',
            'GUARDIAN': 'GUARDIAN',
            'ISOLATION': 'ISOLATION',
            'ZAN': 'ZANZIBAR',
            'SNOW': 'SNOWBOUND',
            'SAND': 'SANDBOX',
            'SHRINE': 'SHRINE',
            'SALVATION': 'SALVATION',
            'DESCENT': 'DESCENT',
            'DOCKS': 'DOCKS',
            'FORTRESS': 'FORTRESS',
            'GHOST': 'GHOSTTOWN',
            'FORGE_HALO': 'FORGE_WORLD',
            'BONEYARD': 'BONEYARD',
            'SWORD_BASE': 'SWORD_BASE',
            'JUN': '010_JUNGLE',
            'BASE': '020_BASE',
            'OUT': '030_OUTSKIRTS',
            'VOI': '040_VOI',
            'FVOI': '050_FLOODVOI',
            'CIT': '100_CITADEL',
            'HALO': '120_HALO',
            'M10': 'M10',
            'M20': 'M20',
            'M30': 'M30',
            'M35': 'M35',
            'M45': 'M45',
            'M50': 'M50',
            'M52': 'M52',
            'M60': 'M60',
            'M70': 'M70',
        }

        current_level = 'SHARED'
        current_section = 'GENERAL'

        try:
            with open(filepath, "r", encoding="utf-8") as f:
                for line in f:
                    s = line.strip()
                    if not s:
                        continue
                    s_up = s.upper()
                    if "SOLO CAMPAIGN TERRAIN" in s_up:
                        current_section = "SOLO_TERRAIN"
                        continue
                    if "HALO: REACH SHADER LIBRARY" in s_up or ("HALO: REACH" in s_up and "HREK" in s_up):
                        current_section = "REACH"
                        continue
                    if "HALO 3 & ODST SHADER LIBRARY" in s_up or ("HALO 3" in s_up and "H3EK" in s_up):
                        current_section = "H3"
                        continue
                    if s.startswith("=") or s.startswith("#") or s.startswith(";") or s.startswith("-"):
                        continue

                    m_level = re.search(r'(?:LEVEL / FOLDER|FOLDER):\s*([^\s|]+)', s)
                    if m_level:
                        lvl_name = m_level.group(1).strip().upper()
                        if '\\' in lvl_name:
                            lvl_name = lvl_name.split('\\')[-1]
                        current_level = lvl_name
                        if current_level not in self.levels:
                            self.levels[current_level] = []
                        continue

                    shader_name = None
                    tag_path = ""
                    if '|' in s:
                        parts = [p.strip() for p in s.split('|')]
                        if len(parts) >= 2 and parts[0] and not parts[0].startswith('BLENDER') and not parts[0].startswith('---'):
                            shader_name = parts[0]
                            tag_path = parts[1]
                    elif s.endswith('.shader') or s.endswith('.shader_terrain') or s.endswith('.shader_water'):
                        shader_name = s.split()[0]
                    elif not s.startswith('-') and ' ' in s:
                        parts = s.split()
                        if len(parts) == 2 and not parts[0].startswith('LEVEL') and not parts[0].startswith('Total'):
                            shader_name = s

                    if shader_name:
                        if shader_name not in self.shader_set:
                            self.all_shaders.append(shader_name)
                            self.shader_set.add(shader_name)

                        if tag_path:
                            self.tag_paths[shader_name] = tag_path

                        # Level indexing
                        if current_level not in self.levels:
                            self.levels[current_level] = []
                        if shader_name not in self.levels[current_level]:
                            self.levels[current_level].append(shader_name)

                        pref = shader_name.split()[0].upper()
                        if pref in prefix_to_level:
                            mapped = prefix_to_level[pref]
                            if mapped not in self.levels:
                                self.levels[mapped] = []
                            if shader_name not in self.levels[mapped]:
                                self.levels[mapped].append(shader_name)

                        # Reach vs H3 indexing
                        is_reach = (
                            current_section == "REACH" or
                            "reach" in tag_path.lower() or
                            pref.startswith("M") and len(pref) <= 4 or
                            pref in ("FORGE_HALO", "BONEYARD", "SWORD_BASE", "SETTLEMENT", "ISLAND", "AFTSHIP", "LAUNCH_STATION")
                        )
                        is_solo_terrain = (
                            current_section == "SOLO_TERRAIN" or
                            ("solo" in tag_path.lower() and ("terrain" in shader_name.lower() or tag_path.endswith(".shader_terrain")))
                        )

                        if is_solo_terrain and shader_name not in self.solo_terrain_shaders:
                            self.solo_terrain_shaders.append(shader_name)

                        if is_reach:
                            if shader_name not in self.reach_shaders:
                                self.reach_shaders.append(shader_name)
                        else:
                            if shader_name not in self.h3_shaders:
                                self.h3_shaders.append(shader_name)

                        # Categorize into clean functional categories
                        s_low = shader_name.lower()
                        self.categories['ALL'].append(shader_name)

                        if any(k in s_low for k in ["terrain", "ground", "dirt", "mud", "soil", "grass", "foliage", "pine", "tree", "leaf", "moss"]):
                            self.categories['TERRAIN_GROUND'].append(shader_name)
                            self.categories['TERRAIN'].append(shader_name)
                            if "grass" in s_low or "foliage" in s_low or "leaf" in s_low or "pine" in s_low:
                                self.categories['GRASS'].append(shader_name)

                        if any(k in s_low for k in ["rock", "cliff", "stone", "boulder", "crag", "slab", "basalt", "scree", "gravel"]):
                            self.categories['CLIFFS_ROCKS'].append(shader_name)
                            self.categories['ROCK'].append(shader_name)

                        if any(k in s_low for k in ["sand", "dune", "beach", "shore", "desert", "dust"]):
                            self.categories['SANDS_DESERT'].append(shader_name)
                            self.categories['SAND'].append(shader_name)

                        if any(k in s_low for k in ["snow", "ice", "glacier", "frost", "chill", "slush"]):
                            self.categories['SNOW_ICE'].append(shader_name)
                            self.categories['SNOW'].append(shader_name)

                        if any(k in s_low for k in ["water", "%water", "river", "swamp", "ocean", "lake", "fluid"]) or s_low.endswith("'"):
                            self.categories['WATER_FLUIDS'].append(shader_name)
                            self.categories['WATER'].append(shader_name)

                        if any(k in s_low for k in ["metal", "steel", "titanium", "plate", "bunker", "wall", "corridor", "forerunner", "sentinel", "construct", "cyber"]):
                            self.categories['METALS_TECH'].append(shader_name)
                            self.categories['METAL'].append(shader_name)
                            if any(k in s_low for k in ["forerunner", "sentinel", "construct"]):
                                self.categories['FORERUNNER'].append(shader_name)

                        if any(k in s_low for k in ["cov", "covenant", "plasma", "purple", "violet", "aftship", "carapace"]):
                            self.categories['COVENANT_ALIEN'].append(shader_name)
                            self.categories['COVENANT'].append(shader_name)

                        if any(k in s_low for k in ["glass", "window", "shield", "clear", "transparent", "lens"]):
                            self.categories['GLASS_TRANSPARENT'].append(shader_name)

                        if "+sky" in s_low or "sky" in s_low or "cloud" in s_low:
                            self.categories['SKIES_FX'].append(shader_name)
                            self.categories['SKY'].append(shader_name)

                        if any(s_low.startswith(p) for p in ["+", "%", "$", "@", "!", "#", "*"]):
                            self.categories['SPECIAL_SYMBOLS'].append(shader_name)
                            self.categories['SPECIAL'].append(shader_name)

            self._loaded_path = filepath
            return True
        except Exception as e:
            print(f"[SHADER MANAGER] Error loading shaders: {e}")
            return False

    def is_valid_shader(self, shader_name):
        """Checks if a shader string matches an authentic tag, user custom shader, or core system tags."""
        if not shader_name:
            return False
        clean = shader_name.strip()
        if '.' in clean and clean.rsplit('.', 1)[1].isdigit():
            clean = clean.rsplit('.', 1)[0]
        clean = clean.strip("'\"")
        if not clean:
            return False
        if clean.startswith(("+", "%", "$", "@", "!", "#", "*")):
            return True
        if clean in self.custom_shaders:
            return True
        return clean in self.shader_set or clean in self.all_shaders

    @staticmethod
    def _find_image_texture(shader_name, ek_path=None):
        """Attempts to find authentic bitmap/tif file for shader in Editing Kit data/tags directories."""
        if not ek_path:
            for candidate_ek in [
                r"C:\Program Files (x86)\Steam\steamapps\common\H3EK",
                r"C:\Program Files (x86)\Steam\steamapps\common\HREK",
                r"C:\Program Files (x86)\Steam\steamapps\common\HCEK",
            ]:
                if os.path.isdir(candidate_ek):
                    ek_path = candidate_ek
                    break
        if not ek_path or not os.path.isdir(ek_path):
            return None

        s_clean = shader_name.strip().lstrip("%!").rstrip("'\"").strip()
        parts = s_clean.split()
        tag_key = parts[-1] if parts else s_clean

        search_dirs = [
            os.path.join(ek_path, "data"),
            os.path.join(ek_path, "tags"),
        ]
        for sdir in search_dirs:
            if not os.path.isdir(sdir):
                continue
            for root, _, files in os.walk(sdir):
                for f in files:
                    f_low = f.lower()
                    if tag_key.lower() in f_low and f_low.endswith(('.tif', '.tiff', '.png', '.dds', '.jpg')):
                        full_img = os.path.join(root, f)
                        try:
                            img = bpy.data.images.load(full_img, check_existing=True)
                            return img
                        except Exception:
                            pass
        return None

    @staticmethod
    def setup_material_nodes(mat, shader_name, ek_path=None, custom_data=None):
        """Builds a complete, authentic shader node tree with 1:1 UV box mapping and vibrant Material Preview rendering."""
        if not mat:
            return
        if hasattr(mat, "use_nodes"):
            mat.use_nodes = True
        nt = mat.node_tree
        if not nt:
            return
        nodes = nt.nodes
        links = nt.links
        nodes.clear()

        # Check if shader is in custom shaders
        sm = Halo3ShaderManager.get()
        if custom_data is None and shader_name in sm.custom_shaders:
            custom_data = sm.custom_shaders[shader_name]

        effective_base = custom_data.get("base_shader", shader_name) if custom_data else shader_name

        # Output Node
        out_node = nodes.new('ShaderNodeOutputMaterial')
        out_node.location = (500, 0)

        # Principled BSDF
        bsdf = nodes.new('ShaderNodeBsdfPrincipled')
        bsdf.location = (80, 0)
        links.new(bsdf.outputs['BSDF'], out_node.inputs['Surface'])

        # TexCoord & Mapping (1:1 UV box scale)
        tex_coord = nodes.new('ShaderNodeTexCoord')
        tex_coord.location = (-850, 0)

        mapping = nodes.new('ShaderNodeMapping')
        mapping.location = (-650, 0)
        mapping.vector_type = 'POINT'
        uv_scale = (1.0, 1.0, 1.0)
        if custom_data and "uv_scale" in custom_data:
            uv_scale = tuple(custom_data["uv_scale"])
        if hasattr(mapping.inputs.get('Scale'), 'default_value'):
            mapping.inputs['Scale'].default_value = uv_scale
        links.new(tex_coord.outputs['UV'], mapping.inputs['Vector'])

        s_lower = effective_base.lower().strip()

        def set_bsdf(socket_name, val):
            if socket_name in bsdf.inputs:
                bsdf.inputs[socket_name].default_value = val

        # 1. SPECIAL ENGINE TAGS
        if '+sky' in s_lower or 'sky' in s_lower:
            set_bsdf('Base Color', (0.35, 0.55, 0.88, 1.0))
            set_bsdf('Roughness', 1.0)
            set_bsdf('Emission Color', (0.35, 0.55, 0.88, 1.0))
            set_bsdf('Emission Strength', 1.6)
            mat.diffuse_color = (0.35, 0.55, 0.88, 1.0)

        elif '%water' in s_lower or 'water' in s_lower or s_lower.endswith("'"):
            water_col = (0.04, 0.42, 0.62, 0.82)
            set_bsdf('Base Color', water_col)
            set_bsdf('Roughness', 0.08)
            set_bsdf('IOR', 1.333)
            set_bsdf('Specular IOR Level', 0.5)
            set_bsdf('Alpha', 0.82)
            set_bsdf('Transmission Weight', 0.75)
            mat.diffuse_color = (0.05, 0.45, 0.65, 0.75)

            if hasattr(mat, 'blend_method'):
                mat.blend_method = 'BLEND'
            if hasattr(mat, 'shadow_method'):
                mat.shadow_method = 'NONE'
            if hasattr(mat, 'show_transparent_back'):
                mat.show_transparent_back = True

            tex_wave = nodes.new('ShaderNodeTexWave')
            tex_wave.location = (-400, -200)
            tex_wave.inputs['Scale'].default_value = 12.0
            tex_wave.inputs['Distortion'].default_value = 2.5
            links.new(mapping.outputs['Vector'], tex_wave.inputs['Vector'])

            bump = nodes.new('ShaderNodeBump')
            bump.location = (-150, -200)
            bump.inputs['Strength'].default_value = 0.22
            links.new(tex_wave.outputs['Color'], bump.inputs['Height'])
            links.new(bump.outputs['Normal'], bsdf.inputs['Normal'])

        elif any(k in s_lower for k in ['+portal', '!portal', '+seam', '+weatherpoly', '+sound']):
            is_vis = '!portal' in s_lower or '+portal' in s_lower
            p_col = (0.95, 0.48, 0.12, 0.45) if is_vis else (0.2, 0.8, 0.9, 0.45)
            set_bsdf('Base Color', p_col)
            set_bsdf('Alpha', 0.50)
            set_bsdf('Emission Color', p_col)
            set_bsdf('Emission Strength', 0.8)
            mat.diffuse_color = p_col
            if hasattr(mat, 'blend_method'):
                mat.blend_method = 'BLEND'

        # 2. METAL & FORERUNNER & COVENANT
        elif any(k in s_lower for k in ['covenant', 'plasma', 'purple', 'violet', 'aftship']):
            set_bsdf('Base Color', (0.18, 0.08, 0.34, 1.0))
            set_bsdf('Metallic', 0.75)
            set_bsdf('Roughness', 0.22)
            mat.diffuse_color = (0.22, 0.10, 0.38, 1.0)

        elif any(k in s_lower for k in ['forerunner', 'silver', 'chrome', 'sentinel', 'halo_plate', 'construct']):
            set_bsdf('Base Color', (0.68, 0.70, 0.74, 1.0))
            set_bsdf('Metallic', 0.92)
            set_bsdf('Roughness', 0.16)
            set_bsdf('Emission Color', (0.1, 0.75, 1.0, 1.0))
            set_bsdf('Emission Strength', 0.4)
            mat.diffuse_color = (0.68, 0.70, 0.74, 1.0)

        elif any(k in s_lower for k in ['metal', 'steel', 'titanium', 'plate', 'bunker', 'wall', 'corridor']):
            set_bsdf('Base Color', (0.26, 0.28, 0.30, 1.0))
            set_bsdf('Metallic', 0.82)
            set_bsdf('Roughness', 0.32)
            mat.diffuse_color = (0.28, 0.30, 0.32, 1.0)

        # 3. NATURAL SURFACES (Cliff, Snow, Sand, Grass, Terrain)
        elif any(k in s_lower for k in ['snow', 'ice', 'glacier']):
            set_bsdf('Base Color', (0.82, 0.88, 0.94, 1.0))
            set_bsdf('Roughness', 0.75)
            mat.diffuse_color = (0.82, 0.88, 0.94, 1.0)
            noise_node = nodes.new('ShaderNodeTexNoise')
            noise_node.location = (-400, -200)
            noise_node.inputs['Scale'].default_value = 28.0
            links.new(mapping.outputs['Vector'], noise_node.inputs['Vector'])
            bump = nodes.new('ShaderNodeBump')
            bump.location = (-150, -200)
            bump.inputs['Strength'].default_value = 0.25
            links.new(noise_node.outputs['Fac'], bump.inputs['Height'])
            links.new(bump.outputs['Normal'], bsdf.inputs['Normal'])

        elif any(k in s_lower for k in ['sand', 'dune', 'beach', 'shore']):
            set_bsdf('Base Color', (0.72, 0.62, 0.44, 1.0))
            set_bsdf('Roughness', 0.92)
            mat.diffuse_color = (0.72, 0.62, 0.44, 1.0)
            noise_node = nodes.new('ShaderNodeTexNoise')
            noise_node.location = (-400, -200)
            noise_node.inputs['Scale'].default_value = 35.0
            links.new(mapping.outputs['Vector'], noise_node.inputs['Vector'])
            bump = nodes.new('ShaderNodeBump')
            bump.location = (-150, -200)
            bump.inputs['Strength'].default_value = 0.18
            links.new(noise_node.outputs['Fac'], bump.inputs['Height'])
            links.new(bump.outputs['Normal'], bsdf.inputs['Normal'])

        elif any(k in s_lower for k in ['cliff', 'rock', 'stone', 'boulder', 'crag']):
            set_bsdf('Base Color', (0.26, 0.26, 0.28, 1.0))
            set_bsdf('Roughness', 0.85)
            mat.diffuse_color = (0.28, 0.28, 0.30, 1.0)
            noise_node = nodes.new('ShaderNodeTexNoise')
            noise_node.location = (-400, -200)
            noise_node.inputs['Scale'].default_value = 18.0
            noise_node.inputs['Detail'].default_value = 5.0
            links.new(mapping.outputs['Vector'], noise_node.inputs['Vector'])
            bump = nodes.new('ShaderNodeBump')
            bump.location = (-150, -200)
            bump.inputs['Strength'].default_value = 0.45
            links.new(noise_node.outputs['Fac'], bump.inputs['Height'])
            links.new(bump.outputs['Normal'], bsdf.inputs['Normal'])

        elif any(k in s_lower for k in ['tree', 'bark', 'wood']):
            set_bsdf('Base Color', (0.22, 0.16, 0.11, 1.0))
            set_bsdf('Roughness', 0.90)
            mat.diffuse_color = (0.24, 0.18, 0.12, 1.0)

        elif any(k in s_lower for k in ['foliage', 'pine', 'needle', 'leaf', 'leaves', 'bush']):
            set_bsdf('Base Color', (0.10, 0.26, 0.06, 1.0))
            set_bsdf('Roughness', 0.60)
            mat.diffuse_color = (0.12, 0.28, 0.08, 1.0)

        else:
            mat.diffuse_color = (0.22, 0.38, 0.14, 1.0)
            set_bsdf('Roughness', 0.80)

            c_dirt = (0.32, 0.24, 0.16, 1.0)
            c_grass = (0.16, 0.38, 0.10, 1.0)
            c_rock = (0.26, 0.26, 0.28, 1.0)
            c_sand = (0.70, 0.60, 0.42, 1.0)

            img = Halo3ShaderManager._find_image_texture(effective_base, ek_path)
            if img:
                tex_img = nodes.new('ShaderNodeTexImage')
                tex_img.location = (-400, 100)
                tex_img.image = img
                links.new(mapping.outputs['Vector'], tex_img.inputs['Vector'])
                links.new(tex_img.outputs['Color'], bsdf.inputs['Base Color'])
            else:
                attr_node = None
                try:
                    attr_node = nodes.new('ShaderNodeAttribute')
                    attr_node.attribute_name = "Terrain_Layers"
                    attr_node.location = (-950, 150)
                except Exception:
                    pass

                noise_tex = nodes.new('ShaderNodeTexNoise')
                noise_tex.location = (-650, 200)
                noise_tex.inputs['Scale'].default_value = 24.0
                links.new(mapping.outputs['Vector'], noise_tex.inputs['Vector'])

                if attr_node and 'Color' in attr_node.outputs:
                    sep = nodes.new('ShaderNodeSeparateColor')
                    sep.location = (-700, -100)
                    links.new(attr_node.outputs['Color'], sep.inputs['Color'])

                    m1, out1 = _safe_mix_color_node(nodes, links, (-500, 50), sep.outputs['Red'], c_dirt, c_grass)
                    m2, out2 = _safe_mix_color_node(nodes, links, (-300, 50), sep.outputs['Green'], c_dirt, c_rock)
                    links.new(out1, m2.inputs[6] if len(m2.inputs) > 6 else m2.inputs['Color1'])
                    m3, out3 = _safe_mix_color_node(nodes, links, (-100, 50), sep.outputs['Blue'], c_dirt, c_sand)
                    links.new(out2, m3.inputs[6] if len(m3.inputs) > 6 else m3.inputs['Color1'])
                    links.new(out3, bsdf.inputs['Base Color'])
                else:
                    m_proc, out_proc = _safe_mix_color_node(nodes, links, (-300, 50), noise_tex.outputs['Fac'], c_grass, c_dirt)
                    links.new(out_proc, bsdf.inputs['Base Color'])

                bump = nodes.new('ShaderNodeBump')
                bump.location = (-150, -200)
                bump.inputs['Strength'].default_value = 0.30
                links.new(noise_tex.outputs['Fac'], bump.inputs['Height'])
                links.new(bump.outputs['Normal'], bsdf.inputs['Normal'])

        # Apply Custom Overrides
        if custom_data:
            if "tint_color" in custom_data:
                tc = tuple(custom_data["tint_color"])
                set_bsdf('Base Color', tc)
                mat.diffuse_color = tc
            if "roughness" in custom_data:
                set_bsdf('Roughness', float(custom_data["roughness"]))
            if "metallic" in custom_data:
                set_bsdf('Metallic', float(custom_data["metallic"]))
            if "emission_color" in custom_data:
                set_bsdf('Emission Color', tuple(custom_data["emission_color"]))
            if "emission_strength" in custom_data:
                set_bsdf('Emission Strength', float(custom_data["emission_strength"]))
            if "alpha" in custom_data:
                set_bsdf('Alpha', float(custom_data["alpha"]))
            # Bump Strength adjustment
            if "bump_strength" in custom_data:
                b_str = float(custom_data["bump_strength"])
                for n in nodes:
                    if n.type == 'BUMP' and 'Strength' in n.inputs:
                        n.inputs['Strength'].default_value = b_str

    @staticmethod
    def get_or_create_material(name, setup_nodes=True, ek_path=None):
        """Gets existing material or creates a new one with node tree configured."""
        clean_name = name.strip()
        mat = bpy.data.materials.get(clean_name)
        if not mat:
            mat = bpy.data.materials.new(name=clean_name)
            if setup_nodes:
                Halo3ShaderManager.setup_material_nodes(mat, clean_name, ek_path=ek_path)
        elif setup_nodes and (not getattr(mat, "node_tree", None) or not mat.node_tree.nodes):
            Halo3ShaderManager.setup_material_nodes(mat, clean_name, ek_path=ek_path)
        return mat

    @staticmethod
    def apply_shader_to_slot(mesh, shader_name):
        """Helper to create and assign shader material to mesh object slot."""
        if not mesh:
            return None
        mat = Halo3ShaderManager.get_or_create_material(shader_name)
        if mat.name not in [m.name for m in mesh.materials if m]:
            mesh.materials.append(mat)
        return mat

    def apply_shader_to_object(self, obj, shader_name, assign_to_selected_faces=False, fit_uv=False, ek_path=None):
        """Applies shader material to object with 1:1 UV box mapping, preserving +sky ceiling/walls."""
        mat = self.get_or_create_material(shader_name, setup_nodes=True, ek_path=ek_path)
        if not obj or obj.type != 'MESH':
            return mat

        mat_names = [m.name for m in obj.data.materials if m]
        if mat.name not in mat_names:
            obj.data.materials.append(mat)
        m_idx = obj.data.materials.find(mat.name)

        is_terrain_obj = (
            "terrain" in obj.name.lower() or "gulch" in obj.name.lower() or
            "valley" in obj.name.lower() or "canyon" in obj.name.lower() or
            "island" in obj.name.lower() or any(m and "+sky" in m.name for m in obj.data.materials if m)
        )

        if not assign_to_selected_faces:
            for p in obj.data.polygons:
                if is_terrain_obj and shader_name != "+sky":
                    if p.material_index < len(obj.data.materials):
                        old_m = obj.data.materials[p.material_index]
                        if old_m and "+sky" in old_m.name:
                            continue
                p.material_index = m_idx
        else:
            for p in obj.data.polygons:
                if p.select:
                    p.material_index = m_idx

        if fit_uv:
            self.fit_object_uv_to_box(obj)

        return mat

    @staticmethod
    def fit_object_uv_to_box(obj, flip_v=False):
        """Scales active UV layer of object so coordinates fit 1:1 in [0, 1] UV box."""
        if not obj or obj.type != 'MESH' or not obj.data.uv_layers:
            return False
        uv_layer = obj.data.uv_layers.active
        if not uv_layer or not obj.data.vertices:
            return False

        min_x = min(v.co.x for v in obj.data.vertices)
        max_x = max(v.co.x for v in obj.data.vertices)
        min_y = min(v.co.y for v in obj.data.vertices)
        max_y = max(v.co.y for v in obj.data.vertices)

        range_x = max(0.001, max_x - min_x)
        range_y = max(0.001, max_y - min_y)

        for poly in obj.data.polygons:
            for loop_idx in poly.loop_indices:
                v_idx = obj.data.loops[loop_idx].vertex_index
                co = obj.data.vertices[v_idx].co
                u = (co.x - min_x) / range_x
                v = (co.y - min_y) / range_y
                if flip_v:
                    v = 1.0 - v
                uv_layer.data[loop_idx].uv = Vector((u, v))
        return True

    def paint_blend_layer(self, obj, hit_loc, radius=200.0, layer="LAYER_1", strength=1.0):
        """Paints vertex color blend layer weights on 'Terrain_Layers' attribute within radius."""
        if not obj or obj.type != 'MESH':
            return 0
        mesh = obj.data
        color_layer = mesh.color_attributes.get("Terrain_Layers")
        if not color_layer:
            color_layer = mesh.color_attributes.new("Terrain_Layers", 'FLOAT_COLOR', 'CORNER')

        hit_v = Vector(hit_loc) if not isinstance(hit_loc, Vector) else hit_loc
        affected = 0

        target_channel = 0
        if layer == "LAYER_0": target_channel = 0
        elif layer == "LAYER_1": target_channel = 0
        elif layer == "LAYER_2": target_channel = 1
        elif layer == "LAYER_3": target_channel = 2
        elif layer == "LAYER_4": target_channel = 3

        mw = obj.matrix_world
        verts_co = [mw @ v.co for v in mesh.vertices]
        v_weights = {}
        for idx, co in enumerate(verts_co):
            dist = (co - hit_v).length
            if dist <= radius:
                w = (1.0 - (dist / max(1.0, radius))) * max(0.01, strength)
                v_weights[idx] = min(1.0, w)

        if not v_weights:
            for idx in range(min(50, len(mesh.vertices))):
                v_weights[idx] = 1.0

        for loop_idx, loop in enumerate(mesh.loops):
            v_idx = loop.vertex_index
            if v_idx in v_weights:
                w = v_weights[v_idx]
                curr = list(color_layer.data[loop_idx].color)
                curr[target_channel] = min(1.0, max(0.0, curr[target_channel] + w))
                color_layer.data[loop_idx].color = curr
                affected += 1

        mesh.update()
        return affected

    def paint_faces_with_shader(self, obj, face_indices, shader_name, fit_uv=False):
        """Applies shader material to specific face indices of mesh."""
        if not obj or obj.type != 'MESH' or not face_indices:
            return False
        mat = self.get_or_create_material(shader_name)
        if mat.name not in [m.name for m in obj.data.materials if m]:
            obj.data.materials.append(mat)
        m_idx = obj.data.materials.find(mat.name)
        for f_idx in face_indices:
            if f_idx < len(obj.data.polygons):
                obj.data.polygons[f_idx].material_index = m_idx
        if fit_uv:
            self.fit_object_uv_to_box(obj)
        return True

    def get_shaders_for_game(self, target_game="HALO_3", category=None):
        """Returns list of shaders filtered for the target game and optional category."""
        if not self.all_shaders:
            self.load_shaders()

        tg = target_game.upper()
        if tg in ("HALO_REACH", "REACH"):
            pool = list(self.reach_shaders) if self.reach_shaders else list(self.all_shaders)
        elif tg in ("HALO_3", "H3"):
            pool = list(self.h3_shaders) if self.h3_shaders else list(self.all_shaders)
        elif tg in ("HALO_CE", "CE"):
            pool = ["ground", "cliff", "sand", "rock", "water", "snow", "+sky", "!portal", "+portal"]
        elif tg in ("HALO_2", "H2"):
            pool = ["coagulation ground_grass", "coagulation rock_cliff", "zanzibar beach_sand", "water", "+sky", "!portal", "+portal"]
        else:
            pool = list(self.all_shaders)

        # Include custom shaders matching game
        custom_names = self.get_custom_shader_names(tg)

        if category == "CUSTOM":
            return custom_names if custom_names else ["(No Custom Shaders Saved)"]

        if not category or category == "ALL":
            return custom_names + pool

        cat_upper = category.upper()
        # Canonical & legacy map
        cat_map = {
            "TERRAIN": "TERRAIN_GROUND",
            "GRASS": "TERRAIN_GROUND",
            "ROCK": "CLIFFS_ROCKS",
            "SAND": "SANDS_DESERT",
            "SNOW": "SNOW_ICE",
            "WATER": "WATER_FLUIDS",
            "METAL": "METALS_TECH",
            "FORERUNNER": "METALS_TECH",
            "COVENANT": "COVENANT_ALIEN",
            "SKY": "SKIES_FX",
            "SPECIAL": "SPECIAL_SYMBOLS"
        }
        canonical_key = cat_map.get(cat_upper, cat_upper)
        cat_list = self.categories.get(canonical_key, [])
        if not cat_list:
            cat_list = self.categories.get(cat_upper, [])

        filtered_custom = [
            c_name for c_name in custom_names
            if self.custom_shaders.get(c_name, {}).get("category", "") in (canonical_key, cat_upper)
        ]

        if cat_list:
            filtered = [s for s in pool if s in cat_list or s in self.special_shaders]
            return filtered_custom + (filtered if filtered else pool)

        return filtered_custom + pool

    def get_enum_items(self, context, category=None, target_game=None):
        """Generates dynamic EnumProperty items for list picker dropdowns in Blender UI."""
        if not self.all_shaders:
            self.load_shaders()

        props = getattr(context.scene, "halo3_tool_props", None) if context else None
        if not target_game and props:
            target_game = props.target_game
        if not target_game:
            target_game = "HALO_3"

        cache_key = f"{target_game}_{category}"
        if cache_key in self._enum_cache:
            return self._enum_cache[cache_key]

        pool = self.get_shaders_for_game(target_game, category)
        items = []
        for idx, s in enumerate(pool[:250]):  # Cap to top 250 for crisp UI performance
            desc = "User Custom Shader" if s in self.custom_shaders else f"Authentic Halo shader tag: {s}"
            items.append((s, s, desc))

        if not items:
            items = [("river riverworld_ground", "river riverworld_ground", "Default Halo ground shader")]

        self._enum_cache[cache_key] = items
        return items


# Global callback helpers for Blender EnumProperty list pickers
def get_ground_shader_enum(self, context):
    sm = Halo3ShaderManager.get()
    return sm.get_enum_items(context, category="TERRAIN_GROUND", target_game=getattr(self, "target_game", "HALO_3"))

def get_cliff_shader_enum(self, context):
    sm = Halo3ShaderManager.get()
    return sm.get_enum_items(context, category="CLIFFS_ROCKS", target_game=getattr(self, "target_game", "HALO_3"))

def get_water_shader_enum(self, context):
    sm = Halo3ShaderManager.get()
    return sm.get_enum_items(context, category="WATER_FLUIDS", target_game=getattr(self, "target_game", "HALO_3"))

def get_foliage_shader_enum(self, context):
    sm = Halo3ShaderManager.get()
    return sm.get_enum_items(context, category="TERRAIN_GROUND", target_game=getattr(self, "target_game", "HALO_3"))

def get_bark_shader_enum(self, context):
    sm = Halo3ShaderManager.get()
    return sm.get_enum_items(context, category="CLIFFS_ROCKS", target_game=getattr(self, "target_game", "HALO_3"))

def get_active_shader_enum(self, context):
    sm = Halo3ShaderManager.get()
    cat = getattr(self, "shader_category_filter", "ALL")
    return sm.get_enum_items(context, category=cat, target_game=getattr(self, "target_game", "HALO_3"))

def get_custom_shaders_enum(self, context):
    sm = Halo3ShaderManager.get()
    tg = getattr(self, "target_game", "HALO_3")
    names = sm.get_custom_shader_names(tg)
    if not names:
        return [("NONE", "(No Custom Shaders)", "Save a custom shader using the palette controls below")]
    return [(n, n, f"Custom shader based on {sm.custom_shaders.get(n, {}).get('base_shader', 'Halo')}") for n in names]


# =============================================================================
# BLENDER OPERATORS FOR CUSTOM SHADER MANAGEMENT
# =============================================================================

class HALO3_OT_save_custom_shader(bpy.types.Operator):
    """Saves modified shader settings as a permanent custom shader in user palette"""
    bl_idname = "halo3.save_custom_shader"
    bl_label = "Save to Custom Shaders"
    bl_description = "Saves current shader adjustments and colors into the persistent custom palette"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        props = getattr(context.scene, "halo3_tool_props", None)
        if not props:
            self.report({'ERROR'}, "Tool properties not available")
            return {'CANCELLED'}

        sm = Halo3ShaderManager.get()
        name = props.custom_shader_name.strip()
        if not name:
            name = f"Custom_{props.active_shader.replace(' ', '_')}"

        sm.register_custom_shader(
            name=name,
            base_shader=props.active_shader,
            target_game=props.target_game,
            category=props.shader_category_filter if props.shader_category_filter != "ALL" else "CLIFFS_ROCKS",
            tint_color=props.custom_shader_tint,
            roughness=props.custom_shader_roughness,
            metallic=props.custom_shader_metallic,
            bump_strength=props.custom_shader_bump,
            uv_scale=props.custom_shader_uv_scale
        )

        # Refresh material on active object if present
        if context.active_object and context.active_object.type == 'MESH':
            sm.apply_shader_to_object(context.active_object, name)

        self.report({'INFO'}, f"Saved custom shader: {name}")
        return {'FINISHED'}


class HALO3_OT_apply_custom_shader(bpy.types.Operator):
    """Applies selected custom shader to active object or selection"""
    bl_idname = "halo3.apply_custom_shader"
    bl_label = "Apply Custom Shader"
    bl_description = "Applies the selected custom shader to the active mesh or selected faces"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        props = getattr(context.scene, "halo3_tool_props", None)
        if not props or not props.selected_custom_shader or props.selected_custom_shader == "NONE":
            self.report({'WARNING'}, "No custom shader selected")
            return {'CANCELLED'}

        obj = context.active_object
        if not obj or obj.type != 'MESH':
            self.report({'WARNING'}, "Select a mesh object first")
            return {'CANCELLED'}

        sm = Halo3ShaderManager.get()
        sm.apply_shader_to_object(obj, props.selected_custom_shader, fit_uv=True)
        self.report({'INFO'}, f"Applied custom shader: {props.selected_custom_shader}")
        return {'FINISHED'}


class HALO3_OT_delete_custom_shader(bpy.types.Operator):
    """Deletes selected custom shader from persistent palette"""
    bl_idname = "halo3.delete_custom_shader"
    bl_label = "Delete Custom Shader"
    bl_description = "Removes the selected custom shader from custom_shaders.json"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        props = getattr(context.scene, "halo3_tool_props", None)
        if not props or not props.selected_custom_shader or props.selected_custom_shader == "NONE":
            self.report({'WARNING'}, "No custom shader selected to delete")
            return {'CANCELLED'}

        target = props.selected_custom_shader
        sm = Halo3ShaderManager.get()
        if sm.delete_custom_shader(target):
            self.report({'INFO'}, f"Deleted custom shader: {target}")
            return {'FINISHED'}
        self.report({'WARNING'}, f"Could not find {target} in custom shaders")
        return {'CANCELLED'}


SHADER_OPERATORS = [
    HALO3_OT_save_custom_shader,
    HALO3_OT_apply_custom_shader,
    HALO3_OT_delete_custom_shader,
]

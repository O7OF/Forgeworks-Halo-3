# -*- coding: utf-8 -*-
"""
Halo Editing Kit Tool & Tool-Fast Compiler Orchestrator
Handles Map Structures, Scenery, Crates, Vehicles, Weapons, and Physics.
Supports HCEEK, H2EK, H3EK, and HREK with auto-detection and accurate syntax.
Created by O7OF | Shoutout to c20 Reclaimers Wiki | Thank you Bungie
"""

import os
import subprocess
import time
import bpy

from . import constants
from .export_ass import export_ass_file
from .export_jms import export_jms_file
from .export_jmi import export_jmi_file
from .export_gr2 import export_gr2_file, HaloReachGr2Exporter


class HaloCompiler:
    """Manages compilation of Halo assets using tool.exe / tool_fast.exe."""

    @staticmethod
    def auto_detect_ek_path(target_game="HALO_3"):
        """Auto-detects authentic Steam MCC Editing Kit path if not explicitly configured."""
        game_dirs = {
            "HALO_CE": "HCEEK",
            "HALO_2": "H2EK",
            "HALO_3": "H3EK",
            "HALO_REACH": "HREK",
        }
        folder = game_dirs.get(target_game, "H3EK")
        search_roots = HaloCompiler._steam_library_roots() + [
            r"C:\Program Files (x86)\Steam\steamapps\common",
            r"C:\SteamLibrary\steamapps\common",
            r"D:\SteamLibrary\steamapps\common",
            r"E:\SteamLibrary\steamapps\common",
        ]
        for root in search_roots:
            candidate = os.path.join(root, folder)
            if os.path.isdir(candidate):
                if any(os.path.isfile(os.path.join(candidate, n)) for n in ("tool.exe", "tool_fast.exe")):
                    return candidate
        return ""

    @staticmethod
    def _steam_library_roots():
        """Reads Steam's registry path + libraryfolders.vdf to find every library 'common' folder."""
        roots = []
        steam_path = ""
        try:
            import winreg
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam") as k:
                steam_path = winreg.QueryValueEx(k, "SteamPath")[0]
        except Exception:
            pass
        if not steam_path:
            return roots
        steam_path = os.path.normpath(steam_path)
        roots.append(os.path.join(steam_path, "steamapps", "common"))
        vdf = os.path.join(steam_path, "steamapps", "libraryfolders.vdf")
        try:
            import re
            with open(vdf, "r", encoding="utf-8", errors="replace") as f:
                for m in re.finditer(r'"path"\s+"([^"]+)"', f.read()):
                    p = os.path.join(m.group(1).replace("\\\\", "\\"), "steamapps", "common")
                    if p not in roots:
                        roots.append(p)
        except Exception:
            pass
        return roots

    get_editing_kit_path = auto_detect_ek_path

    @staticmethod
    def ensure_level_root(context=None):
        """Ensures b_levelroot frame node exists at origin (0, 0, 0) per official Bungie/MCC guidelines."""
        root = bpy.data.objects.get("b_levelroot")
        if not root:
            root = bpy.data.objects.new("b_levelroot", None)
            root.location = (0.0, 0.0, 0.0)
            root.empty_display_type = 'ARROWS'
            root.empty_display_size = 100.0
            scene = context.scene if context else bpy.context.scene
            scene.collection.objects.link(root)
        return root

    @staticmethod
    def parent_to_level_root(obj, context=None):
        """Parents an object to b_levelroot preserving its world matrix transform."""
        if not obj or obj.name == "b_levelroot" or obj.name == "scale_model_spartan":
            return
        root = HaloCompiler.ensure_level_root(context)
        if obj.parent != root:
            loc = obj.location.copy()
            rot = obj.rotation_euler.copy()
            scale = obj.scale.copy()
            obj.parent = root
            obj.matrix_parent_inverse = root.matrix_world.inverted()
            obj.location = loc
            obj.rotation_euler = rot
            obj.scale = scale

    @staticmethod
    def get_tool_executable(ek_path, use_fast=True):
        """Resolves tool executable path (prefers tool_fast.exe when requested)."""
        tool_name = "tool_fast.exe" if use_fast else "tool.exe"
        exe_path = os.path.join(ek_path, tool_name)
        if not os.path.isfile(exe_path):
            exe_path = os.path.join(ek_path, "tool.exe")
        return exe_path if os.path.isfile(exe_path) else None

    @staticmethod
    def setup_directories(ek_path, asset_type, asset_name, target_game="HALO_3", scenario_type="multi", sub_dir=""):
        """Creates the required data and tags folder structure in the Editing Kit per game title."""
        game_preset = constants.HALO_GAME_PRESETS.get(target_game, constants.HALO_GAME_PRESETS["HALO_3"])
        prefix = game_preset.get("folder_prefix", "levels")

        if asset_type == "MAP_STRUCTURE":
            if target_game == "HALO_CE":
                data_rel = os.path.join(prefix, asset_name, "models")
                tags_rel = os.path.join(prefix, asset_name)
                # Also create bitmaps folder for CE
                os.makedirs(os.path.join(ek_path, "data", prefix, asset_name, "bitmaps"), exist_ok=True)
            elif target_game == "HALO_2":
                # H2 uses scenarios folder hierarchy
                data_rel = os.path.join("scenarios", scenario_type, asset_name, "structure")
                tags_rel = os.path.join("scenarios", scenario_type, asset_name)
                os.makedirs(os.path.join(ek_path, "data", "scenarios", scenario_type, asset_name, "bitmaps"), exist_ok=True)
            elif target_game == "HALO_REACH":
                # Reach uses models/ and export/models/ for sidecar workflow
                data_rel = os.path.join(prefix, scenario_type, asset_name, "models")
                tags_rel = os.path.join(prefix, scenario_type, asset_name)
                os.makedirs(os.path.join(ek_path, "data", prefix, scenario_type, asset_name, "export", "models"), exist_ok=True)
                os.makedirs(os.path.join(ek_path, "data", prefix, scenario_type, asset_name, "bitmaps"), exist_ok=True)
            else:  # HALO_3
                data_rel = os.path.join(prefix, scenario_type, asset_name, "structure")
                tags_rel = os.path.join(prefix, scenario_type, asset_name)
                os.makedirs(os.path.join(ek_path, "data", prefix, scenario_type, asset_name, "bitmaps"), exist_ok=True)
        elif asset_type == "SCENERY":
            category = sub_dir.strip() or "scenery"
            data_rel = os.path.join("objects", category, asset_name)
            tags_rel = os.path.join("objects", category, asset_name)
        elif asset_type == "CRATE":
            category = sub_dir.strip() or "crates"
            data_rel = os.path.join("objects", category, asset_name)
            tags_rel = os.path.join("objects", category, asset_name)
        elif asset_type == "VEHICLE":
            category = sub_dir.strip() or "vehicles"
            data_rel = os.path.join("objects", category, asset_name)
            tags_rel = os.path.join("objects", category, asset_name)
        elif asset_type == "WEAPON":
            category = sub_dir.strip() or "weapons"
            data_rel = os.path.join("objects", category, asset_name)
            tags_rel = os.path.join("objects", category, asset_name)
        else:
            data_rel = os.path.join("objects", "custom", asset_name)
            tags_rel = os.path.join("objects", "custom", asset_name)

        full_data = os.path.join(ek_path, "data", data_rel)
        full_tags = os.path.join(ek_path, "tags", tags_rel)
        os.makedirs(full_data, exist_ok=True)
        os.makedirs(full_tags, exist_ok=True)

        return full_data, full_tags, data_rel

    @staticmethod
    def export_and_compile(context, operator=None):
        """Unified 1-click exporter and compiler for Maps, Scenery, Crates, Vehicles, and Weapons."""
        props = context.scene.halo3_tool_props
        target_game = props.target_game
        ek_path = props.editing_kit_path.strip().strip('"')
        if ek_path and os.path.isfile(ek_path):
            # User pointed at tool.exe itself - use its folder.
            ek_path = os.path.dirname(ek_path)

        # Auto-detect if not set or invalid
        if not ek_path or not os.path.isdir(ek_path):
            detected = HaloCompiler.auto_detect_ek_path(target_game)
            if detected:
                ek_path = detected
                props.editing_kit_path = detected

        if not ek_path or not os.path.isdir(ek_path):
            msg = f"Invalid Editing Kit path for {target_game}: '{ek_path}'. Please install {target_game} Editing Kit via Steam or configure path."
            props.last_compile_status = f"FAILED: {msg}"
            if operator:
                operator.report({'ERROR'}, msg)
            return {'CANCELLED'}

        tool_exe = HaloCompiler.get_tool_executable(ek_path, use_fast=props.tool_fast_mode)
        if not tool_exe:
            msg = f"Neither tool_fast.exe nor tool.exe found in '{ek_path}'"
            if operator:
                operator.report({'ERROR'}, msg)
            return {'CANCELLED'}

        asset_type = getattr(props, "export_asset_type", "MAP_STRUCTURE")
        asset_name = props.map_name.strip() or "custom_asset"
        scenario_type = props.map_scenario_type
        game_preset = constants.HALO_GAME_PRESETS.get(target_game, constants.HALO_GAME_PRESETS["HALO_3"])

        # 1. Setup Folders
        data_dir, tags_dir, data_rel = HaloCompiler.setup_directories(
            ek_path=ek_path,
            asset_type=asset_type,
            asset_name=asset_name,
            target_game=target_game,
            scenario_type=scenario_type,
            sub_dir=getattr(props, "export_sub_folder", "")
        )

        # 2. Export asset files based on target game and asset type
        export_success = False
        ass_ver = game_preset.get("default_ass_ver", 3)
        jms_ver = game_preset.get("default_jms_ver", 8213)
        use_toolkit = getattr(props, "export_use_toolkit", True)
        game_title = {"HALO_CE": "halo1", "HALO_2": "halo2", "HALO_3": "halo3", "HALO_REACH": "haloreach"}.get(target_game, "halo3")
        commands = []
        target_file = ""

        if asset_type == "MAP_STRUCTURE":
            if target_game == "HALO_REACH":
                # Reach uses FBX -> GR2 + Sidecar XML import
                target_file = os.path.join(data_dir, f"{asset_name}.gr2")
                export_success = export_gr2_file(
                    filepath=target_file,
                    context=context,
                    scale=1.0,
                    apply_modifiers=True,
                    triangulate=True,
                    hrek_path=ek_path,
                    scenario_type=scenario_type,
                    bsp_name="bsp01"
                )
                # Build Reach tool commands
                asset_dir_rel = f"levels\\{scenario_type}\\{asset_name}"
                fbx_rel = f"{asset_dir_rel}\\models\\{asset_name}.fbx"
                json_rel = f"{asset_dir_rel}\\models\\{asset_name}.json"
                gr2_rel = f"{asset_dir_rel}\\models\\{asset_name}.gr2"
                sidecar_rel = f"{asset_dir_rel}\\{asset_name}.sidecar.xml"

                commands.append([tool_exe, "fbx-to-gr2", fbx_rel, json_rel, gr2_rel, "false"])
                commands.append([tool_exe, "import", sidecar_rel, "force", "farm_bsp"])

            elif target_game == "HALO_CE":
                target_file = os.path.join(data_dir, f"{asset_name}.jms")
                export_success = export_jms_file(target_file, context, version=jms_ver, game_title="halo1", use_toolkit=use_toolkit)
                commands.append([tool_exe, "structure", f"levels\\{asset_name}", asset_name])

            elif target_game == "HALO_2":
                target_file = os.path.join(data_dir, f"{asset_name}.ass")
                export_success = export_ass_file(target_file, context, version=ass_ver, game_title="halo2", use_toolkit=use_toolkit)
                commands.append([tool_exe, "structure", f"scenarios\\{scenario_type}\\{asset_name}", asset_name])

            else:  # HALO_3
                target_file = os.path.join(data_dir, f"{asset_name}.ass")
                export_success = export_ass_file(target_file, context, version=ass_ver, game_title="halo3", use_toolkit=use_toolkit)
                # H3EK tool structure requires relative path in data/ with .ass inside \structure\ directory
                ass_rel_to_data = f"{data_rel}\\{asset_name}.ass"
                commands.append([tool_exe, "structure", ass_rel_to_data])

        else:
            # Objects (Scenery, Crates, Vehicles, Weapons)
            if target_game == "HALO_REACH":
                target_file = os.path.join(data_dir, f"{asset_name}.gr2")
                export_success = export_gr2_file(
                    filepath=target_file,
                    context=context,
                    scale=1.0,
                    hrek_path=ek_path,
                    scenario_type=scenario_type
                )
                sidecar_rel = f"{data_rel}\\{asset_name}.sidecar.xml"
                commands.append([tool_exe, "import", sidecar_rel, "force"])
            else:
                render_dir = os.path.join(data_dir, "render")
                os.makedirs(render_dir, exist_ok=True)
                target_file = os.path.join(render_dir, f"{asset_name}.ass")
                export_success = export_ass_file(target_file, context, version=ass_ver, game_title=game_title, use_toolkit=use_toolkit)

                col_objs = [o for o in context.scene.objects if o.type == 'MESH' and (o.name.startswith("@") or "collision" in o.name.lower())]
                if col_objs:
                    col_dir = os.path.join(data_dir, "collision")
                    os.makedirs(col_dir, exist_ok=True)
                    export_jms_file(os.path.join(col_dir, f"{asset_name}.jms"), context, version=jms_ver, game_title=game_title, use_toolkit=use_toolkit)

                phys_objs = [o for o in context.scene.objects if o.type == 'MESH' and (o.name.startswith("$") or "physics" in o.name.lower())]
                if phys_objs:
                    phys_dir = os.path.join(data_dir, "physics")
                    os.makedirs(phys_dir, exist_ok=True)
                    export_jms_file(os.path.join(phys_dir, f"{asset_name}.jms"), context, version=jms_ver, game_title=game_title, use_toolkit=use_toolkit)

                compile_mode = getattr(props, "object_compile_mode", "RENDER_ALL")
                if compile_mode in ("RENDER_ONLY", "RENDER_ALL"):
                    commands.append([tool_exe, "render", data_rel, "final"])
                if compile_mode in ("COLLISION_ONLY", "RENDER_ALL") and os.path.isdir(os.path.join(data_dir, "collision")):
                    commands.append([tool_exe, "collision", data_rel])
                if compile_mode in ("PHYSICS_ONLY", "RENDER_ALL") and os.path.isdir(os.path.join(data_dir, "physics")):
                    commands.append([tool_exe, "physics", data_rel])

        if not export_success or (target_file and target_game != "HALO_REACH" and not os.path.isfile(target_file)):
            msg = f"Export failed. Geometry could not be exported to: {target_file}"
            props.last_compile_status = f"FAILED: {msg}"
            if operator:
                operator.report({'ERROR'}, msg)
            return {'CANCELLED'}

        # 3. Execute tool commands
        all_output = []
        has_error = False
        t0 = time.time()
        creationflags = getattr(subprocess, "CREATE_NO_WINDOW", 0)

        for cmd in commands:
            cmd_str = " ".join(f'"{c}"' if " " in c else c for c in cmd)
            all_output.append(f"=== RUNNING: {cmd_str} ===")
            print(f"[HALO COMPILER] {cmd_str}")
            try:
                proc = subprocess.run(
                    cmd,
                    cwd=ek_path,  # Tool requires cwd to be the Editing Kit root
                    stdin=subprocess.DEVNULL,  # Never hang on 'press any key' prompts
                    capture_output=True,
                    text=True,
                    encoding="utf-8",
                    errors="replace",
                    timeout=1800,
                    creationflags=creationflags,
                )
                output = (proc.stdout or "") + "\n" + (proc.stderr or "")
                all_output.append(output)
                print(output)
                if "-FATAL-" in output or "### ERROR" in output or proc.returncode != 0:
                    has_error = True
                    all_output.append(f"(exit code {proc.returncode})")
                    break  # later steps depend on this one
            except subprocess.TimeoutExpired:
                has_error = True
                all_output.append("ERROR: tool timed out after 30 minutes")
                break
            except Exception as e:
                has_error = True
                all_output.append(f"ERROR executing command: {str(e)}")
                break

        elapsed = time.time() - t0
        combined_log = "\n".join(all_output)
        props.last_compile_output = combined_log[-4000:] if len(combined_log) > 4000 else combined_log

        props.last_export_filepath = target_file
        props.last_tags_directory = tags_dir

        if has_error:
            props.last_compile_status = f"FAILED in {elapsed:.1f}s -> {target_file}"
            if operator:
                operator.report({'ERROR'}, f"Tool compilation had errors! Log length: {len(combined_log)} bytes.")
        else:
            props.last_compile_status = f"PASSED in {elapsed:.1f}s -> {tags_dir}"
            if operator:
                operator.report({'INFO'}, f"Compilation Succeeded in {elapsed:.1f}s! Target: {tags_dir}")

        # Safely notify user with popup modal
        try:
            wm = getattr(context, "window_manager", None)
            win = getattr(context, "window", None)
            scr = getattr(context, "screen", None)
            if not getattr(bpy.app, "background", False) and wm and win and scr and getattr(win, "screen", None):
                def draw_popup(self, _ctx):
                    self.layout.label(text=f"Export File: {target_file}", icon='FILE')
                    self.layout.label(text=f"Tags Folder: {tags_dir}", icon='FILE_FOLDER')
                    status_icon = 'ERROR' if has_error else 'CHECKMARK'
                    self.layout.label(text=f"Status: {props.last_compile_status}", icon=status_icon)
                wm.popup_menu(draw_popup, title=f"Halo {target_game} Tool Compilation", icon='INFO')
        except Exception:
            pass

        return {'FINISHED'}

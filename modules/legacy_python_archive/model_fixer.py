# -*- coding: utf-8 -*-
"""
Per-Title Asset Classifier, Error Checker & Topology-Safe Model Fixer
=====================================================================
Rules are taken from the bundled c20 docs (docs/c20-master):

* bsp-troubleshooting  -> sealed-world rules apply to BSP + collision only
                          (open edges, >2 faces per edge, degenerate, <0.1 JMS
                          unit edges, nearly coplanar, +/-150000 JMS bounds).
* gbxmodel / materials -> node + weight limits, lower-case <=32 char material
                          names, symbols go at the END of the material name.
* H3 object importing  -> render models need an armature root and materials;
                          physics meshes ($) must be strictly convex and MUST
                          have a material (Tool crashes otherwise).
* H3 PBR shader guide  -> diffuse = albedo * AO, control map R=spec G=rough
                          B=white, DirectX normals with the _zbump suffix.

Render models are NOT required to be manifold, so the model fixer never welds
UV seams, never fills holes, never forces smooth shading and never recalculates
custom split normals. That is what made ported models (e.g. an Infinite Spartan
going into Halo 3) get destroyed by the old BSP-only repair pass.
Created by O7OF | Shoutout to c20 Reclaimers Wiki | Thank you Bungie
"""

import os
import re
import json
import math
import bpy
import bmesh
from mathutils import Vector

from . import constants

ASSET_BSP = "BSP"
ASSET_RENDER = "RENDER"
ASSET_COLLISION = "COLLISION"
ASSET_PHYSICS = "PHYSICS"
ASSET_MARKER = "MARKER"
ASSET_PORTAL = "PORTAL"
ASSET_SKIP = "SKIP"

ASSET_CLASS_ITEMS = [
    ("AUTO", "Auto Detect", "Detect from name prefix, armature parenting and materials"),
    (ASSET_BSP, "Level BSP", "Sealed-world structure geometry"),
    (ASSET_RENDER, "Render Model", "Visible object/character geometry (non-manifold allowed)"),
    (ASSET_COLLISION, "Collision Model", "Projectile collision mesh (@ prefix)"),
    (ASSET_PHYSICS, "Physics Model", "Havok physics mesh, strictly convex ($ prefix)"),
]

SEVERITY_ERROR = "ERROR"
SEVERITY_WARN = "WARNING"
SEVERITY_INFO = "INFO"

REPORT_TEXT_NAME = "Halo_Diagnostics_Report"


def _strip_blender_suffix(name):
    """'visor.001' -> 'visor'."""
    return re.sub(r"\.\d{3,}$", "", name)


def _find_armature(obj):
    if obj.parent and obj.parent.type == 'ARMATURE':
        return obj.parent
    for m in obj.modifiers:
        if m.type == 'ARMATURE' and m.object:
            return m.object
    return None


def _deform_group_indices(obj, arm):
    if not arm:
        return set()
    bone_names = {b.name for b in arm.data.bones}
    return {vg.index for vg in obj.vertex_groups if vg.name in bone_names}


# =============================================================================
# CLASSIFICATION
# =============================================================================

class HaloAssetClassifier:
    """Decides which rule set applies to a mesh object."""

    @staticmethod
    def classify(obj, mode_override="AUTO"):
        if not obj or obj.type != 'MESH':
            return ASSET_SKIP
        forced = obj.get("halo_asset_class")
        if forced in (ASSET_BSP, ASSET_RENDER, ASSET_COLLISION, ASSET_PHYSICS, ASSET_MARKER, ASSET_SKIP):
            return forced
        if mode_override not in ("AUTO", "", None):
            # Prefix symbols still win over the override, they are explicit.
            pass

        name = obj.name
        if name.startswith("#"):
            return ASSET_MARKER
        if name.startswith("$"):
            return ASSET_PHYSICS
        if name.startswith("@"):
            return ASSET_COLLISION
        if name.startswith("Error_") or (obj.users_collection and any(c.name == "Tool BSP Errors" for c in obj.users_collection)):
            return ASSET_SKIP

        mats = [_strip_blender_suffix(m.name) for m in obj.data.materials if m]
        if mats and all(m.startswith("+portal") or m.startswith("+exactportal") for m in mats):
            return ASSET_PORTAL

        if mode_override in (ASSET_BSP, ASSET_RENDER, ASSET_COLLISION, ASSET_PHYSICS):
            return mode_override

        if _find_armature(obj):
            return ASSET_RENDER
        lname = name.lower()
        if any(m.startswith(("+sky", "+seamsealer", "+seam", "+weatherpoly")) for m in mats):
            return ASSET_BSP
        root = obj.parent
        while root:
            if root.name.lower() in ("b_levelroot", "frame_root", "levelroot"):
                return ASSET_BSP
            root = root.parent
        if any(k in lname for k in ("terrain", "level", "bsp", "gulch", "valley", "canyon", "bunker", "hallway", "room")):
            return ASSET_BSP
        if any(k in lname for k in ("portal",)):
            return ASSET_PORTAL
        return ASSET_RENDER


# =============================================================================
# SCANNER
# =============================================================================

class HaloTitleScanner:
    """Scans objects with title + asset-class aware rules and returns issue lists."""

    @staticmethod
    def _issue(code, severity, count, message, fixable=True):
        return {"code": code, "severity": severity, "count": int(count), "message": message, "fixable": fixable}

    @staticmethod
    def check_material_names(obj, profile, asset_class):
        issues = []
        symbols = set(profile["symbols"])
        specials = profile["special_materials"]
        max_len = profile["max_material_name"]
        used = set(p.material_index for p in obj.data.polygons)
        empty = 0
        upper = 0
        too_long = 0
        dup_suffix = 0
        bad_symbols = 0
        for idx, slot in enumerate(obj.material_slots):
            mat = slot.material
            if mat is None:
                if idx in used:
                    empty += 1
                continue
            raw = mat.name
            base = _strip_blender_suffix(raw)
            if base != raw:
                dup_suffix += 1
            if base != base.lower():
                upper += 1
            core = base.rstrip("".join(symbols))
            if len(core) > max_len:
                too_long += 1
            if base.startswith("+"):
                special = base.split(":")[0].rstrip("".join(symbols))
                special = re.sub(r"\d+$", "", special)
                if special not in specials:
                    bad_symbols += 1
        if not obj.material_slots or empty:
            sev = SEVERITY_ERROR if asset_class == ASSET_PHYSICS else SEVERITY_WARN
            msg = ("Physics mesh has no material - Tool crashes with 'index>=0 && index<array->count'"
                   if asset_class == ASSET_PHYSICS else "Faces with no material (Tool assigns shaders\\invalid)")
            issues.append(HaloTitleScanner._issue("NO_MATERIAL", sev, max(1, empty), msg))
        if upper:
            issues.append(HaloTitleScanner._issue("MAT_UPPERCASE", SEVERITY_ERROR, upper, "Material names must be lower-case (tag paths)"))
        if too_long:
            issues.append(HaloTitleScanner._issue("MAT_TOO_LONG", SEVERITY_WARN, too_long, f"Material names longer than {max_len} characters"))
        if dup_suffix:
            issues.append(HaloTitleScanner._issue("MAT_DOT_SUFFIX", SEVERITY_WARN, dup_suffix, "Blender '.001' duplicate materials (export as separate shaders)"))
        if bad_symbols:
            issues.append(HaloTitleScanner._issue("MAT_BAD_SPECIAL", SEVERITY_WARN, bad_symbols,
                                                  f"Special '+' materials not recognised by {profile['short']} Tool", fixable=False))
        return issues

    @staticmethod
    def _inconsistent_winding(bm):
        bad = 0
        for e in bm.edges:
            if len(e.link_loops) == 2:
                l1, l2 = e.link_loops
                if l1.vert == l2.vert:
                    bad += 1
        return bad

    @staticmethod
    def _non_convex_parts(bm, eps=0.01):
        """Returns number of loose parts that are not strictly convex."""
        bm.faces.ensure_lookup_table()
        seen = set()
        bad = 0
        parts = 0
        for f in bm.faces:
            if f.index in seen:
                continue
            stack = [f]
            part = []
            seen.add(f.index)
            while stack:
                cf = stack.pop()
                part.append(cf)
                for e in cf.edges:
                    for nf in e.link_faces:
                        if nf.index not in seen:
                            seen.add(nf.index)
                            stack.append(nf)
            parts += 1
            verts = {v for pf in part for v in pf.verts}
            if len(verts) > 6000:
                continue
            convex = True
            for pf in part:
                n = pf.normal
                c = pf.calc_center_median()
                for v in verts:
                    if (v.co - c).dot(n) > eps:
                        convex = False
                        break
                if not convex:
                    break
            if not convex:
                bad += 1
        return bad, parts

    @staticmethod
    def scan(obj, target_game="HALO_3", asset_class=None, coplanar_deg=1.0):
        profile = constants.get_title_profile(target_game)
        if asset_class is None:
            asset_class = HaloAssetClassifier.classify(obj)
        result = {"object": obj.name, "asset_class": asset_class, "title": profile["short"],
                  "issues": [], "tris": 0, "verts": 0}
        if asset_class in (ASSET_SKIP, ASSET_MARKER, ASSET_PORTAL) or obj.type != 'MESH' or not obj.data or not obj.data.polygons:
            return result

        me = obj.data
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.faces.ensure_lookup_table()
        result["verts"] = len(bm.verts)
        result["tris"] = sum(len(f.verts) - 2 for f in bm.faces)
        add = result["issues"].append
        sealed = asset_class in (ASSET_BSP, ASSET_COLLISION)

        # ---- Shared checks ---------------------------------------------------
        degen = sum(1 for f in bm.faces if f.calc_area() < 1e-6)
        if degen:
            add(HaloTitleScanner._issue("DEGENERATE", SEVERITY_WARN, degen, "Zero-area (degenerate) triangles"))
        loose = sum(1 for v in bm.verts if not v.link_faces) + sum(1 for e in bm.edges if not e.link_faces)
        if loose:
            add(HaloTitleScanner._issue("LOOSE", SEVERITY_WARN, loose, "Loose vertices/edges with no faces"))

        seen = set()
        dups = 0
        for f in bm.faces:
            key = tuple(sorted(v.index for v in f.verts))
            if key in seen:
                dups += 1
            else:
                seen.add(key)
        if dups:
            add(HaloTitleScanner._issue("DUPLICATE", SEVERITY_ERROR if sealed else SEVERITY_WARN, dups,
                                        "Duplicate faces sharing identical vertices"))

        result["issues"].extend(HaloTitleScanner.check_material_names(obj, profile, asset_class))

        # ---- Sealed-world (BSP / collision) ---------------------------------
        if sealed:
            open_e = sum(1 for e in bm.edges if len(e.link_faces) == 1)
            amb = sum(1 for e in bm.edges if len(e.link_faces) > 2)
            if open_e:
                add(HaloTitleScanner._issue("OPEN_EDGE", SEVERITY_ERROR, open_e, "Open edges - BSP/collision must be sealed (red WRL)"))
            if amb:
                add(HaloTitleScanner._issue("COULDNT_UPDATE_EDGE", SEVERITY_ERROR, amb, "Edges shared by >2 faces ('Couldn't update edge')"))
            short = sum(1 for e in bm.edges if e.calc_length() < constants.BSP_MIN_EDGE_LENGTH)
            if short:
                add(HaloTitleScanner._issue("SHORT_EDGE", SEVERITY_WARN, short, "Edges < 0.1 JMS units collapse in Tool"))
            lim = math.radians(coplanar_deg)
            near = 0
            for e in bm.edges:
                if len(e.link_faces) == 2:
                    a = e.calc_face_angle(0.0)
                    if 1e-5 < a < lim:
                        near += 1
            if near:
                add(HaloTitleScanner._issue("NEARLY_COPLANAR", SEVERITY_WARN, near, "Nearly coplanar faces (phantom BSP risk)"))
            ext = profile["bsp_max_extent"]
            mw = obj.matrix_world
            oob = sum(1 for v in bm.verts if any(abs(c) > ext for c in (mw @ v.co)))
            if oob:
                add(HaloTitleScanner._issue("OUT_OF_BOUNDS", SEVERITY_ERROR, oob, "Vertices beyond +/-1500 world units ('vertex has a bad point')", fixable=False))
            wind = HaloTitleScanner._inconsistent_winding(bm)
            if wind:
                add(HaloTitleScanner._issue("FLIPPED_NORMALS", SEVERITY_ERROR, wind, "Adjacent faces with flipped orientation"))

        # ---- Render model ----------------------------------------------------
        if asset_class == ASSET_RENDER:
            arm = _find_armature(obj)
            if not arm:
                add(HaloTitleScanner._issue("NO_ARMATURE", SEVERITY_ERROR, 1, "Render meshes must be parented to an Armature (Halo needs >=1 node)"))
            else:
                nb = len(arm.data.bones)
                if nb > profile["max_nodes"]:
                    add(HaloTitleScanner._issue("TOO_MANY_NODES", SEVERITY_ERROR, nb,
                                                f"{nb} bones > {profile['max_nodes']} node limit for {profile['short']}"))
                deform = _deform_group_indices(obj, arm)
                over = 0
                unweighted = 0
                for v in me.vertices:
                    ws = [g for g in v.groups if g.group in deform and g.weight > 1e-4]
                    if len(ws) > profile["max_weights"]:
                        over += 1
                    if not ws and deform:
                        unweighted += 1
                if over:
                    add(HaloTitleScanner._issue("TOO_MANY_WEIGHTS", SEVERITY_ERROR, over,
                                                f"Vertices influenced by > {profile['max_weights']} nodes"))
                if unweighted:
                    add(HaloTitleScanner._issue("UNWEIGHTED", SEVERITY_WARN, unweighted, "Vertices with no bone weight (rigid to root)"))
                bone_names = {b.name for b in arm.data.bones}
                orphan = [vg.name for vg in obj.vertex_groups if vg.name not in bone_names and not vg.name.startswith("_")]
                if orphan and deform:
                    add(HaloTitleScanner._issue("ORPHAN_GROUPS", SEVERITY_INFO, len(orphan), "Vertex groups with no matching bone (ignored by exporter)"))
            if len(me.uv_layers) == 0:
                add(HaloTitleScanner._issue("NO_UVS", SEVERITY_ERROR, 1, "No UV map - shaders will not map"))
            elif len(me.uv_layers) > profile["max_uvs"]:
                add(HaloTitleScanner._issue("EXTRA_UVS", SEVERITY_INFO, len(me.uv_layers),
                                            f"{len(me.uv_layers)} UV maps; {profile['short']} render models use {profile['max_uvs']}"))
            if result["verts"] > profile["max_part_verts"]:
                add(HaloTitleScanner._issue("PART_TOO_BIG", SEVERITY_ERROR, result["verts"],
                                            f"> {profile['max_part_verts']} verts in one part - split by material or decimate"))
            is_char = bool(arm and len(arm.data.bones) > 15)
            budget = profile["recommended_tris_character" if is_char else "recommended_tris_object"]
            if result["tris"] > budget * 1.5:
                add(HaloTitleScanner._issue("OVER_BUDGET", SEVERITY_WARN, result["tris"],
                                            f"{result['tris']} tris; ~{budget} is typical for {profile['short']}"))
            wind = HaloTitleScanner._inconsistent_winding(bm)
            if wind:
                add(HaloTitleScanner._issue("FLIPPED_NORMALS", SEVERITY_INFO, wind, "Faces with inconsistent winding (check backface culling)", fixable=False))
            if is_char:
                h = HaloModelFixer.measure_height(arm, [obj])
                target_h = HaloModelFixer.target_height(target_game)
                if h > 0 and not (target_h * 0.6 <= h <= target_h * 1.6):
                    add(HaloTitleScanner._issue("SCALE", SEVERITY_WARN, 1,
                                                f"Character is {h:.2f} units tall; {profile['short']} Spartan is ~{target_h:.1f} JMS units"))

        # ---- Physics -----------------------------------------------------------
        if asset_class == ASSET_PHYSICS:
            if not profile["has_physics_model"]:
                add(HaloTitleScanner._issue("NO_PHYSICS_IN_TITLE", SEVERITY_WARN, 1,
                                            f"{profile['short']} has no JMS physics model - use @collision instead", fixable=False))
            bad, parts = HaloTitleScanner._non_convex_parts(bm)
            if bad:
                add(HaloTitleScanner._issue("NOT_CONVEX", SEVERITY_ERROR, bad,
                                            f"{bad}/{parts} physics shapes are not strictly convex (Tool silently hulls them)"))

        bm.free()
        return result

    @staticmethod
    def write_report(results, target_game):
        profile = constants.get_title_profile(target_game)
        txt = bpy.data.texts.get(REPORT_TEXT_NAME) or bpy.data.texts.new(REPORT_TEXT_NAME)
        txt.clear()
        txt.write(f"Halo Diagnostics Report - target: {profile['short']}\n")
        txt.write("=" * 64 + "\n")
        for r in results:
            if not r["issues"]:
                continue
            txt.write(f"\n[{r['asset_class']}] {r['object']}  ({r['tris']} tris, {r['verts']} verts)\n")
            for i in r["issues"]:
                fx = "" if i["fixable"] else "  (manual)"
                txt.write(f"  {i['severity']:<7} {i['code']:<20} x{i['count']:<6} {i['message']}{fx}\n")
        return txt


# =============================================================================
# FIXERS
# =============================================================================

class HaloModelFixer:
    """Topology-safe repair for render/collision/physics models."""

    # ---- helpers -------------------------------------------------------------
    @staticmethod
    def target_height(target_game):
        try:
            from .scale_models import SPARTAN_DIMENSIONS
            key = constants.get_title_profile(target_game)["scale_key"]
            return float(SPARTAN_DIMENSIONS.get(key, SPARTAN_DIMENSIONS["halo3"]).z)
        except Exception:
            return 70.3

    @staticmethod
    def measure_height(root, meshes):
        zs = []
        for m in meshes:
            if m and m.type == 'MESH':
                zs.extend((m.matrix_world @ Vector(c)).z for c in m.bound_box)
        if not zs and root:
            zs.extend((root.matrix_world @ Vector(c)).z for c in root.bound_box)
        return (max(zs) - min(zs)) if zs else 0.0

    # ---- materials -----------------------------------------------------------
    @staticmethod
    def sanitize_materials(obj, target_game, asset_class):
        profile = constants.get_title_profile(target_game)
        symbols = profile["symbols"]
        max_len = profile["max_material_name"]
        rep = {"renamed": 0, "merged": 0, "created": 0}
        me = obj.data

        # Merge 'name.001' duplicates into one slot so they export as one shader.
        first_slot = {}
        remap = {}
        for idx, slot in enumerate(obj.material_slots):
            mat = slot.material
            if not mat:
                continue
            base = _strip_blender_suffix(mat.name).lower()
            if base in first_slot:
                remap[idx] = first_slot[base]
                rep["merged"] += 1
            else:
                first_slot[base] = idx
        if remap:
            for p in me.polygons:
                if p.material_index in remap:
                    p.material_index = remap[p.material_index]

        for slot in obj.material_slots:
            mat = slot.material
            if not mat:
                continue
            base = _strip_blender_suffix(mat.name)
            suffix = ""
            core = base
            while core and core[-1] in symbols and not core.startswith("+"):
                suffix = core[-1] + suffix
                core = core[:-1]
            clean = re.sub(r"[^a-z0-9_ +:\-]", "_", core.lower()).strip()
            clean = re.sub(r"_+", "_", clean)
            if len(clean) > max_len:
                clean = clean[:max_len].rstrip("_ ")
            new_name = clean + suffix
            if new_name and new_name != mat.name:
                existing = bpy.data.materials.get(new_name)
                if existing and existing != mat:
                    slot.material = existing
                else:
                    mat.name = new_name
                rep["renamed"] += 1

        used = {p.material_index for p in me.polygons}
        needs_default = not obj.material_slots or any(
            obj.material_slots[i].material is None for i in used if i < len(obj.material_slots))
        if needs_default:
            default_name = {
                ASSET_PHYSICS: "hard_metal_thick",
                ASSET_COLLISION: "hard_metal_thick",
            }.get(asset_class, _strip_blender_suffix(obj.name).lower().lstrip("$@#")[:max_len] or "default")
            mat = bpy.data.materials.get(default_name) or bpy.data.materials.new(default_name)
            if not obj.material_slots:
                me.materials.append(mat)
            else:
                for i, slot in enumerate(obj.material_slots):
                    if slot.material is None:
                        slot.material = mat
            rep["created"] += 1
        return rep

    # ---- geometry (safe) -----------------------------------------------------
    @staticmethod
    def clean_safe_geometry(obj, remove_loose=True, remove_duplicates=True, remove_degenerate=True):
        """Removes only provably-bad elements. Keeps UV seams, split normals and open edges."""
        rep = {"loose": 0, "duplicates": 0, "degenerate": 0}
        me = obj.data
        bm = bmesh.new()
        bm.from_mesh(me)
        if remove_duplicates:
            seen = set()
            dup = []
            for f in bm.faces:
                key = tuple(sorted(v.index for v in f.verts))
                if key in seen:
                    dup.append(f)
                else:
                    seen.add(key)
            if dup:
                bmesh.ops.delete(bm, geom=dup, context='FACES_ONLY')
                rep["duplicates"] = len(dup)
        if remove_degenerate:
            degen = [f for f in bm.faces if f.is_valid and f.calc_area() < 1e-6]
            if degen:
                # Collapsing a zero-area face's shortest edge removes it without opening a hole.
                edges = list({min(f.edges, key=lambda e: e.calc_length()) for f in degen})
                try:
                    try:
                        bmesh.ops.collapse(bm, edges=edges, uvs=True)
                    except TypeError:
                        bmesh.ops.collapse(bm, edges=edges)
                    rep["degenerate"] = len(degen)
                except Exception as e:
                    print(f"[HALO FIXER] degenerate collapse failed on {obj.name}: {e}")
        if remove_loose:
            lv = [v for v in bm.verts if not v.link_faces]
            le = [e for e in bm.edges if not e.link_faces]
            if le:
                bmesh.ops.delete(bm, geom=le, context='EDGES')
            lv = [v for v in bm.verts if v.is_valid and not v.link_faces]
            if lv:
                bmesh.ops.delete(bm, geom=lv, context='VERTS')
            rep["loose"] = len(le) + len(lv)
        bm.to_mesh(me)
        bm.free()
        me.update()
        return rep

    @staticmethod
    def weld_preserving_seams(obj, dist=1e-4):
        """Merges coincident vertices ONLY when they share identical UVs on every UV layer,
        so UV seams survive. Edges that were split for hard shading are marked sharp first."""
        me = obj.data
        bm = bmesh.new()
        bm.from_mesh(me)
        uv_layers = list(bm.loops.layers.uv.values())
        q = 1.0 / max(dist, 1e-7)
        buckets = {}
        for v in bm.verts:
            key = (round(v.co.x * q), round(v.co.y * q), round(v.co.z * q))
            buckets.setdefault(key, []).append(v)

        def sig(v):
            s = []
            for lay in uv_layers:
                s.append(frozenset((round(l[lay].uv.x, 4), round(l[lay].uv.y, 4)) for l in v.link_loops))
            return tuple(s)

        targetmap = {}
        for verts in buckets.values():
            if len(verts) < 2:
                continue
            groups = {}
            for v in verts:
                groups.setdefault(sig(v), []).append(v)
            for g in groups.values():
                if len(g) > 1:
                    for v in g[1:]:
                        targetmap[v] = g[0]
        # Preserve hard edges: boundary edges about to be stitched get marked sharp
        for e in bm.edges:
            if e.is_boundary and (e.verts[0] in targetmap or e.verts[1] in targetmap):
                e.smooth = False
        merged = len(targetmap)
        if targetmap:
            bmesh.ops.weld_verts(bm, targetmap=targetmap)
        bm.to_mesh(me)
        bm.free()
        me.update()
        return merged

    @staticmethod
    def triangulate_keep_normals(obj, context=None):
        """Triangulates with the modifier so custom split normals are preserved."""
        context = context or bpy.context
        mod = obj.modifiers.new("HaloTriangulate", 'TRIANGULATE')
        mod.quad_method = 'BEAUTY'
        mod.ngon_method = 'BEAUTY'
        if hasattr(mod, "keep_custom_normals"):
            mod.keep_custom_normals = True
        try:
            with context.temp_override(object=obj, active_object=obj, selected_objects=[obj]):
                bpy.ops.object.modifier_move_to_index(modifier=mod.name, index=0)
                bpy.ops.object.modifier_apply(modifier=mod.name)
            return True
        except Exception as e:
            print(f"[HALO FIXER] triangulate failed on {obj.name}: {e}")
            if mod.name in obj.modifiers:
                obj.modifiers.remove(mod)
            return False

    @staticmethod
    def trim_uv_layers(obj, max_uvs):
        me = obj.data
        removed = 0
        if len(me.uv_layers) <= max_uvs:
            return 0
        keep = None
        for uv in me.uv_layers:
            if uv.active_render:
                keep = uv.name
        keep = keep or me.uv_layers[0].name
        for uv in list(me.uv_layers):
            if len(me.uv_layers) <= max_uvs:
                break
            if uv.name != keep:
                me.uv_layers.remove(uv)
                removed += 1
        return removed

    # ---- weights --------------------------------------------------------------
    @staticmethod
    def limit_and_normalize_weights(obj, max_weights, assign_unweighted=True):
        arm = _find_armature(obj)
        rep = {"limited": 0, "normalized": 0, "assigned": 0}
        if not arm:
            return rep
        deform = _deform_group_indices(obj, arm)
        if not deform:
            return rep
        vgs = obj.vertex_groups
        idx_to_vg = {vg.index: vg for vg in vgs}
        root_bone = next((b for b in arm.data.bones if b.parent is None), None)
        root_vg = None
        if assign_unweighted and root_bone:
            root_vg = vgs.get(root_bone.name) or vgs.new(name=root_bone.name)
        for v in obj.data.vertices:
            ws = sorted(((g.group, g.weight) for g in v.groups if g.group in deform), key=lambda t: -t[1])
            ws_pos = [(g, w) for g, w in ws if w > 1e-4]
            for g, w in ws:
                if w <= 1e-4:
                    idx_to_vg[g].remove([v.index])
            if not ws_pos:
                if root_vg is not None:
                    root_vg.add([v.index], 1.0, 'REPLACE')
                    rep["assigned"] += 1
                continue
            if len(ws_pos) > max_weights:
                for g, _ in ws_pos[max_weights:]:
                    idx_to_vg[g].remove([v.index])
                ws_pos = ws_pos[:max_weights]
                rep["limited"] += 1
            total = sum(w for _, w in ws_pos)
            if total > 0 and abs(total - 1.0) > 1e-4:
                for g, w in ws_pos:
                    idx_to_vg[g].add([v.index], w / total, 'REPLACE')
                rep["normalized"] += 1
        return rep

    # ---- skeleton ---------------------------------------------------------------
    _SYNONYMS = [
        (r"upper_?arm|uparm|arm_?upper|shoulder_?twist", "upperarm"),
        (r"fore_?arm|lower_?arm|lowarm|elbow", "forearm"),
        (r"up_?leg|upper_?leg|thigh", "thigh"),
        (r"lower_?leg|low_?leg|shin|calf|knee", "calf"),
        (r"collar|clavicle|shoulder", "clavicle"),
        (r"toe\d*|toebase|ball", "toe"),
        (r"ankle|foot", "foot"),
        (r"wrist|hand", "hand"),
        (r"hips|pelvis", "pelvis"),
        (r"spine2|chest|upper_?spine|spine_?upper", "chest"),
        (r"neck\d*", "neck"),
        (r"head", "head"),
        (r"pedestal|root|origin", "root"),
    ]

    @classmethod
    def canonical_bone(cls, name):
        n = name.lower().strip()
        n = re.sub(r"^(mixamorig:|def[-_]|b_|bip0?1[ _]?|frame[ _]|bone[ _]|jnt[ _]|j_)", "", n)
        side = ""
        m = re.search(r"(^|[ _.\-])(l|left)([ _.\-]|$)|[_.](l)$", n)
        r = re.search(r"(^|[ _.\-])(r|right)([ _.\-]|$)|[_.](r)$", n)
        if m and not r:
            side = "l_"
        elif r and not m:
            side = "r_"
        n = re.sub(r"(^|[ _.\-])(l|left|r|right)([ _.\-]|$)", "_", n)
        n = re.sub(r"[_.](l|r)$", "", n)
        n = re.sub(r"[^a-z0-9]+", "_", n).strip("_")
        for pat, rep in cls._SYNONYMS:
            if re.fullmatch(pat, n) or re.fullmatch(rf"({pat})_?\d*", n):
                n = rep
                break
        return side + n

    @classmethod
    def retarget_weights(cls, mesh_objs, target_arm, reparent=True):
        """Moves vertex weights from an imported skeleton (e.g. Halo Infinite) onto a target
        Halo skeleton. Bones with a matching canonical name are renamed; unmatched helper /
        twist / armor bones fold their weights into the nearest matched ancestor so the mesh
        deforms correctly with the target title's animations."""
        rep = {"mapped": 0, "folded": 0, "unmatched": []}
        if not target_arm or target_arm.type != 'ARMATURE':
            return rep
        targets = {cls.canonical_bone(b.name): b.name for b in target_arm.data.bones}
        target_root = next((b.name for b in target_arm.data.bones if b.parent is None), None)
        for obj in mesh_objs:
            src_arm = _find_armature(obj)
            src_bones = src_arm.data.bones if src_arm else None

            def resolve(name):
                c = cls.canonical_bone(name)
                if c in targets:
                    return targets[c]
                if name in target_arm.data.bones:
                    return name
                if src_bones and name in src_bones:
                    p = src_bones[name].parent
                    while p:
                        c2 = cls.canonical_bone(p.name)
                        if c2 in targets:
                            return targets[c2]
                        if p.name in target_arm.data.bones:
                            return p.name
                        p = p.parent
                return target_root

            plan = {}
            for vg in obj.vertex_groups:
                if src_bones and vg.name not in src_bones and vg.name not in target_arm.data.bones:
                    continue
                dest = resolve(vg.name)
                if dest:
                    plan[vg.name] = dest
            # accumulate weights per vertex per destination
            acc = {}
            idx_name = {vg.index: vg.name for vg in obj.vertex_groups}
            for v in obj.data.vertices:
                for g in v.groups:
                    src = idx_name.get(g.group)
                    if src in plan and g.weight > 0:
                        d = acc.setdefault(v.index, {})
                        d[plan[src]] = d.get(plan[src], 0.0) + g.weight
            for src, dest in plan.items():
                if cls.canonical_bone(src) in targets or src == dest:
                    rep["mapped"] += 1
                else:
                    rep["folded"] += 1
            for name in list(plan.keys()):
                vg = obj.vertex_groups.get(name)
                if vg:
                    obj.vertex_groups.remove(vg)
            for vi, d in acc.items():
                for dest, w in d.items():
                    vg = obj.vertex_groups.get(dest) or obj.vertex_groups.new(name=dest)
                    vg.add([vi], min(1.0, w), 'REPLACE')
            if reparent:
                mw = obj.matrix_world.copy()
                obj.parent = target_arm
                obj.matrix_world = mw
                mod = next((m for m in obj.modifiers if m.type == 'ARMATURE'), None) or obj.modifiers.new("Armature", 'ARMATURE')
                mod.object = target_arm
        return rep

    @staticmethod
    def reduce_skeleton(arm, max_nodes, context=None):
        """Deletes bones that carry no weights (and have no weighted children), then merges the
        lightest leaf bones into their parents until the title's node limit is met."""
        context = context or bpy.context
        rep = {"removed": 0, "merged": 0}
        if not arm or arm.type != 'ARMATURE':
            return rep
        meshes = [o for o in bpy.data.objects if o.type == 'MESH' and _find_armature(o) == arm]
        weight_sum = {b.name: 0.0 for b in arm.data.bones}
        for o in meshes:
            idx_name = {vg.index: vg.name for vg in o.vertex_groups}
            for v in o.data.vertices:
                for g in v.groups:
                    n = idx_name.get(g.group)
                    if n in weight_sum:
                        weight_sum[n] += g.weight

        def subtree_weight(b):
            return weight_sum.get(b.name, 0.0) + sum(subtree_weight(c) for c in b.children)

        from .rigging import HaloBoneManager
        keep_names = {alias for row in HaloBoneManager.SPARTAN_BIPED_BONES for alias in row}
        to_delete = [b.name for b in arm.data.bones if b.parent is not None and subtree_weight(b) <= 1e-6 and b.name not in keep_names]
        count_after = len(arm.data.bones) - len(to_delete)

        merges = []
        if count_after > max_nodes:
            remaining = [b for b in arm.data.bones if b.name not in to_delete and b.parent is not None]
            # leaves first, lightest first
            def is_leaf(b):
                return all(c.name in to_delete for c in b.children)
            candidates = sorted([b for b in remaining if is_leaf(b)], key=lambda b: weight_sum.get(b.name, 0.0))
            merged_set = set(to_delete)
            while count_after > max_nodes and candidates:
                b = candidates.pop(0)
                merges.append((b.name, b.parent.name))
                merged_set.add(b.name)
                count_after -= 1
                p = b.parent
                if p.parent is not None and all(c.name in merged_set for c in p.children):
                    candidates.append(p)
                    candidates.sort(key=lambda x: weight_sum.get(x.name, 0.0))

        # Fold weights of merged bones into parent (resolve chains)
        parent_of = dict(merges)

        def final(n):
            while n in parent_of:
                n = parent_of[n]
            return n
        for o in meshes:
            for src in list(parent_of.keys()):
                vg = o.vertex_groups.get(src)
                if not vg:
                    continue
                dest_name = final(src)
                dest = o.vertex_groups.get(dest_name) or o.vertex_groups.new(name=dest_name)
                for v in o.data.vertices:
                    for g in v.groups:
                        if g.group == vg.index and g.weight > 0:
                            cur = 0.0
                            for g2 in v.groups:
                                if g2.group == dest.index:
                                    cur = g2.weight
                            dest.add([v.index], min(1.0, cur + g.weight), 'REPLACE')
                o.vertex_groups.remove(vg)

        kill = set(to_delete) | set(parent_of.keys())
        if kill:
            prev_active = context.view_layer.objects.active
            prev_mode = context.mode
            try:
                with context.temp_override(object=arm, active_object=arm):
                    bpy.ops.object.mode_set(mode='EDIT')
                    for n in kill:
                        eb = arm.data.edit_bones.get(n)
                        if eb:
                            for c in eb.children:
                                c.parent = eb.parent
                            arm.data.edit_bones.remove(eb)
                    bpy.ops.object.mode_set(mode='OBJECT')
            except Exception as e:
                print(f"[HALO FIXER] reduce_skeleton edit failed: {e}")
            if prev_active:
                context.view_layer.objects.active = prev_active
        rep["removed"] = len(to_delete)
        rep["merged"] = len(parent_of)
        return rep

    @staticmethod
    def scale_to_title(root, meshes, target_game, context=None):
        """Uniformly scales an imported model so its height matches the title's Spartan
        (~70 JMS units), then applies scale so the exporter writes clean transforms."""
        context = context or bpy.context
        h = HaloModelFixer.measure_height(root, meshes)
        if h <= 1e-6:
            return 1.0
        factor = HaloModelFixer.target_height(target_game) / h
        top = root or meshes[0]
        top.scale = top.scale * factor
        context.view_layer.update()
        objs = [top] + [c for c in top.children_recursive] if hasattr(top, "children_recursive") else [top]
        try:
            with context.temp_override(selected_editable_objects=objs, selected_objects=objs,
                                       active_object=top, object=top):
                bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        except Exception as e:
            print(f"[HALO FIXER] transform_apply failed: {e}")
        return factor

    # ---- physics / collision ----------------------------------------------------
    @staticmethod
    def make_physics_convex(obj):
        """Replaces every loose part with its convex hull (docs: Mesh > Convex Hull)."""
        me = obj.data
        src = bmesh.new()
        src.from_mesh(me)
        src.faces.ensure_lookup_table()
        seen = set()
        parts = []
        for f in src.faces:
            if f.index in seen:
                continue
            stack = [f]
            seen.add(f.index)
            part = []
            while stack:
                cf = stack.pop()
                part.append(cf)
                for e in cf.edges:
                    for nf in e.link_faces:
                        if nf.index not in seen:
                            seen.add(nf.index)
                            stack.append(nf)
            parts.append(part)
        out = bmesh.new()
        hulls = 0
        for part in parts:
            mat_idx = part[0].material_index
            tmp = bmesh.new()
            for v in {v for pf in part for v in pf.verts}:
                tmp.verts.new(v.co)
            try:
                res = bmesh.ops.convex_hull(tmp, input=tmp.verts[:], use_existing_faces=False)
                junk = list(set(res.get("geom_interior", [])) | set(res.get("geom_unused", [])))
                if junk:
                    bmesh.ops.delete(tmp, geom=[g for g in junk if isinstance(g, bmesh.types.BMVert)], context='VERTS')
            except Exception:
                tmp.free()
                continue
            vmap = {}
            for v in tmp.verts:
                vmap[v] = out.verts.new(v.co)
            for f in tmp.faces:
                try:
                    nf = out.faces.new([vmap[v] for v in f.verts])
                    nf.material_index = mat_idx
                except ValueError:
                    pass
            tmp.free()
            hulls += 1
        if not out.faces:
            # Every hull failed (flat / <4 verts) - never wipe the user's mesh.
            out.free()
            src.free()
            return 0
        bmesh.ops.recalc_face_normals(out, faces=out.faces[:])
        bmesh.ops.triangulate(out, faces=out.faces[:])
        out.to_mesh(me)
        out.free()
        src.free()
        me.update()
        return hulls

    @staticmethod
    def fix_collision(obj, weld_dist=0.01):
        """Collision follows sealed-world rules but faces point OUTWARD (it's an object)."""
        me = obj.data
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=weld_dist)
        bmesh.ops.dissolve_degenerate(bm, dist=1e-5, edges=bm.edges[:])
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        bm.to_mesh(me)
        bm.free()
        me.update()

    # ---- orchestrator ---------------------------------------------------------------
    @staticmethod
    def fix_object(obj, target_game="HALO_3", asset_class=None, options=None, context=None):
        """Applies the right repair set for the object's asset class and selected title."""
        opts = {
            "fix_materials": True,
            "clean_geometry": True,
            "weld_seams_safe": False,
            "triangulate": False,
            "limit_weights": True,
            "trim_uvs": False,
            "convex_physics": True,
        }
        if options:
            opts.update(options)
        profile = constants.get_title_profile(target_game)
        asset_class = asset_class or HaloAssetClassifier.classify(obj)
        rep = {"asset_class": asset_class}
        if asset_class in (ASSET_SKIP, ASSET_MARKER, ASSET_PORTAL, ASSET_BSP):
            return rep
        if opts["clean_geometry"]:
            rep.update(HaloModelFixer.clean_safe_geometry(obj))
        if asset_class == ASSET_RENDER:
            if opts["weld_seams_safe"]:
                rep["welded"] = HaloModelFixer.weld_preserving_seams(obj)
            if opts["limit_weights"]:
                rep.update(HaloModelFixer.limit_and_normalize_weights(obj, profile["max_weights"]))
            if opts["trim_uvs"]:
                rep["uvs_removed"] = HaloModelFixer.trim_uv_layers(obj, profile["max_uvs"])
            if opts["triangulate"]:
                HaloModelFixer.triangulate_keep_normals(obj, context)
        elif asset_class == ASSET_COLLISION:
            HaloModelFixer.fix_collision(obj)
        elif asset_class == ASSET_PHYSICS and opts["convex_physics"]:
            rep["hulls"] = HaloModelFixer.make_physics_convex(obj)
        if opts["fix_materials"]:
            rep.update(HaloModelFixer.sanitize_materials(obj, target_game, asset_class))
        return rep


# =============================================================================
# SHADER PLANNING & PBR -> HALO BITMAP CONVERSION
# =============================================================================

class HaloShaderPlanner:
    """Infers a Halo shader setup per material and converts PBR textures into the
    bitmaps each title expects (H3/Reach: diffuse, control map, _zbump normal)."""

    ROLE_KEYWORDS = [
        ("glass", ("visor", "glass", "window", "lens", "canopy")),
        ("emissive", ("emis", "illum", "light", "glow", "holo", "screen")),
        ("skin", ("skin", "face", "flesh", "body_skin")),
        ("water", ("water", "liquid")),
    ]

    @staticmethod
    def _image_from_socket(sock, depth=0):
        if sock is None or not sock.is_linked or depth > 6:
            return None
        node = sock.links[0].from_node
        if node.type == 'TEX_IMAGE':
            return node.image
        for name in ("Color", "Image", "Fac", "Height", "Value"):
            inp = node.inputs.get(name) if hasattr(node.inputs, "get") else None
            img = HaloShaderPlanner._image_from_socket(inp, depth + 1)
            if img:
                return img
        for inp in node.inputs:
            img = HaloShaderPlanner._image_from_socket(inp, depth + 1)
            if img:
                return img
        return None

    @staticmethod
    def extract_pbr(mat):
        maps = {}
        if not mat or not mat.use_nodes or not mat.node_tree:
            return maps
        bsdf = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if bsdf:
            def grab(key, *names):
                for n in names:
                    s = bsdf.inputs.get(n)
                    img = HaloShaderPlanner._image_from_socket(s)
                    if img:
                        maps[key] = img
                        return
            grab("albedo", "Base Color")
            grab("metallic", "Metallic")
            grab("roughness", "Roughness")
            grab("normal", "Normal")
            grab("emission", "Emission Color", "Emission")
            grab("alpha", "Alpha")
        for n in mat.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image:
                ln = (n.label or n.name or "").lower() + " " + n.image.name.lower()
                if "ao" in re.split(r"[^a-z]", ln) or "occlusion" in ln:
                    maps.setdefault("ao", n.image)
        return maps

    @classmethod
    def infer_role(cls, mat, asset_class):
        name = mat.name.lower() if mat else ""
        for role, keys in cls.ROLE_KEYWORDS:
            if any(k in name for k in keys):
                return role
        if mat and mat.use_nodes and mat.node_tree:
            bsdf = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
            if bsdf:
                tr = bsdf.inputs.get("Transmission Weight") or bsdf.inputs.get("Transmission")
                if tr is not None and (tr.is_linked or tr.default_value > 0.5):
                    return "glass"
                a = bsdf.inputs.get("Alpha")
                if a is not None and not a.is_linked and a.default_value < 0.95:
                    return "glass"
        return "level" if asset_class == ASSET_BSP else "object"

    @staticmethod
    def _pixels(img, w, h):
        import numpy as np
        if img.size[0] != w or img.size[1] != h:
            img = img.copy()
            img.scale(w, h)
        arr = np.empty(w * h * 4, dtype=np.float32)
        img.pixels.foreach_get(arr)
        return arr.reshape(h, w, 4)

    @staticmethod
    def _save_tif(name, arr, path):
        import numpy as np
        h, w = arr.shape[:2]
        img = bpy.data.images.new(name, w, h, alpha=True, float_buffer=False)
        img.pixels.foreach_set(np.ascontiguousarray(arr, dtype=np.float32).ravel())
        img.filepath_raw = path
        img.file_format = 'TIFF'
        img.save()
        bpy.data.images.remove(img)

    @classmethod
    def bake_bitmaps(cls, mat, out_dir, target_game, flip_green=True):
        """Writes title-ready .tif bitmaps. Returns dict of written files."""
        import numpy as np
        profile = constants.get_title_profile(target_game)
        maps = cls.extract_pbr(mat)
        written = {}
        if not maps:
            return written
        os.makedirs(out_dir, exist_ok=True)
        base = _strip_blender_suffix(mat.name).rstrip(profile["symbols"]).replace(" ", "_")
        ref = maps.get("albedo") or next(iter(maps.values()))
        w, h = max(4, ref.size[0]), max(4, ref.size[1])
        try:
            if "albedo" in maps:
                diff = cls._pixels(maps["albedo"], w, h).copy()
                if "ao" in maps:
                    ao = cls._pixels(maps["ao"], w, h)[..., 0:1]
                    diff[..., :3] *= ao          # diffuse = albedo * AO (pbr-shader guide)
                p = os.path.join(out_dir, f"{base}_diff.tif")
                cls._save_tif(f"{base}_diff", diff, p)
                written["base_map"] = p
            if target_game in ("HALO_3", "HALO_REACH") and ("roughness" in maps or "metallic" in maps):
                rough = cls._pixels(maps["roughness"], w, h)[..., 0] if "roughness" in maps else np.full((h, w), 0.5, np.float32)
                if "metallic" in maps:
                    metal = cls._pixels(maps["metallic"], w, h)[..., 0]
                    spec = np.maximum(metal, 1.0 - rough)             # metallic lighten inverted roughness
                    if "ao" in maps:
                        ao = cls._pixels(maps["ao"], w, h)[..., 0]
                        spec = spec * (1.0 - 0.75 * (1.0 - ao))       # AO multiply @ 75%
                else:
                    spec = np.zeros((h, w), np.float32)               # no metallic -> black red channel
                ctrl = np.stack([spec, rough, np.ones((h, w), np.float32), np.ones((h, w), np.float32)], axis=-1)
                p = os.path.join(out_dir, f"{base}_control.tif")
                cls._save_tif(f"{base}_control", ctrl, p)
                written["material_texture"] = p
            if "normal" in maps:
                nrm = cls._pixels(maps["normal"], w, h).copy()
                if flip_green:
                    nrm[..., 1] = 1.0 - nrm[..., 1]                   # OpenGL (Blender) -> DirectX
                p = os.path.join(out_dir, f"{base}{profile['normal_suffix']}.tif")
                cls._save_tif(f"{base}_bump", nrm, p)
                written["bump_map"] = p
            if "emission" in maps:
                em = cls._pixels(maps["emission"], w, h)
                p = os.path.join(out_dir, f"{base}_illum.tif")
                cls._save_tif(f"{base}_illum", em, p)
                written["self_illum_map"] = p
        except Exception as e:
            print(f"[HALO SHADER] bake failed for {mat.name}: {e}")
        return written

    @classmethod
    def plan_object(cls, obj, target_game, asset_class):
        profile = constants.get_title_profile(target_game)
        plans = []
        for slot in obj.material_slots:
            mat = slot.material
            if not mat:
                continue
            name = _strip_blender_suffix(mat.name)
            if name.startswith("+"):
                continue
            role = cls.infer_role(mat, asset_class)
            tag_type = profile["shader_types"].get(role, profile["shader_types"]["object"])
            entry = {"material": name, "role": role, "tag_type": tag_type, "fields": {}}
            if target_game in ("HALO_3", "HALO_REACH"):
                entry["fields"] = {
                    "albedo": "two_change_color" if any(k in name for k in ("armor", "armour", "spartan", "cc")) else "default",
                    "bump_mapping": "standard",
                    "material_model": "cook_torrance",
                    "base_map": "<name>_diff", "bump_map": f"<name>{profile['normal_suffix']}",
                    "material_texture": "<name>_control",
                }
                if role == "emissive":
                    entry["fields"]["self_illumination"] = "simple"
                if role == "glass":
                    entry["fields"]["blend_mode"] = "alpha_blend"
            elif target_game == "HALO_CE":
                entry["fields"] = {"base_map": "<name>_diff", "bump_map": f"<name>{profile['normal_suffix']}"}
            mat["halo_shader_type"] = tag_type
            mat["halo_shader_role"] = role
            plans.append(entry)
        return plans

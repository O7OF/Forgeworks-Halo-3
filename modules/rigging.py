# -*- coding: utf-8 -*-
"""
Halo Rigging Engine & Animation-Safe Bone Renamer
Universal Action F-curve rewriter supporting Blender 5.x layered actions and legacy systems.
Created by O7OF | Shoutout to c20 Reclaimers Wiki | Thank you Bungie
"""

import bpy
from mathutils import Vector


class HaloBoneManager:
    """Animation-Safe Character, Vehicle, and Weapon Rigging & Bone Naming Engine.
    Renames armature bones while dynamically updating all Action F-curves, vertex groups,
    and bone constraints so character animations, weapon rigs, and player models NEVER break!"""

    SPARTAN_BIPED_BONES = [
        # (Bungie Canon, 3ds Max Bip01, Halo Side, Blender L/R)
        ("b_root", "bip01", "root", "root"),
        ("b_pelvis", "bip01 pelvis", "pelvis", "pelvis"),
        ("b_spine", "bip01 spine", "spine", "spine"),
        ("b_spine1", "bip01 spine1", "spine1", "spine1"),
        ("b_chest", "bip01 spine2", "chest", "chest"),
        ("b_neck", "bip01 neck", "neck", "neck"),
        ("b_head", "bip01 head", "head", "head"),
        # Left Arm
        ("b_l_clavicle", "bip01 l clavicle", "l clavicle", "clavicle.L"),
        ("b_l_upperarm", "bip01 l upperarm", "l upperarm", "upperarm.L"),
        ("b_l_forearm", "bip01 l forearm", "l forearm", "forearm.L"),
        ("b_l_hand", "bip01 l hand", "l hand", "hand.L"),
        # Right Arm
        ("b_r_clavicle", "bip01 r clavicle", "r clavicle", "clavicle.R"),
        ("b_r_upperarm", "bip01 r upperarm", "r upperarm", "upperarm.R"),
        ("b_r_forearm", "bip01 r forearm", "r forearm", "forearm.R"),
        ("b_r_hand", "bip01 r hand", "r hand", "hand.R"),
        # Left Leg
        ("b_l_thigh", "bip01 l thigh", "l thigh", "thigh.L"),
        ("b_l_calf", "bip01 l calf", "l calf", "calf.L"),
        ("b_l_foot", "bip01 l foot", "l foot", "foot.L"),
        ("b_l_toe", "bip01 l toe0", "l toe", "toe.L"),
        # Right Leg
        ("b_r_thigh", "bip01 r thigh", "r thigh", "thigh.R"),
        ("b_r_calf", "bip01 r calf", "r calf", "calf.R"),
        ("b_r_foot", "bip01 r foot", "r foot", "foot.R"),
        ("b_r_toe", "bip01 r toe0", "r toe", "toe.R"),
    ]

    @staticmethod
    def get_action_fcurves(action):
        """Universal Action F-curve extractor supporting Blender 5.x Layered Actions and legacy fcurves."""
        curves = []
        if hasattr(action, "fcurves"):
            curves.extend(action.fcurves)
        if hasattr(action, "layers"):
            for layer in action.layers:
                if hasattr(layer, "strips"):
                    for strip in layer.strips:
                        if hasattr(strip, "channelbags"):
                            for cb in strip.channelbags:
                                if hasattr(cb, "fcurves"):
                                    curves.extend(cb.fcurves)
        return curves

    @staticmethod
    def rename_all_bones(
        armature_obj,
        target_style="BUNGIE_CANON",
        update_actions=True,
        update_vertex_groups=True,
        update_constraints=True
    ):
        """Renames bones in armature and synchronizes Action animation F-curves and vertex groups."""
        if not armature_obj or armature_obj.type != 'ARMATURE':
            return {"bones_renamed": 0, "fcurves_updated": 0, "vgroups_updated": 0}

        style_idx = 0
        if target_style == "MAX_BIP01":
            style_idx = 1
        elif target_style == "HALO_SIDE":
            style_idx = 2
        elif target_style == "BLENDER_LR":
            style_idx = 3

        # Map current bone name to target bone name
        rename_map = {}
        for row in HaloBoneManager.SPARTAN_BIPED_BONES:
            target_name = row[style_idx]
            for alias in row:
                if alias != target_name:
                    rename_map[alias] = target_name

        bpy.context.view_layer.objects.active = armature_obj
        if bpy.context.mode != 'OBJECT':
            try:
                bpy.ops.object.mode_set(mode='OBJECT')
            except Exception:
                pass

        bones_renamed = 0
        actual_renames = {}

        # Rename bones in edit mode
        bpy.ops.object.mode_set(mode='EDIT')
        for bone in armature_obj.data.edit_bones:
            cur_name = bone.name
            if cur_name in rename_map:
                new_name = rename_map[cur_name]
                if cur_name != new_name:
                    bone.name = new_name
                    actual_renames[cur_name] = new_name
                    bones_renamed += 1
        bpy.ops.object.mode_set(mode='OBJECT')

        # Synchronize Action F-curves
        fcurves_updated = 0
        if update_actions and actual_renames:
            for act in bpy.data.actions:
                curves = HaloBoneManager.get_action_fcurves(act)
                for fc in curves:
                    dp = fc.data_path
                    for old_b, new_b in actual_renames.items():
                        old_pattern = f'pose.bones["{old_b}"]'
                        if old_pattern in dp:
                            fc.data_path = dp.replace(old_pattern, f'pose.bones["{new_b}"]')
                            fcurves_updated += 1
                            break

        # Synchronize child mesh vertex groups
        vgroups_updated = 0
        if update_vertex_groups and actual_renames:
            for obj in bpy.data.objects:
                if obj.type == 'MESH' and (obj.parent == armature_obj or any(m.type == 'ARMATURE' and m.object == armature_obj for m in obj.modifiers)):
                    for vg in obj.vertex_groups:
                        if vg.name in actual_renames:
                            vg.name = actual_renames[vg.name]
                            vgroups_updated += 1

        # Synchronize bone constraints
        if update_constraints and actual_renames:
            for pbone in armature_obj.pose.bones:
                for c in pbone.constraints:
                    if hasattr(c, "subtarget") and c.subtarget in actual_renames:
                        c.subtarget = actual_renames[c.subtarget]

        return {
            "bones_renamed": bones_renamed,
            "fcurves_updated": fcurves_updated,
            "vgroups_updated": vgroups_updated
        }

    @staticmethod
    def generate_spartan_reference_rig(context):
        """Generates canon Halo Spartan reference biped armature (70.3 JMS height)."""
        arm_data = bpy.data.armatures.new("Halo_Spartan_Reference_Rig")
        arm_obj = bpy.data.objects.new("Halo_Spartan_Reference_Rig", arm_data)
        context.scene.collection.objects.link(arm_obj)
        context.view_layer.objects.active = arm_obj
        bpy.ops.object.mode_set(mode='EDIT')

        # Create canon hierarchy
        eb = arm_data.edit_bones

        b_root = eb.new("b_root")
        b_root.head = Vector((0.0, 0.0, 0.0))
        b_root.tail = Vector((0.0, 0.0, 5.0))

        b_pelvis = eb.new("b_pelvis")
        b_pelvis.head = Vector((0.0, 0.0, 37.0))
        b_pelvis.tail = Vector((0.0, 0.0, 42.0))
        b_pelvis.parent = b_root

        b_spine = eb.new("b_spine")
        b_spine.head = Vector((0.0, 0.0, 42.0))
        b_spine.tail = Vector((0.0, 0.0, 47.0))
        b_spine.parent = b_pelvis

        b_chest = eb.new("b_chest")
        b_chest.head = Vector((0.0, 0.0, 47.0))
        b_chest.tail = Vector((0.0, 0.0, 58.0))
        b_chest.parent = b_spine

        b_neck = eb.new("b_neck")
        b_neck.head = Vector((0.0, 0.0, 58.0))
        b_neck.tail = Vector((0.0, 0.0, 62.0))
        b_neck.parent = b_chest

        b_head = eb.new("b_head")
        b_head.head = Vector((0.0, 0.0, 62.0))
        b_head.tail = Vector((0.0, 0.0, 70.3))
        b_head.parent = b_neck

        bpy.ops.object.mode_set(mode='OBJECT')
        return arm_obj

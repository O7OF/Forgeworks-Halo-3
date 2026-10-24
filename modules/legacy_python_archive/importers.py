# -*- coding: utf-8 -*-
"""
Halo Multi-Game Model & Scene Importers (.ASS, .JMS, .JMI, .JMF, .RMF)
Compatible with HCEK, H2EK, H3EK, HREK, and Reclaimer formats.
Provides fast standalone pure-Python parsers with optional io_scene_halo integration.
Created by O7OF | Shoutout to c20 Reclaimers Wiki | Thank you Bungie
"""

import os
import re
import struct
import math
import bpy
import bmesh
from mathutils import Vector, Matrix, Quaternion, Euler
from bpy_extras.io_utils import ImportHelper

from . import constants
from .shaders import Halo3ShaderManager


# ==============================================================================
# 1. STANDALONE NATIVE ASS IMPORTER (.ASS)
# ==============================================================================

class HaloNativeAssImporter:
    """Standalone pure-Python parser for Bungie Amalgam Scene Specification (.ASS) files."""

    @staticmethod
    def import_ass(filepath, context=None):
        if context is None:
            context = bpy.context

        if not os.path.isfile(filepath):
            return None

        # Try toolkit first if available
        if hasattr(bpy.ops.import_scene, "ass"):
            try:
                res = bpy.ops.import_scene.ass(filepath=filepath)
                if res == {'FINISHED'}:
                    return context.active_object
            except Exception as e:
                print(f"[ASS IMPORTER] io_scene_halo warning: {e}. Using native reader...")

        # Native parsing
        with open(filepath, "r", encoding="utf-8", errors="ignore") as f:
            lines = [line.strip() for line in f]

        line_idx = 0
        total_lines = len(lines)

        def next_line():
            nonlocal line_idx
            while line_idx < total_lines:
                l = lines[line_idx]
                line_idx += 1
                if l and not l.startswith(";"):
                    return l
            return ""

        # Parse Header
        header = next_line()
        if not header.startswith("###"):
            pass

        materials = []
        objects = []

        line_idx = 0
        while line_idx < total_lines:
            l = lines[line_idx]
            line_idx += 1

            if not l or l.startswith(";"):
                continue

            if l == "### MATERIALS ###":
                mat_count_str = next_line()
                mat_count = int(mat_count_str) if mat_count_str.isdigit() else 0
                for _ in range(mat_count):
                    m_name = next_line()
                    m_path = next_line()
                    materials.append((m_name, m_path))

            elif l == "### OBJECTS ###":
                obj_count_str = next_line()
                obj_count = int(obj_count_str) if obj_count_str.isdigit() else 0
                for _ in range(obj_count):
                    obj_name = next_line()
                    # Vertices
                    vert_hdr = next_line()
                    vert_cnt = int(vert_hdr) if vert_hdr.isdigit() else 0
                    verts = []
                    normals = []
                    colors = []
                    uv_sets = []

                    for _ in range(vert_cnt):
                        pos_str = next_line()
                        norm_str = next_line()
                        node_str = next_line()
                        col_str = next_line()
                        uv_cnt_str = next_line()
                        uv_cnt = int(uv_cnt_str) if uv_cnt_str.isdigit() else 1

                        p_vals = [float(x) for x in pos_str.split()] if pos_str else [0.0, 0.0, 0.0]
                        n_vals = [float(x) for x in norm_str.split()] if norm_str else [0.0, 0.0, 1.0]
                        c_vals = [float(x) for x in col_str.split()] if col_str else [1.0, 1.0, 1.0]

                        uvs = []
                        for _ in range(uv_cnt):
                            uv_line = next_line()
                            uv_pts = [float(x) for x in uv_line.split()] if uv_line else [0.0, 0.0]
                            uvs.append(uv_pts)

                        verts.append(p_vals[:3])
                        normals.append(n_vals[:3])
                        colors.append(c_vals[:3])
                        uv_sets.append(uvs)

                    # Triangles
                    tri_hdr = next_line()
                    tri_cnt = int(tri_hdr) if tri_hdr.isdigit() else 0
                    faces = []
                    face_mat_indices = []

                    for _ in range(tri_cnt):
                        tri_line = next_line()
                        parts = tri_line.split()
                        if len(parts) >= 4:
                            mat_idx = int(parts[0])
                            v0 = int(parts[1])
                            v1 = int(parts[2])
                            v2 = int(parts[3])
                            faces.append((v0, v1, v2))
                            face_mat_indices.append(mat_idx)

                    objects.append({
                        "name": obj_name or "ass_mesh",
                        "verts": verts,
                        "normals": normals,
                        "colors": colors,
                        "uv_sets": uv_sets,
                        "faces": faces,
                        "mat_indices": face_mat_indices
                    })

        # Build in Blender
        created_objs = []
        for obj_data in objects:
            mesh = bpy.data.meshes.new(obj_data["name"])
            mesh.from_pydata(obj_data["verts"], [], obj_data["faces"])
            mesh.update()

            if obj_data.get("normals") and len(obj_data["normals"]) == len(mesh.vertices):
                for p in mesh.polygons:
                    p.use_smooth = True
                if hasattr(mesh, "normals_split_custom_set_from_vertices"):
                    try:
                        mesh.normals_split_custom_set_from_vertices(obj_data["normals"])
                    except Exception:
                        pass

            # Materials
            for m_name, m_path in materials:
                Halo3ShaderManager.apply_shader_to_slot(mesh, m_name or "default")


            # Assign material indices
            if obj_data["mat_indices"] and len(obj_data["mat_indices"]) == len(mesh.polygons):
                for p_idx, poly in enumerate(mesh.polygons):
                    poly.material_index = max(0, min(len(mesh.materials) - 1, obj_data["mat_indices"][p_idx]))

            # UVs
            if obj_data["uv_sets"] and obj_data["uv_sets"][0]:
                max_uvs = min(4, len(obj_data["uv_sets"][0]))
                for uv_idx in range(max_uvs):
                    uv_layer = mesh.uv_layers.new(name=f"UV_{uv_idx}")
                    for poly in mesh.polygons:
                        for loop_idx in poly.loop_indices:
                            v_idx = mesh.loops[loop_idx].vertex_index
                            if v_idx < len(obj_data["uv_sets"]) and uv_idx < len(obj_data["uv_sets"][v_idx]):
                                uv_val = obj_data["uv_sets"][v_idx][uv_idx]
                                uv_layer.data[loop_idx].uv = (uv_val[0], uv_val[1])

            obj = bpy.data.objects.new(mesh.name, mesh)
            context.scene.collection.objects.link(obj)
            created_objs.append(obj)

        if created_objs:
            context.view_layer.objects.active = created_objs[0]
            for o in created_objs:
                o.select_set(True)
            return created_objs[0]

        return None


# ==============================================================================
# 2. STANDALONE NATIVE JMS & JMF IMPORTER (.JMS, .JMF)
# ==============================================================================

class HaloNativeJmsImporter:
    """Standalone pure-Python parser for Bungie Jointed Model Skeleton (.JMS) and (.JMF) files."""

    @staticmethod
    def import_jms(filepath, context=None):
        if context is None:
            context = bpy.context

        if not os.path.isfile(filepath):
            return None

        # Try toolkit first if available
        if hasattr(bpy.ops.import_scene, "jms"):
            try:
                res = bpy.ops.import_scene.jms(filepath=filepath)
                if res == {'FINISHED'}:
                    return context.active_object
            except Exception as e:
                print(f"[JMS IMPORTER] io_scene_halo warning: {e}. Using native reader...")

        with open(filepath, "r", encoding="utf-8", errors="ignore") as f:
            raw_lines = [l.strip() for l in f if l.strip() and not l.strip().startswith(";")]

        if not raw_lines:
            return None

        idx = 0
        def get_line():
            nonlocal idx
            if idx < len(raw_lines):
                val = raw_lines[idx]
                idx += 1
                return val
            return ""

        version_str = get_line()
        try:
            version = int(version_str)
        except ValueError:
            version = 8213

        # Checksum
        get_line()

        # Nodes
        node_cnt_str = get_line()
        node_cnt = int(node_cnt_str) if node_cnt_str.isdigit() else 0
        nodes = []
        for _ in range(node_cnt):
            n_name = get_line()
            p_or_c = get_line()
            if version < 8205:
                get_line() # sibling
            rot_str = get_line()
            trans_str = get_line()
            nodes.append({"name": n_name, "parent": p_or_c, "rot": rot_str, "trans": trans_str})

        # Materials
        mat_cnt_str = get_line()
        mat_cnt = int(mat_cnt_str) if mat_cnt_str.isdigit() else 0
        materials = []
        for _ in range(mat_cnt):
            m_name = get_line()
            m_path = get_line()
            materials.append(m_name)

        # Markers
        marker_cnt_str = get_line()
        marker_cnt = int(marker_cnt_str) if marker_cnt_str.isdigit() else 0
        for _ in range(marker_cnt):
            get_line() # name
            get_line() # node
            get_line() # rot
            get_line() # trans
            get_line() # radius

        # Regions
        region_cnt_str = get_line()
        region_cnt = int(region_cnt_str) if region_cnt_str.isdigit() else 0
        regions = []
        for _ in range(region_cnt):
            regions.append(get_line())

        # Vertices
        vert_cnt_str = get_line()
        vert_cnt = int(vert_cnt_str) if vert_cnt_str.isdigit() else 0
        verts = []
        normals = []
        uvs = []

        for _ in range(vert_cnt):
            node_idx_str = get_line()
            pos_str = get_line()
            norm_str = get_line()
            node1_str = get_line()
            w1_str = get_line()
            uv_str = get_line()
            if version >= 8210:
                get_line() # region

            p_vals = [float(x) for x in pos_str.split()] if pos_str else [0.0, 0.0, 0.0]
            n_vals = [float(x) for x in norm_str.split()] if norm_str else [0.0, 0.0, 1.0]
            uv_vals = [float(x) for x in uv_str.split()] if uv_str else [0.0, 0.0]

            verts.append(p_vals[:3])
            normals.append(n_vals[:3])
            uvs.append(uv_vals[:2])

        # Triangles
        tri_cnt_str = get_line()
        tri_cnt = int(tri_cnt_str) if tri_cnt_str.isdigit() else 0
        faces = []
        face_mats = []

        for _ in range(tri_cnt):
            t_line = get_line()
            parts = t_line.split()
            if version >= 8200 and len(parts) >= 5:
                # region, mat, v0, v1, v2
                face_mats.append(int(parts[1]))
                faces.append((int(parts[2]), int(parts[3]), int(parts[4])))
            elif len(parts) >= 3:
                faces.append((int(parts[0]), int(parts[1]), int(parts[2])))
                face_mats.append(0)
            else:
                # Multi-line triangle format in older CE versions
                m_idx = int(parts[0]) if parts else 0
                v0 = int(get_line())
                v1 = int(get_line())
                v2 = int(get_line())
                faces.append((v0, v1, v2))
                face_mats.append(m_idx)

        # Build Mesh in Blender
        mesh_name = os.path.splitext(os.path.basename(filepath))[0]
        mesh = bpy.data.meshes.new(mesh_name)
        mesh.from_pydata(verts, [], faces)
        mesh.update()

        if normals and len(normals) == len(mesh.vertices):
            for p in mesh.polygons:
                p.use_smooth = True
            if hasattr(mesh, "normals_split_custom_set_from_vertices"):
                try:
                    mesh.normals_split_custom_set_from_vertices(normals)
                except Exception:
                    pass

        # Materials
        for m_name in materials:
            Halo3ShaderManager.apply_shader_to_slot(mesh, m_name or "default")


        if face_mats and len(face_mats) == len(mesh.polygons):
            for p_idx, poly in enumerate(mesh.polygons):
                poly.material_index = max(0, min(len(mesh.materials) - 1, face_mats[p_idx]))

        # UV Layer
        if uvs:
            uv_layer = mesh.uv_layers.new(name="UV_0")
            for poly in mesh.polygons:
                for loop_idx in poly.loop_indices:
                    v_idx = mesh.loops[loop_idx].vertex_index
                    if v_idx < len(uvs):
                        uv_layer.data[loop_idx].uv = (uvs[v_idx][0], uvs[v_idx][1])

        obj = bpy.data.objects.new(mesh_name, mesh)
        context.scene.collection.objects.link(obj)
        context.view_layer.objects.active = obj
        obj.select_set(True)

        return obj


# ==============================================================================
# 3. STANDALONE NATIVE JMI IMPORTER (.JMI)
# ==============================================================================

class HaloNativeJmiImporter:
    """Standalone parser for Jointed Model Instance (.JMI) files."""

    @staticmethod
    def import_jmi(filepath, context=None):
        if context is None:
            context = bpy.context

        if not os.path.isfile(filepath):
            return None

        # JMI defines list of object instances referencing companion JMS file
        obj_names = []
        with open(filepath, "r", encoding="utf-8", errors="ignore") as f:
            raw_lines = [l.strip() for l in f if l.strip() and not l.strip().startswith(";")]

        idx = 0
        if raw_lines:
            # Version
            v = raw_lines[0]
            if len(raw_lines) > 1 and raw_lines[1].isdigit():
                tot_objs = int(raw_lines[1])
                for line in raw_lines[2:2 + tot_objs]:
                    obj_names.append(line)

        # Check for companion JMS file
        base_dir = os.path.dirname(filepath)
        base_name = os.path.splitext(os.path.basename(filepath))[0]
        companion_jms = os.path.join(base_dir, f"{base_name}.jms")

        imported_obj = None
        if os.path.isfile(companion_jms):
            imported_obj = HaloNativeJmsImporter.import_jms(companion_jms, context)

        if not imported_obj and obj_names:
            # Check for individual JMS files matching names
            for name in obj_names:
                sub_jms = os.path.join(base_dir, f"{name}.jms")
                if os.path.isfile(sub_jms):
                    imported_obj = HaloNativeJmsImporter.import_jms(sub_jms, context)

        if not imported_obj:
            # Create instance empties
            collection = bpy.data.collections.new(f"JMI_{base_name}")
            context.scene.collection.children.link(collection)
            for name in (obj_names or [base_name]):
                empty = bpy.data.objects.new(name, None)
                collection.objects.link(empty)
            return collection

        return imported_obj


# ==============================================================================
# 4. RECLAIMER MODEL FORMAT IMPORTER (.RMF)
# ==============================================================================

class HaloNativeRmfImporter:
    """Standalone pure-Python parser for Reclaimer Model Format (.RMF) files.
    Supports both Reclaimer binary chunks and text/structured mesh streams.
    """

    @staticmethod
    def import_rmf(filepath, context=None):
        if context is None:
            context = bpy.context

        if not os.path.isfile(filepath):
            return None

        # Check if external RMF operator is available
        if hasattr(bpy.ops.import_scene, "rmf"):
            try:
                res = bpy.ops.import_scene.rmf(filepath=filepath)
                if res == {'FINISHED'}:
                    return context.active_object
            except Exception as e:
                print(f"[RMF IMPORTER] External RMF operator reported: {e}. Using native engine...")

        # Binary / Structured RMF Reader
        with open(filepath, "rb") as f:
            data = f.read()

        if len(data) < 16:
            return None

        magic = data[:4]
        # Text/ASCII JSON Fallback
        if magic.startswith(b"{") or magic.startswith(b";") or magic.startswith(b"#"):
            return HaloNativeRmfImporter._parse_text_rmf(filepath, context)

        # Binary RMF structure (Header: Magic 4B, Version 4B, Mesh Count 4B, Mat Count 4B)
        try:
            magic_str = magic.decode("ascii", errors="ignore")
            offset = 4
            version, mesh_count, mat_count = struct.unpack_from("<III", data, offset)
            offset += 12

            # Materials
            materials = []
            for _ in range(mat_count):
                name_len = struct.unpack_from("<H", data, offset)[0]
                offset += 2
                mat_name = data[offset:offset + name_len].decode("utf-8", errors="ignore")
                offset += name_len
                materials.append(mat_name)

            created_objs = []
            # Meshes
            for m_i in range(mesh_count):
                m_name_len = struct.unpack_from("<H", data, offset)[0]
                offset += 2
                mesh_name = data[offset:offset + m_name_len].decode("utf-8", errors="ignore") or f"rmf_mesh_{m_i}"
                offset += m_name_len

                v_count, f_count = struct.unpack_from("<II", data, offset)
                offset += 8

                verts = []
                normals = []
                uvs = []

                # Vertex block: X, Y, Z, NX, NY, NZ, U, V (8 floats = 32 bytes)
                for _ in range(v_count):
                    vx, vy, vz, nx, ny, nz, u, v = struct.unpack_from("<8f", data, offset)
                    offset += 32
                    verts.append((vx, vy, vz))
                    normals.append((nx, ny, nz))
                    uvs.append((u, 1.0 - v))

                # Faces block: Triangles (3 x uint32 or uint16)
                faces = []
                idx_fmt = "<III"
                idx_size = 12
                if v_count < 65535 and offset + f_count * 6 <= len(data):
                    idx_fmt = "<HHH"
                    idx_size = 6

                for _ in range(f_count):
                    i0, i1, i2 = struct.unpack_from(idx_fmt, data, offset)
                    offset += idx_size
                    if i0 < len(verts) and i1 < len(verts) and i2 < len(verts):
                        faces.append((i0, i1, i2))

                mesh = bpy.data.meshes.new(mesh_name)
                mesh.from_pydata(verts, [], faces)
                mesh.update()

                if normals and len(normals) == len(mesh.vertices):
                    for p in mesh.polygons:
                        p.use_smooth = True
                    if hasattr(mesh, "normals_split_custom_set_from_vertices"):
                        try:
                            mesh.normals_split_custom_set_from_vertices(normals)
                        except Exception:
                            pass

                for m_name in materials:
                    Halo3ShaderManager.apply_shader_to_slot(mesh, m_name)


                if uvs:
                    uv_layer = mesh.uv_layers.new(name="UV_0")
                    for poly in mesh.polygons:
                        for loop_idx in poly.loop_indices:
                            v_idx = mesh.loops[loop_idx].vertex_index
                            if v_idx < len(uvs):
                                uv_layer.data[loop_idx].uv = uvs[v_idx]

                obj = bpy.data.objects.new(mesh_name, mesh)
                context.scene.collection.objects.link(obj)
                created_objs.append(obj)

            if created_objs:
                context.view_layer.objects.active = created_objs[0]
                return created_objs[0]

        except Exception as e:
            print(f"[RMF IMPORTER] Binary parser encountered error: {e}. Trying text fallback...")
            return HaloNativeRmfImporter._parse_text_rmf(filepath, context)

        return None

    @staticmethod
    def _parse_text_rmf(filepath, context):
        """Text/Structured fallback parser for RMF geometry."""
        with open(filepath, "r", encoding="utf-8", errors="ignore") as f:
            lines = [l.strip() for l in f if l.strip()]

        verts = []
        faces = []
        for l in lines:
            if l.startswith("v "):
                parts = [float(x) for x in l.split()[1:4]]
                verts.append(parts)
            elif l.startswith("f "):
                parts = [int(x.split("/")[0]) - 1 for x in l.split()[1:4]]
                faces.append(parts)

        if verts and faces:
            mesh_name = os.path.splitext(os.path.basename(filepath))[0]
            mesh = bpy.data.meshes.new(mesh_name)
            mesh.from_pydata(verts, [], faces)
            mesh.update()
            obj = bpy.data.objects.new(mesh_name, mesh)
            context.scene.collection.objects.link(obj)
            return obj
        return None


# ==============================================================================
# 5. BLENDER OPERATOR DEFINITIONS WITH FILE DIALOGS
# ==============================================================================

class HALO_OT_import_ass(bpy.types.Operator, ImportHelper):
    """Import a Halo .ASS (Amalgam Scene Specification) file"""
    bl_idname = "halo3.import_ass"
    bl_label = "Import Halo .ASS"
    bl_description = "Imports a Halo 2, 3, or Reach .ASS model or scenario into the scene"
    bl_options = {'REGISTER', 'UNDO'}

    filename_ext = ".ass"
    filter_glob: bpy.props.StringProperty(default="*.ass", options={'HIDDEN'})

    def execute(self, context):
        obj = HaloNativeAssImporter.import_ass(self.filepath, context)
        if obj:
            self.report({'INFO'}, f"Successfully imported .ASS: {os.path.basename(self.filepath)}")
            return {'FINISHED'}
        self.report({'ERROR'}, f"Failed to parse .ASS file: {self.filepath}")
        return {'CANCELLED'}


class HALO_OT_import_jms(bpy.types.Operator, ImportHelper):
    """Import a Halo .JMS (Jointed Model Skeleton) file"""
    bl_idname = "halo3.import_jms"
    bl_label = "Import Halo .JMS"
    bl_description = "Imports a Halo CE, 2, 3, or Reach .JMS model into the scene"
    bl_options = {'REGISTER', 'UNDO'}

    filename_ext = ".jms"
    filter_glob: bpy.props.StringProperty(default="*.jms", options={'HIDDEN'})

    def execute(self, context):
        obj = HaloNativeJmsImporter.import_jms(self.filepath, context)
        if obj:
            self.report({'INFO'}, f"Successfully imported .JMS: {os.path.basename(self.filepath)}")
            return {'FINISHED'}
        self.report({'ERROR'}, f"Failed to parse .JMS file: {self.filepath}")
        return {'CANCELLED'}


class HALO_OT_import_jmi(bpy.types.Operator, ImportHelper):
    """Import a Halo .JMI (Jointed Model Instance) file"""
    bl_idname = "halo3.import_jmi"
    bl_label = "Import Halo .JMI"
    bl_description = "Imports a Halo .JMI instance definition and companion geometry"
    bl_options = {'REGISTER', 'UNDO'}

    filename_ext = ".jmi"
    filter_glob: bpy.props.StringProperty(default="*.jmi", options={'HIDDEN'})

    def execute(self, context):
        res = HaloNativeJmiImporter.import_jmi(self.filepath, context)
        if res:
            self.report({'INFO'}, f"Successfully imported .JMI: {os.path.basename(self.filepath)}")
            return {'FINISHED'}
        self.report({'ERROR'}, f"Failed to parse .JMI file: {self.filepath}")
        return {'CANCELLED'}


class HALO_OT_import_jmf(bpy.types.Operator, ImportHelper):
    """Import a Halo .JMF (Joint Model File) file"""
    bl_idname = "halo3.import_jmf"
    bl_label = "Import Halo .JMF"
    bl_description = "Imports a Halo .JMF geometry model into the scene"
    bl_options = {'REGISTER', 'UNDO'}

    filename_ext = ".jmf"
    filter_glob: bpy.props.StringProperty(default="*.jmf", options={'HIDDEN'})

    def execute(self, context):
        obj = HaloNativeJmsImporter.import_jms(self.filepath, context)
        if obj:
            self.report({'INFO'}, f"Successfully imported .JMF: {os.path.basename(self.filepath)}")
            return {'FINISHED'}
        self.report({'ERROR'}, f"Failed to parse .JMF file: {self.filepath}")
        return {'CANCELLED'}


class HALO_OT_import_rmf(bpy.types.Operator, ImportHelper):
    """Import a Reclaimer Model Format (.RMF) file"""
    bl_idname = "halo3.import_rmf"
    bl_label = "Import Reclaimer .RMF"
    bl_description = "Imports an extracted Halo model in Reclaimer Model Format (.RMF)"
    bl_options = {'REGISTER', 'UNDO'}

    filename_ext = ".rmf"
    filter_glob: bpy.props.StringProperty(default="*.rmf", options={'HIDDEN'})

    def execute(self, context):
        obj = HaloNativeRmfImporter.import_rmf(self.filepath, context)
        if obj:
            self.report({'INFO'}, f"Successfully imported .RMF: {os.path.basename(self.filepath)}")
            return {'FINISHED'}
        self.report({'ERROR'}, f"Failed to parse .RMF file: {self.filepath}")
        return {'CANCELLED'}


class HALO_OT_import_wrl(bpy.types.Operator, ImportHelper):
    """Import a Tool .WRL error geometry file"""
    bl_idname = "halo3.import_wrl"
    bl_label = "Import Tool .WRL Errors"
    bl_description = "Imports Tool VRML .wrl error files and visualizes compiler geometry issues"
    bl_options = {'REGISTER', 'UNDO'}

    filename_ext = ".wrl"
    filter_glob: bpy.props.StringProperty(default="*.wrl", options={'HIDDEN'})

    def execute(self, context):
        from .diagnostics import HaloWrlImporter
        count = HaloWrlImporter.import_wrl(self.filepath, context)
        if count > 0:
            self.report({'INFO'}, f"Successfully imported {count} Tool error objects")
            return {'FINISHED'}
        self.report({'WARNING'}, "No error lines or faces found in WRL file")
        return {'CANCELLED'}


IMPORT_CLASSES = [
    HALO_OT_import_ass,
    HALO_OT_import_jms,
    HALO_OT_import_jmi,
    HALO_OT_import_jmf,
    HALO_OT_import_rmf,
    HALO_OT_import_wrl,
]


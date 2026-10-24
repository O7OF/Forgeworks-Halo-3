# -*- coding: utf-8 -*-
"""
Halo Jointed Model Instance (.JMI) Exporter
Compatible with H2EK, H3EK, and HREK.
Integrates io_scene_halo pipeline with standalone pure-Python fallback.
Created by O7OF | Shoutout to c20 Reclaimers Wiki | Thank you Bungie
"""

import os
import bpy

from . import constants
from .export_jms import HaloNativeJmsExporter


class HaloNativeJmiExporter:
    """Standalone pure-Python exporter for Halo Jointed Model Instance (.JMI) files."""

    @staticmethod
    def export_jmi(filepath, context=None, version=8213, game_title="halo3", scale=1.0):
        if context is None:
            context = bpy.context

        os.makedirs(os.path.dirname(os.path.abspath(filepath)), exist_ok=True)

        export_objs = [o for o in context.scene.objects if o.type == 'MESH' and not o.name.startswith("scale_model")]
        if not export_objs:
            return False

        version_bounds = "8207-8213" if game_title == "halo3" else "8207-8210"

        with open(filepath, "w", encoding="utf-8") as f:
            f.write(";### VERSION ###\n")
            f.write(f"{version}\n")
            f.write(f";\t<{version_bounds}>\n\n")

            f.write(";### TOTAL OBJECTS ###\n")
            f.write(f"{len(export_objs)}\n")
            f.write(";\t<name>\n\n")

            for obj in export_objs:
                clean_name = obj.name.split("!", 1)[-1] if "!" in obj.name else obj.name
                f.write(f"{clean_name}\n")

            f.write("\n")

        # Also write base JMS companion file if required
        base_jms = os.path.splitext(filepath)[0] + ".jms"
        HaloNativeJmsExporter.export_jms(base_jms, context, version=version, game_title=game_title, scale=scale)

        return os.path.isfile(filepath) and os.path.getsize(filepath) > 0


def export_jmi_file(filepath, context=None, version=8213, game_title="halo3", scale=1.0, use_toolkit=True):
    """Universal JMI export dispatcher."""
    if context is None:
        context = bpy.context

    os.makedirs(os.path.dirname(os.path.abspath(filepath)), exist_ok=True)

    if use_toolkit and hasattr(bpy.ops.export_scene, "jmi"):
        try:
            res = bpy.ops.export_scene.jmi(filepath=filepath, game_title=game_title)
            if res == {'FINISHED'} and os.path.isfile(filepath) and os.path.getsize(filepath) > 0:
                return True
        except Exception as e:
            print(f"[JMI EXPORTER] io_scene_halo exporter reported warning: {e}. Falling back to native engine...")

    return HaloNativeJmiExporter.export_jmi(
        filepath=filepath,
        context=context,
        version=version,
        game_title=game_title,
        scale=scale
    )

# -*- coding: utf-8 -*-
"""
Halo 3 & Reach Modular Tool Suite Package
Created by O7OF | Shoutout to c20 Reclaimers Wiki | Thank you Bungie
"""

from . import constants
from .export_ass import HaloNativeAssExporter, export_ass_file
from .export_jms import HaloNativeJmsExporter, export_jms_file
from .export_jmi import HaloNativeJmiExporter, export_jmi_file
from .export_gr2 import HaloReachGr2Exporter, export_gr2_file, HALO_OT_export_gr2
from .importers import (
    HaloNativeAssImporter,
    HaloNativeJmsImporter,
    HaloNativeJmiImporter,
    HaloNativeRmfImporter,
    HALO_OT_import_ass,
    HALO_OT_import_jms,
    HALO_OT_import_jmi,
    HALO_OT_import_jmf,
    HALO_OT_import_rmf,
    HALO_OT_import_wrl,
    IMPORT_CLASSES,
)
from .compiler import HaloCompiler
from .rocks import HaloRockGenerator
from .foliage import HaloFoliageGenerator
from .smoother import HaloGeometrySmoother
from .rigging import HaloBoneManager
from .shaders import (
    Halo3ShaderManager,
    HALO3_OT_save_custom_shader,
    HALO3_OT_apply_custom_shader,
    HALO3_OT_delete_custom_shader,
    SHADER_OPERATORS,
)
from .diagnostics import (
    HaloBspDiagnostics,
    HaloPortalFixer,
    HaloWrlImporter,
    HALO3_OT_select_bsp_errors,
    HALO3_OT_import_wrl_errors,
    DIAGNOSTIC_OPERATORS,
)
from .structures import (
    HaloBunkerGenerator,
    HaloHallwayGenerator,
    HaloPortalGenerator,
    HaloSkyGenerator,
    HaloWaterGenerator,
    HaloAssetPrepper,
)
from .terrain import HaloTerrainGenerator
from .terrain_ce import HaloCeTerrainEngine
from .terrain_h2 import Halo2TerrainEngine
from .terrain_h3 import Halo3TerrainEngine
from .terrain_reach import HaloReachTerrainEngine
from .scale_models import (
    HaloScaleModelManager,
    HALO_OT_scale_model,
    HALO3_OT_scale_model_alias,
    HaloScaleModelProperties,
)
from .ui_panels import (
    Halo3ToolProperties,
    HALO3_PT_category_header,
    HALO3_PT_terrain_panel,
    HALO3_PT_structures_panel,
    HALO3_PT_nature_panel,
    HALO3_PT_repair_panel,
    HALO3_PT_shaders_panel,
    HALO3_PT_asset_prepper_panel,
    HALO3_PT_import_export_panel,
    HALO3_PT_rigging_panel,
    HALO_PT_ScaleModelHelper,
    HALO3_PT_about_panel,
)

__all__ = [
    "constants",
    "HaloNativeAssExporter",
    "export_ass_file",
    "HaloNativeJmsExporter",
    "export_jms_file",
    "HaloNativeJmiExporter",
    "export_jmi_file",
    "HaloReachGr2Exporter",
    "export_gr2_file",
    "HALO_OT_export_gr2",
    "HaloNativeAssImporter",
    "HaloNativeJmsImporter",
    "HaloNativeJmiImporter",
    "HaloNativeRmfImporter",
    "HALO_OT_import_ass",
    "HALO_OT_import_jms",
    "HALO_OT_import_jmi",
    "HALO_OT_import_jmf",
    "HALO_OT_import_rmf",
    "HALO_OT_import_wrl",
    "IMPORT_CLASSES",
    "HaloCompiler",
    "HaloRockGenerator",
    "HaloFoliageGenerator",
    "HaloGeometrySmoother",
    "HaloBoneManager",
    "Halo3ShaderManager",
    "HALO3_OT_save_custom_shader",
    "HALO3_OT_apply_custom_shader",
    "HALO3_OT_delete_custom_shader",
    "SHADER_OPERATORS",
    "HaloBspDiagnostics",
    "HaloPortalFixer",
    "HaloWrlImporter",
    "HALO3_OT_select_bsp_errors",
    "HALO3_OT_import_wrl_errors",
    "DIAGNOSTIC_OPERATORS",
    "HaloBunkerGenerator",
    "HaloHallwayGenerator",
    "HaloPortalGenerator",
    "HaloSkyGenerator",
    "HaloWaterGenerator",
    "HaloAssetPrepper",
    "HaloTerrainGenerator",
    "Halo3ToolProperties",
    "HALO3_PT_category_header",
    "HALO3_PT_terrain_panel",
    "HALO3_PT_structures_panel",
    "HALO3_PT_nature_panel",
    "HALO3_PT_repair_panel",
    "HALO3_PT_shaders_panel",
    "HALO3_PT_asset_prepper_panel",
    "HALO3_PT_import_export_panel",
    "HALO3_PT_rigging_panel",
    "HaloScaleModelManager",
    "HALO_OT_scale_model",
    "HALO3_OT_scale_model_alias",
    "HaloScaleModelProperties",
    "HALO_PT_ScaleModelHelper",
    "HALO3_PT_about_panel",
]

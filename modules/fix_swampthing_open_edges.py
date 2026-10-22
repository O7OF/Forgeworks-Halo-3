# fix_swampthing_open_edges.py
# Run inside Blender with the swampthing level open:
#   Scripting tab > Open this file > Run Script   (or paste into the Text Editor and Run)
# Save the .blend under a NEW name afterwards, re-export the JMS, run tool structure again.
#
# What it does (only to the level's mesh objects):
#   1. Water' quads (material swamp_a_water') become render-only (material swamp_a_water'!),
#      like the Watertop! quads. A single floating collideable quad is 4 open edges.
#   2. Welds split vertices on every mesh (Merge by Distance 0.01) and removes degenerate faces.
#   3. Caps the open ends of tree trunks, the stump, pipes and bases (fill holes + triangulate).
#   4. Caps the small holes left in "Main Swamp Ground" (the big rim that joins the skybox is kept).
#   5. Prints a report of anything still open, in world coordinates, to the System Console.
import bpy, bmesh
from mathutils import Vector

WELD = 0.01
NODES = ("b_levelroot", "frame swampthing")
NOT_PROPS = ("Main Swamp Ground", "skybox", "Portals", "Water", "Foilage")

def noncoll(name):
    return name.endswith("!") or name.startswith("+portal") or name.startswith("+unused") or name.endswith("$")

def boundary_loops(b):
    seen = set(); out = []
    for e in b.edges:
        if len(e.link_faces) != 1 or e in seen: continue
        st = [e]; comp = []
        while st:
            x = st.pop()
            if x in seen: continue
            seen.add(x); comp.append(x)
            for v in x.verts:
                for y in v.link_edges:
                    if len(y.link_faces) == 1 and y not in seen: st.append(y)
        out.append(comp)
    return out

def cap(b, edges):
    r = bmesh.ops.holes_fill(b, edges=edges, sides=0)
    if r["faces"]:
        bmesh.ops.triangulate(b, faces=r["faces"], quad_method='BEAUTY', ngon_method='BEAUTY')
        return 1
    return 0

def run(objs, log=print):
    # 1. water
    for o in objs:
        for s in getattr(o, "material_slots", []):
            if s.material and s.material.name == "swamp_a_water'":
                nm = bpy.data.materials.get("swamp_a_water'!")
                if nm is None:
                    nm = s.material.copy(); nm.name = "swamp_a_water'!"
                s.material = nm
    log("Water' quads -> render only (swamp_a_water'!)")
    # 2-4. per object
    for o in objs:
        if o.type != 'MESH' or o.name in NODES: continue
        me = o.data
        if me.users > 1: o.data = me = me.copy()
        mats = [s.material.name if s.material else "" for s in o.material_slots]
        if mats and all(noncoll(m) for m in mats): continue
        b = bmesh.new(); b.from_mesh(me)
        nv = len(b.verts)
        bmesh.ops.remove_doubles(b, verts=b.verts, dist=WELD)
        bmesh.ops.dissolve_degenerate(b, edges=b.edges, dist=WELD * 0.1)
        loose = [v for v in b.verts if not v.link_faces]
        if loose: bmesh.ops.delete(b, geom=loose, context='VERTS')
        loops = boundary_loops(b); caps = 0
        if o.name.startswith("Main Swamp Ground"):
            for comp in loops:
                if len(comp) < 100: caps += cap(b, comp)
        elif not o.name.startswith(NOT_PROPS):
            for comp in loops: caps += cap(b, comp)
        if caps: bmesh.ops.recalc_face_normals(b, faces=b.faces)
        left = sum(len(c) for c in boundary_loops(b))
        b.to_mesh(me); b.free(); me.update()
        if nv != len(me.vertices) or caps or left:
            log("%-24s welded %d->%d verts, %d holes capped, %d open edges left" % (o.name, nv, len(me.vertices), caps, left))
    # 5. whole-level check (collision faces only, world space)
    G = bmesh.new()
    for o in objs:
        if o.type != 'MESH' or o.name in NODES: continue
        t = bmesh.new(); t.from_mesh(o.data); t.transform(o.matrix_world)
        mats = [s.material.name if s.material else "" for s in o.material_slots]
        kill = [f for f in t.faces if f.material_index < len(mats) and noncoll(mats[f.material_index])]
        if kill: bmesh.ops.delete(t, geom=kill, context='FACES')
        tm = bpy.data.meshes.new("_swampcheck"); t.to_mesh(tm); t.free(); G.from_mesh(tm); bpy.data.meshes.remove(tm)
    bmesh.ops.remove_doubles(G, verts=G.verts, dist=WELD)
    loops = boundary_loops(G)
    multi = [e for e in G.edges if len(e.link_faces) > 2]
    log("LEVEL CHECK: %d collision faces, %d open edges in %d holes, %d edges shared by 3+ faces" %
        (len(G.faces), sum(len(c) for c in loops), len(loops), len(multi)))
    for comp in sorted(loops, key=len, reverse=True)[:30]:
        c = sum(((x.verts[0].co + x.verts[1].co) / 2 for x in comp), Vector()) / len(comp)
        log("   open hole, %3d edges, at (%.1f, %.1f, %.1f)" % (len(comp), c.x, c.y, c.z))
    for e in multi[:30]:
        c = (e.verts[0].co + e.verts[1].co) / 2
        log("   edge with %d faces at (%.1f, %.1f, %.1f)" % (len(e.link_faces), c.x, c.y, c.z))
    G.free()

if not globals().get("SWAMP_NO_AUTORUN"):
    run(list(bpy.context.scene.objects))
    print("Done. Save under a new name, then re-export the JMS and run tool structure.")

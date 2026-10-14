import bpy, bmesh
from mathutils import Vector
sc=bpy.context.scene
meshes=[o for o in sc.objects if o.type=='MESH']
# appliquer les transformations parentes
for o in meshes:
    mw=o.matrix_world.copy(); o.parent=None; o.matrix_world=mw
bpy.ops.object.select_all(action='DESELECT')
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active=meshes[0]
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.ops.object.join()
pad=bpy.context.view_layer.objects.active; pad.name="Pad"
for o in [o for o in sc.objects if o.type=='EMPTY']: bpy.data.objects.remove(o)
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.separate(type='LOOSE'); bpy.ops.object.mode_set(mode='OBJECT')
parts=[o for o in sc.objects if o.type=='MESH']
print("parts",len(parts))
def info(o):
    vs=[o.matrix_world@v.co for v in o.data.vertices]
    mn=Vector((min(v.x for v in vs),min(v.y for v in vs),min(v.z for v in vs))); mx=Vector((max(v.x for v in vs),max(v.y for v in vs),max(v.z for v in vs)))
    return (mn+mx)/2, mx-mn
def near(c, x, z, r): return ((c.x-x)**2+(c.z-z)**2)**0.5 < r
groups={}
for o in parts:
    c,s=info(o); g="Body"
    for side,sx in (("L",-0.0621),("R",0.0622)):
        if near(c,sx,0,0.03) and c.y>0.055: g="Stick"+side
    for side,sx in (("L",-1),("R",1)):
        if abs(c.x-sx*0.1201)<0.01 and abs(c.y+0.0231)<0.01 and abs(c.z-0.1004)<0.01: g=side+"2"
        if abs(c.x-sx*0.1211)<0.01 and abs(c.y-0.0248)<0.01 and abs(c.z-0.113)<0.01: g=side+"1"
    for name,(bx,bz) in {"Triangle":(0.1213,0.0736),"Circle":(0.1415,0.0548),"Cross":(0.1226,0.0346),"Square":(0.1026,0.0536),
                         "DpadUp":(-0.1218,0.0828),"DpadRight":(-0.0931,0.054),"DpadDown":(-0.1219,0.0252),"DpadLeft":(-0.1506,0.054)}.items():
        if near(c,bx,bz,0.012) and c.y>0.052 and max(s.x,s.z)<0.035: g=name
    if abs(c.x)<0.01 and abs(c.z-0.08)<0.012 and s.x>0.14 and c.y>0.04: g="Touchpad"
    if abs(c.x)<0.012 and abs(c.z-0.003)<0.012 and c.y>0.045 and s.x<0.03: g="PS"
    if abs(c.x)<0.005 and abs(c.z+0.0196)<0.005 and c.y>0.035: g="Mute"
    if abs(abs(c.x)-0.0913)<0.006 and abs(c.z-0.0963)<0.008 and c.y>0.05: g="Create" if c.x<0 else "Options"
    groups.setdefault(g,[]).append(o)
for g,objs in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active=objs[0]
    if len(objs)>1: bpy.ops.object.join()
    ob=bpy.context.view_layer.objects.active; ob.name=g
    c,s=info(ob)
    print("GROUP",g,len(objs),"faces",len(ob.data.polygons),"c",tuple(round(x,4) for x in c),"s",tuple(round(x,4) for x in s))
bpy.ops.wm.save_as_mainfile(filepath=r"C:\Users\zgopa\Documents\Claude\se7en dualsense\render\dualsense_parts.blend")

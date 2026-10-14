import bpy, bmesh, numpy as np
sc=bpy.context.scene
im=bpy.data.images["Image_2"]; w,h=im.size; px=np.empty(w*h*4,np.float32); im.pixels.foreach_get(px); px=px.reshape(h,w,4)
tp=sc.objects["Touchpad"]; me=tp.data; uvl=me.uv_layers["UVMap"]
sel=set()
for p in me.polygons:
    if me.materials[p.material_index].name!="1011": continue
    for li in p.loop_indices:
        u,v=uvl.data[li].uv
        if px[int((v%1)*h)%h,int((u%1)*w)%w,:3].max()>0.25: sel.add(p.index); break
for p in me.polygons: p.select = p.index in sel
bpy.ops.object.select_all(action='DESELECT'); tp.select_set(True); bpy.context.view_layer.objects.active=tp
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_mode(type='FACE'); bpy.ops.object.mode_set(mode='OBJECT')
for p in me.polygons: p.select = p.index in sel
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.separate(type='SELECTED'); bpy.ops.object.mode_set(mode='OBJECT')
lb=[o for o in sc.objects if o.name.startswith("Touchpad.")][0]; lb.name="Lightbar"
vs=[v.co for v in lb.data.vertices]
print("LB", len(lb.data.polygons), [round(min(v[i] for v in vs),4) for i in range(3)], [round(max(v[i] for v in vs),4) for i in range(3)])
bpy.ops.wm.save_mainfile()

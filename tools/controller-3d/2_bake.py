import bpy, sys, math, time
args=sys.argv[sys.argv.index("--")+1:]
size=int(args[0]); samples=int(args[1]); out=args[2]
sc=bpy.context.scene
p=bpy.context.preferences.addons['cycles'].preferences
p.compute_device_type='OPTIX'; p.get_devices()
for d in p.devices: d.use = d.type=='OPTIX'
sc.cycles.device='GPU'; sc.cycles.samples=samples

import bmesh, numpy as np
alpha_img={}
for m in bpy.data.materials:
    if not m.node_tree: continue
    b=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    if b.inputs['Alpha'].is_linked:
        im=b.inputs['Alpha'].links[0].from_node.image; w,h=im.size; px=np.empty(w*h*4,dtype=np.float32); im.pixels.foreach_get(px)
        alpha_img[m.name]=(px.reshape(h,w,4)[:,:,3],w,h)
removed=0
for o in [o for o in sc.objects if o.type=='MESH']:
    bm=bmesh.new(); bm.from_mesh(o.data); uv=bm.loops.layers.uv[0]; bm.faces.ensure_lookup_table()
    seen=set(); kill=[]
    for f in bm.faces:
        if f.index in seen: continue
        st=[f]; seen.add(f.index); fs=[]
        while st:
            g=st.pop(); fs.append(g)
            for e in g.edges:
                for hh in e.link_faces:
                    if hh.index not in seen: seen.add(hh.index); st.append(hh)
        mat=o.data.materials[fs[0].material_index].name
        if mat not in alpha_img: continue
        a,w,h=alpha_img[mat]; vals=[]
        for g in fs:
            u=sum(l[uv].uv.x for l in g.loops)/len(g.loops); v=sum(l[uv].uv.y for l in g.loops)/len(g.loops)
            vals.append(a[int((v%1)*h)%h, int((u%1)*w)%w])
        if np.mean(vals)<0.5: kill.extend(fs)
    removed+=len(kill)
    bmesh.ops.delete(bm, geom=kill, context='FACES'); bm.to_mesh(o.data); bm.free()
print("REMOVED", removed)

objs=[o for o in sc.objects if o.type=='MESH']
for o in objs:
    uv=o.data.uv_layers.new(name="Bake")
    o.data.uv_layers.active=uv
    for l in o.data.uv_layers: l.active_render = (l.name!="Bake") and l==o.data.uv_layers[0]
bpy.ops.object.select_all(action='DESELECT')
for o in objs: o.select_set(True)
bpy.context.view_layer.objects.active=objs[0]
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
t=time.time()
bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.003, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
bpy.ops.uv.pack_islands(margin=0.003, rotate=True)
bpy.ops.object.mode_set(mode='OBJECT'); print("UV", time.time()-t)
img=bpy.data.images.new("PadBake", size, size, alpha=False, float_buffer=True)
for m in bpy.data.materials:
    if not m.node_tree: continue
    n=m.node_tree.nodes.new('ShaderNodeTexImage'); n.image=img; n.name="BakeTarget"
    m.node_tree.nodes.active=n
t=time.time()
bpy.ops.object.bake(type='COMBINED', margin=16, use_clear=True)
print("BAKE", time.time()-t)
sc.render.image_settings.file_format='PNG'; img.save_render(out+'.png', scene=sc)
print('VIEW', sc.view_settings.view_transform, sc.view_settings.look, sc.view_settings.exposure)
bpy.ops.wm.save_as_mainfile(filepath=out+".blend")

import bpy, sys, struct, numpy as np, gzip
args=sys.argv[sys.argv.index("--")+1:]
out=args[0]; ratio=float(args[1])
sc=bpy.context.scene
parts=[]
dg=bpy.context.evaluated_depsgraph_get()
total=0
for o in sorted([o for o in sc.objects if o.type=='MESH'], key=lambda o:o.name):
    if o.name=="Body" and ratio<1:
        m=o.modifiers.new("dec",'DECIMATE'); m.ratio=ratio; m.use_collapse_triangulate=True
    dg.update()
    ev=o.evaluated_get(dg); me=ev.to_mesh()
    me.calc_loop_triangles()
    uvl=me.uv_layers["Bake"]
    nl=len(me.loops)
    co=np.empty(len(me.vertices)*3,np.float32); me.vertices.foreach_get("co",co); co=co.reshape(-1,3)
    vi=np.empty(nl,np.int32); me.loops.foreach_get("vertex_index",vi)
    nrm=np.empty(nl*3,np.float32); me.corner_normals.foreach_get("vector",nrm); nrm=nrm.reshape(-1,3)
    uv=np.empty(nl*2,np.float32); uvl.data.foreach_get("uv",uv); uv=uv.reshape(-1,2)
    tri=np.empty(len(me.loop_triangles)*3,np.int32); me.loop_triangles.foreach_get("loops",tri)
    mw=np.array(o.matrix_world)
    pos=co[vi]@mw[:3,:3].T+mw[:3,3]
    key=np.round(np.hstack([pos*1e5,nrm*1e3,uv*1e5])).astype(np.int64)
    uniq,inv=np.unique(key,axis=0,return_inverse=True)
    first=np.zeros(len(uniq),np.int64); first[inv[::-1]]=np.arange(nl)[::-1]
    P=pos[first].astype(np.float32); N=nrm[first].astype(np.float32); U=uv[first].astype(np.float32)
    I=inv.reshape(-1)[tri].astype(np.int32)
    parts.append((o.name,P,N,U,I)); total+=len(I)//3
    print("PART",o.name,"verts",len(P),"tris",len(I)//3)
    ev.to_mesh_clear()
print("TOTAL tris",total)
with gzip.open(out,"wb") as f:
    f.write(b"S7M1"); f.write(struct.pack("<i",len(parts)))
    for name,P,N,U,I in parts:
        nb=name.encode(); f.write(struct.pack("<i",len(nb))); f.write(nb)
        f.write(struct.pack("<i",len(P))); f.write(P.tobytes()); f.write(N.tobytes()); f.write(U.tobytes())
        f.write(struct.pack("<i",len(I))); f.write(I.tobytes())

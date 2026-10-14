import bpy, sys
args=sys.argv[sys.argv.index("--")+1:]
sc=bpy.context.scene
img=bpy.data.images.load(args[1])
mat=bpy.data.materials.new("Baked"); mat.use_nodes=True; nt=mat.node_tree; nt.nodes.clear()
uvn=nt.nodes.new('ShaderNodeUVMap'); uvn.uv_map="Bake"
tex=nt.nodes.new('ShaderNodeTexImage'); tex.image=img; tex.interpolation='Linear'
em=nt.nodes.new('ShaderNodeEmission'); outn=nt.nodes.new('ShaderNodeOutputMaterial')
nt.links.new(uvn.outputs[0],tex.inputs[0]); nt.links.new(tex.outputs[0],em.inputs[0]); nt.links.new(em.outputs[0],outn.inputs[0])
for o in sc.objects:
    if o.type=='MESH':
        o.data.materials.clear(); o.data.materials.append(mat)
for o in sc.objects:
    if o.type=='LIGHT': o.hide_render=True
sc.world.node_tree.nodes['Background'].inputs['Strength'].default_value=0
sc.cycles.samples=8; sc.render.resolution_x=900; sc.render.resolution_y=690
sc.view_settings.view_transform="Standard"; sc.view_settings.exposure=0
sc.render.filepath=args[0]; bpy.ops.render.render(write_still=True)

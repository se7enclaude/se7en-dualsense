import bpy
# Blender +X = côté GAUCHE de la manette (vue de face, caméra en +Y)
ren={"Triangle":"DpadUp","Circle":"DpadLeft","Cross":"DpadDown","Square":"DpadRight",
     "DpadUp":"Triangle","DpadLeft":"Circle","DpadDown":"Cross","DpadRight":"Square",
     "StickL":"StickR","StickR":"StickL","L1":"R1","R1":"L1","L2":"R2","R2":"L2","Create":"Options","Options":"Create"}
objs={o.name:o for o in bpy.context.scene.objects if o.type=='MESH'}
for old in ren: objs[old].name="tmp_"+old
for old,new in ren.items(): bpy.data.objects["tmp_"+old].name=new
print("NAMES",sorted(o.name for o in bpy.context.scene.objects if o.type=='MESH'))
bpy.ops.wm.save_mainfile()

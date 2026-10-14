# Manette 3D temps réel

Génère `src/Se7en.DualSense.App/Assets/Model/` (non versionné) à partir de `render/dualsense_studio.blend`
(modèle « PlayStation 5 DualSense » d'AHarmlessPotato, CC BY 4.0, éclairage studio).

```
blender -b render/dualsense_studio.blend --python 1_separate.py          # pièces mobiles → render/dualsense_parts.blend
blender -b render/dualsense_parts.blend --python 2_bake.py -- 4096 256 <sortie>   # éclairage cuit dans une texture
blender -b <sortie>.blend --python 1b_fix_names.py                         # le modèle est en miroir : noms gauche/droite
blender -b <sortie>.blend --python 2b_lightbar.py                          # barre lumineuse en pièce séparée
blender -b <sortie>.blend --python 3_export.py -- dualsense.s7m 1.0        # maillage binaire (gzip)
```

La texture cuite est ensuite convertie en JPEG 3072 px (`dualsense-baked.jpg`).
Format `.s7m` : `S7M1`, nombre de pièces, puis pour chaque pièce : nom, sommets (positions, normales, UV), indices.
Coordonnées Blender : X à droite, Y vers l'avant (face de la manette), Z vers le haut.

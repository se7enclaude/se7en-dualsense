# Composants tiers

| Composant | Usage | Licence |
|---|---|---|
| [HidSharp](https://www.zer7.com/software/hidsharp) | Lecture HID de la manette | Apache 2.0 |
| [Nefarius.ViGEm.Client](https://github.com/nefarius/ViGEm.NET) | Manettes virtuelles DualShock 4 / Xbox 360 | MIT |
| [NAudio](https://github.com/naudio/NAudio) | Micro de Windows, vibrations générées depuis le son | MIT |
| [Lucide](https://lucide.dev) (`lucide-static` 0.460.0) | Icônes, converties en géométries XAML (`Themes/Icons.xaml`) | ISC |
| Anton, Hanken Grotesk, Geist Mono, Syne | Polices de l'interface | SIL OFL 1.1 — voir `src/Se7en.DualSense.App/Assets/Fonts/OFL.txt` |

## Lucide — licence ISC

```
ISC License

Copyright (c) for portions of Lucide are held by Cole Bemis 2013-2022 as part of Feather (MIT).
All other copyright (c) for Lucide are held by Lucide Contributors 2022.

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
```

## Modèle 3D de la manette

La manette 3D (`Assets/Model`) et l'image de secours (`Assets/Images/*.png`) sont dérivées du modèle
**« Playstation 5 Dualsense »** par **AHarmlessPotato**
(https://sketchfab.com/3d-models/playstation-5-dualsense-878c1f882808477ab81c2fe86d5a3936),
sous licence **CC BY 4.0** (http://creativecommons.org/licenses/by/4.0/).
Modifications : éclairage studio (cuit dans une texture pour la 3D temps réel), découpe en pièces mobiles,
retrait des couches transparentes, cadrage de face, détourage et recadrage pour l'application.

Le modèle et les rendus **ne sont pas inclus dans ce dépôt** (taille, et logos de marque présents sur le modèle).
Voir le README pour les régénérer.

« PlayStation », « DualSense » et « DualShock » sont des marques de Sony Interactive Entertainment.
Ce projet n'est ni affilié ni approuvé par Sony.

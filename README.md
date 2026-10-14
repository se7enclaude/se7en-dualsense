# se7en DualSense

Application Windows pour utiliser une manette PS5 **DualSense** sur PC : lecture USB / Bluetooth,
correction du drift des sticks, et compatibilité avec n'importe quel jeu (émulation DualShock 4 pour
garder les icônes PlayStation, Xbox 360 en option, ou DualSense native).

> **État : phase 1 terminée** (lecture de la manette + interface d'accueil animée).
> L'émulation, l'anti-drift et les profils arrivent dans les phases suivantes — voir [ROADMAP.md](ROADMAP.md).

## Ce que fait la version actuelle
- Détecte la DualSense / DualSense Edge en USB et en Bluetooth, avec reconnexion automatique.
- Affiche en direct boutons, sticks, gâchettes, pavé tactile, batterie et gyroscope.
- Accueil avec une DualSense animée (flottement, inclinaison, boutons qui s'allument, sticks qui bougent).
- Thème clair / sombre (design system « Axis »).

## Prérequis
- Windows 10 / 11 (x64)
- [SDK .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0) ou plus récent pour compiler
- À partir de la phase 2 : [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) et
  [HidHide](https://github.com/nefarius/HidHide/releases) (l'application proposera de les installer)

## Compiler et lancer
```bash
dotnet build Se7enDualSense.sln
dotnet run --project src/Se7en.DualSense.App
```

Outil console (entrées brutes, test des vibrations et de la barre lumineuse) :
```bash
dotnet run --project src/Se7en.DualSense.ConsoleTest
```

Mode démo (manette simulée, pour voir l'interface sans DualSense) — PowerShell :
```powershell
$env:SE7EN_DEMO="1"; dotnet run --project src/Se7en.DualSense.App
```

## Image de la manette (non incluse)
L'accueil affiche une **photo** de la DualSense. C'est une image produit protégée : elle n'est pas versionnée.
Sans elle, l'application fonctionne mais affiche un message à la place de la manette.

Pour la générer à partir d'une photo de face sur fond blanc (Python 3 + `pip install pillow numpy scipy`) :
```bash
python tools/controller-image/cutout.py photo.jpg src/Se7en.DualSense.App/Assets/Images/dualsense.png preview.png mask.png
python tools/controller-image/sticks.py src/Se7en.DualSense.App/Assets/Images/dualsense.png src/Se7en.DualSense.App/Assets/Images
```
Les calques animés (boutons, sticks, pavé) sont positionnés pour une photo officielle vue de face
recadrée en 505 × 386 px ; une autre photo peut demander d'ajuster les coordonnées dans
`Controls/DualSenseView.xaml` et `tools/controller-image/sticks.py`.

## Structure
```
src/Se7en.DualSense.Hid/          Lecture HID (HidSharp) : détection, parsing USB/BT, CRC32, rapports de sortie
src/Se7en.DualSense.ConsoleTest/  Outil console de la phase 1
src/Se7en.DualSense.App/          Interface WPF (thème Axis, accueil animé)
tools/                            Scripts : détourage de la photo, conversion des polices et des icônes
```

## Licences
Code du projet : à définir. Composants tiers (HidSharp, icônes Lucide, polices OFL) : voir
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

« PlayStation », « DualSense » et « DualShock » sont des marques de Sony Interactive Entertainment.
Ce projet n'est ni affilié ni approuvé par Sony.

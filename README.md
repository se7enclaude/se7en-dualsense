# se7en DualSense

Application Windows pour utiliser une manette PS5 **DualSense** sur PC : lecture USB / Bluetooth,
correction du drift des sticks, et compatibilité avec n'importe quel jeu (émulation DualShock 4 pour
garder les icônes PlayStation, Xbox 360 en option, ou DualSense native).

> **État** : lecture de la manette, émulation DualShock 4 / Xbox 360 / DualSense native, masquage HidHide,
> anti-drift (calibration, zones mortes, courbes), remapping, gyroscope, profils par jeu, interface complète.
> Détail et suite : [ROADMAP.md](ROADMAP.md).

### Droits administrateur
HidHide (masquage de la vraie manette) exige les droits administrateur : au lancement, l'application se relance
une fois en administrateur (invite Windows). En cas de refus, tout fonctionne sauf le masquage.
Si la manette restait invisible après un arrêt brutal : `se7en DualSense.exe --unhide-all`.

## Fonctionnalités
- **Jouer** : DualShock 4 virtuelle (icônes PlayStation), Xbox 360 ou DualSense native, en un clic ; la vraie manette est masquée (HidHide).
- **Anti-drift** : calibration guidée du centre et de l'amplitude, zones mortes radiales, anti-zone morte, courbes, détection automatique du drift.
- **Gâchettes** : zones mortes, tout-ou-rien, gâchettes adaptatives (résistance, point dur, vibration).
- **Mappage** : réattribution de chaque bouton, turbo. **Gyroscope** : visée par inclinaison.
- **Profils par jeu** : bibliothèque Steam / Epic / Rockstar détectée, bascule automatique au lancement d'un jeu, import / export.
- **Retours** : vibrations du jeu (intensité réglable), barre lumineuse (jeu, personnalisée, batterie), LED joueur.
- **Confort** : icône près de l'horloge, démarrage avec Windows, overlay en jeu, navigation à la manette, thème clair / sombre.
- **Diagnostic** : circularité des sticks, course des gâchettes, fréquence, rapport brut.

Raccourcis : Ctrl + 1…9 pour les onglets, Échap pour fermer une fenêtre. Options : `--skip-intro`, `--minimized`, `--page=sticks`.

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

Mode démo (manette simulée, pour voir l'interface sans DualSense ; un badge « Mode démo » s'affiche) :
```bash
dotnet run --project src/Se7en.DualSense.App -- --demo
```
Autres options : `--skip-intro` (ouvre directement le tableau de bord), `--demo-all` (toutes les touches simulées enfoncées).

## Image de la manette (non incluse)
L'accueil affiche un **rendu 3D** de la DualSense, réalisé dans Blender à partir du modèle
« Playstation 5 Dualsense » de AHarmlessPotato (Sketchfab, CC BY 4.0). Le modèle et les rendus ne sont
pas versionnés ; sans eux, l'application fonctionne mais affiche un message à la place de la manette.

Pour les régénérer :
1. Télécharger le modèle (format GLB) depuis Sketchfab et l'ouvrir dans Blender, caméra de face
   (135 mm), fond transparent, rendu Cycles en 2400 × 1835 px.
2. Préparer les images pour l'application (Python 3 + `pip install pillow numpy`) :
```bash
python tools/controller-image/prepare_render.py rendu.png src/Se7en.DualSense.App/Assets/Images
```
Les calques animés (boutons, sticks, pavé) sont positionnés dans un repère 505 × 386 ; un autre
cadrage demande d'ajuster les coordonnées dans `Controls/DualSenseView.xaml` et `prepare_render.py`.

## Structure
```
src/Se7en.DualSense.Hid/          Lecture HID (HidSharp) : détection, parsing USB/BT, CRC32, rapports de sortie
src/Se7en.DualSense.ConsoleTest/  Outil console de la phase 1
src/Se7en.DualSense.Processing/   Traitement : calibration, zones mortes, courbes, gâchettes, remapping, gyroscope
src/Se7en.DualSense.Output/       Sortie : DS4 / Xbox 360 virtuelles (ViGEm), HidHide, pilotes, profils, bibliothèque
src/Se7en.DualSense.App/          Interface WPF (thème Axis, accueil animé)
tests/Se7en.DualSense.Tests/      Tests unitaires (dotnet test)
tools/                            Scripts : détourage de la photo, conversion des polices et des icônes
```

## Licences
Code du projet : à définir. Composants tiers (HidSharp, icônes Lucide, polices OFL) : voir
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

« PlayStation », « DualSense » et « DualShock » sont des marques de Sony Interactive Entertainment.
Ce projet n'est ni affilié ni approuvé par Sony.

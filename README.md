# se7en DualSense

Application Windows pour utiliser une manette PS5 **DualSense** sur PC : lecture USB / Bluetooth,
correction du drift des sticks, et compatibilité avec n'importe quel jeu (émulation DualShock 4 pour
garder les icônes PlayStation, Xbox 360 en option, ou DualSense native).

> **État** (v0.7) : émulation DualShock 4 / Xbox 360 / DualSense native / clavier-souris, masquage HidHide,
> anti-drift, mappage complet (clavier, souris, macros, couche secondaire, pavé tactile), plusieurs manettes,
> manette 3D temps réel, exécutable autonome avec mises à jour. Détail et suite : [ROADMAP.md](ROADMAP.md).

### Droits administrateur
HidHide (masquage de la vraie manette) exige les droits administrateur : au lancement, l'application se relance
une fois en administrateur (invite Windows). En cas de refus, tout fonctionne sauf le masquage.
Si la manette restait invisible après un arrêt brutal : `se7en DualSense.exe --unhide-all`.

## Fonctionnalités
- **Jouer** : DualShock 4 virtuelle (icônes PlayStation), Xbox 360, DualSense native ou **clavier / souris**, en un clic ; la vraie manette est masquée (HidHide).
- **Plusieurs manettes** en même temps (jusqu'à 4), chacune avec sa manette virtuelle et sa LED joueur.
- **Anti-drift** : calibration guidée du centre et de l'amplitude, zones mortes radiales, anti-zone morte, courbes, détection automatique du drift, **historique de l'usure** jour après jour.
- **Gâchettes** : zones mortes, tout-ou-rien, gâchettes adaptatives (résistance, point dur, vibration).
- **Mappage** : chaque bouton → autre bouton, **touche du clavier**, **clic / molette**, **macro** ou **action système** ; **couche secondaire** (un bouton maintenu change tout), turbo, **pavé tactile** en zones cliquables, gestes de glissement ou souris, **sticks en souris / molette / ZQSD**.
- **Macros** enregistrées en jouant la séquence sur la manette (une fois, en boucle ou en bascule).
- **Raccourcis PS + bouton** : volume, média, capture d'écran, profil suivant, mode bureau, extinction… (un appui court sur PS reste transmis au jeu).
- **Bouton micro** relié au micro de Windows (LED orange quand il est coupé).
- **Gyroscope** : visée par inclinaison, vers le stick droit ou **directement la souris** ; **flick stick** pour les jeux de tir PC.
- **Anti-drift automatique** : le centre des sticks se recalibre tout seul quand la manette est posée.
- **Profils par jeu** : bibliothèque Steam / Epic / Rockstar détectée, bascule automatique au lancement d'un jeu, import / export, profil « Bureau Windows ».
- **Retours** : vibrations du jeu (intensité réglable), **vibrations générées depuis le son**, **haptique HD** de la DualSense (USB), **gâchettes réactives au son**, barre lumineuse (jeu, personnalisée, batterie, **arc-en-ciel, respiration, musique, couleurs de l'écran**), LED joueur.
- **Son de la manette** (USB) : copie du son du PC dans son haut-parleur / casque, manette ou micro de la manette comme périphérique Windows.
- **Batterie** : alerte à 20 % puis 10 %, **autonomie restante estimée**, historique sur 24 h, **extinction en Bluetooth** (PS + Options), **association Bluetooth** depuis l'application.
- **Lanceur de jeux plein écran** et **clavier virtuel**, pilotés à la manette (PS + R3, PS + L3).
- **Un profil par manette** quand plusieurs sont branchées · détection des conflits avec **Steam Input**.
- **Effets sonores** de l'interface (synthétisés, réglables).
- **Interface** : manette **3D temps réel** (suit les mouvements de la vraie manette, chaque commande bouge, barre lumineuse en direct), couleurs d'accent, thème clair / sombre, animations.
- **Confort** : icône près de l'horloge, démarrage avec Windows, overlay en jeu, navigation à la manette, installation en un clic, **mises à jour automatiques** (publications GitHub).
- **Diagnostic** : circularité des sticks, course des gâchettes, fréquence, historique du drift, rapport brut.

Raccourcis : Ctrl + K pour la recherche rapide (onglets, réglages, profils, actions), Ctrl + 1…9 pour les onglets, Échap pour fermer une fenêtre. Options : `--skip-intro`, `--minimized`, `--page=sticks`.

## Prérequis
- Windows 10 / 11 (x64)
- [SDK .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0) ou plus récent pour compiler
- À partir de la phase 2 : [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) et
  [HidHide](https://github.com/nefarius/HidHide/releases) (l'application proposera de les installer)

## Exécutable autonome
```bash
powershell -ExecutionPolicy Bypass -File tools/publish.ps1
```
Produit `publish/se7en DualSense.exe` : un seul fichier, sans .NET à installer. Au premier lancement, l'assistant
propose de l'installer (dossier Programmes de l'utilisateur, raccourcis Bureau et menu Démarrer).

Mises à jour : l'application interroge les publications GitHub du dépôt (au plus une fois par jour) et propose
d'installer un `.exe` plus récent. Le script affiche l'empreinte SHA-256 à indiquer dans les notes de la publication
(« SHA-256 : … ») pour que l'application la vérifie avant de remplacer l'exécutable.

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

## Manette 3D (non incluse)
L'accueil affiche la DualSense **en 3D temps réel**, à partir du modèle « Playstation 5 Dualsense » de
AHarmlessPotato (Sketchfab, CC BY 4.0) : pièces mobiles séparées et éclairage studio cuit dans une texture
(`tools/controller-3d`, Blender 4+). Le résultat (`Assets/Model`) n'est pas versionné ; sans lui, l'accueil
utilise le rendu 2D ci-dessous, et sans images non plus, un message remplace la manette.

### Rendu 2D (secours)

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
tools/                            Scripts : publication, manette 3D (Blender), détourage, polices, icônes
```

## Licences
Code du projet : à définir. Composants tiers (HidSharp, NAudio, ViGEm, icônes Lucide, polices OFL) : voir
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

« PlayStation », « DualSense » et « DualShock » sont des marques de Sony Interactive Entertainment.
Ce projet n'est ni affilié ni approuvé par Sony.

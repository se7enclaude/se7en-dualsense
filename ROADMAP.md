# se7en DualSense — feuille de route

Légende : ✅ fait · 🧪 fait, à tester en jeu · ⬜ à faire

---

## Phase 1 — Lecture de la manette ✅
- ✅ Détection USB / Bluetooth, reconnexion automatique
- ✅ Parsing des rapports 0x01 (USB), 0x31 (BT, CRC32), rapport BT réduit
- ✅ Rapports de sortie (vibrations, barre lumineuse, LED, gâchettes adaptatives) avec CRC32 en BT

## Phase 2 — Sortie virtuelle et modes de jeu 🧪
- ✅ Vérification de ViGEmBus / HidHide + téléchargement (signature vérifiée) et installation guidées
- ✅ DualShock 4 virtuelle : pavé tactile, gyroscope, bouton PS — vérifiée sur la vraie manette
- ✅ Xbox 360 virtuelle · ✅ DualSense native (lecture seule, « anti-drift inactif »)
- 🧪 Mode clavier / souris (aucune manette pour les jeux, la DualSense pilote clavier et souris)
- ✅ Masquage HidHide sûr (journal, démasquage à la fermeture / au plantage, `--unhide-all`)
- ✅ Bascule de mode en un clic, vibrations et barre lumineuse du jeu renvoyées vers la DualSense
- 🧪 Plusieurs manettes en même temps (jusqu'à 4, LED joueur 1 à 4, même manette en USB + BT ignorée)

## Phase 3 — Sticks, gâchettes, anti-drift ✅
- ✅ Calibration du centre et de l'amplitude (assistant guidé), mémorisée par manette (adresse Bluetooth)
- ✅ Zone morte radiale interne + externe, anti-zone morte
- ✅ Courbes : linéaire, douce, agressive, exposant personnalisé (graphe en direct)
- ✅ Sensibilité X / Y, inversion d'axes, échange des sticks
- ✅ Zones mortes L2 / R2 + tout-ou-rien
- ✅ Détection automatique du drift (alerte sur l'accueil + pastille sur l'onglet)
- ✅ Historique de l'usure (décalage brut au repos, un point par jour, graphique dans Diagnostic)
- ✅ Tests unitaires (56 tests)

## Phase 4 — Interface ✅
- ✅ 9 onglets en 3 sections · notifications · raccourcis Ctrl + 1…9 · assistant de premier lancement
- ✅ Navigation dans l'application à la manette · barre latérale compacte
- ✅ Manette 3D temps réel : suit l'accéléromètre et le gyroscope, sticks / gâchettes / boutons qui bougent, barre lumineuse et LED micro en direct, rotation à la souris
- ✅ Couleurs d'accent (7 couleurs + « barre lumineuse » qui suit la manette)
- ✅ Animations : touches qui s'enfoncent, traînée des sticks, pages en cascade

## Phase 5 — Mappage, profils, retours
- ✅ Remapping de n'importe quel bouton, turbo (cadence réglable)
- ✅ Touches du clavier (combinaisons), clics et molette, actions système, sur n'importe quel bouton
- ✅ Couche secondaire (bouton maintenu)
- ✅ Macros enregistrées depuis la manette (une fois / en boucle / bascule)
- ✅ Pavé tactile : zones cliquables (2 ou 4), gestes de glissement, mode souris
- ✅ Sticks en souris, molette ou touches de déplacement · profil « Bureau Windows » (PS + □)
- 🧪 Raccourcis système PS + bouton (volume, média, capture, profils, overlay, extinction)
- 🧪 Bouton micro relié au micro de Windows (LED orange quand il est coupé)
- 🧪 Vibrations générées depuis le son de Windows (basses / médiums)
- ✅ Profils JSON par jeu, import / export, duplication, bascule auto au lancement d'un jeu
- ✅ Visée au gyroscope · gâchettes adaptatives · barre lumineuse · LED joueur
- 🧪 DualSense Edge : palettes arrière et boutons Fn affectables (lignes affichées quand une Edge est branchée)

## Phase 6 — Confort et publication
- ✅ Icône dans la zone de notification (manettes, mode, profil, mode bureau, extinction)
- ✅ Démarrage avec Windows · overlay en jeu · journal des erreurs
- 🧪 Alerte batterie faible (20 % puis 10 %, bulle Windows si la fenêtre est fermée)
- 🧪 Extinction de la manette en Bluetooth
- ✅ Exécutable autonome en un seul fichier (`tools/publish.ps1`) · installation en un clic, raccourcis, désinstallation
- 🧪 Mises à jour automatiques via les publications GitHub (empreinte SHA-256 vérifiée)
- ✅ Première publication GitHub : v0.5.0 (se7en-DualSense.exe)

---

## Hors de portée (limites techniques)
- Haptique HD et gâchettes adaptatives **envoyées par le jeu** en mode DS4 / Xbox : le jeu ne les produit que pour une vraie DualSense → mode natif.
- DualSense virtuelle : nécessiterait un pilote signé par Microsoft.
- Certains anti-triche bloquent les manettes virtuelles ou les entrées clavier / souris simulées.

## Version 0.6
- ✅ Gyroscope en souris · flick stick (points par tour, durée, test d'un tour)
- ✅ Anti-drift automatique continu (manette posée, gyroscope immobile, sticks lâchés)
- 🧪 Haptique HD depuis le son (USB : canaux 3-4 de la carte son de la manette) + vibrations du jeu synthétisées
- 🧪 Son vers la manette : copie du son du PC, manette / micro de la manette comme périphériques Windows
- 🧪 Gâchettes réactives au son (sons forts pendant que L2 / R2 sont pressées)
- ✅ Barre lumineuse : arc-en-ciel, respiration, musique, couleurs de l'écran (ambilight)
- ✅ Lanceur de jeux plein écran à la manette (Steam, Epic, Rockstar) · clavier virtuel AZERTY à la manette
- 🧪 Association Bluetooth depuis l'application
- ✅ Profil par manette · autonomie restante estimée et historique de batterie · détection des conflits Steam Input
- ✅ Effets sonores de l'interface (clics, bascules, carillons, notifications, intro)
- ✅ 62 tests unitaires
- ✖ Non retenu : anti-recul (triche en ligne), interface en anglais, signature numérique

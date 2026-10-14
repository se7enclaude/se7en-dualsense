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
- ✅ Masquage HidHide sûr (journal, démasquage à la fermeture / au plantage, `--unhide-all`)
- ✅ Bascule de mode en un clic, vibrations et barre lumineuse du jeu renvoyées vers la DualSense
- ⬜ Plusieurs manettes en même temps

## Phase 3 — Sticks, gâchettes, anti-drift ✅
- ✅ Calibration du centre et de l'amplitude (assistant guidé), mémorisée par manette (numéro de série)
- ✅ Zone morte radiale interne + externe, anti-zone morte
- ✅ Courbes : linéaire, douce, agressive, exposant personnalisé (graphe en direct)
- ✅ Sensibilité X / Y, inversion d'axes, échange des sticks
- ✅ Zones mortes L2 / R2 + tout-ou-rien
- ✅ Détection automatique du drift (alerte sur l'accueil + pastille sur l'onglet)
- ✅ Tests unitaires de la chaîne de traitement (44 tests au total)

## Phase 4 — Interface complète ✅
- ✅ 9 onglets en 3 sections : Accueil, Jeux & profils · Sticks, Gâchettes, Mappage, Gyroscope, Vibrations & lumière · Diagnostic, Réglages
- ✅ Testeur : circularité des sticks, course des gâchettes, fréquence, rapport brut, infos matérielles
- ✅ Notifications, transitions de page, raccourcis Ctrl + 1…9, Échap
- ✅ Assistant de premier lancement (pilotes → calibration → mode)
- ✅ Navigation dans l'application à la manette (croix, ✕, ○, L1 / R1)
- ✅ Barre latérale compacte sur les petites fenêtres
- ⬜ Historique du drift (graphique dans le temps)

## Phase 5 — Mappage, profils, retours
- ✅ Remapping de n'importe quel bouton, turbo (cadence réglable)
- ✅ Profils JSON par jeu (mode + réglages), import / export, duplication
- ✅ Bibliothèque de jeux automatique (Steam, Epic, Rockstar) + bascule auto au lancement d'un jeu
- ✅ Visée au gyroscope (toujours / en visant / avec R1), sensibilité, zone morte
- ✅ Vibrations : intensité 0-200 %, tests des moteurs
- ✅ Gâchettes adaptatives en modes DS4 / Xbox (résistance, point dur, vibration) + préréglages
- ✅ Barre lumineuse : couleur du jeu, personnalisée, batterie, éteinte ; luminosité ; LED joueur
- ⬜ Couche secondaire, macros, pavé tactile en zones et gestes
- ⬜ Mode clavier / souris, pavé tactile en souris
- ⬜ Raccourcis système (PS + bouton), bouton micro relié au micro Windows
- ⬜ Vibrations générées depuis le son du jeu
- ⬜ Extinction de la manette en Bluetooth, alerte batterie faible
- ⬜ DualSense Edge : palettes arrière, boutons Fn

## Phase 6 — Confort et publication
- ✅ Icône dans la zone de notification (mode, profil, batterie), fermeture en arrière-plan
- ✅ Démarrage avec Windows (tâche planifiée, droits administrateur, démarrage réduit)
- ✅ Overlay en jeu (batterie, profil, mode)
- ✅ Journal des erreurs (`error.log`)
- ⬜ Mises à jour automatiques (via GitHub Releases)
- ⬜ Publication en un seul .exe autonome (win-x64)

---

## Hors de portée (limites techniques)
- Haptique HD et gâchettes adaptatives **envoyées par le jeu** en mode DS4 / Xbox : le jeu ne les produit que pour une vraie DualSense → mode natif.
- DualSense virtuelle : nécessiterait un pilote signé par Microsoft.
- Certains anti-triche bloquent les manettes virtuelles.

# se7en DualSense — feuille de route

Chaque phase se termine par un test de ta part avant de passer à la suivante.
Légende : ✅ fait · 🔜 phase en cours · ⬜ à faire

---

## Phase 1 — Lecture de la manette ✅ (à valider sur la vraie manette)
- ✅ Détection USB / Bluetooth, reconnexion automatique
- ✅ Parsing des rapports 0x01 (USB), 0x31 (BT, CRC32), rapport BT réduit
- ✅ Rapports de sortie (vibrations, barre lumineuse, LED) avec CRC32 en BT
- ✅ Outil console de test
- ✅ Interface Axis : accueil avec la DualSense (photo) animée en direct, thème clair/sombre, mode démo

## Phase 2 — Sortie virtuelle et modes de jeu
- ⬜ Vérification de ViGEmBus / HidHide au lancement + téléchargement et installation automatiques
- ⬜ DualShock 4 virtuelle (icônes PlayStation), pavé tactile et bouton PS compris
- ⬜ Xbox 360 virtuelle
- ⬜ DualSense native (passthrough, lecture seule) avec mention « anti-drift inactif »
- ⬜ Masquage HidHide sûr (liste blanche, démasquage garanti à la fermeture ou au plantage)
- ⬜ Bascule de mode en un clic, sans redémarrage (les 3 points de l'accueil = DS4 / Natif / Xbox)
- ⬜ Détection de Steam Input (avertissement double entrée)
- ⬜ **Plusieurs manettes** en même temps (une manette virtuelle par DualSense) — à prévoir dès cette phase car cela structure le moteur

## Phase 3 — Sticks, gâchettes, anti-drift
- ⬜ Calibration du centre (manette posée) et de l'amplitude (rotation des sticks)
- ⬜ Zone morte radiale interne + zone morte externe, par stick
- ⬜ **Anti-deadzone** (compense la zone morte du jeu)
- ⬜ Courbes de réponse : linéaire, douce, agressive + **courbe personnalisée à points**
- ⬜ **Sensibilité séparée X / Y**, **inversion d'axes**, **échange des sticks** (gaucher)
- ⬜ Zones mortes L2 / R2 + **gâchette en tout-ou-rien** (hair trigger)
- ⬜ **Détection automatique du drift** au repos, avec proposition de calibration
- ⬜ **Calibration mémorisée par manette** (numéro de série)
- ⬜ Tests unitaires de toute la chaîne de traitement

## Phase 4 — Interface complète
- ⬜ Page Calibration (assistant guidé, sticks bruts / corrigés côte à côte, zones mortes dessinées sur la grande manette)
- ⬜ **Testeur de manette** : tous les boutons, graphique de circularité des sticks, course des gâchettes
- ⬜ **Historique du drift** (graphique dans le temps)
- ⬜ **Infos matérielles** : firmware, numéro de série, adresse Bluetooth
- ⬜ Messages d'erreur clairs en français (manette retirée, pilotes absents, changement de mode)
- ⬜ Retouche du détourage (taches sous les poignées, liseré en thème sombre)

## Phase 5 — Mappage, profils, retours
- ⬜ Remapping de n'importe quel bouton
- ⬜ **Couche secondaire** (bouton maintenu = deuxième jeu de commandes)
- ⬜ **Turbo** et **macros**
- ⬜ **Pavé tactile en zones** (2 ou 4 boutons) et **gestes** (balayages)
- ⬜ **Pavé tactile en souris** sur le bureau
- ⬜ **Mode clavier / souris** pour les jeux sans support manette
- ⬜ **Visée au gyroscope** (vers stick droit ou souris), activable seulement en visant (L2)
- ⬜ **Raccourcis système** (PS + bouton : volume, capture d'écran, Game Bar)
- ⬜ **Bouton micro** : coupe le micro Windows, LED orange synchronisée
- ⬜ Profils JSON par jeu (avec mode de sortie), import / export, **profils partageables**
- ⬜ **Bibliothèque de jeux automatique** (Steam, Epic, GOG) + bascule auto au lancement
- ⬜ **Notification au changement de profil**
- ⬜ Vibrations du jeu → DualSense, **intensité réglable 0-200 %**
- ⬜ **Vibrations générées depuis le son du jeu** (basses)
- ⬜ **Gâchettes adaptatives en mode DS4 / Xbox** (résistance réglée par l'application)
- ⬜ Barre lumineuse : personnalisée, pilotée par le jeu, ou **indicateur de batterie** ; **luminosité** et **LED joueur** réglables
- ⬜ **Alerte batterie faible** (notification + barre rouge)
- ⬜ **Extinction de la manette en Bluetooth** (bouton + automatique après inactivité)
- ⬜ **DualSense Edge** : palettes arrière, boutons Fn

## Phase 6 — Confort et publication
- ⬜ Icône dans la barre des tâches, fonctionnement en arrière-plan
- ⬜ Démarrage avec Windows (tâche planifiée, droits administrateur)
- ⬜ Bouton activer / désactiver l'émulation
- ⬜ **Overlay en jeu** (batterie, profil actif)
- ⬜ **Indicateur de latence** et fréquence de la manette
- ⬜ **Mises à jour automatiques** (nécessite un dépôt de publication, ex. GitHub Releases)
- ⬜ Publication en un seul .exe autonome (win-x64), README complet

---

## Hors de portée (limites techniques)
- Haptique HD et gâchettes adaptatives **envoyées par le jeu** en mode DS4 / Xbox : le jeu ne les produit que pour une vraie DualSense → utiliser le mode natif.
- DualSense virtuelle : nécessiterait un pilote signé par Microsoft.

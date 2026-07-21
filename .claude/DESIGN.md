# DESIGN.md — Muralis

Règles de design de l'UI. Ces règles sont **normatives** : toute nouvelle fenêtre, page ou contrôle doit les respecter. En cas de doute sur un pattern, consulter les références Microsoft Learn listées en bas via le MCP Microsoft Learn **avant** d'improviser.

Référence visuelle cible : **l'app Paramètres de Windows 11** (Système > Affichage est le meilleur exemple). Si un écran de Muralis ne pourrait pas se fondre dans les Paramètres Windows 11, il n'est pas conforme.

## Principes non négociables

1. **Tout réglage vit dans une card.** Jamais de label + contrôle posés à nu sur le fond de la fenêtre. Utiliser `ui:CardControl` (réglage simple sur une ligne) ou `ui:CardExpander` (réglage avec sous-options dépliables).
2. **Pas de headers de section en MAJUSCULES.** Les groupes sont séparés par un `TextBlock` style `BodyStrongTextBlockStyle` en casse normale ("Source", "Affichage"), avec l'espacement défini ci-dessous — ou par simple espacement sans titre quand le groupe est évident.
3. **Navigation : `ui:NavigationView` en mode `LeftFluent` ou pane gauche compact** — pas de hamburger + flyout pour 3 pages. Icônes Segoe Fluent Icons pour chaque entrée (Fonds d'écran / Sources web / Paramètres).
4. **Jamais de tailles de police ou couleurs en dur.** Uniquement les styles typographiques WPF-UI et les ressources de thème (`{DynamicResource TextFillColorPrimaryBrush}` etc.) — sinon le switch thème clair/sombre casse.

## Structure d'une card de réglage

Anatomie `ui:CardControl` :
- **Icône** à gauche (`ui:SymbolIcon`, Segoe Fluent Icons, 20px)
- **Titre** (`BodyTextBlockStyle`) + **description optionnelle** (`CaptionTextBlockStyle`, couleur `TextFillColorSecondaryBrush`) empilés
- **Contrôle** aligné à droite (ComboBox, ToggleSwitch, NumberBox...)

Règles associées :
- Toggle on/off → `ui:ToggleSwitch`, jamais de `CheckBox` pour un réglage (la checkbox est réservée aux listes à sélection multiple)
- Choix parmi ≤ 5 options visuellement comparables (ex. mode d'affichage) → envisager des boutons segmentés ou ComboBox ; > 5 → ComboBox
- Valeur numérique avec unité (intervalle) → `ui:NumberBox` + ComboBox d'unité dans la même card
- Un réglage qui en révèle d'autres (ex. "Diaporama" activé → source, intervalle, ordre aléatoire) → `ui:CardExpander` avec le toggle dans le header et les sous-réglages dedans

## Espacements (échelle 4px, stricte)

| Contexte | Valeur |
|---|---|
| Marges de page (contenu vs bords fenêtre) | 36px horizontal, 24px vertical |
| Entre cards d'un même groupe | 4px |
| Entre groupes (ou avant un titre de groupe) | 24px |
| Titre de groupe → première card | 8px |
| Padding interne des cards | géré par WPF-UI, ne pas surcharger |
| Largeur max du contenu | 1000px, centré si la fenêtre est plus large |

## Typographie (styles WPF-UI uniquement)

| Usage | Style |
|---|---|
| Titre de page ("Fonds d'écran") | `TitleTextBlockStyle` |
| Titre de groupe | `BodyStrongTextBlockStyle` |
| Titre de réglage dans une card | `BodyTextBlockStyle` |
| Description/aide sous un titre | `CaptionTextBlockStyle` + `TextFillColorSecondaryBrush` |
| Jamais | tailles en dur, gras arbitraire, italique décoratif |

## Patterns spécifiques Muralis

### Sélecteur d'écrans (page Fonds d'écran)
Reproduire le pattern de Paramètres > Système > Affichage : des **rectangles cliquables proportionnels à la géométrie réelle des moniteurs** (un écran portrait 1080×1920 est dessiné vertical), numérotés, avec état sélectionné via `AccentFillColorDefaultBrush`. Pas d'onglets texte. La résolution s'affiche sous le sélecteur ou dans un tooltip, pas dans l'onglet.

### Aperçu du fond actuel
Chaque écran sélectionné montre une miniature du wallpaper actuellement appliqué dans le rectangle du sélecteur (comme Windows le fait) — feedback immédiat du résultat.

### Grille de miniatures (dossier local)
`GridView`/`ItemsControl` en grille avec coins arrondis 4px, espacement 8px, ratio préservé. Hauteur max ~2 rangées + scroll.

### Page Sources web
- Sources ajoutées : une card par source (icône globe, nom en titre, URL en description `CaptionTextBlockStyle` **tronquée avec ellipsis**, bouton Retirer à droite dans la card)
- "Ajouter depuis le catalogue" : card avec ComboBox + bouton
- "Source personnalisée" : `ui:CardExpander` replié par défaut — le formulaire 5 champs ne doit pas être étalé en permanence
- Champ "Clé API" → `ui:PasswordBox`, pas un TextBox en clair

### Boutons
- Un seul bouton accent (`Appearance="Primary"`) par page : "Appliquer"
- Actions secondaires (Retirer, Parcourir, Ajouter) : boutons standard
- Bouton Appliquer : ancré en bas dans une barre avec séparateur supérieur (`DividerStrokeColorDefaultBrush`), pas flottant

### Fenêtre
- `ui:FluentWindow` avec backdrop **Mica**, `ExtendsContentIntoTitleBar="True"` + `ui:TitleBar`
- Taille par défaut 1100×700, minimum 900×600

## Workflow de vérification (pour Claude Code)

Après toute modification visuelle significative :
1. Builder et lancer l'app, demander à l'utilisateur une capture d'écran si le rendu ne peut pas être vérifié autrement
2. Comparer contre ce fichier point par point (cards ? espacements 4/24 ? styles typo ? icônes ?)
3. En cas de doute sur un pattern, interroger le MCP Microsoft Learn (voir ci-dessous) plutôt que d'inventer

## Références Microsoft Learn (à consulter via le MCP mslearn)

Utiliser le MCP Microsoft Learn pour récupérer ces articles quand un pattern est incertain :

- **Design basics / structure d'une page de settings** : `learn.microsoft.com/windows/apps/design/basics/` — hiérarchie de page, quand grouper
- **Spacing & layout** : `learn.microsoft.com/windows/apps/design/layout/alignment-margin-padding` — l'échelle 4px et les conventions de marges
- **Typographie Windows 11** : `learn.microsoft.com/windows/apps/design/style/typography` — le ramp complet (Caption→Display) que les styles WPF-UI répliquent
- **Couleur et thèmes** : `learn.microsoft.com/windows/apps/design/style/color` — usage de l'accent, brushes de texte primaire/secondaire
- **Segoe Fluent Icons (liste des glyphes)** : `learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font`
- **Contrôles : quand utiliser quoi** : `learn.microsoft.com/windows/apps/design/controls/` — index des contrôles avec leurs cas d'usage (toggle vs checkbox, combo vs radio...)

Compléments hors Learn (web) :
- **fluent2.microsoft.design** — le design system Fluent 2 complet
- **Doc WPF-UI** : `wpfui.lepo.co` — API exacte de `CardControl`, `CardExpander`, `NavigationView`, `FluentWindow`
- **App WinUI 3 Gallery** (Microsoft Store, sur la machine de dev) — rendu de référence interactif de chaque pattern

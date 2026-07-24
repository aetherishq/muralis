# plan-signpath.md — Signature de code & distribution (suppression de l'avertissement SmartScreen)

Plan d'implémentation pour Claude Code. Objectif : les binaires de release sont signés via **SignPath Foundation** (gratuit pour l'OSS), l'avertissement « Windows protected your PC » disparaît à terme, coût total 0 €.

Contexte acté (ne pas rediscuter) :
- Repo public : `https://github.com/aetherishq/muralis`, licence **GPL-3.0** (validée éligible SignPath).
- MFA GitHub activée. `CODE_SIGNING_POLICY.md` + sections README (« Code signing », « Acknowledgements ») déjà commitées sur `main`.
- Rôles SignPath (Author/Reviewer/Approver) : **@Arkatul** (compte perso — les rôles désignent des personnes, pas l'org).
- **L'EV ne bypass plus SmartScreen (2024)** et **winget ne contourne pas SmartScreen** : la signature est le seul levier, la réputation se construit ensuite avec les téléchargements et se transfère entre versions.

Ordre : Étape A → B → C → D. Les étapes B et D demandent des actions utilisateur (marquées **👤 UTILISATEUR**) — s'arrêter et les demander explicitement quand on y arrive.

> **État au 2026-07-24** : A ✅ · B 🕒 candidature soumise, en attente d'acceptation · C ✅ · D/E/F ⬜ (bloquées par B).

---

## Étape A — Workflow de release (build vérifiable exigé par SignPath) · ✅ faite

Créer `.github/workflows/release.yml` :

- Déclencheurs : `push` sur tags `v*` **et** `workflow_dispatch` (test manuel sans tag — le tag `v1.2.1` existant ne peut pas resservir).
- `permissions: contents: write`.
- Job unique `build` sur `windows-latest` :
  1. `actions/checkout@v4`.
  2. `actions/setup-dotnet@v4`, `dotnet-version: "10.0.x"`.
  3. Installer Inno Setup : `choco install innosetup -y --no-progress`.
  4. **Garde-fou version** (seulement si le run vient d'un tag) : extraire `MyAppVersion` de `installer\Muralis.iss` et échouer si ≠ tag sans le `v`. Message d'erreur explicite (« bumpe la version, recommite, retague »).
  5. Build : `.\installer\build.ps1` (le script existant fait publish + ISCC → `dist\Muralis-Setup-<version>.exe`). Ne pas dupliquer sa logique dans le YAML.
  6. `actions/upload-artifact@v4` : name `Muralis-Setup`, path `dist\*.exe`, `if-no-files-found: error`. **Donner un `id:` à cette step** (ex. `upload`) — l'étape D en aura besoin.
  7. Release GitHub **draft** (`softprops/action-gh-release@v2`, `draft: true`, `generate_release_notes: true`, files `dist\*.exe`) — seulement si run sur tag.

Vérification : lancer un run via `workflow_dispatch` (gh CLI ou UI) et confirmer que l'artefact contient bien l'installeur. Corriger jusqu'au vert.

Commit : `ci: add release workflow (build installer on tag)`.

## Étape B — Candidature SignPath Foundation · 🕒 soumise, en attente

**👤 UTILISATEUR (bloquant)** : soumettre la candidature sur `https://signpath.org` (bouton Apply) avec : URL du repo, licence GPL-3.0, lien vers `CODE_SIGNING_POLICY.md`, description de l'app. Créer le compte SignPath avec MFA. Attendre l'acceptation (peut prendre plusieurs jours).

Claude Code : préparer un bloc de texte prêt-à-coller (description courte EN de Muralis + liens) pour le formulaire, puis s'arrêter.

**Fait le 2026-07-24** : compte créé, candidature soumise. Prochaine action à l'acceptation → étape D.

## Étape C — Pendant l'attente (optionnel, non bloquant) · ✅ faite

- Vérifier que la mention DPAPI de `CODE_SIGNING_POLICY.md` correspond au code réel (clés API chiffrées `ProtectedData`, scope CurrentUser). Corriger le doc ou le code si écart.
- Préparer l'étape winget (ne PAS soumettre) : générer un manifeste `wingetcreate` de la dernière release comme brouillon dans `tools/winget/`. Rappel : winget n'enlève pas SmartScreen, c'est un canal de distribution en plus, à activer après la première release signée.

## Étape D — Intégration de la signature dans la CI (après acceptation SignPath)

**👤 UTILISATEUR d'abord** : dans le dashboard SignPath, créer le projet + signing policy (release-signing) ; installer la GitHub App SignPath sur le repo ou générer un **API token** ; fournir : `ORGANIZATION_ID`, `PROJECT_SLUG`, `SIGNING_POLICY_SLUG`, et ajouter le secret `SIGNPATH_API_TOKEN` dans GitHub → Settings → Secrets → Actions.

Puis Claude Code, dans `release.yml`, **entre** l'upload d'artefact et la Release :

```yaml
- name: Submit signing request to SignPath
  uses: signpath/github-action-submit-signing-request@v1.2
  with:
    api-token: ${{ secrets.SIGNPATH_API_TOKEN }}
    organization-id: <ORGANIZATION_ID>
    project-slug: <PROJECT_SLUG>
    signing-policy-slug: <SIGNING_POLICY_SLUG>
    github-artifact-id: ${{ steps.upload.outputs.artifact-id }}
    wait-for-completion: true
    output-artifact-directory: dist-signed
```

- La Release draft attache alors `dist-signed\*.exe` (le binaire signé), plus `dist\*.exe`.
- Ajouter une step de vérification après signature : `signtool verify /pa dist-signed\*.exe` (échec du job si non signé). `signtool` est dispo via le Windows SDK présent sur `windows-latest` — sinon localiser via `Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter signtool.exe`.
- Config côté SignPath (artifact-configuration) : signer **l'installeur** Inno Setup ; si la config le permet, signer aussi le `Muralis.exe` embarqué (SignPath sait décompresser/resigner les installeurs Inno Setup — sinon, à défaut, l'installeur seul suffit pour SmartScreen au téléchargement).
- **Rappel obligation SignPath : chaque signing request est approuvée manuellement** par l'Approver (@Arkatul) dans le dashboard — `wait-for-completion: true` attendra cette approbation ; prévenir l'utilisateur à chaque release.

Commit : `ci: sign release binaries via SignPath`.

## Étape E — Première release signée

1. Bumper la version aux **deux** endroits : `src\Muralis\Muralis.csproj` (`<Version>`) **et** `installer\Muralis.iss` (`MyAppVersion`). Envisager (chantier séparé, optionnel) de dériver l'une de l'autre pour supprimer la double saisie.
2. Commit, push, puis `git tag vX.Y.Z && git push origin vX.Y.Z`.
3. **👤 UTILISATEUR** : approuver la signature dans SignPath, puis tester l'installeur signé sur une machine/VM Windows propre (vérifier : éditeur affiché dans l'UAC, `signtool verify` OK), publier la Release draft.
4. Documenter dans le README si besoin : l'avertissement SmartScreen peut persister brièvement le temps que la réputation du certificat monte, puis disparaît.

## Étape F (optionnelle) — winget

Après ≥1 release signée publiée : soumettre le manifeste préparé en C à `microsoft/winget-pkgs` (PR), idéalement automatiser la re-soumission à chaque release (`wingetcreate update` dans le workflow, token GitHub dédié). L'utilisateur suit/valide la PR.

---

## Garde-fous globaux

- Ne jamais committer de token/secret dans le repo — uniquement GitHub Secrets.
- Ne pas toucher au processus de build local (`installer/build.ps1` doit continuer de marcher hors CI).
- Toute nouvelle chaîne visible dans l'app (si UI touchée) : passer par `Strings.resx`/`Strings.fr.resx` + `tools\gen-strings.ps1` (cf. AGENTS.md). Ce plan ne devrait pas toucher l'UI.
- Une modif docs/CI ne déclenche **ni bump de version ni release** — seuls les tags `v*` publient.

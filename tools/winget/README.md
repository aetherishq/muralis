# winget manifest — DRAFT, not submitted

Brouillon de manifeste winget pour Muralis (étape C du plan SignPath,
cf. `.claude/plan-signpath.md`). **Ne pas soumettre à `microsoft/winget-pkgs`
avant qu'au moins une release signée soit publiée** (étape F) : winget ne
contourne pas SmartScreen, c'est uniquement un canal de distribution
supplémentaire.

À la soumission (après la première release signée) :

1. Mettre à jour `PackageVersion`, `InstallerUrl` et `InstallerSha256`
   (`Get-FileHash <exe> -Algorithm SHA256`) vers la release signée.
2. Valider localement : `winget validate --manifest tools\winget\Arkatul.Muralis`.
3. Ouvrir la PR sur `microsoft/winget-pkgs` sous
   `manifests/a/Arkatul/Muralis/<version>/` (ou automatiser avec
   `wingetcreate update` dans le workflow de release).

Le `ProductCode` (`{...}_is1`) dérive de l'`AppId` d'Inno Setup déclaré dans
`installer/Muralis.iss` — il ne change pas d'une version à l'autre.

# winget manifest

Manifeste winget pour Muralis (`Aetherishq.Muralis`), soumis à
`microsoft/winget-pkgs` sous `manifests/a/Aetherishq/Muralis/<version>/`.
Les releases ne sont pas signées pour l'instant (cf. `.claude/plan-signpath.md`) :
winget les accepte (validation par hash + scan Defender à la PR), et
`winget install` évite le prompt SmartScreen du double-clic.

À chaque nouvelle release :

1. Mettre à jour `PackageVersion` (les trois fichiers), `InstallerUrl` et
   `InstallerSha256` (`Get-FileHash <exe> -Algorithm SHA256`).
2. Valider localement : `winget validate --manifest tools\winget\Aetherishq.Muralis`.
3. Soumettre : `wingetcreate submit --token (gh auth token) tools\winget\Aetherishq.Muralis`
   (fork + PR automatiques), ou PR manuelle sur `microsoft/winget-pkgs`.
   Automatisation possible plus tard via `wingetcreate update` dans le workflow de release.

Le `ProductCode` (`{...}_is1`) dérive de l'`AppId` d'Inno Setup déclaré dans
`installer/Muralis.iss` — il ne change pas d'une version à l'autre.

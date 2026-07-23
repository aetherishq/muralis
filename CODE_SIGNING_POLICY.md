# Code signing policy

Free code signing for Muralis is provided by [SignPath.io](https://signpath.io),
with a certificate issued by the [SignPath Foundation](https://signpath.org).

Signed release binaries are built exclusively from this repository's source code
through the project's automated GitHub Actions workflow. Every signing request is
reviewed and approved manually before a certificate is applied.

## Team roles

This project is maintained by a single author, who fills all roles:

- **Author** (submits builds for signing): [@Arkatul](https://github.com/Arkatul)
- **Reviewer** (reviews source changes): [@Arkatul](https://github.com/Arkatul)
- **Approver** (approves signing requests): [@Arkatul](https://github.com/Arkatul)

## Privacy policy

Muralis will not transfer any information to networked systems unless explicitly
requested by the user operating it.

- Configuration is stored locally in `%LocalAppData%\Muralis`.
- The app contacts third-party image APIs (e.g. Bing, Wallhaven) **only** when the
  user configures a web wallpaper source. API keys for the built-in providers,
  entered in Settings, are stored locally and encrypted (Windows DPAPI,
  current-user scope). A key entered on a user-defined custom source is stored
  locally in the configuration file, unencrypted.
- No telemetry, no analytics, and no data is sent to the maintainer.

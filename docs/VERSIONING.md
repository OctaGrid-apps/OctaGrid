# Versioning & Releases

OctaGrid uses [Semantic Versioning 2.0.0](https://semver.org) with
[Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning) (NBGV).
**`version.json` is the single source of truth.** Everything else is derived from it.

## Version format

```
MAJOR.MINOR.PATCH[-CHANNEL[.N]]
```

| Part | Meaning |
|---|---|
| `MAJOR` | Breaking changes (e.g. incompatible local database or server protocol changes). |
| `MINOR` | New backwards-compatible features. |
| `PATCH` | Backwards-compatible bug fixes. |
| `-CHANNEL` | Optional pre-release label. No label = stable. |
| `.N` | Optional counter for several builds of the same channel (`-preview.1`, `-preview.2`). |

While the major version is `1`, the app is still **pre-alpha**: features and database
schemas may change between releases.

### Channels

Ordered from least to most mature (SemVer sorts labels alphabetically, so a later
channel must sort after an earlier one — `pre-alpha` < `preview` < `rc` is fine, and any
pre-release always sorts *before* the stable release of the same number):

| Channel | Example | GitHub release | Microsoft Store | Audience |
|---|---|---|---|---|
| Pre-alpha | `1.0.17-pre-alpha` | Pre-release | No | Early testers |
| Alpha / Beta | `1.1.0-beta.2` | Pre-release | No | Testers |
| Preview | `1.1.0-preview.1` | Pre-release | No | Early adopters |
| Release candidate | `1.1.0-rc.1` | Pre-release | No | Final verification |
| Stable | `1.1.0` | Latest release | Yes | Everyone |

Precedence example: `1.1.0-preview.1` < `1.1.0-preview.2` < `1.1.0-rc.1` < `1.1.0`.

## How a version flows through the system

| Where | Value | Source |
|---|---|---|
| Git tag | `v1.0.17-pre-alpha` | Must equal `v` + `version.json` (enforced by CI) |
| Assembly / InformationalVersion | `1.0.17-pre-alpha` (+ commit id on non-release builds) | NBGV |
| App UI (window title, Settings) | `v1.0.17-pre-alpha` | `Helper.GetAppVersion()` |
| Velopack package / installer | `1.0.17-pre-alpha` | `version.json` |
| GitHub release | Title `OctaGrid 1.0.17-pre-alpha (pre-alpha)`, flagged *Pre-release* | Derived from the label |
| MSIX / Microsoft Store | `1.0.17.0` | Numeric part only; Store builds are stable only |

Non-release builds (local, branches, PRs) are stamped by NBGV with a git height and commit id
(e.g. `1.0.17-pre-alpha.5+g1a2b3c4`), so every build is traceable to a commit. Only builds from a
tag matching `publicReleaseRefSpec` get the clean version.

## Release process

1. **Decide the version** using the rules above and edit `version.json`:
   - next pre-alpha: `"version": "1.0.18-pre-alpha"`
   - first preview of 1.1: `"version": "1.1.0-preview.1"`
   - stable: `"version": "1.1.0"`
2. **Commit** — conventional commits are used to generate release notes, so merge the
   release commit like any other change:
   ```bash
   git commit -am "chore(release): 1.1.0-preview.1"
   ```
3. **Tag** the commit with `v` + the exact version and push the tag:
   ```bash
   git tag v1.1.0-preview.1
   git push origin main v1.1.0-preview.1
   ```
4. The **Release & Publish** workflow (`.github/workflows/release.yml`) then:
   - validates `version.json` and that the tag matches it (fails fast otherwise);
   - generates release notes from commits since the previous release (a stable release is
     compared with the previous *stable* tag, a pre-release with the previous tag of any kind);
   - builds the MSIX bundle and the Velopack installer, portable zip and update feed;
   - publishes the GitHub release (pre-release flag, "Latest" only for stable);
   - submits to the Microsoft Store — **stable releases only**.

The workflow can also be started manually (*Actions → Release & Publish → Run workflow*). It
uses the version in `version.json` and creates the matching tag if it does not exist.

### Release notes / commit conventions

Notes are grouped by [Conventional Commit](https://www.conventionalcommits.org) type:

| Prefix | Section |
|---|---|
| `feat:` | Features |
| `perf:`, `refactor:` | Improvements & Performance |
| `fix:` | Fixes |
| anything else | Other Changes |

Use `!` or a `BREAKING CHANGE:` footer for breaking changes and bump `MAJOR` (or `MINOR`
while on `0.x`/pre-alpha, as agreed for that release).

## Auto-update behaviour

- **Stable builds** only see stable releases.
- **Pre-release builds** (any version containing `-`) also see pre-release GitHub releases,
  so testers are moved along the channel and onto the stable release when it ships.
- Microsoft Store installs are updated by the Store (stable only).
- Portable / dev runs do not auto-update.

## Rules of thumb

- Never reuse or move a published tag; publish a new version instead.
- Never bump `version.json` without tagging, or tag without bumping — CI rejects mismatches.
- Don't put the channel anywhere except `version.json` (no manual `-pre-alpha` in tags,
  workflow inputs or scripts).
- Pre-release versions must never be submitted to the Microsoft Store.
- Hotfix for a stable release: bump `PATCH`, tag, release.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| *"Tag … does not match version.json"* | Bump `version.json` to the tag's version (or re-tag). |
| Release not marked pre-release | The version has no `-label` in `version.json`. |
| App shows a `+g<hash>` suffix | Not built from a release tag (local/dev build) — expected. |
| Tester doesn't receive pre-releases | Installed version has no `-label`; install a pre-release build once. |

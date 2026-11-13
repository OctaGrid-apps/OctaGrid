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

OctaGrid is currently in **pre-alpha**: features and database schemas may change between
releases. This is expressed by the release channel (`-pre-alpha`), not by the major version.

### Channels

SemVer orders pre-release labels alphabetically (ASCII), so within one `MAJOR.MINOR.PATCH`
the supported channels progress in this order, and every pre-release sorts *before* the
stable release with the same number:

| Order | Channel | Example | GitHub release | Microsoft Store | Audience |
|---|---|---|---|---|---|
| 1 | Pre-alpha | `1.0.17-pre-alpha` | Pre-release | No | Early testers |
| 2 | Preview | `1.1.0-preview.1` | Pre-release | No | Early adopters |
| 3 | Release candidate | `1.1.0-rc.1` | Pre-release | No | Final verification |
| 4 | Stable | `1.1.0` | Latest release | Yes | Everyone |

Precedence example: `1.1.0-pre-alpha` < `1.1.0-preview.1` < `1.1.0-preview.2` < `1.1.0-rc.1` < `1.1.0`.

> [!IMPORTANT]
> Only use the channels above. Labels such as `alpha` and `beta` sort *before* `pre-alpha`
> (`alpha` < `beta` < `pre-alpha`), so mixing them in one version number would stop testers
> from being offered the update.

## How a version flows through the system

| Where | Value | Source |
|---|---|---|
| Git tag | `v1.0.17-pre-alpha` | Must equal `v` + `version.json` (enforced by CI) |
| Assembly / InformationalVersion | `1.0.17.4-pre-alpha+4f6d5bb` (the 4th number is the git height, the part after `+` the commit) | NBGV |
| App UI (window title, Settings) | `v1.0.17-pre-alpha` (height and commit removed) | `Helper.GetAppVersion()` |
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
3. **Tag** the commit and push the tag. The tag name is `v` + the version from `version.json`,
   so generate it from the file instead of typing it:
   ```powershell
   $tag = "v" + (Get-Content version.json -Raw | ConvertFrom-Json).version
   git tag $tag
   git push origin main $tag
   ```
   (For `"version": "1.1.0-preview.1"` this creates `v1.1.0-preview.1`.)
4. The **Release & Publish** workflow (`.github/workflows/release.yml`) then:
   - validates `version.json` and that the tag matches it (fails fast otherwise);
   - generates release notes from commits since the previous release (a stable release is
     compared with the previous *stable* tag, a pre-release with the previous tag of any kind);
   - builds the MSIX bundle and the Velopack installer, portable zip and update feed;
   - downloads the previous Velopack release (the newest stable one for a stable release, the
     newest of any kind for a pre-release) so `vpk pack` also builds a delta package, which
     installed apps download instead of the full package when they are on that version;
   - publishes the GitHub release with `vpk upload github` (pre-release flag, "Latest" only
     for stable) and then sets its notes;
   - submits to the Microsoft Store — **stable releases only**.

The workflow can also be started manually (*Actions → Release & Publish → Run workflow*) to
re-run a release. It uses the version in `version.json` and creates the matching tag if it
does not exist. Prefer pushing a tag: only tag builds get the clean version number in the
app (see above), whereas a manual run on a branch produces an installer whose assembly
version carries a commit suffix.

### Release notes / commit conventions

Commits and pull request titles follow [Conventional Commits](https://www.conventionalcommits.org)
(see [Commit Conventions](../README.md#commit-conventions)). Release notes are grouped by type:

| Prefix | Section |
|---|---|
| `feat:` | Features |
| `perf:`, `refactor:` | Improvements & Performance |
| `fix:` | Fixes |
| anything else | Other Changes |

Mark breaking changes with `!` or a `BREAKING CHANGE:` footer and reflect them in the next
version: `MAJOR` for a stable breaking change. While in pre-alpha, breaking changes are
expected and may ship in a `MINOR` or `PATCH` bump.

## Auto-update behaviour

- **Stable installs** only see stable releases.
- **Pre-release installs** (an installed Velopack version containing `-`) also see pre-release
  GitHub releases, so testers are moved along the channel and onto the stable release when it ships.
- **Settings > Updates > Pre-release updates** overrides both. Turning it off never downgrades:
  the app stays on its version until a newer stable release ships, because an older version
  cannot undo database changes a newer one made.
- A Velopack update downloads while the app keeps running and is installed when the app
  closes (or right away with *Restart to apply*). If the updater could not start on exit,
  Velopack installs the downloaded update on the next start.
- Microsoft Store installs are updated by the Store (stable only).
- The portable zip (`OctaGrid-win-Portable.zip`, built by Velopack) updates itself like an
  install. Dev runs and copies of a build folder do not auto-update.

## Rules of thumb

- Never reuse or move a published tag; publish a new version instead.
- Never bump `version.json` without tagging, or tag without bumping — CI rejects mismatches.
- The channel is *defined* only in `version.json`. Tags, installers, GitHub releases and the
  Store gating are all derived from it or checked against it, so never hard-code a channel
  label in workflow inputs or scripts, and never type a tag by hand with a different label.
- Pre-release versions must never be submitted to the Microsoft Store.
- Hotfix for a stable release: bump `PATCH`, tag, release.
- The root `version.json` is the product version. A component that is released on its own (for example a separate agent) may have its own `version.json` next to it, but always with the full `major.minor.patch` (never `1.0-…`), `"inherit": true`, its own tag prefix (for example `agent-v1.2.0`) and a release workflow that checks that tag against its file the way `release.yml` does. Add `"pathFilters"` so commits that only touch other components do not raise its git height.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| *"Tag … does not match version.json"* | Bump `version.json` to the tag's version (or re-tag). |
| Release not marked pre-release | The version has no `-label` in `version.json`. |
| App shows a `+g<hash>` suffix | Not built from a release tag (local/dev build) — expected. |
| Tester doesn't receive pre-releases | Installed version has no `-label`; install a pre-release build once. |
| App shows a different number than the release (for example `1.0.38` for release `1.0.20`) | A `version.json` with only `major.minor` (for example `1.0-pre-alpha`) sits closer to the project than the root one. Nerdbank.GitVersioning uses the nearest file, and without a patch number it uses the git height instead. Every `version.json` must name `major.minor.patch` (a test checks this). |

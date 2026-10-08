# IMPORT.md - Sync `base` guidance into brownNote

Agent playbook for pulling shared rules, docs, and script improvements from [base](https://github.com/fosterbarnes/base) into brownNote. brownNote already follows base's layout; this is a merge, not a fresh import. Do not replace brownNote application code.

No em dashes. Use `-` or `:`. Do not run `prePush.ps1`, `pushRelease.ps1`, builds, or version bumps as part of a sync. Do not edit `buildNotes.txt`.

## 1. Locate `base`

1. If the user has a local clone of `base` that contains `AGENTS.md`, `src\.md\`, and `.scripts\`, use that tree.
2. Otherwise, clone `https://github.com/fosterbarnes/base` into a temp directory and use that clone as the source.
3. Never treat brownNote as the source.

## 2. What to merge

Both repos use the same layout: `AGENTS.md` at the root, shared docs in `src\.md\`, scripts in `.scripts\`.

| base file | brownNote handling |
|-----------|--------------------|
| `AGENTS.md` | The General Guidelines, buildNotes.md, and buildNotes.txt sections must match base exactly; copy them over. Keep brownNote's header, Project Guidelines, WPF stack section, and Agent Notes. |
| `src\.md\STYLE.md` | Replace with base's copy. |
| `src\.md\SCRIPTS.md` | Replace with base's copy, then keep the `brownNote configuration` section at the end. |
| `src\.md\THEMING.md` | Replace with base's copy, then restore the `brownNote` palette section in place of base's. |
| `src\.md\DEPENDENCIES.md` | Keep brownNote's tables (NAudio, MaterialDesign). Merge only shared tool rows. |
| `.scripts\*.ps1` | Shared operators (`push.ps1`, `pushRelease.ps1`, `agentPush.ps1`, `newVersion.ps1`, `updateReadme.ps1`) must match base exactly; copy them over. Merge `scriptHelper.ps1`, `build.ps1`, `buildInstaller.ps1`, `.run.ps1`, and `prePush.ps1` by hand, keeping brownNote's project values. |

## 3. Do not copy

- base's sample app, updater, `base.sln`, or csproj files.
- `buildUpdater.ps1`: brownNote has no updater.
- Avalonia packages, AXAML, or Avalonia stack rules.
- base's `README.md` or its `.version\` values.
- `buildNotes.md` / `buildNotes.txt`: local to each clone.

## 4. brownNote rules to preserve

- `scriptHelper.ps1`: `$projectName = 'brownNote'`, `$csproj` under `$srcRoot` (`src\brownNote\brownNote.csproj`), brownNote's GitHub URL and icon paths.
- `$buildTargets`: x64 and arm64 only. Buttons are `x64Installer.svg`, `x64Portable.svg`, `arm64.svg`, and `arm64Portable.svg` from [fosterbarnes/res/btn](https://github.com/fosterbarnes/res/tree/main/btn).
- `checkPitch.ps1` is brownNote-only; keep it.
- `.res\wav\` sounds stay linked from `src\brownNote\brownNote.csproj`.
- Keep the complete `closeOut` implementation: `closeOut 0` after successful standalone scripts, `closeOut -KeepOpen` before rethrowing handled failures, no closeout on help paths.
- `agentPush.ps1`: agents run it only when the user explicitly asks them to push.

## 5. Verify

- `AGENTS.md` still has only the WPF stack section and brownNote's Project Guidelines.
- No base sample, updater, or Avalonia references were introduced.
- `scriptHelper.ps1` paths, names, and `$buildTargets` still match brownNote.
- No personal paths, no em dashes in files you edited.
- If the user keeps a local `buildNotes.md`, add one session entry after a successful sync.

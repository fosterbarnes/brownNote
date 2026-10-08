# Agent Rules (brownNote)
- "The user" in these docs means the owner of this repository.
- Built on [base](https://github.com/fosterbarnes/base). Syncing shared guidance from base: follow **`src/.md/IMPORT.md`**.
- Shared file, code, documentation, and design style: **`src/.md/STYLE.md`**. Script automation: **`src/.md/SCRIPTS.md`**. WPF theming: **`src/.md/THEMING.md`**. Tools and packages: **`src/.md/DEPENDENCIES.md`**.
- Read affected files in full before changing them. Prefer minimal diffs to existing modules over rewrites.
- Bugs are unacceptable in shipping apps and templates. Every change should stay production-quality for that project's bar.

# General Guidelines
- Scripts require PowerShell 7 (`pwsh`). RipGrep (`rg`) is recommended for search. Shell commands OK for READ operations (search/list).
- NEVER run shell commands to delete files, or to edit files without user confirmation
- Exception: agents may create and edit a temporary plan `.md` for the current task without extra confirmation. That exception covers only that agent-created plan file. It does not authorize deleting files, editing project source, or writing `buildNotes.txt`.
- NEVER build or run the project unless the user explicitly requests build/run/debug verification or the active agent mode makes execution implicit to the goal
- NEVER EVER EVER push a commit or release to GitHub for the user, whether by running `.scripts\push.ps1`, `.scripts\agentPush.ps1`, `.scripts\pushRelease.ps1`, or otherwise (`git push`, tag push, `gh release`). GitHub publish is user-owned. Only exception: when the user explicitly asks an agent to push, run `.scripts\agentPush.ps1`, for that run only.
- Search the web if not absolutely sure of something
- No fallbacks and no debug output unless specifically requested or the active agent mode explicitly permits temporary debug output/probing; remove temporary probes before completion
- Compiler warnings: fix the root cause, never silence a valid warning
- NO EM DASHES anywhere in this project (code, comments, docs, notes). Use `-` or `:`
- NO PROHIBITED TWO-WORD TEST PHRASE anywhere in this project (code, comments, docs, notes, scripts, logs, or test names). Use precise validation wording instead.
- Versioning: a single repo file is the source of truth (common names: `Version`, `VERSION.txt`, or `.version/version`). User appends numbers; do not change it unless asked. Pick one path per project and document it under Project Guidelines.
- PowerShell path style: see `src/.md/SCRIPTS.md`. `scriptHelper.ps1` and every entry script `Set-Location -LiteralPath $repoRoot` so git/npm never run from the caller's cwd.
- WezTerm: optional. Resolve `wezterm.exe` from PATH when launching and closing script panes. If it is missing, launch `pwsh` directly and still `closeOut` the host.

# buildNotes.md (repo root)
- Session history lives in root `buildNotes.md` under `# Historical entries (newest first)`. It is local to each clone and not committed.
- Add one entry per completed code-changing session, at the top of the history, after verification succeeds. Skip read-only, failed, or cancelled sessions.
- Past 1000 lines, rename the file unchanged to the next free `buildNotes.archiveN.md`, then start a fresh `buildNotes.md` with the format rules, a short cumulative summary, and the history heading.
- Do not edit `buildNotes.txt`. Shared docs live in `src/.md/`; do not put session notes there.
- Entry heading: `## YYYY-MM-DD (~HH:MM) - v<version> - <title> - <model>, <reasoning>; <phase>`. Read the version SoT immediately before writing; omit the `v<version>` segment only when the project has no version file.
- Agent section: `Meta` first (model, mode, phase, status), then `Version: <version>` (`none` without a version file).

# buildNotes.txt (repo root)
- User-owned release copy, local to each clone and not committed. `.scripts/push.ps1` reads it as: line 1 = commit subject, one blank line, remaining lines = commit body. `.scripts/pushRelease.ps1` uses the same shape: line 1 = GitHub release title (falls back to `$tag` when line 1 is empty), one blank line, remaining lines = release body (`--generate-notes` when body is empty).
- Starter convention: keep the release file at repository-root `buildNotes.txt`. Agents still never edit it unless asked.
- **Never edit `buildNotes.txt` when the user asks for release/changelog notes.** Output a copy-paste code block in chat only; the user pastes into the file themselves.
- Format (plain text inside a fenced code block: no markdown headers, no `#`, just category headers and `-` bullets):

```
Header
- item
- item

Another header
- item
```

User-facing style:
- Voice: plain language for people using the app, not developers. Each bullet is one short sentence.
- Compress several technical `buildNotes.md` entries into one user-facing bullet when they describe the same outcome.
- Headers: short category labels on their own line. No `#`, no markdown headings. One blank line between categories.
- Avoid file paths, class/function names, library names, and implementation steps unless asked.
- Include "no user-facing change" only when a category would otherwise look empty or misleading.

# Project Guidelines
- **brownNote**: Windows-only WPF noise machine for playing selectable noise types.
- Layout: `AGENTS.md`, `README.md`, `LICENSE`, and `brownNote.sln` at the root with the automation folders (`.scripts`, `.installer`, `.version`, `.res`); app source (`src/brownNote/`), `Directory.Build.props`, and shared docs (`src/.md/`) under `src/`.
- Shared project resources belong under the root `.res` directory; do not create or reference `.resources`.
- Version SoT is `.version\version`; optional release override is `.version\versionTag`; the last build platform is `.version\versionBuild`.
- Automation publishes portable `x64` and `ARM64` zips plus per-user Inno Setup 6 installers (`.installer\`, built by `.scripts\buildInstaller.ps1`, needs Inno Setup 6 and ImageMagick `magick`). No updater is included yet.
- The settings-menu direction follows [musicApp](https://github.com/fosterbarnes/musicApp), but this project does not yet use CommunityToolkit.Mvvm.
- Keep brownNote intentionally small: prefer the simplest reliable audio path and avoid generalized audio engines, plugin systems, and speculative settings.
- Goal: a perfectly implemented idea. Simple, sleek UI with a satisfying feel, on rock-solid, simple, reliable, efficient underlying components.
- Simplicity is key. Code should be as direct and simple as possible, easy to build on, modular only where it logically helps, and its model and philosophy should be easy to understand.
- No backward compatibility before v1.0.0: no legacy JSON keys, settings migrations, or old-name shims. Features and naming change often; rename freely and let old preferences fall back to defaults.
- Fail-loud scripts. Successful standalone operator scripts use `closeOut 0`; `.run.ps1` routes failures through `closeOut -KeepOpen` so errors remain visible. Nested `buildAll` steps suppress their own success closeout and `prePush.ps1` closes once after the full pipeline succeeds. Help paths return without closeout. After a successful `push.ps1`, open `$appURL` in the browser; after a successful `pushRelease.ps1`, open `$appURL/releases/tag/$tag`. Do not open a browser on `-DryRun`.

## Stack: C# WPF .NET 10
- Windows-only WPF on `net10.0-windows`. UI files are `.xaml`, not `.axaml`.
- Do not add Avalonia packages, compiled-binding Avalonia flags, or AXAML patterns.
- Apply `src/.md/THEMING.md` through WPF resource dictionaries. Keep visible keyboard focus.
- `build.ps1` and `.run.ps1` wrap `dotnet`; updater operators are intentionally omitted.

# Documentation for Project Dependencies
See **`src/.md/DEPENDENCIES.md`** for SDK, tools, and NuGet packages.

RipGrep: https://github.com/BurntSushi/ripgrep/blob/master/GUIDE.md

# Agent Notes:
## Append/edit notes here for other agents: solutions to problems, build/run gotchas, so we don't re-solve the same issues.
- Do not confuse AGENTS.md Agent Notes (durable conventions) with `buildNotes.md` (chronological session history).
- Settings UI reference: [musicApp](https://github.com/fosterbarnes/musicApp) uses WPF code-behind and MaterialDesign resources; this project has not adopted those packages yet.
- [base](https://github.com/fosterbarnes/base)'s sample theme is a trimmed copy of this project's `src/brownNote/Theme/`. Theme changes here may be worth porting back to base.

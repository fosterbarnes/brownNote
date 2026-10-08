# brownNote

Brown noise machine app. Windows only. C# .NET 10 WPF


<!-- Quick Reference -->
<table border="0">
<tbody>
<tr>
<td valign="top"><a href="https://github.com/fosterbarnes/brownNote/releases/download/v0.2.1/brownNote_v0.2.1_windows-x64.exe"><img src="https://raw.githubusercontent.com/fosterbarnes/res/main/btn/x64Installer.svg" width="180" height="auto" alt="Windows x64 installer"/></a></td>
<td valign="top"><a href="https://github.com/fosterbarnes/brownNote/releases/download/v0.2.1/brownNote_v0.2.1_windows-arm64.exe"><img src="https://raw.githubusercontent.com/fosterbarnes/res/main/btn/arm64.svg" width="180" height="auto" alt="Windows arm64 installer"/></a></td>
</tr>
<tr>
<td valign="top"><a href="https://github.com/fosterbarnes/brownNote/releases/download/v0.2.1/brownNote_v0.2.1_windows-x64.zip"><img src="https://raw.githubusercontent.com/fosterbarnes/res/main/btn/x64Portable.svg" width="180" height="auto" alt="Windows x64 portable ZIP"/></a></td>
<td valign="top"><a href="https://github.com/fosterbarnes/brownNote/releases/download/v0.2.1/brownNote_v0.2.1_windows-arm64.zip"><img src="https://raw.githubusercontent.com/fosterbarnes/res/main/btn/arm64Portable.svg" width="180" height="auto" alt="Windows arm64 portable ZIP"/></a></td>
</tr>
</tbody>
</table>
<!-- End Quick Reference -->

## Noise Machine Tabs

| <h3>Brown</h3> |
|:---:|
| <img src="./.res/scr/1.png" width="600" style="margin: 20px; padding: 20px;"> |

| <h3>Green</h3> |
|:---:|
| <img src="./.res/scr/2.png" width="600" style="margin: 20px; padding: 20px;"> |

| <h3>White</h3> |
|:---:|
| <img src="./.res/scr/3.png" width="600" style="margin: 20px; padding: 20px;"> |

## Info and Links 

Simple explanation of colored noise : https://www.nm.org/healthbeat/healthy-tips/what-noise-color-is-best-for-sleep

## Requirements

- .NET 10 SDK
- PowerShell 7
- Inno Setup 6 for installers
- GitHub CLI for release scripts
- ImageMagick (`magick`) for installer icon helpers

## Versioning

- Semantic version: `.version/version`
- Optional release tag override: `.version/versionTag`
- Last build platform: `.version/versionBuild`
- User release notes: `buildNotes.txt` (local, not committed)
- Agent history: `buildNotes.md` (local, not committed)

## Agent Docs

Built on [base](https://github.com/fosterbarnes/base).

- [AGENTS.md](AGENTS.md): agent rules and project guidelines
- [STYLE.md](src/.md/STYLE.md): file, code, docs, and design style
- [SCRIPTS.md](src/.md/SCRIPTS.md): PowerShell script guide
- [THEMING.md](src/.md/THEMING.md): colors and theming
- [IMPORT.md](src/.md/IMPORT.md): sync shared guidance from `base`
- [DEPENDENCIES.md](src/.md/DEPENDENCIES.md): tools and packages

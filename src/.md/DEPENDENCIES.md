# Dependencies

Windows-only WPF and PowerShell application for generating brown noise through shared-mode
WASAPI output.

## Development Tools

| Tool | Purpose |
|------|---------|
| .NET 10 SDK | Build and publish the WPF app |
| PowerShell 7 (pwsh) | Run `.scripts` automation |
| GitHub CLI | Release script (`pushRelease.ps1`) |
| Inno Setup 6 | Installer script (`buildInstaller.ps1`) |
| ImageMagick (`magick`) | Installer icon and wizard image helpers |
| RipGrep (`rg`) | Repo-wide search (recommended) |

Target machines use Windows. Portable publishing currently covers `win-x64` and `win-arm64` and uses the installed .NET 10 runtime.

## .NET Packages

| Package | Version | Purpose |
|---------|---------|---------|
| NAudio.Wasapi | 3.1.0 | Shared-mode Windows audio output |
| MaterialDesignThemes | 4.9.0 | WPF icons and theme controls |
| MaterialDesignColors | 2.1.4 | MaterialDesign theme color support |

The settings-menu direction follows [musicApp](https://github.com/fosterbarnes/musicApp)'s WPF code-behind. The current app uses
MaterialDesign icons for About-page folder actions.

## Supported Platforms

| Architecture | Runtime identifier | Portable asset |
|--------------|--------------------|----------------|
| x64 | win-x64 | brownNote_v<version>_windows-x64.zip |
| ARM64 | win-arm64 | brownNote_v<version>_windows-arm64.zip |

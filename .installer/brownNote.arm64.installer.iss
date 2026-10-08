#ifndef AppVersion
#define AppVersion "0.0.0"
#endif
[Setup]
AppId={{E2987E36-B744-4A79-8B0D-279B74737DF9}
DefaultDirName={userappdata}\brownNote
OutputDir=Output
OutputBaseFilename=brownNote-arm64-installer
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
Uninstallable=yes
[Files]
Source: "..\publish\build\arm64\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
#include "brownNote.installer.iss"

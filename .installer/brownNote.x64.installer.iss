#ifndef AppVersion
#define AppVersion "0.0.0"
#endif
[Setup]
AppId={{3B58AD54-6F7E-428E-B3B9-00ACC94E3E5D}
DefaultDirName={userappdata}\brownNote
OutputDir=Output
OutputBaseFilename=brownNote-x64-installer
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Uninstallable=yes
[Files]
Source: "..\publish\build\x64\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
#include "brownNote.installer.iss"

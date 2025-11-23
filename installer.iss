[Setup]
AppName=BMW Drive Recorder Overlay
AppVersion=1.0
DefaultDirName={pf}\BMWDriveRecorderOverlay
DefaultGroupName=BMWDriveRecorderOverlay
OutputDir=.\installer
OutputBaseFilename=BMWDriveRecorderOverlayInstaller
Compression=lzma
SolidCompression=yes

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\BMW Drive Recorder Overlay"; Filename: "{app}\BmwDriveRecorderOverlay.exe"
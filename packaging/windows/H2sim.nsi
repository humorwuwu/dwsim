!ifndef VERSION
  !define VERSION "0.0.0"
!endif

!ifndef OUTDIR
  !define OUTDIR "release\windows"
!endif

Name "H2sim"
Caption "H2sim"
OutFile "${OUTDIR}\H2sim-${VERSION}-windows-x64.exe"
RequestExecutionLevel user
SilentInstall silent
InstallDir "$LOCALAPPDATA\H2sim\${VERSION}"
SetCompressor /SOLID lzma
SetCompressorDictSize 64

Section
  SetOutPath "$INSTDIR"
  File /r "DistPackages\Windows\*"
  Exec "$INSTDIR\DWSIM.exe"
SectionEnd
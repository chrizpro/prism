Unicode true
!include "MUI2.nsh"
!include "x64.nsh"
!ifndef VERSION
  !error "VERSION is required"
!endif
!ifndef PUBLISH_DIR
  !error "PUBLISH_DIR is required"
!endif
Name "Prism"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\Prism"
InstallDirRegKey HKCU "Software\Prism" "InstallDirectory"
RequestExecutionLevel user
SetCompressor /SOLID lzma
Icon "${BRAND_ICON}"
UninstallIcon "${BRAND_ICON}"
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "Prism"
VIAddVersionKey "FileDescription" "Prism CAD Setup"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "Prism"
!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "Prism requires 64-bit Windows."
    Abort
  ${EndIf}
FunctionEnd

Section "Prism application" Main
  SectionIn RO
  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}\*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateShortcut "$SMPROGRAMS\Prism.lnk" "$INSTDIR\Prism.Desktop.exe"
  WriteRegStr HKCU "Software\Prism" "InstallDirectory" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Prism" "DisplayName" "Prism"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Prism" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Prism" "Publisher" "Prism"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Prism" "DisplayIcon" "$INSTDIR\Prism.Desktop.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Prism" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Prism" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Prism" "NoRepair" 1
SectionEnd

Section /o "Desktop shortcut" DesktopShortcut
  CreateShortcut "$DESKTOP\Prism.lnk" "$INSTDIR\Prism.Desktop.exe"
SectionEnd

Section "Uninstall"
  ; Delete only the installed manifest. Never recursively remove a user-selected directory.
  !include "${UNINSTALL_MANIFEST}"
  Delete "$INSTDIR\Uninstall.exe"
  Delete "$SMPROGRAMS\Prism.lnk"
  Delete "$DESKTOP\Prism.lnk"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Prism"
  DeleteRegKey HKCU "Software\Prism"
  RMDir "$INSTDIR"
SectionEnd

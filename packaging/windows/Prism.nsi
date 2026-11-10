Unicode true
!include "MUI2.nsh"
!include "x64.nsh"
!define PRISM_DOCUMENT "Prism.Document.1"
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

!macro RegisterPrismType suffix progid label
  WriteRegStr HKCU "Software\Classes\${progid}" "" "${label}"
  WriteRegStr HKCU "Software\Classes\${progid}\DefaultIcon" "" '$\"$INSTDIR\Prism.Desktop.exe$\",0'
  WriteRegStr HKCU "Software\Classes\${progid}\shell\open\command" "" '$\"$INSTDIR\Prism.Desktop.exe$\" $\"%1$\"'
  WriteRegStr HKCU "Software\Classes\${suffix}\OpenWithProgids" "${progid}" ""
  ReadRegStr $0 HKCR "${suffix}" ""
  ${If} $0 == ""
    WriteRegStr HKCU "Software\Classes\${suffix}" "" "${progid}"
  ${EndIf}
!macroend

!macro UnregisterPrismType suffix progid
  DeleteRegValue HKCU "Software\Classes\${suffix}\OpenWithProgids" "${progid}"
  DeleteRegKey /ifempty HKCU "Software\Classes\${suffix}\OpenWithProgids"
  ReadRegStr $0 HKCU "Software\Classes\${suffix}" ""
  ${If} $0 == "${progid}"
    DeleteRegValue HKCU "Software\Classes\${suffix}" ""
  ${EndIf}
  DeleteRegKey /ifempty HKCU "Software\Classes\${suffix}"
  DeleteRegKey HKCU "Software\Classes\${progid}"
!macroend

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
  !insertmacro RegisterPrismType ".prism" "${PRISM_DOCUMENT}" "Prism Document"
  !insertmacro RegisterPrismType ".prism_prt" "Prism.Part.1" "Prism Part"
  !insertmacro RegisterPrismType ".prism_asm" "Prism.Assembly.1" "Prism Assembly"
  !insertmacro RegisterPrismType ".prism_dwg" "Prism.Drawing.1" "Prism Drawing"
  !insertmacro RegisterPrismType ".prism_mld" "Prism.Mold.1" "Prism Mold"
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
SectionEnd

Section /o "Desktop shortcut" DesktopShortcut
  CreateShortcut "$DESKTOP\Prism.lnk" "$INSTDIR\Prism.Desktop.exe"
SectionEnd

Section "Uninstall"
  !insertmacro UnregisterPrismType ".prism" "${PRISM_DOCUMENT}"
  !insertmacro UnregisterPrismType ".prism_prt" "Prism.Part.1"
  !insertmacro UnregisterPrismType ".prism_asm" "Prism.Assembly.1"
  !insertmacro UnregisterPrismType ".prism_dwg" "Prism.Drawing.1"
  !insertmacro UnregisterPrismType ".prism_mld" "Prism.Mold.1"
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
  ; Delete only the installed manifest. Never recursively remove a user-selected directory.
  !include "${UNINSTALL_MANIFEST}"
  Delete "$INSTDIR\Uninstall.exe"
  Delete "$SMPROGRAMS\Prism.lnk"
  Delete "$DESKTOP\Prism.lnk"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Prism"
  DeleteRegKey HKCU "Software\Prism"
  RMDir "$INSTDIR"
SectionEnd

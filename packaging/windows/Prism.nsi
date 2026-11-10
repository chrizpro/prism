Unicode true
!include "MUI2.nsh"
!include "x64.nsh"
!include "WordFunc.nsh"
!define PRISM_DOCUMENT "Prism.Document.1"
!define PRISM_DOCUMENT_KEY "Software\Classes\${PRISM_DOCUMENT}"
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
  Call OfferPreviousUninstall
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
  WriteRegStr HKCU "${PRISM_DOCUMENT_KEY}" "" "Prism Document"
  WriteRegStr HKCU "${PRISM_DOCUMENT_KEY}\DefaultIcon" "" '$\"$INSTDIR\Prism.Desktop.exe$\",0'
  WriteRegStr HKCU "${PRISM_DOCUMENT_KEY}\shell\open\command" "" '$\"$INSTDIR\Prism.Desktop.exe$\" $\"%1$\"'
  WriteRegStr HKCU "Software\Classes\.prism\OpenWithProgids" "${PRISM_DOCUMENT}" ""
  ReadRegStr $0 HKCR ".prism" ""
  ${If} $0 == ""
    WriteRegStr HKCU "Software\Classes\.prism" "" "${PRISM_DOCUMENT}"
  ${EndIf}
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
SectionEnd

Function OfferPreviousUninstall
  IfSilent done
  ReadRegStr $0 HKCU "Software\Prism" "InstallDirectory"
  StrCmp $0 "" done
  ReadRegStr $1 HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Prism" "UninstallString"
  StrCmp $1 '$\"$0\Uninstall.exe$\"' 0 done
  IfFileExists "$0\Prism.Desktop.exe" 0 done
  IfFileExists "$0\Uninstall.exe" 0 done
  ReadRegStr $2 HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Prism" "DisplayVersion"
  StrCmp $2 "" done
  ${VersionCompare} $2 "${VERSION}" $3
  StrCmp $3 "1" done
  MessageBox MB_YESNO|MB_ICONQUESTION|MB_DEFBUTTON2 "Prism $2 is installed at:$\r$\n$0$\r$\n$\r$\nRemove that installation before continuing? Close Prism first. Your documents and preferences will be kept." IDNO done
  ClearErrors
  ExecWait '$\"$0\Uninstall.exe$\" /S _?=$0' $1
  IfErrors failed
  StrCmp $1 "0" 0 failed
  IfFileExists "$0\Prism.Desktop.exe" failed
  Delete "$0\Uninstall.exe"
  RMDir "$0"
  Goto done
failed:
  MessageBox MB_OK|MB_ICONSTOP "Setup could not remove the previous installation. Close Prism and retry setup."
  SetErrorLevel 1
  Abort
done:
FunctionEnd

Section /o "Desktop shortcut" DesktopShortcut
  CreateShortcut "$DESKTOP\Prism.lnk" "$INSTDIR\Prism.Desktop.exe"
SectionEnd

Section "Uninstall"
  DeleteRegValue HKCU "Software\Classes\.prism\OpenWithProgids" "${PRISM_DOCUMENT}"
  DeleteRegKey /ifempty HKCU "Software\Classes\.prism\OpenWithProgids"
  ReadRegStr $0 HKCU "Software\Classes\.prism" ""
  ${If} $0 == "${PRISM_DOCUMENT}"
    DeleteRegValue HKCU "Software\Classes\.prism" ""
  ${EndIf}
  DeleteRegKey /ifempty HKCU "Software\Classes\.prism"
  DeleteRegKey HKCU "${PRISM_DOCUMENT_KEY}"
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

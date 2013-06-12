;NSIS Modern User Interface

;--------------------------------
;Include Modern UI

  !include "MUI2.nsh"

;--------------------------------
;General

  !define VERSION "1.2.1"

  ;Name and file
  Name "Ec2Manager"
  OutFile "Ec2Manager-v${VERSION}.exe"
  Icon "..\bin\Release\icon.ico"

  ;Default installation folder
  InstallDir "$PROGRAMFILES\Ec2Manager"

  ;Get installation folder from registry if available
  InstallDirRegKey HKCU "Software\Ec2Manager" ""

  RequestExecutionLevel admin
  ;Request application privileges for Windows Vista

;--------------------------------
;Interface Settings

  !define MUI_ABORTWARNING

;--------------------------------
;Pages

  !insertmacro MUI_PAGE_LICENSE "..\bin\Release\LICENSE.txt"
  !insertmacro MUI_PAGE_COMPONENTS
  !insertmacro MUI_PAGE_DIRECTORY
  !insertmacro MUI_PAGE_INSTFILES

  !insertmacro MUI_UNPAGE_CONFIRM
  !insertmacro MUI_UNPAGE_INSTFILES

;--------------------------------
;Languages

  !insertmacro MUI_LANGUAGE "English"

;--------------------------------
;Installer Sections

Section
  SetOutPath "$INSTDIR"

  File "..\bin\Release\Ec2Manager.exe"
  File "..\bin\Release\Ec2Manager.exe.config"

  File "..\bin\Release\AWSSDK.dll"
  File "..\bin\Release\Caliburn.Micro.dll"
  File "..\bin\Release\Renci.SshNet.dll"
  File "..\bin\Release\System.Windows.Interactivity.dll"
  File "..\bin\Release\icon.ico"
  File "..\Bin\Release\LICENSE.txt"
  File "..\Bin\Release\README.md"

  ;Store installation folder
  WriteRegStr HKCU "Ec2Manager" "" $INSTDIR

  ;Create uninstaller
  WriteUninstaller  "$INSTDIR\uninstall.exe"

  ;Register to add/remove programs
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ec2Manager" \
                   "DisplayName" "Ec2Manager"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ec2Manager" \
                   "UninstallString" "$\"$INSTDIR\uninstall.exe$\""
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ec2Manager" \
                   "QuietUninstallString" "$\"$INSTDIR\uninstall.exe$\" /S"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ec2Manager" \
                   "DisplayIcon" "$\"$INSTDIR\icon.ico$\""
  WriteRegDWord HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ec2Manager" \
                     "NoModify" 1
  WriteRegDWord HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ec2Manager" \
                     "NoModifyRepair" 1
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ec2Manager" \
                   "InstallLocation" "$\"$INSTDIR$\""
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ec2Manager" \
                   "DisplayVersion" "${VERSION}"

 ; ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
 ; IntFmt $0 "0x%08X" $0
 ; WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ec2Manager" \
                    ; "EstimatedSize" "$0"

SectionEnd

Section "Start Menu Shortcut" secStartMenu
  CreateShortCut "$SMPROGRAMS\Ec2Manager.lnk" "$INSTDIR\Ec2Manager.exe" "" "$INSTDIR\icon.ico"
SectionEnd

Section "Desktop Shortcut" secDesktop
  CreateShortCut "$DESKTOP\Ec2Manager.lnk" "$INSTDIR\Ec2Manager.exe" "" "$INSTDIR\icon.ico"
SectionEnd

;--------------------------------
;Descriptions

  ;Language strings
  ;LangString DESC_secStartMenu ${LANG_ENGLISH} "Add a link in the start menu"

  ;Assign language strings to sections
  ;!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  ;  !insertmacro MUI_DESCRIPTION_TEXT ${SecDummy} $(DESC_SecDummy)
  ;!insertmacro MUI_FUNCTION_DESCRIPTION_END

;--------------------------------
;Uninstaller Section

Section "Uninstall"

  Delete "$SMPROGRAMS\Ec2Manager.lnk"
  Delete "$DESKTOP\Ec2Manager.lnk"

  RMDir /r "$APPDATA\Ec2Manager"

  RMDir /r "$INSTDIR"

  DeleteRegKey /ifempty HKCU "Software\Ec2Manager"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ec2Manager"

SectionEnd

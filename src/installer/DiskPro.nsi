; DiskPro C盘清理大师 - Windows Installer (NSIS)
; Encoding: UTF-8 with BOM for Chinese string support
Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "x64.nsh"

Name "DiskPro C盘清理大师"
OutFile "..\release\DiskPro-Setup.exe"
InstallDir "$PROGRAMFILES64\DiskPro"
InstallDirRegKey HKLM "Software\DiskPro" "InstallDir"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
SetCompressorDictSize 32
ShowInstDetails show
ShowUnInstDetails show
BrandingText "DiskPro C盘清理大师 v2.0.0"

!define PRODUCT_NAME "DiskPro"
!define PRODUCT_NAME_CN "DiskPro C盘清理大师"
!define PRODUCT_VERSION "2.0.0"
!define PRODUCT_PUBLISHER "DiskPro"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\DiskPro"
!define PRODUCT_DIR_REGKEY "Software\DiskPro"

; EstimatedSize in KB (DiskPro.exe ~70061 + README)
!define PRODUCT_ESTIMATED_SIZE 72000

VIProductVersion "2.0.0.0"
VIAddVersionKey /LANG=2052 "ProductName" "DiskPro C盘清理大师"
VIAddVersionKey /LANG=2052 "FileDescription" "DiskPro C盘清理大师 安装程序"
VIAddVersionKey /LANG=2052 "FileVersion" "2.0.0"
VIAddVersionKey /LANG=2052 "ProductVersion" "2.0.0"
VIAddVersionKey /LANG=2052 "CompanyName" "DiskPro"
VIAddVersionKey /LANG=2052 "LegalCopyright" "© DiskPro"

!define MUI_ABORTWARNING
!define MUI_ICON "diskpro.ico"
!define MUI_UNICON "diskpro.ico"
!define MUI_WELCOMEFINISHPAGE_BITMAP "${NSISDIR}\Contrib\Graphics\Wizard\nsis3-metro.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "${NSISDIR}\Contrib\Graphics\Wizard\nsis3-metro.bmp"

!define MUI_FINISHPAGE_RUN "$INSTDIR\DiskPro.exe"
!define MUI_FINISHPAGE_RUN_TEXT "立即运行 DiskPro"
!define MUI_FINISHPAGE_NOREBOOTSUPPORT

; Pages
!define MUI_WELCOMEPAGE_TITLE "欢迎安装 DiskPro C盘清理大师"
!define MUI_WELCOMEPAGE_TEXT "此向导将引导您完成 DiskPro C盘清理大师 的安装。$\r$\n$\r$\n建议在继续安装前关闭其他应用程序。$\r$\n$\r$\n点击“下一步”继续。"
!define MUI_FINISHPAGE_TITLE "安装完成"
!define MUI_FINISHPAGE_TEXT "DiskPro C盘清理大师 已成功安装到您的计算机。$\r$\n$\r$\n点击“完成”关闭安装向导。"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

; Language - Simplified Chinese
!insertmacro MUI_LANGUAGE "SimpChinese"

; Custom Chinese strings for components / finish
LangString DESC_SEC_MAIN ${LANG_SIMPCHINESE} "安装 DiskPro 主程序（必需）"
LangString DESC_SEC_DESKTOP ${LANG_SIMPCHINESE} "在桌面创建 DiskPro 快捷方式（可选）"

; -------------------- Sections --------------------

Section "DiskPro 主程序" SEC_MAIN
  SectionIn RO
  ; close a running instance so the exe can be replaced (upgrade)
  nsExec::ExecToLog 'taskkill /F /IM DiskPro.exe'
  Pop $0
  Sleep 500
  SetOutPath "$INSTDIR"
  Delete "$INSTDIR\README.md" ; legacy v1 file

  ; Main application + docs
  File "..\release\DiskPro.exe"
  File "diskpro.ico"

  ; Uninstaller
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; Start Menu shortcuts
  CreateDirectory "$SMPROGRAMS\DiskPro"
  CreateShortCut "$SMPROGRAMS\DiskPro\DiskPro.lnk" "$INSTDIR\DiskPro.exe" "" "$INSTDIR\diskpro.ico" 0
  CreateShortCut "$SMPROGRAMS\DiskPro\卸载 DiskPro.lnk" "$INSTDIR\Uninstall.exe" "" "$INSTDIR\Uninstall.exe" 0

  ; App install dir registry
  WriteRegStr HKLM "${PRODUCT_DIR_REGKEY}" "InstallDir" "$INSTDIR"
  WriteRegStr HKLM "${PRODUCT_DIR_REGKEY}" "Version" "${PRODUCT_VERSION}"

  ; Uninstall registry (Add/Remove Programs)
  WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "DisplayName" "${PRODUCT_NAME_CN}"
  WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "DisplayIcon" "$INSTDIR\DiskPro.exe"
  WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegDWORD HKLM "${PRODUCT_UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${PRODUCT_UNINST_KEY}" "NoRepair" 1
  WriteRegDWORD HKLM "${PRODUCT_UNINST_KEY}" "EstimatedSize" ${PRODUCT_ESTIMATED_SIZE}
SectionEnd

Section "桌面快捷方式" SEC_DESKTOP
  CreateShortCut "$DESKTOP\DiskPro.lnk" "$INSTDIR\DiskPro.exe" "" "$INSTDIR\diskpro.ico" 0
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_MAIN} $(DESC_SEC_MAIN)
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_DESKTOP} $(DESC_SEC_DESKTOP)
!insertmacro MUI_FUNCTION_DESCRIPTION_END

; -------------------- Uninstaller --------------------

Section "Uninstall"
  ; Stop running instance if possible (best-effort)
  nsExec::ExecToLog 'taskkill /F /IM DiskPro.exe'
  Pop $0
  ; remove the auto-clean scheduled task if the user enabled it
  nsExec::ExecToLog 'schtasks /Delete /F /TN "DiskPro\AutoClean"'
  Pop $0

  Delete "$INSTDIR\DiskPro.exe"
  Delete "$INSTDIR\diskpro.ico"
  Delete "$INSTDIR\README.md"
  Delete "$INSTDIR\Uninstall.exe"

  Delete "$DESKTOP\DiskPro.lnk"
  Delete "$SMPROGRAMS\DiskPro\DiskPro.lnk"
  Delete "$SMPROGRAMS\DiskPro\卸载 DiskPro.lnk"
  RMDir "$SMPROGRAMS\DiskPro"

  DeleteRegKey HKLM "${PRODUCT_UNINST_KEY}"
  DeleteRegKey HKLM "${PRODUCT_DIR_REGKEY}"

  RMDir "$INSTDIR"
SectionEnd

; -------------------- Init --------------------

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "DiskPro 仅支持 64 位 Windows。$\r$\n请在 64 位系统上安装。"
    Abort
  ${EndIf}
  SetRegView 64
  SetShellVarContext all
FunctionEnd

Function un.onInit
  SetRegView 64
  SetShellVarContext all
FunctionEnd

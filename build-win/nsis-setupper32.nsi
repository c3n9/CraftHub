Unicode True
RequestExecutionLevel admin

!define APP_NAME "CraftHub"
!define COMP_NAME "Manakov Corporation"
!define WEB_SITE "https://github.com/c3n9/CraftHub"
!ifndef VERSION
!define VERSION "1.0.0"
!endif
!define COPYRIGHT "[Manakov Corporation, 2026]"
!define DESCRIPTION "CraftHub features a powerful JSON editor and JSON creation tools, alongside an intuitive platform for uploading existing programming language classes or building new ones from scratch."
!define INSTALLER_NAME "crafthub_x86.exe"
!define MAIN_APP_EXE "CraftHub.exe"
!define ICON "../build-resources/logo.ico"
!define BANNER "../build-resources/banner.bmp"
#!define LICENSE_TXT "[CHANGEME License Text Document]"

!define INSTALL_DIR "$PROGRAMFILES\${APP_NAME}"
!define INSTALL_TYPE "SetShellVarContext all"
!define REG_ROOT "HKLM"
!define REG_APP_PATH "Software\Microsoft\Windows\CurrentVersion\App Paths\${MAIN_APP_EXE}"
!define UNINSTALL_PATH "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"
!define REG_START_MENU "Start Menu Folder"

var SM_Folder

######################################################################

VIProductVersion  "${VERSION}"
VIAddVersionKey "ProductName"  "${APP_NAME}"
VIAddVersionKey "CompanyName"  "${COMP_NAME}"
VIAddVersionKey "LegalCopyright"  "${COPYRIGHT}"
VIAddVersionKey "FileDescription"  "${DESCRIPTION}"
VIAddVersionKey "FileVersion"  "${VERSION}"

######################################################################

SetCompressor /SOLID Lzma
Name "${APP_NAME}"
Caption "${APP_NAME}"
OutFile "${INSTALLER_NAME}"
BrandingText "${APP_NAME}"
InstallDirRegKey "${REG_ROOT}" "${REG_APP_PATH}" ""
InstallDir "${INSTALL_DIR}"

######################################################################

!define MUI_ICON "${ICON}"
!define MUI_UNICON "${ICON}"
!define MUI_WELCOMEFINISHPAGE_BITMAP "${BANNER}"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "${BANNER}"

######################################################################

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "Sections.nsh"

; --- Предупреждения о прерывании установки ---
!define MUI_ABORTWARNING
!define MUI_UNABORTWARNING

; --- Страница приветствия ---
!insertmacro MUI_PAGE_WELCOME

; --- Лицензионное соглашение, если задано ---
!ifdef LICENSE_TXT
    !insertmacro MUI_PAGE_LICENSE "${LICENSE_TXT}"
!endif

!insertmacro MUI_PAGE_COMPONENTS

; --- Выбор папки установки ---
!insertmacro MUI_PAGE_DIRECTORY

; --- Страница выбора Start Menu ---
!ifdef REG_START_MENU
    !define MUI_STARTMENUPAGE_DEFAULTFOLDER "${APP_NAME}"
    !define MUI_STARTMENUPAGE_REGISTRY_ROOT "${REG_ROOT}"
    !define MUI_STARTMENUPAGE_REGISTRY_KEY "${UNINSTALL_PATH}"
    !define MUI_STARTMENUPAGE_REGISTRY_VALUENAME "${REG_START_MENU}"
    !insertmacro MUI_PAGE_STARTMENU Application $SM_Folder
!endif

; --- Страница копирования файлов ---
!insertmacro MUI_PAGE_INSTFILES

; --- Финальная страница с галочкой "Launch CraftHub" ---
!define MUI_FINISHPAGE_RUN "$INSTDIR\${MAIN_APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Launch ${APP_NAME}"
!insertmacro MUI_PAGE_FINISH

; --- Страницы удаления (Uninstall) ---
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

; --- Язык интерфейса ---
!insertmacro MUI_LANGUAGE "English"

LangString DESC_SecContextMenu ${LANG_ENGLISH} \
    "Add an 'Open with ${APP_NAME}' entry to the right-click context menu for .json and .cs files."

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
!insertmacro MUI_DESCRIPTION_TEXT ${SecContextMenu} $(DESC_SecContextMenu)
!insertmacro MUI_FUNCTION_DESCRIPTION_END

######################################################################

Function LaunchApplication
    Exec '"$INSTDIR\${MAIN_APP_EXE}"'
FunctionEnd

Function .onInit
    ReadRegDWORD $0 ${REG_ROOT} "${UNINSTALL_PATH}" "ContextMenuEnabled"
    ${If} $0 == 0
        !insertmacro UnselectSection ${SecContextMenu}
    ${Else}
        !insertmacro SelectSection ${SecContextMenu}
    ${EndIf}
FunctionEnd

######################################################################

Section -MainProgram
	${INSTALL_TYPE}

	SetOverwrite ifnewer
	SetOutPath "$INSTDIR"
    SetDetailsPrint none
	File /r "staging_folder32\\"
    SetDetailsPrint both

    ExecWait 'icacls "$INSTDIR" /grant *S-1-1-0:(OI)(CI)F /T'
SectionEnd

######################################################################

Section -Icons_Reg
    SetOutPath "$INSTDIR"
    WriteUninstaller "$INSTDIR\uninstall.exe"

    !ifdef REG_START_MENU
    !insertmacro MUI_STARTMENU_WRITE_BEGIN Application
    CreateDirectory "$SMPROGRAMS\$SM_Folder"
    CreateShortCut "$SMPROGRAMS\$SM_Folder\${APP_NAME}.lnk" "$INSTDIR\${MAIN_APP_EXE}"
    CreateShortCut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${MAIN_APP_EXE}"
    CreateShortCut "$SMPROGRAMS\$SM_Folder\Uninstall ${APP_NAME}.lnk" "$INSTDIR\uninstall.exe"

    !ifdef WEB_SITE
    WriteIniStr "$INSTDIR\${APP_NAME} website.url" "InternetShortcut" "URL" "${WEB_SITE}"
    CreateShortCut "$SMPROGRAMS\$SM_Folder\${APP_NAME} Website.lnk" "$INSTDIR\${APP_NAME} website.url"
    !endif
    !insertmacro MUI_STARTMENU_WRITE_END
    !endif

    !ifndef REG_START_MENU
    CreateDirectory "$SMPROGRAMS\${APP_NAME}"
    CreateShortCut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${MAIN_APP_EXE}"
    CreateShortCut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${MAIN_APP_EXE}"
    CreateShortCut "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk" "$INSTDIR\uninstall.exe"

    !ifdef WEB_SITE
    WriteIniStr "$INSTDIR\${APP_NAME} website.url" "InternetShortcut" "URL" "${WEB_SITE}"
    CreateShortCut "$SMPROGRAMS\${APP_NAME}\${APP_NAME} Website.lnk" "$INSTDIR\${APP_NAME} website.url"
    !endif
    !endif

    WriteRegStr ${REG_ROOT} "${REG_APP_PATH}" "" "$INSTDIR\${MAIN_APP_EXE}"
    WriteRegStr ${REG_ROOT} "${UNINSTALL_PATH}"  "DisplayName" "${APP_NAME}"
    WriteRegStr ${REG_ROOT} "${UNINSTALL_PATH}"  "UninstallString" "$INSTDIR\uninstall.exe"
    WriteRegStr ${REG_ROOT} "${UNINSTALL_PATH}"  "DisplayIcon" "$INSTDIR\${MAIN_APP_EXE}"
    WriteRegStr ${REG_ROOT} "${UNINSTALL_PATH}"  "DisplayVersion" "${VERSION}"
    WriteRegStr ${REG_ROOT} "${UNINSTALL_PATH}"  "Publisher" "${COMP_NAME}"
    WriteRegDWORD ${REG_ROOT} "${UNINSTALL_PATH}" "NoModify" 1
    WriteRegDWORD ${REG_ROOT} "${UNINSTALL_PATH}" "NoRepair" 1

    !ifdef WEB_SITE
    WriteRegStr ${REG_ROOT} "${UNINSTALL_PATH}"  "URLInfoAbout" "${WEB_SITE}"
    !endif

    ${IfNot} ${SectionIsSelected} ${SecContextMenu}
        WriteRegDWORD ${REG_ROOT} "${UNINSTALL_PATH}" "ContextMenuEnabled" 0
    ${EndIf}

    ; --- Silent режим: автоматический запуск после установки ---
    ${If} ${Silent}
        Call LaunchApplication
    ${EndIf}
SectionEnd

######################################################################

Section "Open with ${APP_NAME} (context menu)" SecContextMenu
    ${INSTALL_TYPE}

    WriteRegStr HKLM "Software\Classes\SystemFileAssociations\.json\shell\${APP_NAME}" "" "Open with ${APP_NAME}"
    WriteRegStr HKLM "Software\Classes\SystemFileAssociations\.json\shell\${APP_NAME}" "Icon" "$INSTDIR\${MAIN_APP_EXE}"
    WriteRegStr HKLM "Software\Classes\SystemFileAssociations\.json\shell\${APP_NAME}\command" "" '"$INSTDIR\${MAIN_APP_EXE}" "%1"'

    WriteRegStr HKLM "Software\Classes\SystemFileAssociations\.cs\shell\${APP_NAME}" "" "Open with ${APP_NAME}"
    WriteRegStr HKLM "Software\Classes\SystemFileAssociations\.cs\shell\${APP_NAME}" "Icon" "$INSTDIR\${MAIN_APP_EXE}"
    WriteRegStr HKLM "Software\Classes\SystemFileAssociations\.cs\shell\${APP_NAME}\command" "" '"$INSTDIR\${MAIN_APP_EXE}" "%1"'

    WriteRegDWORD ${REG_ROOT} "${UNINSTALL_PATH}" "ContextMenuEnabled" 1

    System::Call 'shell32::SHChangeNotify(i 0x8000000, i 0, i 0, i 0)'
SectionEnd

######################################################################

Section Uninstall
    ${INSTALL_TYPE}

    DeleteRegKey HKLM "Software\Classes\SystemFileAssociations\.json\shell\${APP_NAME}"
    DeleteRegKey HKLM "Software\Classes\SystemFileAssociations\.cs\shell\${APP_NAME}"
    System::Call 'shell32::SHChangeNotify(i 0x8000000, i 0, i 0, i 0)'

    RmDir /r "$INSTDIR"

    !ifdef REG_START_MENU
    !insertmacro MUI_STARTMENU_GETFOLDER "Application" $SM_Folder
    Delete "$SMPROGRAMS\$SM_Folder\${APP_NAME}.lnk"
    Delete "$SMPROGRAMS\$SM_Folder\Uninstall ${APP_NAME}.lnk"
    !ifdef WEB_SITE
    Delete "$SMPROGRAMS\$SM_Folder\${APP_NAME} Website.lnk"
    !endif
    Delete "$DESKTOP\${APP_NAME}.lnk"

    RmDir "$SMPROGRAMS\$SM_Folder"
    !endif

    !ifndef REG_START_MENU
    Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
    Delete "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk"
    !ifdef WEB_SITE
    Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME} Website.lnk"
    !endif
    Delete "$DESKTOP\${APP_NAME}.lnk"

    RmDir "$SMPROGRAMS\${APP_NAME}"
    !endif

    DeleteRegKey ${REG_ROOT} "${REG_APP_PATH}"
    DeleteRegKey ${REG_ROOT} "${UNINSTALL_PATH}"
SectionEnd
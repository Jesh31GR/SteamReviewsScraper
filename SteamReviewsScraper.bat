@echo off
setlocal enabledelayedexpansion

:: ======================================
:: CONFIGURACIÓN (AJUSTA ESTOS PATHS)
:: ======================================
set SCRAPER_EXE=U:\SteamReviewsScraper\SteamReviewsScraper\bin\Release\net9.0\SteamReviewsScraper.exe
set CONVERTER_EXE=U:\SteamReviewsScraper\TxtToCsv\bin\Release\net9.0\TxtToCsv.exe
set SCRAPER_DIR=U:\SteamReviewsScraper
set DESKTOP=%USERPROFILE%\Desktop

:: ======================================
:: PEDIR URL UNA SOLA VEZ
:: ======================================
cls
echo ============================
echo   Steam Review Scraper 
echo ============================
echo.
set /p URL= Ingresa la URL: 

if "%URL%"=="" (
    echo Debes ingresar una URL.
    pause
    exit /b
)

:: ======================================
:: LOOP PRINCIPAL
:: ======================================
:MENU
cls
echo ============================
echo   Steam Review Scraper 
echo ============================
echo URL en uso:
echo %URL%
echo ============================
echo.
echo Selecciona el tipo de proceso:
echo   1. All reviews
echo   2. Negative only
echo   3. Positive only
echo   4. EVERYTHING (1+2+3 sin preguntar)
echo   5. Salir
echo.
set /p OPT= Opcion: 

if "%OPT%"=="5" goto END_ALL
if "%OPT%"=="4" goto EVERYTHING

if "%OPT%"=="1" set FLAG=
if "%OPT%"=="2" set FLAG=-n
if "%OPT%"=="3" set FLAG=-po

if NOT "%OPT%"=="1" if NOT "%OPT%"=="2" if NOT "%OPT%"=="3" (
    echo Opcion invalida.
    pause
    goto MENU
)

goto RUN_SINGLE


:: ======================================
:: ************  MODO EVERYTHING ************
:: ======================================
:EVERYTHING
echo ======================================
echo        EJECUTANDO TODO
echo   (all + negative + positive)
echo ======================================
echo.

for %%X in (" " "-n" "-po") do (
    set "FLAG=%%~X"

    echo ----------------------------------
    echo Ejecutando proceso: %%~X
    echo ----------------------------------

    call :PROCESS_RUN "%%~X"
)

echo.
echo ✓ MODO COMPLETO FINALIZADO
echo.
goto END_ALL


:: ======================================
:: *********  MODO INDIVIDUAL ***********
:: ======================================
:RUN_SINGLE
call :PROCESS_RUN "%FLAG%"
goto ASK_AGAIN


:: =======================================================
:: *************  SUBRUTINA PRINCIPAL  ********************
:: =======================================================
:PROCESS_RUN
set FLAG=%~1

cls
echo Ejecutando scraper con FLAG: %FLAG%
echo.

"%SCRAPER_EXE%" "%URL%" %FLAG%

echo.
echo Buscando carpeta generada...

set LASTDIR=
for /f "delims=" %%d in ('dir "%DESKTOP%\reviews_*" /ad /b /o-d') do (
    if not defined LASTDIR set LASTDIR=%%d
)

if not defined LASTDIR (
    echo ❌ No se encontro carpeta reviews_*
    exit /b
)

set OUTDIR=%DESKTOP%\!LASTDIR!
echo Carpeta detectada: "!OUTDIR!"
echo.

:: mover page.html si existe
if exist "%SCRAPER_DIR%\page.html" (
    move "%SCRAPER_DIR%\page.html" "!OUTDIR!\page.html" >nul
)

:: Obtener archivos TXT
set FILES=
for %%f in ("!OUTDIR!\*.txt") do (
    set FILES=!FILES! "%%~f"
)

if "!FILES!"=="" exit /b

:: SOLO crear carpeta CSV si hay archivos que NO sean all_reviews
set CREATE_CSV_DIR=0

for %%A in (!FILES!) do (
    if /i NOT "%%~nxA"=="all_reviews.txt" (
        set CREATE_CSV_DIR=1
    )
)

:: Crear carpeta CSV solo si NO es all_reviews
set "CREATECSV=0"

for %%f in ("!OUTDIR!\*.txt") do (
    echo %%~nxf | findstr /i "negative" >nul && set CREATECSV=1
    echo %%~nxf | findstr /i "positive" >nul && set CREATECSV=1
)

if !CREATECSV!==1 (
    if not exist "!OUTDIR!\csv" mkdir "!OUTDIR!\csv"
)

:: convertir
for %%f in (!FILES!) do (
    "%CONVERTER_EXE%" "%%~f" -csv
    if exist "%%~dpnf.csv" (
        if /i "%%~nxf"=="all_reviews.txt" (
            move "%%~dpnf.csv" "!OUTDIR!\" >nul
        ) else (
            move "%%~dpnf.csv" "!OUTDIR!\csv\" >nul
        )
    )
)

:: organizar TXT
for %%f in ("!OUTDIR!\*.txt") do (
    if /i "%%~nxf"=="all_reviews.txt" (
        rem no mover
    ) else (
        echo %%~nxf | findstr /i "negative" >nul && (
            if not exist "!OUTDIR!\negative" mkdir "!OUTDIR!\negative"
            move "%%~f" "!OUTDIR!\negative\" >nul
        )
        echo %%~nxf | findstr /i "positive" >nul && (
            if not exist "!OUTDIR!\positive" mkdir "!OUTDIR!\positive"
            move "%%~f" "!OUTDIR!\positive\" >nul
        )
    )
)

:: organizar CSV
if exist "!OUTDIR!\csv" (
    for %%c in ("!OUTDIR!\csv\*.csv") do (
        echo %%~nxc | findstr /i "negative" >nul && (
            if not exist "!OUTDIR!\negative" mkdir "!OUTDIR!\negative"
            move "%%c" "!OUTDIR!\negative\" >nul
        )
        echo %%~nxc | findstr /i "positive" >nul && (
            if not exist "!OUTDIR!\positive" mkdir "!OUTDIR!\positive"
            move "%%c" "!OUTDIR!\positive\" >nul
        )
    )
)

exit /b


:: =======================================================
:: PREGUNTAR SI QUIERE REPETIR
:: =======================================================
:ASK_AGAIN
echo.
echo Realizar otro proceso con esta misma URL? (S/N):
set /p AGAIN=

if /i "!AGAIN!"=="S" goto MENU


:: =======================================================
:: SALIR
:: =======================================================
:END_ALL
echo Gracias por usar Steam Review Scraper.
echo.
pause
echo Abriendo carpeta generada...
explorer "!OUTDIR!"

:: Borrar carpeta CSV completamente
if exist "!OUTDIR!\csv" rd /s /q "!OUTDIR!\csv"

exit /b
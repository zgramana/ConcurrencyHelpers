@echo off >NUL 2>NUL

SET VS_VERSION=12.0
SET VERBOSITY=FALSE
if "%1"=="TRUE" (
	SET VERBOSITY=TRUE
)

echo Setting up environment variables
echo ================================================================

SET UTILS_ROOT=%CD%

SET VS_NAME=VSPATH

VisualStudioIdentifier %VS_VERSION% %VS_NAME% vs.bat"
call vs.bat
if "%VERBOSITY%"=="TRUE" (
    del /q vs.bat
) ELSE (
    del /q vs.bat >NUL 2>NUL
)
Cd ..
SET CURRENT_DIR=%CD%
SET SOLUTION_DIR=%CD%
SET NUGET_DIR=%CURRENT_DIR%\.nuget


cd %VSPATH%
cd..
SET VSPATH=%CD%
CD %CURRENT_DIR%

call "%VSPATH%\Tools\VsDevCmd.bat"

echo @echo off ^>NUL 2^>NUL > %UTILS_ROOT%\cleanup.bat

REM mkdir tmp_nuget
CD %UTILS_ROOT%
echo.
@echo off
setlocal
rem Builds the native stack-overflow logger shim (x64) and drops it next to YaOpt.dll.
rem Adjust MSVC/SDK roots if the toolchain moves; INCLUDE/LIB from an existing VS
rem developer prompt take precedence.

set MSVC=D:\VS\VC\Tools\MSVC\14.51.36231
set SDK=D:\Windows Kits\10
set SDKVER=10.0.26100.0
set CL_EXE=%MSVC%\bin\Hostx64\x64\cl.exe

if "%INCLUDE%"=="" (
	set INCLUDE=%MSVC%\include;%SDK%\Include\%SDKVER%\um;%SDK%\Include\%SDKVER%\shared;%SDK%\Include\%SDKVER%\ucrt
	set LIB=%MSVC%\lib\x64;%SDK%\Lib\%SDKVER%\um\x64;%SDK%\Lib\%SDKVER%\ucrt\x64
)

if not exist "%CL_EXE%" (
	echo cl.exe not found at "%CL_EXE%" - update MSVC root in this script.
	exit /b 1
)

"%CL_EXE%" /nologo /LD /MT /O2 /W4 /GS- YaOptVeh.c /Fe:..\..\1.7\Assemblies\YaOpt.NativeVEH.dll /Fo:YaOptVeh.obj /link /IMPLIB:YaOptVeh.lib
if errorlevel 1 exit /b 1
del YaOptVeh.lib YaOptVeh.exp >nul 2>nul
echo YaOpt.NativeVEH.dll build OK
endlocal

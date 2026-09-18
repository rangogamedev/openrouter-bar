@echo off
rem Builds and runs the self-test for the UI-free logic in src\Core.cs.
setlocal
cd /d "%~dp0"
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
set "CSC=%FW%\csc.exe"
if not exist "%CSC%" (echo csc.exe not found at %CSC% & exit /b 1)
if not exist bin mkdir bin
"%CSC%" /nologo /target:exe /noconfig /langversion:5 /codepage:65001 /warnaserror+ /out:bin\SelfTest.exe ^
  /r:"%FW%\System.dll" /r:"%FW%\System.Drawing.dll" /r:"%FW%\System.Runtime.Serialization.dll" /r:"%FW%\System.Xml.dll" ^
  src\AssemblyInfo.cs src\Core.cs tests\SelfTest.cs
if errorlevel 1 exit /b 1
bin\SelfTest.exe
exit /b %ERRORLEVEL%

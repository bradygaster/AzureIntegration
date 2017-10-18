SET DOTNET=D:\Program Files (x86)\dotnet
SET DOTNETCACHE=D:\DotNetCache
SET RUNTIMES=%DOTNET%\shared\Microsoft.NETCore.App

REM robocopy "%DOTNET%" "." /E /XC /XN /XO /NFL /NDL ^
REM     /XD "%DOTNET%\sdk" ^
REM     /XD "%RUNTIMES%\1.0.3" ^
REM     /XD "%RUNTIMES%\1.0.4" ^
REM     /XD "%RUNTIMES%\1.1.0" ^
REM     /XD "%RUNTIMES%\1.1.0-preview1-001100-00" ^
REM     /XD "%RUNTIMES%\1.1.1" ^
REM     /XD "%RUNTIMES%\2.0.0-preview1-002111-00" ^
REM     /XD "%RUNTIMES%\2.0.0-preview2-25407-01"

rem don't need this untill we test new cache
rem robocopy "%DOTNETCACHE%" "DotNetCache" /E /XC /XN /XO /NFL /NDL

rem force first time experience
dotnet msbuild /version

if %errorlevel% geq 8 exit /b 1
exit /b 0
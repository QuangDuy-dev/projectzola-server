@echo off
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo ========================================================
    echo [!] VUI LONG CHAY BANG QUYEN ADMINISTRATOR
    echo Chuot phai vao file nay va chon "Run as administrator"
    echo ========================================================
    pause
    exit /b 1
)

echo [1/3] Dang dung service MySocialAppService...
sc.exe stop "MySocialAppService"
timeout /t 3 /nobreak >nul

echo [2/3] Dang publish ban backend moi nhat vao D:\ZOLA...
dotnet publish "d:\Projectzola\sever\src\MySocialApp.Api\MySocialApp.Api.csproj" -c Release -o "D:\ZOLA"

echo [3/3] Dang khoi dong lai service...
sc.exe start "MySocialAppService"
timeout /t 2 /nobreak >nul

sc.exe query "MySocialAppService"

echo ========================================================
echo Cap nhat backend thanh cong!
echo ========================================================
pause

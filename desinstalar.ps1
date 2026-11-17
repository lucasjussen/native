<#
.SYNOPSIS
    Remove o Native deste usuário.

.PARAMETER ApagarDados
    Também apaga ajustes, contatos guardados, ícones e registros (%LOCALAPPDATA%\Native).

.PARAMETER Silencioso
    Não pergunta nada (mantém os dados, a menos que -ApagarDados seja usado).
#>
param([switch]$ApagarDados, [switch]$Silencioso)

$ErrorActionPreference = 'SilentlyContinue'
$App = 'Native'
$Destino = Join-Path $env:LOCALAPPDATA "Programs\$App"
$Dados = Join-Path $env:LOCALAPPDATA $App

if (-not $Silencioso -and -not $ApagarDados) {
    $resp = Read-Host 'Apagar também os seus ajustes e a agenda guardada neste PC? (s/N)'
    $ApagarDados = $resp -match '^[sS]'
}

Get-Process $App | Stop-Process -Force
Start-Sleep -Milliseconds 600

Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $App
Remove-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$App" -Recurse -Force
Remove-Item -Path (Join-Path ([Environment]::GetFolderPath('Programs')) "$App.lnk") -Force
Remove-Item -Path (Join-Path ([Environment]::GetFolderPath('SendTo')) 'iPhone (Native).lnk') -Force
if (Get-NetFirewallRule -DisplayName 'Native (arquivos do iPhone)') {
    if (-not $Silencioso) { Write-Host 'Removendo a regra do firewall (o Windows vai pedir permissão)...' }
    Start-Process powershell -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList '-NoProfile', '-Command',
        "Remove-NetFirewallRule -DisplayName 'Native (arquivos do iPhone)'"
}
if ($ApagarDados) { Remove-Item -Path $Dados -Recurse -Force }

# A pasta do programa é apagada logo depois que este script terminar (ele mesmo está dentro dela).
Start-Process -FilePath cmd.exe -ArgumentList "/c timeout /t 2 >nul & rmdir /s /q `"$Destino`"" -WindowStyle Hidden

Write-Host 'Native removido.' -ForegroundColor Green
if (-not $Silencioso) { Start-Sleep -Seconds 2 }

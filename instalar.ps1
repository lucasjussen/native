<#
.SYNOPSIS
    Compila e instala o Native para o usuário atual (sem precisar de administrador).

.DESCRIPTION
    - Instala o .NET 8 SDK pelo winget, se faltar.
    - Roda os testes automáticos do protocolo.
    - Publica em %LOCALAPPDATA%\Programs\Native.
    - Cria atalho no Menu Iniciar e registra em "Aplicativos instalados" (com desinstalador).
    - Abre o app.

.PARAMETER SemTestes
    Pula os testes automáticos (mais rápido).
#>
param([switch]$SemTestes)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Set-Location $PSScriptRoot

$App        = 'Native'
$Destino    = Join-Path $env:LOCALAPPDATA "Programs\$App"
$Projeto    = Join-Path $PSScriptRoot 'Native\Native.csproj'
$Testes     = Join-Path $PSScriptRoot 'Native.Tests\Native.Tests.csproj'
$Exe        = Join-Path $Destino "$App.exe"
$ChaveUninst = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$App"
$ChaveRun   = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

function Passo($texto) { Write-Host ""; Write-Host "==> $texto" -ForegroundColor Cyan }
function Ok($texto)    { Write-Host "    $texto" -ForegroundColor Green }

Write-Host ""
Write-Host "  Native - instalação" -ForegroundColor White
Write-Host "  iPhone no Windows: notificações, mensagens, ligações e espelhamento" -ForegroundColor DarkGray

# ---------------------------------------------------------------- .NET 8
Passo 'Verificando o .NET 8 SDK'
$temSdk = (Get-Command dotnet -ErrorAction SilentlyContinue) -and ((dotnet --list-sdks) -match '^8\.')
if (-not $temSdk) {
    Write-Host '    Instalando o .NET 8 SDK (o Windows vai pedir permissão)...'
    winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements | Out-Null
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
    if (-not ((dotnet --list-sdks) -match '^8\.')) { throw 'Não consegui instalar o .NET 8 SDK. Instale manualmente em https://dot.net e rode de novo.' }
}
Ok 'Ok'

# ---------------------------------------------------------------- testes
if (-not $SemTestes) {
    Passo 'Rodando os testes automáticos'
    dotnet run --project $Testes -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Algum teste falhou. A instalação foi interrompida para não instalar uma versão com defeito.' }
}

# ---------------------------------------------------------------- publicar
Passo 'Fechando versões abertas'
Get-Process $App, 'Continuidade', 'PhoneExperienceHost' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600
Ok 'Ok'

# ---------------------------------------------------------------- versão antiga ("Continuidade")
# O app se chamava Continuidade até a 1.2. Os ajustes são trazidos pelo próprio app na primeira abertura.
$Antigo = 'Continuidade'
$abrirComWindows = [bool](Get-ItemProperty -Path $ChaveRun -Name $Antigo -ErrorAction SilentlyContinue)
$temAntigo = (Test-Path (Join-Path $env:LOCALAPPDATA "Programs\$Antigo")) -or (Test-Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$Antigo")
if ($temAntigo) {
    Passo 'Removendo a versão antiga (Continuidade)'
    Remove-Item (Join-Path $env:LOCALAPPDATA "Programs\$Antigo") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$Antigo" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $ChaveRun -Name $Antigo -ErrorAction SilentlyContinue
    Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) "$Antigo.lnk") -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path ([Environment]::GetFolderPath('SendTo')) 'iPhone (Continuidade).lnk') -Force -ErrorAction SilentlyContinue
    Ok 'Ok (ajustes e pareamento são mantidos)'
}

Passo "Compilando e instalando em $Destino"
$temp = Join-Path $env:TEMP "$App-publicacao"
if (Test-Path $temp) { Remove-Item $temp -Recurse -Force }
dotnet publish $Projeto -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $temp --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'A compilação falhou. Veja o erro acima.' }

New-Item -ItemType Directory -Force -Path $Destino | Out-Null
Get-ChildItem $Destino -Exclude 'desinstalar.ps1' -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item "$temp\*" $Destino -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'desinstalar.ps1') $Destino -Force
Copy-Item (Join-Path $PSScriptRoot 'Native\Assets\Native.ico') $Destino -Force
Remove-Item $temp -Recurse -Force
Ok 'Ok'

# ---------------------------------------------------------------- atalhos e registro
Passo 'Criando atalho no Menu Iniciar e registrando o app'
$menu = Join-Path ([Environment]::GetFolderPath('Programs')) "$App.lnk"
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($menu)
$lnk.TargetPath = $Exe
$lnk.WorkingDirectory = $Destino
$lnk.IconLocation = (Join-Path $Destino 'Native.ico')
$lnk.Description = 'iPhone no Windows'
$lnk.Save()

# "Enviar para > iPhone (Native)" no menu do botão direito do Explorer
$sendTo = Join-Path ([Environment]::GetFolderPath('SendTo')) 'iPhone (Native).lnk'
$lnk = $shell.CreateShortcut($sendTo)
$lnk.TargetPath = $Exe
$lnk.Arguments = '--send'
$lnk.WorkingDirectory = $Destino
$lnk.IconLocation = (Join-Path $Destino 'Native.ico')
$lnk.Description = 'Mandar para o iPhone pelo Native'
$lnk.Save()

$versao = (Get-Item $Exe).VersionInfo.ProductVersion
$tamanhoKb = [int]((Get-ChildItem $Destino -Recurse | Measure-Object Length -Sum).Sum / 1KB)
New-Item -Path $ChaveUninst -Force | Out-Null
$valores = @{
    DisplayName     = $App
    DisplayVersion  = $versao
    Publisher       = 'Lucas Jussen'
    DisplayIcon     = (Join-Path $Destino 'Native.ico')
    InstallLocation = $Destino
    UninstallString = "powershell -NoProfile -ExecutionPolicy Bypass -File `"$Destino\desinstalar.ps1`""
    QuietUninstallString = "powershell -NoProfile -ExecutionPolicy Bypass -File `"$Destino\desinstalar.ps1`" -Silencioso"
    URLInfoAbout    = 'https://github.com/lucasjussen'
}
foreach ($k in $valores.Keys) { Set-ItemProperty -Path $ChaveUninst -Name $k -Value $valores[$k] }
Set-ItemProperty -Path $ChaveUninst -Name EstimatedSize -Value $tamanhoKb -Type DWord
Set-ItemProperty -Path $ChaveUninst -Name NoModify -Value 1 -Type DWord
Set-ItemProperty -Path $ChaveUninst -Name NoRepair -Value 1 -Type DWord

# Se "Abrir junto com o Windows" já estava ligado (de uma versão antiga), aponta para a instalação nova.
if ($abrirComWindows -or (Get-ItemProperty -Path $ChaveRun -Name $App -ErrorAction SilentlyContinue)) {
    Set-ItemProperty -Path $ChaveRun -Name $App -Value "`"$Exe`" --minimized"
}
Ok 'Ok'

# ---------------------------------------------------------------- firewall (compartilhamento de arquivos)
Passo 'Liberando o compartilhamento de arquivos na rede de casa'
$regra = 'Native (arquivos do iPhone)'
$existe = Get-NetFirewallRule -DisplayName $regra -ErrorAction SilentlyContinue |
    Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue | Where-Object { $_.Program -eq $Exe }
if ($existe) {
    Ok 'Já liberado'
} else {
    Write-Host '    O Windows vai pedir permissão de administrador (só para esta regra, só redes privadas).'
    $cmd = "Remove-NetFirewallRule -DisplayName '$regra', 'Continuidade (arquivos do iPhone)' -ErrorAction SilentlyContinue; " +
           "New-NetFirewallRule -DisplayName '$regra' -Direction Inbound -Program '$Exe' -Action Allow -Profile Private,Domain " +
           "-Description 'Deixa o iPhone mandar e buscar arquivos no Native pela rede local.' | Out-Null"
    try {
        Start-Process powershell -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList '-NoProfile', '-Command', $cmd
        Ok 'Ok'
    } catch {
        Write-Host '    Pulado. Quando o Windows perguntar, permita o Native em redes privadas.' -ForegroundColor Yellow
    }
}

# ---------------------------------------------------------------- pronto
Passo 'Abrindo o Native'
Start-Process $Exe
Write-Host ""
Write-Host "  Pronto! Versão $versao instalada." -ForegroundColor Green
Write-Host '  Procure "Native" no Menu Iniciar. Ele fica no ícone da bandeja, perto do relógio.'
Write-Host '  Atalho: Ctrl + Alt + N abre e fecha de qualquer lugar.'
Write-Host '  Arquivos: arraste para a janela, ou botão direito > Enviar para > iPhone (Native).'
Write-Host '  Para desinstalar: Configurações > Aplicativos > Aplicativos instalados > Native.'
Write-Host ""

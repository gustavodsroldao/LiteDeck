<#
.SYNOPSIS
    Instala o LiteDeck em %ProgramFiles%\LiteDeck, cria o atalho no Menu Iniciar,
    registra a tarefa agendada de inicialização e abre o app na bandeja.
.PARAMETER Exe
    Caminho do LiteDeck.exe. Padrão: ao lado deste script ou em .\dist.
#>
param(
    [string]$Exe,
    [string]$User = "$env:USERDOMAIN\$env:USERNAME"   # usuário que fez login (preservado ao elevar)
)
$ErrorActionPreference = 'Stop'

if (-not $Exe) {
    $Exe = @("$PSScriptRoot\LiteDeck.exe", "$PSScriptRoot\dist\LiteDeck.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $Exe) { throw "LiteDeck.exe não encontrado. Rode .\publish.ps1 antes ou passe -Exe <caminho>." }
}
$Exe = (Resolve-Path $Exe).Path

# ---- elevação (UAC) ----
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Exe', "`"$Exe`"", '-User', "`"$User`"")
    $p = Start-Process powershell.exe -Verb RunAs -ArgumentList $argList -Wait -PassThru
    exit $p.ExitCode
}

$TaskName   = 'LiteDeck'
$InstallDir = Join-Path $env:ProgramFiles 'LiteDeck'
$Target     = Join-Path $InstallDir 'LiteDeck.exe'
$Shortcut   = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\LiteDeck.lnk'

try {
    # encerra uma instância anterior para poder sobrescrever o exe
    Get-Process LiteDeck -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $InstallDir 'LiteDeck.exe') } | Stop-Process -Force   # só a cópia instalada
    Start-Sleep -Milliseconds 500

    New-Item -ItemType Directory -Force $InstallDir | Out-Null
    Copy-Item $Exe $Target -Force
    Write-Host "Copiado para $Target"

    $sh = (New-Object -ComObject WScript.Shell).CreateShortcut($Shortcut)
    $sh.TargetPath = $Target
    $sh.WorkingDirectory = $InstallDir
    $sh.IconLocation = "$Target,0"
    $sh.Description = 'LiteDeck: teclado, ventoinhas e sensores'
    $sh.Save()
    Write-Host "Atalho criado: $Shortcut"

    # Tarefa ao fazer logon, com privilégio máximo (abre sem pedir UAC).
    # Mesmo nome que o app usa na opção "Iniciar com o Windows"; aqui também libera rodar na bateria e sem limite de tempo.
    $action    = New-ScheduledTaskAction -Execute $Target -Argument '--tray' -WorkingDirectory $InstallDir
    $trigger   = New-ScheduledTaskTrigger -AtLogOn -User $User
    $principal = New-ScheduledTaskPrincipal -UserId $User -LogonType Interactive -RunLevel Highest
    $settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
    Write-Host "Tarefa agendada '$TaskName' registrada para $User"

    Start-ScheduledTask -TaskName $TaskName
    Write-Host 'LiteDeck iniciado na bandeja.'
}
catch {
    Write-Host "Erro: $_" -ForegroundColor Red
    Read-Host 'Enter para fechar'
    exit 1
}

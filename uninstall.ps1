<#
.SYNOPSIS
    Remove o LiteDeck: encerra o processo, apaga a tarefa agendada, o atalho e a pasta de instalação.
.PARAMETER Purge
    Também apaga as configurações salvas (%AppData%\LiteDeck). Sem ele, settings.json é mantido.
#>
param(
    [switch]$Purge,
    [string]$DataDir = (Join-Path $env:APPDATA 'LiteDeck')   # AppData do usuário que fez login (preservado ao elevar)
)
$ErrorActionPreference = 'Stop'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-DataDir', "`"$DataDir`"")
    if ($Purge) { $argList += '-Purge' }
    $p = Start-Process powershell.exe -Verb RunAs -ArgumentList $argList -Wait -PassThru
    exit $p.ExitCode
}

$TaskName   = 'LiteDeck'
$InstallDir = Join-Path $env:ProgramFiles 'LiteDeck'
$Shortcut   = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\LiteDeck.lnk'

try {
    Get-Process LiteDeck -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $InstallDir 'LiteDeck.exe') } | Stop-Process -Force   # só a cópia instalada
    Start-Sleep -Milliseconds 500

    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Host "Tarefa '$TaskName' removida"
    }
    if (Test-Path $Shortcut)   { Remove-Item $Shortcut -Force; Write-Host 'Atalho removido' }
    if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force; Write-Host "Pasta removida: $InstallDir" }

    if ($Purge) {
        if (Test-Path $DataDir) { Remove-Item $DataDir -Recurse -Force; Write-Host "Configurações removidas: $DataDir" }
    }
    elseif (Test-Path $DataDir) { Write-Host "Configurações mantidas em $DataDir (use -Purge para apagar)" }

    Write-Host 'LiteDeck desinstalado.'
}
catch {
    Write-Host "Erro: $_" -ForegroundColor Red
    Read-Host 'Enter para fechar'
    exit 1
}

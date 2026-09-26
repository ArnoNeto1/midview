# Installs Midview on this Windows machine with the lab installer, then
# updates it over the network for real -- the Agent service, the service
# control manager, the switch between builds, the restarts: what the unit
# tests fake -- and finally reinstalls and uninstalls it with the USB
# scripts. Run by .github/workflows/atualizacao.yml on a throwaway runner,
# as administrator, under Windows PowerShell 5.1:
#
#   powershell -ExecutionPolicy Bypass -File installer\tests\Testar-Atualizacao.ps1 `
#       -Pacote <built package> -Ferramenta <Midview.E2E.exe> [-Registros <folder>]
#
# It changes this machine (installs the MidviewAgent service, then removes
# it): never run it on a machine that matters.
#
# ASCII-only, like the other scripts run straight from the repository: 5.1
# reads a .ps1 without a BOM as ANSI.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Pacote,
    [Parameter(Mandatory = $true)]
    [string]$Ferramenta,
    [string]$Registros
)

$ErrorActionPreference = 'Stop'
$Pacote = (Resolve-Path -LiteralPath $Pacote).ProviderPath
$work = Join-Path ([IO.Path]::GetTempPath()) ('midview-atualizacao-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $work | Out-Null
if (-not $Registros) {
    $Registros = Join-Path $work 'registros'
}
New-Item -ItemType Directory -Path $Registros -Force | Out-Null

$teacher = Join-Path $work 'professor.pfx'
$usbExe = Join-Path $env:ProgramFiles 'Midview\Midview.Agent.exe'
$versions = Join-Path $env:ProgramFiles 'Midview.update'
$data = Join-Path $env:ProgramData 'Midview'
$stateFolder = Join-Path $data 'update'
$recoveryTask = "Midview - Recuperar atualiza$([char]0x00E7)$([char]0x00E3)o"

function Step([string]$What) {
    Write-Host ''
    Write-Host "==> $What" -ForegroundColor Cyan
}

function Check([bool]$Condition, [string]$What) {
    if (-not $Condition) {
        throw "FALHA: $What"
    }
    Write-Host "ok    $What"
}

function Invoke-Tool([string[]]$Arguments) {
    & $Ferramenta @Arguments | Out-Host
    return $LASTEXITCODE
}

function Invoke-Script([string]$Name, [string[]]$Arguments) {
    $path = Join-Path $Pacote "programa\scripts\$Name"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $path @Arguments | Out-Host
    return $LASTEXITCODE
}

function Get-Service-Config {
    Get-CimInstance -ClassName Win32_Service -Filter "Name='MidviewAgent'"
}

function Test-RecoveryTask {
    $null -ne (Get-ScheduledTask -TaskName $recoveryTask -ErrorAction SilentlyContinue)
}

# A copy of the package's student program under another build label (so
# another fingerprint), optionally changed.
function New-Build([string]$Name, [string]$Label, [scriptblock]$Change) {
    $target = Join-Path $work $Name
    New-Item -ItemType Directory -Path (Join-Path $target 'programa') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $Pacote 'programa\aluno') -Destination (Join-Path $target 'programa\aluno') -Recurse
    $manifest = Join-Path $target 'programa\aluno\midview-build.json'
    $text = [IO.File]::ReadAllText($manifest) -replace '"build": "[^"]*"', ('"build": "' + $Label + '"')
    [IO.File]::WriteAllText($manifest, $text, (New-Object Text.UTF8Encoding $false))
    if ($Change) {
        & $Change (Join-Path $target 'programa\aluno') | Out-Null
    }
    return $target
}

function Save-Logs([string]$Suffix) {
    foreach ($file in @((Join-Path $data 'agent.log'), (Join-Path $stateFolder 'update-state.json'))) {
        if (Test-Path -LiteralPath $file) {
            Copy-Item -LiteralPath $file -Destination (Join-Path $Registros ("$Suffix-" + (Split-Path -Leaf $file))) -Force
        }
    }
}

$failed = $false
try {
    Step 'Autorizar um professor de teste no pacote'
    Check ((Invoke-Tool @('authorize', $Pacote, $teacher)) -eq 0) 'the test teacher is authorized in the package'

    Step 'Instalar pelo pendrive (Instalar-Aluno)'
    Check ((Invoke-Script 'Instalar-Aluno.ps1' @('-Pacote', $Pacote)) -eq 0) 'Instalar-Aluno finishes'
    $service = Get-Service-Config
    Check ($service.State -eq 'Running') "the service runs ($($service.State))"
    Check ($service.PathName -eq "`"$usbExe`"") "from the installed folder ($($service.PathName))"
    Check ((Invoke-Tool @('status', $Pacote, $teacher, $Pacote)) -eq 0) "the Agent reports the package's build"
    Check (Test-Path -LiteralPath $stateFolder) 'the Agent made its update folder'
    $open = @((Get-Acl -LiteralPath $stateFolder).GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) |
            Where-Object { $_.IdentityReference.Value -notin @('S-1-5-18', 'S-1-5-32-544') })
    Check ($open.Count -eq 0) "only SYSTEM and Administrators can open it ($(($open | ForEach-Object { $_.IdentityReference.Value }) -join ', '))"

    Step 'Atualizar pela rede para uma versao nova (tem que confirmar)'
    $good = New-Build 'versao-boa' '2099-01-01 00:00 e2e-boa' $null
    Check ((Invoke-Tool @('push', $Pacote, $good, $teacher, 'committed')) -eq 0) 'the new build is committed and running'
    $service = Get-Service-Config
    Check ($service.State -eq 'Running') "the service runs ($($service.State))"
    Check ($service.PathName -like "`"$versions\*\Midview.Agent.exe`"") "from Midview.update ($($service.PathName))"
    $goodPath = $service.PathName
    $goodFolder = Split-Path -Parent $goodPath.Trim('"')
    Check (Test-Path -LiteralPath (Join-Path $goodFolder 'Midview.Session.exe')) 'the Session is next to the new Agent'
    Check (-not (Test-RecoveryTask)) 'the boot recovery task is gone'
    Check (Test-Path -LiteralPath $usbExe) 'the previous build is kept'

    Step 'Atualizar para uma versao quebrada (tem que voltar sozinha)'
    $broken = New-Build 'versao-quebrada' '2099-01-02 00:00 e2e-quebrada' {
        param($folder)
        [IO.File]::WriteAllText((Join-Path $folder 'Midview.Agent.dll'), 'isto nao e uma dll')
    }
    Check ((Invoke-Tool @('push', $Pacote, $broken, $teacher, 'rolledBack')) -eq 0) 'the broken build is rolled back'
    $service = Get-Service-Config
    Check ($service.State -eq 'Running') "the service runs ($($service.State))"
    Check ($service.PathName -eq $goodPath) "on the build before it ($($service.PathName))"
    Check ((Invoke-Tool @('status', $Pacote, $teacher, $good)) -eq 0) 'the Agent reports the good build again'
    Check (-not (Test-RecoveryTask)) 'the boot recovery task is gone'

    Step 'Reinstalar pelo pendrive por cima da versao da rede'
    Save-Logs 'antes-de-reinstalar'
    Check ((Invoke-Script 'Instalar-Aluno.ps1' @('-Pacote', $Pacote)) -eq 0) 'Instalar-Aluno finishes'
    $service = Get-Service-Config
    Check ($service.State -eq 'Running') "the service runs ($($service.State))"
    Check ($service.PathName -eq "`"$usbExe`"") "from the installed folder again ($($service.PathName))"
    Check (-not (Test-Path -LiteralPath $versions)) 'Midview.update is gone'
    Check (-not (Test-Path -LiteralPath (Join-Path $stateFolder 'update-state.json'))) 'the update record is gone'
    Check ((Invoke-Tool @('status', $Pacote, $teacher, $Pacote)) -eq 0) "the Agent reports the package's build"

    Step 'Desinstalar (Desinstalar-Aluno -RemoverDados)'
    Save-Logs 'antes-de-desinstalar'
    Check ((Invoke-Script 'Desinstalar-Aluno.ps1' @('-RemoverDados')) -eq 0) 'Desinstalar-Aluno finishes'
    Check ($null -eq (Get-Service-Config)) 'the service is gone'
    Check (-not (Test-Path -LiteralPath (Split-Path -Parent $usbExe))) 'the program is gone'
    Check (-not (Test-Path -LiteralPath $versions)) 'Midview.update is gone'
    Check (-not (Test-Path -LiteralPath $data)) 'the data folder is gone'
}
catch {
    $failed = $true
    Write-Host ''
    Write-Host $_.Exception.Message -ForegroundColor Red
    Save-Logs 'falha'
    $log = Join-Path $data 'agent.log'
    if (Test-Path -LiteralPath $log) {
        Write-Host ''
        Write-Host '--- agent.log (end) ---'
        Get-Content -LiteralPath $log -Tail 80 | Out-Host
    }
}

if ($failed) {
    exit 1
}
Write-Host ''
Write-Host 'Network update checked on a real Windows machine.' -ForegroundColor Green
exit 0

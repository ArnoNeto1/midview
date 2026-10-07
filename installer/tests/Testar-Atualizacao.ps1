# Installs Midview on this Windows machine with the setup program (silently,
# /TIPO=aluno), pairs a test teacher with it for real (the code on the
# student's screen included), updates it over the network for real -- the
# Agent service, the service control manager, the switch between builds,
# the restarts: what the unit tests fake -- and finally reinstalls it with
# the setup and uninstalls it. Run by .github/workflows/atualizacao.yml on a
# throwaway runner, as administrator, under Windows PowerShell 5.1:
#
#   powershell -ExecutionPolicy Bypass -File installer\tests\Testar-Atualizacao.ps1 `
#       -Pacote <Gerar-Instalador output> -Ferramenta <Midview.E2E.exe> [-Registros <folder>]
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
$machine = Join-Path $work 'maquina.json'
$setup = @(Get-ChildItem -LiteralPath $Pacote -Filter 'MidviewSetup.exe' | Select-Object -First 1 | ForEach-Object { $_.FullName })[0]
$studentProgram = Join-Path $Pacote 'programa\aluno'
$installedExe = Join-Path $env:ProgramFiles 'Midview\Midview.Agent.exe'
$uninstaller = Join-Path $env:ProgramFiles 'Midview\unins000.exe'
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

function Invoke-Setup([string[]]$Arguments) {
    $process = Start-Process -FilePath $setup -ArgumentList $Arguments -Wait -PassThru
    return $process.ExitCode
}

# Silently, the way an unattended uninstall runs: it keeps the paired
# teachers (C:\ProgramData\Midview), which the caller then removes.
function Invoke-Uninstall([string]$Log) {
    $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', "/LOG=$Log") -Wait -PassThru
    return $process.ExitCode
}

function Get-Service-Config {
    Get-CimInstance -ClassName Win32_Service -Filter "Name='MidviewAgent'"
}

function Test-RecoveryTask {
    $null -ne (Get-ScheduledTask -TaskName $recoveryTask -ErrorAction SilentlyContinue)
}

# A copy of the setup's student program under another build label (so
# another fingerprint), optionally changed.
function New-Build([string]$Name, [string]$Label, [scriptblock]$Change) {
    $target = Join-Path $work $Name
    Copy-Item -LiteralPath $studentProgram -Destination $target -Recurse
    $manifest = Join-Path $target 'midview-build.json'
    $text = [IO.File]::ReadAllText($manifest) -replace '"build": "[^"]*"', ('"build": "' + $Label + '"')
    [IO.File]::WriteAllText($manifest, $text, (New-Object Text.UTF8Encoding $false))
    if ($Change) {
        & $Change $target | Out-Null
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
    Step 'Instalar com o instalador (aluno, silencioso)'
    Check ($null -ne $setup) "the setup is in $Pacote"
    Check ((Invoke-Setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/TIPO=aluno', "/LOG=$Registros\instalar.log")) -eq 0) 'the setup finishes'
    $service = Get-Service-Config
    Check ($service.State -eq 'Running') "the service runs ($($service.State))"
    Check ($service.PathName -eq "`"$installedExe`"") "from the installed folder ($($service.PathName))"
    Check ((Invoke-Setup @('/VERYSILENT', '/SUPPRESSMSGBOXES')) -ne 0) 'a silent install without /TIPO is refused'
    Check ((Invoke-Setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/TIPO=professor')) -ne 0) 'installing the other kind is refused'
    Check (-not (Test-Path -LiteralPath (Join-Path $env:ProgramFiles 'Midview Professor'))) 'and nothing of it was installed'

    Step 'Parear um professor de teste (o codigo aparece na tela do aluno)'
    Check ((Invoke-Tool @('pair', $teacher, $machine)) -eq 0) 'the test teacher pairs with the machine'
    Check ((Invoke-Tool @('status', $machine, $teacher, $studentProgram)) -eq 0) "the Agent reports the setup's build to the paired teacher"
    Check (Test-Path -LiteralPath $stateFolder) 'the Agent made its update folder'
    $open = @((Get-Acl -LiteralPath $stateFolder).GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) |
            Where-Object { $_.IdentityReference.Value -notin @('S-1-5-18', 'S-1-5-32-544') })
    Check ($open.Count -eq 0) "only SYSTEM and Administrators can open it ($(($open | ForEach-Object { $_.IdentityReference.Value }) -join ', '))"

    Step 'Atualizar pela rede para uma versao nova (tem que confirmar)'
    $good = New-Build 'versao-boa' '2099-01-01 00:00 e2e-boa' $null
    Check ((Invoke-Tool @('push', $machine, $good, $teacher, 'committed')) -eq 0) 'the new build is committed and running'
    $service = Get-Service-Config
    Check ($service.State -eq 'Running') "the service runs ($($service.State))"
    Check ($service.PathName -like "`"$versions\*\Midview.Agent.exe`"") "from Midview.update ($($service.PathName))"
    $goodPath = $service.PathName
    $goodFolder = Split-Path -Parent $goodPath.Trim('"')
    Check (Test-Path -LiteralPath (Join-Path $goodFolder 'Midview.Session.exe')) 'the Session is next to the new Agent'
    Check (-not (Test-RecoveryTask)) 'the boot recovery task is gone'
    Check (Test-Path -LiteralPath $installedExe) 'the previous build is kept'

    Step 'Atualizar para uma versao quebrada (tem que voltar sozinha)'
    $broken = New-Build 'versao-quebrada' '2099-01-02 00:00 e2e-quebrada' {
        param($folder)
        [IO.File]::WriteAllText((Join-Path $folder 'Midview.Agent.dll'), 'isto nao e uma dll')
    }
    Check ((Invoke-Tool @('push', $machine, $broken, $teacher, 'rolledBack')) -eq 0) 'the broken build is rolled back'
    $service = Get-Service-Config
    Check ($service.State -eq 'Running') "the service runs ($($service.State))"
    Check ($service.PathName -eq $goodPath) "on the build before it ($($service.PathName))"
    Check ((Invoke-Tool @('status', $machine, $teacher, $good)) -eq 0) 'the Agent reports the good build again'
    Check (-not (Test-RecoveryTask)) 'the boot recovery task is gone'

    Step 'Reinstalar com o instalador por cima da versao da rede'
    Save-Logs 'antes-de-reinstalar'
    Check ((Invoke-Setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/TIPO=aluno', "/LOG=$Registros\reinstalar.log")) -eq 0) 'the setup finishes'
    $service = Get-Service-Config
    Check ($service.State -eq 'Running') "the service runs ($($service.State))"
    Check ($service.PathName -eq "`"$installedExe`"") "from the installed folder again ($($service.PathName))"
    Check (-not (Test-Path -LiteralPath $versions)) 'Midview.update is gone'
    Check (-not (Test-Path -LiteralPath (Join-Path $stateFolder 'update-state.json'))) 'the update record is gone'
    Check ((Invoke-Tool @('status', $machine, $teacher, $studentProgram)) -eq 0) 'the teacher is still paired after reinstalling'

    Step 'Desinstalar (silencioso)'
    Save-Logs 'antes-de-desinstalar'
    Check ((Invoke-Uninstall "$Registros\desinstalar.log") -eq 0) 'the uninstaller finishes'
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Service-Config) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
    }
    Check ($null -eq (Get-Service-Config)) 'the service is gone'
    Check (-not (Test-Path -LiteralPath (Split-Path -Parent $installedExe))) 'the program is gone'
    Check (-not (Test-Path -LiteralPath $versions)) 'Midview.update is gone'
    Check (Test-Path -LiteralPath (Join-Path $data 'trusted-masters.json')) 'a silent uninstall keeps the paired teachers'
    Remove-Item -LiteralPath $data -Recurse -Force }
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

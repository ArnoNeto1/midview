# Installs Midview on this Windows machine with the setup program, pairs a
# test teacher with it and goes through every function of "Painel do
# laboratorio" for real: what each one writes (Chrome's and the other
# browsers' policies, the wallpaper and taskbar locks, the student's own
# hive, the Desktop's permissions), that Edge is closed, that the machine
# keeps them across a service restart, and that turning them off, "RESET
# geral" and uninstalling leave nothing behind. Meant for a throwaway
# machine (Windows Sandbox, or a CI runner), as administrator, under
# Windows PowerShell 5.1:
#
#   powershell -ExecutionPolicy Bypass -File installer\tests\Testar-Painel.ps1 `
#       -Pacote <Gerar-Instalador output> -Ferramenta <Midview.E2E.exe> [-Registros <folder>]
#
# It changes this machine: never run it on a machine that matters.
#
# Whether Chrome then honours each policy is Chrome's part (the policies'
# names and values are the ones Google documents); this checks Midview's.
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
$work = Join-Path ([IO.Path]::GetTempPath()) ('midview-painel-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $work | Out-Null
if (-not $Registros) {
    $Registros = Join-Path $work 'registros'
}
New-Item -ItemType Directory -Path $Registros -Force | Out-Null

$teacher = Join-Path $work 'professor.pfx'
$machine = Join-Path $work 'maquina.json'
$setup = @(Get-ChildItem -LiteralPath $Pacote -Filter 'MidviewSetup.exe' | Select-Object -First 1 | ForEach-Object { $_.FullName })[0]
$uninstaller = Join-Path $env:ProgramFiles 'Midview\unins000.exe'
$data = Join-Path $env:ProgramData 'Midview'
$log = Join-Path $data 'agent.log'
$all = @('clean', 'lockdown', 'downloads', 'guest', 'adblock', 'edge', 'taskbar', 'desktop')
$sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$desktop = [Environment]::GetFolderPath('Desktop')
$script:checks = 0

$chrome = 'HKLM:\SOFTWARE\Policies\Google\Chrome'
$explorer = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer'
$wallpaper = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop'
$firefox = 'HKLM:\SOFTWARE\Policies\Mozilla\Firefox'
$studentExplorer = "Registry::HKEY_USERS\$sid\Software\Policies\Microsoft\Windows\Explorer"
$otherBrowsers = @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'HKLM:\SOFTWARE\Policies\Chromium')

function Step([string]$What) {
    Write-Host ''
    Write-Host "==> $What" -ForegroundColor Cyan
}

function Check([bool]$Condition, [string]$What) {
    if (-not $Condition) {
        throw "FALHA: $What"
    }
    $script:checks++
    Write-Host "ok    $What"
}

function Invoke-Tool([string[]]$Arguments) {
    $output = & $Ferramenta @Arguments
    $code = $LASTEXITCODE
    $output | Out-Host
    return [pscustomobject]@{ Code = $code; Output = (@($output) -join ' ') }
}

function Invoke-Setup([string[]]$Arguments) {
    (Start-Process -FilePath $setup -ArgumentList $Arguments -Wait -PassThru).ExitCode
}

function Get-ServiceState {
    (Get-CimInstance -ClassName Win32_Service -Filter "Name='MidviewAgent'").State
}

# A registry value, or $null when the key or the value is not there.
function Get-Value([string]$Key, [string]$Name) {
    $item = Get-ItemProperty -LiteralPath $Key -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return $null
    }
    return $item.$Name
}

# The values of a list key (1, 2, 3...), or an empty list.
function Get-List([string]$Key) {
    if (-not (Test-Path -LiteralPath $Key)) {
        return @()
    }
    $item = Get-Item -LiteralPath $Key
    return @($item.GetValueNames() | ForEach-Object { $item.GetValue($_) })
}

# The Deny entries on the Desktop folder ("Travar a Area de Trabalho"),
# as text: compared with the ones the machine had before Midview.
function Get-DesktopDenies {
    @((Get-Acl -LiteralPath $desktop).Access | Where-Object { $_.AccessControlType -eq 'Deny' } |
        ForEach-Object { "$($_.IdentityReference) $($_.FileSystemRights) $($_.InheritanceFlags)" })
}

function Set-Lab([string[]]$Functions) {
    $result = Invoke-Tool (@('lab', $machine, $teacher) + $Functions)
    return $result
}

# The Agent's failures written since the given line count (every line of
# agent.log is WARN, the normal ones too: these are the ones saying something
# could not be done).
function Get-NewWarnings([int]$From) {
    if (-not (Test-Path -LiteralPath $log)) {
        return @()
    }
    @(Get-Content -LiteralPath $log | Select-Object -Skip $From |
        Where-Object { $_ -match "Couldn't|could not|failed|Failed|exception|Exception|denied|Denied" })
}

function Get-LogLength {
    if (Test-Path -LiteralPath $log) { return @(Get-Content -LiteralPath $log).Count }
    return 0
}

function Check-AllOn {
    Check ((Get-List "$chrome\ClearBrowsingDataOnExitList").Count -eq 5) 'Passo 2: Chrome clears 5 kinds of data on exit'
    Check ((Get-Value $chrome 'IncognitoModeAvailability') -eq 1) 'Passo 3: incognito is off'
    Check ((Get-Value $chrome 'BrowserSignin') -eq 0) 'Passo 3: Chrome sign-in is off'
    Check ((Get-Value $chrome 'BrowserAddPersonEnabled') -eq 0) 'Passo 3: no new Chrome profiles'
    Check ((Get-List "$chrome\URLBlocklist") -contains 'chrome://settings') 'Passo 3: chrome://settings is blocked'
    Check ((Get-List "$chrome\ExtensionInstallBlocklist") -contains '*') 'Passo 3: other extensions are blocked'
    Check ((Get-Value $chrome 'HomepageLocation') -eq 'https://www.google.com.br') 'Passo 3: the home page is set'
    Check ((Get-Value $wallpaper 'NoChangingWallPaper') -eq 1) 'Passo 3: the wallpaper is locked'
    Check ((Get-Value $chrome 'DownloadRestrictions') -eq 3) 'Travar downloads: Chrome blocks every download'
    foreach ($browser in $otherBrowsers) {
        Check ((Get-Value $browser 'DownloadRestrictions') -eq 3) "Travar downloads: $browser blocks every download"
    }
    Check ($null -ne (Get-Value $firefox 'DownloadDirectory')) 'Travar downloads: Firefox saves only to a locked folder'
    Check ($null -eq (Get-Value $chrome 'DownloadRestriction')) 'Travar downloads: the misnamed value from before 0.0.48 is not there'
    Check ((Get-Value $chrome 'BrowserGuestModeEnforced') -eq 1 -and (Get-Value $chrome 'BrowserGuestModeEnabled') -eq 1) 'Modo visitante: Chrome opens as a guest'
    Check (((Get-List "$chrome\ExtensionInstallForcelist") -join ' ') -match 'cfhdojbkjhnklbpkdaibdccddilifddb') 'Passo 1: the ad blocker is force-installed'
    Check ($null -ne (Get-Value "$chrome\ExtensionSettings" 'cfhdojbkjhnklbpkdaibdccddilifddb')) 'Passo 1: its settings are there'
    Check ((Get-Value $explorer 'NoPinningToTaskbar') -eq 1) 'Travar a barra de tarefas: the machine-wide pin block'
    Check ((Get-Value $studentExplorer 'NoPinningToTaskbar') -eq 1) "Travar a barra de tarefas: the pin block in the student's own hive"
    Check ((Get-DesktopDenies).Count -gt $script:denies.Count) 'Travar a Area de Trabalho: the Desktop denies creating and deleting'
}

function Check-AllOff([string]$After) {
    Check (-not (Test-Path -LiteralPath "$chrome\ClearBrowsingDataOnExitList")) "$After - no clean-on-exit list"
    foreach ($name in @('IncognitoModeAvailability', 'BrowserSignin', 'BrowserAddPersonEnabled', 'HomepageLocation', 'DownloadRestrictions',
            'BrowserGuestModeEnforced', 'BrowserGuestModeEnabled')) {
        Check ($null -eq (Get-Value $chrome $name)) "$After - Chrome's $name is gone"
    }
    foreach ($list in @('URLBlocklist', 'ExtensionInstallBlocklist', 'ExtensionInstallForcelist', 'ExtensionSettings')) {
        Check (-not (Test-Path -LiteralPath "$chrome\$list")) "$After - Chrome's $list is gone"
    }
    Check ($null -eq (Get-Value $wallpaper 'NoChangingWallPaper')) "$After - the wallpaper is free"
    foreach ($browser in $otherBrowsers) {
        Check ($null -eq (Get-Value $browser 'DownloadRestrictions')) "$After - $browser downloads are free"
    }
    Check ($null -eq (Get-Value $firefox 'DownloadDirectory')) "$After - Firefox downloads are free"
    Check ($null -eq (Get-Value $explorer 'NoPinningToTaskbar')) "$After - the machine-wide pin block is gone"
    Check ($null -eq (Get-Value $studentExplorer 'NoPinningToTaskbar')) "$After - the student's pin block is gone"
    Check (((Get-DesktopDenies) -join '|') -eq ($script:denies -join '|')) "$After - the Desktop is as it was before Midview"
}

# Edge is closed by the Agent's program blocking: it says so in its log.
function Check-EdgeEnded([string]$What) {
    $edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    Check ($null -ne $edge) 'Edge is installed on this machine'
    $before = Get-LogLength
    Invoke-Tool @('launch-raw', $edge, '--no-first-run', 'about:blank') | Out-Null
    $limit = (Get-Date).AddSeconds(25)
    $ended = $false
    while ((Get-Date) -lt $limit -and -not $ended) {
        Start-Sleep -Seconds 1
        $ended = @(Get-Content -LiteralPath $log | Select-Object -Skip $before | Where-Object { $_ -like '*Program blocking ended msedge.exe*' }).Count -gt 0
    }
    Get-Process -Name 'msedge' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Check $ended $What
}

$failed = $false
try {
    Step 'Instalar com o instalador e parear um professor de teste'
    Check ($null -ne $setup) "the setup is in $Pacote"
    Check ((Invoke-Setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/TIPO=aluno', "/LOG=$Registros\instalar.log")) -eq 0) 'the setup finishes'
    Check ((Get-ServiceState) -eq 'Running') 'the service runs'
    Check ((Invoke-Tool @('pair', $teacher, $machine)).Code -eq 0) 'the test teacher pairs with the machine'
    Check ((Invoke-Tool @('lab-status', $machine, $teacher)).Code -eq 0) 'the Agent knows the lab panel'
    $script:denies = @(Get-DesktopDenies)
    Write-Host "The Desktop's own Deny entries before Midview: $($script:denies.Count)"
    $script:denies | Out-Host
    Check-AllOff 'Before anything'

    Step 'Ativar tudo de uma vez'
    $start = Get-LogLength
    $answer = Set-Lab $all
    Check ($answer.Code -eq 0) 'the panel with every function is accepted'
    Check ($answer.Output -notmatch 'rea de Trabalho:') 'the Desktop could be locked (no problem reported)'
    Check-AllOn
    $status = Invoke-Tool @('lab-status', $machine, $teacher)
    Check ($status.Output -match 'clean=True lockdown=True downloads=True guest=True adblock=True edge=True taskbar=True desktop=True') 'the machine reports every function on'
    Check-EdgeEnded 'Passo 4: Edge is closed when the student opens it'
    $warnings = Get-NewWarnings $start
    $warnings | Out-Host
    Check ($warnings.Count -eq 0) 'the Agent reported no failure while applying'

    Step 'Continua valendo depois de reiniciar o servico'
    Restart-Service -Name 'MidviewAgent'
    Start-Sleep -Seconds 10
    Check ((Get-ServiceState) -eq 'Running') 'the service is back'
    Check-AllOn
    Check-EdgeEnded 'Edge is still closed'

    Step 'Uma funcao de cada vez'
    foreach ($one in $all) {
        $answer = Set-Lab @($one)
        Check ($answer.Code -eq 0) "only ${one}: accepted"
    }
    Check ($null -eq (Get-Value $explorer 'NoPinningToTaskbar')) 'after the last one alone (desktop), the taskbar lock is off again'
    Check ((Get-DesktopDenies).Count -gt $script:denies.Count) 'and the Desktop is locked'

    Step 'Desativar tudo'
    $start = Get-LogLength
    Check ((Set-Lab @()).Code -eq 0) 'the panel with nothing on is accepted'
    Check-AllOff 'Desativar tudo'
    $warnings = Get-NewWarnings $start
    $warnings | Out-Host
    Check ($warnings.Count -eq 0) 'the Agent reported no failure while removing'

    Step 'RESET geral'
    Check ((Set-Lab $all).Code -eq 0) 'everything on again'
    Check-AllOn
    Check ((Invoke-Tool @('lab-reset', $machine, $teacher)).Code -eq 0) 'RESET geral is accepted'
    Check-AllOff 'RESET geral'
    $status = Invoke-Tool @('lab-status', $machine, $teacher)
    Check ($status.Output -match 'clean=False lockdown=False downloads=False guest=False adblock=False edge=False taskbar=False desktop=False') 'the machine reports everything off'

    Step 'Desinstalar com tudo ativado'
    Check ((Set-Lab $all).Code -eq 0) 'everything on again'
    Check-AllOn
    Copy-Item -LiteralPath $log -Destination (Join-Path $Registros 'agent-antes-de-desinstalar.log') -Force -ErrorAction SilentlyContinue
    $code = (Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', "/LOG=$Registros\desinstalar.log") -Wait -PassThru).ExitCode
    Check ($code -eq 0) 'the uninstaller finishes'
    Check ($null -eq (Get-CimInstance -ClassName Win32_Service -Filter "Name='MidviewAgent'")) 'the service is gone'
    Check-AllOff 'Desinstalar'
}
catch {
    $failed = $true
    Write-Host ''
    Write-Host $_.Exception.Message -ForegroundColor Red
    if (Test-Path -LiteralPath $log) {
        Copy-Item -LiteralPath $log -Destination (Join-Path $Registros 'agent-falha.log') -Force -ErrorAction SilentlyContinue
        Write-Host ''
        Write-Host '--- agent.log (end) ---'
        Get-Content -LiteralPath $log -Tail 60 | Out-Host
    }
}

Write-Host ''
if ($failed) {
    Write-Host "Painel do laboratorio: FALHOU depois de $($script:checks) conferencias." -ForegroundColor Red
    exit 1
}
Write-Host "Painel do laboratorio conferido de verdade: $($script:checks) conferencias, nenhuma falha." -ForegroundColor Green
exit 0

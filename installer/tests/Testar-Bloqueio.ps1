# Installs Midview on this Windows machine with the setup program and pairs
# a test teacher with it, then blocks programs for real -- the Agent service enforcing the block on real
# processes: what the unit tests can only fake. Run by
# .github/workflows/bloqueio.yml on a throwaway runner, as administrator,
# under Windows PowerShell 5.1:
#
#   powershell -ExecutionPolicy Bypass -File installer\tests\Testar-Bloqueio.ps1 `
#       -Pacote <Gerar-Instalador output> -Ferramenta <Midview.E2E.exe> [-Registros <folder>]
#
# It changes this machine (installs the MidviewAgent service, then removes
# it): never run it on a machine that matters.
#
# The runner's own account is an administrator, and the Agent never ends an
# elevated program, so two kinds of program are started: one of the runner's
# own (elevated: it must survive every block, which proves the fence) and
# one with a limited token of the same user (like a student's: it must be
# ended when the policy says so).
#
# Nothing here is skipped quietly: if this machine can't show what the test
# is about (no interactive session, an account or shell that isn't elevated),
# the test fails with the reason instead of passing without having tried.
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
$work = Join-Path ([IO.Path]::GetTempPath()) ('midview-bloqueio-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $work | Out-Null
if (-not $Registros) {
    $Registros = Join-Path $work 'registros'
}
New-Item -ItemType Directory -Path $Registros -Force | Out-Null

$teacher = Join-Path $work 'professor.pfx'
$machine = Join-Path $work 'maquina.json'
$setup = @(Get-ChildItem -LiteralPath $Pacote -Filter 'MidviewSetup.exe' | Select-Object -First 1 | ForEach-Object { $_.FullName })[0]
$studentProgram = Join-Path $Pacote 'programa\aluno'
$uninstaller = Join-Path $env:ProgramFiles 'Midview\unins000.exe'
$data = Join-Path $env:ProgramData 'Midview'
$blockFile = Join-Path $data 'app-block.json'
$ping = Join-Path $env:SystemRoot 'System32\PING.EXE'
$script:launched = New-Object System.Collections.Generic.List[int]

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
    $output = & $Ferramenta @Arguments
    $code = $LASTEXITCODE
    $output | Out-Host
    return [pscustomobject]@{ Code = $code; Output = @($output) }
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

function Get-ServiceState {
    (Get-CimInstance -ClassName Win32_Service -Filter "Name='MidviewAgent'").State
}

# Process id 0 is the Idle process: it must never stand for "a program that
# didn't start".
function Test-Alive([int]$ProcessId) {
    if ($ProcessId -le 0) {
        return $false
    }
    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    return ($null -ne $process) -and (-not $process.HasExited)
}

# Waits up to $Seconds for a process to be gone; true when it is.
function Wait-Gone([int]$ProcessId, [int]$Seconds) {
    $limit = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $limit) {
        if (-not (Test-Alive $ProcessId)) {
            return $true
        }
        Start-Sleep -Milliseconds 500
    }
    return -not (Test-Alive $ProcessId)
}

# True when the process is still there after $Seconds (a block that left it
# alone has had its chances).
function Stays-Alive([int]$ProcessId, [int]$Seconds) {
    Start-Sleep -Seconds $Seconds
    return (Test-Alive $ProcessId)
}

# A copy of ping.exe under another name, to run "for ever" (-t).
function New-Program([string]$Name, [bool]$OwnedByStudent) {
    $path = Join-Path $work $Name
    Copy-Item -LiteralPath $ping -Destination $path -Force
    if ($OwnedByStudent) {
        # Created by an administrator, a file is owned by the Administrators
        # group; a student's own file is owned by the student.
        & icacls.exe $path /setowner $env:USERNAME | Out-Null
    }
    return $path
}

# The runner's own account (elevated), as is.
function Start-Elevated([string]$Path) {
    $process = Start-Process -FilePath $Path -ArgumentList '-t', '127.0.0.1' -PassThru -WindowStyle Hidden
    $script:launched.Add($process.Id)
    Start-Sleep -Seconds 1
    Check (Test-Alive $process.Id) "the runner's own program starts and keeps running"
    return $process.Id
}

# A limited token of the same user, like a student's program. -Raw for one
# that a block is about to end: the tool then doesn't watch it for the first
# seconds (the block would end it meanwhile, which would look like a failure).
function Start-Limited([string]$Path, [switch]$Raw) {
    $verb = if ($Raw) { 'launch-raw' } else { 'launch' }
    $result = Invoke-Tool @($verb, $Path, '-t', '127.0.0.1')
    # Only the line that is just "pid=N": the attempts that failed print other text.
    $line = $result.Output | Where-Object { "$_" -match '\]\s+pid=(\d+)\s*$' } | Select-Object -First 1
    if ($result.Code -ne 0 -or -not $line) {
        throw "FALHA: could not start a limited-token program from $Path (see the tool output above for why)"
    }
    $processId = [int]([regex]::Match("$line", 'pid=(\d+)').Groups[1].Value)
    $script:launched.Add($processId)
    return $processId
}

# The Agent writes a line each time it ends a program: what tells "the Agent
# ended it" from "it exited by itself", which looks the same from outside.
# The line names the process, so an earlier program of the same name doesn't
# count.
function Agent-Ended([int]$ProcessId, [string]$ImageName) {
    $log = Join-Path $data 'agent.log'
    if (-not (Test-Path -LiteralPath $log)) {
        return $false
    }
    $lines = @(Get-Content -LiteralPath $log -ErrorAction SilentlyContinue)
    return $null -ne ($lines | Where-Object { $_ -like "*Program blocking ended $ImageName (process $ProcessId).*" } | Select-Object -First 1)
}

# True when the Agent ended the program: it's gone and the Agent said so.
function Wait-EndedByAgent([int]$ProcessId, [string]$ImageName, [int]$Seconds) {
    if ($ProcessId -le 0 -or -not (Wait-Gone $ProcessId $Seconds)) {
        return $false
    }
    Start-Sleep -Milliseconds 500
    return (Agent-Ended $ProcessId $ImageName)
}

function Stop-Launched {
    foreach ($processId in $script:launched) {
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
    }
    $script:launched.Clear()
}

function Set-Block([string]$Mode, [string[]]$Programs) {
    $arguments = @('block', $machine, $teacher, $Mode) + $Programs
    return (Invoke-Tool $arguments).Code -eq 0
}

function Save-Logs([string]$Suffix) {
    foreach ($file in @((Join-Path $data 'agent.log'), $blockFile)) {
        if (Test-Path -LiteralPath $file) {
            Copy-Item -LiteralPath $file -Destination (Join-Path $Registros ("$Suffix-" + (Split-Path -Leaf $file))) -Force
        }
    }
}

$failed = $false
try {
    Step 'Instalar com o instalador e parear um professor de teste'
    Check ($null -ne $setup) "the setup is in $Pacote"
    Check ((Invoke-Setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/TIPO=aluno', "/LOG=$Registros\instalar.log")) -eq 0) 'the setup finishes'
    Check ((Get-ServiceState) -eq 'Running') 'the service runs'
    Check ((Invoke-Tool @('pair', $teacher, $machine)).Code -eq 0) 'the test teacher pairs with the machine'
    Check ((Invoke-Tool @('status', $machine, $teacher, $studentProgram)).Code -eq 0) 'the Agent answers'
    Check ((Invoke-Tool @('block-status', $machine, $teacher)).Code -eq 0) 'the Agent knows program blocking'

    Step 'Onde este teste roda'
    $factsResult = Invoke-Tool @('facts')
    Check ($factsResult.Code -eq 0) 'the tool can tell where it runs'
    $facts = $factsResult.Output -join ' '
    Check ($facts -match '\]\s+session=(\d+)' -and $facts -match '\]\s+console=(\d+)') 'the tool printed its session and the console session'
    $session = [int]([regex]::Match($facts, '\]\s+session=(\d+)').Groups[1].Value)
    $console = [int]([regex]::Match($facts, '\]\s+console=(\d+)').Groups[1].Value)
    # Only the tool's own line: the lines about other tokens say elevated= too.
    $elevated = @($factsResult.Output | Where-Object { "$_" -match '\]\s+elevated=True\s*$' }).Count -gt 0
    # The shell's token: when explorer runs elevated, so does everything the
    # user starts from it (the runner's account is the built-in Administrator,
    # which is never filtered by UAC).
    $shellLines = @($factsResult.Output | Where-Object { "$_" -match 'explorer \d+ \(session \d+\) token: ' })
    $shellElevated = ($shellLines.Count -gt 0) -and (@($shellLines | Where-Object { "$_" -notmatch 'elevated=True' }).Count -eq 0)
    Write-Host "session=$session console=$console elevated=$elevated shell-elevated=$shellElevated"
    # The Agent only acts in interactive sessions; and "allow only" ends every
    # program of the user that is not elevated, so it is only safe to try where
    # everything of the user is elevated, in the console session.
    Check ($session -ne 0) 'this machine runs the test in an interactive session (without one the Agent has nothing to act on)'
    Check ($session -eq $console) 'it is the console session, the one the Agent watches for the user'
    Check $elevated 'the runner''s own account is elevated (its programs stand for an administrator''s)'
    Check $shellElevated 'the shell of this account is elevated too, so allow only cannot reach the runner''s own programs'

    $blockme = New-Program 'blockme.exe' $false
    $student = New-Program 'studentcopy.exe' $true

    Step 'A cerca: um programa elevado nunca e encerrado, em nenhum modo'
    $own = Start-Elevated $blockme
    Check (Set-Block 'blocklist' @('blockme.exe')) 'a block list naming it is accepted'
    Check (Stays-Alive $own 8) 'an elevated program survives a block list that names it'
    Check (Set-Block 'lift' @()) 'the block is lifted'
    Stop-Launched

    $own = Start-Elevated $blockme
    $shellBefore = @(Get-Process -Name 'explorer' -ErrorAction SilentlyContinue).Count
    Check (Set-Block 'allowonly' @()) 'allow only, nothing allowed, is accepted'
    Check (Stays-Alive $own 8) 'an elevated program survives allow only with nothing allowed'
    Check ((Get-ServiceState) -eq 'Running') 'the service and this machine still work under allow only'
    $shellAfter = @(Get-Process -Name 'explorer' -ErrorAction SilentlyContinue).Count
    Check ($shellAfter -eq $shellBefore) "the shell is untouched ($shellBefore explorer process(es) before and after)"
    Check (Set-Block 'lift' @()) 'the block is lifted'
    Stop-Launched

    # With no block in place, a limited program must stay open: otherwise
    # "it was ended" below would prove nothing.
    $probe = Start-Limited $blockme
    Check (Stays-Alive $probe 5) 'a limited-token program keeps running when nothing blocks it'
    Stop-Launched

    Step 'Lista de bloqueio: o programa aberto e o que abrir depois sao encerrados'
    $open = Start-Limited $blockme
    Check (Stays-Alive $open 2) 'a limited program is running'
    Check (Set-Block 'blocklist' @('blockme.exe')) 'the block list is applied'
    Check (Wait-EndedByAgent $open 'blockme.exe' 20) 'what was already open is ended by the Agent'
    $again = Start-Limited $blockme -Raw
    Check (Wait-EndedByAgent $again 'blockme.exe' 20) 'and what is started afterwards, too'
    $other = Start-Limited $student
    Check (Stays-Alive $other 6) 'a program that is not listed is left alone'
    Stop-Launched

    Step 'O bloqueio sobrevive a reiniciar o servico'
    Restart-Service -Name 'MidviewAgent'
    Start-Sleep -Seconds 8
    Check ((Get-ServiceState) -eq 'Running') 'the service is back'
    $status = (Invoke-Tool @('block-status', $machine, $teacher))
    Check (($status.Output -join ' ') -match 'mode=BlockList' -and ($status.Output -join ' ') -match 'blockme.exe') 'the machine still follows the block'
    $afterRestart = Start-Limited $blockme -Raw
    Check (Wait-EndedByAgent $afterRestart 'blockme.exe' 20) 'and it still ends the program'

    Step 'Liberar tudo'
    Check (Set-Block 'lift' @()) 'the block is lifted'
    $free = Start-Limited $blockme
    Check (Stays-Alive $free 8) 'the program now stays open'
    Stop-Launched

    Step 'Permitir so os escolhidos'
    Check (Set-Block 'allowonly' @('blockme.exe')) 'allow only blockme.exe is applied'
    $listed = Start-Limited $blockme
    Check (Stays-Alive $listed 8) 'an installed program that is listed stays'
    # A Windows program (owned by TrustedInstaller, in the Windows folder) is
    # "Windows itself": allowed without being listed. If the Agent could not
    # read the owner, this would be ended.
    $windows = Start-Limited $ping
    Check (Stays-Alive $windows 8) 'a program of Windows itself stays although it is not listed'
    $unlisted = Start-Limited $student -Raw
    Check (Wait-EndedByAgent $unlisted 'studentcopy.exe' 20) 'a program that is not listed is ended'
    Stop-Launched

    Check (Set-Block 'allowonly' @('studentcopy.exe')) 'allow only studentcopy.exe is applied'
    $copyByName = Start-Limited $student -Raw
    Check (Wait-EndedByAgent $copyByName 'studentcopy.exe' 20) 'a student''s own copy is ended when only its file name is listed (it must be that program)'
    Check (Set-Block 'allowonly' @('studentcopy.exe', 'ping.exe')) 'allow only both names is applied'
    $copyAndOriginal = Start-Limited $student
    Check (Stays-Alive $copyAndOriginal 8) 'it stays once its real identity is listed too'
    Stop-Launched
    Check (Set-Block 'lift' @()) 'the block is lifted'

    Step 'Desinstalar desfaz o bloqueio'
    Check (Set-Block 'blocklist' @('blockme.exe')) 'a block is in place'
    Check (Test-Path -LiteralPath $blockFile) 'it is saved on the machine'
    Save-Logs 'antes-de-desinstalar'
    Check ((Invoke-Uninstall "$Registros\desinstalar.log") -eq 0) 'the uninstaller finishes'
    Check (-not (Test-Path -LiteralPath $blockFile)) 'the saved block is gone'
    Remove-Item -LiteralPath $data -Recurse -Force -ErrorAction SilentlyContinue
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
finally {
    # Whatever happened, do not leave a block running on the machine (allow
    # only would end everything that is not elevated, for the rest of the job).
    try {
        if (Test-Path -LiteralPath $blockFile) {
            Set-Block 'lift' @() | Out-Null
            Stop-Service -Name 'MidviewAgent' -ErrorAction SilentlyContinue
        }
    }
    catch {
        Write-Host "could not lift the block while cleaning up: $($_.Exception.Message)"
    }
    Stop-Launched
}

if ($failed) {
    exit 1
}
Write-Host ''
Write-Host 'Program blocking checked on a real Windows machine.' -ForegroundColor Green
exit 0

# Installs Midview on this Windows machine with the lab installer, then
# blocks programs for real -- the Agent service enforcing the block on real
# processes: what the unit tests can only fake. Run by
# .github/workflows/bloqueio.yml on a throwaway runner, as administrator,
# under Windows PowerShell 5.1:
#
#   powershell -ExecutionPolicy Bypass -File installer\tests\Testar-Bloqueio.ps1 `
#       -Pacote <built package> -Ferramenta <Midview.E2E.exe> [-Registros <folder>]
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

function Invoke-Script([string]$Name, [string[]]$Arguments) {
    $path = Join-Path $Pacote "programa\scripts\$Name"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $path @Arguments | Out-Host
    return $LASTEXITCODE
}

function Get-ServiceState {
    (Get-CimInstance -ClassName Win32_Service -Filter "Name='MidviewAgent'").State
}

function Test-Alive([int]$ProcessId) {
    $null -ne (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)
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
    return $process.Id
}

# A limited token of the same user, like a student's program.
function Start-Limited([string]$Path) {
    $result = Invoke-Tool @('launch', $Path, '-t', '127.0.0.1')
    $line = $result.Output | Where-Object { "$_" -match 'pid=(\d+)' } | Select-Object -First 1
    if ($line) {
        $script:launched.Add([int]([regex]::Match("$line", 'pid=(\d+)').Groups[1].Value))
    }
    if ($result.Code -ne 0 -or -not $line) {
        return $null
    }
    return $script:launched[$script:launched.Count - 1]
}

# The Agent writes a line each time it ends a program: what tells "the Agent
# ended it" from "it exited by itself", which looks the same from outside.
function Agent-Ended([string]$ImageName) {
    $log = Join-Path $data 'agent.log'
    if (-not (Test-Path -LiteralPath $log)) {
        return $false
    }
    $lines = @(Get-Content -LiteralPath $log -ErrorAction SilentlyContinue)
    return $null -ne ($lines | Where-Object { $_ -like "*Program blocking ended $ImageName *" } | Select-Object -First 1)
}

# True when the Agent ended the program: it's gone and the Agent said so.
function Wait-EndedByAgent([int]$ProcessId, [string]$ImageName, [int]$Seconds) {
    if (-not (Wait-Gone $ProcessId $Seconds)) {
        return $false
    }
    Start-Sleep -Milliseconds 500
    return (Agent-Ended $ImageName)
}

function Stop-Launched {
    foreach ($processId in $script:launched) {
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
    }
    $script:launched.Clear()
}

function Set-Block([string]$Mode, [string[]]$Programs) {
    $arguments = @('block', $Pacote, $teacher, $Mode) + $Programs
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
    Step 'Autorizar um professor de teste e instalar pelo pendrive'
    Check ((Invoke-Tool @('authorize', $Pacote, $teacher)).Code -eq 0) 'the test teacher is authorized in the package'
    Check ((Invoke-Script 'Instalar-Aluno.ps1' @('-Pacote', $Pacote)) -eq 0) 'Instalar-Aluno finishes'
    Check ((Get-ServiceState) -eq 'Running') 'the service runs'
    Check ((Invoke-Tool @('status', $Pacote, $teacher, $Pacote)).Code -eq 0) 'the Agent answers'
    Check ((Invoke-Tool @('block-status', $Pacote, $teacher)).Code -eq 0) 'the Agent knows program blocking'

    Step 'Onde este teste roda'
    $facts = (Invoke-Tool @('facts')).Output -join ' '
    $session = [int]([regex]::Match($facts, 'session=(\d+)').Groups[1].Value)
    $elevated = $facts -match 'elevated=True'
    $canEnd = $session -ne 0
    Write-Host "session=$session elevated=$elevated (the Agent only acts in interactive sessions: $canEnd)"

    $blockme = New-Program 'blockme.exe' $false
    $student = New-Program 'studentcopy.exe' $true

    Step 'A cerca: um programa elevado nunca e encerrado, em nenhum modo'
    $own = Start-Elevated $blockme
    Check (Set-Block 'blocklist' @('blockme.exe')) 'a block list naming it is accepted'
    if ($elevated) {
        Check (Stays-Alive $own 8) 'an elevated program survives a block list that names it'
    }
    elseif ($canEnd) {
        Check (Wait-Gone $own 15) 'this runner is not elevated: its program is ended like a student''s'
    }
    Check (Set-Block 'lift' @()) 'the block is lifted'
    Stop-Launched

    # Allow only ends everything the student runs that isn't allowed: on a
    # runner whose own account is not elevated, that would be the runner's
    # own processes. Only here, where the fence covers them, is it safe.
    if ($elevated) {
        $own = Start-Elevated $blockme
        $shellBefore = @(Get-Process -Name 'explorer' -ErrorAction SilentlyContinue).Count
        Check (Set-Block 'allowonly' @()) 'allow only, nothing allowed, is accepted'
        Check (Stays-Alive $own 8) 'an elevated program survives allow only with nothing allowed'
        Check ((Get-ServiceState) -eq 'Running') 'the service and this machine still work under allow only'
        $shellAfter = @(Get-Process -Name 'explorer' -ErrorAction SilentlyContinue).Count
        Check ($shellAfter -eq $shellBefore) "the shell is untouched ($shellBefore explorer process(es) before and after)"
        Check (Set-Block 'lift' @()) 'the block is lifted'
        Stop-Launched
    }
    else {
        Write-Host 'SKIP: this runner is not elevated, so allow only is not tried on it.' -ForegroundColor Yellow
    }

    if (-not $canEnd) {
        Write-Host ''
        Write-Host 'SKIP: this runner has no interactive session for the Agent to act in; the kills below can only be checked on a machine that has one.' -ForegroundColor Yellow
    }
    else {
        # With no block in place, a limited program must stay open: otherwise
        # "it was ended" below would prove nothing.
        $probe = Start-Limited $blockme
        Check ($null -ne $probe) 'a limited-token program starts and keeps running on its own (see the tool output above for why not)'
        Check (Stays-Alive $probe 5) 'it stays open when nothing blocks it'
        Stop-Process -Id $probe -Force -ErrorAction SilentlyContinue
        $script:launched.Clear()
        if ($true) {

            Step 'Lista de bloqueio: o programa aberto e o que abrir depois sao encerrados'
            $open = Start-Limited $blockme
            Check ($null -ne $open) 'a limited program starts'
            Check (Set-Block 'blocklist' @('blockme.exe')) 'the block list is applied'
            Check (Wait-EndedByAgent $open 'blockme.exe' 20) 'what was already open is ended by the Agent'
            $again = Start-Limited $blockme
            Check (Wait-EndedByAgent $again 'blockme.exe' 20) 'and what is started afterwards, too'
            $other = Start-Limited $student
            Check (Stays-Alive $other 6) 'a program that is not listed is left alone'
            Stop-Launched

            Step 'O bloqueio sobrevive a reiniciar o servico'
            Restart-Service -Name 'MidviewAgent'
            Start-Sleep -Seconds 8
            Check ((Get-ServiceState) -eq 'Running') 'the service is back'
            $status = (Invoke-Tool @('block-status', $Pacote, $teacher))
            Check (($status.Output -join ' ') -match 'mode=BlockList' -and ($status.Output -join ' ') -match 'blockme.exe') 'the machine still follows the block'
            $afterRestart = Start-Limited $blockme
            Check (Wait-EndedByAgent $afterRestart 'blockme.exe' 20) 'and it still ends the program'

            Step 'Liberar tudo'
            Check (Set-Block 'lift' @()) 'the block is lifted'
            $free = Start-Limited $blockme
            Check (Stays-Alive $free 8) 'the program now stays open'
            Stop-Launched

            Step 'Permitir so os escolhidos'
            if (-not $elevated) {
                throw 'FALHA: allow only needs an elevated runner to be tried safely'
            }
            Check (Set-Block 'allowonly' @('blockme.exe')) 'allow only blockme.exe is applied'
            $listed = Start-Limited $blockme
            Check (Stays-Alive $listed 8) 'an installed program that is listed stays'
            $unlisted = Start-Limited $student
            Check (Wait-EndedByAgent $unlisted 'studentcopy.exe' 20) 'a program that is not listed is ended'
            Stop-Launched

            Check (Set-Block 'allowonly' @('studentcopy.exe')) 'allow only studentcopy.exe is applied'
            $copyByName = Start-Limited $student
            Check (Wait-EndedByAgent $copyByName 'studentcopy.exe' 20) 'a student''s own copy is ended when only its file name is listed (it must be that program)'
            Check (Set-Block 'allowonly' @('studentcopy.exe', 'ping.exe')) 'allow only both names is applied'
            $copyAndOriginal = Start-Limited $student
            Check (Stays-Alive $copyAndOriginal 8) 'it stays once its real identity is listed too'
            Stop-Launched
            Check (Set-Block 'lift' @()) 'the block is lifted'
        }
    }

    Step 'Desinstalar desfaz o bloqueio'
    Check (Set-Block 'blocklist' @('blockme.exe')) 'a block is in place'
    Check (Test-Path -LiteralPath $blockFile) 'it is saved on the machine'
    Save-Logs 'antes-de-desinstalar'
    Check ((Invoke-Script 'Desinstalar-Aluno.ps1' @()) -eq 0) 'Desinstalar-Aluno finishes'
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
    Stop-Launched
}

if ($failed) {
    exit 1
}
Write-Host ''
Write-Host 'Program blocking checked on a real Windows machine.' -ForegroundColor Green
exit 0

# Checks a built installation package's scripts under whatever PowerShell
# runs this -- in CI (.github/workflows/pacote.yml), Windows PowerShell 5.1,
# the one lab machines have:
#
#   powershell -ExecutionPolicy Bypass -File installer\tests\Testar-Scripts.ps1 -Pacote dist\Midview-Instalacao
#
# It runs against the package rather than installer\scripts because the
# package's copies are the ones with the BOM and CRLF that 5.1 and cmd.exe
# need (see Gerar-Pacote.ps1). The shared functions (Comum.ps1) are run for
# real, including the 5.1 quirks they work around.
#
# ASCII-only, like Gerar-Pacote.ps1: 5.1 reads a .ps1 without a BOM as ANSI.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Pacote
)

$ErrorActionPreference = 'Stop'
$script:failures = 0

function Check([bool]$Condition, [string]$What) {
    if ($Condition) {
        Write-Host "ok    $What"
    }
    else {
        Write-Host "FALHA $What" -ForegroundColor Red
        $script:failures++
    }
}

Write-Host "PowerShell $($PSVersionTable.PSVersion) ($($PSVersionTable.PSEdition))"
$scripts = Join-Path $Pacote 'programa\scripts'

foreach ($file in Get-ChildItem -Path $scripts -Filter '*.ps1') {
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors) | Out-Null
    Check ($errors.Count -eq 0) "$($file.Name) is valid PowerShell $(($errors | ForEach-Object { $_.Message }) -join '; ')"
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    Check ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) "$($file.Name) starts with a UTF-8 BOM"
}

foreach ($file in Get-ChildItem -Path $Pacote -Filter '*.cmd') {
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    Check (-not ($bytes | Where-Object { $_ -gt 127 })) "$($file.Name) is ASCII"
    $text = [IO.File]::ReadAllText($file.FullName)
    Check (($text -replace "`r`n", '') -notmatch "`n") "$($file.Name) uses CRLF line endings"
}

foreach ($program in 'aluno\Midview.Agent.exe', 'aluno\Midview.Session.exe', 'professor\Midview.Master.exe') {
    Check (Test-Path -LiteralPath (Join-Path $Pacote "programa\$program")) "the package has $program"
}

# What the MidviewMaster reads to push the student program over the network
# (BuildManifest): exactly the files in the folder, besides itself.
$student = Join-Path $Pacote 'programa\aluno'
$manifestPath = Join-Path $student 'midview-build.json'
Check (Test-Path -LiteralPath $manifestPath) 'the student program has midview-build.json'
if (Test-Path -LiteralPath $manifestPath) {
    $bytes = [IO.File]::ReadAllBytes($manifestPath)
    Check (-not ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB)) 'midview-build.json has no BOM'
    $manifest = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
    Check ($manifest.build -match '^\d{4}-\d{2}-\d{2} \d{2}:\d{2} \S+$') "the build is labelled with its date, time and commit ($($manifest.build))"
    Check ($manifest.updateProtocol -eq 1) 'the manifest speaks update protocol 1'
    $listed = @($manifest.files)
    Check ($listed -contains 'Midview.Agent.exe' -and $listed -contains 'Midview.Session.exe') 'the manifest lists the Agent and the Session'
    Check (-not ($listed -contains 'midview-build.json')) 'the manifest does not list itself'
    Check (-not ($listed | Where-Object { $_ -like '*\*' })) "the manifest's paths use '/'"
    $root = (Get-Item -LiteralPath $student).FullName.TrimEnd('\') + '\'
    $actual = @(Get-ChildItem -LiteralPath $student -Recurse -File -Force |
            ForEach-Object { $_.FullName.Substring($root.Length).Replace('\', '/') } |
            Where-Object { $_ -ne 'midview-build.json' })
    $missing = @($listed | Where-Object { $actual -notcontains $_ })
    $unlisted = @($actual | Where-Object { $listed -notcontains $_ })
    Check ($missing.Count -eq 0 -and $unlisted.Count -eq 0) "the manifest lists exactly the $($actual.Count) files in the folder (missing: $($missing -join ', '); unlisted: $($unlisted -join ', '))"
}

. (Join-Path $scripts 'Comum.ps1')

$t1 = 'A' * 40
$t2 = 'B' * 40
$session = '0123456789ABCDEF0123456789ABCDEF01234567'
$agent = 'FEDCBA9876543210FEDCBA9876543210FEDCBA98'
$work = Join-Path ([IO.Path]::GetTempPath()) ('midview-scripts-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $work | Out-Null
try {
    Check ((ConvertTo-ThumbprintNormalizado " $($t1.ToLowerInvariant()) ") -eq $t1) 'a thumbprint is trimmed and upper-cased'
    Check ($null -eq (ConvertTo-ThumbprintNormalizado 'xyz')) 'a bad thumbprint is rejected'

    # Exactly as the Master writes it (System.Text.Json, indented). One item
    # is the case where 5.1's ConvertFrom-Json doesn't unroll the list.
    $one = Join-Path $work 'one.json'
    [IO.File]::WriteAllText($one, "[`r`n  `"$($t1.ToLowerInvariant())`"`r`n]")
    $read = @(Read-ProfessoresAutorizados $one)
    Check ($read.Count -eq 1 -and $read[0] -eq $t1) 'one authorized teacher is read as one'

    $two = Join-Path $work 'two.json'
    [IO.File]::WriteAllText($two, "[`"$t1`", `"ruim`", `"$t2`", `"$t1`"]")
    $read = @(Read-ProfessoresAutorizados $two)
    Check ($read.Count -eq 2 -and $read[1] -eq $t2) 'two teachers are read, the bad and repeated ones dropped'

    try {
        Read-ProfessoresAutorizados (Join-Path $work 'missing.json') | Out-Null
        Check $false 'a missing teacher list stops the install'
    }
    catch {
        # "nao" with its accent, spelled out to keep this file ASCII: the
        # messages must come through the BOM with their accents intact.
        $nao = "n$([char]0x00E3)o"
        Check ($_.Exception.Message -like "*Autorizar este professor no pacote*" -and $_.Exception.Message -like "*$nao*") 'a missing teacher list says what to do, accents intact'
    }

    $written = Join-Path $work 'written.json'
    Write-ProfessoresAutorizados $written @($t1)
    Check ([IO.File]::ReadAllText($written) -eq "[`"$t1`"]") 'one teacher is written as a JSON list'
    Check (@(Read-ProfessoresAutorizados $written).Count -eq 1) 'and read back as one'

    $when = Get-Date '2026-09-24T10:00:00'
    foreach ($case in @(
            @{ Name = 'ALUNO-01'; Addresses = @('192.168.0.21'); Mac = '00-1A-2B-3C-4D-5E' },
            @{ Name = 'ALUNO-02'; Addresses = @(); Mac = $null },
            @{ Name = 'ALUNO-03'; Addresses = @('10.0.0.3', '192.168.56.1'); Mac = '00-11-22-33-44-55' },
            @{ Name = 'SALA"1\x'; Addresses = @('10.0.0.4'); Mac = $null })) {
        $json = New-FichaMaquina $case.Name $case.Addresses $session $agent $case.Mac $when
        $parsed = $json | ConvertFrom-Json
        Check ($parsed.computerName -eq $case.Name -and $parsed.sessionThumbprint -eq $session -and $parsed.agentThumbprint -eq $agent) "the machine file for $($case.Name) reads back"
        $addresses = @($parsed.addresses)
        Check ($addresses.Count -eq $case.Addresses.Count -and (($addresses -join ',') -eq ($case.Addresses -join ','))) "and keeps its $($case.Addresses.Count) address(es) as a list"
        Check ($parsed.macAddress -eq $case.Mac) "and its network card ($(if ($case.Mac) { $case.Mac } else { 'none' }))"
    }

    # The same rule as the Master's WakeOnLan.NormalizeMacAddress.
    Check ((ConvertTo-EnderecoMacNormalizado '00:1a:2b:3c:4d:5e') -eq '00-1A-2B-3C-4D-5E') 'a MAC address is normalized'
    Check ((ConvertTo-EnderecoMacNormalizado ' 001A2B3C4D5E ') -eq '00-1A-2B-3C-4D-5E') 'a MAC address without separators is normalized'
    foreach ($bad in @('', '00-1A-2B-3C-4D', '00-1A-2B-3C-4D-ZZ', '00-00-00-00-00-00', 'FF-FF-FF-FF-FF-FF', '01-00-5E-00-00-01')) {
        Check ($null -eq (ConvertTo-EnderecoMacNormalizado $bad)) "'$bad' is not a network card's MAC address"
    }

    $mac = Get-EnderecoMac
    Check ($null -eq $mac -or $mac -match '^([0-9A-F]{2}-){5}[0-9A-F]{2}$') "this machine's network card is found or left out, never garbled ($mac)"

    $ficha = Join-Path $work 'ficha.json'
    Write-TextoUtf8 $ficha 'x'
    Check ([IO.File]::ReadAllBytes($ficha)[0] -eq [byte][char]'x') 'machine files are written without a BOM'

    $addresses = @(Get-EnderecosIPv4)
    Check ($addresses.Count -ge 1 -and -not ($addresses | Where-Object { $_ -like '127.*' -or $_ -like '169.254.*' })) "this machine's IPv4 addresses are found ($($addresses -join ', '))"

    # The service's PathName, as Win32_Service reports it.
    $exe = 'C:\Program Files\Midview.update\AB12\Midview.Agent.exe'
    Check ((ConvertFrom-CaminhoDoServico "`"$exe`"") -eq $exe) "a quoted service path is read"
    Check ((ConvertFrom-CaminhoDoServico "`"$exe`" --flag") -eq $exe) "a quoted service path with arguments is read"
    Check ((ConvertFrom-CaminhoDoServico 'C:\Midview\Midview.Agent.exe') -eq 'C:\Midview\Midview.Agent.exe') "an unquoted service path is read"
    Check ($null -eq (ConvertFrom-CaminhoDoServico '') -and $null -eq (ConvertFrom-CaminhoDoServico '"')) "an empty service path is nothing"

    # The lock a network update holds (the Agent's UpdateLock).
    $lock = Join-Path $work 'update.lock'
    Check ($null -eq (Enter-TravaDeAtualizacao (Join-Path $work 'nowhere\update.lock') 1)) 'no data folder, no lock to wait for'
    $held = [IO.File]::Open($lock, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        Enter-TravaDeAtualizacao $lock 2 | Out-Null
        Check $false 'a held update lock stops the install after the wait'
    }
    catch {
        Check ($_.Exception.Message -like '*atualiza*') 'a held update lock stops the install after the wait'
    }
    finally {
        $held.Dispose()
    }
    $mine = Enter-TravaDeAtualizacao $lock 2
    try {
        Check ($null -ne $mine) 'a free update lock is taken'
        $again = $null
        try {
            $again = [IO.File]::Open($lock, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        }
        catch {
        }
        Check ($null -eq $again) 'and held until released'
        if ($again) {
            $again.Dispose()
        }
    }
    finally {
        if ($mine) {
            $mine.Dispose()
        }
    }

    # What a network update leaves behind (Remove-RestosDeAtualizacao), in
    # stand-in folders, with a file held open so Midview.update can't go.
    $PastaAtualizacoes = Join-Path $work 'Midview.update'
    $PastaEstadoAtualizacao = Join-Path $work 'estado'
    $TarefaRecuperacao = 'Midview - tarefa de teste que nao existe'
    New-Item -ItemType Directory -Path (Join-Path $PastaAtualizacoes 'AB12') -Force | Out-Null
    New-Item -ItemType Directory -Path $PastaEstadoAtualizacao -Force | Out-Null
    Write-TextoUtf8 (Join-Path $PastaEstadoAtualizacao 'update-state.json') '{}'
    $held = [IO.File]::Open((Join-Path $PastaAtualizacoes 'AB12\Midview.Agent.dll'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        Remove-RestosDeAtualizacao 3>$null
        Check $true 'a Midview.update that cannot be deleted only warns after an install'
        Check (-not (Test-Path -LiteralPath (Join-Path $PastaEstadoAtualizacao 'update-state.json'))) 'the update record is deleted'
        try {
            Remove-RestosDeAtualizacao -Obrigatorio 3>$null
            Check $false 'but it stops an uninstall'
        }
        catch {
            Check ($_.Exception.Message -like "*$PastaAtualizacoes*") 'but it stops an uninstall'
        }
    }
    finally {
        $held.Dispose()
    }
    Remove-RestosDeAtualizacao -Obrigatorio
    Check (-not (Test-Path -LiteralPath $PastaAtualizacoes)) 'and goes once nothing holds it'

    Check ((Split-Path -Leaf (Join-Path $env:ProgramFiles 'Midview')) -eq 'Midview') 'the default install folder is named Midview'
    try {
        Assert-PastaDoMidview 'C:\'
        Check $false 'mirroring into a folder not named Midview is refused'
    }
    catch {
        Check $true 'mirroring into a folder not named Midview is refused'
    }
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force
}

if ($script:failures -gt 0) {
    Write-Host "$($script:failures) check(s) failed." -ForegroundColor Red
    exit 1
}
Write-Host 'All checks passed.' -ForegroundColor Green

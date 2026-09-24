# Remove o Midview desta máquina de aluno. Rodado por Desinstalar-Aluno.cmd,
# como administrador. Antes de apagar qualquer coisa, desfaz o que uma aula
# pode ter deixado: internet ou sites bloqueados, teclado e mouse desativados
# (Midview.Agent.exe --uninstall-cleanup).
#
# Os dados em C:\ProgramData\Midview (a identidade da máquina e os
# professores autorizados) ficam, para uma reinstalação manter a mesma ficha
# no MidviewMaster. -RemoverDados apaga também.
[CmdletBinding()]
param(
    [switch]$RemoverDados,
    [string]$Destino = (Join-Path $env:ProgramFiles 'Midview')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Comum.ps1')

try {
    Assert-Administrador
    Assert-PastaDoMidview $Destino

    Write-Etapa 'Parando o Midview'
    Stop-Midview

    Write-Etapa 'Desfazendo bloqueios de internet e de sites e reativando teclado e mouse'
    $agente = Join-Path $Destino 'Midview.Agent.exe'
    $limpezaCompleta = $false
    if (Test-Path -LiteralPath $agente) {
        $limpezaCompleta = (Invoke-Programa $agente @('--uninstall-cleanup')).Codigo -eq 0
    }
    else {
        Write-Warning "Não achei $agente, então bloqueios que estejam ativos não puderam ser desfeitos."
    }

    Write-Etapa 'Removendo o serviço MidviewAgent'
    if (Get-Service -Name $NomeServico -ErrorAction SilentlyContinue) {
        & sc.exe delete $NomeServico | Out-Null
    }

    Write-Etapa "Apagando $Destino"
    if (Test-Path -LiteralPath $Destino) {
        Remove-Item -LiteralPath $Destino -Recurse -Force
    }

    if ($RemoverDados) {
        Write-Etapa "Apagando $PastaDados"
        if (Test-Path -LiteralPath $PastaDados) {
            Remove-Item -LiteralPath $PastaDados -Recurse -Force
        }
    }
    else {
        Write-Host "Os dados em $PastaDados foram mantidos (reinstalar mantém a mesma ficha). Para apagar também: Desinstalar-Aluno.cmd -RemoverDados"
    }

    Write-Host ''
    if (-not $limpezaCompleta) {
        Write-Host 'Midview removido, mas a limpeza não foi completa (veja acima): confira se a internet, os sites e o teclado/mouse estão normais.' -ForegroundColor Yellow
        exit 1
    }
    Write-Host "Midview removido de $env:COMPUTERNAME." -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ''
    Write-Host "A remoção NÃO terminou: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

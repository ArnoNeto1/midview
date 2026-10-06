# Remove o serviço do Midview Aluno. Rodado pelo desinstalador (Aplicativos
# instalados), como administrador, antes de ele apagar os arquivos. Antes de
# tudo, desfaz o que uma aula pode ter deixado: internet, sites ou programas
# bloqueados (Midview.Agent.exe --uninstall-cleanup, da versão que o serviço
# roda, que depois de uma atualização pela rede fica em Midview.update).
# Apaga também o que as atualizações pela rede deixaram.
#
# Os dados em C:\ProgramData\Midview (a identidade da máquina e os
# professores pareados) ficam, para uma reinstalação continuar pareada.
# -RemoverDados apaga também.
[CmdletBinding()]
param(
    [switch]$RemoverDados,
    [string]$Destino = (Split-Path -Path $PSScriptRoot -Parent),
    [string]$Erro
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Comum.ps1')

$trava = $null
try {
    Assert-Administrador
    Assert-PastaDoMidview $Destino

    Write-Etapa 'Parando o Midview'
    $trava = Enter-TravaDeAtualizacao
    Stop-Midview

    Write-Etapa 'Desfazendo bloqueios de internet, de sites e de programas'
    $agente = Get-ExecutavelDoServico
    if (-not $agente -or -not (Test-Path -LiteralPath $agente)) {
        $agente = Join-Path $Destino 'Midview.Agent.exe'
    }
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
    Remove-RestosDeAtualizacao -Obrigatorio

    if ($trava) {
        $trava.Dispose()
        $trava = $null
    }
    if ($RemoverDados) {
        Write-Etapa "Apagando $PastaDados"
        if (Test-Path -LiteralPath $PastaDados) {
            Remove-Item -LiteralPath $PastaDados -Recurse -Force
        }
    }
    else {
        Write-Host "Os dados em $PastaDados foram mantidos: instalando de novo, a máquina continua pareada com os mesmos professores."
    }

    if (-not $limpezaCompleta) {
        $mensagem = 'nem tudo o que uma aula deixou pôde ser desfeito.'
        Write-Host "Midview removido, mas $mensagem" -ForegroundColor Yellow
        Write-Erro $Erro $mensagem
        exit 1
    }
    Write-Host "Midview removido de $env:COMPUTERNAME." -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ''
    Write-Host "A remoção NÃO terminou: $($_.Exception.Message)" -ForegroundColor Red
    Write-Erro $Erro $_.Exception.Message
    exit 1
}
finally {
    if ($trava) {
        $trava.Dispose()
    }
}

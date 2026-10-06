# Configura o Midview Aluno nesta máquina depois que o instalador
# (Midview-Instalador) copiou os arquivos para a pasta acima desta:
#   1. espera uma atualização pela rede terminar, se houver uma, e para o
#      Midview;
#   2. prepara a identidade da máquina (Midview.Agent.exe --prepare-install);
#   3. registra o serviço MidviewAgent nesta cópia (mesmo que uma
#      atualização pela rede o tenha levado para Midview.update), apaga o
#      que as atualizações pela rede deixaram e inicia o serviço.
# Os professores que a máquina já aceita (trusted-masters.json) ficam como
# estão: quem acrescenta um professor é o pareamento, no Midview Professor.
[CmdletBinding()]
param(
    [string]$Destino,
    [string]$Erro
)

$ErrorActionPreference = 'Stop'
# O Windows PowerShell 5.1 deixa $PSScriptRoot vazio nos valores padrão do
# param() quando o script roda com -File, como o instalador o roda: a pasta
# é calculada aqui.
if (-not $Destino) {
    $Destino = Split-Path -Path $PSScriptRoot -Parent
}
. (Join-Path $PSScriptRoot 'Comum.ps1')

$trava = $null
try {
    Assert-Administrador
    Assert-PastaDoMidview $Destino
    $agente = Join-Path $Destino 'Midview.Agent.exe'
    if (-not (Test-Path -LiteralPath $agente)) {
        throw "Não achei $agente. Rode o instalador de novo."
    }

    Write-Etapa 'Parando a versão anterior (se houver)'
    $trava = Enter-TravaDeAtualizacao
    Stop-Midview

    Write-Etapa 'Preparando a identidade desta máquina'
    $preparo = Invoke-Programa $agente @('--prepare-install')
    if ($preparo.Codigo -ne 0) {
        throw 'O Midview.Agent não conseguiu preparar a máquina.'
    }

    Write-Etapa 'Registrando e iniciando o serviço MidviewAgent'
    $caminhoServico = '"' + $agente + '"'
    if (Get-Service -Name $NomeServico -ErrorAction SilentlyContinue) {
        Set-ExecutavelDoServico $caminhoServico
        Set-Service -Name $NomeServico -StartupType Automatic
    }
    else {
        New-Service -Name $NomeServico -BinaryPathName $caminhoServico -DisplayName 'Midview Agent' `
            -Description 'Midview: controle da sala de aula pelo professor.' -StartupType Automatic | Out-Null
    }
    # Se o serviço cair, o Windows o reinicia: depois de 5 s, 5 s e 1 min.
    & sc.exe failure $NomeServico reset= 86400 actions= restart/5000/restart/5000/restart/60000 | Out-Null
    Remove-RestosDeAtualizacao
    Start-Service -Name $NomeServico

    Write-Host ''
    Write-Host "Midview Aluno configurado em $env:COMPUTERNAME." -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ''
    Write-Host "A configuração NÃO terminou: $($_.Exception.Message)" -ForegroundColor Red
    Write-Erro $Erro $_.Exception.Message
    exit 1
}
finally {
    if ($trava) {
        $trava.Dispose()
    }
}

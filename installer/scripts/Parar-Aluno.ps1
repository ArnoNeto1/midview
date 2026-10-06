# Para o Midview Aluno antes de o instalador (Midview-Instalador) copiar os
# arquivos novos: espera uma atualização pela rede terminar, se houver uma,
# e para o serviço e a Session. Numa máquina ainda sem Midview, não faz
# nada. Rodado pelo instalador, como administrador.
[CmdletBinding()]
param(
    [string]$Erro
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Comum.ps1')

$trava = $null
try {
    Assert-Administrador
    Write-Etapa 'Parando a versão anterior (se houver)'
    $trava = Enter-TravaDeAtualizacao
    Stop-Midview
    exit 0
}
catch {
    Write-Host "O Midview não pôde ser parado: $($_.Exception.Message)" -ForegroundColor Red
    Write-Erro $Erro $_.Exception.Message
    exit 1
}
finally {
    if ($trava) {
        $trava.Dispose()
    }
}

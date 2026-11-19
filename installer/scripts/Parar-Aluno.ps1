# Para o Midview Aluno antes de o instalador (MidviewSetup) copiar os
# arquivos novos: espera uma atualização pela rede terminar, se houver uma,
# e para o serviço e a Session. Numa máquina ainda sem Midview, não faz
# nada. Rodado pelo instalador, como administrador.
[CmdletBinding()]
param(
    [string]$Erro
)

$ErrorActionPreference = 'Stop'
# A pasta deste script: $PSScriptRoot quando roda como arquivo, ou a que o
# instalador deu (MidviewScriptRoot) quando roda como comando, o jeito que o
# instalador usa para uma política de grupo que proíbe scripts não o barrar
# (RodarScript em Midview.iss). Comum.ps1 é carregado pelo texto, também sem
# rodar um arquivo de script.
$PastaDoScript = if ($PSScriptRoot) { $PSScriptRoot } else { $global:MidviewScriptRoot }
. ([scriptblock]::Create([IO.File]::ReadAllText((Join-Path $PastaDoScript 'Comum.ps1'), [Text.Encoding]::UTF8)))

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

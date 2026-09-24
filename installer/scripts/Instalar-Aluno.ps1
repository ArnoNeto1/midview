# Instala ou atualiza o Midview nesta máquina de aluno. Rodado por
# Instalar-Aluno.cmd, como administrador:
#   1. para a versão anterior, se houver;
#   2. copia o programa para C:\Program Files\Midview;
#   3. prepara a identidade da máquina (Midview.Agent.exe --prepare-install);
#   4. autoriza os professores do pacote;
#   5. registra e inicia o serviço MidviewAgent;
#   6. deixa a ficha da máquina na pasta "maquinas" do pacote, para o
#      professor importar no MidviewMaster.
[CmdletBinding()]
param(
    [string]$Pacote,
    [string]$Destino = (Join-Path $env:ProgramFiles 'Midview')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Comum.ps1')
if (-not $Pacote) {
    $Pacote = Split-Path -Path (Split-Path -Path $PSScriptRoot -Parent) -Parent
}

try {
    Assert-Administrador
    Assert-PastaDoMidview $Destino
    $professores = @(Read-ProfessoresAutorizados (Join-Path $Pacote 'trusted-masters.json'))
    $origem = Join-Path $Pacote 'programa\aluno'
    if (-not (Test-Path -LiteralPath (Join-Path $origem 'Midview.Agent.exe'))) {
        throw "Não achei o programa do aluno em $origem. O pacote está completo?"
    }

    Write-Etapa 'Parando a versão anterior (se houver)'
    Stop-Midview

    Write-Etapa "Copiando o programa para $Destino"
    Copy-Pasta $origem $Destino

    Write-Etapa 'Preparando a identidade desta máquina'
    $agente = Join-Path $Destino 'Midview.Agent.exe'
    $preparo = Invoke-Programa $agente @('--prepare-install')
    if ($preparo.Codigo -ne 0) {
        throw 'O Midview.Agent não conseguiu preparar a máquina (veja a mensagem acima).'
    }
    $identidade = $preparo.Saida | ConvertFrom-Json

    Write-Etapa 'Autorizando os professores do pacote'
    Write-ProfessoresAutorizados (Join-Path $PastaDados 'trusted-masters.json') $professores
    Write-Host "Professores autorizados nesta máquina: $($professores.Count)"

    Write-Etapa 'Registrando e iniciando o serviço MidviewAgent'
    $caminhoServico = '"' + $agente + '"'
    if (Get-Service -Name $NomeServico -ErrorAction SilentlyContinue) {
        # O Set-Service do PowerShell 5.1 não troca o executável; o registro do serviço sim.
        Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$NomeServico" -Name ImagePath -Value $caminhoServico -Type ExpandString
        Set-Service -Name $NomeServico -StartupType Automatic
    }
    else {
        New-Service -Name $NomeServico -BinaryPathName $caminhoServico -DisplayName 'Midview Agent' `
            -Description 'Midview: controle da sala de aula pelo professor.' -StartupType Automatic | Out-Null
    }
    # Se o serviço cair, o Windows o reinicia: depois de 5 s, 5 s e 1 min.
    & sc.exe failure $NomeServico reset= 86400 actions= restart/5000/restart/5000/restart/60000 | Out-Null
    Start-Service -Name $NomeServico

    Write-Etapa 'Deixando a ficha desta máquina no pacote'
    $enderecos = @(Get-EnderecosIPv4)
    $ficha = New-FichaMaquina $env:COMPUTERNAME $enderecos $identidade.sessionThumbprint $identidade.agentThumbprint (Get-Date)
    $pastaFichas = Join-Path $Pacote 'maquinas'
    $caminhoFicha = Join-Path $pastaFichas "$env:COMPUTERNAME.json"
    $fichaSalva = $true
    try {
        New-Item -ItemType Directory -Path $pastaFichas -Force | Out-Null
        Write-TextoUtf8 $caminhoFicha $ficha
    }
    catch {
        $fichaSalva = $false
        Write-Warning "Não foi possível gravar a ficha em ${caminhoFicha}: $($_.Exception.Message)"
        Write-Host 'Cadastre a máquina à mão no MidviewMaster ("Adicionar / testar máquina") com estes dados:'
        Write-Host $ficha
    }

    Write-Host ''
    Write-Host "Midview instalado em $env:COMPUTERNAME." -ForegroundColor Green
    if ($enderecos.Count -gt 0) {
        Write-Host "  Endereço IP: $($enderecos[0])"
    }
    else {
        Write-Warning 'Nenhum endereço IP encontrado: no MidviewMaster, importe conectando pelo nome do computador.'
    }
    if ($fichaSalva) {
        Write-Host "  Ficha: $caminhoFicha"
    }
    Write-Host 'Depois de instalar em todas as máquinas, importe-as no MidviewMaster: "Instalar nos alunos..." > "Importar máquinas instaladas".'
    exit 0
}
catch {
    Write-Host ''
    Write-Host "A instalação NÃO terminou: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Corrija o problema e rode Instalar-Aluno.cmd de novo.'
    exit 1
}

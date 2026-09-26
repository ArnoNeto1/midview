# Funções usadas pelos scripts de instalação do Midview. Precisam rodar no
# Windows PowerShell 5.1, o que vem com o Windows 10/11.

$NomeServico = 'MidviewAgent'
$PastaDados = Join-Path $env:ProgramData 'Midview'

# O que a atualização pela rede usa (os mesmos nomes do Midview.Agent, em
# UpdatePaths): as versões recebidas, a tarefa que desfaz ao ligar a máquina
# uma atualização interrompida, e o registro da última tentativa.
$PastaAtualizacoes = Join-Path $env:ProgramFiles 'Midview.update'
$TarefaRecuperacao = 'Midview - Recuperar atualização'
$ArquivosDeAtualizacao = @('update-state.json', 'update-state.json.tmp', 'update-confirmed.json', 'update-confirmed.json.tmp')

function Write-Etapa([string]$Texto) {
    Write-Host ''
    Write-Host "==> $Texto" -ForegroundColor Cyan
}

function Assert-Administrador {
    $identidade = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal $identidade
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'É preciso rodar como administrador (clique com o botão direito no .cmd e escolha "Executar como administrador").'
    }
}

# A cópia espelha a pasta (apaga o que não veio do pacote), então ela nunca
# pode ser outra coisa que a pasta do próprio Midview.
function Assert-PastaDoMidview([string]$Pasta) {
    $nome = Split-Path -Path $Pasta -Leaf
    if (-not $nome -or $nome -notlike 'Midview*') {
        throw "A pasta de instalação precisa se chamar Midview... (recebi: $Pasta)."
    }
}

# A mesma regra do MidviewMaster (InstallPackage.NormalizeThumbprint).
function ConvertTo-ThumbprintNormalizado([string]$Valor) {
    if (-not $Valor) {
        return $null
    }
    $normalizado = $Valor.Trim().ToUpperInvariant()
    if ($normalizado -match '^[0-9A-F]{40}$') {
        return $normalizado
    }
    return $null
}

# Os professores que o pacote autoriza, gravados pelo MidviewMaster em
# "Instalar nos alunos..." > "Autorizar este professor no pacote".
function Read-ProfessoresAutorizados([string]$Caminho) {
    $comoAutorizar = 'No PC do professor, abra o MidviewMaster, clique em "Instalar nos alunos...", escolha a pasta do pacote e use "Autorizar este professor no pacote".'
    if (-not (Test-Path -LiteralPath $Caminho)) {
        throw "O pacote ainda não autoriza nenhum professor (falta $Caminho). $comoAutorizar"
    }

    # Atribuído antes do foreach: o ConvertFrom-Json do PowerShell 5.1 não
    # separa os itens de uma lista num pipeline.
    $lista = Get-Content -LiteralPath $Caminho -Raw -Encoding UTF8 | ConvertFrom-Json
    $validos = New-Object System.Collections.Generic.List[string]
    foreach ($item in $lista) {
        $thumbprint = ConvertTo-ThumbprintNormalizado $item
        if ($thumbprint -and -not $validos.Contains($thumbprint)) {
            $validos.Add($thumbprint)
        }
    }
    if ($validos.Count -eq 0) {
        throw "$Caminho não tem nenhum professor válido. $comoAutorizar"
    }
    return $validos.ToArray()
}

# O JSON dos arquivos abaixo é escrito à mão, e não com ConvertTo-Json: o do
# PowerShell 5.1 muda o formato de listas com um item só.
function ConvertTo-TextoJson([string]$Texto) {
    '"' + ($Texto -replace '\\', '\\' -replace '"', '\"') + '"'
}

# A lista do pacote substitui a da máquina: é assim que se tira a autorização
# de um professor (gere o pacote sem ele e reinstale).
function Write-ProfessoresAutorizados([string]$Caminho, [string[]]$Thumbprints) {
    $itens = foreach ($thumbprint in $Thumbprints) { ConvertTo-TextoJson $thumbprint }
    Write-TextoUtf8 $Caminho ('[' + ($itens -join ', ') + ']')
}

# A ficha que o MidviewMaster importa (InstallPackage.LoadInstalledMachines).
# Sem o endereço da placa de rede, "macAddress" fica null.
function New-FichaMaquina([string]$Nome, [string[]]$Enderecos, [string]$SessionThumbprint, [string]$AgentThumbprint, [string]$EnderecoMac, [datetime]$Quando) {
    $itens = foreach ($endereco in $Enderecos) { ConvertTo-TextoJson $endereco }
    $mac = if ($EnderecoMac) { ConvertTo-TextoJson $EnderecoMac } else { 'null' }
    @(
        '{'
        '  "computerName": ' + (ConvertTo-TextoJson $Nome) + ','
        '  "addresses": [' + ($itens -join ', ') + '],'
        '  "sessionThumbprint": ' + (ConvertTo-TextoJson $SessionThumbprint) + ','
        '  "agentThumbprint": ' + (ConvertTo-TextoJson $AgentThumbprint) + ','
        '  "macAddress": ' + $mac + ','
        '  "installedAt": ' + (ConvertTo-TextoJson $Quando.ToString('o'))
        '}'
    ) -join "`r`n"
}

# A mesma regra do MidviewMaster (WakeOnLan.NormalizeMacAddress): 12 dígitos
# hexadecimais, com ou sem "-" ou ":", que não sejam todos zero nem um
# endereço de grupo (multicast/broadcast). Devolve "AA-BB-CC-DD-EE-FF".
function ConvertTo-EnderecoMacNormalizado([string]$Valor) {
    if (-not $Valor) {
        return $null
    }
    $hex = ($Valor.Trim() -replace '[-:]', '').ToUpperInvariant()
    if ($hex -notmatch '^[0-9A-F]{12}$' -or $hex -eq '000000000000') {
        return $null
    }
    if ([Convert]::ToInt32($hex.Substring(0, 2), 16) -band 1) {
        return $null
    }
    return (($hex -split '(..)' | Where-Object { $_ }) -join '-')
}

# O endereço da placa de rede do laboratório (a primeira placa ligada com
# gateway, como em Get-EnderecosIPv4), para o professor poder ligar a
# máquina pela rede (Wake-on-LAN). Nunca impede a instalação: sem ele, o
# MidviewMaster aprende o endereço sozinho quando vê a máquina ligada.
function Get-EnderecoMac {
    try {
        foreach ($configuracao in @(Get-NetIPConfiguration -ErrorAction Stop)) {
            if ($configuracao.IPv4DefaultGateway -and $configuracao.NetAdapter.Status -eq 'Up') {
                $mac = ConvertTo-EnderecoMacNormalizado $configuracao.NetAdapter.MacAddress
                if ($mac) {
                    return $mac
                }
            }
        }
    }
    catch {
        # Sem Get-NetIPConfiguration: a ficha vai sem o endereço.
    }
    return $null
}

function Write-TextoUtf8([string]$Caminho, [string]$Texto) {
    [IO.File]::WriteAllText($Caminho, $Texto, (New-Object Text.UTF8Encoding $false))
}

# Primeiro os endereços das placas ligadas com gateway (normalmente a rede
# do laboratório), depois os demais; nunca loopback nem 169.254.x.x.
function Get-EnderecosIPv4 {
    $enderecos = New-Object System.Collections.Generic.List[string]
    try {
        foreach ($configuracao in @(Get-NetIPConfiguration -ErrorAction Stop)) {
            if ($configuracao.IPv4DefaultGateway -and $configuracao.NetAdapter.Status -eq 'Up') {
                foreach ($ip in @($configuracao.IPv4Address)) {
                    if ($ip.IPAddress -and -not $enderecos.Contains($ip.IPAddress)) {
                        $enderecos.Add($ip.IPAddress)
                    }
                }
            }
        }
    }
    catch {
        # Sem Get-NetIPConfiguration: fica só a lista abaixo.
    }
    try {
        foreach ($ip in @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop)) {
            $endereco = $ip.IPAddress
            if ($endereco -and $endereco -notlike '127.*' -and $endereco -notlike '169.254.*' -and -not $enderecos.Contains($endereco)) {
                $enderecos.Add($endereco)
            }
        }
    }
    catch {
        # Nunca impede a instalação: sem endereço, a ficha ainda pode ser
        # importada pelo nome do computador.
    }
    return $enderecos.ToArray()
}

# Para o serviço e a Session (que roda na sessão do aluno) para os arquivos
# poderem ser trocados. Parar o serviço já reativa teclado e mouse.
function Stop-Midview {
    $servico = Get-Service -Name $NomeServico -ErrorAction SilentlyContinue
    if ($servico -and $servico.Status -ne 'Stopped') {
        Stop-Service -Name $NomeServico -Force
        $servico.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
    Get-Process -Name 'Midview.Session', 'Midview.Agent' -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

# Uma atualização pela rede segura update.lock aberto, sem compartilhar, do
# começo ao fim. Instalar e desinstalar esperam ela terminar e seguram o
# arquivo até o fim, para nenhuma começar no meio. Devolve o arquivo aberto
# (feche com .Dispose()), ou $null se o Midview nunca rodou aqui (sem a
# pasta de dados, não há atualização).
function Enter-TravaDeAtualizacao([string]$Trava = (Join-Path $PastaDados 'update.lock'), [int]$Segundos = 120) {
    if (-not (Test-Path -LiteralPath (Split-Path -Path $Trava -Parent))) {
        return $null
    }
    $limite = (Get-Date).AddSeconds($Segundos)
    while ($true) {
        try {
            return [IO.File]::Open($Trava, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        }
        catch {
            if ((Get-Date) -ge $limite) {
                throw 'Uma atualização pela rede está em andamento nesta máquina e não terminou a tempo. Espere alguns minutos e rode de novo.'
            }
            Start-Sleep -Seconds 1
        }
    }
}

# O executável no PathName de um serviço ("C:\...\x.exe" --arg, ou sem
# aspas), ou $null.
function ConvertFrom-CaminhoDoServico([string]$PathName) {
    if (-not $PathName) {
        return $null
    }
    $caminho = $PathName.Trim()
    if ($caminho.StartsWith('"')) {
        $fim = $caminho.IndexOf('"', 1)
        if ($fim -lt 2) {
            return $null
        }
        return $caminho.Substring(1, $fim - 1)
    }
    return $caminho
}

# O Midview.Agent.exe para o qual o serviço aponta de verdade: depois de uma
# atualização pela rede, ele fica em Midview.update\<tentativa>. $null sem o
# serviço.
function Get-ExecutavelDoServico {
    try {
        $servico = Get-CimInstance -ClassName Win32_Service -Filter "Name='$NomeServico'" -ErrorAction Stop
    }
    catch {
        return $null
    }
    if (-not $servico) {
        return $null
    }
    $caminho = ConvertFrom-CaminhoDoServico $servico.PathName
    if ($caminho -and (Split-Path -Path $caminho -Leaf) -eq 'Midview.Agent.exe') {
        return $caminho
    }
    return $null
}

# Pelo Gerenciador de Serviços: o Set-Service do PowerShell 5.1 não troca o
# executável, e uma escrita direta no registro só vale depois de reiniciar a
# máquina (o serviço continuaria apontando para a versão da rede).
function Set-ExecutavelDoServico([string]$CaminhoServico) {
    $servico = Get-CimInstance -ClassName Win32_Service -Filter "Name='$NomeServico'" -ErrorAction Stop
    $resultado = Invoke-CimMethod -InputObject $servico -MethodName Change -Arguments @{ PathName = $CaminhoServico } -ErrorAction Stop
    if ($resultado.ReturnValue -ne 0) {
        throw "Não foi possível apontar o serviço $NomeServico para $CaminhoServico (Win32_Service.Change devolveu $($resultado.ReturnValue))."
    }
}

# Depois de instalar pelo pendrive (ou para desinstalar), nada do que uma
# atualização pela rede deixou vale mais. Só com o serviço parado e
# apontando para fora de Midview.update.
function Remove-RestosDeAtualizacao {
    try {
        if (Get-ScheduledTask -TaskName $TarefaRecuperacao -ErrorAction SilentlyContinue) {
            Unregister-ScheduledTask -TaskName $TarefaRecuperacao -Confirm:$false -ErrorAction Stop
        }
    }
    catch {
        Write-Warning "Não foi possível apagar a tarefa agendada `"$TarefaRecuperacao`": $($_.Exception.Message)"
    }
    foreach ($nome in $ArquivosDeAtualizacao) {
        Remove-Item -LiteralPath (Join-Path $PastaDados $nome) -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $PastaAtualizacoes) {
        try {
            Remove-Item -LiteralPath $PastaAtualizacoes -Recurse -Force -ErrorAction Stop
        }
        catch {
            Write-Warning "Não foi possível apagar $PastaAtualizacoes (a próxima atualização pela rede apaga): $($_.Exception.Message)"
        }
    }
}

# Roda um programa deixando as mensagens de erro dele aparecerem, sem que o
# PowerShell as transforme em erro do script.
function Invoke-Programa([string]$Caminho, [string[]]$Argumentos) {
    $anterior = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $saida = & $Caminho @Argumentos
        $codigo = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $anterior
    }
    [pscustomobject]@{ Saida = (@($saida) -join "`n"); Codigo = $codigo }
}

function Copy-Pasta([string]$Origem, [string]$Destino) {
    Assert-PastaDoMidview $Destino
    & robocopy.exe $Origem $Destino /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    # Códigos abaixo de 8 do robocopy são sucesso.
    if ($LASTEXITCODE -ge 8) {
        throw "A cópia de $Origem para $Destino falhou (robocopy, código $LASTEXITCODE)."
    }
    # Um pacote baixado como .zip e extraído vem marcado como "da internet",
    # e o Windows avisaria (SmartScreen) ao abrir o MidviewMaster.
    Get-ChildItem -LiteralPath $Destino -Recurse -File | Unblock-File
}

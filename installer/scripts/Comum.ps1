# Funções usadas pelos scripts de instalação do Midview. Precisam rodar no
# Windows PowerShell 5.1, o que vem com o Windows 10/11.

$NomeServico = 'MidviewAgent'
$PastaDados = Join-Path $env:ProgramData 'Midview'

# O que a atualização pela rede usa (os mesmos nomes do Midview.Agent, em
# UpdatePaths): as versões recebidas, a tarefa que desfaz ao ligar a máquina
# uma atualização interrompida, e a pasta (só do sistema e dos
# administradores) com a trava e o registro da última tentativa.
$PastaAtualizacoes = Join-Path $env:ProgramFiles 'Midview.update'
$TarefaRecuperacao = 'Midview - Recuperar atualização'
$PastaEstadoAtualizacao = Join-Path $PastaDados 'update'
$ArquivosDeAtualizacao = @('update-state.json', 'update-state.json.tmp', 'update-confirmed.json', 'update-confirmed.json.tmp')

function Write-Etapa([string]$Texto) {
    Write-Host ''
    Write-Host "==> $Texto" -ForegroundColor Cyan
}

function Assert-Administrador {
    $identidade = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal $identidade
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'É preciso rodar como administrador.'
    }
}

# A pasta que estes scripts tratam (e o desinstalador apaga) nunca pode ser
# outra coisa que a pasta do próprio Midview.
function Assert-PastaDoMidview([string]$Pasta) {
    $nome = Split-Path -Path $Pasta -Leaf
    if (-not $nome -or $nome -notlike 'Midview*') {
        throw "A pasta de instalação precisa se chamar Midview... (recebi: $Pasta)."
    }
}

# O instalador (installer\Midview.iss) mostra ao professor a mensagem de
# erro gravada aqui, em UTF-8, que ele lê com os acentos certos.
function Write-Erro([string]$Caminho, [string]$Mensagem) {
    if (-not $Caminho) {
        return
    }
    try {
        [IO.File]::WriteAllText($Caminho, $Mensagem, (New-Object Text.UTF8Encoding $false))
    }
    catch {
        # Sem o arquivo, o instalador mostra só o código de saída.
    }
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
# (feche com .Dispose()), ou $null se nunca houve um Midview com atualização
# pela rede aqui (sem a pasta dela, não há atualização).
function Enter-TravaDeAtualizacao([string]$Trava = (Join-Path $PastaEstadoAtualizacao 'update.lock'), [int]$Segundos = 120) {
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

# Depois de instalar pelo MidviewSetup (ou para desinstalar), nada do que uma
# atualização pela rede deixou vale mais. Só com o serviço parado e
# apontando para fora de Midview.update. -Obrigatorio (ao desinstalar, quando
# não fica Agent nenhum para apagar depois) falha se Midview.update ficar.
function Remove-RestosDeAtualizacao([switch]$Obrigatorio) {
    try {
        if (Get-ScheduledTask -TaskName $TarefaRecuperacao -ErrorAction SilentlyContinue) {
            Unregister-ScheduledTask -TaskName $TarefaRecuperacao -Confirm:$false -ErrorAction Stop
        }
    }
    catch {
        Write-Warning "Não foi possível apagar a tarefa agendada `"$TarefaRecuperacao`": $($_.Exception.Message)"
    }
    foreach ($nome in $ArquivosDeAtualizacao) {
        Remove-Item -LiteralPath (Join-Path $PastaEstadoAtualizacao $nome) -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $PastaAtualizacoes) {
        try {
            Remove-Item -LiteralPath $PastaAtualizacoes -Recurse -Force -ErrorAction Stop
        }
        catch {
            if ($Obrigatorio) {
                throw "Não foi possível apagar ${PastaAtualizacoes}: $($_.Exception.Message)"
            }
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

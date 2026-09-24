# Instala ou atualiza o MidviewMaster para o usuário atual (não precisa de
# administrador). Rodado por Instalar-Professor.cmd.
[CmdletBinding()]
param(
    [string]$Pacote,
    [string]$Destino = (Join-Path $env:LOCALAPPDATA 'Programs\Midview Professor')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Comum.ps1')
if (-not $Pacote) {
    $Pacote = Split-Path -Path (Split-Path -Path $PSScriptRoot -Parent) -Parent
}

function New-Atalho([string]$Caminho, [string]$Alvo) {
    $shell = New-Object -ComObject WScript.Shell
    $atalho = $shell.CreateShortcut($Caminho)
    $atalho.TargetPath = $Alvo
    $atalho.WorkingDirectory = Split-Path -Path $Alvo -Parent
    $atalho.Save()
}

try {
    Assert-PastaDoMidview $Destino
    $origem = Join-Path $Pacote 'programa\professor'
    if (-not (Test-Path -LiteralPath (Join-Path $origem 'Midview.Master.exe'))) {
        throw "Não achei o MidviewMaster em $origem. O pacote está completo?"
    }
    if (Get-Process -Name 'Midview.Master' -ErrorAction SilentlyContinue) {
        throw 'Feche o MidviewMaster antes de instalar ou atualizar.'
    }

    Write-Etapa "Copiando o MidviewMaster para $Destino"
    Copy-Pasta $origem $Destino

    Write-Etapa 'Criando os atalhos'
    $executavel = Join-Path $Destino 'Midview.Master.exe'
    New-Atalho (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Midview - Sala de aula.lnk') $executavel
    New-Atalho (Join-Path ([Environment]::GetFolderPath('Programs')) 'Midview - Sala de aula.lnk') $executavel

    Write-Host ''
    Write-Host 'MidviewMaster instalado: abra "Midview - Sala de aula" na Área de Trabalho.' -ForegroundColor Green
    Write-Host 'Para preparar o pendrive dos alunos, use "Instalar nos alunos..." no MidviewMaster.'
    exit 0
}
catch {
    Write-Host ''
    Write-Host "A instalação NÃO terminou: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

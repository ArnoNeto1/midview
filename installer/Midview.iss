; Midview's setup program: one .exe that installs either the teacher's
; program ("Professor") or a lab machine ("Aluno"). See
; docs/superpowers/specs/2026-10-05-installer-and-pairing-design.md.
;
; Built by installer\Gerar-Instalador.ps1, which compiles a UTF-8-with-BOM
; copy of this file and passes:
;   /DVersao=0.0.1                   Midview's version (Directory.Build.props): the
;                                    wizard, Installed apps and MidviewSetup.exe's properties
;   /DPrograma=<folder>              aluno\, professor\ and instalador\
;   /DSaida=<folder>                 where the setup goes
;   /DAssets=<folder>                the repository's assets\ (icon and wizard images,
;                                    made by assets\gerar-icone.py)
;
; Exit codes beyond Inno's own: 10 = the files were copied, but the
; MidviewAgent service could not be configured (Configurar-Aluno.ps1).

#ifndef Versao
  #define Versao "0.0.0"
#endif
#ifndef Programa
  #error Passe /DPrograma=<pasta com aluno, professor e instalador>: use installer\Gerar-Instalador.ps1.
#endif
#ifndef Saida
  #define Saida "."
#endif
#ifndef Assets
  #error Passe /DAssets=<pasta assets do repositório>: use installer\Gerar-Instalador.ps1.
#endif

[Setup]
AppId={{6F2B1C7E-4D3A-4B8E-9C61-2E7A5D0B9F14}
AppName=Midview
AppVersion={#Versao}
AppVerName=Midview {#Versao}
AppPublisher=Midview
VersionInfoVersion={#Versao}
; The same text in the setup's properties, not a padded "0.0.1.0".
VersionInfoTextVersion={#Versao}
VersionInfoProductTextVersion={#Versao}
DefaultDirName={code:PastaPadrao}
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
; The welcome page (off by default in Inno 6) shows the logo first.
DisableWelcomePage=no
UninstallDisplayName={code:NomeNaLista}
UninstallDisplayIcon={app}\{code:ExecutavelPrincipal}
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
AppMutex=Midview.Master,Global\Midview.Master
CloseApplications=no
RestartApplications=no
WizardStyle=modern
SetupIconFile={#Assets}\midview.ico
; Tells Explorer at the end that icons may have changed: an update keeps the
; same paths, and the taskbar otherwise keeps the old icon from its cache.
ChangesAssociations=yes
; Inno picks the file that fits the screen's scale.
WizardImageFile={#Assets}\instalador-grande-164.bmp,{#Assets}\instalador-grande-328.bmp
WizardSmallImageFile={#Assets}\instalador-pequena-55.bmp,{#Assets}\instalador-pequena-69.bmp,{#Assets}\instalador-pequena-83.bmp,{#Assets}\instalador-pequena-110.bmp
; The repository's LICENSE (Gerar-Instalador.ps1 copies it next to the
; programs): the wizard asks to accept it, a silent install clicks through.
LicenseFile={#Programa}\aluno\LICENSE.txt
OutputDir={#Saida}
OutputBaseFilename=MidviewSetup
Compression=lzma2
SolidCompression=yes
SetupLogging=yes

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "{#Programa}\aluno\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: EhAluno
Source: "{#Programa}\instalador\*.ps1"; DestDir: "{app}\instalador"; Flags: ignoreversion; Check: EhAluno
Source: "{#Programa}\professor\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: EhProfessor
Source: "{#Programa}\aluno\*"; DestDir: "{app}\aluno"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: EhProfessor
; For PrepareToInstall, before anything is copied.
Source: "{#Programa}\instalador\Comum.ps1"; Flags: dontcopy
Source: "{#Programa}\instalador\Parar-Aluno.ps1"; Flags: dontcopy

[Icons]
Name: "{commondesktop}\Midview - Sala de aula"; Filename: "{app}\Midview.Master.exe"; Check: EhProfessor
Name: "{commonprograms}\Midview - Sala de aula"; Filename: "{app}\Midview.Master.exe"; Check: EhProfessor

[Run]
; The last page's "open it now" box, checked by default. The Master runs
; without administrator rights, so it opens as the user who ran the setup;
; a silent install opens nothing.
Filename: "{app}\Midview.Master.exe"; Description: "Executar o Midview - Sala de aula"; Flags: postinstall nowait skipifsilent runasoriginaluser; Check: EhProfessor

[InstallDelete]
; What Instalar-Professor.cmd (the USB-stick package) installed for one user.
Type: filesandordirs; Name: "{localappdata}\Programs\Midview Professor"; Check: EhProfessor
Type: files; Name: "{userdesktop}\Midview - Sala de aula.lnk"; Check: EhProfessor
Type: files; Name: "{userprograms}\Midview - Sala de aula.lnk"; Check: EhProfessor

[Registry]
Root: HKLM; Subkey: "Software\Midview"; ValueType: string; ValueName: "Tipo"; ValueData: "{code:TipoEscolhido}"; Flags: uninsdeletekey

[UninstallDelete]
; Everything in it, including files left by an older USB-stick install.
Type: filesandordirs; Name: "{app}"

[Code]
const
  TipoProfessor = 'professor';
  TipoAluno = 'aluno';
  CodigoFalhaAoConfigurar = 10;
  { "Dar acesso a um colega": the teacher's program listens here while an
    invite is open (TeacherInvite.Port). }
  PortaConvite = '28902';
  RegraConviteTcp = 'MidviewProfessorConvite';
  RegraConviteUdp = 'MidviewProfessorConviteBusca';
  RegraBuscaRede = 'MidviewProfessorBuscaRede';
  { "Pasta da turma" with two teacher PCs in one lab: the teacher's program
    that keeps the class folders listens here for the other teacher PCs
    (ClassFolderRemote.Port). }
  PortaPastaDaTurma = '28903';
  RegraPastaDaTurma = 'MidviewProfessorPastaDaTurma';

var
  PaginaTipo: TInputOptionWizardPage;
  Tipo: String;
  TipoAnterior: String;
  FalhaAoConfigurar: Boolean;

{ The kind already installed on this computer, or ''. }
function TipoInstalado: String;
begin
  if not RegQueryStringValue(HKLM, 'Software\Midview', 'Tipo', Result) then
    Result := '';
end;

function NomeDoTipo(const Valor: String): String;
begin
  if Valor = TipoProfessor then
    Result := 'Professor'
  else
    Result := 'Aluno';
end;

function EhAluno: Boolean;
begin
  Result := Tipo = TipoAluno;
end;

function EhProfessor: Boolean;
begin
  Result := Tipo = TipoProfessor;
end;

function TipoEscolhido(Param: String): String;
begin
  Result := Tipo;
end;

function NomeNaLista(Param: String): String;
begin
  Result := 'Midview ' + NomeDoTipo(Tipo);
end;

function ExecutavelPrincipal(Param: String): String;
begin
  if EhProfessor then
    Result := 'Midview.Master.exe'
  else
    Result := 'Midview.Agent.exe';
end;

function PastaDoTipo(const Valor: String): String;
begin
  if Valor = TipoProfessor then
    Result := ExpandConstant('{autopf}\Midview Professor')
  else
    Result := ExpandConstant('{autopf}\Midview');
end;

function PastaPadrao(Param: String): String;
begin
  Result := PastaDoTipo(Tipo);
end;

function RecusaOutroTipo(const Escolhido: String): String;
begin
  Result := '';
  if (TipoAnterior <> '') and (Escolhido <> TipoAnterior) then
    Result := 'Este computador já tem o Midview ' + NomeDoTipo(TipoAnterior) +
      '. Desinstale-o antes de instalar como ' + NomeDoTipo(Escolhido) + '.';
end;

{ The script's error message (UTF-8, see Write-Erro in Comum.ps1), or ''. }
function LerErro(const Arquivo: String): String;
var
  Linhas: TArrayOfString;
  I: Integer;
begin
  Result := '';
  if LoadStringsFromFile(Arquivo, Linhas) then
    for I := 0 to GetArrayLength(Linhas) - 1 do
      Result := Result + Linhas[I] + #13#10;
  Result := Trim(Result);
end;

function RodarScript(const Script, Argumentos, ArquivoDeErro: String; var Codigo: Integer): Boolean;
begin
  DeleteFile(ArquivoDeErro);
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -ExecutionPolicy Bypass -File "' + Script + '" -Erro "' + ArquivoDeErro + '"' + Argumentos,
    '', SW_HIDE, ewWaitUntilTerminated, Codigo);
  Log(Format('%s%s: started=%d, exit code=%d', [Script, Argumentos, Ord(Result), Codigo]));
end;

function RodarNetsh(const Argumentos: String): Boolean;
var
  Codigo: Integer;
begin
  Codigo := -1;
  Result := Exec(ExpandConstant('{sys}\netsh.exe'), Argumentos, '', SW_HIDE, ewWaitUntilTerminated, Codigo) and (Codigo = 0);
  Log(Format('netsh %s: exit code=%d', [Argumentos, Codigo]));
end;

{ "add rule" never replaces a rule with the same name, so they are deleted
  first (deleting one that isn't there just fails, harmlessly). The class
  folders' rule goes with the invite's. }
procedure ApagarRegrasDoConvite;
begin
  RodarNetsh('advfirewall firewall delete rule name="' + RegraConviteTcp + '"');
  RodarNetsh('advfirewall firewall delete rule name="' + RegraConviteUdp + '"');
  RodarNetsh('advfirewall firewall delete rule name="' + RegraPastaDaTurma + '"');
  RodarNetsh('advfirewall firewall delete rule name="' + RegraBuscaRede + '"');
end;

{ Coming in to this program only, from the local network only, on that port. }
function RestricaoDaPorta(const Porta: String): String;
begin
  Result := ' dir=in action=allow localport=' + Porta +
    ' program="' + ExpandConstant('{app}\Midview.Master.exe') + '" remoteip=localsubnet profile=any';
end;

{ The Master runs without administrator rights, so it can't open its own
  ports: the setup does, for this program and the local network only. The
  invite's ("Dar acesso a um colega") and the class folders' (another teacher
  PC working this one's "Pasta da turma") are made together. }
function CriarRegrasDoConvite: Boolean;
begin
  ApagarRegrasDoConvite;
  Result := RodarNetsh('advfirewall firewall add rule name="' + RegraConviteTcp + '" protocol=TCP' + RestricaoDaPorta(PortaConvite)) and
    RodarNetsh('advfirewall firewall add rule name="' + RegraConviteUdp + '" protocol=UDP' + RestricaoDaPorta(PortaConvite)) and
    RodarNetsh('advfirewall firewall add rule name="' + RegraPastaDaTurma + '" protocol=TCP' + RestricaoDaPorta(PortaPastaDaTurma)) and
    RodarNetsh('advfirewall firewall add rule name="' + RegraBuscaRede + '" protocol=UDP dir=in action=allow program="' + ExpandConstant('{app}\Midview.Master.exe') + '" remoteip=localsubnet profile=any');
end;

function DescreverFalha(const ArquivoDeErro: String; Codigo: Integer): String;
begin
  Result := LerErro(ArquivoDeErro);
  if Result = '' then
    Result := 'o script terminou com o código ' + IntToStr(Codigo) + '.';
end;

function InitializeSetup: Boolean;
var
  Pedido, Recusa: String;
begin
  Result := True;
  TipoAnterior := TipoInstalado;
  Pedido := Lowercase(Trim(ExpandConstant('{param:TIPO|}')));
  if Pedido <> '' then begin
    if (Pedido <> TipoProfessor) and (Pedido <> TipoAluno) then begin
      SuppressibleMsgBox('/TIPO precisa ser "professor" ou "aluno" (recebi "' + Pedido + '").', mbError, MB_OK, IDOK);
      Result := False;
      Exit;
    end;
    Recusa := RecusaOutroTipo(Pedido);
    if Recusa <> '' then begin
      SuppressibleMsgBox(Recusa, mbError, MB_OK, IDOK);
      Result := False;
      Exit;
    end;
    Tipo := Pedido;
  end else if WizardSilent then begin
    Log('Silent install without /TIPO: nothing installed.');
    SuppressibleMsgBox('Na instalação silenciosa, diga o tipo: /TIPO=professor ou /TIPO=aluno.', mbError, MB_OK, IDOK);
    Result := False;
  end;
end;

procedure InitializeWizard;
begin
  PaginaTipo := CreateInputOptionPage(wpLicense,
    'Tipo de instalação', 'Este computador é o do professor ou uma máquina de aluno?',
    'Escolha uma opção e clique em Avançar.', True, False);
  PaginaTipo.Add('Professor: o computador de onde você controla a sala');
  PaginaTipo.Add('Aluno: uma máquina do laboratório');
  if (Tipo = TipoProfessor) or ((Tipo = '') and (TipoAnterior = TipoProfessor)) then
    PaginaTipo.SelectedValueIndex := 0
  else if (Tipo = TipoAluno) or ((Tipo = '') and (TipoAnterior = TipoAluno)) then
    PaginaTipo.SelectedValueIndex := 1;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = PaginaTipo.ID) and (ExpandConstant('{param:TIPO|}') <> '');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Escolhido, Recusa: String;
begin
  Result := True;
  if CurPageID <> PaginaTipo.ID then
    Exit;

  if PaginaTipo.SelectedValueIndex = 0 then
    Escolhido := TipoProfessor
  else if PaginaTipo.SelectedValueIndex = 1 then
    Escolhido := TipoAluno
  else begin
    MsgBox('Escolha Professor ou Aluno.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  Recusa := RecusaOutroTipo(Escolhido);
  if Recusa <> '' then begin
    MsgBox(Recusa, mbError, MB_OK);
    Result := False;
    Exit;
  end;

  Tipo := Escolhido;
  WizardForm.DirEdit.Text := PastaDoTipo(Tipo);
end;

{ Aluno: stop the Midview that is there before its files are replaced. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ArquivoDeErro: String;
  Codigo: Integer;
begin
  Result := '';
  if not EhAluno then
    Exit;
  ExtractTemporaryFile('Comum.ps1');
  ExtractTemporaryFile('Parar-Aluno.ps1');
  ArquivoDeErro := ExpandConstant('{tmp}\parar-erro.txt');
  if not RodarScript(ExpandConstant('{tmp}\Parar-Aluno.ps1'), '', ArquivoDeErro, Codigo) or (Codigo <> 0) then
    Result := 'Não foi possível parar o Midview desta máquina: ' + DescreverFalha(ArquivoDeErro, Codigo);
end;

{ Once the files are in place. Professor: open the ports of the invite and
  of the class folders. Aluno: set up and start the service. }
procedure CurStepChanged(CurStep: TSetupStep);
var
  ArquivoDeErro: String;
  Codigo: Integer;
begin
  if CurStep <> ssPostInstall then
    Exit;
  if EhProfessor then begin
    if not CriarRegrasDoConvite then
      SuppressibleMsgBox('O Midview Professor foi instalado, mas as regras do firewall não puderam ser criadas: ' +
        '"Dar acesso a um colega" não vai funcionar neste computador, e a "Pasta da turma" dele não vai poder ' +
        'ser usada de outro PC de professor. Rode o instalador de novo.',
        mbError, MB_OK, IDOK);
    Exit;
  end;
  if not EhAluno then
    Exit;
  WizardForm.StatusLabel.Caption := 'Configurando o serviço do Midview...';
  ArquivoDeErro := ExpandConstant('{tmp}\configurar-erro.txt');
  if not RodarScript(ExpandConstant('{app}\instalador\Configurar-Aluno.ps1'), '', ArquivoDeErro, Codigo) or (Codigo <> 0) then begin
    FalhaAoConfigurar := True;
    SuppressibleMsgBox('Os arquivos foram copiados, mas o Midview não pôde ser configurado: ' +
      DescreverFalha(ArquivoDeErro, Codigo) + #13#10#13#10 + 'Corrija o problema e rode o instalador de novo.',
      mbError, MB_OK, IDOK);
  end;
end;

function GetCustomSetupExitCode: Integer;
begin
  if FalhaAoConfigurar then
    Result := CodigoFalhaAoConfigurar
  else
    Result := 0;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID <> wpFinished then
    Exit;
  if EhAluno and not FalhaAoConfigurar then
    WizardForm.FinishedLabel.Caption := 'O Midview Aluno está instalado e rodando nesta máquina.' + #13#10#13#10 +
      'Agora abra o Midview Professor no computador do professor e clique em "Máquinas da sala" > "Procurar na rede". ' +
      'Pareie esta máquina logo: até lá, qualquer professor da rede pode pareá-la.'
  else if EhProfessor then
    WizardForm.FinishedLabel.Caption := 'O Midview Professor está instalado: abra "Midview - Sala de aula" na Área de Trabalho.' + #13#10#13#10 +
      'Para trazer as máquinas, instale o Midview Aluno nelas e clique em "Máquinas da sala" > "Procurar na rede".';
end;

{ Professor: close the ports of the invite and of the class folders. Aluno:
  undo what a lesson left and remove the service, before the files go. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Argumentos, ArquivoDeErro: String;
  Codigo: Integer;
begin
  if CurUninstallStep <> usUninstall then
    Exit;
  if TipoInstalado = TipoProfessor then begin
    ApagarRegrasDoConvite;
    Exit;
  end;
  if TipoInstalado <> TipoAluno then
    Exit;

  Argumentos := '';
  if not UninstallSilent then
    if MsgBox('Apagar também os professores pareados e a identidade desta máquina?' + #13#10#13#10 +
        'Escolha Não para manter: instalando de novo, ela continua pareada com os mesmos professores.',
        mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      Argumentos := ' -RemoverDados';

  ArquivoDeErro := AddBackslash(GetTempDir) + 'midview-desinstalar-erro.txt';
  if not RodarScript(ExpandConstant('{app}\instalador\Desinstalar-Aluno.ps1'), Argumentos, ArquivoDeErro, Codigo) or (Codigo <> 0) then
    SuppressibleMsgBox('A limpeza do Midview não foi completa: ' + DescreverFalha(ArquivoDeErro, Codigo) + #13#10#13#10 +
      'Confira se a internet, os sites e os programas estão liberados nesta máquina.', mbError, MB_OK, IDOK);
end;

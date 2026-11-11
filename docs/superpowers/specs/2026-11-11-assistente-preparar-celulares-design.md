# Assistente "Preparar celulares" (desenho)

Data: 2026-11-11. Mudanças no `src/Midview.Master` (programa do professor) e
no agente Android (`src/Midview.Android`). Continua o desenho
`2026-11-11-agente-android-design.md` e depende do plano 1 dele
(`docs/superpowers/plans/2026-11-11-agente-android-1-base.md`).

## Intenção

Hoje, para pôr um celular da escola no Midview, o professor precisa saber de
cor uma sequência que nenhum programa faz por ele: sair da conta Mi, restaurar
o celular, tocar 6 vezes na tela de boas-vindas, escanear o QR de "Preparar
celular" (escondido em Máquinas da sala → Procurar e parear) e, quando o
celular termina, parear com o código de 6 dígitos. Quem não conhece Android
trava em qualquer um desses pontos.

O professor quer que **qualquer outro professor, mesmo quem não sabe nada de
celular, prepare os celulares da escola sozinho**, só com o Midview aberto no
PC do professor, sem ajuda e sem cabo USB.

## Decidido pelo professor

- **Quem usa:** professores que podem não saber nada de celular nem de
  computador. Estão sozinhos na frente do Midview do PC do professor, sem
  vídeo, folha impressa ou alguém para perguntar.
- **Os celulares:** da escola, da mesma marca (Xiaomi), modelos diferentes:
  Redmi 10C, Redmi Note 10S, Redmi Note 9 Pro ("até onde eu sei"). **Todos
  podem ser apagados.** Cada um tem **um número escrito atrás**.
- **Um PC só:** os celulares ficam sempre com a mesma turma e o mesmo PC de
  professor.
- **Formato:** um **assistente de um passo por vez** (mockup "A · Um passo por
  vez", em `.superpowers/brainstorm/2043-1794420649/content/passo-a-passo.html`),
  **bem detalhado**, "pois tem professor que não vai achar direito".
- **Cada passo** tem: título curto, os toques um por linha com os nomes exatos
  dos menus, a imagem da tela do celular com o lugar do toque contornado em
  vermelho, quanto tempo leva, o que aparece quando dá certo, e um **"Deu
  errado?"** com os problemas comuns daquele passo e a saída de cada um
  (mockup aprovado:
  `.superpowers/brainstorm/2043-1794420649/content/passos-com-prints.html`).
- **Imagens:** prints reais do Redmi 10C, tirados pelo `adb` sem tocar em nada
  (feito nesta conversa, com o nome do Wi-Fi borrado). As telas que só existem
  depois de restaurar (boas-vindas, leitor de QR) e a tela da conta Mi (bloqueia
  print) começam como **desenho** e são trocadas pelo print real depois da
  primeira preparação de verdade.
- **O número de trás vira o nome:** o assistente pergunta o número no início,
  o número vai dentro do QR e o celular aparece no Midview como **"Celular 07"**
  sem precisar renomear.
- **Sem código de 6 dígitos:** o celular preparado pelo assistente **entra na
  turma sozinho**, já pareado com este PC.
- **Onde fica:** quando o filtro **Celulares** está vazio, o espaço vazio mostra
  um **botão grande "Preparar celulares"**. O link "Preparar celular..." em
  Máquinas da sala → Procurar e parear continua, para os celulares seguintes.
- **Fora do assistente:** a otimização por cabo USB e o script
  `celulares/otimizar-celulares.ps1`. Como todos os celulares podem ser
  apagados, o caminho do QR cobre tudo; o script fica só como ferramenta do
  professor que o escreveu.
- **Anti-objetivo principal:** o professor **travar no meio e não saber o que
  fazer**, mesmo que no fim o celular fique pronto.
- **Preparar de novo um celular que já é da escola** (o agente bloqueia a
  Redefinição de fábrica nas Configurações): um **botão no Midview, "Apagar e
  preparar de novo"**, que só o PC em que o celular confia pode usar (não o
  modo de recuperação).
- **O Redmi 10C pode ser apagado durante a construção** para uma preparação de
  verdade, com a qual se tiram os prints das telas que aparecem depois do QR.

## Objetivos

### 1. Abrir o assistente

- Filtro **Celulares** vazio: em vez do texto de hoje (`GridFilter`, "Nenhum
  celular nesta sala ainda. Para colocar o primeiro, abra..."), um botão
  grande **"Preparar celulares"** e uma frase curta ("Nenhum celular nesta sala
  ainda.").
- O link de Máquinas da sala → Procurar e parear abre o mesmo assistente (a
  `PreparePhoneWindow` de hoje é substituída por ele).
- Enquanto o agente Android não estiver publicado (`PhoneProvisioning.IsReady`
  falso), o botão e o link não aparecem, como hoje.

### 2. Wi-Fi do laboratório (só na primeira vez)

- Se não há Wi-Fi guardado (`PhoneWifiFile`), a primeira tela pede o nome e a
  senha, com as mesmas regras de hoje (nome obrigatório, senha WPA com 8
  caracteres ou mais) e o aviso de que fica guardado só neste PC.
- Com Wi-Fi guardado, o assistente pula essa tela e mostra, discreto, "Wi-Fi:
  <nome> · trocar".
- Dica no "Deu errado?": o Wi-Fi tem de ser de senha comum (WPA2); rede com
  usuário e senha (empresarial) ou com página de login não funciona.

### 3. Os passos, para cada celular

Um passo por tela, com **Voltar** e um botão de avançar escrito como o
professor confirma ("Já tirei, próximo passo", "Apareceu o Bem-vindo"). Na
ordem:

1. **Número do celular.** "Qual número está escrito atrás deste celular?"
   Campo para o número. Se o número já existe na turma, avisa ("Já existe um
   Celular 07. Vai preparar de novo o 07?"). Se o professor confirmar e o
   Celular 07 está online, o assistente oferece **"Apagar e preparar de novo"**
   (objetivo 6) e pula para o passo 6; se está desligado, segue os passos
   normais e o antigo é trocado pelo novo quando este aparecer.
2. **Bateria.** Ligar o celular na tomada ou conferir que tem bateria (a
   preparação leva uns 10 a 15 minutos).
3. **Tirar a conta Mi.** Configurações → Conta Mi (no topo) → desligar
   "Encontrar dispositivo" → Sair (pede a senha da conta). "Deu errado?":
   "Não aparece Conta Mi no topo" → não há conta, siga; "Ninguém sabe a senha"
   → ver a ajuda "Celular com conta desconhecida".
4. **Tirar a conta Google.** Configurações → Contas e sincronização → Google →
   a conta → Mais → Remover conta. "Deu errado?": "Não aparece Google" → não há
   conta, siga; "Pediu a senha e ninguém sabe" → ajuda "Celular com conta
   desconhecida".
5. **Restaurar de fábrica.** Configurações → Sobre o telefone → descer até o
   fim → Redefinição de fábrica → Limpar todos os dados → confirmar. Leva de 3
   a 7 minutos; acaba na tela "Bem-vindo". "Deu errado?": "Pediu um PIN que
   ninguém sabe" → ajuda "Restaurar sem o PIN".
6. **Seis toques e o código.** Na tela "Bem-vindo", tocar 6 vezes seguidas numa
   parte vazia; abre a câmera; apontar para o QR (que já leva o Wi-Fi e o
   número). "Deu errado?": "A câmera não abriu" → tocar mais rápido, sempre no
   mesmo lugar; "Apareceu uma mensagem de erro" → lista das mensagens
   conhecidas e o que fazer (ver objetivo 5).

Os passos 3 e 4 vêm **antes** de restaurar e o assistente insiste nisso: um
celular restaurado com a conta Mi ou Google ainda dentro trava na volta e pede
a senha da conta; com conta Google sobrando, o QR não passa (ver Pesquisa).

**Telas depois do QR.** Depois do scan o celular mostra telas próprias (o
consentimento "Aceitar e continuar" do Android e o resto da configuração da
MIUI). Elas ganham passos no assistente, com print real, tirados numa
preparação de verdade do Redmi 10C durante a construção (decidido pelo
professor). Até essa preparação, o passo 6 tem um texto genérico ("toque em
Aceitar/Continuar até aparecer a tela do Midview").

**Outras versões do MIUI.** *(Escolha minha, o professor não sabia a versão
dos outros modelos.)* Os textos e prints são do MIUI 14. Em vez de perguntar o
modelo, cada passo de menu traz no "Deu errado?" o caminho do MIUI 12/13
("Não achei Redefinição de fábrica" → Configurações → Configurações adicionais
→ Backup e redefinição) e a dica de usar a busca das Configurações com a
palavra do item.

### 4. Esperar o celular e entrar na turma sozinho

- Depois do QR, a tela vira **"Esperando o Celular 07 terminar…"**, com o que
  está acontecendo no celular em linguagem simples ("o celular está entrando
  no Wi-Fi e baixando o Midview; isso leva alguns minutos").
- Quando o celular aparece na rede, o Midview o **adiciona sozinho** à turma
  com o nome "Celular 07", sem código de 6 dígitos, e ele aparece no filtro
  Celulares. A otimização roda sozinha, como já está no desenho do agente.
- *(Escolha minha.)* Isso vale **mesmo com o assistente fechado**, depois dos
  15 minutos de espera ou com o Midview reiniciado: as provas de QR ainda
  válidas ficam guardadas no PC (protegidas pelo Windows, como o Wi-Fi) e a
  janela principal adiciona sozinha o celular que aparecer com uma delas. Se a
  prova venceu, o celular continua aceitando o pareamento com código de hoje
  (ainda não confia em ninguém), e o "ainda não apareceu" ensina esse caminho.
- O celular mostra, ao terminar, uma tela **"Pronto. Este é o Celular 07 da
  escola."**, para quem está com ele na mão saber que acabou.
- O assistente mostra **"Celular 07 pronto ✓ (3 de 11)"** com **"Preparar o
  próximo"** (volta ao passo 1, com o Wi-Fi já guardado) e **"Terminei"**. A
  contagem é de celulares na turma; o "de 11" vem do total que o professor
  disser *(escolha minha: perguntado uma vez, opcional; sem ele, mostra só
  "3 celulares na turma")*.

### 5. Nunca deixar o professor travado

- Todo passo tem o "Deu errado?" com as saídas daquele passo.
- Se o celular não aparece em **15 minutos** *(escolha minha)* depois do QR, o
  assistente troca a espera por **"O Celular 07 ainda não apareceu"** e uma
  lista do que conferir, na ordem: a mensagem na tela do celular (com as
  mensagens conhecidas e o que fazer), o Wi-Fi certo, o celular perto do
  roteador, e por fim "Recomeçar este celular" (volta ao passo 5). Se o
  celular já mostra a tela do Midview mas não aparece no PC, o problema é de
  rede (mesmo Wi-Fi do PC), não de preparação, e o assistente diz isso em vez
  de mandar restaurar (o agente já bloqueou a Redefinição de fábrica).
- Ajudas fixas, abertas a partir dos "Deu errado?":
  - **Celular com conta desconhecida:** sem a senha da conta Mi ou Google, o
    celular não pode ser preparado pelo professor; separe-o e procure quem
    tem a senha (ou a assistência Xiaomi com a nota fiscal).
  - **Restaurar sem o PIN:** desligar; segurar Ligar + Volume para cima até
    aparecer o menu; Wipe Data → Wipe All Data → Confirm → Reboot to System.
    Só funciona se as contas já foram tiradas.
- O professor sempre pode **fechar** o assistente; os celulares que já
  entraram continuam na turma, e reabrir começa pelo passo 1.

### 6. "Apagar e preparar de novo"

- No menu do quadradinho de um celular (e no passo 1, quando o número já
  existe), **"Apagar e preparar de novo"**.
- Pede confirmação clara ("O Celular 07 vai ser apagado e voltar de fábrica.
  Os alunos perdem o que estiver nele.") e só funciona com o celular online.
- O comando só é aceito pelo celular vindo de um PC em que ele confia (o mesmo
  canal TLS dos outros comandos). O agente apaga o aparelho pela política do
  Android de dono do aparelho, limpando também a proteção de fábrica, para
  que o celular volte à tela "Bem-vindo" sem pedir conta.
- Em seguida o assistente abre direto no passo 6 com o mesmo número, e o
  celular antigo sai da turma quando o novo entra.
- Celular da escola que **não aparece** no Midview não pode ser apagado pelo
  botão; para ele, a ajuda "Restaurar sem o PIN" (modo de recuperação) continua
  sendo a saída.

## Não-objetivos

- Preparar celulares sem restaurar (o caminho do cabo USB).
- Instalar o agente, ativar administrador ou ligar notificações à mão.
- Zero-touch de revendedor ou qualquer caminho que pule os 6 toques (o Android
  não permite sem revendedor).
- Celulares de outras marcas: o texto e as imagens são da Xiaomi/MIUI. Outra
  marca funciona pelo mesmo QR, mas sem imagens próprias.
- Vários PCs de professor para os mesmos celulares (cada celular confia só no
  PC onde foi preparado; "Permitir novo professor" do agente continua sendo o
  caminho para trocar de PC).
- Preparar vários celulares ao mesmo tempo (forma B, não escolhida).

## Anti-objetivos (seria fracasso mesmo funcionando)

- O professor travar no meio sem saber o que fazer.
- Um celular restaurado com conta dentro e travado por isso, por o assistente
  não ter insistido nos passos 3 e 4.
- Outro aparelho (de aluno, por exemplo) entrar na turma sozinho por ter
  fotografado o QR.
- Qualquer piora para quem já usa o Midview só com computadores, ou para o
  pareamento com código de hoje (que continua valendo para PCs e para
  celulares preparados antes).
- Texto técnico para o professor (Device Owner, APK, provisionamento, adb).

## Restrições

- **Depende do plano 1 do agente Android**, em especial da parte que espera o
  professor (Task 9B): a chave de assinatura, o `SignatureChecksum` real e o
  APK publicado na versão do GitHub. Sem isso o QR não instala nada.
- **O QR carrega o Wi-Fi em texto aberto** (já é assim hoje) e passa a
  carregar o número e uma prova de que veio deste PC; o QR fica na tela do
  professor, que pode estar projetada. A prova precisa valer **uma vez** e
  **por pouco tempo** *(escolha minha: até 1 hora)*, ligada àquele número.
- **Entrar sozinho na turma** precisa manter as garantias do pareamento de
  hoje: o celular só confia no thumbprint deste PC (`TrustedMasters`), e o
  Master só aceita o certificado daquele celular, guardado como num
  pareamento normal (`KnownMachinesFile`). Compatível com o protocolo e com
  Masters antigos (um Master antigo ignora o que não conhece).
- **Android:** o número e a prova chegam ao agente pelo
  `PROVISIONING_ADMIN_EXTRAS_BUNDLE` do QR, que o Android entrega ao
  `ProvisioningModeActivity`/`PolicyComplianceActivity` e ao `AdminReceiver`.
- **Textos** em português, no tom do Midview, sem jargão; nunca citar produto
  concorrente.
- **Testes de interface** pelo harness do Master (`MasterData.Folder`
  temporário, roteiro falso, capturas com `RenderTargetBitmap`); nunca abrir o
  Midview do professor real nem testar contra máquinas do laboratório.
- **Prints:** os do Redmi 10C ficam em
  `.superpowers/brainstorm/2043-1794420649/content/` (fora do git:
  `01-configuracoes.png`, `03-contas.png`, `04-sobre.png`,
  `06-redefinicao.png`). Para refazer com o celular no cabo:
  `am start -a android.settings.SETTINGS`, `-a android.settings.SYNC_SETTINGS`,
  `-a android.settings.DEVICE_INFO_SETTINGS`,
  `-a com.android.settings.action.FACTORY_RESET` e `adb exec-out screencap -p`;
  a MIUI bloqueia `input` (toques e rolagem) pelo `adb`, então só dá para
  capturar o topo de cada tela, e a tela de login da conta Xiaomi sai vazia
  (bloqueia print). Depois de capturar, fechar as telas abertas
  (`am stack remove <id>`), para a tela de apagar não ficar aberta no celular.
  Borrar nomes de rede e contas antes de pôr no programa.

## Escolhas minhas (a revisar pelo professor)

- O assistente **substitui** a janela `PreparePhoneWindow` de hoje (o mesmo
  ponto de entrada passa a abrir o assistente).
- Espera de **15 minutos** antes de "ainda não apareceu"; prova do QR válida
  por **1 hora**.
- O total "de 11" é opcional e perguntado uma vez.
- Preparar de novo um número que já existe troca o celular antigo pelo novo
  quando o novo aparecer.
- Imagens empacotadas no Master como recursos (PNG), com o contorno vermelho
  desenhado pelo programa por coordenadas relativas, para trocar um print
  sem redesenhar o contorno à mão.
- A tela "Pronto. Este é o Celular 07 da escola." no celular é do agente; fica
  aberta até alguém tocar em "OK" ou por alguns minutos.
- Provas de QR guardadas no PC e entrada automática feita pela janela
  principal, com o pareamento por código como reserva (objetivo 4).
- MIUI 12/13 tratado no "Deu errado?" de cada passo, sem perguntar o modelo.
- **Ordem:** este trabalho vem depois do plano 1 do agente (inclusive a Task
  9B) e não corre ao mesmo tempo que outro plano que mexa nos mesmos arquivos
  do agente (`AdminReceiver`, `PairingFlow`, `CommandHandler`, `Lockdown`):
  um de cada vez.

## Deixado para quem constrói

- O formato da prova no QR e como o Master e o celular a conferem (por
  exemplo, um segredo de uso único gerado pelo Master, guardado em memória
  enquanto o assistente espera, e apresentado pelo celular numa mensagem nova
  depois do TLS), desde que respeite as Restrições.
- Como o Master percebe o celular novo (busca periódica enquanto o assistente
  espera é o caminho natural, reaproveitando `Discovery`).
- A organização da janela (uma `MidviewWindow` com páginas, como
  `MachinesWindow`/`MachinesPage`, é o caminho natural).
- Os textos finais de cada passo e das ajudas, a partir dos deste documento.
- O que o agente faz contra a MIUI matando o serviço em segundo plano (início
  automático, economia de bateria, limpar recentes): é do agente, não do
  assistente, mas aparece na primeira preparação de verdade. Ver Pesquisa.

## Pesquisa (2026-11-11, fontes na web)

- **Versões:** os três modelos param no MIUI 14, sem HyperOS. Redmi 10C:
  MIUI 13/Android 11 → MIUI 14.0.9/Android 13 (o da escola está em
  14.0.9.0.TGEMIXM, Android 13, confirmado no aparelho). Redmi Note 10S: MIUI
  12.5/Android 11 → MIUI 14.0.11/Android 13. Redmi Note 9 Pro: MIUI 11/Android
  10 → MIUI 14.0.3/Android 12. Todos atendem ao mínimo Android 10 do agente.
- **QR:** em Android 9 ou mais novo o leitor dos 6 toques é embutido e não
  precisa de internet antes; o Wi-Fi do QR basta (rede WPA2 com senha comum).
  Não foram achados relatos de falha do QR nesses três modelos. O Redmi Note 9
  Pro foi certificado Android Enterprise Recommended (2020); os outros dois,
  incerto. Falhas relatadas em Xiaomi são de modelos mais novos com HyperOS.
- **Contas antes de restaurar:** com "Encontrar dispositivo" ligado, o celular
  restaurado fica travado pela conta Mi até digitar a senha dela; com conta
  Google, a proteção de fábrica (FRP) pede a conta e impede o QR. Por isso os
  passos 3 e 4.
- **Modo de recuperação:** Ligar + Volume para cima com o celular desligado;
  Wipe Data → Wipe All Data → Confirm; primeiro boot de 3 a 7 minutos.
- **Menus (MIUI 14, Redmi 10C, em português, conferidos nos prints):**
  "Configurações", "Sobre o telefone" (primeiro item), "Contas e
  sincronização" com a seção "GOOGLE", e a tela "Limpar todos os dados
  (redefinir para a configuração original)" com o botão "Limpar todos os
  dados". O caminho da conta Mi e o item "Redefinição de fábrica" no fim de
  "Sobre o telefone" vêm da pesquisa, não de print.
- **MIUI e o agente:** a MIUI tem limites próprios de segundo plano (início
  automático, economia de bateria, limpar recentes mata o processo), sem API
  pública para desligar; não verificado se ser Device Owner já protege o
  agente.

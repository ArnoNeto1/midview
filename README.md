<p align="center">
  <img src="assets/midview-logo.jpg" alt="Midview" width="140">
</p>

# Midview

**Português** · [English](README.en.md)

[![Versão mais recente](https://img.shields.io/github/v/release/ArnoNeto1/midview?label=vers%C3%A3o&color=0e7490)](https://github.com/ArnoNeto1/midview/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/ArnoNeto1/midview/total?label=downloads&color=6b7684)](https://github.com/ArnoNeto1/midview/releases)
[![Baixar o instalador](https://img.shields.io/badge/⬇%20Baixar-Instalador%20(.exe)-0e7490?style=for-the-badge)](https://github.com/ArnoNeto1/midview/releases/latest/download/MidviewSetup.exe)

> O botão acima baixa sempre a versão mais nova. O mesmo instalador serve
> para o computador do professor e para as máquinas dos alunos.
> Página para os professores: https://arnoneto1.github.io/midview/

O Midview gerencia o laboratório de informática da escola. Do próprio computador, o professor vê e controla as máquinas dos alunos, fala com a turma, envia e recolhe arquivos, bloqueia programas e sites e liga ou desliga o laboratório. Tudo passa pela rede local da escola, sem servidor externo. A internet só é usada pelo computador do professor, para buscar as atualizações do Midview no GitHub, e pelos celulares otimizados, que usam um DNS público sem anúncios (veja Privacidade).

## Veja funcionando

[![Todas as funções do Midview em menos de 2 minutos, com narração: a sala de aula com todas as telas ao vivo, no visual escuro](docs/resumo-capa.jpg)](https://arnoneto1.github.io/midview/midview.mp4)

[Assista ao vídeo com narração](https://arnoneto1.github.io/midview/midview.mp4) (MP4, 1min36): todas as funções da barra, no visual escuro da versão 0.0.41. Energia (desligar, reiniciar, ligar e agendar), Tela (ver, controlar e bloquear telas), Turma (mensagem, bate-papo, enviar e recolher arquivos, pasta da turma), Programas e sites (abrir site e programa, instalar, desinstalar, fechar e bloquear programas, bloquear sites) e Configurar (painel do laboratório e máquinas da sala). As máquinas e telas que aparecem nele são fictícias.

### Tutorial completo

Passo a passo, com narração, de todas as funções, no visual escuro da versão 0.0.41: instalação, pareamento, a sala de aula, energia, tela, turma, programas e sites, configurações, o botão direito e a sala com dois professores.

[![Tutorial do Midview: a janela "Midview - Sala de aula" no visual escuro, com todas as telas ao vivo](docs/tutorial-capa.jpg)](https://arnoneto1.github.io/midview/tutorial.mp4)

[Assistir ao tutorial](https://arnoneto1.github.io/midview/tutorial.mp4) (MP4, 8 min).

## O que o professor pode fazer

A janela "Midview - Sala de aula" mostra a tela de cada máquina numa grade. Os botões da barra de ferramentas agem nas máquinas selecionadas, se houver alguma (a barra de baixo avisa, por exemplo "Só na seleção (2)."); sem seleção, agem em todas as que a grade mostra (com o botão **Telas** em **Todos**, a turma toda). O botão direito sobre uma máquina age nela, ou em todas as selecionadas quando ela faz parte da seleção; fechado o menu, ou aberta a tela com dois cliques, a seleção volta a ser a de antes.

| Grupo | Funções |
| --- | --- |
| **Energia** | **Desligar** e **Reiniciar**, com aviso de 1 ou 5 minutos e, se quiser, instalando as atualizações do Windows que estiverem esperando; **Ligar** pela rede (Wake-on-LAN); **Agendar** os dias e horários para ligar e desligar. |
| **Tela** | **Ver a tela** de um aluno (ou dois cliques no quadrado dele), numa janela no formato da tela dele, sem faixas pretas: a barra de botões fica por cima da imagem e some sozinha (volta quando o mouse encosta na borda de cima), e **Tela Cheia** ocupa o monitor inteiro; **Controlar** a máquina (o teclado e o mouse do aluno ficam travados e ele vê o aviso "Este computador está sendo controlado pelo professor", que quase some quando o mouse chega perto, para não tapar o que está embaixo). O controle continua mesmo quando aparece a tela do UAC (o pedido de senha de administrador): com a senha do laboratório guardada, **Preencher a senha de administrador** digita no UAC com um clique. **Bloquear telas** com um cadeado; enquanto as telas estão bloqueadas, se o aluno apertar Ctrl+Alt+Del, a tela azul do Windows fecha sozinha e, enquanto aparece, fica sem o Gerenciador de Tarefas, o Sair, o Trocar usuário, o Bloquear, o Alterar senha e o botão de desligar, reiniciar ou suspender. |
| **Turma** | **Mensagem** na tela de todos; **Bate-papo** com a turma (as respostas chegam só ao professor); **Enviar arquivo** para a pasta "Arquivos do professor"; **Recolher arquivos** da pasta "Entregar ao professor"; **Pasta da turma**, uma pasta na Área de Trabalho dos alunos em que o que cada um baixa ou salva aparece em todos os computadores (com turmas, aulas e até dois PCs de professor). Durante a aula, os downloads de qualquer programa vão para a pasta da turma, e o Word, o Excel, o PowerPoint (2016 ou mais novo), o Bloco de Notas e o Paint abrem o "Salvar como" nela. |
| **Programas e sites** | **Abrir site** (um ou até 10 de uma vez, separados por vírgula) e **Abrir programa** nas máquinas; **Instalar programa** na turma toda, como administrador, sem a tela do UAC e sem digitar senha; **Desinstalar programa**; **Fechar programas**, com aviso e prazo para o aluno salvar; **Bloquear programas** (lista proibida ou só os permitidos); **Bloquear sites** (a internet inteira ou só alguns sites, valendo mesmo depois de reiniciar). |
| **Configurar** | **Painel do laboratório** (políticas do Chrome e do Windows, como bloqueador de anúncios, modo visitante e travar a Área de Trabalho e os downloads do aluno); **Máquinas da sala**, com as páginas Procurar e parear (procurar na rede e parear conferindo o código em até 5 minutos), Atualizações (atualizar este computador e as máquinas), Outro professor (dar acesso a um colega ou entrar na sala dele), Senha de administrador (guardar a senha do laboratório) e Avançado. |

No botão direito há também **Conversar** com um aluno só, **Desinstalar programa**, **Editar a máquina** e **Remover da lista da sala**. Na barra de baixo, **Visualizar todos** encaixa todas as telas, **Só online** esconde as máquinas desligadas e o controle deslizante muda o tamanho das telas (também com Ctrl + roda do mouse). **Organizar sala** deixa arrastar as telas até ficarem como as máquinas estão na sala, e **Alinhar** acerta a grade.

## Como funciona

O mesmo instalador tem dois tipos:

- **Professor**: instala o programa "Midview - Sala de aula", que roda sem direitos de administrador.
- **Aluno**: instala o serviço **Midview Agent**, que roda como sistema em cada máquina do laboratório, e o programa que mostra as mensagens, o bloqueio de tela e os avisos para o aluno.

O professor acha as máquinas pela rede e pareia cada uma conferindo, em até 5 minutos, um código de 6 dígitos que aparece na tela do aluno. Depois disso, a máquina só aceita os professores pareados. Os comandos, as telas e os arquivos passam criptografados com TLS e certificados próprios; só a busca de máquinas e o Wake-on-LAN usam pacotes UDP sem criptografia. O Midview não tem telemetria. A única conexão do computador do professor com a internet é com o GitHub, para ver se saiu uma versão nova e baixá-la; as máquinas dos alunos recebem as atualizações pela rede local, e os celulares otimizados usam um DNS público sem anúncios (veja Privacidade).

Portas usadas na rede local:

| Porta | Uso |
| --- | --- |
| TCP 28900 | Comandos para o serviço do aluno |
| TCP 5901 | Tela e controle remoto |
| TCP 5902 | Tela e controle remoto na tela segura (UAC) |
| UDP 28901 | Busca de máquinas na rede |
| TCP e UDP 28902 | Acesso de outro professor (convite) |
| TCP 28903 | Pasta da turma entre dois PCs de professor |
| UDP 9 | Ligar as máquinas pela rede |

## Requisitos

- Windows 10 ou 11 de 64 bits, no computador do professor e nas máquinas.
- Direitos de administrador para instalar.
- Professor e máquinas na mesma rede local. Para **Ligar**, o Wake-on-LAN precisa estar ativo na BIOS e na placa de rede das máquinas.
- Internet só no computador do professor, e só para receber as atualizações do Midview.
- Não é preciso instalar o .NET: ele vem dentro do Midview.

## Instalação

1. Clique em **⬇ Baixar Instalador (.exe)**, lá em cima. Não precisa instalar o .NET nem mais nada.
2. No computador do professor, abra o instalador e escolha **Professor** (se aparecer a tela "O Windows protegeu o computador", clique em **Mais informações** e depois em **Executar assim mesmo**). Ele cria o atalho "Midview - Sala de aula" na Área de Trabalho.
3. Em cada máquina do laboratório, abra o instalador e escolha **Aluno**.
4. No Midview, clique em **Máquinas da sala** > **Procurar e parear** > **Procurar na rede**, selecione as máquinas e clique em **Parear as selecionadas**. Confira o código de 6 dígitos que aparece na tela de cada aluno (você tem 5 minutos para isso).

Pareie as máquinas logo depois de instalar: até o primeiro pareamento, qualquer professor da rede pode pareá-las.

O instalador mostra a licença de uso e pede que ela seja aceita. Para instalar muitas máquinas de uma vez, use a instalação silenciosa, que dispensa as telas do instalador:

```powershell
MidviewSetup.exe /VERYSILENT /SUPPRESSMSGBOXES /TIPO=aluno
```

Use `/TIPO=professor` no computador do professor. Sem `/TIPO`, a instalação silenciosa não instala nada. O código de saída 10 indica que os arquivos foram copiados, mas o serviço não pôde ser configurado.

## Atualização

- **Computador do professor**: quando sai uma versão nova no GitHub, o Midview avisa ao abrir. **Atualizar agora** baixa o instalador, confere se ele veio inteiro, fecha o Midview, instala e abre de novo sozinho; **Agora não** deixa para a próxima vez que o Midview abrir. O mesmo pode ser feito a qualquer hora em **Máquinas da sala** > **Atualizações** > **Atualizar professor**. O Windows pede a permissão de administrador uma vez, e enquanto o Midview fecha para instalar (1 ou 2 minutos), as telas bloqueadas dos alunos são liberadas. Também dá para rodar o instalador da versão nova por cima da anterior.
- **Máquinas do laboratório**: depois que o professor é atualizado, o Midview pergunta se quer atualizar os alunos. Ou use **Máquinas da sala** > **Atualizações** > **Atualizar as máquinas pela rede...**. Uma máquina é atualizada primeiro e as outras só depois que ela confirma; se algo falhar, a máquina volta sozinha para a versão anterior. Cada máquina fica fora da grade por 1 a 3 minutos.

## Mais de um professor

Em **Máquinas da sala** > **Outro professor**:

- **Dar acesso a um colega...** mostra um código de 9 caracteres, válido por 10 minutos. O colega digita o código em **Entrar na sala de um colega...** e passa a ver as mesmas máquinas.
- **Permitir novo professor (10 minutos)** mostra um código de 8 caracteres. Por 10 minutos, as máquinas aceitam o pareamento do professor que digitar esse código em **Procurar e parear**, uma vez em cada máquina.
- **Esquecer os outros professores...** faz as máquinas da sala aceitarem só você.

Limites:

- Uma máquina da sala que esteja comprometida pode revelar o código de **Permitir novo professor** enquanto ele vale (10 minutos).
- Qualquer professor confiável pode esquecer todos os outros, inclusive quem montou a sala.

## Celulares

Em breve: o aplicativo do celular ainda não foi publicado.

O celular Android (Android 10 ou mais novo) aparece na grade e é pareado com o código de 6 dígitos, como as máquinas. O botão **Telas** escolhe o que a grade mostra: **Todos**, **Computadores** ou **Celulares** (**Tablets**: em breve). O professor vê a tela do celular e a controla (**Controlar**: o clique é um toque, arrastar desliza, a roda do mouse rola, o teclado digita no campo de texto, e a janela ganha os botões **Voltar**, **Início** e **Recentes**; o aluno vê o aviso "Este celular está sendo controlado pelo professor", que quase some quando o mouse chega perto, e o toque dele continua valendo), bloqueia a tela com o cadeado e bloqueia apps e sites:

- **Bloquear programas**, com só celulares selecionados (ou com **Telas** em **Celulares**), mostra os apps do celular com os ícones. **Bloquear só estes** deixa os apps marcados cinza, sem abrir; **Liberar só estes** deixa abrir só os marcados (o Midview, a tela inicial, o telefone e as Configurações continuam funcionando).
- **Bloquear sites** é a mesma janela dos PCs. No celular, os sites bloqueados não abrem no Chrome, e os outros navegadores ficam escondidos. **Bloquear a internet** corta a internet de todos os apps, e o Midview continua vendo o celular.

Os bloqueios de apps e de sites continuam valendo se o celular reiniciar.

O Midview também trava, sempre, duas coisas no celular: o aluno não muda a data, a hora nem o fuso horário (o celular continua acertando tudo sozinho pela rede), e os outros apps não abrem janelas por cima da tela (as janelas flutuantes da MIUI, os balões de conversa), nem por cima do cadeado do professor. Os avisos do próprio Midview continuam aparecendo.

A **Pasta da turma** vale para a sala toda, celulares também. No celular ela fica em Download/Midview/Pasta da turma – <turma>, no app Arquivos, e o que o aluno salva ou move para ela aparece em todos os computadores e celulares, como nos PCs. O Chrome do celular continua baixando em Download (no Android não há como mandar os downloads para outra pasta): para dividir um download com a turma, o aluno o move para essa pasta no app Arquivos. Ela precisa do acesso a todos os arquivos, que a preparação pelo cabo liga. **Recolher arquivos** e o seu **Apagar as pastas dos alunos** não mexem nela. Quando a aula acaba, os arquivos saem da pasta e ficam guardados numa área escondida, Download/Midview/.midview-turmas, até o Midview do professor buscá-los na aula seguinte (o que ele já tem, ou o que foi apagado com **Apagar tudo**, só é apagado do celular). O que ninguém buscar sai sozinho depois de 30 dias, ou antes, se a área passar de 2 GB.

Para receber arquivos (**Enviar arquivo**, **Pasta da turma**, **Instalar programa** e a atualização do aplicativo), o celular precisa continuar com pelo menos 100 MB livres depois de receber; um app ou uma atualização precisa de duas vezes o seu tamanho, mais esses 100 MB. Sem esse espaço, o professor lê "sem espaço no celular".

Num celular com Android 14 ou mais novo, com a tela apagada, o Midview pode demorar um pouco mais para responder ao professor: a partir do Android 14, o Wi-Fi entra em economia de energia com a tela apagada, e o Midview não consegue impedir.

Valem para os celulares selecionados ou, sem seleção, para todos os celulares da sala:

- **Telas** > **Otimizar celulares e tablets...** desinstala os apps que a escola não usa e que vêm no celular (jogos, redes sociais e vídeos curtos, compras e streaming, extras da Xiaomi e do Google; os que o Android não deixa desinstalar, por serem do sistema, ficam escondidos), esconde os apps de propaganda que vêm de fábrica e liga um DNS sem anúncios, que tira a maior parte dos anúncios dos sites e dos apps, quando a rede deixa. Se o DNS sem anúncios não funcionar na rede, o celular volta sozinho ao DNS de sempre e continua com internet. Com a internet bloqueada, ele liga sozinho ao desbloquear.
- **Painel do laboratório** vale também nos celulares, no que faz sentido neles: **Bloqueador de anúncios** liga ou desliga o DNS sem anúncios do **Otimizar** (o painel é quem decide; o **Otimizar** respeita), **Travar downloads** faz o Chrome não baixar nada (precisa do Chrome 131 ou mais novo; o que o professor manda com **Enviar arquivo** continua chegando), **Bloquear sites de distração** vale pelo Chrome, e **RESET geral** libera os downloads e liga de novo o DNS sem anúncios. As outras funções são só dos computadores, e a janela diz isso embaixo de cada uma quando há celulares na lista.
- **Telas** > **Senha dos celulares e tablets...** põe na tela de bloqueio um PIN de 4 números que o aluno não consegue trocar nem tirar. O PIN não fica guardado no computador.
- **Máquinas da sala** > **Atualizações** > **Atualizar os celulares pela rede...** manda a versão nova do aplicativo do celular, a mesma versão do Midview do professor, baixada do GitHub. Um celular é atualizado primeiro, e os outros só depois que ele confirmar a versão nova. O aluno não precisa tocar em nada, e os bloqueios continuam valendo; o Android mostra ao aluno o aviso "Atualizado pelo seu administrador".

### Para preparar um celular

O celular é preparado uma vez, pelo cabo USB ligado ao computador do professor. O QR da primeira tela (6 toques) não serve: o Google só deixa entrar por ele os aplicativos de gerenciamento da lista dele, e o Midview não está nela.

1. Restaure o celular para o padrão de fábrica e, na configuração inicial, pule todas as contas.
2. Ligue as **Opções do desenvolvedor** (toque 7 vezes em **Versão do MIUI**, na Xiaomi) e, dentro delas, a **Depuração USB**. Na Xiaomi, ligue também a **Depuração USB (configurações de segurança)**: ela só liga com uma conta Mi conectada, e às vezes com um chip no celular.
3. Saia de todas as contas do celular (a conta Mi também). Sem nenhuma conta, o celular aceita o Midview como dono.
4. Ligue o cabo e toque em **Permitir** no aviso de depuração. No computador, com o `adb` (Android SDK Platform-Tools, baixado do [site do Google](https://developer.android.com/tools/releases/platform-tools)) e o `MidviewAgent.apk` da versão do Midview do professor (na página da versão no GitHub), rode nesta ordem:

   ```
   adb install MidviewAgent.apk
   adb shell cmd deviceidle whitelist +io.github.arnoneto1.midview.agent
   adb shell cmd appops set io.github.arnoneto1.midview.agent 10008 allow
   adb shell settings put secure enabled_accessibility_services io.github.arnoneto1.midview.agent/io.github.arnoneto1.midview.agent.screen.AccessibilityCapture
   adb shell settings put secure accessibility_enabled 1
   adb shell dpm set-device-owner io.github.arnoneto1.midview.agent/.AdminReceiver
   ```

   O segundo tira o Midview da economia de bateria; o terceiro, só na Xiaomi (MIUI), deixa o Midview iniciar sozinho; o quarto e o quinto deixam o professor ver a tela sem o aluno tocar em "Iniciar agora" (Android 11 ou mais novo) e controlar o celular. O `dpm set-device-owner` vem **por último**: logo depois, as travas do celular desligam a depuração USB.
5. Em **Máquinas da sala** > **Procurar e parear** > **Procurar na rede**, selecione o celular e confira o código de 6 dígitos que aparece nele.

Se o aluno fechar todos os apps recentes ("Limpar tudo" na Xiaomi), o Midview do celular volta sozinho em poucos segundos (mais devagar com o celular parado).

## Desinstalação

Em **Configurações** > **Aplicativos**, desinstale "Midview Professor" ou "Midview Aluno". Na máquina do aluno, o desinstalador libera o que estava bloqueado, destrava a Área de Trabalho, devolve as pastas de downloads e de salvar se uma aula estiver em andamento e pergunta se deve apagar também os professores pareados.

As demais políticas do **Painel do laboratório** continuam aplicadas depois da desinstalação. Para tirá-las, use **RESET geral** no painel antes de desinstalar.

## Privacidade

O Midview não envia nada ao desenvolvedor. Entre o professor e as máquinas passam as imagens de tela, o texto copiado durante o controle, as mensagens, o bate-papo, os arquivos enviados e recolhidos, os nomes e os endereços MAC das máquinas e os nomes dos programas abertos ou instalados.

Para as atualizações, o computador do professor consulta o GitHub (`api.github.com`), informando a versão do Midview instalada, e baixa de lá o instalador e o aplicativo dos celulares (`MidviewAgent.apk`). Nenhum dado da sala vai junto. Os computadores dos alunos não acessam a internet pelo Midview.

Nos celulares otimizados, o Android pergunta os endereços dos sites e apps a um serviço público de DNS sem anúncios, por conexão criptografada, em vez do DNS da rede da escola. Esse serviço fica sabendo quais endereços o celular procura, mas não o conteúdo das páginas. Para ligar e conferir esse DNS, o aplicativo do celular testa a internet pelo endereço de teste do Android e pelo próprio serviço. Sem otimizar, ou se a rede bloquear esse DNS, o celular usa o DNS de sempre.

A tela do aluno pode ser vista sem aviso na máquina dele; só o controle remoto mostra um aviso. Por isso, a escola deve informar alunos e responsáveis sobre o uso do Midview, como pede a cláusula 6 da [licença](LICENSE).

A senha de administrador do laboratório, se guardada em **Máquinas da sala**, fica só no computador do professor, protegida pelo Windows (DPAPI do usuário), e nunca é enviada às máquinas dos alunos. O **Preencher a senha de administrador** a digita no pedido de UAC da máquina controlada apenas quando o professor clica.

Algumas opções do **Painel do laboratório** configuram o Google Chrome, que pode então acessar serviços do Google por conta própria, por exemplo para baixar o bloqueador de anúncios.

## Para desenvolvedores

| Pasta | Conteúdo |
| --- | --- |
| `src/Midview.Master` | Programa do professor (WPF) |
| `src/Midview.Agent` | Serviço do Windows nas máquinas dos alunos |
| `src/Midview.Session` | Programa que roda na sessão do aluno: tela, controle, mensagens e bloqueio |
| `src/Midview.Desk` | Auxiliar que mostra e controla a tela segura (UAC), preenche a senha de administrador e fecha a tela do Ctrl+Alt+Del enquanto as telas estão bloqueadas |
| `src/Midview.Protocol` | Biblioteca comum: mensagens, TLS, pareamento, busca e atualização |
| `tests/` | Testes com xUnit e o teste de ponta a ponta (`Midview.E2E`) |
| `installer/` | Instalador (Inno Setup), scripts das máquinas e seus testes |
| `docs/superpowers/` | Especificações e planos de cada função |
| `site/` | Página para os professores, em português e em inglês (`site/en/`), publicada no GitHub Pages pelo fluxo **Site** |
| `promo/` | Vídeos de divulgação e o tutorial, feitos com Remotion (o GIF e os MP4 de `docs/` saem dele) |

Requisitos: Windows, SDK do .NET 8 e, para o instalador, Inno Setup 6.

```powershell
dotnet build Midview.sln --configuration Release
dotnet test Midview.sln --configuration Release --no-build
```

Alguns testes do serviço precisam de administrador e são pulados sem ele.

Para gerar o instalador em `dist\MidviewSetup.exe` e conferi-lo:

```powershell
powershell -ExecutionPolicy Bypass -File installer\Gerar-Instalador.ps1
powershell -ExecutionPolicy Bypass -File installer\tests\Testar-Scripts.ps1 -Pacote dist
```

A versão fica em `Directory.Build.props`. No GitHub, quatro fluxos conferem cada mudança: **Testes (Windows)**, **Instalador**, **Atualização pela rede (Windows de verdade)** e **Bloqueio de programas (Windows de verdade)**. Uma tag `vX.Y.Z` faz o fluxo **Instalador** publicar o `MidviewSetup.exe` na versão do GitHub, que é de onde o Midview do professor se atualiza.

## Licença

Copyright © 2026 MidView. Todos os direitos reservados.

O Midview é software proprietário, de uso gratuito para escolas, nos termos da [Licença de Uso](LICENSE). Os componentes de terceiros incluídos e suas licenças estão em [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). O `adb` do Google, que o Midview Professor usa para preparar os celulares pelo cabo USB, não vem no instalador: na primeira vez que o professor prepara um celular, e só quando ele pede, o Midview o baixa do próprio Google, confere o arquivo e o guarda neste computador; ele segue os termos do Google.

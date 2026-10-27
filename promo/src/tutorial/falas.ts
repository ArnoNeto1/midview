/**
 * Roteiro falado do tutorial completo: uma fala por trecho do vídeo, na ordem.
 * A voz é a "Amanda" do ElevenLabs v4, gerada pelo Higgsfield (preset
 * 22aeaea5-3677-5fd7-b932-34f362087a9b), e cada áudio fica em
 * public/narracao-tutorial/<id>.mp3, preparado por scripts/narracao.mjs.
 * Cada trecho dura o tempo da sua fala mais uma pausa (src/tutorial/tempo.ts).
 */
export type ChapterId =
  | "abertura"
  | "instalacao"
  | "pareamento"
  | "sala"
  | "energia"
  | "tela"
  | "turma"
  | "programas"
  | "configurar"
  | "botaoDireito"
  | "doisProfessores"
  | "encerramento";

/** Nome de cada capítulo, como aparece acima da legenda. */
export const CHAPTERS: Record<ChapterId, string> = {
  abertura: "Midview",
  instalacao: "Instalação",
  pareamento: "Pareamento",
  sala: "Sala de aula",
  energia: "Energia",
  tela: "Tela",
  turma: "Turma",
  programas: "Programas e sites",
  configurar: "Configurar",
  botaoDireito: "Uma máquina só",
  doisProfessores: "Dois professores",
  encerramento: "Midview",
};

/** `beat` reaproveita a cena de outra fala (o resumo abre com a cena de t01). */
export type Fala = { id: string; chapter: ChapterId; say: string; caption?: string; beat?: string };

export const FALAS: Fala[] = [
  { id: "t01", chapter: "abertura", say: "Este é o Midview, o programa que coloca o laboratório de informática inteiro na tela do professor. Vamos ver, passo a passo, tudo o que ele faz." },

  { id: "t02", chapter: "instalacao", caption: "Um instalador para professor e alunos", say: "Tudo começa com um único instalador, o mesmo para o computador do professor e para as máquinas dos alunos." },
  { id: "t03", chapter: "instalacao", caption: "Professor ou Aluno", say: "Depois de aceitar a licença de uso, escolha o tipo: Professor, no seu computador, ou Aluno, em cada máquina do laboratório." },
  { id: "t04", chapter: "instalacao", caption: "Pronto para abrir", say: "No computador do professor, o atalho Midview, Sala de aula, aparece na Área de Trabalho." },
  { id: "t05", chapter: "instalacao", caption: "Muitas máquinas de uma vez", say: "Para instalar muitas máquinas de uma vez, a instalação silenciosa faz tudo sem nenhuma tela." },

  { id: "t06", chapter: "pareamento", caption: "A sala começa vazia", say: "Com o programa aberto, a sala ainda está vazia. Clique em Procurar máquinas na rede." },
  { id: "t07", chapter: "pareamento", caption: "Procurar na rede", say: "O Midview encontra as máquinas com o Midview Aluno nesta rede. Marque as da sua sala e clique em Parear as selecionadas." },
  { id: "t08", chapter: "pareamento", caption: "Confira o código", say: "Cada máquina mostra um código de seis dígitos na tela do aluno. Confira se é o mesmo que aparece para você e clique em Confere." },
  { id: "t09", chapter: "pareamento", caption: "Máquina pareada", say: "Pronto: a máquina está pareada e, a partir de agora, só aceita os professores pareados com ela." },

  { id: "t10", chapter: "sala", caption: "Todas as telas ao vivo", say: "Agora a janela principal mostra, ao vivo, a tela de cada aluno numa grade." },
  { id: "t11", chapter: "sala", caption: "O estado de cada máquina", say: "Cada tela mostra o nome da máquina, e as que estão desligadas aparecem como Tela indisponível." },
  { id: "t12", chapter: "sala", caption: "Selecione uma ou várias", say: "Para escolher máquinas, clique numa tela, use o Ctrl para somar outras ou arraste um retângulo em volta delas." },
  { id: "t13", chapter: "sala", caption: "Do tamanho que você quiser", say: "Na barra de baixo, mude o tamanho das telas, encaixe todas com Visualizar todos e esconda as desligadas com Só online." },
  { id: "t14", chapter: "sala", caption: "Do jeito da sua sala", say: "Com Organizar sala, arraste as telas até ficarem como as máquinas estão na sala. Alinhar deixa tudo no lugar." },

  { id: "t15", chapter: "energia", caption: "Ligue pela rede", say: "O primeiro grupo da barra é Energia. Ligar acorda pela rede as máquinas desligadas, e elas aparecem na grade em um ou dois minutos." },
  { id: "t16", chapter: "energia", caption: "Desligue com aviso", say: "Desligar e Reiniciar avisam o aluno antes: escolha esperar um ou cinco minutos, ou agir na hora." },
  { id: "t17", chapter: "energia", caption: "O aluno é avisado", say: "O aluno vê a contagem no alto da tela, para salvar o trabalho. Se você mudar de ideia, Cancelar desligamento interrompe tudo." },
  { id: "t18", chapter: "energia", caption: "Agende a semana", say: "Em Agendar, escolha os dias e os horários para o laboratório ligar e desligar sozinho." },

  { id: "t19", chapter: "tela", caption: "Veja a tela de um aluno", say: "No grupo Tela, selecione uma máquina e clique em Ver a tela para acompanhar o aluno em tamanho grande." },
  { id: "t20", chapter: "tela", caption: "Só vendo", say: "Só vendo, o aluno continua usando o computador normalmente. Tela Cheia ocupa o seu monitor inteiro." },
  { id: "t21", chapter: "tela", caption: "Assuma o controle", say: "Controlar trava o teclado e o mouse do aluno e passa o controle para você. No alto da tela dele, um aviso mostra que o professor está no controle." },
  { id: "t22", chapter: "tela", caption: "Atenção da turma em um clique", say: "Bloquear telas cobre a tela de todos com um cadeado e trava o teclado e o mouse." },
  { id: "t23", chapter: "tela", caption: "Devolva quando quiser", say: "Na grade, cada máquina mostra Tela bloqueada. Desbloquear telas devolve tudo aos alunos." },

  { id: "t24", chapter: "turma", caption: "Mande um recado", say: "No grupo Turma, Mensagem mostra um recado na tela de todos os alunos." },
  { id: "t25", chapter: "turma", caption: "O aluno lê na hora", say: "O aluno lê o recado numa janela em destaque e fecha em OK, entendi." },
  { id: "t26", chapter: "turma", caption: "Converse com a turma", say: "Bate-papo abre uma conversa com a turma toda, e a resposta de cada aluno chega só para você." },
  { id: "t27", chapter: "turma", caption: "Ou com um aluno só", say: "Para falar com um aluno só, use Conversar, no botão direito sobre a máquina dele." },
  { id: "t28", chapter: "turma", caption: "Envie arquivos", say: "Enviar arquivo salva o arquivo na pasta Arquivos do professor, na Área de Trabalho de cada aluno. Se quiser, o Midview já abre a pasta ou o arquivo para ele." },
  { id: "t29", chapter: "turma", caption: "Recolha os trabalhos", say: "Recolher arquivos traz para o seu computador tudo o que os alunos puseram na pasta Entregar ao professor." },
  { id: "t30", chapter: "turma", caption: "Comece a próxima aula limpa", say: "No fim da aula, esvazie as pastas dos alunos com um clique. Os arquivos vão para a Lixeira." },
  { id: "t31", chapter: "turma", caption: "Pasta da turma", say: "A Pasta da turma é para a turma inteira trabalhar junta. Crie a turma, por exemplo, Terceiro A, e clique em Começar a aula." },
  { id: "t32", chapter: "turma", caption: "Uma pasta em todos os PCs", say: "A Pasta da turma aparece na Área de Trabalho de cada aluno. O que ele baixar ou salvar nela aparece em todos os computadores." },
  { id: "t33", chapter: "turma", caption: "Tudo em dia", say: "Durante a aula, você vê cada computador em dia, copiando ou desligado. Os desligados recebem a pasta quando ligam." },
  { id: "t34", chapter: "turma", caption: "Encerre do seu jeito", say: "Ao encerrar a aula, escolha manter os arquivos para a próxima aula, apagar só os de hoje ou apagar tudo da turma." },
  { id: "t35", chapter: "turma", caption: "Abra um site em todas", say: "Abrir site abre o mesmo endereço no navegador de todos. Marque Salvar no menu para deixar o site a um clique." },
  { id: "t36", chapter: "turma", caption: "A um clique", say: "Os sites salvos aparecem no menu do botão. Abrir programa funciona do mesmo jeito, com o comando do programa." },

  { id: "t37", chapter: "programas", caption: "Feche os programas", say: "Fechar programas fecha o que os alunos abriram. Peça para fechar, como clicar no X, ou force o fechamento." },
  { id: "t38", chapter: "programas", caption: "Com tempo para salvar", say: "O aluno vê a contagem e o aviso para salvar o trabalho. A área de trabalho e o Midview nunca são fechados." },
  { id: "t39", chapter: "programas", caption: "Bloqueie programas", say: "Bloquear programas proíbe os programas que você marcar, ou deixa abrir só os permitidos." },
  { id: "t40", chapter: "programas", caption: "Bloqueio na hora", say: "Se o aluno abrir um programa bloqueado, ele fecha na hora, com um aviso. Na grade, cada máquina mostra quantos estão bloqueados." },
  { id: "t41", chapter: "programas", caption: "Bloqueie sites", say: "Bloquear sites bloqueia a internet inteira ou só os sites que você escolher, e o bloqueio continua mesmo se a máquina reiniciar." },

  { id: "t42", chapter: "configurar", caption: "Painel do laboratório", say: "Em Configurar, o Painel do laboratório prepara o navegador e o Windows de todas as máquinas." },
  { id: "t43", chapter: "configurar", caption: "Cinco passos para começar", say: "Siga os cinco passos do início da aula: bloqueador de anúncios, limpeza ao fechar, blindagem total, bloquear o Microsoft Edge e bloquear downloads." },
  { id: "t44", chapter: "configurar", caption: "Extras e manutenção", say: "Há também bloqueios extras, como o modo visitante, e a manutenção, com o RESET geral, que devolve as máquinas ao padrão." },
  { id: "t45", chapter: "configurar", caption: "Atualize pela rede", say: "Em Máquinas da sala, Atualizar as máquinas pela rede leva a versão nova do Midview a todas, sem pendrive." },
  { id: "t46", chapter: "configurar", caption: "Com segurança", say: "Uma máquina é atualizada primeiro, e as outras só depois que ela confirma. Se algo falhar, a máquina volta sozinha à versão anterior." },

  { id: "t47", chapter: "botaoDireito", caption: "Botão direito", say: "Tudo isso também vale para uma máquina só: clique com o botão direito sobre a tela dela." },
  { id: "t48", chapter: "botaoDireito", caption: "Ou nas selecionadas", say: "Com várias máquinas selecionadas, o menu age em todas de uma vez." },
  { id: "t49", chapter: "botaoDireito", caption: "Desinstale programas", say: "Desinstalar programa lista os programas daquela máquina e remove os que você marcar, se quiser, na turma toda." },
  { id: "t50", chapter: "botaoDireito", caption: "Organize a lista", say: "E, se uma máquina saiu da sala, Remover da lista da sala tira a tela dela da grade." },

  { id: "t51", chapter: "doisProfessores", caption: "Divida a sala", say: "Outro professor também pode usar a sala. Em Máquinas da sala, clique em Dar acesso a um colega." },
  { id: "t52", chapter: "doisProfessores", caption: "Um código para o colega", say: "Aparece um código de nove caracteres, que vale por dez minutos. Nada aparece na tela dos alunos." },
  { id: "t53", chapter: "doisProfessores", caption: "O colega entra", say: "O colega clica em Entrar na sala de um colega, digita o código e passa a ver as mesmas máquinas." },
  { id: "t54", chapter: "doisProfessores", caption: "Ou libere por 10 minutos", say: "Outra opção é Permitir novo professor: por dez minutos, as máquinas aceitam o pareamento de outro professor." },
  { id: "t55", chapter: "doisProfessores", caption: "Pasta da turma em dois PCs", say: "Com dois computadores de professor, as pastas das turmas ficam num deles, e o outro trabalha nelas pela rede. Guardar as pastas das turmas neste computador traz tudo para o seu." },

  { id: "t56", chapter: "encerramento", say: "Midview: o laboratório inteiro na sua tela, gratuito para escolas. Transforme sua sala de aula." },
];

/**
 * O resumo de até 3 minutos: as funções mais usadas, com as mesmas cenas e falas
 * do tutorial completo e uma abertura própria (r01).
 */
const RESUMO_IDS = ["t07", "t08", "t10", "t16", "t17", "t21", "t22", "t24", "t25", "t28", "t29", "t31", "t32", "t35", "t39", "t40", "t41", "t42", "t56"];

export const FALAS_RESUMO: Fala[] = [
  { id: "r01", beat: "t01", chapter: "abertura", say: "Este é o Midview, o programa que coloca o laboratório de informática inteiro na tela do professor. Veja, em três minutos, o principal do que ele faz." },
  // Sem a conversa com o aluno antes, "Enviar arquivo" começa direto.
  ...RESUMO_IDS.map((id) => ({ ...FALAS.find((f) => f.id === id)!, ...(id === "t28" ? { beat: "t28r" } : {}) })),
];

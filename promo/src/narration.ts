/**
 * Narração do vídeo: voz "Amanda" do ElevenLabs v4, gerada pelo Higgsfield
 * (preset 22aeaea5-3677-5fd7-b932-34f362087a9b). Cada fala é um arquivo em
 * public/narracao/<id>.mp3, preparado por scripts/narracao.mjs, e começa no
 * frame absoluto `at`, junto com o clique ou a legenda que ela explica
 * (components/app/tour.ts). Cada fala termina antes do `at` da seguinte.
 */
export type NarrationLine = { id: string; at: number; text: string };

export const NARRATION: NarrationLine[] = [
  { id: "01", at: 36, text: "Controle total do seu laboratório. Sem complicação. Conheça o Midview." },
  { id: "02", at: 198, text: "Na janela principal, você vê ao vivo a tela de cada aluno." },
  { id: "03", at: 339, text: "Ajuste o tamanho das telas e veja só as máquinas ligadas." },
  { id: "04", at: 488, text: "No grupo Tela, veja a tela de qualquer aluno." },
  { id: "05", at: 618, text: "Ou assuma o controle quando o aluno precisar de ajuda." },
  { id: "06", at: 739, text: "E bloqueie todas as telas com um clique." },
  { id: "07", at: 848, text: "Em Turma, mande uma mensagem para todos os alunos de uma vez." },
  { id: "08", at: 990, text: "Converse com a turma no bate-papo." },
  { id: "09", at: 1068, text: "Envie arquivos para os alunos e recolha os trabalhos entregues." },
  { id: "10", at: 1244, text: "Abra um site em todas as máquinas." },
  { id: "11", at: 1339, text: "Bloqueie os programas que tiram o foco da aula." },
  { id: "12", at: 1444, text: "Bloqueie a internet ou só alguns sites." },
  { id: "13", at: 1549, text: "E feche os programas da turma, com um aviso para o aluno salvar o trabalho." },
  { id: "14", at: 1699, text: "Ligue as máquinas pela rede, sem sair da sua mesa." },
  { id: "15", at: 1813, text: "E agende o horário para o laboratório ligar e desligar sozinho." },
  { id: "16", at: 1986, text: "Prepare o laboratório num painel só." },
  { id: "17", at: 2070, text: "Atualize o Midview em todas as máquinas." },
  { id: "18", at: 2175, text: "E faça tudo isso numa máquina só, pelo botão direito." },
  { id: "19", at: 2310, text: "Leve na rede, centralizado e feito para a sala de aula." },
  { id: "20", at: 2498, text: "Midview: o futuro da gestão de laboratórios. Transforme sua sala de aula." },
];

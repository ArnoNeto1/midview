import { WIN, type Box, type Point } from "./geometry";

/**
 * Janelas que os botões abrem, centradas na janela principal (CenterOwner), e a
 * posição dos controles em que o cursor clica. Os diálogos desenham os controles
 * nestas mesmas caixas, então o clique cai sempre no lugar certo.
 */
const centered = (w: number, h: number): Box => ({ x: Math.round((WIN.w - w) / 2), y: Math.round((WIN.h - h) / 2) + 14, w, h });

export const DLG = {
  view: centered(1000, 636),
  message: centered(480, 240),
  chat: centered(440, 470),
  sendFile: centered(560, 340),
  collect: centered(560, 340),
  openSite: centered(520, 270),
  blockApps: centered(600, 430),
  blockSites: centered(580, 310),
  closeApps: centered(580, 370),
  schedule: centered(580, 330),
  labPanel: centered(640, 470),
  machines: centered(600, 450),
  update: centered(480, 220),
} as const;

export type DialogId = keyof typeof DLG;

/** Controles, relativos à caixa do diálogo. */
export const CTRL = {
  viewFull: { x: 16, y: 42, w: 104, h: 28 },
  viewDisconnect: { x: 128, y: 42, w: 118, h: 28 },
  viewScreen: { x: 16, y: 78, w: 968, h: 544 },
  messageText: { x: 16, y: 46, w: 448, h: 128 },
  messageSend: { x: 368, y: 190, w: 96, h: 32 },
  chatList: { x: 16, y: 46, w: 408, h: 330 },
  chatInput: { x: 16, y: 388, w: 316, h: 32 },
  chatSend: { x: 340, y: 388, w: 84, h: 32 },
  chatClose: { x: 394, y: 0, w: 46, h: 32 },
  sendFileChoose: { x: 16, y: 92, w: 156, h: 30 },
  sendFileSend: { x: 444, y: 292, w: 100, h: 32 },
  collectRun: { x: 16, y: 98, w: 112, h: 32 },
  collectOpen: { x: 136, y: 98, w: 112, h: 32 },
  openSiteInput: { x: 16, y: 104, w: 488, h: 30 },
  openSiteRun: { x: 354, y: 222, w: 150, h: 32 },
  blockAppsList: { x: 16, y: 176, w: 568, h: 166 },
  blockAppsApply: { x: 16, y: 378, w: 140, h: 32 },
  blockSitesInput: { x: 16, y: 196, w: 548, h: 30 },
  blockSitesSite: { x: 16, y: 240, w: 140, h: 30 },
  closeAppsRun: { x: 414, y: 322, w: 150, h: 32 },
  scheduleSave: { x: 394, y: 282, w: 170, h: 32 },
  labFirstOn: { x: 434, y: 146, w: 86, h: 28 },
  machinesUpdate: { x: 16, y: 182, w: 252, h: 32 },
} as const;

export type ControlId = keyof typeof CTRL;

/** Ponto, em coordenadas da janela, no meio de um controle de um diálogo. */
export const controlTarget = (dialog: DialogId, control: ControlId, dx = 0, dy = 0): Point => {
  const d = DLG[dialog];
  const c = CTRL[control];
  return { x: d.x + c.x + c.w / 2 + dx, y: d.y + c.y + c.h / 2 + dy };
};

/** As linhas do Painel do laboratório (src/Midview.Master/LabConfigWindow.xaml). */
export const LAB_STEPS = [
  { title: "Passo 1 — Bloqueador de anúncios", text: "Instala o bloqueador no Chrome, que aparece sozinho na próxima vez que o aluno abrir." },
  { title: "Passo 2 — Limpeza ao fechar", text: "O Chrome apaga histórico, cookies, senhas e downloads quando fecha." },
  { title: "Passo 3 — Blindagem total", text: "Trava as configurações, o modo anônimo e a página inicial do Chrome, e o papel de parede." },
  { title: "Passo 4 — Bloquear o Microsoft Edge", text: "O Edge não abre nas máquinas dos alunos." },
  { title: "Passo 5 — Bloquear downloads", text: "O aluno não consegue baixar arquivos pelo navegador." },
];
export const LAB_ROW = { top: 140, height: 62 } as const;

/** Itens do menu do botão direito de uma máquina (MainWindow.xaml), "-" = separador. */
export const MENU_ITEMS = [
  "Ver a tela",
  "Controlar (trava o teclado e o mouse do aluno)",
  "Bloquear a tela",
  "Desbloquear a tela",
  "-",
  "Enviar mensagem...",
  "Conversar (chat)...",
  "Enviar arquivo...",
  "Recolher arquivos...",
  "Abrir site ›",
  "Abrir programa ›",
  "-",
  "Fechar programas...",
  "Bloquear programas...",
  "Bloquear sites...",
  "Painel do laboratório...",
  "Desinstalar programa...",
  "-",
  "Desligar...",
  "Reiniciar...",
  "Ligar",
  "-",
  "Editar a máquina (avançado)...",
  "Remover da lista da sala",
];
export const MENU = { w: 330, item: 27, separator: 9, pad: 6 } as const;

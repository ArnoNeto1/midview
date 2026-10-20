import {
  FolderOpen,
  Globe,
  LayoutGrid,
  MessageSquare,
  Minus,
  Search,
  Settings,
  Square,
  Users,
  Wifi,
  X,
  type LucideIcon,
} from "lucide-react";
import React from "react";
import { MidviewLogo } from "../MidviewLogo";
import { Wordmark } from "../Wordmark";
import { GRID, WINDOW, WINDOW_HEIGHT, cardRect, type Point } from "./layout";
import { PcCard, type CardState } from "./PcCard";
import { PCS } from "./pcs";

type DashboardWindowProps = {
  /** Frame absoluto do vídeo (conteúdo ao vivo das telas). */
  frame: number;
  cards: CardState[];
  onlineCount: number;
  /** Opacidade do texto de status no rodapé. */
  footer?: number;
  /** Escurece barra de título, menu lateral e cabeçalho (foco na barra de comandos). */
  dim?: number;
  castOrigin?: Point;
  /** Camadas extras em coordenadas da janela: barra de comandos, cursor, ondas. */
  children?: React.ReactNode;
};

/** Mock da janela principal do midview: grade 4×3 com as telas dos alunos. */
export const DashboardWindow: React.FC<DashboardWindowProps> = ({
  frame,
  cards,
  onlineCount,
  footer = 1,
  dim = 0,
  castOrigin,
  children,
}) => (
  <div
    className="relative overflow-hidden border border-white/10 backdrop-blur-xl"
    style={{
      width: WINDOW.width,
      height: WINDOW_HEIGHT,
      borderRadius: WINDOW.radius,
      background: "linear-gradient(180deg, rgba(18,24,39,0.88) 0%, rgba(10,14,25,0.94) 100%)",
      boxShadow: "0 60px 140px -40px rgba(0,0,0,0.9), 0 0 0 1px rgba(255,255,255,0.02), inset 0 1px 0 rgba(255,255,255,0.08)",
    }}
  >
    <div
      className="absolute inset-x-0 top-0 h-px"
      style={{ background: "linear-gradient(90deg, transparent, rgba(255,255,255,0.3), transparent)" }}
    />
    <TitleBar />
    <Rail />
    <Header frame={frame} onlineCount={onlineCount} />
    {PCS.map((pc, i) => (
      <PcCard key={pc.id} pc={pc} index={i} rect={cardRect(i)} state={cards[i]} frame={frame} castOrigin={castOrigin} />
    ))}
    <Footer opacity={footer} />
    {dim > 0.001 ? <ChromeDim dim={dim} /> : null}
    {children}
  </div>
);

const TitleBar: React.FC = () => (
  <div
    className="absolute inset-x-0 top-0 flex items-center border-b border-white/[0.06] bg-white/[0.02]"
    style={{ height: WINDOW.titleBar }}
  >
    <div className="flex items-center gap-2.5 pl-4" style={{ width: 380 }}>
      <MidviewLogo size={24} />
      <Wordmark size={18} />
      <span className="mx-1.5 h-4 w-px bg-white/10" />
      <span className="text-[13.5px] text-slate-400">Laboratório 01</span>
    </div>
    <div className="flex flex-1 justify-center">
      <div className="flex h-[30px] w-[340px] items-center gap-2 rounded-lg border border-white/[0.07] bg-white/[0.03] px-3 text-[13px] text-slate-500">
        <Search size={14} strokeWidth={2} />
        <span>Buscar computador ou aluno…</span>
        <span className="ml-auto rounded border border-white/10 px-1.5 font-mono text-[10.5px] text-slate-400">Ctrl K</span>
      </div>
    </div>
    <div className="flex h-full items-center justify-end text-slate-400" style={{ width: 380 }}>
      {[Minus, Square, X].map((Icon, i) => (
        <div key={i} className="flex h-full w-[46px] items-center justify-center">
          <Icon size={i === 1 ? 12 : 15} strokeWidth={1.7} />
        </div>
      ))}
    </div>
  </div>
);

const RAIL: { Icon: LucideIcon; active?: boolean }[] = [
  { Icon: LayoutGrid, active: true },
  { Icon: Users },
  { Icon: Globe },
  { Icon: MessageSquare },
  { Icon: FolderOpen },
];

const Rail: React.FC = () => (
  <div
    className="absolute bottom-0 left-0 flex flex-col items-center gap-2 border-r border-white/[0.06] py-4"
    style={{ top: WINDOW.titleBar, width: WINDOW.rail }}
  >
    {RAIL.map(({ Icon, active }, i) => (
      <div
        key={i}
        className={`relative flex h-11 w-11 items-center justify-center rounded-xl ${
          active ? "bg-white/[0.07] text-cyan-300" : "text-slate-500"
        }`}
      >
        {active ? <span className="absolute -left-3 h-5 w-[3px] rounded-r bg-cyan-400" /> : null}
        <Icon size={20} strokeWidth={1.8} />
      </div>
    ))}
    <div className="mt-auto flex h-11 w-11 items-center justify-center rounded-xl text-slate-500">
      <Settings size={20} strokeWidth={1.8} />
    </div>
  </div>
);

const Header: React.FC<{ frame: number; onlineCount: number }> = ({ frame, onlineCount }) => {
  const ping = (frame % 40) / 40;
  return (
    <div
      className="absolute flex items-center justify-between"
      style={{ left: GRID.left, right: WINDOW.padX, top: WINDOW.titleBar, height: WINDOW.header }}
    >
      <div>
        <div className="flex items-center gap-3">
          <span className="font-display text-[23px] font-bold tracking-[-0.02em] text-white">Laboratório 01</span>
          <span className="flex items-center gap-2 rounded-full border border-emerald-400/25 bg-emerald-400/10 px-2.5 py-[3px] text-[12px] font-semibold text-emerald-300">
            <span className="relative h-1.5 w-1.5">
              <span
                className="absolute inset-0 rounded-full bg-emerald-400"
                style={{ transform: `scale(${1 + ping * 2})`, opacity: 0.7 * (1 - ping) }}
              />
              <span className="absolute inset-0 rounded-full bg-emerald-400" />
            </span>
            Ao vivo
          </span>
        </div>
        <div className="mt-1 text-[13.5px] text-slate-400">
          2º Ano B · Aula de Informática · <span className="tabular-nums text-slate-300">{onlineCount} de 12 online</span>
        </div>
      </div>
      <div className="flex items-center gap-3">
        <div className="flex rounded-lg border border-white/[0.08] bg-white/[0.03] p-1 text-[13px] font-medium">
          <span className="px-3 py-1 text-slate-500">Todos</span>
          <span className="rounded-md bg-white/[0.08] px-3 py-1 text-white">Só online</span>
        </div>
        <div className="flex h-9 w-9 items-center justify-center rounded-lg border border-white/[0.08] bg-white/[0.03] text-slate-400">
          <LayoutGrid size={16} strokeWidth={2} />
        </div>
      </div>
    </div>
  );
};

const Footer: React.FC<{ opacity: number }> = ({ opacity }) =>
  opacity > 0.01 ? (
    <div
      className="absolute flex items-center justify-between text-[13px] text-slate-500"
      style={{ left: GRID.left, right: WINDOW.padX, top: GRID.top + GRID.height, height: WINDOW.footer, opacity }}
    >
      <div className="flex items-center gap-2">
        <Wifi size={15} strokeWidth={2} />
        Rede local · 12 dispositivos conectados
      </div>
      <div>Sincronizado agora</div>
    </div>
  ) : null;

const ChromeDim: React.FC<{ dim: number }> = ({ dim }) => {
  const shade = `rgba(4,7,14,${0.55 * dim})`;
  return (
    <>
      <div className="absolute inset-x-0 top-0" style={{ height: WINDOW.titleBar, background: shade }} />
      <div className="absolute bottom-0 left-0" style={{ top: WINDOW.titleBar, width: WINDOW.rail, background: shade }} />
      <div className="absolute right-0" style={{ left: WINDOW.rail, top: WINDOW.titleBar, height: WINDOW.header, background: shade }} />
    </>
  );
};

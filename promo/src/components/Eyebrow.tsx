import React from "react";

type EyebrowProps = {
  children: React.ReactNode;
  /** Cor do indicador pulsante (opcional). */
  dot?: string;
  frame?: number;
  style?: React.CSSProperties;
};

/** Rótulo pequeno em pílula, acima dos títulos. */
export const Eyebrow: React.FC<EyebrowProps> = ({ children, dot, frame = 0, style }) => {
  const ping = (frame % 40) / 40;
  return (
    <div
      className="inline-flex items-center gap-3 rounded-full border border-white/10 bg-white/[0.04] px-4 py-2 font-mono text-[15px] font-medium uppercase tracking-[0.16em] text-slate-300"
      style={style}
    >
      {dot ? (
        <span className="relative flex h-2.5 w-2.5">
          <span
            className="absolute inset-0 rounded-full"
            style={{ background: dot, transform: `scale(${1 + ping * 1.8})`, opacity: 0.6 * (1 - ping) }}
          />
          <span className="relative h-2.5 w-2.5 rounded-full" style={{ background: dot, boxShadow: `0 0 12px ${dot}` }} />
        </span>
      ) : null}
      {children}
    </div>
  );
};

import React from "react";
import { clamp01, mix } from "../../lib/motion";

type PadlockProps = {
  size: number;
  /** 0 = aberto (arco levantado) → 1 = fechado. Aceita overshoot de mola. */
  close: number;
};

/** Cadeado animado: o arco desce e encaixa no corpo, com um anel de brilho no fechamento. */
export const Padlock: React.FC<PadlockProps> = ({ size, close }) => {
  const lift = (1 - close) * 7;
  const ring = clamp01(close);
  return (
    <svg width={size} height={size} viewBox="0 0 40 40" style={{ overflow: "visible" }}>
      <circle
        cx={20}
        cy={26}
        r={mix(10, 24, ring)}
        fill="none"
        stroke="#A5B4FC"
        strokeWidth={1.5}
        opacity={ring > 0.02 ? (1 - ring) * 0.8 : 0}
      />
      <circle cx={20} cy={26} r={15} fill="rgba(129,140,248,0.28)" opacity={ring} />
      <path
        d="M13 19 V13.5 a7 7 0 0 1 14 0 V19"
        fill="none"
        stroke="#E2E8F0"
        strokeWidth={3.4}
        strokeLinecap="round"
        transform={`translate(0 ${-lift})`}
      />
      <rect x={9} y={18} width={22} height={17} rx={4.5} fill="#F1F5F9" />
      <rect x={9} y={30} width={22} height={5} rx={2.5} fill="#CBD5E1" opacity={0.6} />
      <circle cx={20} cy={25.2} r={2.3} fill="#312E81" />
      <rect x={19.1} y={26.2} width={1.8} height={4.2} rx={0.9} fill="#312E81" />
    </svg>
  );
};

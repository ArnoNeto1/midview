import React, { useMemo } from "react";
import { interpolate, random, spring, useVideoConfig } from "remotion";
import { CLAMP, EASE, mix } from "../lib/motion";
import { LOGO_CENTER, LOGO_VIEWBOX, sampleLogo } from "./logoGeometry";

type LogoParticlesProps = {
  frame: number;
  /** Centro e lado do símbolo na tela. */
  cx: number;
  cy: number;
  size: number;
  /** Frame em que as partículas começam a convergir. */
  convergeAt: number;
  /** Frame em que as partículas somem (o símbolo vetorial assume). */
  fadeOutAt: number;
};

type Particle = {
  sx: number;
  sy: number;
  tx: number;
  ty: number;
  delay: number;
  swirl: number;
  r: number;
  depth: number;
  phase: number;
  cyan: boolean;
};

/**
 * Rede de partículas que converge em espiral até o contorno do símbolo do midview.
 * As linhas de conexão encurtam conforme os nós se aproximam e acabam desenhando a logo.
 */
export const LogoParticles: React.FC<LogoParticlesProps> = ({ frame, cx, cy, size, convergeAt, fadeOutAt }) => {
  const { fps, width, height } = useVideoConfig();

  const particles = useMemo<Particle[]>(() => {
    const scale = size / LOGO_VIEWBOX;
    return sampleLogo(9).map((p, i) => {
      const angle = random(`angle-${i}`) * Math.PI * 2;
      const distance = 380 + random(`dist-${i}`) * 720;
      return {
        sx: cx + Math.cos(angle) * distance * 1.3,
        sy: cy + Math.sin(angle) * distance * 0.75,
        tx: cx + (p.x - LOGO_CENTER) * scale,
        ty: cy + (p.y - LOGO_CENTER) * scale,
        delay: random(`delay-${i}`) * 14,
        swirl: (random(`swirl-${i}`) - 0.5) * 2.4,
        r: 1.3 + random(`size-${i}`) * 2.1,
        depth: 0.5 + random(`depth-${i}`) * 0.5,
        phase: random(`phase-${i}`) * Math.PI * 2,
        cyan: p.x > LOGO_CENTER || (p.x === LOGO_CENTER && i % 2 === 0),
      };
    });
  }, [cx, cy, size]);

  // Já visíveis no frame 0: o gancho não começa com tela vazia.
  const appear = interpolate(frame, [0, 12], [0.35, 1], CLAMP);
  const fade = 1 - interpolate(frame, [fadeOutAt, fadeOutAt + 14], [0, 1], CLAMP);
  if (fade <= 0) return null;

  const converge = interpolate(frame, [convergeAt, convergeAt + 42], [0, 1], { ...CLAMP, easing: EASE.inOut });
  const linkDistance = mix(210, 32, converge);

  const nodes = particles.map((p) => {
    const k = spring({
      frame: frame - convergeAt - p.delay,
      fps,
      config: { damping: 13, stiffness: 62, mass: 1 },
      durationInFrames: 40,
    });
    const lx = mix(p.sx, p.tx, k) - cx;
    const ly = mix(p.sy, p.ty, k) - cy;
    const angle = p.swirl * (1 - k);
    const drift = (1 - Math.min(1, k)) * 14;
    return {
      x: cx + lx * Math.cos(angle) - ly * Math.sin(angle) + Math.sin(frame * 0.05 + p.phase) * drift,
      y: cy + lx * Math.sin(angle) + ly * Math.cos(angle) + Math.cos(frame * 0.045 + p.phase) * drift,
      o: appear * fade * p.depth,
    };
  });

  const links: React.ReactNode[] = [];
  for (let i = 0; i < nodes.length; i++) {
    for (let j = i + 1; j < nodes.length; j++) {
      const d = Math.hypot(nodes[i].x - nodes[j].x, nodes[i].y - nodes[j].y);
      if (d > linkDistance) continue;
      const alpha = Math.pow(1 - d / linkDistance, 1.6) * 0.6 * Math.min(nodes[i].o, nodes[j].o);
      if (alpha < 0.025) continue;
      links.push(
        <line
          key={`${i}-${j}`}
          x1={nodes[i].x}
          y1={nodes[i].y}
          x2={nodes[j].x}
          y2={nodes[j].y}
          stroke={particles[i].cyan && particles[j].cyan ? "#22D3EE" : "#CBD5E1"}
          strokeOpacity={alpha}
          strokeWidth={1}
        />,
      );
    }
  }

  return (
    <svg width={width} height={height} style={{ position: "absolute", inset: 0 }}>
      {links}
      {nodes.map((n, i) => {
        const color = particles[i].cyan ? "#67E8F9" : "#E2E8F0";
        return (
          <g key={i} opacity={n.o}>
            <circle cx={n.x} cy={n.y} r={particles[i].r * 3.2} fill={color} opacity={0.16} />
            <circle cx={n.x} cy={n.y} r={particles[i].r} fill={color} />
          </g>
        );
      })}
    </svg>
  );
};

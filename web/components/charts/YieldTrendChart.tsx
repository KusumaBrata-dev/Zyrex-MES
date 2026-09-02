import type { YieldTrendPoint } from "@/lib/api";

const W = 640;
const H = 240;
const PAD_L = 36;
const PAD_R = 16;
const PAD_T = 16;
const PAD_B = 24;
const INNER_W = W - PAD_L - PAD_R;
const INNER_H = H - PAD_T - PAD_B;

/**
 * Hand-rolled SVG yield trend: polyline + point markers, 0-100% scale.
 * Null yieldPercent days render as gaps (segment break), not zero drops.
 */
export default function YieldTrendChart({ points }: { points: YieldTrendPoint[] }) {
  const x = (i: number) => PAD_L + (points.length <= 1 ? INNER_W / 2 : (i / (points.length - 1)) * INNER_W);
  const y = (v: number) => PAD_T + INNER_H - (Math.max(0, Math.min(100, v)) / 100) * INNER_H;

  const segments: { idx: number; v: number }[][] = [];
  points.forEach((p, i) => {
    if (p.yieldPercent === null) return;
    const last = segments[segments.length - 1];
    if (last && last[last.length - 1].idx === i - 1) last.push({ idx: i, v: p.yieldPercent });
    else segments.push([{ idx: i, v: p.yieldPercent }]);
  });

  return (
    <svg viewBox={`0 0 ${W} ${H}`} className="w-full max-w-2xl" data-testid="yield-trend-chart" role="img" aria-label="Yield trend">
      {/* axes + 100% guide */}
      <line x1={PAD_L} y1={PAD_T} x2={PAD_L} y2={PAD_T + INNER_H} stroke="currentColor" opacity={0.3} />
      <line x1={PAD_L} y1={PAD_T + INNER_H} x2={PAD_L + INNER_W} y2={PAD_T + INNER_H} stroke="currentColor" opacity={0.3} />
      <line x1={PAD_L} y1={y(100)} x2={PAD_L + INNER_W} y2={y(100)} stroke="currentColor" opacity={0.15} strokeDasharray="4 4" />
      <text x={4} y={y(100) + 4} fontSize={10} fill="currentColor" opacity={0.6}>
        100
      </text>
      <text x={4} y={y(0) + 4} fontSize={10} fill="currentColor" opacity={0.6}>
        0
      </text>

      {segments.map((seg, si) => (
        <polyline
          key={si}
          data-testid="yield-trend-segment"
          fill="none"
          stroke="#C71C2E"
          strokeWidth={2}
          points={seg.map((s) => `${x(s.idx)},${y(s.v)}`).join(" ")}
        />
      ))}
      {points.map((p, i) =>
        p.yieldPercent === null ? null : (
          <circle key={p.date} data-testid="yield-point" cx={x(i)} cy={y(p.yieldPercent)} r={3} fill="#FA1A3F" />
        ),
      )}
      {points.map((p, i) => (
        <text key={p.date} data-testid={`yield-tick-${p.date}`} x={x(i)} y={H - 6} fontSize={10} textAnchor="middle" fill="currentColor" opacity={0.7}>
          {p.date.slice(5)}
        </text>
      ))}
    </svg>
  );
}

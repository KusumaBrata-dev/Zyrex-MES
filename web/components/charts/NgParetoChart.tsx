import type { NgParetoItem } from "@/lib/api";

const W = 640;
const ROW_H = 32;
const LABEL_W = 140;
const CUM_W = 64;
const BAR_MAX_W = W - LABEL_W - CUM_W - 40;

/**
 * Hand-rolled SVG Pareto: horizontal bars sorted desc with running
 * cumulative percentage at the right edge.
 */
export default function NgParetoChart({ items }: { items: NgParetoItem[] }) {
  const sorted = [...items].sort((a, b) => b.count - a.count);
  const total = sorted.reduce((s, i) => s + i.count, 0);
  const max = sorted[0]?.count ?? 0;
  const height = Math.max(sorted.length * ROW_H + 8, 40);

  let cumulative = 0;

  return (
    <svg viewBox={`0 0 ${W} ${height}`} className="w-full max-w-2xl" data-testid="ng-pareto-chart" role="img" aria-label="NG Pareto">
      {sorted.length === 0 && (
        <text x={8} y={22} fontSize={12} fill="currentColor" opacity={0.6}>
          No NG data for this period.
        </text>
      )}
      {sorted.map((it, i) => {
        cumulative += it.count;
        const pct = total > 0 ? Math.round((cumulative / total) * 1000) / 10 : 0;
        const barW = max > 0 ? (it.count / max) * BAR_MAX_W : 0;
        const y = i * ROW_H + 4;
        return (
          <g key={it.ngCode} data-testid="pareto-bar">
            <text x={0} y={y + 15} fontSize={12} fill="currentColor">
              {it.ngCode}
            </text>
            <rect x={LABEL_W} y={y} width={barW} height={20} rx={2} fill="#C71C2E" />
            <text x={LABEL_W + barW + 6} y={y + 15} fontSize={12} fill="currentColor">
              {it.count}
            </text>
            <text
              data-testid={`pareto-cum-${it.ngCode}`}
              x={W - 8}
              y={y + 15}
              fontSize={12}
              textAnchor="end"
              fill="currentColor"
              opacity={0.7}
            >
              {pct}%
            </text>
          </g>
        );
      })}
    </svg>
  );
}

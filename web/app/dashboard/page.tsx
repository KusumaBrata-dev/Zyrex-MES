"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import GridBoard from "@/components/dashboard/GridBoard";
import { authHeaders } from "@/lib/api";

const API = process.env.NEXT_PUBLIC_API_BASE ?? "";

interface Thresholds {
  minYieldPercent: number;
  yieldDropPercent: number;
  ngSpikePerHour: number;
  evaluationIntervalMinutes: number;
}

export default function DashboardPage() {
  const [filterLine, setFilterLine] = useState("All");
  const [lines, setLines] = useState<string[]>([]);
  const [thresholds, setThresholds] = useState<Thresholds | null>(null);

  useEffect(() => {
    async function load() {
      try {
        const [gridRes, thrRes] = await Promise.all([
          fetch(`${API}/api/reports/line-grid`, { headers: { ...authHeaders() } }),
          fetch(`${API}/api/insights/thresholds`, { headers: { ...authHeaders() } }),
        ]);
        if (gridRes.ok) {
          const data = (await gridRes.json()) as { lines: { lineCode: string }[] };
          setLines(data.lines.map((l) => l.lineCode));
        }
        if (thrRes.ok) {
          setThresholds((await thrRes.json()) as Thresholds);
        }
      } catch {
        // ignore; thresholds is optional badge
      }
    }
    void load();
  }, []);

  return (
    <div className="flex flex-1 flex-col gap-4 p-6" data-testid="dashboard-page">
      <div className="flex flex-wrap items-center gap-4">
        <h1 className="text-xl font-bold">Dashboard</h1>
        <label className="flex items-center gap-2 text-sm">
          Line
          <select
            value={filterLine}
            onChange={(e) => setFilterLine(e.target.value)}
            className="rounded border border-black/20 px-2 py-1 dark:border-white/20 dark:bg-neutral-800"
            data-testid="line-filter"
          >
            <option value="All">All</option>
            {lines.map((lc) => (
              <option key={lc} value={lc}>
                {lc}
              </option>
            ))}
          </select>
        </label>
        <Link
          href={`/dashboard/tv/${filterLine === "All" ? (lines[0] ?? "") : filterLine}`}
          className="text-sm text-zbright underline"
          data-testid="tv-link"
        >
          TV mode
        </Link>
        <Link href="/dashboard/insights" className="text-sm text-zbright underline" data-testid="insights-link">
          Insights
        </Link>
        {thresholds && (
          <span className="ml-auto rounded-full bg-neutral-100 px-3 py-1 text-xs dark:bg-neutral-800" data-testid="thresholds-badge">
            yield &lt; {thresholds.minYieldPercent}% · drop {thresholds.yieldDropPercent}% · NG/h {thresholds.ngSpikePerHour}
          </span>
        )}
      </div>

      <GridBoard filterLine={filterLine} />
    </div>
  );
}

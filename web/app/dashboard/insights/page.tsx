"use client";

import { useEffect, useState } from "react";
import {
  getNgPareto,
  getYieldTrend,
  type NgParetoItem,
  type YieldTrendPoint,
} from "@/lib/api";
import YieldTrendChart from "@/components/charts/YieldTrendChart";
import NgParetoChart from "@/components/charts/NgParetoChart";

const API = process.env.NEXT_PUBLIC_API_BASE ?? "";
const DAYS = 7;

function isoDate(d: Date): string {
  return d.toISOString().slice(0, 10);
}

export default function InsightsPage() {
  const [points, setPoints] = useState<YieldTrendPoint[] | null>(null);
  const [items, setItems] = useState<NgParetoItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const today = isoDate(new Date());
  const from = isoDate(new Date(Date.now() - (DAYS - 1) * 24 * 60 * 60 * 1000));

  useEffect(() => {
    async function load() {
      try {
        const [t, p] = await Promise.all([getYieldTrend(DAYS), getNgPareto(from, today)]);
        setPoints(t);
        setItems(p);
      } catch (e) {
        setError(e instanceof Error ? e.message : "failed to load insights");
      }
    }
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="flex flex-1 flex-col gap-8 p-6" data-testid="insights-page">
      <div className="flex flex-wrap items-center gap-4">
        <h1 className="text-xl font-bold">Insights</h1>
        <span className="text-sm text-neutral-500">
          {from} → {today}
        </span>
        <div className="no-print ml-auto flex items-center gap-3">
          <a
            href={`${API}/api/export/ng-list.xlsx?from=${from}&to=${today}`}
            className="rounded border border-black/20 px-3 py-1 text-sm dark:border-white/20"
            data-testid="export-ng-list"
          >
            NG List .xlsx
          </a>
          <a
            href={`${API}/api/export/station-summary.xlsx?date=${today}`}
            className="rounded border border-black/20 px-3 py-1 text-sm dark:border-white/20"
            data-testid="export-station-summary"
          >
            Station Summary .xlsx
          </a>
          <button
            type="button"
            onClick={() => window.print()}
            className="rounded bg-zred px-3 py-1 text-sm text-white"
            data-testid="print-button"
          >
            Print / PDF
          </button>
        </div>
      </div>

      {error && (
        <p role="alert" className="text-sm text-zbright">
          {error}
        </p>
      )}

      <section>
        <h2 className="mb-2 text-sm font-bold tracking-widest text-neutral-500">YIELD TREND ({DAYS} DAYS)</h2>
        {points ? <YieldTrendChart points={points} /> : <p className="text-sm text-neutral-400">Loading trend…</p>}
      </section>

      <section>
        <h2 className="mb-2 text-sm font-bold tracking-widest text-neutral-500">NG PARETO</h2>
        {items ? <NgParetoChart items={items} /> : <p className="text-sm text-neutral-400">Loading pareto…</p>}
      </section>
    </div>
  );
}

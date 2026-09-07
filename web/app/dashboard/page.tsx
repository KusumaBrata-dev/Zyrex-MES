"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import GridBoard from "@/components/dashboard/GridBoard";
import AlertsPanel from "@/components/dashboard/AlertsPanel";
import { authHeaders, type AlertDto } from "@/lib/api";
import { useAlerts } from "@/lib/useAlerts";
import { useLiveEvents } from "@/lib/useLiveEvents";

const TOAST_MS = 4_000;
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
  const { alerts, push, ack } = useAlerts();
  const [toast, setToast] = useState<AlertDto | null>(null);

  // Realtime AlertRaised → prepend panel + short red toast
  const onAlert = useCallback(
    (a: AlertDto) => {
      push(a);
      setToast(a);
    },
    [push],
  );
  useLiveEvents([], { onAlert });

  useEffect(() => {
    if (!toast) return;
    const t = window.setTimeout(() => setToast(null), TOAST_MS);
    return () => window.clearTimeout(t);
  }, [toast]);

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

  const tvLine = filterLine === "All" ? (lines[0] ?? "") : filterLine;

  return (
    <div className="flex flex-1 flex-col gap-6 p-6 animate-fade-in" data-testid="dashboard-page">
      {/* ── Header ── */}
      <header className="flex flex-wrap items-center gap-4">
        <div>
          <h1 className="text-xl font-black tracking-tight text-slate-900">
            Dashboard
          </h1>
          <p className="text-xs text-slate-400 uppercase tracking-wider mt-0.5">
            Real-time Production Monitoring
          </p>
        </div>

        {/* Line selector */}
        <label className="flex items-center gap-2 text-sm text-slate-600">
          <span className="text-xs uppercase tracking-wider text-slate-400">Line</span>
          <select
            value={filterLine}
            onChange={(e) => setFilterLine(e.target.value)}
            className="rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-700 focus:border-zred focus:outline-none focus:ring-1 focus:ring-zred/20 transition-colors"
            data-testid="line-filter"
          >
            <option value="All">All Lines</option>
            {lines.map((lc) => (
              <option key={lc} value={lc}>
                {lc}
              </option>
            ))}
          </select>
        </label>

        {/* TV mode link */}
        <Link
          href={`/dashboard/tv/${tvLine}`}
          className="flex items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm font-medium text-slate-600 hover:border-zred hover:text-zred transition-colors"
          data-testid="tv-link"
        >
          <span className="text-base">📺</span> TV Mode
        </Link>

        {/* Insights link */}
        <Link
          href="/dashboard/insights"
          className="flex items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm font-medium text-slate-600 hover:border-zred hover:text-zred transition-colors"
          data-testid="insights-link"
        >
          <span className="text-base">📊</span> Insights
        </Link>

        {/* Alerts badge */}
        <button
          onClick={() => document.querySelector('[data-testid="alerts-panel"]')?.scrollIntoView({ behavior: "smooth" })}
          className={`relative rounded-full px-3 py-1 text-xs font-bold transition-all ${
            alerts.length > 0
              ? "bg-zbright text-white shadow-lg shadow-red-100"
              : "bg-slate-100 text-slate-500 border border-slate-200"
          }`}
          data-testid="alerts-badge"
        >
          {alerts.length}
          {alerts.length > 0 && <span className="sr-only">unacknowledged alerts</span>}
        </button>

        {/* Thresholds info */}
        {thresholds && (
          <span
            className="ml-auto hidden sm:inline-flex items-center gap-2 rounded-full border border-slate-200 bg-white px-3 py-1 text-[11px] text-slate-500"
            data-testid="thresholds-badge"
          >
            <span>Yield &lt; {thresholds.minYieldPercent}%</span>
            <span className="text-slate-300">·</span>
            <span>Drop {thresholds.yieldDropPercent}%</span>
            <span className="text-slate-300">·</span>
            <span>NG/h {thresholds.ngSpikePerHour}</span>
          </span>
        )}
      </header>

      {/* ── Main content ── */}
      <div className="flex flex-1 flex-col gap-6 lg:flex-row">
        <div className="flex-1 min-w-0">
          <GridBoard filterLine={filterLine} />
        </div>
        <AlertsPanel alerts={alerts} onAck={(id) => void ack(id)} />
      </div>

      {/* ── Toast ── */}
      {toast && (
        <div
          role="alert"
          className="fixed bottom-6 right-6 max-w-sm rounded-xl border border-red-200 bg-white px-5 py-4 text-sm font-semibold text-slate-800 shadow-xl shadow-red-100 animate-fade-in"
          data-testid="alert-toast"
        >
          <div className="flex items-start gap-3">
            <span className="text-lg">⚠</span>
            <div>
              <p className="uppercase tracking-wider text-[10px] opacity-60 text-red-600 mb-0.5">{toast.severity}</p>
              <p>{toast.message}</p>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
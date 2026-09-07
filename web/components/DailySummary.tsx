"use client";

import { useCallback, useEffect, useState } from "react";
import { getStationSummary, type StationSummary } from "@/lib/api";

const REFRESH_INTERVAL_MS = 60_000;

/**
 * Today's counters for the configured station (OUTPUT / NG / YIELD%).
 * Refreshes whenever `refreshSignal` changes (each successful scan) and on a
 * 60 s interval. Yield shows "—" while there is no output yet.
 */
export default function DailySummary({ stationId, refreshSignal = 0 }: { stationId: number; refreshSignal?: number }) {
  const [summary, setSummary] = useState<StationSummary | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    try {
      const data = await getStationSummary(stationId, wibToday());
      setSummary(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "failed to load summary");
    } finally {
      setLoading(false);
    }
  }, [stationId]);

  useEffect(() => {
    void load();
    const timer = window.setInterval(() => void load(), REFRESH_INTERVAL_MS);
    return () => window.clearInterval(timer);
  }, [load, refreshSignal]);

  return (
    <section className="grid grid-cols-3 gap-4" data-testid="daily-summary">
      <Stat label="OUTPUT" value={summary ? String(summary.output) : "—"} color={summary ? "text-emerald-600" : "text-slate-300"} />
      <Stat label="NG" value={summary ? String(summary.ng) : "—"} color={summary && summary.ng > 0 ? "text-red-600" : "text-slate-300"} />
      <Stat label="YIELD" value={summary && summary.yieldPercent !== null ? `${summary.yieldPercent}%` : "—"} color={
        summary && summary.yieldPercent !== null
          ? summary.yieldPercent >= 95 ? "text-emerald-600"
          : summary.yieldPercent >= 85 ? "text-amber-500"
          : "text-red-600"
          : "text-slate-300"
      } />
      {loading && !summary && <p className="col-span-3 text-center text-xs text-slate-400">Loading...</p>}
      {error && (
        <p role="alert" className="col-span-3 text-center text-sm font-medium text-red-600">
          ⚠ {error}
        </p>
      )}
    </section>
  );
}

function Stat({ label, value, color }: { label: string; value: string; color: string }) {
  return (
    <div className="zyrex-card flex flex-col items-center justify-center p-5">
      <p className="zyrex-label mb-2">{label}</p>
      <p className={`text-4xl font-black ${color}`} style={{ fontFamily: "var(--font-jetbrains-mono)" }}>{value}</p>
    </div>
  );
}

/** Calendar date "today" in the factory timezone (WIB, UTC+7). */
export function wibToday(): string {
  return new Date(Date.now() + 7 * 3_600_000).toISOString().slice(0, 10);
}
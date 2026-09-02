"use client";

import { useCallback, useEffect, useState } from "react";
import { authHeaders } from "@/lib/api";
import { useLiveEvents } from "@/lib/useLiveEvents";
import StationTile from "@/components/dashboard/StationTile";

const API = process.env.NEXT_PUBLIC_API_BASE ?? "";
const REFRESH_MS = 30_000;

interface StationDto {
  stationId: number;
  stationCode: string;
  name: string;
  outputToday: number;
  ngToday: number;
  lastEventAtUtc: string | null;
  status: string;
}
interface LineDto {
  lineCode: string;
  stations: StationDto[];
}
interface GridDto {
  lines: LineDto[];
}

export default function GridBoard({ filterLine }: { filterLine: string }) {
  const [grid, setGrid] = useState<GridDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  const fetchGrid = useCallback(async () => {
    try {
      const res = await fetch(`${API}/api/reports/line-grid`, {
        headers: { ...authHeaders() },
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const data = (await res.json()) as GridDto;
      setGrid(data);
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "failed to load grid");
    }
  }, []);

  useEffect(() => {
    void fetchGrid();
    const t = window.setInterval(() => void fetchGrid(), REFRESH_MS);
    return () => window.clearInterval(t);
  }, [fetchGrid]);

  const lineCodes = grid?.lines.map((l) => l.lineCode) ?? [];
  const liveEvent = useLiveEvents(lineCodes);

  // Optimistic increment on live event
  useEffect(() => {
    if (!liveEvent) return;
    const { type, stationCode, atUtc } = liveEvent;
    setGrid((prev) => {
      if (!prev) return prev;
      // avoid double-increment if already at this atUtc (effect re-runs)
      const alreadyApplied = prev.lines.some((l) => l.stations.some((s) => s.stationCode === stationCode && s.lastEventAtUtc === atUtc));
      if (alreadyApplied) return prev;
      return {
        lines: prev.lines.map((line) => ({
          ...line,
          stations: line.stations.map((s) => {
            if (s.stationCode !== stationCode) return s;
            const incOutput = type === "ScanAccepted" ? 1 : 0;
            const incNg = type === "QcFailed" ? 1 : 0;
            return {
              ...s,
              outputToday: s.outputToday + incOutput,
              ngToday: s.ngToday + incNg,
              lastEventAtUtc: atUtc,
              status: "active",
            };
          }),
        })),
      };
    });
  }, [liveEvent]);

  if (error) {
    return (
      <p role="alert" className="text-sm text-zbright">
        {error}
      </p>
    );
  }
  if (!grid) {
    return <p className="text-sm text-neutral-400">Loading grid…</p>;
  }

  const lines = filterLine === "All" || !filterLine ? grid.lines : grid.lines.filter((l) => l.lineCode === filterLine);

  return (
    <div className="flex flex-col gap-8" data-testid="grid-board">
      {lines.map((line) => (
        <section key={line.lineCode} data-testid={`line-section-${line.lineCode}`}>
          <h2 className="mb-3 text-sm font-bold tracking-widest text-neutral-500">{line.lineCode}</h2>
          <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
            {line.stations.map((s) => (
              <StationTile
                key={s.stationId}
                stationId={s.stationId}
                stationCode={s.stationCode}
                name={s.name}
                outputToday={s.outputToday}
                ngToday={s.ngToday}
                lastEventAtUtc={s.lastEventAtUtc}
                status={s.status as "active" | "idle"}
              />
            ))}
          </div>
        </section>
      ))}
      {lines.length === 0 && <p className="text-sm text-neutral-400">No stations for this line.</p>}
    </div>
  );
}

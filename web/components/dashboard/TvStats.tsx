"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { authHeaders } from "@/lib/api";
import { useLiveEvents, type LiveEvent } from "@/lib/useLiveEvents";

const API = process.env.NEXT_PUBLIC_API_BASE ?? "";
const REFRESH_MS = 10_000;
const MAX_EVENTS = 5;

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

function formatTime(atUtc: string): string {
  const d = new Date(atUtc);
  return Number.isNaN(d.getTime()) ? atUtc : d.toLocaleTimeString();
}

/**
 * Andon TV fullscreen view for a single line. Polls line-grid every 10s
 * (client-side filtered) and applies live hub events optimistically,
 * keeping the last few events visible as a scrolling feed.
 */
export default function TvStats({ lineCode }: { lineCode: string }) {
  const [line, setLine] = useState<LineDto | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [events, setEvents] = useState<LiveEvent[]>([]);

  const fetchGrid = useCallback(async () => {
    try {
      const res = await fetch(`${API}/api/reports/line-grid`, {
        headers: { ...authHeaders() },
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const data = (await res.json()) as GridDto;
      const found = data.lines.find((l) => l.lineCode === lineCode) ?? null;
      setLine(found);
      setNotFound(!found);
    } catch {
      // keep last known state; next tick retries
    }
  }, [lineCode]);

  useEffect(() => {
    void fetchGrid();
    const t = window.setInterval(() => void fetchGrid(), REFRESH_MS);
    return () => window.clearInterval(t);
  }, [fetchGrid]);

  const liveEvent = useLiveEvents([lineCode]);

  useEffect(() => {
    if (!liveEvent || liveEvent.lineCode !== lineCode) return;
    const { type, stationCode, atUtc } = liveEvent;
    setEvents((prev) => [liveEvent, ...prev.filter((e) => e.atUtc !== atUtc)].slice(0, MAX_EVENTS));
    setLine((prev) => {
      if (!prev) return prev;
      const alreadyApplied = prev.stations.some((s) => s.stationCode === stationCode && s.lastEventAtUtc === atUtc);
      if (alreadyApplied) return prev;
      return {
        ...prev,
        stations: prev.stations.map((s) =>
          s.stationCode !== stationCode
            ? s
            : {
                ...s,
                outputToday: s.outputToday + (type === "ScanAccepted" ? 1 : 0),
                ngToday: s.ngToday + (type === "QcFailed" ? 1 : 0),
                lastEventAtUtc: atUtc,
                status: "active",
              },
        ),
      };
    });
  }, [liveEvent, lineCode]);

  if (notFound) {
    return (
      <div className="flex h-screen flex-col items-center justify-center gap-4 bg-black text-white" data-testid="tv-not-found">
        <p className="text-2xl font-semibold">Line {lineCode} not found</p>
        <Link href="/dashboard" className="rounded bg-white/10 px-4 py-2 hover:bg-white/20" data-testid="tv-exit">
          Exit TV
        </Link>
      </div>
    );
  }

  if (!line) {
    return (
      <div className="flex h-screen items-center justify-center bg-black text-white/50" data-testid="tv-loading">
        Loading line {lineCode}…
      </div>
    );
  }

  const totalOutput = line.stations.reduce((sum, s) => sum + s.outputToday, 0);
  const totalNg = line.stations.reduce((sum, s) => sum + s.ngToday, 0);
  const yieldPercent = totalOutput > 0 ? Math.round(((totalOutput - totalNg) / totalOutput) * 1000) / 10 : null;

  return (
    <div className="flex h-screen cursor-none flex-col bg-black p-8 text-white" data-testid="tv-page">
      <header className="flex items-center justify-between border-b border-white/20 pb-6">
        <h1 className="text-6xl font-black tracking-wide" data-testid="tv-line-code">
          LINE {lineCode}
        </h1>
        <Link href="/dashboard" className="cursor-pointer rounded bg-white/10 px-4 py-2 text-sm hover:bg-white/20" data-testid="tv-exit">
          Exit TV
        </Link>
      </header>

      <main className="flex flex-1 flex-col gap-8 overflow-hidden pt-8">
        <div className="grid grid-cols-3 gap-6">
          <div className="rounded-2xl bg-white/5 p-6 text-center">
            <p className="text-sm font-bold tracking-widest text-white/60">OUTPUT</p>
            <p className="mt-2 text-8xl font-black text-emerald-400" data-testid="tv-output">
              {totalOutput}
            </p>
          </div>
          <div className="rounded-2xl bg-white/5 p-6 text-center">
            <p className="text-sm font-bold tracking-widest text-white/60">NG</p>
            <p className="mt-2 text-8xl font-black text-zbright" data-testid="tv-ng">
              {totalNg}
            </p>
          </div>
          <div className="rounded-2xl bg-white/5 p-6 text-center">
            <p className="text-sm font-bold tracking-widest text-white/60">YIELD</p>
            <p className="mt-2 text-8xl font-black" data-testid="tv-yield">
              {yieldPercent === null ? "—" : `${yieldPercent}%`}
            </p>
          </div>
        </div>

        <section className="flex-1 overflow-hidden">
          <h2 className="mb-3 text-lg font-semibold text-white/70">Recent Events</h2>
          {events.length === 0 ? (
            <p className="text-white/40" data-testid="tv-events-empty">
              No events yet.
            </p>
          ) : (
            <ul className="space-y-2" role="list">
              {events.map((e) => (
                <li
                  key={`${e.atUtc}-${e.stationCode}-${e.type}`}
                  className="flex items-center gap-4 rounded bg-white/5 px-4 py-2 text-lg"
                  data-testid="tv-event-item"
                >
                  <span
                    className={`inline-block h-3 w-3 rounded-full ${
                      e.type === "ScanAccepted" ? "bg-emerald-400" : e.type === "QcFailed" ? "bg-zbright" : "bg-amber-400"
                    }`}
                  />
                  <span className="font-semibold">{e.type}</span>
                  <span className="text-white/70">{e.stationCode}</span>
                  <span className="ml-auto text-sm text-white/50">{formatTime(e.atUtc)}</span>
                </li>
              ))}
            </ul>
          )}
        </section>
      </main>
    </div>
  );
}

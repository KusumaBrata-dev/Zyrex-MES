"use client";

import { useEffect, useState, useCallback } from "react";

interface StationStat {
  id: number;
  code: string;
  name: string;
  output: number;
  ng: number;
  yield: number;
  status: "active" | "idle";
  lastEventAtUtc: string | null;
}

interface NgItem {
  Sn: string;
  StationCode: string;
  NgCode: string | null;
  Notes: string | null;
  CheckedAtUtc: string;
}

interface TvStatsProps {
  lineCode: string;
}

function wibDateStr(): string {
  return new Date(Date.now() + 7 * 3600 * 1000).toISOString().split("T")[0];
}

export default function TvStats({ lineCode }: TvStatsProps) {
  const [stations, setStations] = useState<StationStat[]>([]);
  const [events, setEvents] = useState<NgItem[]>([]);
  const [cursorHidden, setCursorHidden] = useState(false);
  const [lastRefresh, setLastRefresh] = useState<Date>(new Date());

  const fetchData = useCallback(async () => {
    try {
      const base = process.env.NEXT_PUBLIC_API_BASE ?? "";
      const headers: Record<string, string> = {
        "Content-Type": "application/json",
      };
      const token = localStorage.getItem("kiosk_token") || localStorage.getItem("token") || "";
      if (token) headers.Authorization = `Bearer ${token}`;

      // line-grid already has OutputToday/NgToday per station — no N+1 station-summary needed
      const gridRes = await fetch(`${base}/api/reports/line-grid`, { headers });
      if (!gridRes.ok) throw new Error("grid fetch failed");
      const gridData = await gridRes.json();
      const lineData = (gridData.lines ?? gridData.Lines ?? []).find((l: any) => (l.lineCode ?? l.LineCode) === lineCode);
      if (!lineData) return;

      const rawStations = lineData.stations ?? lineData.Stations ?? [];
      const stats: StationStat[] = rawStations.map((s: any) => {
        const output = s.outputToday ?? s.OutputToday ?? s.output ?? 0;
        const ng = s.ngToday ?? s.NgToday ?? s.ng ?? 0;
        return {
          id: s.stationId ?? s.StationId ?? s.id,
          code: s.stationCode ?? s.StationCode ?? s.code,
          name: s.name ?? s.Name ?? s.code,
          output,
          ng,
          yield: output > 0 ? Math.round((100 * (output - ng)) / output * 10) / 10 : 0,
          status: (s.status ?? s.Status) as "active" | "idle",
          lastEventAtUtc: s.lastEventAtUtc ?? s.LastEventAtUtc ?? null,
        };
      });
      setStations(stats);

      // reuse existing /ng-list endpoint (no new backend) — filter to this line's stations
      const allowed = new Set(stats.map((s) => s.code));
      const date = wibDateStr();
      const ngRes = await fetch(`${base}/api/reports/ng-list?date=${date}&page=1&pageSize=25`, { headers });
      if (ngRes.ok) {
        const ngData = await ngRes.json();
        const items: NgItem[] = ngData.items ?? ngData.Items ?? [];
        const filtered = items.filter((it) => allowed.has(it.StationCode)).slice(0, 5);
        setEvents(filtered);
      }
      setLastRefresh(new Date());
    } catch (e) {
      console.error(e);
    }
  }, [lineCode]);

  useEffect(() => {
    fetchData();
    const interval = setInterval(fetchData, 10000);
    return () => clearInterval(interval);
  }, [fetchData]);

  // Auto-hide cursor after 3s inactivity
  useEffect(() => {
    let timer: ReturnType<typeof setTimeout>;
    const reset = () => {
      setCursorHidden(false);
      clearTimeout(timer);
      timer = setTimeout(() => setCursorHidden(true), 3000);
    };
    window.addEventListener("mousemove", reset);
    window.addEventListener("keypress", reset);
    window.addEventListener("touchstart", reset);
    reset();
    return () => {
      window.removeEventListener("mousemove", reset);
      window.removeEventListener("keypress", reset);
      window.removeEventListener("touchstart", reset);
      clearTimeout(timer);
    };
  }, []);

  const totalOutput = stations.reduce((sum, s) => sum + s.output, 0);
  const totalNg = stations.reduce((sum, s) => sum + s.ng, 0);
  // ponytail: aggregate yield, not mean(yields)
  const totalYield = totalOutput === 0 ? null : Math.round((100 * (totalOutput - totalNg)) / totalOutput * 10) / 10;
  const yieldForSvg = totalYield ?? 0;

  // Yield ring SVG path (75% circle for 100%)
  const radius = 80;
  const circumference = 2 * Math.PI * radius;
  const yieldOffset = circumference - (yieldForSvg / 100) * circumference;

  return (
    <div
      className={`relative flex flex-col h-screen bg-black text-white overflow-hidden ${cursorHidden ? "cursor-none" : ""}`}
      data-testid="tv-page"
    >
      {/* Scan-line effect */}
      <div className="pointer-events-none absolute inset-0 overflow-hidden opacity-10">
        <div className="absolute inset-x-0 h-px bg-white animate-[zyrex-scanline_4s_linear_infinite]" />
      </div>

      {/* ── Header ── */}
      <header className="flex items-center justify-between p-8 border-b border-white/10 relative z-10">
        <div>
          <h1 className="text-7xl font-black tracking-tight text-white uppercase" data-testid="tv-line-code">
            LINE <span className="text-zbright">{lineCode}</span>
          </h1>
          <p className="text-sm text-white/40 mt-1 font-mono">
            Updated {lastRefresh.toLocaleTimeString("en-GB")} · {stations.length} stations
          </p>
        </div>
          <button
            onClick={() => window.history.back()}
            className="px-5 py-2.5 rounded-lg border border-white/20 text-sm font-semibold hover:bg-white/10 transition-colors"
            data-testid="tv-exit"
          >
            Exit TV
          </button>
      </header>

      {/* ── Main KPIs ── */}
      <main className="flex-1 flex flex-col gap-8 p-8 relative z-10">
        <div className="grid grid-cols-3 gap-6">
          {/* OUTPUT */}
          <div className="zyrex-card flex flex-col items-center justify-center py-10" data-testid="tv-output">
            <span className="zyrex-label mb-2">OUTPUT</span>
            <p className="text-8xl font-black text-zyrex-success" style={{ fontFamily: "var(--font-jetbrains-mono)", letterSpacing: "-0.03em" }}>
              {totalOutput}
            </p>
            <span className="text-sm text-white/40 mt-2 font-mono">pcs</span>
          </div>

          {/* NG */}
          <div className="zyrex-card flex flex-col items-center justify-center py-10" data-testid="tv-ng">
            <span className="zyrex-label mb-2">NG</span>
            <p className={`text-8xl font-black ${totalNg > 0 ? "text-zbright" : "text-white/30"}`}
              style={{ fontFamily: "var(--font-jetbrains-mono)", letterSpacing: "-0.03em" }}
            >
              {totalNg}
            </p>
            <span className="text-sm text-white/40 mt-2 font-mono">pcs</span>
          </div>

          {/* YIELD with ring */}
          <div className="zyrex-card flex flex-col items-center justify-center py-10" data-testid="tv-yield">
            <span className="zyrex-label mb-2">YIELD</span>
            {totalYield === null ? (
              <p className="text-6xl font-black text-white/30" style={{ fontFamily: "var(--font-jetbrains-mono)" }}>—</p>
            ) : (
              <>
                <div className="relative w-48 h-48">
                  <svg viewBox="0 0 200 200" className="w-full h-full -rotate-90">
                    <circle cx="100" cy="100" r={radius} fill="none" stroke="rgba(255,255,255,0.08)" strokeWidth="12" />
                    <circle
                      cx="100" cy="100" r={radius}
                      fill="none"
                      stroke={totalYield >= 95 ? "#10B981" : totalYield >= 85 ? "#F59E0B" : "#FA1A3F"}
                      strokeWidth="12"
                      strokeLinecap="round"
                      strokeDasharray={circumference}
                      strokeDashoffset={yieldOffset}
                      className="transition-all duration-700 ease-out"
                    />
                  </svg>
                  <div className="absolute inset-0 flex flex-col items-center justify-center">
                    <span className="text-6xl font-black text-white" style={{ fontFamily: "var(--font-jetbrains-mono)" }}>
                      {totalYield}%
                    </span>
                  </div>
                </div>
              </>
            )}
          </div>
        </div>

        {/* ── Recent Events ── */}
        <div className="flex-1 min-h-0">
          <div className="flex items-center justify-between mb-4">
            <h2 className="text-base font-semibold uppercase tracking-widest text-white/60">Recent Events</h2>
            <span className="text-xs text-white/30 font-mono">Last 5 events · auto-scroll</span>
          </div>
          <ul className="space-y-2 font-mono text-sm" role="list">
            {events.length === 0 ? (
              <li className="py-12 text-center text-white/30 text-base" data-testid="tv-events-empty">
                No NG events today
              </li>
            ) : (
              events.map((ev, i) => (
                <li
                  key={i}
                  className="flex items-center gap-4 px-4 py-3 rounded-lg border border-white/10 bg-white/5 hover:bg-white/10 transition-colors"
                  data-testid="tv-event-item"
                >
                  <span className={`w-2 h-2 rounded-full flex-shrink-0 ${ev.NgCode ? "bg-zbright animate-pulse-ring" : "bg-zyrex-success"}`} />
                  <span className="font-bold text-white min-w-[100px]">{ev.StationCode}</span>
                  <span className={`uppercase font-semibold tracking-wider ${ev.NgCode ? "text-zbright" : "text-zyrex-success"}`}>
                    {ev.NgCode ?? "OK"}
                  </span>
                  <span className="text-white/40 flex-1 truncate">{ev.Sn}</span>
                  <span className="text-white/30 text-xs">
                    {new Date(ev.CheckedAtUtc).toLocaleTimeString("en-GB")}
                  </span>
                </li>
              ))
            )}
          </ul>
        </div>
      </main>
    </div>
  );
}
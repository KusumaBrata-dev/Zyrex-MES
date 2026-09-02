"use client";

import { useEffect, useState } from "react";

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

interface TvStatsProps {
  lineCode: string;
}

export default function TvStats({ lineCode }: TvStatsProps) {
  const [stations, setStations] = useState<StationStat[]>([]);

  useEffect(() => {
    let cancelled = false;
    let interval: ReturnType<typeof setInterval>;

    const fetchStats = async () => {
      try {
        // Fetch line-grid for stations in this line
        const stationsRes = await fetch(
          `${process.env.NEXT_PUBLIC_API_BASE ?? ""}/api/reports/line-grid`,
          {
            headers: {
              "Content-Type": "application/json",
              Authorization: `Bearer ${localStorage.getItem("kiosk_token") || ""}`,
            },
          });
        if (!stationsRes.ok) throw new Error("Failed to fetch stations");
        const stationsData = await stationsRes.json();
        const lineData = stationsData.lines.find((l: any) => l.lineCode === lineCode);
        if (!lineData) return;
        const stationIds = lineData.stations.map((s: any) => s.id);

        // Fetch station-summary for each station
        const summaries = await Promise.all(
          stationIds.map(async (sid) => {
            const res = await fetch(
              `${process.env.NEXT_PUBLIC_API_BASE}/api/reports/station-summary?stationId=${sid}&date=${new Date().toISOString().split("T")[0]}`,
              {
                headers: {
                  Authorization: `Bearer ${localStorage.getItem("kiosk_token") || ""}`,
                },
              });
            const data = await res.json();
            return { stationId: sid, data: await res.json() };
          })
        );

        const summariesByStation = Object.fromEntries(summaries.map(s => [s.stationId, s.data]));

        const stationsWithStats = lineData.stations.map((s: any) => {
          const summary = summariesByStation[s.id];
          return {
            id: s.id,
            code: s.code,
            name: s.name,
            output: summary?.output ?? 0,
            ng: summary?.ng ?? 0,
            yield: summary?.output
              ? Math.round(100 * (summary.output - summary.ng) / summary.output * 10) / 10
              : 0,
            status: summary?.ng && summary.ng > 0 ? "active" : "idle",
            lastEventAtUtc: summary?.lastEventAtUtc ?? null,
          };
        });

        setStations(stationsWithStats);
      } catch (e) {
        console.error(e);
      }
    };

    fetchStats();
    interval = setInterval(fetchStats, 10000);
    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, [lineCode]);

  if (stations.length === 0) {
    return (
      <div className="flex h-screen items-center justify-center text-white/50">
        Loading line {lineCode}...
      </div>
    );

    const totalOutput = stations.reduce((sum, s) => sum + s.output, 0);
    const totalNg = stations.reduce((sum, s) => sum + s.ng, 0);
    const totalYield = stations.length
      ? Math.round(stations.reduce((sum, s) => sum + s.yield, 0) / stations.length * 10) / 10
      : 0;

    return (
      <div className="flex flex-col h-screen bg-black text-white p-8" data-testid="tv-page">
        <header className="flex items-center justify-between p-6 border-b border-white/20">
          <h1 className="text-4xl font-bold">Line {lineCode}</h1>
          <div className="flex items-center gap-4">
            <span className="text-sm opacity-70">{new Date().toLocaleTimeString()}</span>
            <button
              onClick={() => window.history.back()}
              className="px-4 py-2 rounded bg-white/10 hover:bg-white/20"
            >
              Exit TV
            </button>
          </div>
        </header>

        <main className="flex-1 flex flex-col gap-8 p-6 overflow-auto">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            <StatCard label="OUTPUT" value={stations.reduce((sum, s) => sum + s.output, 0)} unit="pcs" />
            <StatCard label="NG" value={stations.reduce((s, s) => s + s.ng, 0)} color="red" />
            <StatCard label="YIELD" value={`${stations.length ? Math.round(stations.reduce((s, s) => s + s.yield, 0) / stations.length * 10) / 10 : 0}%`} />
          </div>

          <div className="flex-1">
            <h2 className="text-lg font-semibold mb-4">Recent Events</h2>
            <ul className="space-y-2" role="list">
              {/* events will be injected here */}
            </ul>
          </div>
        </main>
      </div>
    );
  }

  const stationsRes = await fetch(
    `${process.env.NEXT_PUBLIC_API_BASE ?? ""}/api/reports/line-grid`,
    {
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${localStorage.getItem("kiosk_token") || ""}`,
      },
    });
    if (!stationsRes.ok) throw new Error("Failed to fetch stations");
    const stationsData = await stationsRes.json();
    const lineData = stationsRes.lines.find((l: any) => l.lineCode === lineCode);
    if (!lineData) return;

    const stationIds = lineData.stations.map((s: any) => s.id);
    const summaries = await Promise.all(
      stationIds.map(async (sid) => {
        const res = await fetch(
          `${process.env.NEXT_PUBLIC_API_BASE}/api/reports/station-summary?stationId=${sid}&date=${new Date().toISOString().split("T")[0]}`,
          {
            headers: {
              Authorization: `Bearer ${localStorage.getItem("kiosk_token") || ""}`,
            },
          });
        const data = await res.json();
        return { stationId: sid, data: await res.json() };
      })
    );

    const summariesByStation = Object.fromEntries(summaries.map(s => [s.stationId, s.data]));

    const stationsWithStats = lineData.stations.map((s: any) => {
      const summary = summariesByStation[s.id];
      return {
        id: s.id,
        code: s.code,
        name: s.name,
        output: summary?.output ?? 0,
        ng: summary?.ng ?? 0,
        yield: summary?.output
          ? Math.round(100 * (summary.output - summary.ng) / summary.output * 10) / 10
          : 0,
        status: summary?.ng && summary.ng > 0 ? "active" : "idle",
        lastEventAtUtc: summary?.lastEventAtUtc ?? null,
      });
    });

    setStations(stationsWithStats);
  }, [lineCode]);

  if (stations.length === 0) {
    return (
      <div className="flex h-screen items-center justify-center text-white/50">
        Loading line {lineCode}...
      </div>
    );

    const totalOutput = stations.reduce((sum, s) => sum + s.output, 0);
    const totalNg = stations.reduce((sum, s) => sum + s.ng, 0);
    const totalYield = stations.length
      ? Math.round(stations.reduce((sum, s) => sum + s.yield, 0) / stations.length * 10) / 10
      : 0;

    return (
      <div className="flex flex-col h-screen bg-black text-white p-8" data-testid="tv-page">
        <header className="flex items-center justify-between p-6 border-b border-white/20">
          <h1 className="text-4xl font-bold">Line {lineCode}</h1>
          <div className="flex items-center gap-4">
            <span className="text-sm opacity-70">{new Date().toLocaleTimeString()}</span>
            <button
              onClick={() => window.history.back()}
              className="px-4 py-2 rounded bg-white/10 hover:bg-white/20"
            >
              Exit TV
            </button>
          </div>
        </header>

        <main className="flex-1 flex flex-col gap-8 p-6">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            <StatCard label="OUTPUT" value={totalOutput} unit="pcs" />
            <StatCard label="NG" value={totalNg} color="red" />
            <StatCard label="YIELD" value={`${totalYield.toFixed(1)}%`} />
          </div>

          <div className="flex-1">
            <h2 className="text-lg font-semibold mb-4">Recent Events</h2>
            <ul className="space-y-2" role="list">
              {/* events will be injected here */}
            </ul>
          </div>
        </main>
      </div>
    );
  }

  interface StatCardProps {
    label: string;
    value: string | number;
    unit?: string;
    color?: "red" | "green" | "blue";
  }

  function StatCard({ label, value, unit, color }: StatCardProps) {
    return (
      <div className={`p-6 rounded-xl border border-white/10 ${color === "red" ? "border-red-500/30" : color === "green" ? "border-green-500/30" : "border-blue-500/30"} bg-white/5`}>
        <p className="text-sm opacity-70 mb-1">{label}</p>
        <p className={`text-4xl font-bold ${color === "red" ? "text-red-400" : color === "green" ? "text-green-400" : "text-blue-400"}`}>
          {value}
          {unit && <span className="text-xl font-normal ml-1 opacity-70">{unit}</span>}
        </p>
      </div>
    );
  }
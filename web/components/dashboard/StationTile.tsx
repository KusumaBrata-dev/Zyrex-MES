"use client";

import { useEffect, useState } from "react";
import Link from "next/link";

export interface StationTileProps {
  stationId: number;
  stationCode: string;
  name: string;
  outputToday: number;
  ngToday: number;
  lastEventAtUtc: string | null;
  status: "active" | "idle";
}

export default function StationTile({
  stationId,
  stationCode,
  name,
  outputToday,
  ngToday,
  lastEventAtUtc,
  status,
}: StationTileProps) {
  const [displayOutput, setDisplayOutput] = useState(outputToday);
  const [displayNg, setDisplayNg] = useState(ngToday);
  const [popped, setPopped] = useState(false);

  useEffect(() => {
    if (outputToday !== displayOutput) {
      setPopped(true);
      setDisplayOutput(outputToday);
      const t = setTimeout(() => setPopped(false), 200);
      return () => clearTimeout(t);
    }
  }, [outputToday, displayOutput]);

  useEffect(() => {
    if (ngToday !== displayNg) {
      setPopped(true);
      setDisplayNg(ngToday);
      const t = setTimeout(() => setPopped(false), 200);
      return () => clearTimeout(t);
    }
  }, [ngToday, displayNg]);

  const yieldPercent = outputToday > 0
    ? Math.round((100 * (outputToday - ngToday)) / outputToday)
    : null;

  const isActive = status === "active";

  return (
    <Link
      href={`/ng-report?stationId=${stationId}`}
      data-testid={`station-tile-${stationCode}`}
      className="zyrex-card zyrex-card-hover flex flex-col p-4 cursor-pointer animate-fade-in relative overflow-hidden"
    >
      {/* Status indicator line */}
      <div
        className={`absolute top-0 left-0 right-0 h-[3px] ${
          isActive ? "bg-zyrex-success" : "bg-slate-200"
        }`}
        data-testid={`station-status-${stationCode}`}
      />

      {/* Header */}
      <div className="flex items-start justify-between mb-3 mt-1">
        <div>
          <p
            className="text-base font-black tracking-tight text-slate-900"
            style={{ fontFamily: "var(--font-jetbrains-mono)" }}
          >
            {stationCode}
          </p>
          <p className="text-[11px] text-slate-500 truncate mt-0.5 max-w-[120px]">{name}</p>
        </div>
        <span
          className={`text-[10px] uppercase tracking-wider font-semibold px-2 py-0.5 rounded-full ${
            isActive
              ? "bg-emerald-100 text-emerald-700"
              : "bg-slate-100 text-slate-400"
          }`}
        >
          {isActive ? "● Active" : "○ Idle"}
        </span>
      </div>

      {/* KPI grid */}
      <div className="grid grid-cols-3 gap-2 flex-1">
        {/* OUTPUT */}
        <div className="flex flex-col">
          <span className="zyrex-label">Output</span>
          <p
            className={`text-xl font-bold mt-0.5 ${popped ? "animate-counter-pop text-slate-900" : "text-slate-900"}`}
            style={{ fontFamily: "var(--font-jetbrains-mono)" }}
            data-testid={`station-output-${stationCode}`}
          >
            {displayOutput}
          </p>
        </div>

        {/* NG */}
        <div className="flex flex-col">
          <span className="zyrex-label">NG</span>
          <p
            className={`text-xl font-bold mt-0.5 ${
              ngToday > 0 ? "text-zbright" : "text-slate-400"
            } ${popped ? "animate-counter-pop" : ""}`}
            style={{ fontFamily: "var(--font-jetbrains-mono)" }}
            data-testid={`station-ng-${stationCode}`}
          >
            {displayNg}
          </p>
        </div>

        {/* YIELD */}
        <div className="flex flex-col">
          <span className="zyrex-label">Yield</span>
          <p
            className={`text-xl font-bold mt-0.5 ${
              yieldPercent !== null && yieldPercent >= 95
                ? "text-emerald-600"
                : yieldPercent !== null && yieldPercent >= 85
                ? "text-amber-500"
                : yieldPercent !== null
                ? "text-zbright"
                : "text-slate-300"
            }`}
            style={{ fontFamily: "var(--font-jetbrains-mono)" }}
          >
            {yieldPercent !== null ? `${yieldPercent}%` : "—"}
          </p>
        </div>
      </div>

      {/* Footer */}
      <div className="mt-3 pt-3 border-t border-slate-100 flex items-center justify-between text-[10px] text-slate-400">
        <span>
          {lastEventAtUtc
            ? new Date(lastEventAtUtc).toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit", second: "2-digit" })
            : "—"}
        </span>
        <span className="uppercase tracking-wider">{status}</span>
      </div>
    </Link>
  );
}
"use client";

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
  const strip = status === "active" ? "bg-emerald-500" : "bg-neutral-300 dark:bg-neutral-600";
  return (
    <Link
      href={`/ng-report?stationId=${stationId}`}
      data-testid={`station-tile-${stationCode}`}
      className="flex flex-col rounded-xl border border-black/10 bg-white p-4 shadow-sm transition hover:shadow-md dark:border-white/10 dark:bg-neutral-900"
    >
      <div className={`mb-2 h-1 w-full rounded ${strip}`} data-testid={`station-status-${stationCode}`} />
      <p className="text-2xl font-black tracking-tight">{stationCode}</p>
      <p className="truncate text-xs text-neutral-500">{name}</p>
      <div className="mt-3 grid grid-cols-2 gap-2 text-center">
        <div className="rounded bg-neutral-50 py-2 dark:bg-neutral-800">
          <p className="text-xs text-neutral-500">OUTPUT</p>
          <p className="text-xl font-bold" data-testid={`station-output-${stationCode}`}>
            {outputToday}
          </p>
        </div>
        <div className="rounded bg-neutral-50 py-2 dark:bg-neutral-800">
          <p className="text-xs text-neutral-500">NG</p>
          <p className="text-xl font-bold" data-testid={`station-ng-${stationCode}`}>
            {ngToday}
          </p>
        </div>
      </div>
      <p className="mt-2 text-[11px] text-neutral-400">
        {lastEventAtUtc ? new Date(lastEventAtUtc).toLocaleTimeString("en-GB") : "—"}
      </p>
      <span className="mt-1 text-[11px] uppercase tracking-widest text-neutral-400">{status}</span>
    </Link>
  );
}

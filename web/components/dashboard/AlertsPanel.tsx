"use client";

import { useState } from "react";
import type { AlertDto } from "@/lib/api";

const SEVERITY_CONFIG: Record<string, { bg: string; border: string; text: string; dot: string }> = {
  critical: {
    bg: "bg-red-50",
    border: "border-red-200",
    text: "text-red-700",
    dot: "bg-red-500",
  },
  warning: {
    bg: "bg-amber-50",
    border: "border-amber-200",
    text: "text-amber-700",
    dot: "bg-amber-500",
  },
  info: {
    bg: "bg-blue-50",
    border: "border-blue-200",
    text: "text-blue-700",
    dot: "bg-blue-500",
  },
};

/**
 * Collapsible alerts list (unacknowledged). Presentational — data and ack
 * handling live in useAlerts / the parent page.
 */
export default function AlertsPanel({ alerts, onAck }: { alerts: AlertDto[]; onAck: (id: number) => void }) {
  const [open, setOpen] = useState(true);

  return (
    <aside className="w-full shrink-0 lg:w-80 flex flex-col" data-testid="alerts-panel">
      {/* Header */}
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        className={`mb-3 flex w-full items-center justify-between rounded-lg border px-4 py-3 text-sm font-semibold transition-all ${
          open
            ? "border-slate-200 bg-white shadow-sm"
            : "border-slate-200 bg-slate-50 hover:border-slate-300"
        }`}
        data-testid="alerts-toggle"
        aria-expanded={open}
      >
        <span className="flex items-center gap-2">
          <span className={`h-2 w-2 rounded-full ${alerts.length > 0 ? "bg-zbright animate-pulse-ring" : "bg-slate-300"}`} />
          Alerts
          {alerts.length > 0 && (
            <span className="inline-flex items-center justify-center rounded-full bg-zbright px-2 py-0.5 text-[10px] font-bold text-white min-w-[1.25rem]">
              {alerts.length}
            </span>
          )}
        </span>
        <span aria-hidden className="text-slate-400">{open ? "▾" : "▸"}</span>
      </button>

      {open &&
        (alerts.length === 0 ? (
          <div className="rounded-lg border border-slate-200 bg-slate-50 p-6 text-center" data-testid="alerts-empty">
            <p className="text-emerald-600 text-sm">✓ No active alerts</p>
            <p className="text-slate-400 text-xs mt-1">All stations running within thresholds</p>
          </div>
        ) : (
          <ul className="flex flex-col gap-2" role="list">
            {alerts.map((a, idx) => {
              const cfg = SEVERITY_CONFIG[a.severity] ?? SEVERITY_CONFIG.info;
              return (
                <li
                  key={a.id}
                  className={`rounded-lg border px-4 py-3 text-sm ${cfg.bg} ${cfg.border} animate-fade-in`}
                  style={{ animationDelay: `${idx * 50}ms` }}
                  data-testid="alert-item"
                >
                  <div className="flex items-center justify-between gap-2 mb-1">
                    <span className={`font-semibold uppercase tracking-wide text-xs ${cfg.text}`}>
                      ● {a.severity}
                    </span>
                    {a.lineCode && (
                      <span className="text-[10px] opacity-70 font-mono">{a.lineCode}</span>
                    )}
                  </div>
                  <p className="text-slate-700 leading-snug">{a.message}</p>
                  <div className="mt-2 flex items-center justify-between">
                    <span className="text-[10px] opacity-60 font-mono text-slate-500">
                      {new Date(a.createdAtUtc).toLocaleString("en-GB", {
                        day: "2-digit",
                        month: "short",
                        hour: "2-digit",
                        minute: "2-digit",
                      })}
                    </span>
                    <button
                      type="button"
                      onClick={() => onAck(a.id)}
                      className="rounded-md bg-white border border-slate-200 px-2.5 py-1 text-[10px] font-semibold uppercase tracking-wider text-slate-600 hover:bg-slate-50 hover:border-slate-300 transition-colors"
                      data-testid={`alert-ack-${a.id}`}
                    >
                      Ack
                    </button>
                  </div>
                </li>
              );
            })}
          </ul>
        ))}
    </aside>
  );
}
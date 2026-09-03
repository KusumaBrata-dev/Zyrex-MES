"use client";

import { useState } from "react";
import type { AlertDto } from "@/lib/api";

const SEVERITY_STYLES: Record<string, string> = {
  critical: "border-zbright bg-zbright/10 text-zbright",
  warning: "border-amber-400 bg-amber-400/10 text-amber-500",
  info: "border-sky-400 bg-sky-400/10 text-sky-500",
};

/**
 * Collapsible alerts list (unacknowledged). Presentational — data and ack
 * handling live in useAlerts / the parent page.
 */
export default function AlertsPanel({ alerts, onAck }: { alerts: AlertDto[]; onAck: (id: number) => void }) {
  const [open, setOpen] = useState(true);

  return (
    <aside className="w-full shrink-0 lg:w-80" data-testid="alerts-panel">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        className="mb-2 flex w-full items-center justify-between rounded border border-black/10 px-3 py-2 text-sm font-semibold dark:border-white/10"
        data-testid="alerts-toggle"
      >
        Alerts
        <span aria-hidden>{open ? "▾" : "▸"}</span>
      </button>

      {open &&
        (alerts.length === 0 ? (
          <p className="px-1 text-sm text-neutral-400" data-testid="alerts-empty">
            No active alerts.
          </p>
        ) : (
          <ul className="flex flex-col gap-2" role="list">
            {alerts.map((a) => (
              <li
                key={a.id}
                className={`rounded border px-3 py-2 text-sm ${SEVERITY_STYLES[a.severity] ?? SEVERITY_STYLES.info}`}
                data-testid="alert-item"
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="font-semibold uppercase tracking-wide">{a.severity}</span>
                  {a.lineCode && <span className="text-xs opacity-70">{a.lineCode}</span>}
                </div>
                <p className="mt-1 text-foreground">{a.message}</p>
                <div className="mt-2 flex items-center justify-between">
                  <span className="text-xs opacity-60">{new Date(a.createdAtUtc).toLocaleString()}</span>
                  <button
                    type="button"
                    onClick={() => onAck(a.id)}
                    className="rounded bg-white/10 px-2 py-0.5 text-xs hover:bg-white/20"
                    data-testid={`alert-ack-${a.id}`}
                  >
                    Ack
                  </button>
                </div>
              </li>
            ))}
          </ul>
        ))}
    </aside>
  );
}

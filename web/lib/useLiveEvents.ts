"use client";

import { useEffect, useState } from "react";
import { TOKEN_KEY, type AlertDto } from "@/lib/api";

const API = process.env.NEXT_PUBLIC_API_BASE ?? "";

export type LiveEvent = {
  type: "ScanAccepted" | "ScanRejected" | "QcFailed";
  stationCode: string;
  lineCode: string;
  atUtc: string;
  raw: unknown;
};

function normalizeAlert(payload: unknown): AlertDto | null {
  const p = payload as Record<string, unknown>;
  const id = (p.id as number) ?? (p.Id as number);
  if (typeof id !== "number") return null;
  return {
    id,
    type: (p.type as string) ?? (p.Type as string) ?? "unknown",
    severity: (p.severity as string) ?? (p.Severity as string) ?? "info",
    message: (p.message as string) ?? (p.Message as string) ?? "",
    lineCode: (p.lineCode as string) ?? (p.LineCode as string) ?? null,
    createdAtUtc: (p.createdAtUtc as string) ?? (p.CreatedAtUtc as string) ?? new Date().toISOString(),
    acknowledgedAtUtc: (p.acknowledgedAtUtc as string | null) ?? (p.AcknowledgedAtUtc as string | null) ?? null,
  };
}

/**
 * Single HubConnection to /hubs/production (JWT via accessTokenFactory).
 * Joins each lineCode group, exposes lastEvent, auto-reconnect.
 * `opts.onAlert` receives broadcast AlertRaised events (sent to all clients,
 * no line group required) — pass an empty lineCodes array for alerts-only.
 */
export function useLiveEvents(lineCodes: string[], opts?: { onAlert?: (alert: AlertDto) => void }): LiveEvent | null {
  const [lastEvent, setLastEvent] = useState<LiveEvent | null>(null);
  const key = JSON.stringify([...lineCodes].sort());
  const onAlert = opts?.onAlert;

  useEffect(() => {
    if (typeof window === "undefined") return;
    const codes = JSON.parse(key) as string[];

    let cancelled = false;
    let conn: {
      on: (e: string, cb: (p: unknown) => void) => void;
      off: (e: string) => void;
      start: () => Promise<void>;
      stop: () => Promise<void>;
      invoke: (m: string, ...a: unknown[]) => Promise<void>;
    } | null = null;

    async function connect() {
      try {
        const { HubConnectionBuilder } = await import("@microsoft/signalr");
        const c = new HubConnectionBuilder()
          .withUrl(`${API}/hubs/production`, {
            accessTokenFactory: () => sessionStorage.getItem(TOKEN_KEY) ?? "",
          })
          .withAutomaticReconnect()
          .build() as unknown as typeof conn & { state: string };
        conn = c;

        const emit = (type: LiveEvent["type"]) => (payload: unknown) => {
          const p = payload as Record<string, unknown>;
          const stationCode = (p.stationCode as string) ?? (p.StationCode as string) ?? "";
          const lineCode = (p.lineCode as string) ?? (p.LineCode as string) ?? "";
          const atUtc = (p.atUtc as string) ?? (p.AtUtc as string) ?? new Date().toISOString();
          if (!stationCode) return;
          setLastEvent({ type, stationCode, lineCode, atUtc, raw: payload });
        };

        c.on("ScanAccepted", emit("ScanAccepted"));
        c.on("ScanRejected", emit("ScanRejected"));
        c.on("QcFailed", emit("QcFailed"));
        c.on("AlertRaised", (payload: unknown) => {
          const alert = normalizeAlert(payload);
          if (alert) onAlert?.(alert);
        });

        await c.start();
        if (cancelled) {
          await c.stop();
          return;
        }
        for (const lc of codes) {
          try {
            await c.invoke("JoinLine", lc);
          } catch {
            // ignore
          }
        }
      } catch {
        // fallback polling covers it
      }
    }

    void connect();

    return () => {
      cancelled = true;
      if (conn) {
        try {
          conn.off("ScanAccepted");
          conn.off("ScanRejected");
          conn.off("QcFailed");
          conn.off("AlertRaised");
          void conn.stop();
        } catch {
          // ignore
        }
      }
    };
  }, [key, onAlert]);

  return lastEvent;
}

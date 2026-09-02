"use client";

import { useEffect, useState } from "react";
import { TOKEN_KEY } from "@/lib/api";

const API = process.env.NEXT_PUBLIC_API_BASE ?? "";

export type LiveEvent = {
  type: "ScanAccepted" | "ScanRejected" | "QcFailed";
  stationCode: string;
  lineCode: string;
  atUtc: string;
  raw: unknown;
};

/**
 * Single HubConnection to /hubs/production (JWT via accessTokenFactory).
 * Joins each lineCode group, exposes lastEvent, auto-reconnect.
 */
export function useLiveEvents(lineCodes: string[]): LiveEvent | null {
  const [lastEvent, setLastEvent] = useState<LiveEvent | null>(null);
  const key = JSON.stringify([...lineCodes].sort());

  useEffect(() => {
    if (typeof window === "undefined") return;
    const codes = JSON.parse(key) as string[];
    if (codes.length === 0) return;

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
          void conn.stop();
        } catch {
          // ignore
        }
      }
    };
  }, [key]);

  return lastEvent;
}

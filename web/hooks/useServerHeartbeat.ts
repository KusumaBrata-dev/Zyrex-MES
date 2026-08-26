"use client";

import { useEffect, useState } from "react";

const API = process.env.NEXT_PUBLIC_API_BASE ?? "";

export const HEARTBEAT_INTERVAL_MS = 5_000;
export const HEARTBEAT_TIMEOUT_MS = 3_000;
/** Consecutive failed pings before the kiosk is considered offline (anti-flap). */
const FAILURES_TO_OFFLINE = 2;

/**
 * Polls the MES API /health endpoint every 5 s (3 s abort timeout per ping).
 * The kiosk is "offline" only after two consecutive failures so a single lost
 * packet does not flash the blocking overlay; any success clears it.
 */
export function useServerHeartbeat(): { offline: boolean } {
  const [offline, setOffline] = useState(false);

  useEffect(() => {
    let consecutiveFailures = 0;

    async function ping() {
      const abort = new AbortController();
      const timeout = window.setTimeout(() => abort.abort(), HEARTBEAT_TIMEOUT_MS);
      try {
        const res = await fetch(`${API}/health`, { signal: abort.signal });
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        consecutiveFailures = 0;
        setOffline(false);
      } catch {
        consecutiveFailures += 1;
        if (consecutiveFailures >= FAILURES_TO_OFFLINE) setOffline(true);
      } finally {
        window.clearTimeout(timeout);
      }
    }

    void ping();
    const timer = window.setInterval(() => void ping(), HEARTBEAT_INTERVAL_MS);
    return () => window.clearInterval(timer);
  }, []);

  return { offline };
}

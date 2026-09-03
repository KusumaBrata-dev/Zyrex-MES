"use client";

import { useCallback, useEffect, useState } from "react";
import { ackAlert, getAlerts, type AlertDto } from "@/lib/api";

const REFRESH_MS = 60_000;

/**
 * Unacknowledged alerts feed: initial fetch + 60s polling.
 * `push` prepends a realtime (hub) alert; `ack` calls the API and
 * removes the item locally.
 */
export function useAlerts(refreshMs = REFRESH_MS) {
  const [alerts, setAlerts] = useState<AlertDto[]>([]);

  const refresh = useCallback(async () => {
    try {
      setAlerts(await getAlerts(true));
    } catch {
      // keep last known state; next tick retries
    }
  }, []);

  useEffect(() => {
    void refresh();
    const t = window.setInterval(() => void refresh(), refreshMs);
    return () => window.clearInterval(t);
  }, [refresh, refreshMs]);

  const push = useCallback((alert: AlertDto) => {
    setAlerts((prev) => [alert, ...prev.filter((a) => a.id !== alert.id)]);
  }, []);

  const ack = useCallback(async (id: number) => {
    await ackAlert(id);
    setAlerts((prev) => prev.filter((a) => a.id !== id));
  }, []);

  return { alerts, push, ack };
}

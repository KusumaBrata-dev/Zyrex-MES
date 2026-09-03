"use client";

import { usePathname } from "next/navigation";
import { useServerHeartbeat } from "@/hooks/useServerHeartbeat";
import OfflineOverlay from "@/components/OfflineOverlay";

/**
 * Client boundary mounted once in the root layout: blocks the whole kiosk UI
 * with the SERVER OFFLINE overlay whenever the MES API is unreachable.
 * Dashboard is excluded — it remains usable with cached/polling data.
 */
export default function OfflineGate() {
  const { offline } = useServerHeartbeat();
  const pathname = usePathname();
  if (pathname?.startsWith("/dashboard")) return null;
  return offline ? <OfflineOverlay /> : null;
}

"use client";

import { useServerHeartbeat } from "@/hooks/useServerHeartbeat";
import OfflineOverlay from "@/components/OfflineOverlay";

/**
 * Client boundary mounted once in the root layout: blocks the whole kiosk UI
 * with the SERVER OFFLINE overlay whenever the MES API is unreachable.
 */
export default function OfflineGate() {
  const { offline } = useServerHeartbeat();
  return offline ? <OfflineOverlay /> : null;
}

"use client";

import { useEffect, useRef, type MouseEvent } from "react";
import { playNg, playOk } from "@/lib/sound";

export interface ResultOverlayProps {
  kind: "PASS" | "REJECTED";
  sn?: string;
  reason?: string;
  nextStationCode?: string;
  /** Called when the overlay should close (auto after 1.5 s for PASS, on tap for REJECTED). */
  onDone: () => void;
}

const PASS_AUTO_DISMISS_MS = 1500;

/**
 * Fullscreen scan result feedback. PASS: dark green, giant serial number,
 * auto-dismisses after 1.5 s. REJECTED: full Zyrex red with the reason,
 * stays until the operator taps anywhere.
 */
export default function ResultOverlay({ kind, sn, reason, nextStationCode, onDone }: ResultOverlayProps) {
  // Keep the latest callback in a ref so parent re-renders (new inline arrow
  // each time) never reset the auto-dismiss timer.
  const onDoneRef = useRef(onDone);
  useEffect(() => {
    onDoneRef.current = onDone;
  });

  useEffect(() => {
    if (kind === "PASS") playOk();
    else playNg();
  }, [kind]);

  useEffect(() => {
    if (kind !== "PASS") return;
    const timer = window.setTimeout(() => onDoneRef.current(), PASS_AUTO_DISMISS_MS);
    return () => window.clearTimeout(timer);
  }, [kind]);

  function handleTap(e: MouseEvent) {
    e.preventDefault();
    if (kind === "REJECTED") onDoneRef.current();
  }

  return (
    <div
      data-testid="result-overlay"
      onClick={handleTap}
      className={`fixed inset-0 z-50 flex flex-col items-center justify-center gap-6 text-white ${
        kind === "PASS" ? "bg-green-800" : "bg-zred"
      }`}
    >
      <p className="text-8xl font-black tracking-widest">{kind}</p>
      {sn && <p className="text-6xl font-bold break-all px-8 text-center">{sn}</p>}
      {kind === "PASS" && nextStationCode && (
        <p className="text-3xl">Next station: {nextStationCode}</p>
      )}
      {kind === "REJECTED" && (
        <>
          <p className="max-w-4xl px-8 text-center text-5xl font-bold">{reason ?? "Rejected"}</p>
          <p className="text-xl opacity-80">Tap anywhere to continue</p>
        </>
      )}
    </div>
  );
}

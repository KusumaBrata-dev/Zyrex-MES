"use client";

import { Suspense, useCallback, useEffect, useState, type FormEvent } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import Link from "next/link";
import DailySummary from "@/components/DailySummary";
import ResultOverlay from "@/components/ResultOverlay";
import ScanInput from "@/components/ScanInput";
import { isLoggedIn, scan } from "@/lib/api";

const STATION_KEY = "kiosk_station_id";

type Phase =
  | { state: "idle" }
  | { state: "submitting" }
  | { state: "pass"; sn: string; nextStationCode?: string }
  | { state: "rejected"; sn: string; reason: string };

function readStationId(): number | null {
  const raw = window.localStorage.getItem(STATION_KEY);
  const id = raw ? Number.parseInt(raw, 10) : Number.NaN;
  return Number.isInteger(id) && id > 0 ? id : null;
}

export default function ScanPage() {
  // useSearchParams requires a Suspense boundary for static prerendering.
  return (
    <Suspense fallback={<p className="p-6 text-slate-400">Loading...</p>}>
      <ScanScreen />
    </Suspense>
  );
}

function ScanScreen() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const [stationId, setStationId] = useState<number | null>(null);
  const [stationInput, setStationInput] = useState("");
  const [phase, setPhase] = useState<Phase>({ state: "idle" });
  const [toast, setToast] = useState<string | null>(null);
  const [summaryRefreshKey, setSummaryRefreshKey] = useState(0);

  useEffect(() => {
    if (!isLoggedIn()) {
      router.replace("/login");
      return;
    }
    // Kiosk URL pattern: /scan?stationId=5 (persisted to localStorage).
    const fromUrl = Number.parseInt(searchParams.get("stationId") ?? "", 10);
    if (Number.isInteger(fromUrl) && fromUrl > 0) {
      window.localStorage.setItem(STATION_KEY, String(fromUrl));
      setStationId(fromUrl);
      return;
    }
    setStationId(readStationId());
  }, [router, searchParams]);

  const onScan = useCallback(
    async (sn: string) => {
      if (stationId === null) return;
      setPhase({ state: "submitting" });
      try {
        const result = await scan(sn, stationId);
        if (result.result === "PASS") {
          setPhase({ state: "pass", sn, nextStationCode: result.nextStationCode });
          setSummaryRefreshKey((k) => k + 1);
        } else {
          setPhase({ state: "rejected", sn, reason: result.reason });
        }
      } catch (err) {
        setPhase({ state: "idle" });
        setToast(err instanceof Error ? err.message : "scan failed");
        window.setTimeout(() => setToast(null), 4000);
      }
    },
    [stationId],
  );

  function saveStation(e: FormEvent) {
    e.preventDefault();
    const id = Number.parseInt(stationInput, 10);
    if (!Number.isInteger(id) || id <= 0) return;
    window.localStorage.setItem(STATION_KEY, String(id));
    setStationId(id);
  }

  if (stationId === null) {
    return (
      <div className="flex flex-1 items-center justify-center p-6">
        <form
          onSubmit={saveStation}
          className="w-full max-w-xs zyrex-card p-6 animate-fade-in"
        >
          <h1 className="mb-4 text-center text-lg font-bold text-slate-900">Set Station ID</h1>
          <input
            autoFocus
            inputMode="numeric"
            pattern="\d+"
            required
            value={stationInput}
            onChange={(e) => setStationInput(e.target.value)}
            className="zyrex-input mb-4 text-center text-2xl font-bold"
            aria-label="Station ID"
          />
          <button type="submit" className="zyrex-btn-primary w-full">
            Save
          </button>
          <p className="mt-3 text-center text-xs text-slate-400">or open /scan?stationId=&lt;id&gt;</p>
        </form>
      </div>
    );
  }

  return (
    <div className="flex flex-1 flex-col gap-6 p-6 animate-fade-in" data-testid="scan-page">
      {/* Header */}
      <header className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-black text-slate-900">Production Scan</h1>
          <p className="text-xs text-slate-400 mt-0.5">Station #{stationId} · Real-time Monitoring</p>
        </div>
        <Link href="/ng-report" className="zyrex-btn-secondary">
          NG Report
        </Link>
      </header>

      {/* Daily Summary */}
      <DailySummary stationId={stationId} refreshSignal={summaryRefreshKey} />

      {/* Scan Input */}
      <div className="flex flex-1 items-center justify-center">
        <div className="w-full max-w-2xl">
          <ScanInput onSubmit={onScan} disabled={phase.state === "submitting"} />
        </div>
      </div>

      {/* Toast */}
      {toast && (
        <div
          role="status"
          className="fixed bottom-6 left-1/2 -translate-x-1/2 rounded-lg bg-slate-900 px-4 py-2 text-sm text-white shadow-lg"
        >
          {toast}
        </div>
      )}

      {/* Result Overlay */}
      {(phase.state === "pass" || phase.state === "rejected") && (
        <ResultOverlay
          kind={phase.state === "pass" ? "PASS" : "REJECTED"}
          sn={phase.sn}
          reason={phase.state === "rejected" ? phase.reason : undefined}
          nextStationCode={phase.state === "pass" ? phase.nextStationCode : undefined}
          onDone={() => setPhase({ state: "idle" })}
        />
      )}
    </div>
  );
}
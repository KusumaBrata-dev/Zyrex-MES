"use client";

/**
 * Today's counters for the configured station (output / NG / yield).
 * Stub in this task: renders placeholders and reacts to `refreshKey` changes;
 * live data wiring via api.getStationSummary lands in the next task.
 */
export default function DailySummary({ stationId, refreshKey = 0 }: { stationId: number; refreshKey?: number }) {
  void refreshKey; // data fetch arrives with Task 8
  return (
    <section className="grid grid-cols-3 gap-4">
      <Stat label="Output" value="—" />
      <Stat label="NG" value="—" />
      <Stat label="Yield" value="—" />
      <p className="col-span-3 text-center text-xs text-neutral-400">
        station {stationId} · summary wiring pending
      </p>
    </section>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border border-black/10 bg-white p-4 text-center dark:border-white/10 dark:bg-neutral-900">
      <p className="text-sm font-medium text-neutral-500">{label}</p>
      <p className="text-4xl font-black">{value}</p>
    </div>
  );
}

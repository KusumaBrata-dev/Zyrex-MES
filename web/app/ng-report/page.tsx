"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { getNgList, type NgListResponse } from "@/lib/api";
import { wibToday } from "@/components/DailySummary";

const PAGE_SIZE = 25;

/**
 * NG report for the factory floor: filterable by date (WIB), paged 25 rows.
 * Data source: GET /api/reports/ng-list.
 */
export default function NgReportPage() {
  const [date, setDate] = useState(wibToday());
  const [page, setPage] = useState(1);
  const [data, setData] = useState<NgListResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const res = await getNgList({ date, page, pageSize: PAGE_SIZE });
      setData(res);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "failed to load NG list");
    }
  }, [date, page]);

  useEffect(() => {
    void load();
  }, [load]);

  const totalPages = data ? Math.max(1, Math.ceil(data.total / PAGE_SIZE)) : 1;

  return (
    <div className="flex flex-1 flex-col gap-4 p-6">
      <div className="flex items-center gap-4">
        <h1 className="text-xl font-semibold">NG Report</h1>
        <label className="flex items-center gap-2 text-sm">
          Date
          <input
            type="date"
            value={date}
            onChange={(e) => {
              setDate(e.target.value);
              setPage(1);
            }}
            className="rounded border border-black/20 px-2 py-1 dark:border-white/20 dark:bg-neutral-800"
          />
        </label>
        <Link href="/scan" className="ml-auto text-sm text-zbright underline">
          ← Back to scan
        </Link>
      </div>

      {error && (
        <p role="alert" className="text-sm font-medium text-zbright">
          {error}
        </p>
      )}

      <table className="w-full border-collapse text-left text-sm">
        <thead>
          <tr className="border-b border-black/20 text-neutral-500 dark:border-white/20">
            <th className="py-2 pr-4">SN</th>
            <th className="py-2 pr-4">Station</th>
            <th className="py-2 pr-4">NG Code</th>
            <th className="py-2 pr-4">Notes</th>
            <th className="py-2">Time</th>
          </tr>
        </thead>
        <tbody>
          {data?.items.map((item, i) => (
            /* eslint-disable-next-line react/no-array-index-key -- rows have no stable id in the payload */
            <tr key={`${item.sn}-${item.checkedAtUtc}-${i}`} className="border-b border-black/5 dark:border-white/10">
              <td className="py-2 pr-4 font-mono">{item.sn}</td>
              <td className="py-2 pr-4">{item.stationCode}</td>
              <td className="py-2 pr-4">{item.ngCode ?? "—"}</td>
              <td className="py-2 pr-4">{item.notes ?? "—"}</td>
              <td className="py-2">{new Date(item.checkedAtUtc).toLocaleTimeString("en-GB")}</td>
            </tr>
          ))}
          {data && data.items.length === 0 && (
            <tr>
              <td colSpan={5} className="py-6 text-center text-neutral-400">
                No NG records for this date.
              </td>
            </tr>
          )}
        </tbody>
      </table>

      <div className="mt-auto flex items-center justify-between text-sm">
        <span className="text-neutral-500">
          {data ? `${data.total} record(s) · page ${data.page} of ${totalPages}` : ""}
        </span>
        <div className="flex gap-2">
          <button
            type="button"
            disabled={page <= 1}
            onClick={() => setPage((p) => Math.max(1, p - 1))}
            className="rounded border border-black/20 px-3 py-1 disabled:opacity-40 dark:border-white/20"
          >
            ← Prev
          </button>
          <button
            type="button"
            disabled={!data || page >= totalPages}
            onClick={() => setPage((p) => p + 1)}
            className="rounded border border-black/20 px-3 py-1 disabled:opacity-40 dark:border-white/20"
          >
            Next →
          </button>
        </div>
      </div>
    </div>
  );
}

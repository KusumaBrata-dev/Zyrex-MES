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
    <div className="flex flex-1 flex-col gap-4 p-6 animate-fade-in" data-testid="ng-report-page">
      {/* Header */}
      <header className="flex items-center gap-4">
        <h1 className="text-xl font-black text-slate-900">NG Report</h1>
        <label className="flex items-center gap-2 text-sm text-slate-600">
          <span className="text-xs uppercase tracking-wider text-slate-400">Date</span>
          <input
            type="date"
            value={date}
            onChange={(e) => {
              setDate(e.target.value);
              setPage(1);
            }}
            className="rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-700 focus:border-zred focus:outline-none focus:ring-1 focus:ring-zred/20 transition-colors"
          />
        </label>
        <Link href="/scan" className="ml-auto zyrex-btn-secondary">
          ← Back to Scan
        </Link>
      </header>

      {error && (
        <div role="alert" className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          ⚠ {error}
        </div>
      )}

      {/* Table */}
      <div className="zyrex-card overflow-hidden">
        <table className="w-full border-collapse text-left text-sm">
          <thead>
            <tr className="border-b border-slate-200 bg-slate-50">
              <th className="py-3 px-4 text-xs font-semibold uppercase tracking-wider text-slate-500">SN</th>
              <th className="py-3 px-4 text-xs font-semibold uppercase tracking-wider text-slate-500">Station</th>
              <th className="py-3 px-4 text-xs font-semibold uppercase tracking-wider text-slate-500">NG Code</th>
              <th className="py-3 px-4 text-xs font-semibold uppercase tracking-wider text-slate-500">Notes</th>
              <th className="py-3 px-4 text-xs font-semibold uppercase tracking-wider text-slate-500">Time</th>
            </tr>
          </thead>
          <tbody>
            {data?.items.map((item, i) => (
              /* eslint-disable-next-line react/no-array-index-key -- rows have no stable id in the payload */
              <tr key={`${item.sn}-${item.checkedAtUtc}-${i}`} className="border-b border-slate-100 hover:bg-slate-50 transition-colors">
                <td className="py-3 px-4 font-mono text-slate-700">{item.sn}</td>
                <td className="py-3 px-4 text-slate-600">{item.stationCode}</td>
                <td className="py-3 px-4">
                  <span className="inline-flex items-center rounded-full bg-red-100 px-2 py-0.5 text-xs font-semibold text-red-700">
                    {item.ngCode ?? "—"}
                  </span>
                </td>
                <td className="py-3 px-4 text-slate-500">{item.notes ?? "—"}</td>
                <td className="py-3 px-4 font-mono text-slate-400 text-xs">
                  {new Date(item.checkedAtUtc).toLocaleTimeString("en-GB")}
                </td>
              </tr>
            ))}
            {data && data.items.length === 0 && (
              <tr>
                <td colSpan={5} className="py-12 text-center text-slate-400">
                  No NG records for this date.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {/* Pagination */}
      <div className="mt-auto flex items-center justify-between text-sm text-slate-600">
        <span className="text-slate-500">
          {data ? `${data.total} record(s) · page ${data.page} of ${totalPages}` : ""}
        </span>
        <div className="flex gap-2">
          <button
            type="button"
            disabled={page <= 1}
            onClick={() => setPage((p) => Math.max(1, p - 1))}
            className="zyrex-btn-secondary disabled:opacity-40 disabled:cursor-not-allowed"
          >
            ← Prev
          </button>
          <button
            type="button"
            disabled={!data || page >= totalPages}
            onClick={() => setPage((p) => p + 1)}
            className="zyrex-btn-secondary disabled:opacity-40 disabled:cursor-not-allowed"
          >
            Next →
          </button>
        </div>
      </div>
    </div>
  );
}
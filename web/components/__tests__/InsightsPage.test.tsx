import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import InsightsPage from "@/app/dashboard/insights/page";

const trendPayload = {
  points: [
    { date: "2026-08-18", output: 100, ng: 5, yieldPercent: 95.0 },
    { date: "2026-08-19", output: 80, ng: 8, yieldPercent: 90.0 },
  ],
};
const paretoPayload = {
  items: [
    { ngCode: "SCRATCH", count: 10 },
    { ngCode: "DENT", count: 5 },
  ],
};

describe("Insights page", () => {
  beforeEach(() => {
    sessionStorage.setItem("kiosk_token", "tok");
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: string) => {
        const u = String(url);
        if (u.includes("/api/insights/yield-trend")) {
          return { ok: true, json: async () => trendPayload, status: 200 } as unknown as Response;
        }
        if (u.includes("/api/insights/ng-pareto")) {
          return { ok: true, json: async () => paretoPayload, status: 200 } as unknown as Response;
        }
        return { ok: false, status: 404, json: async () => ({}) } as unknown as Response;
      }) as unknown as typeof fetch,
    );
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    sessionStorage.clear();
  });

  it("renders both charts after loading", async () => {
    render(<InsightsPage />);
    await waitFor(() => expect(screen.getByTestId("yield-trend-chart")).toBeInTheDocument());
    expect(screen.getByTestId("ng-pareto-chart")).toBeInTheDocument();
    expect(screen.getAllByTestId("pareto-bar")).toHaveLength(2);
  });

  it("has export links for ng-list and station-summary xlsx", async () => {
    render(<InsightsPage />);
    await waitFor(() => expect(screen.getByTestId("yield-trend-chart")).toBeInTheDocument());
    expect(screen.getByTestId("export-ng-list").getAttribute("href")).toContain("/api/export/ng-list.xlsx");
    expect(screen.getByTestId("export-station-summary").getAttribute("href")).toContain("/api/export/station-summary.xlsx");
  });

  it("print button triggers window.print", async () => {
    const printSpy = vi.fn();
    window.print = printSpy;
    render(<InsightsPage />);
    await waitFor(() => expect(screen.getByTestId("yield-trend-chart")).toBeInTheDocument());
    fireEvent.click(screen.getByTestId("print-button"));
    expect(printSpy).toHaveBeenCalledTimes(1);
  });
});

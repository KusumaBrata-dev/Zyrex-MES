import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const mockUseLiveEvents = vi.fn((_codes: string[]) => null);
vi.mock("@/lib/useLiveEvents", () => ({
  useLiveEvents: (codes: string[]) => (mockUseLiveEvents as unknown as (c: string[]) => unknown)(codes),
}));

vi.mock("next/link", () => ({
  default: ({ href, children, ...props }: { href: string; children: React.ReactNode }) => (
    <a href={href} {...props}>
      {children}
    </a>
  ),
}));

import TvStats from "@/components/dashboard/TvStats";

const gridPayload = {
  lines: [
    {
      lineCode: "L01",
      stations: [
        { stationId: 1, stationCode: "ST-A", name: "A", outputToday: 12, ngToday: 1, lastEventAtUtc: "2026-08-24T00:00:00Z", status: "idle" },
        { stationId: 2, stationCode: "ST-B", name: "B", outputToday: 5, ngToday: 0, lastEventAtUtc: null, status: "idle" },
      ],
    },
    {
      lineCode: "L02",
      stations: [
        { stationId: 3, stationCode: "ST-C", name: "C", outputToday: 7, ngToday: 2, lastEventAtUtc: null, status: "idle" },
      ],
    },
  ],
};

describe("TvStats", () => {
  beforeEach(() => {
    mockUseLiveEvents.mockReturnValue(null);
    sessionStorage.setItem("kiosk_token", "tok");
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => ({
        ok: true,
        json: async () => gridPayload,
        status: 200,
      })) as unknown as typeof fetch,
    );
  });
  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
    sessionStorage.clear();
    vi.clearAllMocks();
  });

  it("shows the line code and aggregated totals for that line only", async () => {
    render(<TvStats lineCode="L01" />);
    await waitFor(() => expect(screen.getByTestId("tv-page")).toBeInTheDocument());
    expect(screen.getByTestId("tv-line-code")).toHaveTextContent("L01");
    // L01 totals: output 12+5=17, ng 1 → yield (16/17)*100 = 94.1%
    expect(screen.getByTestId("tv-output")).toHaveTextContent("17");
    expect(screen.getByTestId("tv-ng")).toHaveTextContent("1");
    // yield shows ring with percentage
    expect(screen.getByTestId("tv-yield")).toBeInTheDocument();
  });

  it("shows a dash for yield when the line has no output yet", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => ({
        ok: true,
        json: async () => ({
          lines: [{ lineCode: "L03", stations: [{ stationId: 9, stationCode: "ST-Z", name: "Z", outputToday: 0, ngToday: 0, lastEventAtUtc: null, status: "idle" }] }],
        }),
        status: 200,
      })) as unknown as typeof fetch,
    );
    render(<TvStats lineCode="L03" />);
    await waitFor(() => expect(screen.getByTestId("tv-yield")).toBeInTheDocument());
    expect(screen.getByTestId("tv-yield").textContent).toContain("—");
  });

  it("fetches data on mount", async () => {
    const fetchMock = vi.mocked(fetch);
    render(<TvStats lineCode="L01" />);
    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const gridCalls = fetchMock.mock.calls.filter((c) => String(c[0]).includes("/api/reports/line-grid"));
    expect(gridCalls.length).toBeGreaterThanOrEqual(1);
  });

  it("has an exit button that calls history.back", async () => {
    const backMock = vi.fn();
    Object.defineProperty(window, "history", {
      value: { back: backMock },
      writable: true,
    });
    render(<TvStats lineCode="L01" />);
    await waitFor(() => expect(screen.getByTestId("tv-exit")).toBeInTheDocument());
    await act(async () => {
      screen.getByTestId("tv-exit").click();
    });
    expect(backMock).toHaveBeenCalled();
  });
});
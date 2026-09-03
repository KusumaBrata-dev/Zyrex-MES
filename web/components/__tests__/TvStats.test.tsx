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
    expect(screen.getByTestId("tv-yield")).toHaveTextContent("94.1%");
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
    await waitFor(() => expect(screen.getByTestId("tv-yield")).toHaveTextContent("—"));
  });

  it("refreshes line-grid every 10 seconds", async () => {
    vi.useFakeTimers();
    try {
      const fetchMock = vi.mocked(fetch);
      render(<TvStats lineCode="L01" />);
      await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
      await act(async () => {
        await vi.advanceTimersByTimeAsync(10_000);
      });
      await vi.waitFor(() => expect(fetchMock.mock.calls.length).toBeGreaterThanOrEqual(2));
      expect(fetchMock.mock.calls.every((c) => String(c[0]).includes("/api/reports/line-grid"))).toBe(true);
    } finally {
      vi.useRealTimers();
    }
  });

  it("lists recent live events, newest first, capped at 5", async () => {
    const { rerender } = render(<TvStats lineCode="L01" />);
    await waitFor(() => expect(screen.getByTestId("tv-page")).toBeInTheDocument());
    expect(screen.getByTestId("tv-events-empty")).toBeInTheDocument();

    for (let i = 1; i <= 6; i++) {
      mockUseLiveEvents.mockReturnValue({
        type: i % 2 === 0 ? "QcFailed" : "ScanAccepted",
        stationCode: `ST-${i}`,
        lineCode: "L01",
        atUtc: `2026-08-24T02:00:0${i}Z`,
        raw: {},
      } as unknown as ReturnType<typeof mockUseLiveEvents>);
      // act per iteration so each event's effect flushes deterministically
      // eslint-disable-next-line no-await-in-loop
      await act(async () => {
        rerender(<TvStats lineCode="L01" />);
      });
    }

    await waitFor(() => expect(screen.getAllByTestId("tv-event-item")).toHaveLength(5));
    const items = screen.getAllByTestId("tv-event-item");
    expect(items[0]).toHaveTextContent("ST-6");
    expect(items[0]).toHaveTextContent("QcFailed");
    expect(items[4]).toHaveTextContent("ST-2");
  });

  it("optimistically increments output on ScanAccepted for a station in this line", async () => {
    const { rerender } = render(<TvStats lineCode="L01" />);
    await waitFor(() => expect(screen.getByTestId("tv-output")).toHaveTextContent("17"));

    mockUseLiveEvents.mockReturnValue({
      type: "ScanAccepted",
      stationCode: "ST-A",
      lineCode: "L01",
      atUtc: "2026-08-24T02:00:00Z",
      raw: {},
    } as unknown as ReturnType<typeof mockUseLiveEvents>);
    rerender(<TvStats lineCode="L01" />);

    await waitFor(() => expect(screen.getByTestId("tv-output")).toHaveTextContent("18"));
  });

  it("has an exit link back to the dashboard", async () => {
    render(<TvStats lineCode="L01" />);
    await waitFor(() => expect(screen.getByTestId("tv-page")).toBeInTheDocument());
    expect(screen.getByTestId("tv-exit")).toHaveAttribute("href", "/dashboard");
  });

  it("shows a not-found state for an unknown line", async () => {
    render(<TvStats lineCode="NOPE" />);
    await waitFor(() => expect(screen.getByTestId("tv-not-found")).toBeInTheDocument());
  });
});

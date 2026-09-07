import { cleanup, render, screen, waitFor } from "@testing-library/react";
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

vi.mock("@microsoft/signalr", () => ({
  HubConnectionBuilder: class {
    withUrl() { return this; }
    withAutomaticReconnect() { return this; }
    build() {
      return { on: vi.fn(), off: vi.fn(), start: vi.fn().mockResolvedValue(undefined), stop: vi.fn().mockResolvedValue(undefined), invoke: vi.fn().mockResolvedValue(undefined) };
    }
  },
  HttpTransportType: { LongPolling: 1 },
}));

import GridBoard from "@/components/dashboard/GridBoard";

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

describe("GridBoard", () => {
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

  it("renders stations grouped by lineCode", async () => {
    render(<GridBoard filterLine="All" />);
    await waitFor(() => expect(screen.getByTestId("grid-board")).toBeInTheDocument());
    expect(screen.getByTestId("line-section-L01")).toBeInTheDocument();
    expect(screen.getByTestId("line-section-L02")).toBeInTheDocument();
    expect(screen.getByTestId("station-tile-ST-A")).toBeInTheDocument();
    expect(screen.getByTestId("station-output-ST-A")).toHaveTextContent("12");
  });

  it("increments output on ScanAccepted for matching station", async () => {
    const { rerender } = render(<GridBoard filterLine="All" />);
    await waitFor(() => expect(screen.getByTestId("station-output-ST-A")).toHaveTextContent("12"));

    mockUseLiveEvents.mockReturnValue({
      type: "ScanAccepted",
      stationCode: "ST-A",
      lineCode: "L01",
      atUtc: "2026-08-24T02:00:00Z",
      raw: {},
    } as unknown as ReturnType<typeof mockUseLiveEvents>);
    rerender(<GridBoard filterLine="All" />);

    await waitFor(() => expect(screen.getByTestId("station-output-ST-A")).toHaveTextContent("13"));
    expect(screen.getByTestId("station-status-ST-A").className).toContain("bg-zyrex-success");
  });

  it("filters by line selector", async () => {
    render(<GridBoard filterLine="L02" />);
    await waitFor(() => expect(screen.getByTestId("grid-board")).toBeInTheDocument());
    expect(screen.queryByTestId("line-section-L01")).not.toBeInTheDocument();
    expect(screen.getByTestId("line-section-L02")).toBeInTheDocument();
  });

  it("fetches line-grid on mount (fallback polling configured)", async () => {
    const fetchMock = vi.mocked(fetch);
    render(<GridBoard filterLine="All" />);
    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const calledWithGrid = fetchMock.mock.calls.some((c) => String(c[0]).includes("/api/reports/line-grid"));
    expect(calledWithGrid).toBe(true);
  });
});

import { act, render, screen, waitFor, fireEvent } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import DashboardPage from "@/app/dashboard/page";
import type { AlertDto } from "@/lib/api";

vi.mock("next/link", () => ({
  default: ({ href, children, ...props }: { href: string; children: React.ReactNode }) => (
    <a href={href} {...props}>
      {children}
    </a>
  ),
}));

// capture the onAlert option so tests can simulate AlertRaised hub events
type AlertHandler = (a: AlertDto) => void;
const alertHandlers: AlertHandler[] = [];
vi.mock("@/lib/useLiveEvents", () => ({
  useLiveEvents: (_codes: string[], opts?: { onAlert?: AlertHandler }) => {
    if (opts?.onAlert) alertHandlers.push(opts.onAlert);
    return null;
  },
}));

const alertsPayload: AlertDto[] = [
  {
    id: 1,
    type: "low_yield",
    severity: "warning",
    message: "Low yield on L01",
    lineCode: "L01",
    createdAtUtc: "2026-08-24T01:00:00Z",
    acknowledgedAtUtc: null,
  },
  {
    id: 2,
    type: "ng_spike",
    severity: "critical",
    message: "NG spike on L02",
    lineCode: "L02",
    createdAtUtc: "2026-08-24T02:00:00Z",
    acknowledgedAtUtc: null,
  },
];

const gridPayload = {
  lines: [
    {
      lineCode: "L01",
      stations: [{ stationId: 1, stationCode: "ST-A", name: "A", outputToday: 1, ngToday: 0, lastEventAtUtc: null, status: "idle" }],
    },
    {
      lineCode: "L02",
      stations: [{ stationId: 2, stationCode: "ST-B", name: "B", outputToday: 2, ngToday: 0, lastEventAtUtc: null, status: "idle" }],
    },
  ],
};

describe("Dashboard page", () => {
  beforeEach(() => {
    sessionStorage.setItem("kiosk_token", "tok");
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: string) => {
        if (typeof url === "string" && url.includes("/api/insights/thresholds")) {
          return {
            ok: true,
            json: async () => ({ minYieldPercent: 95, yieldDropPercent: 5, ngSpikePerHour: 10, evaluationIntervalMinutes: 30 }),
            status: 200,
          } as unknown as Response;
        }
        if (typeof url === "string" && url.includes("/api/alerts")) {
          return {
            ok: true,
            json: async () => alertsPayload,
            status: 200,
          } as unknown as Response;
        }
        return {
          ok: true,
          json: async () => gridPayload,
          status: 200,
        } as unknown as Response;
      }) as unknown as typeof fetch,
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    sessionStorage.clear();
    alertHandlers.length = 0;
  });

  it("shows header, TV link and thresholds badge", async () => {
    render(<DashboardPage />);
    expect(screen.getByText("Dashboard")).toBeInTheDocument();
    // TV mode targets the first line until the selector picks another one
    await waitFor(() => expect(screen.getByTestId("tv-link")).toHaveAttribute("href", "/dashboard/tv/L01"));
    await waitFor(() => expect(screen.getByTestId("thresholds-badge")).toBeInTheDocument());
    expect(screen.getByTestId("thresholds-badge").textContent).toContain("95");
  });

  it("TV link follows the selected line", async () => {
    render(<DashboardPage />);
    await waitFor(() => expect(screen.getByTestId("line-section-L01")).toBeInTheDocument());
    const select = screen.getByTestId("line-filter") as HTMLSelectElement;
    fireEvent.change(select, { target: { value: "L02" } });
    await waitFor(() => expect(screen.getByTestId("tv-link")).toHaveAttribute("href", "/dashboard/tv/L02"));
  });

  it("shows unacked alert count badge and panel items", async () => {
    render(<DashboardPage />);
    await waitFor(() => expect(screen.getByTestId("alerts-badge")).toHaveTextContent("2"));
    expect(screen.getAllByTestId("alert-item")).toHaveLength(2);
    expect(screen.getByText("NG spike on L02")).toBeInTheDocument();
  });

  it("prepends realtime AlertRaised events and shows a toast", async () => {
    render(<DashboardPage />);
    await waitFor(() => expect(screen.getByTestId("alerts-badge")).toHaveTextContent("2"));
    expect(alertHandlers.length).toBeGreaterThan(0);

    const raised: AlertDto = {
      id: 3,
      type: "yield_drop",
      severity: "critical",
      message: "Yield drop on L03",
      lineCode: "L03",
      createdAtUtc: "2026-08-24T04:00:00Z",
      acknowledgedAtUtc: null,
    };
    act(() => alertHandlers.forEach((h) => h(raised)));

    await waitFor(() => expect(screen.getByTestId("alerts-badge")).toHaveTextContent("3"));
    expect(screen.getAllByTestId("alert-item")[0]).toHaveTextContent("Yield drop on L03");
    expect(screen.getByTestId("alert-toast")).toHaveTextContent("Yield drop on L03");
  });

  it("ack removes the alert and decrements the badge", async () => {
    const fetchMock = vi.mocked(fetch);
    render(<DashboardPage />);
    await waitFor(() => expect(screen.getByTestId("alerts-badge")).toHaveTextContent("2"));

    fireEvent.click(screen.getByTestId("alert-ack-2"));

    await waitFor(() => expect(screen.getByTestId("alerts-badge")).toHaveTextContent("1"));
    expect(screen.queryByText("NG spike on L02")).not.toBeInTheDocument();
    const ackCall = fetchMock.mock.calls.find(
      (c) => String(c[0]).includes("/api/alerts/2/ack") && (c[1]?.method ?? "GET") === "POST",
    );
    expect(ackCall).toBeDefined();
  });

  it("line selector filters grid", async () => {
    render(<DashboardPage />);
    await waitFor(() => expect(screen.getByTestId("line-filter")).toBeInTheDocument());
    await waitFor(() => expect(screen.getByTestId("line-section-L01")).toBeInTheDocument());
    const select = screen.getByTestId("line-filter") as HTMLSelectElement;
    fireEvent.change(select, { target: { value: "L02" } });
    await waitFor(() => expect(screen.getByTestId("line-section-L02")).toBeInTheDocument());
    expect(screen.queryByTestId("line-section-L01")).not.toBeInTheDocument();
  });
});

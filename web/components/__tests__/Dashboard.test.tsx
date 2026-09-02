import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import DashboardPage from "@/app/dashboard/page";

vi.mock("next/link", () => ({
  default: ({ href, children, ...props }: { href: string; children: React.ReactNode }) => (
    <a href={href} {...props}>
      {children}
    </a>
  ),
}));

vi.mock("@/lib/useLiveEvents", () => ({
  useLiveEvents: () => null,
}));

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

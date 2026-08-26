import { act } from "@testing-library/react";
import { render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi, beforeEach } from "vitest";
import DailySummary, { wibToday } from "@/components/DailySummary";

vi.mock("@/lib/api", () => ({
  getStationSummary: vi.fn(),
}));

import { getStationSummary } from "@/lib/api";
const getSummaryMock = vi.mocked(getStationSummary);

describe("DailySummary", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    getSummaryMock.mockReset();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("renders output / ng / yield numbers from the API", async () => {
    vi.useRealTimers();
    getSummaryMock.mockResolvedValue({
      stationId: 5, stationCode: "ST-05", output: 12, ng: 3, yieldPercent: 66.7,
    });

    render(<DailySummary stationId={5} />);

    await waitFor(() => expect(screen.getByText("12")).toBeInTheDocument());
    expect(screen.getByText("3")).toBeInTheDocument();
    expect(screen.getByText("66.7%")).toBeInTheDocument();
    expect(getSummaryMock).toHaveBeenCalledWith(5, wibToday());
  });

  it("shows an em dash while yield is null (no output yet)", async () => {
    vi.useRealTimers();
    getSummaryMock.mockResolvedValue({
      stationId: 5, stationCode: "ST-05", output: 0, ng: 0, yieldPercent: null,
    });

    render(<DailySummary stationId={5} />);

    const dashes = await screen.findAllByText("—");
    expect(dashes.length).toBeGreaterThanOrEqual(3);
  });

  it("shows an inline error when the API call fails", async () => {
    vi.useRealTimers();
    getSummaryMock.mockRejectedValue(new Error("api down"));

    render(<DailySummary stationId={5} />);

    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("api down"));
  });

  it("refetches when refreshSignal changes and on the 60 s interval", async () => {
    getSummaryMock.mockResolvedValue({
      stationId: 5, stationCode: "ST-05", output: 1, ng: 0, yieldPercent: 100,
    });
    const { rerender } = render(<DailySummary stationId={5} refreshSignal={0} />);
    await act(async () => {});

    rerender(<DailySummary stationId={5} refreshSignal={1} />);
    await act(async () => {});
    expect(getSummaryMock).toHaveBeenCalledTimes(2);

    await act(async () => {
      vi.advanceTimersByTime(60_000);
    });
    expect(getSummaryMock).toHaveBeenCalledTimes(3);
  });
});

import { render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi, beforeEach } from "vitest";
import ScanPage from "@/app/scan/page";

const replaceMock = vi.fn();
let stationIdParam: string | null = null;

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace: replaceMock, push: vi.fn() }),
  useSearchParams: () => ({ get: (key: string) => (key === "stationId" ? stationIdParam : null) }),
}));

vi.mock("@/lib/api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api")>();
  return { ...actual, isLoggedIn: () => true };
});

describe("ScanPage station resolution", () => {
  beforeEach(() => {
    window.localStorage.clear();
    replaceMock.mockClear();
    stationIdParam = null;
  });

  it("accepts /scan?stationId=5 and persists it", async () => {
    stationIdParam = "5";

    render(<ScanPage />);

    await waitFor(() => expect(screen.getByLabelText("Serial number")).toBeInTheDocument());
    expect(window.localStorage.getItem("kiosk_station_id")).toBe("5");
    expect(replaceMock).not.toHaveBeenCalled();
  });

  it("rejects ?stationId=0 and shows the Set Station form instead", async () => {
    stationIdParam = "0";

    render(<ScanPage />);

    await waitFor(() => expect(screen.getByText("Set Station ID")).toBeInTheDocument());
    expect(screen.queryByLabelText("Serial number")).not.toBeInTheDocument();
    expect(window.localStorage.getItem("kiosk_station_id")).toBeNull();
  });
});

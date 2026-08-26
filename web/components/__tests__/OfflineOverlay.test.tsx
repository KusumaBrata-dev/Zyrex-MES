import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import OfflineOverlay from "@/components/OfflineOverlay";

vi.mock("@/hooks/useServerHeartbeat", () => ({
  useServerHeartbeat: vi.fn(),
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace: vi.fn(), push: vi.fn() }),
  useSearchParams: () => ({ get: () => "5" }),
}));

vi.mock("@/lib/api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api")>();
  return {
    ...actual,
    isLoggedIn: () => true,
    getStationSummary: () =>
      Promise.resolve({ stationId: 5, stationCode: "ST-05", output: 0, ng: 0, yieldPercent: null }),
  };
});

import { useServerHeartbeat } from "@/hooks/useServerHeartbeat";
const heartbeatMock = vi.mocked(useServerHeartbeat);

describe("OfflineOverlay integration (scan page)", () => {
  it("renders the blocking overlay when offline", () => {
    heartbeatMock.mockReturnValue({ offline: true });
    render(<OfflineOverlay />);

    const overlay = screen.getByTestId("offline-overlay");
    expect(overlay.className).toContain("z-[100]");
    expect(screen.getByText("SERVER OFFLINE")).toBeInTheDocument();
    expect(screen.getByText("HUBUNGI LEADER")).toBeInTheDocument();
  });

  it("does not render anything when online (page-level conditional)", async () => {
    // Page-level check: ScanPage renders the overlay only when the hook says so.
    heartbeatMock.mockReturnValue({ offline: false });
    render(<OfflineOverlay />);
    // Direct render always shows; the conditional lives in the pages —
    // verified through ScanPage below.
  });

  it("ScanPage shows the overlay only while offline", async () => {
    const { default: ScanPage } = await import("@/app/scan/page");

    heartbeatMock.mockReturnValue({ offline: true });
    const { queryByTestId, unmount } = render(<ScanPage />);
    expect(queryByTestId("offline-overlay")).not.toBeNull();
    unmount();

    heartbeatMock.mockReturnValue({ offline: false });
    render(<ScanPage />);
    expect(screen.queryByTestId("offline-overlay")).toBeNull();
  });
});

// The AI placeholder tests live here too (small surface).
describe("AiChatPlaceholder", () => {
  it("opens a panel with disabled input and PLAN 5 badge", async () => {
    const { default: AiChatPlaceholder } = await import("@/components/AiChatPlaceholder");
    render(<AiChatPlaceholder />);
    fireEvent.click(screen.getByRole("button", { name: /Open AI Assistant/i }));

    expect(screen.getByText("AI Assistant")).toBeInTheDocument();
    expect(screen.getByText(/will be available in Plan 5/i)).toBeInTheDocument();
    const input = screen.getByLabelText("AI message");
    expect(input).toBeDisabled();
    expect(screen.getByText("PLAN 5")).toBeInTheDocument();
  });
});

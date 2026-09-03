import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import OfflineGate from "@/components/OfflineGate";

vi.mock("@/hooks/useServerHeartbeat", () => ({
  useServerHeartbeat: vi.fn(() => ({ offline: true })),
}));

const pathnameMock = vi.fn(() => "/scan");

vi.mock("next/navigation", () => ({
  usePathname: () => pathnameMock(),
  useRouter: () => ({ replace: vi.fn(), push: vi.fn() }),
  useSearchParams: () => ({ get: () => null }),
}));

describe("OfflineGate dashboard exception", () => {
  it("blocks scan route when offline", () => {
    pathnameMock.mockReturnValue("/scan");
    render(<OfflineGate />);
    expect(screen.getByTestId("offline-overlay")).toBeInTheDocument();
  });

  it("does NOT block dashboard route even when offline", () => {
    pathnameMock.mockReturnValue("/dashboard");
    const { container } = render(<OfflineGate />);
    expect(screen.queryByTestId("offline-overlay")).not.toBeInTheDocument();
    // container should be empty
    expect(container.innerHTML).toBe("");
  });

  it("does NOT block dashboard sub-route", () => {
    pathnameMock.mockReturnValue("/dashboard?tv=1");
    const { container } = render(<OfflineGate />);
    expect(container.innerHTML).toBe("");
  });
});

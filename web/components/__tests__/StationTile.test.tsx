import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import StationTile from "@/components/dashboard/StationTile";

vi.mock("next/link", () => ({
  default: ({ href, children, ...props }: { href: string; children: React.ReactNode }) => (
    <a href={href} {...props}>
      {children}
    </a>
  ),
}));

describe("StationTile", () => {
  it("renders stationCode, name, output/ng and status strip", () => {
    render(
      <StationTile
        stationId={11}
        stationCode="ST-A1"
        name="Station A1"
        outputToday={12}
        ngToday={3}
        lastEventAtUtc="2026-08-24T01:00:00Z"
        status="active"
      />,
    );
    expect(screen.getByText("ST-A1")).toBeInTheDocument();
    expect(screen.getByText("Station A1")).toBeInTheDocument();
    expect(screen.getByTestId("station-output-ST-A1")).toHaveTextContent("12");
    expect(screen.getByTestId("station-ng-ST-A1")).toHaveTextContent("3");
    expect(screen.getByTestId("station-status-ST-A1").className).toContain("bg-emerald-500");
    expect(screen.getByText("active")).toBeInTheDocument();
    const link = screen.getByTestId("station-tile-ST-A1") as HTMLAnchorElement;
    expect(link.getAttribute("href")).toBe("/ng-report?stationId=11");
  });

  it("renders idle strip when status idle", () => {
    render(
      <StationTile
        stationId={5}
        stationCode="ST-05"
        name="Station 05"
        outputToday={0}
        ngToday={0}
        lastEventAtUtc={null}
        status="idle"
      />,
    );
    expect(screen.getByTestId("station-status-ST-05").className).toContain("bg-neutral-300");
  });
});

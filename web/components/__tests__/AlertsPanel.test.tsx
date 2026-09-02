import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import AlertsPanel from "@/components/dashboard/AlertsPanel";
import type { AlertDto } from "@/lib/api";

const alerts: AlertDto[] = [
  {
    id: 2,
    type: "ng_spike",
    severity: "critical",
    message: "NG spike on L02",
    lineCode: "L02",
    createdAtUtc: "2026-08-24T03:00:00Z",
    acknowledgedAtUtc: null,
  },
  {
    id: 1,
    type: "yield_drop",
    severity: "warning",
    message: "Yield drop on L01",
    lineCode: "L01",
    createdAtUtc: "2026-08-24T02:00:00Z",
    acknowledgedAtUtc: null,
  },
];

describe("AlertsPanel", () => {
  afterEach(() => cleanup());

  it("renders the alert list with severity, message and line", () => {
    render(<AlertsPanel alerts={alerts} onAck={() => {}} />);
    const items = screen.getAllByTestId("alert-item");
    expect(items).toHaveLength(2);
    expect(items[0]).toHaveTextContent("NG spike on L02");
    expect(items[0]).toHaveTextContent("critical");
    expect(items[1]).toHaveTextContent("Yield drop on L01");
  });

  it("shows an empty state when there are no alerts", () => {
    render(<AlertsPanel alerts={[]} onAck={() => {}} />);
    expect(screen.getByTestId("alerts-empty")).toBeInTheDocument();
  });

  it("ack button calls onAck with the alert id", () => {
    const onAck = vi.fn();
    render(<AlertsPanel alerts={alerts} onAck={onAck} />);
    fireEvent.click(screen.getByTestId("alert-ack-2"));
    expect(onAck).toHaveBeenCalledWith(2);
  });

  it("collapses and expands via the toggle", () => {
    render(<AlertsPanel alerts={alerts} onAck={() => {}} />);
    expect(screen.getByTestId("alerts-panel")).toBeInTheDocument();
    fireEvent.click(screen.getByTestId("alerts-toggle"));
    expect(screen.queryByTestId("alert-item")).not.toBeInTheDocument();
    fireEvent.click(screen.getByTestId("alerts-toggle"));
    expect(screen.getAllByTestId("alert-item")).toHaveLength(2);
  });
});

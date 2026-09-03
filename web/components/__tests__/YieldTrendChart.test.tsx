import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import YieldTrendChart from "@/components/charts/YieldTrendChart";

const points = [
  { date: "2026-08-18", output: 100, ng: 5, yieldPercent: 95.0 },
  { date: "2026-08-19", output: 80, ng: 8, yieldPercent: 90.0 },
  { date: "2026-08-20", output: 0, ng: 0, yieldPercent: null },
  { date: "2026-08-21", output: 120, ng: 6, yieldPercent: 95.0 },
  { date: "2026-08-22", output: 90, ng: 3, yieldPercent: 96.7 },
];

describe("YieldTrendChart", () => {
  it("renders an svg with a point per non-null day", () => {
    render(<YieldTrendChart points={points} />);
    expect(screen.getByTestId("yield-trend-chart")).toBeInTheDocument();
    expect(screen.getAllByTestId("yield-point")).toHaveLength(4);
  });

  it("breaks the line into segments on null (gap day)", () => {
    render(<YieldTrendChart points={points} />);
    // null on 08-20 splits the polyline into 2 segments
    expect(screen.getAllByTestId("yield-trend-segment")).toHaveLength(2);
  });

  it("labels the x axis with short dates", () => {
    render(<YieldTrendChart points={points} />);
    expect(screen.getByTestId("yield-tick-2026-08-18")).toHaveTextContent("08-18");
    expect(screen.getByTestId("yield-tick-2026-08-22")).toHaveTextContent("08-22");
  });

  it("renders a single segment when there are no gaps", () => {
    render(<YieldTrendChart points={points.filter((p) => p.yieldPercent !== null)} />);
    expect(screen.getAllByTestId("yield-trend-segment")).toHaveLength(1);
  });
});

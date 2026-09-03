import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import NgParetoChart from "@/components/charts/NgParetoChart";

const items = [
  { ngCode: "SCRATCH", count: 10 },
  { ngCode: "DENT", count: 5 },
  { ngCode: "OTHER", count: 5 },
];

describe("NgParetoChart", () => {
  it("renders one bar per item", () => {
    render(<NgParetoChart items={items} />);
    expect(screen.getByTestId("ng-pareto-chart")).toBeInTheDocument();
    expect(screen.getAllByTestId("pareto-bar")).toHaveLength(3);
  });

  it("shows cumulative percentages in order (total 20 → 50 / 75 / 100)", () => {
    render(<NgParetoChart items={items} />);
    expect(screen.getByTestId("pareto-cum-SCRATCH")).toHaveTextContent("50%");
    expect(screen.getByTestId("pareto-cum-DENT")).toHaveTextContent("75%");
    expect(screen.getByTestId("pareto-cum-OTHER")).toHaveTextContent("100%");
  });

  it("sorts bars descending even when input is unsorted", () => {
    render(<NgParetoChart items={[items[1], items[2], items[0]]} />);
    const bars = screen.getAllByTestId("pareto-bar");
    expect(bars[0]).toHaveTextContent("SCRATCH");
  });

  it("handles an empty list", () => {
    render(<NgParetoChart items={[]} />);
    expect(screen.getByTestId("ng-pareto-chart")).toBeInTheDocument();
    expect(screen.queryAllByTestId("pareto-bar")).toHaveLength(0);
  });
});

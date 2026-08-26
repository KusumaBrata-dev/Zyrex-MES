import { render, screen, fireEvent, act } from "@testing-library/react";
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import ResultOverlay from "@/components/ResultOverlay";

vi.mock("@/lib/sound", () => ({
  playOk: vi.fn(),
  playNg: vi.fn(),
}));

import { playNg, playOk } from "@/lib/sound";

describe("ResultOverlay", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.mocked(playOk).mockClear();
    vi.mocked(playNg).mockClear();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("PASS: renders giant serial + next station, plays ok sound, auto-dismisses after 1.5 s", () => {
    const onDone = vi.fn();
    render(
      <ResultOverlay kind="PASS" sn="SN-777" nextStationCode="ST-20" onDone={onDone} />,
    );

    expect(screen.getByText("PASS")).toBeInTheDocument();
    expect(screen.getByText("SN-777")).toBeInTheDocument();
    expect(screen.getByText(/Next station: ST-20/)).toBeInTheDocument();
    expect(playOk).toHaveBeenCalledOnce();
    expect(playNg).not.toHaveBeenCalled();

    act(() => vi.advanceTimersByTime(1500));
    expect(onDone).toHaveBeenCalledOnce();
  });

  it("REJECTED: renders reason in red overlay, plays ng sound, dismisses only on tap", () => {
    const onDone = vi.fn();
    render(<ResultOverlay kind="REJECTED" sn="SN-1" reason="duplicate transaction at this station" onDone={onDone} />);

    const overlay = screen.getByTestId("result-overlay");
    expect(overlay.className).toContain("bg-zred");
    expect(screen.getByText("REJECTED")).toBeInTheDocument();
    expect(screen.getByText("duplicate transaction at this station")).toBeInTheDocument();
    expect(playNg).toHaveBeenCalledOnce();
    expect(playOk).not.toHaveBeenCalled();

    act(() => vi.advanceTimersByTime(3000));
    expect(onDone).not.toHaveBeenCalled(); // no auto-dismiss

    fireEvent.click(overlay);
    expect(onDone).toHaveBeenCalledOnce();
  });
});

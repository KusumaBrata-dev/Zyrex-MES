import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import ScanInput from "@/components/ScanInput";

function type(input: HTMLElement, value: string) {
  fireEvent.change(input, { target: { value } });
}

describe("ScanInput", () => {
  let onSubmit: ReturnType<typeof vi.fn<(sn: string) => void>>;

  beforeEach(() => {
    vi.useFakeTimers();
    onSubmit = vi.fn<(sn: string) => void>();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("submits the trimmed serial on Enter and clears the field", () => {
    render(<ScanInput onSubmit={onSubmit} />);
    const input = screen.getByLabelText("Serial number");
    type(input, "  SN-001  ");

    fireEvent.keyDown(input, { key: "Enter" });

    expect(onSubmit).toHaveBeenCalledExactlyOnceWith("SN-001");
    expect((input as HTMLInputElement).value).toBe("");
  });

  it("ignores Enter within 500 ms of the previous submit (scanner double-read)", () => {
    render(<ScanInput onSubmit={onSubmit} />);
    const input = screen.getByLabelText("Serial number");

    type(input, "SN-1");
    fireEvent.keyDown(input, { key: "Enter" });
    type(input, "SN-2");
    fireEvent.keyDown(input, { key: "Enter" }); // inside guard window

    expect(onSubmit).toHaveBeenCalledExactlyOnceWith("SN-1");

    vi.advanceTimersByTime(500);
    type(input, "SN-2");
    fireEvent.keyDown(input, { key: "Enter" }); // after guard window

    expect(onSubmit).toHaveBeenCalledTimes(2);
    expect(onSubmit).toHaveBeenLastCalledWith("SN-2");
  });

  it("does not submit empty input", () => {
    render(<ScanInput onSubmit={onSubmit} />);
    const input = screen.getByLabelText("Serial number");

    fireEvent.keyDown(input, { key: "Enter" });

    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("re-focuses itself after losing focus", () => {
    render(<ScanInput onSubmit={onSubmit} />);
    const input = screen.getByLabelText("Serial number") as HTMLInputElement;

    input.blur();

    expect(document.activeElement).toBe(input);
  });
});

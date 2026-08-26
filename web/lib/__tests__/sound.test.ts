import { describe, expect, it } from "vitest";
import { toneFreqs } from "../sound";

describe("toneFreqs", () => {
  it("returns the success chime frequencies 880 -> 1320 Hz", () => {
    expect(toneFreqs().ok).toEqual([880, 1320]);
  });

  it("returns the failure buzz frequency 180 Hz", () => {
    expect(toneFreqs().ng).toEqual([180]);
  });
});

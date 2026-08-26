import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi, beforeEach } from "vitest";
import { HEARTBEAT_INTERVAL_MS, useServerHeartbeat } from "@/hooks/useServerHeartbeat";

const fetchMock = vi.fn<(url: string) => Promise<Response>>();

vi.stubGlobal("fetch", fetchMock);

function ok(): Promise<Response> {
  return Promise.resolve(new Response(JSON.stringify({ status: "ok" }), { status: 200 }));
}
function fail(): Promise<Response> {
  return Promise.reject(new TypeError("network down"));
}

describe("useServerHeartbeat", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    fetchMock.mockReset();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("starts online and stays online while /health answers", async () => {
    fetchMock.mockImplementation(ok);
    const { result } = renderHook(() => useServerHeartbeat());

    await act(async () => {});
    expect(result.current.offline).toBe(false);

    await act(async () => {
      vi.advanceTimersByTime(HEARTBEAT_INTERVAL_MS);
    });
    expect(result.current.offline).toBe(false);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(fetchMock.mock.calls[0]?.[0]).toBe("/health");
  });

  it("goes offline only after two consecutive failures", async () => {
    fetchMock.mockImplementation(fail);
    const { result } = renderHook(() => useServerHeartbeat());
    await act(async () => {}); // failure #1 (initial ping)
    expect(result.current.offline).toBe(false); // single failure does not flap

    await act(async () => {
      vi.advanceTimersByTime(HEARTBEAT_INTERVAL_MS);
    }); // failure #2
    expect(result.current.offline).toBe(true);
  });

  it("recovers to online after a success following failures", async () => {
    fetchMock.mockImplementation(fail);
    const { result } = renderHook(() => useServerHeartbeat());
    await act(async () => {});
    await act(async () => {
      vi.advanceTimersByTime(HEARTBEAT_INTERVAL_MS);
    });
    expect(result.current.offline).toBe(true);

    fetchMock.mockImplementation(ok);
    await act(async () => {
      vi.advanceTimersByTime(HEARTBEAT_INTERVAL_MS);
    });
    expect(result.current.offline).toBe(false);
  });
});

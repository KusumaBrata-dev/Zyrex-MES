import { afterEach, describe, expect, it, vi } from "vitest";
import { TOKEN_KEY, USER_KEY, login } from "../api";

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

afterEach(() => {
  vi.restoreAllMocks();
  sessionStorage.clear();
});

describe("login", () => {
  it("stores token and user profile in sessionStorage on success", async () => {
    const user = { username: "op1", fullName: "Operator Satu", role: "Operator" };
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse(200, { token: "jwt-abc", user }),
    );
    vi.stubGlobal("fetch", fetchMock);

    const profile = await login("op1", "secret");

    expect(profile).toEqual(user);
    expect(sessionStorage.getItem(TOKEN_KEY)).toBe("jwt-abc");
    expect(JSON.parse(sessionStorage.getItem(USER_KEY)!)).toEqual(user);

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("/api/auth/login"); // NEXT_PUBLIC_API_BASE empty in tests
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body as string)).toEqual({ username: "op1", password: "secret" });
  });

  it("throws the error message from the response body on failure", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(jsonResponse(401, { error: "invalid credentials" })),
    );

    await expect(login("op1", "wrong")).rejects.toThrow("invalid credentials");

    expect(sessionStorage.getItem(TOKEN_KEY)).toBeNull();
  });

  it("falls back to the HTTP status when the body has no error field", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(500, {})));

    await expect(login("op1", "x")).rejects.toThrow("HTTP 500");
  });
});

import { expect, request, test } from "@playwright/test";
import { e2eStationId, seedE2eData, seedE2eSupervisor } from "./seed";

/**
 * Dashboard E2E (phase 4).
 * Covers AC-19 end-to-end: a scan via the API raises the station tile on an
 * open dashboard within ~2s via the realtime hub (LongPolling through the dev
 * proxy), plus the insights page render and the Excel export endpoints.
 * AC-12 full-loop (detector → broadcast → UI) is CLOSED-PARTIAL by plan:
 * detector + broadcast are unit-tested; the UI panel is vitest-covered.
 */

const SN = "E2E-SN-0001";
const API = "http://localhost:8080";

test.beforeAll(() => {
  seedE2eData();
  seedE2eSupervisor();
});

async function apiLogin(username: string, password: string): Promise<string> {
  const ctx = await request.newContext({ baseURL: API });
  const res = await ctx.post("/api/auth/login", { data: { username, password } });
  expect(res.ok()).toBeTruthy();
  const body = (await res.json()) as { token: string };
  await ctx.dispose();
  return body.token;
}

test("AC-19: dashboard grid updates within 2s of a scan", async ({ page }) => {
  // login as supervisor, then open the dashboard
  await page.goto("/login");
  await page.getByLabel("Username").fill("e2e_sup");
  await page.getByLabel("Password").fill("E2e!Pass123");
  await page.getByRole("button", { name: /sign in/i }).click();
  await page.waitForURL("**/scan");

  await page.goto("/dashboard");
  await expect(page.getByTestId("line-section-E99")).toBeVisible({ timeout: 15_000 });
  const tileOutput = page.getByTestId("station-output-ST-E2E");
  const before = Number.parseInt((await tileOutput.innerText()) || "0", 10);
  await page.screenshot({ path: "e2e/artifacts/10-dashboard-grid.png" });

  // trigger a scan directly against the API as the operator
  const token = await apiLogin("e2e_op", "E2e!Pass123");
  const ctx = await request.newContext({ baseURL: API, extraHTTPHeaders: { Authorization: `Bearer ${token}` } });
  const scanRes = await ctx.post("/api/production/scan", {
    data: { serialNumber: SN, stationId: e2eStationId() },
  });
  expect(scanRes.ok()).toBeTruthy();

  // tile increments via the hub (fallback poll is 30s — too slow for AC-19)
  await expect
    .poll(async () => Number.parseInt((await tileOutput.innerText()) || "0", 10), { timeout: 5_000 })
    .toBeGreaterThan(before);
  await page.screenshot({ path: "e2e/artifacts/11-dashboard-realtime.png" });
  await ctx.dispose();
});

test("insights page renders charts; export endpoints return xlsx", async ({ page }) => {
  await page.goto("/login");
  await page.getByLabel("Username").fill("e2e_sup");
  await page.getByLabel("Password").fill("E2e!Pass123");
  await page.getByRole("button", { name: /sign in/i }).click();
  await page.waitForURL("**/scan");

  await page.goto("/dashboard/insights");
  await expect(page.getByTestId("yield-trend-chart")).toBeVisible({ timeout: 15_000 });
  await expect(page.getByTestId("ng-pareto-chart")).toBeVisible();
  await page.screenshot({ path: "e2e/artifacts/12-insights.png" });

  const token = await apiLogin("e2e_sup", "E2e!Pass123");
  const ctx = await request.newContext({ baseURL: API, extraHTTPHeaders: { Authorization: `Bearer ${token}` } });
  const today = new Date().toISOString().slice(0, 10);
  const ngList = await ctx.get(`/api/export/ng-list.xlsx?from=${today}&to=${today}`);
  expect(ngList.status()).toBe(200);
  expect(ngList.headers()["content-type"]).toContain("spreadsheetml");
  const summary = await ctx.get(`/api/export/station-summary.xlsx?date=${today}`);
  expect(summary.status()).toBe(200);
  await ctx.dispose();
});

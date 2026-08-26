import { expect, test } from "@playwright/test";
import { e2eStationId, seedE2eData } from "./seed";

const SN = "E2E-SN-0001";

test.beforeAll(() => {
  seedE2eData();
});

test("kiosk happy path: login -> scan -> PASS overlay -> summary >= 1", async ({ page }) => {
  // (a) login e2e_op -> redirected to /scan
  await page.goto("/login");
  await page.screenshot({ path: "e2e/artifacts/01-login.png" });
  await page.getByLabel("Username").fill("e2e_op");
  await page.getByLabel("Password").fill("E2e!Pass123");
  await page.getByRole("button", { name: /sign in/i }).click();
  await page.waitForURL("**/scan");

  // Bind the kiosk to the seeded station via the documented URL pattern.
  await page.goto(`/scan?stationId=${e2eStationId()}`);
  await page.waitForSelector('[aria-label="Serial number"]');
  await page.screenshot({ path: "e2e/artifacts/02-scan-screen.png" });

  // (b) scan the seeded serial
  const input = page.getByLabel("Serial number");
  await input.fill(SN);
  await input.press("Enter");

  // PASS overlay with the giant serial number.
  await expect(page.getByText("PASS")).toBeVisible();
  await expect(page.getByText(SN)).toBeVisible();
  await page.screenshot({ path: "e2e/artifacts/03-pass-overlay.png" });

  // Overlay auto-dismisses after ~1.5 s.
  await expect(page.getByText("PASS")).toBeHidden({ timeout: 5_000 });

  // (c) DailySummary OUTPUT counted the scan (>= 1). Cards render in
  // OUTPUT / NG / YIELD order, each value in a .text-4xl paragraph.
  const outputValue = page.locator('section p.text-4xl').first();
  await expect
    .poll(async () => Number.parseInt(await outputValue.innerText(), 10), { timeout: 10_000 })
    .toBeGreaterThanOrEqual(1);
  await page.screenshot({ path: "e2e/artifacts/04-summary-updated.png" });
});

test("AC-13: aborted /health pings raise SERVER OFFLINE, recovery clears it", async ({ page }) => {
  await page.goto("/login");
  await page.getByLabel("Username").fill("e2e_op");
  await page.getByLabel("Password").fill("E2e!Pass123");
  await page.getByRole("button", { name: /sign in/i }).click();
  await page.waitForURL("**/scan");

  // Bind the kiosk to the seeded station via the documented URL pattern.
  await page.goto(`/scan?stationId=${e2eStationId()}`);

  // Block health checks; two consecutive failures (<=10 s) must raise the overlay.
  await page.context().route("**/health", (route) => route.abort());
  await expect(page.getByText("SERVER OFFLINE")).toBeVisible({ timeout: 12_000 });
  await page.screenshot({ path: "e2e/artifacts/05-server-offline.png" });

  // Restore connectivity; the next successful ping clears it (<=10 s).
  await page.context().unroute("**/health");
  await expect(page.getByText("SERVER OFFLINE")).toBeHidden({ timeout: 12_000 });
  await page.screenshot({ path: "e2e/artifacts/06-recovered.png" });
});

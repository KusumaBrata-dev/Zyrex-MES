import type { Config } from "@playwright/test";

/**
 * E2E config. globalSetup boots the stack (Postgres relay, API :8080, Next
 * dev :3000) — services already listening are reused; teardown kills only the
 * ones it started. Manual run docs live in web/README.md ("E2E").
 */
const config: Config = {
  testDir: "./e2e",
  outputDir: "./e2e/artifacts",
  timeout: 90_000,
  workers: 1, // sequential — the suite shares one seeded dataset
  retries: 0,
  globalSetup: "./e2e/global-setup.ts",
  globalTeardown: "./e2e/global-teardown.ts",
  use: {
    baseURL: "http://localhost:3000",
    screenshot: "only-on-failure",
    trace: "retain-on-failure",
  },
  reporter: [["list"], ["html", { outputFolder: "./e2e/html-report", open: "never" }]],
};

export default config;

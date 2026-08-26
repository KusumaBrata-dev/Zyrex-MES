import { execSync } from "node:child_process";

/**
 * Kills only the services global-setup started (tracked on globalThis);
 * pre-existing listeners are left alone.
 */
export default function globalTeardown(): void {
  const g = globalThis as unknown as {
    __e2eApi?: { pid?: number };
    __e2eWeb?: { pid?: number };
  };

  for (const [name, child] of [
    ["web", g.__e2eWeb],
    ["api", g.__e2eApi],
  ] as const) {
    if (!child?.pid) continue;
    try {
      if (process.platform === "win32") {
        execSync(`taskkill /PID ${child.pid} /T /F`, { stdio: "ignore" });
      } else {
        process.kill(-child.pid, "SIGTERM");
      }
      console.log(`stopped ${name} (pid ${child.pid})`);
    } catch {
      // already gone
    }
  }
}

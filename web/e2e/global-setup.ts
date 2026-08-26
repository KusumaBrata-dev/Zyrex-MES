import { type ChildProcess, execSync, spawn } from "node:child_process";
import { closeSync, mkdirSync, openSync, readFileSync } from "node:fs";

/**
 * Starts the E2E stack (Postgres relay, MES API on :8080, Next dev on :3000).
 * Services already listening are reused; only processes started here are
 * killed by global-teardown.ts. Service logs go to e2e/.logs/ (gitignored).
 */

const g = globalThis as unknown as {
  __e2eApi?: ChildProcess;
  __e2eWeb?: ChildProcess;
};

function waitFor(url: string, timeoutMs: number): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  return new Promise((resolve, reject) => {
    const tick = async () => {
      try {
        const res = await fetch(url);
        if (res.ok || res.status === 404) return resolve(); // reachable is enough
      } catch {
        // not up yet
      }
      if (Date.now() > deadline) return reject(new Error(`timeout waiting for ${url}`));
      setTimeout(tick, 500);
    };
    void tick();
  });
}

function start(command: string, cwd: string, log: string): ChildProcess {
  const fd = openSync(log, "a");
  const child = spawn(command, { cwd, stdio: ["ignore", fd, fd], shell: true });
  // Parent side can release the fd; the child keeps its own handle.
  closeSync(fd);
  return child;
}

export default async function globalSetup(): Promise<void> {
  mkdirSync("e2e/.logs", { recursive: true });

  // 1. Postgres relay inside WSL.
  try {
    execSync("powershell -File scripts/wsl-db-restart.ps1", { cwd: "..", stdio: "inherit", timeout: 180_000 });
  } catch (err) {
    console.warn("db-restart script failed (continuing if DB already up):", err);
  }

  // 2. MES API on :8080.
  try {
    await waitFor("http://localhost:8080/health", 2_000);
    console.log("API already running on :8080 — reusing.");
  } catch {
    // cwd is web/ → repo root is ".."; the project path must be root-relative.
    g.__e2eApi = start("dotnet run --project server/src/ZyrexMES.Api", "..", "e2e/.logs/api.log");
    await waitFor("http://localhost:8080/health", 120_000);
    if (readFileSync("e2e/.logs/api.log", "utf8").includes("does not exist")) {
      throw new Error("API failed to start: project path not found (see e2e/.logs/api.log)");
    }
    console.log("API started on :8080.");
  }

  // 3. Next dev server on :3000.
  try {
    await waitFor("http://localhost:3000/login", 2_000);
    console.log("Next dev already running on :3000 — reusing.");
  } catch {
    g.__e2eWeb = start("npm run dev", ".", "e2e/.logs/web.log");
    await waitFor("http://localhost:3000/login", 120_000);
    console.log("Next dev started on :3000.");
  }

  // 4. Warm the dev-server proxy paths: the very first rewritten request on a
  // cold next dev can hang, which would otherwise flake the login step.
  const warmDeadline = Date.now() + 60_000;
  for (const [url, init] of [
    ["http://localhost:3000/health", { method: "GET" }],
    [
      "http://localhost:3000/api/auth/login",
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ username: "warmup", password: "warmup" }),
      },
    ],
  ] as const) {
    for (;;) {
      try {
        const res = await fetch(url, init);
        if (res.status !== 502 && res.status !== 504) break; // proxied answer reached the API
      } catch {
        // retry
      }
      if (Date.now() > warmDeadline) throw new Error(`proxy warm-up failed for ${url}`);
      await new Promise((r) => setTimeout(r, 500));
    }
    console.log(`warmed ${url}`);
  }
}

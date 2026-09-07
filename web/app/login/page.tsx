"use client";

import { FormEvent, useState } from "react";
import { useRouter } from "next/navigation";
import { login } from "@/lib/api";

export default function LoginPage() {
  const router = useRouter();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await login(username.trim(), password);
      router.push("/scan");
    } catch (err) {
      setError(err instanceof Error ? err.message : "login failed");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-1 items-center justify-center p-4">
      {/* Background accent */}
      <div className="pointer-events-none absolute inset-0 overflow-hidden">
        <div className="absolute -top-40 -right-40 h-96 w-96 rounded-full bg-zred/10 blur-3xl" />
        <div className="absolute -bottom-40 -left-40 h-96 w-96 rounded-full bg-zbright/5 blur-3xl" />
      </div>

      <form
        onSubmit={onSubmit}
        className="relative w-full max-w-sm animate-fade-in"
      >
        {/* Logo */}
        <div className="mb-8 text-center">
          <img src="/brand/logo-white.svg" alt="Zyrex" className="mx-auto mb-4 h-16 w-auto opacity-90" />
          <h1 className="text-2xl font-black tracking-tight text-white">
            Zyrex<span className="text-zbright">MES</span>
          </h1>
          <p className="mt-2 text-xs uppercase tracking-widest text-zyrex-muted">
            Production Control System
          </p>
        </div>

        {/* Card */}
        <div className="zyrex-card p-6">
          {/* Username */}
          <div className="mb-4">
            <label className="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-zyrex-muted" htmlFor="username">
              Username
            </label>
            <input
              id="username"
              type="text"
              autoComplete="username"
              required
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              className="w-full rounded-lg border border-zyrex-border bg-zyrex-surface px-4 py-2.5 text-white placeholder-zyrex-muted/50 focus:border-zred focus:outline-none focus:ring-1 focus:ring-zred/50 transition-all"
              disabled={busy}
            />
          </div>

          {/* Password */}
          <div className="mb-5">
            <label className="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-zyrex-muted" htmlFor="password">
              Password
            </label>
            <input
              id="password"
              type="password"
              autoComplete="current-password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="w-full rounded-lg border border-zyrex-border bg-zyrex-surface px-4 py-2.5 text-white placeholder-zyrex-muted/50 focus:border-zred focus:outline-none focus:ring-1 focus:ring-zred/50 transition-all"
              disabled={busy}
            />
          </div>

          {/* Error */}
          {error && (
            <div role="alert" className="mb-4 rounded-lg border border-zbright/30 bg-zbright/10 px-4 py-3 text-sm text-zbright">
              ⚠ {error}
            </div>
          )}

          {/* Submit */}
          <button
            type="submit"
            disabled={busy}
            className="w-full rounded-lg bg-zred py-3 font-bold text-white hover:bg-zbright disabled:opacity-50 disabled:cursor-not-allowed transition-colors focus:outline-none focus:ring-2 focus:ring-zred/50 focus:ring-offset-2 focus:ring-offset-zyrex-black"
          >
            {busy ? (
              <span className="flex items-center justify-center gap-2">
                <span className="h-4 w-4 animate-spin rounded-full border-2 border-white/30 border-t-white" />
                Signing in...
              </span>
            ) : (
              "Sign In"
            )}
          </button>
        </div>

        {/* Footer hint */}
        <p className="mt-6 text-center text-[11px] text-zyrex-muted">
          PT Zyrexindo Mandiri Buana Tbk · MES v1.0
        </p>
      </form>
    </div>
  );
}
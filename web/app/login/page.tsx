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
    <div className="flex flex-1 items-center justify-center p-4 relative overflow-hidden">
      {/* Background accent */}
      <div className="pointer-events-none absolute inset-0 overflow-hidden">
        <div className="absolute -top-40 -right-40 h-96 w-96 rounded-full bg-red-50 blur-3xl" />
        <div className="absolute -bottom-40 -left-40 h-96 w-96 rounded-full bg-red-100 blur-3xl" />
      </div>

      <form
        onSubmit={onSubmit}
        className="relative w-full max-w-sm animate-fade-in"
      >
        {/* Logo */}
        <div className="mb-8 text-center">
          <img src="/brand/logo-white.svg" alt="Zyrex" className="mx-auto mb-4 h-16 w-auto" />
          <h1 className="text-2xl font-black tracking-tight text-slate-900">
            Zyrex<span className="text-zbright">MES</span>
          </h1>
          <p className="mt-2 text-xs uppercase tracking-widest text-slate-400">
            Production Control System
          </p>
        </div>

        {/* Card */}
        <div className="zyrex-card p-6">
          {/* Username */}
          <div className="mb-4">
            <label className="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-slate-500" htmlFor="username">
              Username
            </label>
            <input
              id="username"
              type="text"
              autoComplete="username"
              required
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              className="zyrex-input"
              disabled={busy}
            />
          </div>

          {/* Password */}
          <div className="mb-5">
            <label className="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-slate-500" htmlFor="password">
              Password
            </label>
            <input
              id="password"
              type="password"
              autoComplete="current-password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="zyrex-input"
              disabled={busy}
            />
          </div>

          {/* Error */}
          {error && (
            <div role="alert" className="mb-4 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
              ⚠ {error}
            </div>
          )}

          {/* Submit */}
          <button
            type="submit"
            disabled={busy}
            className="zyrex-btn-primary w-full"
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
        <p className="mt-6 text-center text-[11px] text-slate-400">
          PT Zyrexindo Mandiri Buana Tbk · MES v1.0
        </p>
      </form>
    </div>
  );
}
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
    <div className="flex flex-1 items-center justify-center p-6">
      <form
        onSubmit={onSubmit}
        className="w-full max-w-sm rounded-xl border border-black/10 bg-white p-8 shadow-lg dark:border-white/10 dark:bg-neutral-900"
      >
        <img src="/brand/logo-white.svg" alt="Zyrex" className="mx-auto mb-6 h-16 w-auto" />
        <h1 className="mb-6 text-center text-xl font-semibold">Login</h1>

        <label className="mb-1 block text-sm font-medium" htmlFor="username">
          Username
        </label>
        <input
          id="username"
          autoComplete="username"
          required
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          className="mb-4 w-full rounded border border-black/20 px-3 py-2 dark:border-white/20 dark:bg-neutral-800"
        />

        <label className="mb-1 block text-sm font-medium" htmlFor="password">
          Password
        </label>
        <input
          id="password"
          type="password"
          autoComplete="current-password"
          required
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          className="mb-4 w-full rounded border border-black/20 px-3 py-2 dark:border-white/20 dark:bg-neutral-800"
        />

        {error && (
          <p role="alert" className="mb-4 text-sm font-medium text-zbright">
            {error}
          </p>
        )}

        <button
          type="submit"
          disabled={busy}
          className="w-full rounded bg-zred py-2 font-semibold text-white hover:bg-zbright disabled:opacity-50"
        >
          {busy ? "Signing in…" : "Sign in"}
        </button>
      </form>
    </div>
  );
}

"use client";

import { useRef, type KeyboardEvent } from "react";

const DOUBLE_SUBMIT_GUARD_MS = 500;

/**
 * Full-width barcode/serial input for the kiosk. Keeps focus persistently
 * (re-focuses on blur) and submits on Enter, guarded against double submits
 * within 500 ms (scanners often fire twice).
 */
export default function ScanInput({
  onSubmit,
  disabled = false,
  placeholder = "Scan serial number…",
}: {
  onSubmit: (serialNumber: string) => void;
  disabled?: boolean;
  placeholder?: string;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const lastSubmitAtRef = useRef(0);

  function handleKeyDown(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key !== "Enter") return;
    const sn = e.currentTarget.value.trim();
    if (!sn) return;
    const now = Date.now();
    if (now - lastSubmitAtRef.current < DOUBLE_SUBMIT_GUARD_MS) {
      e.currentTarget.value = ""; // swallow the duplicate beep-read
      return;
    }
    lastSubmitAtRef.current = now;
    e.currentTarget.value = "";
    onSubmit(sn);
  }

  return (
    <input
      ref={inputRef}
      autoFocus
      disabled={disabled}
      placeholder={placeholder}
      aria-label="Serial number"
      className="w-full rounded-xl border-4 border-black/20 bg-white px-6 py-6 text-center text-4xl font-bold tracking-widest outline-none focus:border-zred dark:border-white/20 dark:bg-neutral-900"
      onBlur={(e) => e.currentTarget.focus()}
      onKeyDown={handleKeyDown}
    />
  );
}

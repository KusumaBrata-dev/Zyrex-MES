"use client";

import { useState } from "react";

/**
 * Collapsible AI assistant placeholder (bottom-right). The real assistant
 * ships in Plan 5 — this panel only communicates that.
 */
export default function AiChatPlaceholder() {
  const [open, setOpen] = useState(false);

  if (!open) {
    return (
      <button
        type="button"
        aria-label="Open AI Assistant"
        onClick={() => setOpen(true)}
        className="fixed bottom-6 right-6 z-40 h-14 w-14 rounded-full bg-zred text-lg font-bold text-white shadow-lg hover:bg-zbright"
      >
        AI
      </button>
    );
  }

  return (
    <div className="fixed bottom-6 right-6 z-40 flex w-80 flex-col overflow-hidden rounded-xl border border-black/10 bg-white shadow-2xl dark:border-white/10 dark:bg-neutral-900">
      <div className="flex items-center justify-between bg-zred px-4 py-2 text-white">
        <span className="font-semibold">AI Assistant</span>
        <button type="button" aria-label="Close AI Assistant" onClick={() => setOpen(false)} className="px-1 text-xl leading-none">
          ×
        </button>
      </div>
      <div className="p-4 text-sm text-neutral-600 dark:text-neutral-300">
        <p>AI Assistant will be available in Plan 5.</p>
      </div>
      <div className="flex items-center gap-2 border-t border-black/10 p-3 dark:border-white/10">
        <input
          disabled
          placeholder="Ask something…"
          className="flex-1 rounded border border-black/15 px-2 py-1 text-sm opacity-60 dark:border-white/15 dark:bg-neutral-800"
          aria-label="AI message"
        />
        <span className="rounded-full bg-neutral-200 px-2 py-0.5 text-xs font-semibold text-neutral-600 dark:bg-neutral-700 dark:text-neutral-300">
          PLAN 5
        </span>
      </div>
    </div>
  );
}

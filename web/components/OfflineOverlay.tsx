/**
 * Blocking full-screen overlay shown while the MES API is unreachable.
 * Covers every interaction (no dismiss) until connectivity returns.
 */
export default function OfflineOverlay() {
  return (
    <div
      data-testid="offline-overlay"
      className="fixed inset-0 z-[100] flex flex-col items-center justify-center gap-8 bg-zred text-white"
    >
      {/* wifi-off icon */}
      <svg
        xmlns="http://www.w3.org/2000/svg"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
        className="h-32 w-32 opacity-90"
        aria-hidden="true"
      >
        <line x1="1" y1="1" x2="23" y2="23" />
        <path d="M16.72 11.06A10.94 10.94 0 0 1 19 12.55" />
        <path d="M5 12.55a10.94 10.94 0 0 1 5.17-2.39" />
        <path d="M10.71 5.05A16 16 0 0 1 22.58 9" />
        <path d="M1.42 9a15.91 15.91 0 0 1 4.7-2.88" />
        <path d="M8.53 16.11a6 6 0 0 1 6.95 0" />
        <line x1="12" y1="20" x2="12.01" y2="20" />
      </svg>
      <p className="text-7xl font-black tracking-widest">SERVER OFFLINE</p>
      <p className="text-4xl font-bold">HUBUNGI LEADER</p>
    </div>
  );
}

import type { ReactNode } from "react";

export default function LogoHeader({
  stationName,
  children,
}: {
  stationName?: string;
  children?: ReactNode;
}) {
  const name = stationName ?? process.env.NEXT_PUBLIC_STATION_NAME ?? "Zyrex Kiosk";
  return (
    <header className="flex items-center gap-3 bg-zred px-6 py-3 text-white">
      <img src="/brand/logo-white.svg" alt="Zyrex" className="h-10 w-auto" />
      <span className="text-lg font-semibold tracking-wide">{name}</span>
      {children}
    </header>
  );
}

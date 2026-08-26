"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { isLoggedIn } from "@/lib/api";

/**
 * Placeholder for the kiosk scan screen (built in a later task). Guards the
 * auth shell: no token → back to /login.
 */
export default function ScanPage() {
  const router = useRouter();

  useEffect(() => {
    if (!isLoggedIn()) router.replace("/login");
  }, [router]);

  return (
    <div className="flex flex-1 items-center justify-center p-6 text-neutral-500">
      Scan screen — under construction.
    </div>
  );
}

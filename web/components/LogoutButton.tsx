"use client";

import { useRouter } from "next/navigation";
import { logout } from "@/lib/api";

export default function LogoutButton() {
  const router = useRouter();
  return (
    <button
      type="button"
      onClick={() => {
        logout();
        router.replace("/login");
      }}
      className="ml-auto rounded border border-white/40 px-3 py-1 text-sm hover:bg-white/10"
    >
      Logout
    </button>
  );
}

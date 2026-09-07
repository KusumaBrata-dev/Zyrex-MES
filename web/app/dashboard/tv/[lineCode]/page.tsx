"use client";

import { useParams } from "next/navigation";
import TvStats from "@/components/dashboard/TvStats";

export default function TvPage() {
  const params = useParams<{ lineCode: string }>();
  return <TvStats lineCode={params.lineCode} />;
}
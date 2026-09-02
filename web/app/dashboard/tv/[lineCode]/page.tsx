"use client";

import { useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { authHeaders } from "@/lib/api";
import TvStats from "@/components/dashboard/TvStats";

const API = process.env.NEXT_PUBLIC_API_BASE ?? "";

interface TvPageProps {
  params: Promise<{ lineCode: string }>;
}

export default function TvPage({ params }: { params: Promise<{ lineCode: string }> }) {
  const resolvedParams = await params;
  return <TvStats lineCode={resolvedParams.lineCode} />;
}

export default function TvPage({ params }: { params: Promise<{ lineCode: string }> }) {
  const resolvedParams = await params;
  return <TvStats lineCode={resolvedParams.lineCode} />;
}
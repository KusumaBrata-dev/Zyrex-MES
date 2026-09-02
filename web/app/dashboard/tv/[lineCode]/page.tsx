import TvStats from "@/components/dashboard/TvStats";

export default async function TvPage({ params }: { params: Promise<{ lineCode: string }> }) {
  const { lineCode } = await params;
  return <TvStats lineCode={lineCode} />;
}

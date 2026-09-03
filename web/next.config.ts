import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Dev proxy: /api/* on the Next dev server is forwarded to the MES API so
  // the kiosk can run same-origin (NEXT_PUBLIC_API_BASE=""). API_ORIGIN
  // selects the backend; production deployments typically set
  // NEXT_PUBLIC_API_BASE to the absolute API URL instead.
  async rewrites() {
    return [
      {
        source: "/api/:path*",
        destination: `${process.env.API_ORIGIN ?? "http://localhost:9090"}/api/:path*`,
      },
      {
        // Kiosk heartbeat (useServerHeartbeat) pings same-origin /health.
        source: "/health",
        destination: `${process.env.API_ORIGIN ?? "http://localhost:9090"}/health`,
      },
      {
        // SignalR hub (useLiveEvents) — LongPolling only; dev rewrites can't
        // carry websocket upgrades.
        source: "/hubs/:path*",
        destination: `${process.env.API_ORIGIN ?? "http://localhost:9090"}/hubs/:path*`,
      },
    ];
  },
};

export default nextConfig;

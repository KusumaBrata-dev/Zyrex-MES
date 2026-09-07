import type { Metadata } from "next";
import { Inter, JetBrains_Mono } from "next/font/google";
import LogoHeader from "@/components/LogoHeader";
import LogoutButton from "@/components/LogoutButton";
import OfflineGate from "@/components/OfflineGate";
import "./globals.css";

const inter = Inter({
  subsets: ["latin"],
  variable: "--font-inter",
  display: "swap",
});

const jetbrainsMono = JetBrains_Mono({
  subsets: ["latin"],
  variable: "--font-jetbrains-mono",
  display: "swap",
});

export const metadata: Metadata = {
  title: "Zyrex MES — Production Dashboard",
  description: "PT Zyrexindo Mandiri Buana Tbk — Manufacturing Execution System",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html
      lang="en"
      className={`${inter.variable} ${jetbrainsMono.variable}`}
      suppressHydrationWarning
    >
      <body className="flex min-h-full flex-col bg-zyrex-black text-foreground antialiased">
        <OfflineGate />
        <LogoHeader />
        <main className="flex flex-1 flex-col">{children}</main>
      </body>
    </html>
  );
}
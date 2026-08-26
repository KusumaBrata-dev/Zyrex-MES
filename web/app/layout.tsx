import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import LogoHeader from "@/components/LogoHeader";
import LogoutButton from "@/components/LogoutButton";
import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "Zyrex Kiosk",
  description: "Zyrexindo MES production kiosk",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html
      lang="en"
      className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}
    >
      <body className="flex min-h-full flex-col">
        <LogoHeader>
          <LogoutButton />
        </LogoHeader>
        <main className="flex flex-1 flex-col">{children}</main>
      </body>
    </html>
  );
}

import type { Metadata } from "next";
import { DM_Sans, Fraunces } from "next/font/google";
import Link from "next/link";
import "./globals.css";

const sans = DM_Sans({
  subsets: ["latin"],
  variable: "--font-sans",
});

const display = Fraunces({
  subsets: ["latin"],
  variable: "--font-display",
});

export const metadata: Metadata = {
  title: "Content Writer",
  description: "Template → generate → edit → save",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body className={`${sans.variable} ${display.variable}`}>
        <div className="shell">
          <header className="topbar">
            <Link href="/" className="brand">
              Content Writer
            </Link>
            <nav className="nav">
              <Link href="/templates">Templates</Link>
              <Link href="/documents">Documents</Link>
              <Link href="/brand-voices">Brand Voices</Link>
              <Link href="/usage">Usage</Link>
            </nav>
          </header>
          <main className="main">{children}</main>
        </div>
      </body>
    </html>
  );
}

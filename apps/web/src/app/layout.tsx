import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "NovaCMS",
  description: "NovaCMS public website",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="tr">
      <body>{children}</body>
    </html>
  );
}

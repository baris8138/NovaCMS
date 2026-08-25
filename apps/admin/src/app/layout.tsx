import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "NovaCMS Admin",
  description: "NovaCMS yönetim paneli",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="tr">
      <body>{children}</body>
    </html>
  );
}

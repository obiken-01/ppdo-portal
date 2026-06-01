/**
 * Public landing page — RAL-41 (updated)
 *
 * Layout:
 *   Navbar → Hero (green, Mission/Vision carousel) → Announcements (white) → Footer
 */

import Link from "next/link";
import Navbar from "@/components/landing/Navbar";
import Footer from "@/components/landing/Footer";
import MissionVisionCarousel from "@/components/landing/MissionVisionCarousel";

export default function LandingPage() {
  return (
    <div className="min-h-screen flex flex-col font-sans">
      <Navbar />

      <main className="flex-1">
        {/* ── Hero — green section with carousel ──────────────────────────── */}
        <section className="bg-green-700 text-white">
          <div className="max-w-6xl mx-auto px-6 py-10">
            {/* 3-column logo header */}
            <div className="flex items-center justify-between gap-4 mb-8">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src="/images/Ph_seal_occidental_mindoro.png"
                alt="Province of Occidental Mindoro Official Seal"
                width={72}
                height={72}
                className="object-contain flex-shrink-0"
              />
              <div className="flex flex-col items-center text-center gap-1 flex-1 min-w-0">
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img
                  src="/images/ppdo-logo-placeholder.png"
                  alt="PPDO Logo"
                  width={60}
                  height={60}
                  className="object-contain"
                />
                <h1 className="text-lg md:text-xl font-bold leading-tight">
                  Provincial Planning and Development Office
                </h1>
                <p className="text-green-200 text-xs">
                  Province of Occidental Mindoro, Philippines
                </p>
              </div>
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src="/images/Bagong_Pilipinas_logo.png"
                alt="Bagong Pilipinas"
                width={72}
                height={72}
                className="object-contain flex-shrink-0"
              />
            </div>

            {/* Mission / Vision carousel */}
            <MissionVisionCarousel />

            {/* CTA below carousel */}
            <div className="text-center mt-8">
              <Link
                href="/login"
                className="inline-block bg-white text-green-700 font-bold px-8 py-2.5 rounded-lg
                           hover:bg-green-50 transition-colors focus:outline-none focus:ring-2 focus:ring-white"
              >
                Sign In to Portal
              </Link>
            </div>
          </div>
        </section>

        {/* ── Announcements — white section ────────────────────────────────── */}
        <section className="bg-slate-100 flex-1">
          <div className="max-w-6xl mx-auto px-6 py-10">
            <h2 className="text-xl font-bold text-slate-800 mb-5">
              Announcements
            </h2>

            {/* Empty state */}
            <div className="bg-white rounded-lg border border-slate-200 px-8 py-14 text-center">
              <div className="mx-auto mb-4 w-12 h-12 rounded-full bg-slate-100 flex items-center justify-center">
                <MegaphoneIcon />
              </div>
              <p className="text-slate-600 font-medium">No announcements yet</p>
              <p className="text-slate-400 text-sm mt-1">
                Check back later for updates from the office.
              </p>
            </div>
          </div>
        </section>
      </main>

      <Footer />
    </div>
  );
}

function MegaphoneIcon() {
  return (
    <svg
      className="w-6 h-6 text-slate-400"
      fill="none"
      stroke="currentColor"
      viewBox="0 0 24 24"
      aria-hidden="true"
    >
      <path
        strokeLinecap="round"
        strokeLinejoin="round"
        strokeWidth={1.5}
        d="M11 5.882V19.24a1.76 1.76 0 01-3.417.592l-2.147-6.15M18 13a3 3 0 100-6M5.436 13.683A4.001 4.001 0 017 6h1.832c4.1 0 7.625-1.234 9.168-3v14c-1.543-1.766-5.067-3-9.168-3H7a3.988 3.988 0 01-1.564-.317z"
      />
    </svg>
  );
}

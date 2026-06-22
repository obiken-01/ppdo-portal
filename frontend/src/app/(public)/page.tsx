/**
 * Public landing page — RAL-41
 *
 * Layout:
 *   Navbar → Hero (green, fills viewport, Mission/Vision carousel)
 *          → Announcements (white) → Footer
 */

import Navbar from "@/components/landing/Navbar";
import Footer from "@/components/landing/Footer";
import MissionVisionCarousel from "@/components/landing/MissionVisionCarousel";
import AnnouncementsSection from "@/components/landing/AnnouncementsSection";

export default function LandingPage() {
  return (
    <div className="min-h-screen flex flex-col font-sans">
      <Navbar />

      <main className="flex-1">
        {/* ── Hero — fills exactly the remaining viewport height ───────────
            h-[calc(100vh-3.5rem)] accounts for the 56px (3.5rem) navbar.
            flex-col + justify-between distributes:
              top: logo header  |  middle: carousel  |  bottom: scroll cue  */}
        <section
          className="bg-green-700 text-white flex flex-col"
          style={{ minHeight: "calc(100vh - 3.5rem)" }}
        >
          <div className="max-w-6xl mx-auto px-6 py-8 w-full flex flex-col flex-1 justify-between gap-6">

            {/* 3-column logo header */}
            <div className="flex items-center justify-between gap-4">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src="/images/Ph_seal_occidental_mindoro.png"
                alt="Province of Occidental Mindoro Official Seal"
                width={90}
                height={90}
                className="object-contain flex-shrink-0"
              />
              <div className="flex flex-col items-center text-center gap-1 flex-1 min-w-0">
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img
                  src="/images/ppdo-logo-placeholder.png"
                  alt="PPDO Logo"
                  width={56}
                  height={56}
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
                width={90}
                height={90}
                className="object-contain flex-shrink-0"
              />
            </div>

            {/* Carousel — vertically centered in the remaining space */}
            <div className="flex-1 flex items-center justify-center">
              <MissionVisionCarousel />
            </div>

            {/* Scroll-down indicator */}
            <div className="flex flex-col items-center pb-2 text-white/60">
              <span className="text-xs uppercase tracking-widest mb-1">Scroll</span>
              <svg
                className="w-6 h-6 animate-bounce"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
                aria-hidden="true"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth={2}
                  d="M19 9l-7 7-7-7"
                />
              </svg>
            </div>

          </div>
        </section>

        <AnnouncementsSection />
      </main>

      <Footer />
    </div>
  );
}


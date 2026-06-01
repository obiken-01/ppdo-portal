/**
 * Public landing page — RAL-41
 * Matches "01 Landing" Penpot frame.
 *
 * Sections (top → bottom):
 *   1. Hero — logo header, PPDO name, tagline, sign-in CTA
 *   2. Mission & Vision — two side-by-side cards, stacked on mobile
 *   3. Announcements — empty state until Admin creates posts
 *   4. Bottom CTA
 *
 * Design tokens from PPDO_PROJECT_CONTEXT.md Section 8 / tailwind.config.ts.
 * Images in /public/images/ — replace real logos before production deploy.
 */

import Link from "next/link";

export default function LandingPage() {
  return (
    <main className="min-h-screen bg-slate-100 font-sans">
      {/* ── 1. Hero ──────────────────────────────────────────────────────── */}
      <section className="bg-green-700 text-white">
        <div className="max-w-5xl mx-auto px-6 py-10">
          {/* Three-column logo header — matches official Philippine govt layout */}
          <div className="flex items-center justify-between gap-4 mb-8">
            {/* Left — Province seal */}
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              src="/images/Ph_seal_occidental_mindoro.png"
              alt="Province of Occidental Mindoro Official Seal"
              width={80}
              height={80}
              className="object-contain flex-shrink-0"
            />

            {/* Center — PPDO logo + name */}
            <div className="flex flex-col items-center text-center gap-2 flex-1 min-w-0">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src="/images/ppdo-logo-placeholder.png"
                alt="PPDO Logo"
                width={72}
                height={72}
                className="object-contain"
              />
              <h1 className="text-xl md:text-2xl font-bold leading-tight">
                Provincial Planning and Development Office
              </h1>
              <p className="text-green-200 text-sm">
                Province of Occidental Mindoro, Philippines
              </p>
            </div>

            {/* Right — Bagong Pilipinas */}
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              src="/images/Bagong_Pilipinas_logo.png"
              alt="Bagong Pilipinas"
              width={80}
              height={80}
              className="object-contain flex-shrink-0"
            />
          </div>

          {/* Tagline + primary CTA */}
          <div className="text-center pb-4">
            <p className="text-green-100 text-lg mb-6">
              Staff portal for inventory monitoring, records management, and
              office coordination.
            </p>
            <Link
              href="/login"
              className="inline-block bg-white text-green-700 font-bold text-base px-8 py-3 rounded-lg
                         hover:bg-green-50 transition-colors focus:outline-none focus:ring-2 focus:ring-white"
            >
              Sign In to Portal
            </Link>
          </div>
        </div>
      </section>

      {/* ── 2. Mission & Vision ──────────────────────────────────────────── */}
      <section className="max-w-5xl mx-auto px-6 py-12">
        <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
          {/* Mission Card */}
          <MissionVisionCard title="MISSION">
            <p className="text-slate-700 leading-relaxed text-center italic">
              &ldquo;To be an{" "}
              <span className="text-red-600 not-italic font-semibold">
                effective
              </span>{" "}
              and{" "}
              <span className="text-red-600 not-italic font-semibold">
                efficient
              </span>{" "}
              department in helping the LGU attain its goals and thrust and
              provide better quality service.&rdquo;
            </p>
          </MissionVisionCard>

          {/* Vision Card */}
          <MissionVisionCard title="VISION">
            <p className="text-slate-700 leading-relaxed text-center italic">
              &ldquo;Occidental Mindoro{" "}
              <span className="text-red-600 not-italic font-semibold">
                PPDO
              </span>{" "}
              is an organization handled by competent, people-oriented,
              committed, proactive and innovative staff equipped with updated
              capabilities to generate and utilize a vast array of information
              and technology to propose to stakeholders appropriate
              socio-economic, physical, cultural and environmental development
              frameworks and able to work harmoniously with other local and
              national government functionaries towards the provincial
              government&apos;s mandate.&rdquo;
            </p>
          </MissionVisionCard>
        </div>
      </section>

      {/* ── 3. Announcements ─────────────────────────────────────────────── */}
      <section className="max-w-5xl mx-auto px-6 pb-12">
        <h2 className="text-xl font-bold text-slate-800 mb-5">
          Announcements
        </h2>

        {/* Empty state — replace with real posts once API is wired */}
        <div className="bg-white rounded-lg border border-slate-200 px-8 py-14 text-center">
          <div className="mx-auto mb-4 w-12 h-12 rounded-full bg-slate-100 flex items-center justify-center">
            <MegaphoneIcon />
          </div>
          <p className="text-slate-600 font-medium">No announcements yet</p>
          <p className="text-slate-400 text-sm mt-1">
            Check back later for updates from the office.
          </p>
        </div>
      </section>

      {/* ── 4. Bottom CTA ────────────────────────────────────────────────── */}
      <section className="bg-green-700 text-white py-10">
        <div className="max-w-5xl mx-auto px-6 text-center">
          <h2 className="text-xl font-semibold mb-2">Ready to get started?</h2>
          <p className="text-green-200 mb-6">
            Sign in with your PPDO staff account to access the portal.
          </p>
          <Link
            href="/login"
            className="inline-block bg-white text-green-700 font-bold text-base px-8 py-3 rounded-lg
                       hover:bg-green-50 transition-colors focus:outline-none focus:ring-2 focus:ring-white"
          >
            Sign In to Portal
          </Link>
        </div>
      </section>
    </main>
  );
}

// ── Sub-components ────────────────────────────────────────────────────────────

/**
 * Card shared by Mission and Vision.
 * Header: 3-column layout — Province seal | title | Bagong Pilipinas logo.
 * Matches the official PPDO slide deck header layout.
 */
function MissionVisionCard({
  title,
  children,
}: {
  title: "MISSION" | "VISION";
  children: React.ReactNode;
}) {
  return (
    <div className="bg-white rounded-lg shadow-sm border border-slate-200 overflow-hidden">
      {/* Card header — 3-column logo row */}
      <div className="grid grid-cols-3 items-center gap-2 px-4 py-3 border-b border-slate-200">
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img
          src="/images/Ph_seal_occidental_mindoro.png"
          alt="Province Seal"
          width={48}
          height={48}
          className="object-contain justify-self-start"
        />
        <h2 className="text-center font-bold text-lg underline underline-offset-2 text-slate-800 tracking-wide">
          {title}
        </h2>
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img
          src="/images/Bagong_Pilipinas_logo.png"
          alt="Bagong Pilipinas"
          width={48}
          height={48}
          className="object-contain justify-self-end"
        />
      </div>

      {/* Card body */}
      <div className="px-6 py-6">{children}</div>
    </div>
  );
}

/** Simple megaphone SVG for the announcements empty state. */
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

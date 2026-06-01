"use client";

import { useState, useEffect, useCallback } from "react";

/** Auto-advance interval in ms. */
const AUTO_ADVANCE_MS = 5000;

/**
 * Carousel that cycles between the MISSION and VISION cards.
 *
 * Both cards share the SAME fixed outer height (h-72 = 288px) so the
 * layout never shifts when switching slides:
 *   - Mission text is short → vertically centered with white space.
 *   - Vision text is longer → fits at text-sm with relaxed leading.
 *
 * Auto-advances every 5 s; pauses on manual interaction, resumes after 10 s.
 */
export default function MissionVisionCarousel() {
  const [active, setActive]   = useState(0);
  const [visible, setVisible] = useState(true);
  const [paused, setPaused]   = useState(false);

  const goTo = useCallback((index: number) => {
    setVisible(false);
    setTimeout(() => {
      setActive(index);
      setVisible(true);
    }, 220);
  }, []);

  const next = useCallback(() => {
    goTo((active + 1) % 2);
  }, [active, goTo]);

  const prev = useCallback(() => {
    goTo((active - 1 + 2) % 2);
  }, [active, goTo]);

  useEffect(() => {
    if (paused) return;
    const id = setInterval(next, AUTO_ADVANCE_MS);
    return () => clearInterval(id);
  }, [paused, next]);

  const handleManual = (action: () => void) => {
    setPaused(true);
    action();
    const t = setTimeout(() => setPaused(false), 10_000);
    return () => clearTimeout(t);
  };

  return (
    <div className="relative max-w-2xl mx-auto w-full select-none">
      {/* ── Card — fixed height, same for both slides ──────────────────── */}
      {/* h-72 = 288px.  Header ≈ 60px, body ≈ 228px.
          Vision text (text-sm leading-relaxed ≈ 22px/line × 9 lines ≈ 198px)
          fits comfortably with py-5 (40px) padding. */}
      <div className="bg-white rounded-xl shadow-md overflow-hidden h-72">
        {/* Title header */}
        <div className="px-8 pt-5 pb-3 border-b border-slate-200 text-center">
          <h2 className="font-bold text-xl underline underline-offset-4 text-slate-800 tracking-widest">
            {active === 0 ? "MISSION" : "VISION"}
          </h2>
        </div>

        {/* Body — fixed remaining height, content centered */}
        <div
          className="px-10 flex items-center justify-center"
          style={{
            height: "calc(288px - 60px)", /* card height minus header */
            opacity: visible ? 1 : 0,
            transition: "opacity 220ms ease-in-out",
          }}
        >
          {active === 0 ? <MissionText /> : <VisionText />}
        </div>
      </div>

      {/* Prev arrow */}
      <button
        onClick={() => handleManual(prev)}
        aria-label="Previous slide"
        className="absolute -left-5 top-[144px] -translate-y-1/2 w-10 h-10 rounded-full
                   bg-white/90 shadow flex items-center justify-center text-green-700
                   hover:bg-white transition-colors focus:outline-none focus:ring-2 focus:ring-white"
      >
        <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden>
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
        </svg>
      </button>

      {/* Next arrow */}
      <button
        onClick={() => handleManual(next)}
        aria-label="Next slide"
        className="absolute -right-5 top-[144px] -translate-y-1/2 w-10 h-10 rounded-full
                   bg-white/90 shadow flex items-center justify-center text-green-700
                   hover:bg-white transition-colors focus:outline-none focus:ring-2 focus:ring-white"
      >
        <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden>
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
        </svg>
      </button>

      {/* Dot indicators */}
      <div className="flex justify-center gap-2 mt-4">
        {[0, 1].map((i) => (
          <button
            key={i}
            onClick={() => handleManual(() => goTo(i))}
            aria-label={`Go to slide ${i + 1}`}
            className={`h-2 rounded-full transition-all duration-300 focus:outline-none ${
              i === active ? "w-6 bg-white" : "w-2.5 bg-white/40 hover:bg-white/70"
            }`}
          />
        ))}
      </div>
    </div>
  );
}

function MissionText() {
  return (
    <p className="text-slate-700 leading-relaxed text-center italic text-sm">
      &ldquo;To be an{" "}
      <span className="text-red-600 not-italic font-semibold">effective</span>{" "}
      and{" "}
      <span className="text-red-600 not-italic font-semibold">efficient</span>{" "}
      department in helping the LGU attain its goals and thrust and provide
      better quality service.&rdquo;
    </p>
  );
}

function VisionText() {
  return (
    <p className="text-slate-700 leading-relaxed text-center italic text-sm">
      &ldquo;Occidental Mindoro{" "}
      <span className="text-red-600 not-italic font-semibold">PPDO</span> is an
      organization handled by competent, people-oriented, committed, proactive
      and innovative staff equipped with updated capabilities to generate and
      utilize a vast array of information and technology to propose to
      stakeholders appropriate socio-economic, physical, cultural and
      environmental development frameworks and able to work harmoniously with
      other local and national government functionaries towards the provincial
      government&apos;s mandate.&rdquo;
    </p>
  );
}

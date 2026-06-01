"use client";

import { useState, useEffect, useCallback } from "react";

type Slide = {
  title: "MISSION" | "VISION";
  body: React.ReactNode;
};

const SLIDES: Slide[] = [
  {
    title: "MISSION",
    body: (
      <p className="text-slate-700 leading-relaxed text-center italic text-base">
        &ldquo;To be an{" "}
        <span className="text-red-600 not-italic font-semibold">effective</span>{" "}
        and{" "}
        <span className="text-red-600 not-italic font-semibold">efficient</span>{" "}
        department in helping the LGU attain its goals and thrust and provide
        better quality service.&rdquo;
      </p>
    ),
  },
  {
    title: "VISION",
    body: (
      <p className="text-slate-700 leading-relaxed text-center italic text-base">
        &ldquo;Occidental Mindoro{" "}
        <span className="text-red-600 not-italic font-semibold">PPDO</span> is
        an organization handled by competent, people-oriented, committed,
        proactive and innovative staff equipped with updated capabilities to
        generate and utilize a vast array of information and technology to
        propose to stakeholders appropriate socio-economic, physical, cultural
        and environmental development frameworks and able to work harmoniously
        with other local and national government functionaries towards the
        provincial government&apos;s mandate.&rdquo;
      </p>
    ),
  },
];

/** Auto-advance interval in ms. */
const AUTO_ADVANCE_MS = 5000;

/**
 * Carousel that cycles between the MISSION and VISION cards.
 *
 * - No logos inside the cards — clean title-only header.
 * - Both cards share the same fixed height so the layout never shifts.
 * - Smooth fade transition between slides.
 * - Auto-advances every 5 s; pauses when the user manually navigates and
 *   resumes after 10 s of inactivity.
 */
export default function MissionVisionCarousel() {
  const [active, setActive]     = useState(0);
  const [visible, setVisible]   = useState(true); // controls fade
  const [paused, setPaused]     = useState(false);

  // Fade-transition helper: fade out → swap → fade in
  const goTo = useCallback((index: number) => {
    setVisible(false);
    setTimeout(() => {
      setActive(index);
      setVisible(true);
    }, 250); // matches CSS transition duration below
  }, []);

  const next = useCallback(() => {
    goTo((active + 1) % SLIDES.length);
  }, [active, goTo]);

  const prev = useCallback(() => {
    goTo((active - 1 + SLIDES.length) % SLIDES.length);
  }, [active, goTo]);

  // Auto-advance
  useEffect(() => {
    if (paused) return;
    const id = setInterval(next, AUTO_ADVANCE_MS);
    return () => clearInterval(id);
  }, [paused, next]);

  // Resume auto-advance 10 s after the user last interacted
  const handleManual = (action: () => void) => {
    setPaused(true);
    action();
    const resume = setTimeout(() => setPaused(false), 10_000);
    return () => clearTimeout(resume);
  };

  const slide = SLIDES[active];

  return (
    <div className="relative max-w-2xl mx-auto select-none">
      {/* Card — fixed height so both slides occupy the same space */}
      <div
        className="bg-white rounded-xl shadow-md overflow-hidden"
        style={{ minHeight: "220px" }}
      >
        {/* Header — title only, no logos */}
        <div className="px-8 pt-6 pb-3 text-center border-b border-slate-200">
          <h2 className="font-bold text-2xl underline underline-offset-4 text-slate-800 tracking-widest">
            {slide.title}
          </h2>
        </div>

        {/* Body with fade transition */}
        <div
          className="px-10 py-8 flex items-center justify-center"
          style={{
            opacity: visible ? 1 : 0,
            transition: "opacity 250ms ease-in-out",
            minHeight: "154px", // (220px total) - (66px header) = fixed body height
          }}
        >
          {slide.body}
        </div>
      </div>

      {/* Previous arrow */}
      <button
        onClick={() => handleManual(prev)}
        aria-label="Previous slide"
        className="absolute -left-5 top-1/2 -translate-y-1/2 w-10 h-10 rounded-full
                   bg-white/90 shadow flex items-center justify-center text-green-700
                   hover:bg-white transition-colors focus:outline-none focus:ring-2 focus:ring-white"
      >
        <ChevronLeft />
      </button>

      {/* Next arrow */}
      <button
        onClick={() => handleManual(next)}
        aria-label="Next slide"
        className="absolute -right-5 top-1/2 -translate-y-1/2 w-10 h-10 rounded-full
                   bg-white/90 shadow flex items-center justify-center text-green-700
                   hover:bg-white transition-colors focus:outline-none focus:ring-2 focus:ring-white"
      >
        <ChevronRight />
      </button>

      {/* Dot indicators + progress bar */}
      <div className="flex flex-col items-center gap-2 mt-4">
        <div className="flex gap-2">
          {SLIDES.map((_, i) => (
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
    </div>
  );
}

function ChevronLeft() {
  return (
    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden>
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
    </svg>
  );
}

function ChevronRight() {
  return (
    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden>
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
    </svg>
  );
}

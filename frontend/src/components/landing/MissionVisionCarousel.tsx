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
      <p className="text-slate-700 leading-relaxed text-center italic">
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
      <p className="text-slate-700 leading-relaxed text-center italic">
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

const AUTO_ADVANCE_MS = 6000;

/**
 * Carousel that cycles between the MISSION and VISION cards.
 * Each card keeps the official 3-column header layout from the PPDO slide deck:
 *   Province seal | bold underlined title | Bagong Pilipinas logo
 *
 * Auto-advances every 6 s; pauses on user interaction.
 */
export default function MissionVisionCarousel() {
  const [active, setActive] = useState(0);
  const [paused, setPaused] = useState(false);

  const next = useCallback(() => {
    setActive((i) => (i + 1) % SLIDES.length);
  }, []);

  const prev = useCallback(() => {
    setActive((i) => (i - 1 + SLIDES.length) % SLIDES.length);
  }, []);

  // Auto-advance unless the user has interacted
  useEffect(() => {
    if (paused) return;
    const id = setInterval(next, AUTO_ADVANCE_MS);
    return () => clearInterval(id);
  }, [paused, next]);

  const handleManual = (action: () => void) => {
    setPaused(true);
    action();
  };

  const slide = SLIDES[active];

  return (
    <div className="relative max-w-2xl mx-auto select-none">
      {/* Card */}
      <div className="bg-white rounded-xl shadow-md overflow-hidden">
        {/* 3-column card header — Province seal | title | Bagong Pilipinas */}
        <div className="grid grid-cols-3 items-center gap-2 px-5 py-3 border-b border-slate-200">
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src="/images/Ph_seal_occidental_mindoro.png"
            alt="Province Seal"
            width={48}
            height={48}
            className="object-contain justify-self-start"
          />
          <h2 className="text-center font-bold text-xl underline underline-offset-2 text-slate-800 tracking-wider">
            {slide.title}
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
        <div className="px-8 py-7 min-h-[140px] flex items-center justify-center">
          {slide.body}
        </div>
      </div>

      {/* Previous arrow */}
      <button
        onClick={() => handleManual(prev)}
        aria-label="Previous slide"
        className="absolute -left-5 top-1/2 -translate-y-1/2 w-10 h-10 rounded-full bg-white/90
                   shadow flex items-center justify-center text-green-700 hover:bg-white
                   transition-colors focus:outline-none focus:ring-2 focus:ring-white"
      >
        <ChevronLeft />
      </button>

      {/* Next arrow */}
      <button
        onClick={() => handleManual(next)}
        aria-label="Next slide"
        className="absolute -right-5 top-1/2 -translate-y-1/2 w-10 h-10 rounded-full bg-white/90
                   shadow flex items-center justify-center text-green-700 hover:bg-white
                   transition-colors focus:outline-none focus:ring-2 focus:ring-white"
      >
        <ChevronRight />
      </button>

      {/* Dot indicators */}
      <div className="flex justify-center gap-2 mt-4">
        {SLIDES.map((_, i) => (
          <button
            key={i}
            onClick={() => handleManual(() => setActive(i))}
            aria-label={`Go to slide ${i + 1}`}
            className={`w-2.5 h-2.5 rounded-full transition-colors focus:outline-none ${
              i === active ? "bg-white" : "bg-white/40 hover:bg-white/70"
            }`}
          />
        ))}
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

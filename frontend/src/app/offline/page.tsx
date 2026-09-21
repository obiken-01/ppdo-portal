"use client";

/**
 * Offline fallback — served by the service worker when a navigation fails and
 * nothing for that URL is in the cache (see public/sw.js `handleNavigation`).
 *
 * Deliberately self-contained: no API calls, no auth, no shared providers.
 * It has to render from cache alone, so anything it depended on would have to
 * be cached too.
 *
 * This is NOT the same thing as /reconnecting. That page handles "the session
 * could not be refreshed" — usually an Azure Functions cold start, sometimes a
 * genuinely offline device. This one handles "the page itself could not be
 * fetched at all", which only happens with no connection and no cached copy.
 */

import Image from "next/image";

export default function OfflinePage() {
  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-100 px-4 font-sans">
      <div className="w-full max-w-sm bg-white border border-slate-200 px-8 py-8 text-center">
        <Image
          src="/icons/icon-192.png"
          alt="PPDO Portal"
          width={64}
          height={64}
          className="mx-auto mb-5 object-contain"
        />

        <h1 className="text-lg font-bold text-slate-800 mb-2">You&apos;re offline</h1>

        <p className="text-sm text-slate-600 mb-1">
          This page isn&apos;t available without an internet connection.
        </p>
        <p className="text-xs text-slate-600 mb-6">
          Pages you&apos;ve already opened will still load. Anything needing live
          data from the server will wait until you&apos;re reconnected.
        </p>

        <button
          type="button"
          onClick={() => window.location.reload()}
          className="w-full bg-green-600 text-white font-semibold py-2.5 text-sm
                     hover:bg-green-500 active:bg-green-700 transition-colors
                     focus:outline-none focus:ring-2 focus:ring-green-600 focus:ring-offset-2"
        >
          Try again
        </button>
      </div>
    </div>
  );
}

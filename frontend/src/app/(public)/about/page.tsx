import Link from "next/link";
import Navbar from "@/components/landing/Navbar";
import Footer from "@/components/landing/Footer";

export default function AboutPage() {
  return (
    <div className="min-h-screen flex flex-col font-sans">
      <Navbar />
      <main className="flex-1 bg-slate-100">
        <div className="max-w-6xl mx-auto px-6 py-16 text-center">
          <h1 className="text-3xl font-bold text-slate-800 mb-3">About PPDO</h1>
          <p className="text-slate-500 mb-8">
            The Provincial Planning and Development Office of Occidental Mindoro.
          </p>
          <div className="inline-block bg-white border border-slate-200 rounded-xl px-12 py-10 text-slate-400">
            <p className="text-sm font-medium">Content coming soon</p>
            <p className="text-xs mt-1">This page will be updated in a future release.</p>
          </div>
          <div className="mt-8">
            <Link href="/" className="text-green-700 text-sm hover:underline">
              ← Back to home
            </Link>
          </div>
        </div>
      </main>
      <Footer />
    </div>
  );
}

/**
 * Public site footer — dark strip with copyright and portal version.
 * Matches the Penpot "01 Landing" frame footer.
 */
import { APP_VERSION } from "@/lib/version";

export default function Footer() {
  return (
    <footer className="bg-slate-800 text-slate-400">
      <div className="max-w-6xl mx-auto px-6 py-4 flex items-center justify-between text-sm">
        <span>
          &copy; 2026 Provincial Government of Occidental Mindoro &mdash; PPDO
        </span>
        <span className="text-slate-600">Portal {APP_VERSION}</span>
      </div>
    </footer>
  );
}

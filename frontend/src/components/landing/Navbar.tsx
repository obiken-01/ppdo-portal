import Link from "next/link";
import { APP_NAME, OFFICE_LOGO_WEBP } from "@/lib/branding";

const navLinks = [
  { href: "/about",    label: "About"    },
  { href: "/services", label: "Services" },
  { href: "/contact",  label: "Contact"  },
];

/**
 * Public site navbar — used on landing, about, services, contact pages.
 * Not used on the login page (standalone screen).
 */
export default function Navbar() {
  return (
    <nav className="bg-green-800 text-white shadow-sm sticky top-0 z-50">
      <div className="max-w-6xl mx-auto px-6 h-14 flex items-center justify-between gap-4">
        {/* Logo + site name */}
        <Link
          href="/"
          className="flex items-center gap-2 font-bold text-base hover:text-green-200 transition-colors flex-shrink-0"
        >
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src={OFFICE_LOGO_WEBP}
            alt="OPPDC"
            width={40}
            height={40}
            className="object-contain"
          />
          <span>{APP_NAME}</span>
          <span className="hidden sm:inline text-xs text-green-300 font-normal ml-0.5">
            Occidental Mindoro
          </span>
        </Link>

        {/* Nav links + Sign In */}
        <div className="flex items-center gap-5 text-sm">
          {navLinks.map((link) => (
            <Link
              key={link.href}
              href={link.href}
              className="hidden md:inline hover:text-green-200 transition-colors"
            >
              {link.label}
            </Link>
          ))}
          <Link
            href="/login"
            className="bg-white text-green-800 font-semibold text-sm px-4 py-1.5 rounded-md
                       hover:bg-green-50 transition-colors focus:outline-none focus:ring-2 focus:ring-white"
          >
            Sign In
          </Link>
        </div>
      </div>
    </nav>
  );
}

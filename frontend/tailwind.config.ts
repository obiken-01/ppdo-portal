import type { Config } from "tailwindcss";

/**
 * PPDO Portal — Tailwind Design Tokens
 * Sources:
 *   - CLAUDE.md § "Tailwind Design Tokens"
 *   - PPDO_PROJECT_CONTEXT.md § 8 "Design System"
 *   - shadcn/ui CSS-variable integration
 */
const config: Config = {
  darkMode: ["class"],
  content: [
    "./src/pages/**/*.{js,ts,jsx,tsx,mdx}",
    "./src/components/**/*.{js,ts,jsx,tsx,mdx}",
    "./src/app/**/*.{js,ts,jsx,tsx,mdx}",
  ],
  theme: {
    extend: {
      // ── shadcn/ui CSS-variable colour hooks ───────────────────────────────
      colors: {
        background: "hsl(var(--background))",
        foreground: "hsl(var(--foreground))",
        card: {
          DEFAULT: "hsl(var(--card))",
          foreground: "hsl(var(--card-foreground))",
        },
        popover: {
          DEFAULT: "hsl(var(--popover))",
          foreground: "hsl(var(--popover-foreground))",
        },
        primary: {
          DEFAULT: "hsl(var(--primary))",
          foreground: "hsl(var(--primary-foreground))",
        },
        secondary: {
          DEFAULT: "hsl(var(--secondary))",
          foreground: "hsl(var(--secondary-foreground))",
        },
        muted: {
          DEFAULT: "hsl(var(--muted))",
          foreground: "hsl(var(--muted-foreground))",
        },
        accent: {
          DEFAULT: "hsl(var(--accent))",
          foreground: "hsl(var(--accent-foreground))",
        },
        destructive: {
          DEFAULT: "hsl(var(--destructive))",
          foreground: "hsl(var(--destructive-foreground))",
        },
        border: "hsl(var(--border))",
        input: "hsl(var(--input))",
        ring: "hsl(var(--ring))",

        // ── PPDO Green — Primary brand ─────────────────────────────────────
        // (Philippine government green, DICT/DepEd inspired)
        green: {
          950: "#071F12",
          900: "#0F4526",
          800: "#13512D",
          700: "#196638", // Login header, landing hero
          600: "#1F7A45", // PRIMARY — sidebar, buttons
          500: "#2E9958", // Hover on primary buttons
          400: "#3BAD6A", // Progress bars, dots, badges
          300: "#6DC492",
          200: "#A8DABC", // Borders on green elements
          100: "#D4EDDE",
          50: "#F0FAF4",  // Table hover, icon backgrounds
          25: "#F7FCF9",
        },

        // ── PPDO Slate — Neutral ───────────────────────────────────────────
        slate: {
          800: "#343A40", // Footer
          600: "#6C757D", // Body text, labels
          400: "#ADB5BD", // Disabled, muted
          200: "#E9ECEF", // Input borders
          100: "#F1F3F5", // Page background
          50: "#F8F9FA",  // Zebra rows
        },

        // ── Status colours ─────────────────────────────────────────────────
        amber: {
          500: "#EF9F27", // Warning, partial delivery
          100: "#FEF3CD", // Warning pill background
        },
        danger: {
          500: "#E24B4A", // Danger, out of stock
          100: "#FDECEA", // Danger pill background
        },
        info: {
          500: "#378ADD", // Personal events, open PR
          100: "#E3F2FD", // Open PR pill background
        },

        // ── Stat card backgrounds (Inventory Dashboard) ────────────────────
        stat: {
          blue:   "#EBF4FF", // Open PRs
          amber:  "#FEF9EC", // Partially Delivered
          green:  "#F0FAF4", // Completed / In Stock
          red:    "#FEF2F2", // Out of Stock
          purple: "#F3F0FF", // Unique Items
        },

        // ── Excel-like cell colours ────────────────────────────────────────
        cell: {
          fill:  "#FFFDE7", // Yellow — user fills in
          auto:  "#F1F3F5", // Gray — auto-fill
          green: "#F0FAF4", // Green tint — system-generated
        },
      },

      // ── Border radius ─────────────────────────────────────────────────────
      borderRadius: {
        lg: "var(--radius)",
        md: "calc(var(--radius) - 2px)",
        sm: "calc(var(--radius) - 4px)",
      },

      // ── Typography ────────────────────────────────────────────────────────
      fontFamily: {
        sans: ["Segoe UI", "Source Sans Pro", "system-ui", "sans-serif"],
      },

      // ── Animations ───────────────────────────────────────────────────────
      keyframes: {
        "slide-in": {
          "0%":   { opacity: "0", transform: "translateX(120%)" },
          "100%": { opacity: "1", transform: "translateX(0)" },
        },
        "toast-progress": {
          "0%":   { transform: "scaleX(1)" },
          "100%": { transform: "scaleX(0)" },
        },
      },
      animation: {
        "slide-in":       "slide-in 0.22s ease-out",
        "toast-progress": "toast-progress linear forwards",
      },
    },
  },
  plugins: [],
};

export default config;

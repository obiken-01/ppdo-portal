#!/usr/bin/env node
/**
 * Generates the PWA icon set from the official PPDO logo.
 *
 *   node scripts/generate-pwa-icons.js
 *
 * Run from the repo root. Requires `sharp`, which is already a devDependency
 * of the frontend — no extra install needed.
 *
 * Source: `frontend/public/images/ppdo-logo.png` — the official PPDO logo,
 * 256x256 with transparency. (It was called `ppdo-logo-placeholder.png` until
 * 2026-08-14; the name long outlived the placeholder it once described and was
 * renamed once it had misled a reader into believing the real logo was missing.)
 *
 * Outputs (frontend/public/icons/):
 *   icon-192.png           plain, transparent — downscaled from 256, crisp
 *   icon-512.png           plain, transparent — UPSCALED 2x from 256 (see below)
 *   icon-maskable-512.png  badge at 80% on white, for Android adaptive icons
 *   apple-touch-icon.png   180x180 on white — iOS ignores the manifest icons
 *                          and composites transparency unpredictably
 *
 * Two deliberate compromises, both agreed with Ralph on 2026-08-14:
 *
 * 1. The 512 is a 2x upscale, because the only source available is 256x256.
 *    Lanczos3 keeps it acceptable, and at ordinary home-screen sizes (48-192px)
 *    the difference is invisible; it is mildly soft only on the Android splash
 *    screen, which renders the 512 large. If a vector or larger PNG of the logo
 *    ever turns up, drop it in as the source and re-run — every size below is
 *    derived, so nothing else needs to change.
 *
 * 2. The maskable icon pads the badge to 80% on white. Android crops icons to a
 *    device-chosen circle/squircle keeping roughly the centre 80%, and this logo
 *    puts its ring text ("PROVINCIAL PLANNING AND DEVELOPMENT OFFICE") right at
 *    the outer edge — unpadded, that text is clipped on most Android phones.
 */

const path = require("path");
const fs = require("fs");
const sharp = require(path.join(__dirname, "..", "frontend", "node_modules", "sharp"));

const SRC = path.join(__dirname, "..", "frontend", "public", "images", "ppdo-logo.png");
const OUT_DIR = path.join(__dirname, "..", "frontend", "public", "icons");

// Android's maskable safe zone is the centre 80% of the canvas.
const MASKABLE_SAFE_RATIO = 0.8;
const WHITE = { r: 255, g: 255, b: 255, alpha: 1 };

async function main() {
  if (!fs.existsSync(SRC)) throw new Error(`Source logo not found: ${SRC}`);
  fs.mkdirSync(OUT_DIR, { recursive: true });

  const { width, height } = await sharp(SRC).metadata();
  console.log(`Source: ${path.relative(process.cwd(), SRC)} (${width}x${height})`);
  if (width < 512) {
    console.log(`Note: source is under 512px, so icon-512 is a ${(512 / width).toFixed(1)}x upscale.`);
  }

  // ── Plain icons — transparency preserved ─────────────────────────────────
  for (const size of [192, 512]) {
    const out = path.join(OUT_DIR, `icon-${size}.png`);
    await sharp(SRC).resize(size, size, { kernel: "lanczos3", fit: "contain" }).png().toFile(out);
    console.log(`  ✓ icon-${size}.png`);
  }

  // ── Maskable — badge at 80% on white, so Android's crop can't clip the ring text ──
  const maskableSize = 512;
  const inner = Math.round(maskableSize * MASKABLE_SAFE_RATIO);
  const badge = await sharp(SRC).resize(inner, inner, { kernel: "lanczos3", fit: "contain" }).png().toBuffer();
  await sharp({ create: { width: maskableSize, height: maskableSize, channels: 4, background: WHITE } })
    .composite([{ input: badge, gravity: "center" }])
    .png()
    .toFile(path.join(OUT_DIR, "icon-maskable-512.png"));
  console.log(`  ✓ icon-maskable-512.png (badge at ${MASKABLE_SAFE_RATIO * 100}%)`);

  // ── Apple touch icon — flattened on white; iOS handles alpha poorly ──────
  const appleSize = 180;
  const appleBadge = await sharp(SRC).resize(appleSize, appleSize, { kernel: "lanczos3", fit: "contain" }).png().toBuffer();
  await sharp({ create: { width: appleSize, height: appleSize, channels: 4, background: WHITE } })
    .composite([{ input: appleBadge, gravity: "center" }])
    .flatten({ background: WHITE })
    .png()
    .toFile(path.join(OUT_DIR, "apple-touch-icon.png"));
  console.log(`  ✓ apple-touch-icon.png`);

  console.log(`\nWrote to ${path.relative(process.cwd(), OUT_DIR)}/`);
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});

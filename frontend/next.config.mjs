/** @type {import('next').NextConfig} */
const nextConfig = {
  output: "export",
  images: {
    // next/image server-side optimization is not available with static export.
    // unoptimized: true lets <Image> render normally (lazy, correct dimensions,
    // no CLS) while serving the pre-converted WebP files directly.
    unoptimized: true,
  },
};

export default nextConfig;

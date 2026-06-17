import LdipEditClient from "./LdipEditClient";

export function generateStaticParams() {
  return [{ id: "__placeholder__" }];
}

export default function LdipEditPage() {
  return <LdipEditClient />;
}

import { inflateRawSync } from "node:zlib";

// PPDO-271 — a small .xlsx reader for the export specs. An .xlsx is a zip of XML files; this reads
// just enough (shared strings, sheet cells) to ask "is this the workbook, and does it hold the total
// the screen shows". It is not a spreadsheet library and compares nothing cell by cell.

export interface Cell {
  ref: string;
  /** Text for string cells; the number for numeric cells. */
  value: string | number;
}
export interface Sheet {
  name: string;
  cells: Cell[];
}

/** Every file in a zip, by name. Reads the central directory, so entry sizes are always known. */
export function unzip(buf: Buffer): Map<string, Buffer> {
  let eocd = -1;
  for (let i = buf.length - 22; i >= 0 && i >= buf.length - 65_557; i--) {
    if (buf.readUInt32LE(i) === 0x06054b50) { eocd = i; break; }
  }
  if (eocd < 0) throw new Error("not a zip file (no end-of-central-directory record)");
  const count = buf.readUInt16LE(eocd + 10);
  let p = buf.readUInt32LE(eocd + 16);
  const files = new Map<string, Buffer>();
  for (let n = 0; n < count; n++) {
    if (buf.readUInt32LE(p) !== 0x02014b50) throw new Error("bad zip central directory");
    const method = buf.readUInt16LE(p + 10);
    const compressedSize = buf.readUInt32LE(p + 20);
    const nameLen = buf.readUInt16LE(p + 28);
    const extraLen = buf.readUInt16LE(p + 30);
    const commentLen = buf.readUInt16LE(p + 32);
    const localAt = buf.readUInt32LE(p + 42);
    const name = buf.toString("utf8", p + 46, p + 46 + nameLen);
    const dataAt = localAt + 30 + buf.readUInt16LE(localAt + 26) + buf.readUInt16LE(localAt + 28);
    const raw = buf.subarray(dataAt, dataAt + compressedSize);
    files.set(name, method === 0 ? raw : inflateRawSync(raw));
    p += 46 + nameLen + extraLen + commentLen;
  }
  return files;
}

const decode = (s: string) =>
  s.replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&quot;/g, '"').replace(/&apos;/g, "'")
    .replace(/&#(\d+);/g, (_, d) => String.fromCodePoint(Number(d))).replace(/&amp;/g, "&");

// Writers differ: Excel emits <c>, the Open XML SDK emits <x:c>. Every tag name takes an optional prefix.
const P = "(?:[A-Za-z0-9]+:)?";
const ANY = String.raw`[\s\S]*?`;
const WORD = String.raw`\b`;
const tag = (name: string) => new RegExp(`<${P}${name}${WORD}[^>]*?(?:/>|>${ANY}</${P}${name}>)`, "g");
const innerText = (xml: string) =>
  decode(Array.from(xml.matchAll(new RegExp(`<${P}t${WORD}[^>]*>(${ANY})</${P}t>`, "g"))).map((m) => m[1]).join(""));

export function readWorkbook(buf: Buffer): Sheet[] {
  const files = unzip(buf);
  const text = (name: string) => files.get(name)?.toString("utf8");
  const workbook = text("xl/workbook.xml");
  if (!workbook) throw new Error("not an Excel workbook (no xl/workbook.xml)");

  const strings = (text("xl/sharedStrings.xml")?.match(tag("si")) ?? []).map(innerText);
  const names = Array.from(workbook.matchAll(new RegExp(`<${P}sheet${WORD}[^>]*?${WORD}name="([^"]*)"`, "g"))).map((m) => decode(m[1]));
  const value = new RegExp(`<${P}v>(${ANY})</${P}v>`);

  const sheets: Sheet[] = [];
  names.forEach((name, i) => {
    const xml = text(`xl/worksheets/sheet${i + 1}.xml`);
    if (!xml) return;
    const cells: Cell[] = [];
    for (const c of xml.match(tag("c")) ?? []) {
      const ref = /\br="([A-Z]+\d+)"/.exec(c)?.[1];
      const type = /\bt="([^"]*)"/.exec(c)?.[1];
      if (!ref) continue;
      if (type === "inlineStr") { cells.push({ ref, value: innerText(c) }); continue; }
      const v = value.exec(c)?.[1];
      if (v === undefined) continue;
      if (type === "s") cells.push({ ref, value: strings[Number(v)] ?? "" });
      else if (type === "str") cells.push({ ref, value: decode(v) });
      else if (!type || type === "n") cells.push({ ref, value: Number(v) });
    }
    sheets.push({ name, cells });
  });
  return sheets;
}

/**
 * True when the workbook shows `amount`: a numeric cell equal to it to the centavo, or a text cell
 * that contains it written the way the screen writes it (1,234.50), e.g. "Total: ₱370.35".
 */
export function hasAmount(sheets: Sheet[], amount: number): boolean {
  const written = amount.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  return sheets.some((s) => s.cells.some((c) =>
    typeof c.value === "number" ? Math.abs(c.value - amount) < 0.005 : c.value.includes(written)));
}

/** True when some text cell contains `needle`. */
export function hasText(sheets: Sheet[], needle: string | RegExp): boolean {
  return sheets.some((s) => s.cells.some((c) => typeof c.value === "string"
    && (typeof needle === "string" ? c.value.includes(needle) : needle.test(c.value))));
}

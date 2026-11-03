import { describe, expect, it } from "vitest";
import { isHttpUrl } from "./safe-url";

describe("isHttpUrl (PPDO-299)", () => {
  it.each([
    "javascript:alert(1)",
    "JAVASCRIPT:alert(1)",
    "   javascript:alert(1)   ",
    "java\tscript:alert(1)",
    "data:text/html,<script>alert(1)</script>",
    "/resource-links",
    "drive.google.com/folder",
    "mailto:ppdo@example.com",
    "ftp://example.com/file",
    "",
    null,
    undefined,
  ])("refuses %j", (url) => {
    expect(isHttpUrl(url)).toBe(false);
  });

  it.each([
    "https://drive.google.com/drive/folders/abc",
    "http://intranet.example.com/page",
    "  HTTPS://example.com/x  ",
  ])("accepts %j", (url) => {
    expect(isHttpUrl(url)).toBe(true);
  });
});

// PPDO-281 — while a page's data loads, its header actions are disabled as one group (a disabled
// <fieldset>), so a primary "+ Add …" button can't be clicked before the list it adds to arrives.
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import ConfigPageHeader from "./ConfigPageHeader";

const render = (busy?: boolean) =>
  renderToStaticMarkup(
    createElement(ConfigPageHeader, {
      title: "Offices",
      description: "d",
      busy,
      actions: createElement("button", null, "+ Add Office"),
    }),
  );

describe("ConfigPageHeader busy", () => {
  it("disables the actions while busy", () => {
    expect(render(true)).toMatch(/<fieldset[^>]* disabled=""/);
  });

  it("leaves the actions enabled when not busy", () => {
    expect(render(false)).not.toContain(" disabled=");
    expect(render(undefined)).not.toContain(" disabled=");
  });
});

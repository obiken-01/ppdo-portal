"use client";

import { useEffect, useState } from "react";
import { listCcTypologies, listEsreCodes } from "@/lib/config";
import { cacheKey, loadThroughCache } from "@/lib/reference-cache";
import type { ClimateChangeTypologyResponse, EsreCodeResponse } from "@/types/config";

/**
 * The config-backed code lists the AIP editors offer — eSRE and CC typology
 * (Demo 2.2 / PPDO-124, Demo 2.3 / PPDO-125).
 *
 * <b>Why this exists.</b> Both lists had a full CRUD config page whose values never reached the
 * three AIP editors: eSRE read a hardcoded four-item `AIP_ESRE_OPTIONS`, and CC typology was a
 * free-text input. Editing either config screen changed nothing an encoder could see — an admin
 * would add a code, watch it save, and never learn it was not on offer.
 *
 * ⚠️ <b>Fetched once per page load, not once per component.</b> Five different parents render
 * those three editors across the detail, entry and review trees, and a row-level fetch would fire
 * one request per activity row on a tree with hundreds of them. The in-flight promise is cached at
 * module scope so concurrent callers share a single request (CLAUDE.md — "fetch shared state
 * once"; the WFP page once fired `/auth/me` four times per load).
 *
 * The consequence is deliberate and worth knowing: a config edit made in another tab does not
 * appear in an already-open AIP page until a reload. That is the normal cost of caching a list
 * that changes a few times a year, and it is still an enormous improvement on never.
 */
export interface AipCodeOption {
  code: string;
  /** Display name. Falls back to the code when config has no distinct name for it. */
  name: string;
  description: string | null;
}

export interface AipCodeOptions {
  esre: AipCodeOption[];
  ccTypology: AipCodeOption[];
  /** False until the fetch settles. A picker should not claim a code is unknown before then. */
  loaded: boolean;
}

const EMPTY: AipCodeOptions = { esre: [], ccTypology: [], loaded: false };

/**
 * ⚠️ Module scope, so it survives client-side navigation between AIP pages and is shared by every
 * component mounted from one load. Reset only by a hard reload.
 *
 * ↩️ PPDO-111 — the lists now come through the reference cache: a repeat visit gets them from
 * IndexedDB at once, and a background re-fetch updates every mounted picker if a list changed. So
 * a config edit in another tab now shows up within one page load rather than after a reload.
 */
let current: AipCodeOptions = EMPTY;
let started = false;
const listeners = new Set<(o: AipCodeOptions) => void>();

function publish(next: AipCodeOptions) {
  current = next;
  listeners.forEach((l) => l(next));
}

function toOption(row: EsreCodeResponse | ClimateChangeTypologyResponse): AipCodeOption {
  return {
    code: row.code,
    // ⚠️ `name` is seeded equal to `code` for all 60 CC typologies, with every description null.
    // Rendering "A222-01 — A222-01" would be noise dressed as information, so callers get a name
    // they can compare against the code and collapse. Fixing the DATA is the other half of
    // PPDO-125 and is not something this layer can paper over.
    name: row.name?.trim() || row.code,
    description: row.description?.trim() || null,
  };
}

function startLoading() {
  if (started) return;
  started = true;
  // `loaded` turns true once BOTH lists have answered — from the cache, the server, or a failure.
  const settled = { esre: false, ccTypology: false };
  const settle = (side: "esre" | "ccTypology", rows: AipCodeOption[]) => {
    settled[side] = true;
    publish({ ...current, [side]: rows, loaded: settled.esre && settled.ccTypology });
  };
  // ⚠️ Neither list is a prerequisite for editing an activity. A config outage must cost the
  // encoder the picker's SUGGESTIONS, never the ability to save the rest of the row — so each
  // side falls back to empty rather than rejecting. AipCodeSelect keeps whatever is already
  // stored selectable regardless, which is what stops an outage from silently blanking a field.
  void loadThroughCache(
    cacheKey("esre-codes", { active: "true" }),
    () => listEsreCodes({ active: "true" }),
    (rows) => settle("esre", rows.map(toOption)),
    () => settle("esre", []));
  void loadThroughCache(
    cacheKey("cc-typologies", { active: "true" }),
    () => listCcTypologies({ active: "true" }),
    (rows) => settle("ccTypology", rows.map(toOption)),
    () => settle("ccTypology", []));
}

export function useAipCodeOptions(): AipCodeOptions {
  const [options, setOptions] = useState<AipCodeOptions>(current);

  useEffect(() => {
    listeners.add(setOptions);
    setOptions(current); // anything that arrived between the first render and this effect
    startLoading();
    return () => {
      listeners.delete(setOptions);
    };
  }, []);

  return options;
}

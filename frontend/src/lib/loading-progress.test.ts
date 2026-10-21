import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  DONE_HOLD_MS,
  FINISH_GRACE_MS,
  NAV_TIMEOUT_MS,
  SHOW_DELAY_MS,
  createProgressTracker,
  isSameDocumentUrl,
  trackRequestEnd,
  trackRequestStart,
  type ProgressTrackable,
  type ProgressPhase,
  type ProgressTracker,
} from "./loading-progress";

describe("createProgressTracker", () => {
  let tracker: ProgressTracker;
  let phases: ProgressPhase[];

  beforeEach(() => {
    vi.useFakeTimers();
    tracker = createProgressTracker();
    phases = [];
    tracker.subscribe((p) => phases.push(p));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("requestStarted_FastRequest_NeverShows", () => {
    tracker.requestStarted();
    vi.advanceTimersByTime(SHOW_DELAY_MS - 1);
    tracker.requestFinished();
    vi.advanceTimersByTime(5_000);

    expect(phases).toEqual([]);
    expect(tracker.getPhase()).toBe("idle");
  });

  it("requestStarted_SlowRequest_ShowsAfterDelayThenFinishes", () => {
    tracker.requestStarted();
    vi.advanceTimersByTime(SHOW_DELAY_MS);
    expect(tracker.getPhase()).toBe("loading");

    tracker.requestFinished();
    vi.advanceTimersByTime(FINISH_GRACE_MS);
    expect(tracker.getPhase()).toBe("done");

    vi.advanceTimersByTime(DONE_HOLD_MS);
    expect(phases).toEqual(["loading", "done", "idle"]);
  });

  it("requestFinished_WhileOthersInFlight_StaysLoading", () => {
    tracker.requestStarted();
    tracker.requestStarted();
    vi.advanceTimersByTime(SHOW_DELAY_MS);

    tracker.requestFinished();
    vi.advanceTimersByTime(FINISH_GRACE_MS);
    expect(tracker.getPhase()).toBe("loading");

    tracker.requestFinished();
    vi.advanceTimersByTime(FINISH_GRACE_MS);
    expect(tracker.getPhase()).toBe("done");
  });

  it("requestFinished_MoreThanStarted_NeverGoesNegative", () => {
    tracker.requestFinished();
    tracker.requestStarted();
    vi.advanceTimersByTime(SHOW_DELAY_MS);
    expect(tracker.getPhase()).toBe("loading");

    tracker.requestFinished();
    vi.advanceTimersByTime(FINISH_GRACE_MS);
    expect(tracker.getPhase()).toBe("done");
  });

  it("requestStarted_DuringDoneFade_ResumesLoading", () => {
    tracker.requestStarted();
    vi.advanceTimersByTime(SHOW_DELAY_MS);
    tracker.requestFinished();
    vi.advanceTimersByTime(FINISH_GRACE_MS);
    expect(tracker.getPhase()).toBe("done");

    tracker.requestStarted();
    expect(tracker.getPhase()).toBe("loading");

    // The cancelled fade must not drop the bar to idle under the new request.
    vi.advanceTimersByTime(DONE_HOLD_MS);
    expect(tracker.getPhase()).toBe("loading");
  });

  it("navigationStarted_ThenFinished_ShowsAndClears", () => {
    tracker.navigationStarted();
    vi.advanceTimersByTime(SHOW_DELAY_MS);
    expect(tracker.getPhase()).toBe("loading");

    tracker.navigationFinished();
    vi.advanceTimersByTime(FINISH_GRACE_MS + DONE_HOLD_MS);
    expect(tracker.getPhase()).toBe("idle");
  });

  it("navigationFinished_WithRequestStillInFlight_StaysLoading", () => {
    tracker.navigationStarted();
    tracker.requestStarted();
    vi.advanceTimersByTime(SHOW_DELAY_MS);

    tracker.navigationFinished();
    vi.advanceTimersByTime(FINISH_GRACE_MS);
    expect(tracker.getPhase()).toBe("loading");

    tracker.requestFinished();
    vi.advanceTimersByTime(FINISH_GRACE_MS);
    expect(tracker.getPhase()).toBe("done");
  });

  it("navigationStarted_NeverFinished_SafetyTimeoutClearsIt", () => {
    tracker.navigationStarted();
    vi.advanceTimersByTime(NAV_TIMEOUT_MS - 1);
    expect(tracker.getPhase()).toBe("loading");

    vi.advanceTimersByTime(1 + FINISH_GRACE_MS);
    expect(tracker.getPhase()).toBe("done");
  });

  it("navigationFinished_PageRequestStartsWithinGrace_StaysLoadingWithoutBlink", () => {
    tracker.navigationStarted();
    vi.advanceTimersByTime(SHOW_DELAY_MS);

    // The new page mounts, then fires its first request a moment later.
    tracker.navigationFinished();
    vi.advanceTimersByTime(FINISH_GRACE_MS - 1);
    tracker.requestStarted();
    vi.advanceTimersByTime(FINISH_GRACE_MS);

    expect(phases).toEqual(["loading"]);
  });

  it("navigationFinished_WithoutStart_DoesNothing", () => {
    tracker.navigationFinished();
    vi.advanceTimersByTime(5_000);
    expect(phases).toEqual([]);
  });

  it("subscribe_Unsubscribed_StopsNotifying", () => {
    const seen: ProgressPhase[] = [];
    const off = tracker.subscribe((p) => seen.push(p));
    off();

    tracker.requestStarted();
    vi.advanceTimersByTime(SHOW_DELAY_MS);
    expect(seen).toEqual([]);
  });
});

describe("isSameDocumentUrl", () => {
  const here = "http://localhost:3000/budget-planning/aip/entry?office=3";

  it("isSameDocumentUrl_SamePathAndQuery_ReturnsTrue", () => {
    expect(isSameDocumentUrl("/budget-planning/aip/entry?office=3", here)).toBe(true);
  });

  it("isSameDocumentUrl_OnlyHashDiffers_ReturnsTrue", () => {
    expect(isSameDocumentUrl("/budget-planning/aip/entry?office=3#top", here)).toBe(true);
  });

  it("isSameDocumentUrl_QueryDiffers_ReturnsFalse", () => {
    expect(isSameDocumentUrl("?office=3&program=12", here)).toBe(false);
  });

  it("isSameDocumentUrl_PathDiffers_ReturnsFalse", () => {
    expect(isSameDocumentUrl("/dashboard", here)).toBe(false);
  });
});

describe("trackRequestStart / trackRequestEnd", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("trackRequestStart_SkipProgress_IsNotCounted", () => {
    const tracker = createProgressTracker();
    trackRequestStart({ skipProgress: true }, tracker);
    vi.advanceTimersByTime(SHOW_DELAY_MS);
    expect(tracker.getPhase()).toBe("idle");
  });

  it("trackRequestStart_RetryWithSameConfig_CountedOnce", () => {
    const tracker = createProgressTracker();
    const config: ProgressTrackable = {};
    trackRequestStart(config, tracker);
    trackRequestStart(config, tracker); // the 401 retry goes back through the request interceptor
    vi.advanceTimersByTime(SHOW_DELAY_MS);

    trackRequestEnd(config, tracker); // the retry's own response
    trackRequestEnd(config, tracker); // the outer chain's response, same config
    vi.advanceTimersByTime(FINISH_GRACE_MS);
    expect(tracker.getPhase()).toBe("done");

    // A second, unrelated request must still be able to show the bar — the count did not go negative.
    trackRequestStart({}, tracker);
    expect(tracker.getPhase()).toBe("loading");
  });

  it("trackRequestEnd_UntrackedOrMissingConfig_DoesNothing", () => {
    const tracker = createProgressTracker();
    const busy: ProgressTrackable = {};
    trackRequestStart(busy, tracker);
    vi.advanceTimersByTime(SHOW_DELAY_MS);

    trackRequestEnd(undefined, tracker);
    trackRequestEnd({ skipProgress: true }, tracker);
    vi.advanceTimersByTime(FINISH_GRACE_MS);
    expect(tracker.getPhase()).toBe("loading");
  });
});

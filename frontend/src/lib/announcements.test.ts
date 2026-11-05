import { describe, expect, it } from "vitest";
import { findAnnouncement } from "./announcements";
import type { AnnouncementPublicDto } from "@/types";

describe("findAnnouncement (PPDO-284)", () => {
  const list: AnnouncementPublicDto[] = [
    { id: "a1", title: "First", content: "<p>One</p>", publishedAt: "2026-11-01T00:00:00Z" },
    { id: "b2", title: "Second", content: "<p>Two</p>", publishedAt: "2026-11-02T00:00:00Z" },
  ];

  it("picks the published announcement by id", () => {
    expect(findAnnouncement(list, "b2")?.title).toBe("Second");
  });

  it("matches ids case-insensitively, as a GUID is", () => {
    expect(findAnnouncement(list, "B2")?.title).toBe("Second");
  });

  it("is null once it is no longer published (archived, unpublished or deleted)", () => {
    expect(findAnnouncement(list, "gone")).toBeNull();
  });
});

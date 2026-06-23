/**
 * Announcement module types (v1.1.1 RAL-83/86/87) — mirrors
 * PPDO.Application/DTOs/Announcements/.
 *
 * Status workflow: Draft → Published → Archived
 * Publish → Unpublish reverts to Draft; Archive is terminal.
 */

export type AnnouncementStatus = "Draft" | "Published" | "Archived";

/** Full read model returned by admin /manage endpoint. */
export interface AnnouncementDto {
  id: string;
  title: string;
  content: string;
  status: AnnouncementStatus;
  publishedAt: string | null;
  createdById: string;
  createdByName: string;
  createdAt: string;
  updatedAt: string;
}

/** Slim read model for the public landing page endpoint (no auth required). */
export interface AnnouncementPublicDto {
  id: string;
  title: string;
  content: string;
  publishedAt: string;
}

export interface CreateAnnouncementRequest {
  title: string;
  content: string;
}

export interface UpdateAnnouncementRequest {
  title: string;
  content: string;
}

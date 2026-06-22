/**
 * Announcement API helpers (RAL-83 endpoints).
 *
 * Admin endpoints (/manage, POST, PUT, DELETE) go through the shared Axios
 * instance and return the resource directly (not wrapped in ApiResponse<T>).
 *
 * The public GET /api/announcements endpoint is unauthenticated and returns
 * AnnouncementPublicDto[] — used by the landing page (RAL-87).
 */

import api from "./api";
import type {
  AnnouncementDto,
  AnnouncementPublicDto,
  CreateAnnouncementRequest,
  UpdateAnnouncementRequest,
} from "@/types";

// ---------------------------------------------------------------------------
// Public (no auth)
// ---------------------------------------------------------------------------

/** GET /api/announcements — published announcements for the landing page. */
export async function getPublicAnnouncements(): Promise<AnnouncementPublicDto[]> {
  const { data } = await api.get<AnnouncementPublicDto[]>("/announcements");
  return data;
}

// ---------------------------------------------------------------------------
// Admin (JWT required)
// ---------------------------------------------------------------------------

/** GET /api/announcements/manage — all announcements (Admin/SuperAdmin). */
export async function getAnnouncementsManage(): Promise<AnnouncementDto[]> {
  const { data } = await api.get<AnnouncementDto[]>("/announcements/manage");
  return data;
}

/** POST /api/announcements — create a new draft announcement. */
export async function createAnnouncement(
  body: CreateAnnouncementRequest,
): Promise<AnnouncementDto> {
  const { data } = await api.post<AnnouncementDto>("/announcements", body);
  return data;
}

/** PUT /api/announcements/{id} — update title + content. */
export async function updateAnnouncement(
  id: string,
  body: UpdateAnnouncementRequest,
): Promise<AnnouncementDto> {
  const { data } = await api.put<AnnouncementDto>(`/announcements/${id}`, body);
  return data;
}

/** PUT /api/announcements/{id}/publish — Draft → Published. */
export async function publishAnnouncement(id: string): Promise<AnnouncementDto> {
  const { data } = await api.put<AnnouncementDto>(`/announcements/${id}/publish`);
  return data;
}

/** PUT /api/announcements/{id}/unpublish — Published → Draft. */
export async function unpublishAnnouncement(id: string): Promise<AnnouncementDto> {
  const { data } = await api.put<AnnouncementDto>(`/announcements/${id}/unpublish`);
  return data;
}

/** PUT /api/announcements/{id}/archive — any status → Archived. */
export async function archiveAnnouncement(id: string): Promise<AnnouncementDto> {
  const { data } = await api.put<AnnouncementDto>(`/announcements/${id}/archive`);
  return data;
}

/** DELETE /api/announcements/{id} — 204 on success; 409 if Published. */
export async function deleteAnnouncement(id: string): Promise<void> {
  await api.delete(`/announcements/${id}`);
}

// ---------------------------------------------------------------------------
// Error helpers
// ---------------------------------------------------------------------------

/** Extract a human-readable message from an Axios error for announcements endpoints. */
export function announcementErrorMessage(err: unknown, fallback: string): string {
  const axiosErr = err as {
    response?: { data?: string | { error?: string; message?: string } };
  };
  const body = axiosErr?.response?.data;
  if (typeof body === "string" && body.trim()) return body;
  if (typeof body === "object" && body !== null) {
    return body.error ?? body.message ?? fallback;
  }
  return fallback;
}

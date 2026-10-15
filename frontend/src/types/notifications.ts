/**
 * Notification bell types (v1.9.0 — PPDO-222, `docs/v1.9/Notification_Centre_Spec.md` §4).
 * Mirrors `PPDO.Application/DTOs/Notifications/NotificationDtos.cs`.
 */

/** `NotificationTypes` on the server. Unknown values are tolerated by the helpers. */
export type NotificationType =
  | "ProposalComment" | "ProposalReply" | "ProposalResolved"
  | "AipReturnedByPpdo" | "AipReturnedByDepartmentHead" | "AipSentToPpdo"
  | "AipDivisionSubmitted" | "AipReviewerEdit";

/** One entry. `text` is frozen at write time; `link` is a portal-relative path and stays live. */
export interface AppNotification {
  id: number;
  type: NotificationType | string;
  text: string;
  link: string;
  createdAt: string;
  readAt: string | null;
}

export interface NotificationPage {
  items: AppNotification[];
  total: number;
}

export interface NotificationCount {
  count: number;
}

/** Mirrors PPDO.Application/DTOs/Dashboard/ */

export interface CalendarEventResponse {
  id: string | null;
  title: string;
  description: string | null;
  startDate: string;        // ISO 8601 UTC
  endDate: string | null;   // ISO 8601 UTC — null means same-day
  isAllDay: boolean;
  /** "Office" | "Personal" | "Holiday" */
  eventType: string;
  /** "Nager.Date" | "Static" | null */
  source: string | null;
  /** null for holidays; "Pending" | "Approved" | "Rejected" for DB events */
  status: 'Pending' | 'Approved' | 'Rejected' | null;
  rejectionReason: string | null;
}

export interface PendingCalendarEvent {
  id: string;
  title: string;
  description: string | null;
  startDate: string;
  endDate: string | null;
  isAllDay: boolean;
  createdById: string;
  createdByName: string;
  createdAt: string;
}

export interface CreateCalendarEventRequest {
  title: string;
  description?: string | null;
  startDate: string;
  endDate?: string | null;
  isAllDay: boolean;
  /** "Office" | "Personal" */
  eventType: string;
}

export interface DashboardStats {
  totalPRs: number;
  openPRs: number;
  partiallyDeliveredPRs: number;
  fullyDeliveredPRs: number;
  totalItems: number;
  newItemsPendingReview: number;
}

export interface ResourceLink {
  id: string;
  title: string;
  url: string;
  category: string;
  categoryOrder: number;
  linkOrder: number;
}

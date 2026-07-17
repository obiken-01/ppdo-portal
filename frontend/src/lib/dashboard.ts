import api from './api';
import type { PendingCalendarEvent, UpdateCalendarEventRequest } from '@/types';

export async function getPendingEvents(): Promise<PendingCalendarEvent[]> {
  const { data } = await api.get<PendingCalendarEvent[]>('/dashboard/events/pending');
  return data;
}

export async function reviewCalendarEvent(
  id: string,
  approved: boolean,
  rejectionReason?: string,
): Promise<void> {
  await api.put(`/dashboard/events/${id}/review`, {
    approved,
    rejectionReason: rejectionReason ?? null,
  });
}

export async function deleteCalendarEvent(id: string): Promise<void> {
  await api.delete(`/dashboard/events/${id}`);
}

export async function updateCalendarEvent(
  id: string,
  payload: UpdateCalendarEventRequest,
): Promise<void> {
  await api.put(`/dashboard/events/${id}`, payload);
}

export async function createCalendarEvent(payload: {
  title: string;
  description?: string | null;
  startDate: string;
  endDate?: string | null;
  isAllDay: boolean;
  eventType: string;
}): Promise<void> {
  await api.post('/dashboard/events', payload);
}

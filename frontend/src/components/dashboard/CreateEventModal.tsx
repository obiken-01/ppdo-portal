"use client";

import { useState } from "react";
import Modal from "@/components/ui/Modal";
import { useToast } from "@/components/ui/Toast";
import { createCalendarEvent, updateCalendarEvent } from "@/lib/dashboard";
import type { CalendarEventResponse } from "@/types";

interface CreateEventModalProps {
  open: boolean;
  initialDate: string;
  isAdmin: boolean;
  /** When set, the modal edits this event instead of creating a new one. EventType is
   *  fixed at creation and cannot be changed here. */
  editingEvent?: CalendarEventResponse | null;
  onClose: () => void;
  onSaved: () => void;
}

export default function CreateEventModal({
  open,
  initialDate,
  isAdmin,
  editingEvent,
  onClose,
  onSaved,
}: CreateEventModalProps) {
  const { toast } = useToast();
  const isEditing = editingEvent != null;

  const [title, setTitle]         = useState(editingEvent?.title ?? "");
  const [description, setDesc]    = useState(editingEvent?.description ?? "");
  const [startDate, setStartDate] = useState(editingEvent?.startDate.slice(0, 10) ?? initialDate);
  const [endDate, setEndDate]     = useState(editingEvent?.endDate?.slice(0, 10) ?? "");
  const [isAllDay, setIsAllDay]   = useState(editingEvent?.isAllDay ?? true);
  const [eventType, setEventType] = useState<"Office" | "Personal">(
    (editingEvent?.eventType as "Office" | "Personal") ?? "Office"
  );
  const [saving, setSaving]       = useState(false);

  const submitLabel = isEditing
    ? "Save Changes"
    : eventType === "Office" && !isAdmin ? "Submit for Approval" : "Save Event";

  async function handleSubmit() {
    if (!title.trim()) {
      toast.error("Title is required.");
      return;
    }
    if (endDate && endDate < startDate) {
      toast.error("End date cannot be before start date.");
      return;
    }
    setSaving(true);
    try {
      if (isEditing && editingEvent?.id) {
        await updateCalendarEvent(editingEvent.id, {
          title: title.trim(),
          description: description.trim() || null,
          startDate,
          endDate: endDate || null,
          isAllDay,
        });
        toast.success(
          eventType === "Office" && editingEvent.status !== "Pending" && !isAdmin
            ? "Event updated — resubmitted for admin approval."
            : "Event updated."
        );
      } else {
        await createCalendarEvent({
          title: title.trim(),
          description: description.trim() || null,
          startDate,
          endDate: endDate || null,
          isAllDay,
          eventType,
        });
        toast.success(
          eventType === "Office" && !isAdmin
            ? "Event submitted for approval."
            : "Event saved."
        );
      }
      onSaved();
      resetForm();
    } catch {
      toast.error("Failed to save event.");
    } finally {
      setSaving(false);
    }
  }

  function resetForm() {
    setTitle("");
    setDesc("");
    setStartDate(initialDate);
    setEndDate("");
    setIsAllDay(true);
    setEventType("Office");
  }

  function handleClose() {
    resetForm();
    onClose();
  }

  if (!open) return null;

  return (
    <Modal
      title={isEditing ? "Edit Calendar Event" : "Add Calendar Event"}
      size="sm"
      onClose={handleClose}
      footer={
        <>
          <Modal.SecondaryButton onClick={handleClose}>Cancel</Modal.SecondaryButton>
          <Modal.PrimaryButton onClick={handleSubmit} loading={saving}>
            {submitLabel}
          </Modal.PrimaryButton>
        </>
      }
    >
      <div className="space-y-4">
        {/* Title */}
        <div>
          <label className="block text-sm font-medium text-slate-700 mb-1">
            Title <span className="text-red-500">*</span>
          </label>
          <input
            type="text"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            className="w-full border border-slate-300 px-3 py-2 text-sm text-slate-800 focus:outline-none focus:ring-1 focus:ring-green-500 focus:border-green-500"
            placeholder="Event title"
            maxLength={200}
          />
        </div>

        {/* Description */}
        <div>
          <label className="block text-sm font-medium text-slate-700 mb-1">Description</label>
          <textarea
            value={description}
            onChange={(e) => setDesc(e.target.value)}
            className="w-full border border-slate-300 px-3 py-2 text-sm text-slate-800 focus:outline-none focus:ring-1 focus:ring-green-500 focus:border-green-500 resize-none"
            rows={3}
            placeholder="Optional description"
          />
        </div>

        {/* Date range */}
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="block text-sm font-medium text-slate-700 mb-1">
              Start Date <span className="text-red-500">*</span>
            </label>
            <input
              type="date"
              value={startDate}
              onChange={(e) => {
                setStartDate(e.target.value);
                // Clear end date if it's now before the new start
                if (endDate && endDate < e.target.value) setEndDate("");
              }}
              className="w-full border border-slate-300 px-3 py-2 text-sm text-slate-800 focus:outline-none focus:ring-1 focus:ring-green-500 focus:border-green-500"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-slate-700 mb-1">
              End Date <span className="text-slate-600 font-normal">(optional)</span>
            </label>
            <input
              type="date"
              value={endDate}
              min={startDate}
              onChange={(e) => setEndDate(e.target.value)}
              className="w-full border border-slate-300 px-3 py-2 text-sm text-slate-800 focus:outline-none focus:ring-1 focus:ring-green-500 focus:border-green-500"
            />
          </div>
        </div>

        {/* All day toggle */}
        <div className="flex items-center gap-2">
          <input
            id="isAllDay"
            type="checkbox"
            checked={isAllDay}
            onChange={(e) => setIsAllDay(e.target.checked)}
            className="accent-green-600"
          />
          <label htmlFor="isAllDay" className="text-sm text-slate-700">All day</label>
        </div>

        {/* Event type — fixed at creation, not editable */}
        <div>
          <label className="block text-sm font-medium text-slate-700 mb-2">Type</label>
          {isEditing ? (
            <span className="inline-block px-2 py-0.5 text-xs font-medium bg-slate-100 text-slate-700">
              {eventType}
            </span>
          ) : (
            <div className="flex gap-4">
              {(["Office", "Personal"] as const).map((t) => (
                <label key={t} className="flex items-center gap-2 text-sm text-slate-700 cursor-pointer">
                  <input
                    type="radio"
                    name="eventType"
                    value={t}
                    checked={eventType === t}
                    onChange={() => setEventType(t)}
                    className="accent-green-600"
                  />
                  {t}
                </label>
              ))}
            </div>
          )}
          {eventType === "Office" && !isAdmin && (
            isEditing ? (
              editingEvent!.status !== "Pending" && (
                <p className="mt-1.5 text-xs text-amber-600">
                  Saving will resubmit this event for admin approval.
                </p>
              )
            ) : (
              <p className="mt-1.5 text-xs text-amber-600">
                Office events require admin approval before they appear to others.
              </p>
            )
          )}
        </div>
      </div>
    </Modal>
  );
}

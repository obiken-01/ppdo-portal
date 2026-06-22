"use client";

import { useState } from "react";
import Modal from "@/components/ui/Modal";
import { useToast } from "@/components/ui/Toast";
import { createCalendarEvent } from "@/lib/dashboard";

interface CreateEventModalProps {
  open: boolean;
  initialDate: string;
  isAdmin: boolean;
  onClose: () => void;
  onCreated: () => void;
}

export default function CreateEventModal({
  open,
  initialDate,
  isAdmin,
  onClose,
  onCreated,
}: CreateEventModalProps) {
  const { toast } = useToast();

  const [title, setTitle]         = useState("");
  const [description, setDesc]    = useState("");
  const [startDate, setStartDate] = useState(initialDate);
  const [isAllDay, setIsAllDay]   = useState(true);
  const [eventType, setEventType] = useState<"Office" | "Personal">("Office");
  const [saving, setSaving]       = useState(false);

  const submitLabel =
    eventType === "Office" && !isAdmin ? "Submit for Approval" : "Save Event";

  async function handleSubmit() {
    if (!title.trim()) {
      toast.error("Title is required.");
      return;
    }
    setSaving(true);
    try {
      await createCalendarEvent({
        title: title.trim(),
        description: description.trim() || null,
        startDate,
        isAllDay,
        eventType,
      });
      toast.success(
        eventType === "Office" && !isAdmin
          ? "Event submitted for approval."
          : "Event saved."
      );
      onCreated();
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
      title="Add Calendar Event"
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

        {/* Date */}
        <div>
          <label className="block text-sm font-medium text-slate-700 mb-1">Date</label>
          <input
            type="date"
            value={startDate}
            onChange={(e) => setStartDate(e.target.value)}
            className="w-full border border-slate-300 px-3 py-2 text-sm text-slate-800 focus:outline-none focus:ring-1 focus:ring-green-500 focus:border-green-500"
          />
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

        {/* Event type */}
        <div>
          <label className="block text-sm font-medium text-slate-700 mb-2">Type</label>
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
          {eventType === "Office" && !isAdmin && (
            <p className="mt-1.5 text-xs text-amber-600">
              Office events require admin approval before they appear to others.
            </p>
          )}
        </div>
      </div>
    </Modal>
  );
}

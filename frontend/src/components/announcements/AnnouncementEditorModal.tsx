"use client";

import { useEffect, useState } from "react";
import { useEditor, EditorContent } from "@tiptap/react";
import StarterKit from "@tiptap/starter-kit";
import { TextStyle } from "@tiptap/extension-text-style";
import { Color } from "@tiptap/extension-color";
import FontFamily from "@tiptap/extension-font-family";
import Underline from "@tiptap/extension-underline";
import { Indent } from "./IndentExtension";
import Modal from "@/components/ui/Modal";
import RichTextToolbar from "./RichTextToolbar";
import { useToast } from "@/components/ui/Toast";
import {
  createAnnouncement,
  updateAnnouncement,
  publishAnnouncement,
  announcementErrorMessage,
} from "@/lib/announcements";
import type { AnnouncementDto } from "@/types";

interface AnnouncementEditorModalProps {
  open: boolean;
  /** Null = create mode; non-null = edit mode. */
  announcement: AnnouncementDto | null;
  onClose: () => void;
  /** Called after a successful save or publish so the list can refresh. */
  onSaved: () => void;
}

export default function AnnouncementEditorModal({
  open,
  announcement,
  onClose,
  onSaved,
}: AnnouncementEditorModalProps) {
  const { toast } = useToast();
  const [title, setTitle] = useState("");
  const [saving, setSaving] = useState(false);

  const editor = useEditor({
    extensions: [StarterKit, TextStyle, Color, FontFamily, Underline, Indent],
    content: "",
    editorProps: {
      attributes: {
        class:
          "tiptap-editor min-h-[220px] p-3 focus:outline-none text-sm text-slate-800 leading-relaxed",
      },
    },
  });

  // Populate form fields whenever the modal opens or the target changes.
  useEffect(() => {
    if (!open) return;
    if (announcement) {
      setTitle(announcement.title);
      editor?.commands.setContent(announcement.content ?? "");
    } else {
      setTitle("");
      editor?.commands.setContent("");
    }
  }, [open, announcement, editor]);

  async function handleSaveDraft() {
    const content = editor?.getHTML() ?? "";
    if (!title.trim()) {
      toast.error("Title required.", "Please enter a title before saving.");
      return;
    }
    setSaving(true);
    try {
      if (announcement) {
        await updateAnnouncement(announcement.id, { title: title.trim(), content });
        toast.success("Announcement updated.", "Saved as draft.");
      } else {
        await createAnnouncement({ title: title.trim(), content });
        toast.success("Announcement created.", "Saved as draft.");
      }
      onSaved();
      onClose();
    } catch (err) {
      toast.error("Save failed.", announcementErrorMessage(err, "Could not save the announcement."));
    } finally {
      setSaving(false);
    }
  }

  async function handlePublishNow() {
    const content = editor?.getHTML() ?? "";
    if (!title.trim()) {
      toast.error("Title required.", "Please enter a title before publishing.");
      return;
    }
    setSaving(true);
    try {
      let saved: AnnouncementDto;
      if (announcement) {
        saved = await updateAnnouncement(announcement.id, { title: title.trim(), content });
      } else {
        saved = await createAnnouncement({ title: title.trim(), content });
      }
      await publishAnnouncement(saved.id);
      toast.success("Announcement published.", `"${title.trim()}" is now live.`);
      onSaved();
      onClose();
    } catch (err) {
      toast.error(
        "Publish failed.",
        announcementErrorMessage(err, "Could not publish the announcement."),
      );
    } finally {
      setSaving(false);
    }
  }

  if (!open) return null;

  return (
    <Modal
      title={announcement ? "Edit Announcement" : "New Announcement"}
      size="xl"
      onClose={onClose}
      footer={
        <>
          <Modal.SecondaryButton onClick={onClose} disabled={saving}>
            Cancel
          </Modal.SecondaryButton>
          <button
            type="button"
            onClick={handleSaveDraft}
            disabled={saving}
            className="px-4 py-2 text-sm border border-green-700 text-green-700 bg-white hover:bg-green-50 transition-colors font-medium disabled:opacity-60"
          >
            {saving ? "Saving…" : "Save as Draft"}
          </button>
          <Modal.PrimaryButton onClick={handlePublishNow} disabled={saving} loading={saving}>
            Publish Now
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
            placeholder="Announcement title"
            disabled={saving}
            className="w-full border border-slate-200 px-3 py-2 text-sm text-slate-800 focus:outline-none focus:ring-2 focus:ring-green-500 focus:border-green-500 disabled:bg-slate-50"
          />
        </div>

        {/* Rich-text content */}
        <div>
          <label className="block text-sm font-medium text-slate-700 mb-1">Content</label>
          <div className="border border-slate-200">
            {editor && <RichTextToolbar editor={editor} />}
            <EditorContent editor={editor} />
          </div>
        </div>
      </div>
    </Modal>
  );
}

"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import api from "@/lib/api";
import {
  getAnnouncementsManage,
  publishAnnouncement,
  unpublishAnnouncement,
  archiveAnnouncement,
  deleteAnnouncement,
  announcementErrorMessage,
} from "@/lib/announcements";
import type { AnnouncementDto, MeResponse } from "@/types";
import DataTable, { type Column } from "@/components/ui/DataTable";
import AnnouncementEditorModal from "@/components/announcements/AnnouncementEditorModal";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import { useToast } from "@/components/ui/Toast";

const STATUS_BADGE_CLS: Record<string, string> = {
  Draft:     "bg-slate-100 text-slate-600",
  Published: "bg-green-100 text-green-700",
  Archived:  "bg-amber-100 text-amber-700",
};

function StatusBadge({ status }: { status: string }) {
  return (
    <span
      className={`inline-block px-2 py-0.5 text-xs font-medium rounded-full ${STATUS_BADGE_CLS[status] ?? "bg-slate-100 text-slate-500"}`}
    >
      {status}
    </span>
  );
}

function fmtDate(iso: string | null | undefined): React.ReactNode {
  if (!iso) return <span className="text-slate-400 text-xs">—</span>;
  return new Date(iso).toLocaleDateString("en-PH", {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

export default function AnnouncementsPage() {
  const router = useRouter();
  const { toast } = useToast();

  const [authChecked, setAuthChecked] = useState(false);
  const [items, setItems]             = useState<AnnouncementDto[]>([]);
  const [loading, setLoading]         = useState(true);
  const [loadError, setLoadError]     = useState<string | null>(null);
  const [editorOpen, setEditorOpen]   = useState(false);
  const [editing, setEditing]         = useState<AnnouncementDto | null>(null);
  const [actioning, setActioning]     = useState<string | null>(null);
  const [dialog, setDialog]           = useState<ConfirmDialogProps | null>(null);

  // ── Auth guard (Admin / SuperAdmin only) ──────────────────────────────────

  useEffect(() => {
    api
      .get<MeResponse>("/auth/me")
      .then((r) => {
        const me = r.data;
        if (me?.role !== "Admin" && me?.role !== "SuperAdmin") {
          router.replace("/dashboard");
          return;
        }
        setAuthChecked(true);
      })
      .catch(() => router.replace("/login"));
  }, [router]);

  // ── Data loading ──────────────────────────────────────────────────────────

  const load = useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    try {
      setItems(await getAnnouncementsManage());
    } catch {
      setLoadError("Could not load announcements.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (authChecked) load();
  }, [authChecked, load]);

  // ── Actions ───────────────────────────────────────────────────────────────

  function openCreate() {
    setEditing(null);
    setEditorOpen(true);
  }

  function openEdit(item: AnnouncementDto) {
    setEditing(item);
    setEditorOpen(true);
  }

  async function handlePublish(item: AnnouncementDto) {
    setActioning(item.id);
    try {
      await publishAnnouncement(item.id);
      toast.success("Published.", `"${item.title}" is now live.`);
      await load();
    } catch (err) {
      toast.error("Publish failed.", announcementErrorMessage(err, "Could not publish."));
    } finally {
      setActioning(null);
    }
  }

  async function handleUnpublish(item: AnnouncementDto) {
    setActioning(item.id);
    try {
      await unpublishAnnouncement(item.id);
      toast.success("Unpublished.", `"${item.title}" moved back to draft.`);
      await load();
    } catch (err) {
      toast.error("Unpublish failed.", announcementErrorMessage(err, "Could not unpublish."));
    } finally {
      setActioning(null);
    }
  }

  function handleArchive(item: AnnouncementDto) {
    setDialog({
      title:        "Archive announcement?",
      message:      `"${item.title}" will be hidden from the public landing page. You can restore it by unpublishing then republishing.`,
      confirmLabel: "Archive",
      variant:      "warning",
      onConfirm:    () => void performArchive(item),
      onClose:      () => setDialog(null),
    });
  }

  async function performArchive(item: AnnouncementDto) {
    setActioning(item.id);
    try {
      await archiveAnnouncement(item.id);
      toast.success("Archived.", `"${item.title}" has been archived.`);
      await load();
    } catch (err) {
      toast.error("Archive failed.", announcementErrorMessage(err, "Could not archive."));
    } finally {
      setActioning(null);
    }
  }

  function handleDelete(item: AnnouncementDto) {
    setDialog({
      title:        "Delete announcement?",
      message:      `"${item.title}" will be permanently removed. This cannot be undone.`,
      confirmLabel: "Delete",
      variant:      "danger",
      onConfirm:    () => void performDelete(item),
      onClose:      () => setDialog(null),
    });
  }

  async function performDelete(item: AnnouncementDto) {
    setActioning(item.id);
    try {
      await deleteAnnouncement(item.id);
      toast.success("Deleted.", `"${item.title}" has been removed.`);
      await load();
    } catch (err) {
      toast.error(
        "Delete failed.",
        announcementErrorMessage(err, "Published announcements cannot be deleted — unpublish first."),
      );
    } finally {
      setActioning(null);
    }
  }

  // ── Table columns ─────────────────────────────────────────────────────────

  const columns: Column<AnnouncementDto>[] = [
    {
      key: "title",
      header: "Title",
      sortable: true,
      render: (row) => <span className="font-medium text-slate-800">{row.title}</span>,
    },
    {
      key: "status",
      header: "Status",
      render: (row) => <StatusBadge status={row.status} />,
    },
    {
      key: "publishedAt",
      header: "Published Date",
      sortable: true,
      sortValue: (row) => row.publishedAt ?? "",
      render: (row) => fmtDate(row.publishedAt),
    },
    {
      key: "updatedAt",
      header: "Last Updated",
      sortable: true,
      sortValue: (row) => row.updatedAt,
      render: (row) => fmtDate(row.updatedAt),
    },
    {
      key: "actions",
      header: "Actions",
      align: "right",
      render: (row) => {
        const busy = actioning === row.id;
        return (
          <div className="flex items-center justify-end gap-3">
            <button
              onClick={() => openEdit(row)}
              disabled={busy}
              className="text-xs text-green-700 hover:text-green-900 font-medium disabled:opacity-50"
            >
              Edit
            </button>
            {row.status === "Draft" && (
              <button
                onClick={() => handlePublish(row)}
                disabled={busy}
                className="text-xs text-blue-600 hover:text-blue-800 font-medium disabled:opacity-50"
              >
                {busy ? "…" : "Publish"}
              </button>
            )}
            {row.status === "Published" && (
              <button
                onClick={() => handleUnpublish(row)}
                disabled={busy}
                className="text-xs text-amber-600 hover:text-amber-800 font-medium disabled:opacity-50"
              >
                {busy ? "…" : "Unpublish"}
              </button>
            )}
            {row.status !== "Archived" && (
              <button
                onClick={() => handleArchive(row)}
                disabled={busy}
                className="text-xs text-slate-500 hover:text-slate-700 disabled:opacity-50"
              >
                Archive
              </button>
            )}
            {row.status !== "Published" && (
              <button
                onClick={() => handleDelete(row)}
                disabled={busy}
                className="text-xs text-red-500 hover:text-red-700 disabled:opacity-50"
              >
                Delete
              </button>
            )}
          </div>
        );
      },
    },
  ];

  // ── Render ────────────────────────────────────────────────────────────────

  if (!authChecked) return null;

  return (
    <div className="p-6 max-w-6xl mx-auto space-y-5">
      {/* Page header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold text-slate-800">📢 Announcements</h1>
          <p className="text-sm text-slate-500 mt-0.5">
            Manage announcements displayed on the public landing page.
          </p>
        </div>
        <button
          onClick={openCreate}
          className="px-4 py-2 bg-green-700 text-white text-sm font-medium hover:bg-green-800 transition-colors"
        >
          + New Announcement
        </button>
      </div>

      {/* Table */}
      <DataTable<AnnouncementDto>
        columns={columns}
        rows={items}
        rowKey={(r) => r.id}
        loading={loading}
        error={loadError}
        onRetry={load}
        emptyMessage="No announcements yet. Click '+ New Announcement' to create one."
        pageSize={25}
        rowNoun={["announcement", "announcements"]}
      />

      {/* Editor modal */}
      <AnnouncementEditorModal
        open={editorOpen}
        announcement={editing}
        onClose={() => {
          setEditorOpen(false);
          setEditing(null);
        }}
        onSaved={load}
      />

      {/* Confirmation dialog (Archive / Delete) */}
      {dialog && <ConfirmDialog {...dialog} />}
    </div>
  );
}

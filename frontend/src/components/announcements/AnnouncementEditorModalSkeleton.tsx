/** Overlay shown while the lazy announcement editor modal downloads (PPDO-188 / O14). */
export default function AnnouncementEditorModalSkeleton() {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40" aria-hidden="true">
      <div className="w-full max-w-3xl bg-white border border-slate-200 shadow-lg animate-pulse" style={{ height: 560 }} />
    </div>
  );
}

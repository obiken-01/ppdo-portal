/**
 * Single stat card — matches the grouped stat card design in Penpot frame 04.
 * Uses PPDO stat color tokens: stat-blue, stat-amber, stat-green, stat-red, stat-purple.
 */

interface StatCardProps {
  label: string;
  value: number | string;
  /** Tailwind bg class for the card background */
  bgClass: string;
  /** Tailwind text class for the value */
  valueClass: string;
  icon?: string;
}

export default function StatCard({ label, value, bgClass, valueClass, icon }: StatCardProps) {
  return (
    <div className={`rounded-xl px-4 py-3 flex flex-col gap-1 min-w-[110px] ${bgClass}`}>
      <div className="flex items-center gap-1.5">
        {icon && <span className="text-sm leading-none">{icon}</span>}
        <p className="text-xs text-slate-500 font-medium leading-tight">{label}</p>
      </div>
      <p className={`text-2xl font-bold leading-tight ${valueClass}`}>{value}</p>
    </div>
  );
}

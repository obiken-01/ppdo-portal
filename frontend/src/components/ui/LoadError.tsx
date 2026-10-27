"use client";

// Inline "couldn't load" state with a Retry link. Use it where a failed GET would
// otherwise leave an empty list or zeros that read as real data (PPDO-260).
// Same look as the fetchError block on items-master and pr-register.

interface LoadErrorProps {
  message: string;
  onRetry: () => void;
  /** Vertical padding; tables use the default, a one-line band can pass "py-3". */
  className?: string;
}

export default function LoadError({ message, onRetry, className = "py-12" }: LoadErrorProps) {
  return (
    <div role="alert" className={`flex flex-col items-center justify-center gap-3 ${className}`}>
      <p className="text-sm text-red-500">{message}</p>
      <button type="button" onClick={onRetry} className="text-sm text-green-600 hover:underline">
        Retry
      </button>
    </div>
  );
}

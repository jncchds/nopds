export function Logo({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 64 64" className={className} aria-hidden>
      <rect width="64" height="64" rx="14" fill="#b75a18" />
      <path d="M14 18c6-2 12-2 18 2v28c-6-4-12-4-18-2z" fill="#fdf6ec" />
      <path d="M50 18c-6-2-12-2-18 2v28c6-4 12-4 18-2z" fill="#f2cc94" />
      <path d="M32 20v28" stroke="#622f18" strokeWidth="2" />
    </svg>
  )
}

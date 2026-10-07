// Presentation only. Domain transitions and capabilities belong to the backend.
export const statusPresentation = {
  NotStarted: { label: 'Не начата', tone: 'neutral', symbol: '○' },
  Submitted: { label: 'На проверке', tone: 'info', symbol: '◷' },
  NeedsRevision: { label: 'Нужна доработка', tone: 'warning', symbol: '!' },
  Accepted: { label: 'Принята', tone: 'success', symbol: '✓' },
} as const;
export type ProblemWorkStatus = keyof typeof statusPresentation;

export function StatusBadge({ status }: { status: ProblemWorkStatus }) {
  const { label, tone, symbol } = statusPresentation[status];
  return <span className={`badge badge--${tone}`} aria-label={`Статус: ${label}`}>
    <span aria-hidden="true">{symbol}</span>{label}
  </span>;
}

export function AvailabilityBadge({ isAvailable, explanation }:
  { isAvailable: boolean; explanation?: string }) {
  // isAvailable and explanation must come from the screen's server model.
  return <span className={`badge badge--${isAvailable ? 'success' : 'neutral'}`}>
    <span aria-hidden="true">{isAvailable ? '✓' : '⊘'}</span>
    {isAvailable ? 'Доступен' : explanation || 'Недоступен'}
  </span>;
}

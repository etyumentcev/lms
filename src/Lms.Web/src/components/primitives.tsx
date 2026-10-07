import { useId } from 'react';
import type { ComponentProps, ReactNode } from 'react';
import { Link } from 'react-router-dom';

export function Button({ variant = 'primary', className = '', type = 'button', ...props }:
  ComponentProps<'button'> & { variant?: 'primary' | 'secondary' }) {
  return <button {...props} type={type} className={`button button--${variant} ${className}`} />;
}

export function AsyncButton({ pending, pendingLabel = 'Выполняется…', disabled, children, ...props }:
  Parameters<typeof Button>[0] & { pending: boolean; pendingLabel?: string }) {
  return <Button {...props} disabled={disabled || pending} aria-busy={pending}>
    {pending ? pendingLabel : children}
  </Button>;
}

export function Breadcrumbs({ items }: { items: readonly { label: string; to?: string }[] }) {
  return <nav aria-label="Хлебные крошки" className="breadcrumbs"><ol>
    {items.map((item, index) => <li key={`${item.label}-${index}`}>
      {item.to && index < items.length - 1 ? <Link to={item.to}>{item.label}</Link>
        : <span aria-current={index === items.length - 1 ? 'page' : undefined}>{item.label}</span>}
    </li>)}
  </ol></nav>;
}

export function PageHeader({ title, description, meta, actions }:
  { title: string; description?: string; meta?: ReactNode; actions?: ReactNode }) {
  return <header className="page-header"><div>
    <h1>{title}</h1>{meta && <div className="page-meta">{meta}</div>}
    {description && <p className="muted">{description}</p>}
  </div>{actions && <div className="page-actions">{actions}</div>}</header>;
}

export function ProgressBar({ value, label }: { value: number; label: string }) {
  // Clamp only the visual percentage; never calculate domain progress here.
  const percent = Number.isFinite(value) ? Math.min(100, Math.max(0, value)) : 0;
  return <div className="progress"><div className="progress-label"><span>{label}</span><span>{percent}%</span></div>
    <div role="progressbar" aria-label={label} aria-valuenow={percent} aria-valuemin={0} aria-valuemax={100}
      className="progress-track"><div style={{ width: `${percent}%` }} /></div>
  </div>;
}

export function EmptyState({ title, description, action }: { title: string; description?: string; action?: ReactNode }) {
  return <div className="empty-state"><span className="empty-mark" aria-hidden="true">▤</span>
    <h2>{title}</h2>{description && <p className="muted">{description}</p>}{action}
  </div>;
}

export function ErrorState({ title = 'Не удалось загрузить данные', description, onRetry }:
  { title?: string; description?: string; onRetry?: () => void }) {
  return <div className="error-state"><div role="alert"><h2>{title}</h2>
    {description && <p>{description}</p>}</div>
    {onRetry && <Button variant="secondary" onClick={onRetry}>Повторить</Button>}
  </div>;
}

export function InlineError({ id, children }: { id?: string; children: ReactNode }) {
  return <p id={id} role="alert" className="inline-error">{children}</p>;
}

export function LoadingSkeleton({ label = 'Загрузка…', rows = 3 }: { label?: string; rows?: number }) {
  return <div role="status" aria-label={label} className="loading-skeleton">
    <span className="sr-only">{label}</span>
    {Array.from({ length: Math.max(1, Math.min(10, rows)) }, (_, index) =>
      <div key={index} className="skeleton-line" aria-hidden="true" />)}
  </div>;
}

export function Section({ title, children }: { title: string; children: ReactNode }) {
  const id = useId();
  return <section className="section" aria-labelledby={id}><h2 id={id}>{title}</h2>{children}</section>;
}

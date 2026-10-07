import { useId, useRef } from 'react';
import type { KeyboardEvent, ReactNode } from 'react';

export type TabItem = { id: string; label: string; content: ReactNode; disabled?: boolean };

/** Automatic activation: suitable for already-loaded panels, not network-bound tabs. */
export function Tabs({ label, items, value, onChange }:
  { label: string; items: readonly TabItem[]; value: string; onChange: (id: string) => void }) {
  const prefix = useId();
  const refs = useRef(new Map<string, HTMLButtonElement>());
  const enabled = items.filter(item => !item.disabled);
  const selected = enabled.find(item => item.id === value)?.id ?? enabled[0]?.id;
  function navigate(event: KeyboardEvent<HTMLButtonElement>, id: string) {
    const index = enabled.findIndex(item => item.id === id);
    let next: number;
    switch (event.key) {
      case 'ArrowRight': next = (index + 1) % enabled.length; break;
      case 'ArrowLeft': next = (index - 1 + enabled.length) % enabled.length; break;
      case 'Home': next = 0; break;
      case 'End': next = enabled.length - 1; break;
      default: return;
    }
    event.preventDefault();
    const target = enabled[next];
    if (target) { onChange(target.id); refs.current.get(target.id)?.focus(); }
  }
  return <div className="tabs">
    <div role="tablist" aria-label={label} className="tab-list">
      {items.map(item => <button key={item.id} type="button" role="tab"
        id={`${prefix}-tab-${item.id}`} aria-controls={`${prefix}-panel-${item.id}`}
        aria-selected={selected === item.id} disabled={item.disabled}
        tabIndex={selected === item.id ? 0 : -1}
        ref={node => { if (node) refs.current.set(item.id, node); else refs.current.delete(item.id); }}
        onClick={() => onChange(item.id)} onKeyDown={event => navigate(event, item.id)}>{item.label}</button>)}
    </div>
    {items.map(item => <div key={item.id} role="tabpanel" id={`${prefix}-panel-${item.id}`}
      aria-labelledby={`${prefix}-tab-${item.id}`} hidden={selected !== item.id} tabIndex={0}
      className="tab-panel">{item.content}</div>)}
  </div>;
}

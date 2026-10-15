import { useEffect, useId, useRef, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { Button } from '../components/primitives';

export type NavigationRole = 'Student' | 'Teacher';
export type SidebarSession = { organizationName: string; displayName: string; roleLabel: string };

const commonItems = [
  { label: 'Главная', to: '/', icon: 'home' },
  { label: 'Расписание', to: '/schedule', icon: '▦' },
] as const;

export function navigationItems(role: NavigationRole | null, organizationName = 'Главная') {
  return [{ ...commonItems[0], label: organizationName }, ...commonItems.slice(1),
    ...(role === null ? [] : [role === 'Teacher' ? { label: 'Проверка', to: '/teacher/reviews', icon: '✓' }
      : { label: 'Текущие задачи', to: '/tasks', icon: '☷' }]),
    { label: 'Архив задач', to: '/archive', icon: '▣' },
    { label: 'Прогресс', to: '/progress', icon: '↗' },
    { label: 'Сообщения', to: '/messages', icon: '▱' },
  ];
}

function initials(name: string) {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  return (parts.length > 1 ? parts.slice(0, 2) : parts).map(part => part[0]).join('').toLocaleUpperCase('ru-RU');
}

function NavigationIcon({ icon }: { icon: string }) {
  if (icon === 'home') return <svg aria-hidden="true" viewBox="0 0 20 20" fill="none" stroke="currentColor"
    strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
    <path d="m3 9.25 7-6 7 6V17a1 1 0 0 1-1 1h-4v-5H8v5H4a1 1 0 0 1-1-1V9.25Z" />
  </svg>;
  return <>{icon}</>;
}

export function UserMenu({ session, onLogout }: { session: SidebarSession; onLogout: () => void | Promise<void> }) {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const menuItem = useRef<HTMLButtonElement>(null);
  const menuId = useId();
  useEffect(() => {
    if (!open) return;
    menuItem.current?.focus();
    const outside = (event: PointerEvent) => {
      if (!root.current?.contains(event.target as Node)) setOpen(false);
    };
    const escape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { setOpen(false); trigger.current?.focus(); }
    };
    document.addEventListener('pointerdown', outside);
    document.addEventListener('keydown', escape);
    return () => {
      document.removeEventListener('pointerdown', outside);
      document.removeEventListener('keydown', escape);
    };
  }, [open]);
  return <div className="user-menu" ref={root}>
    {open && <div className="user-menu-popover" id={menuId} role="menu" aria-label="Меню пользователя">
      <button type="button" role="menuitem" ref={menuItem} onClick={() => { setOpen(false); void onLogout(); }}>
        <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><path d="M10 5H5v14h5M14 8l4 4-4 4M18 12H9"/></svg>
        <span>Выйти</span>
      </button>
    </div>}
    <button type="button" className="user-menu-trigger" ref={trigger} aria-haspopup="menu" aria-controls={menuId}
      aria-expanded={open} onClick={() => setOpen(value => !value)} onKeyDown={event => {
        if (!open && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) { event.preventDefault(); setOpen(true); }
      }}>
      <span className="avatar" aria-hidden="true">{initials(session.displayName)}</span>
      <span className="user-menu-copy"><strong title={session.displayName}>{session.displayName}</strong><span title={session.roleLabel}>{session.roleLabel}</span></span>
      <svg className="user-menu-chevron" aria-hidden="true" viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.8"><path d={open ? 'm5 12 5-5 5 5' : 'm5 8 5 5 5-5'}/></svg>
    </button>
  </div>;
}

export function SidebarNavigation({ role, session, onLogout }: {
  role: NavigationRole | null; session?: SidebarSession; onLogout?: () => void | Promise<void>;
}) {
  const [expanded, setExpanded] = useState(false);
  const toggle = useRef<HTMLButtonElement>(null);
  const navId = useId();
  const { pathname } = useLocation();
  function isActive(to: string) {
    if (to === '/tasks') return pathname === to || pathname.startsWith('/problem-assignments/');
    return to === '/' ? pathname === '/' : pathname === to || pathname.startsWith(`${to}/`);
  }
  return <div className="sidebar" data-expanded={expanded} onKeyDown={event => {
    if (event.key === 'Escape' && expanded) { setExpanded(false); toggle.current?.focus(); }
  }}>
    <div className="brand-row"><Link to="/" className="brand" aria-label="Sapientum — главная">
      <svg aria-hidden="true" viewBox="0 0 28 28" fill="none"><path d="M4 23v-7m6 7V12m6 11V8m6 15V3M3 11l7-5 6 1 7-6" stroke="currentColor" strokeWidth="2" /></svg>
      <span>Sapientum</span></Link>
      <Button variant="secondary" className="nav-toggle" ref={toggle} aria-expanded={expanded}
        aria-controls={navId} onClick={() => setExpanded(!expanded)}>Меню</Button>
    </div>
    <nav id={navId} aria-label="Основная навигация" className="sidebar-nav">
      <ul>{navigationItems(role, session?.organizationName).map(item => <li key={item.to}><Link to={item.to}
        aria-label={item.label} title={item.label} aria-current={isActive(item.to) ? 'page' : undefined}
        onClick={() => { setExpanded(false); }}>
        <span className="nav-icon" aria-hidden="true"><NavigationIcon icon={item.icon} /></span><span className="nav-label">{item.label}</span>
      </Link></li>)}</ul>
    </nav>
    {session && onLogout && <div className="sidebar-footer"><UserMenu session={session} onLogout={onLogout} /></div>}
  </div>;
}

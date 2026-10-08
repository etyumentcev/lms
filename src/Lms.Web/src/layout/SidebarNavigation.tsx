import { useId, useRef, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { Button } from '../components/primitives';

export type NavigationRole = 'Student' | 'Teacher';
const commonItems = [
  { label: 'Главная', to: '/', icon: '⌂' },
  { label: 'Мои курсы', to: '/courses', icon: '▤' },
  { label: 'Расписание', to: '/schedule', icon: '▦' },
] as const;
export function navigationItems(role: NavigationRole | null) {
  return [...commonItems,
    ...(role === null ? [] : [role === 'Teacher' ? { label: 'Проверка', to: '/teacher/reviews', icon: '✓' }
      : { label: 'Текущие задачи', to: '/tasks', icon: '☷' }]),
    { label: 'Архив задач', to: '/archive', icon: '▣' },
    { label: 'Прогресс', to: '/progress', icon: '↗' },
    { label: 'Сообщения', to: '/messages', icon: '▱' },
  ];
}

export function SidebarNavigation({ role, preview = true }: { role: NavigationRole | null; preview?: boolean }) {
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
      <ul>{navigationItems(role).map(item => <li key={item.to}><Link to={item.to}
        aria-label={item.label} title={item.label} aria-current={isActive(item.to) ? 'page' : undefined}
        onClick={() => { setExpanded(false); }}>
        <span className="nav-icon" aria-hidden="true">{item.icon}</span><span className="nav-label">{item.label}</span>
      </Link></li>)}</ul>
    </nav>
    <div className="sidebar-footer"><span className="avatar" aria-hidden="true">S</span>
      <div className="nav-label"><strong>{preview ? 'Демо-пространство' : 'Учебное пространство'}</strong>{role && <span>{role === 'Teacher' ? 'Преподаватель' : 'Ученик'}</span>}</div>
    </div>
  </div>;
}

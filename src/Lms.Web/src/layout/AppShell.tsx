import { useId, useState } from 'react';
import type { ReactNode } from 'react';
import { Button } from '../components/primitives';
import { SidebarNavigation } from './SidebarNavigation';
import type { NavigationRole } from './SidebarNavigation';
import { useSession } from '../auth/session';

export function ContextSection({ title, children }: { title: string; children: ReactNode }) {
  const id = useId();
  return <section className="context-section" aria-labelledby={id}><h3 id={id}>{title}</h3>{children}</section>;
}

export function ContextPanel({ children, title = 'Контекст' }: { children: ReactNode; title?: string }) {
  const [expanded, setExpanded] = useState(true);
  const headingId = useId();
  const bodyId = useId();
  return <aside className="context-panel" aria-labelledby={headingId} data-expanded={expanded}>
    <div className="context-heading"><h2 id={headingId}>{title}</h2>
      <Button variant="secondary" className="context-toggle" aria-expanded={expanded} aria-controls={bodyId}
        onClick={() => setExpanded(!expanded)}>{expanded ? 'Свернуть контекст' : 'Развернуть контекст'}</Button>
    </div>
    <div className="context-body" id={bodyId}>{children}</div>
  </aside>;
}

export function MainContent({ children }: { children: ReactNode }) {
  return <main id="main-content" className="main-content" tabIndex={-1}>{children}</main>;
}

export function AppShell({ role, children, context, preview: _preview = true }: { role: NavigationRole | null; children: ReactNode; context: ReactNode; preview?: boolean }) {
  const { state, controller } = useSession();
  const session = state.phase === 'authenticated' ? {
    organizationName: state.session.organizationName,
    displayName: state.session.displayName,
    roleLabel: state.session.hasOrganizationAdminScope ? 'Администратор'
      : state.session.role === 'Teacher' ? 'Преподаватель'
        : state.session.role === 'Student' ? 'Ученик' : 'Нет учебных полномочий'
  } : undefined;
  return <><a className="skip-link" href="#main-content">К основному содержимому</a>
    <div className="app-shell"><SidebarNavigation role={role} session={session} onLogout={session ? () => controller.logout() : undefined} />
      <MainContent>{children}</MainContent><ContextPanel>{context}</ContextPanel>
    </div>
  </>;
}

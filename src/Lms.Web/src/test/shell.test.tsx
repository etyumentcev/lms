import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { AppShell, ContextSection } from '../layout/AppShell';
import { SidebarNavigation } from '../layout/SidebarNavigation';

function renderApp(path = '/') { return render(<MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }} initialEntries={[path]}><App /></MemoryRouter>); }

describe('Shared shell', () => {
  it('composes navigation, main and one required contextual region', () => {
    render(<MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}><AppShell role="Student" context={<ContextSection title="Контекст задачи">Важное действие</ContextSection>}>
      <h1>Workspace</h1></AppShell></MemoryRouter>);
    expect(screen.getByRole('navigation', { name: 'Основная навигация' })).toBeInTheDocument();
    expect(within(screen.getByRole('main')).getByRole('heading', { level: 1 })).toHaveTextContent('Workspace');
    expect(screen.getAllByRole('complementary')).toHaveLength(1);
    expect(within(screen.getByRole('complementary')).getByText('Важное действие')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'К основному содержимому' })).toHaveAttribute('href', '#main-content');
  });
  it.each(['Student', 'Teacher'] as const)('uses role-specific sidebar items for %s', role => {
    render(<MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}><SidebarNavigation role={role} /></MemoryRouter>);
    const nav = within(screen.getByRole('navigation'));
    expect(nav.getAllByRole('link')).toHaveLength(7);
    expect(nav.getByRole('link', { name: role === 'Student' ? 'Текущие задачи' : 'Проверка' })).toBeInTheDocument();
    expect(nav.queryByRole('link', { name: role === 'Student' ? 'Проверка' : 'Текущие задачи' })).not.toBeInTheDocument();
  });
  it.each([['Student', 'Ученик'], ['Teacher', 'Преподаватель'], [null, 'Администратор']] as const)(
    'renders current user data for %s role', (role, roleLabel) => {
      render(<MemoryRouter><SidebarNavigation role={role}
        session={{ organizationName: 'Малый матфак', displayName: 'Тюменцев Евгений', roleLabel }}
        onLogout={vi.fn()} /></MemoryRouter>);
      expect(screen.getByRole('button', { name: new RegExp(`Тюменцев Евгений.*${roleLabel}`) })).toBeVisible();
    });
  it('uses the active organization and an accessible user menu with real session data', async () => {
    const logout = vi.fn();
    const displayName = 'Тюменцев Евгений Александрович С Очень Длинной Фамилией';
    const organizationName = 'Малый матфак с очень длинным названием организации';
    render(<MemoryRouter><SidebarNavigation role="Student" session={{ organizationName, displayName, roleLabel: 'Ученик' }} onLogout={logout} /></MemoryRouter>);
    const organization = screen.getByRole('link', { name: organizationName });
    expect(organization).toHaveAttribute('href', '/');
    expect(organization).toHaveAttribute('title', organizationName);
    expect(organization.querySelector('.nav-icon svg')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Главная' })).toBeNull();
    expect(screen.queryByText(/11111111|organizationId/i)).toBeNull();
    const trigger = screen.getByRole('button', { name: /Тюменцев Евгений.*Ученик/ });
    expect(within(trigger).getByText('ТЕ')).toBeInTheDocument();
    expect(within(trigger).getByText(displayName)).toHaveAttribute('title', displayName);
    await userEvent.click(trigger);
    expect(trigger).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('menuitem', { name: 'Выйти' })).toBeVisible();
    expect(screen.getByRole('menuitem', { name: 'Выйти' })).toHaveFocus();
    await userEvent.click(trigger);
    expect(screen.queryByRole('menuitem', { name: 'Выйти' })).toBeNull();
    await userEvent.click(trigger);
    await userEvent.click(document.body);
    expect(trigger).toHaveAttribute('aria-expanded', 'false');
    await userEvent.click(trigger);
    await userEvent.keyboard('{Escape}');
    expect(trigger).toHaveAttribute('aria-expanded', 'false');
    expect(trigger).toHaveFocus();
    await userEvent.keyboard('{ArrowDown}');
    expect(screen.getByRole('menuitem', { name: 'Выйти' })).toHaveFocus();
    await userEvent.keyboard('{Escape}');
    expect(trigger).toHaveFocus();
    await userEvent.click(trigger);
    await userEvent.click(screen.getByRole('menuitem', { name: 'Выйти' }));
    expect(logout).toHaveBeenCalledOnce();
  });
  it('does not expose the former demo navigation fallback', () => {
    renderApp();
    expect(screen.getByRole('heading', { level: 2, name: 'Страница не найдена' })).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Навигация' })).not.toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Основная навигация' })).not.toBeInTheDocument();
  });
  it('keeps the same context DOM and input state when toggled', async () => {
    render(<MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}><AppShell role="Teacher" context={<label>Заметка<input defaultValue="Контекст сохранён" /></label>}>
      <h1>Workspace</h1></AppShell></MemoryRouter>);
    const input = screen.getByRole('textbox');
    await userEvent.click(screen.getByRole('button', { name: 'Свернуть контекст' }));
    expect(screen.getByRole('button', { name: 'Развернуть контекст' })).toHaveAttribute('aria-expanded', 'false');
    await userEvent.click(screen.getByRole('button', { name: 'Развернуть контекст' }));
    expect(screen.getByRole('textbox')).toBe(input);
    expect(input).toHaveValue('Контекст сохранён');
    // CSS geometry and mobile restoration are checked in the real-browser smoke script.
    expect(screen.getByRole('main').compareDocumentPosition(screen.getByRole('complementary')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
  it('closes compact navigation with Escape and restores toggle focus', async () => {
    render(<MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}><AppShell role="Student" context={null}>
      <h1>Workspace</h1></AppShell></MemoryRouter>);
    const menu = screen.getByRole('button', { name: 'Меню' });
    await userEvent.click(menu);
    expect(menu).toHaveAttribute('aria-expanded', 'true');
    await userEvent.keyboard('{Escape}');
    expect(menu).toHaveAttribute('aria-expanded', 'false');
    expect(menu).toHaveFocus();
  });
});

describe('Routes inside shell', () => {
  it.each([
    ['/courses', 'Мои курсы'],
  ])('%s loads server courses inside shell', (path, title) => {
    renderApp(path);
    expect(within(screen.getByRole('main')).getByRole('heading', { level: 1, name: title })).toBeInTheDocument();
    expect(screen.getByRole('status', { name: 'Загрузка курсов' })).toBeInTheDocument();
    expect(screen.getByRole('complementary')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Отправить|Принять решение/ })).not.toBeInTheDocument();
    expect(document.title).toBe(`${title} · Sapientum`);
  });
  it.each(['', '?tab=history'])('Problem route keeps shell while loading %s', search => {
    renderApp(`/problem-assignments/assignment-1${search}`);
    expect(screen.getByRole('status', { name: 'Загрузка условия задачи' })).toBeInTheDocument();
    expect(screen.getByRole('complementary')).toBeInTheDocument();
  });
  it('Teacher Review route keeps shell while loading the real query', () => {
    renderApp('/teacher/reviews/work-1');
    expect(screen.getByRole('status', { name: 'Загрузка решения' })).toBeInTheDocument();
    expect(screen.getByRole('complementary')).toBeInTheDocument();
  });
  it('Queue route keeps shell while loading the real query', () => {
    renderApp('/teacher/reviews');
    expect(screen.getByRole('status', { name: 'Загрузка работ' })).toBeInTheDocument();
    expect(screen.getByRole('complementary')).toBeInTheDocument();
  });
  it('Lesson route keeps shell while loading the real query', () => {
    renderApp('/courses/algebra/lessons/fractions');
    expect(screen.getByRole('status', { name: 'Загрузка задач' })).toBeInTheDocument();
    expect(screen.getByRole('complementary')).toBeInTheDocument();
  });
  it('keeps the attachment harness on the DEV-only route', () => {
    renderApp('/dev/attachments');
    expect(screen.getByRole('heading', { level: 1, name: 'Тест загрузки фотографий · U1' })).toBeInTheDocument();
  });
  it('fails closed for unknown routes without composing the demo shell', () => {
    renderApp('/missing');
    expect(screen.getByRole('heading', { level: 2, name: 'Страница не найдена' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'На главную' })).toHaveAttribute('href', '/');
    expect(screen.queryByRole('complementary')).not.toBeInTheDocument();
  });
});

import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
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
  it('changes demo menu role without claiming authentication', async () => {
    renderApp();
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Навигация' }), 'Teacher');
    expect(within(screen.getByRole('navigation', { name: 'Основная навигация' })).getByRole('link', { name: 'Проверка' })).toBeInTheDocument();
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
    renderApp();
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
    ['/problem-assignments/assignment-1', 'Задача · Решение'],
    ['/problem-assignments/assignment-1?tab=history', 'Задача · История'],
    ['/teacher/reviews', 'Проверка'],
    ['/teacher/reviews/work-1', 'Проверка решения'],
  ])('%s is only a placeholder', (path, title) => {
    renderApp(path);
    expect(within(screen.getByRole('main')).getByRole('heading', { level: 1, name: title })).toBeInTheDocument();
    expect(screen.getByText('Раздел готовится')).toBeInTheDocument();
    expect(screen.getByRole('complementary')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Отправить|Принять решение/ })).not.toBeInTheDocument();
    expect(document.title).toBe(`${title} · Sapientum`);
  });
  it('Lesson route keeps shell while loading the real query', () => {
    renderApp('/courses/algebra/lessons/fractions');
    expect(screen.getByRole('status', { name: 'Загрузка задач' })).toBeInTheDocument();
    expect(screen.getByRole('complementary')).toBeInTheDocument();
  });
  it('navigates from the demo and focuses main content', async () => {
    renderApp();
    await userEvent.click(screen.getByRole('link', { name: /Урок.*Задачи и контекст урока/ }));
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Занятие');
    expect(screen.getByRole('main')).toHaveFocus();
    expect(within(screen.getByRole('navigation', { name: 'Основная навигация' })).getByRole('link', { name: 'Мои курсы' })).toHaveAttribute('aria-current', 'page');
  });
  it('keeps unknown routes in the shell', () => {
    renderApp('/missing');
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Страница не найдена');
    expect(screen.getByRole('complementary')).toBeInTheDocument();
  });
});

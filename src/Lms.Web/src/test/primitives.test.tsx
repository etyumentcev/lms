import { useState } from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { AsyncButton, Button, EmptyState, ErrorState, InlineError, LoadingSkeleton, ProgressBar } from '../components/primitives';
import { AvailabilityBadge, StatusBadge } from '../components/StatusBadge';
import { Tabs } from '../components/Tabs';

function TestTabs() {
  const [value, setValue] = useState('one');
  return <Tabs label="Разделы" value={value} onChange={setValue} items={[
    { id: 'one', label: 'Первая', content: 'Первый блок' },
    { id: 'disabled', label: 'Недоступная', disabled: true, content: 'Нет доступа' },
    { id: 'two', label: 'Вторая', content: 'Второй блок' },
    { id: 'three', label: 'Третья', content: 'Третий блок' },
  ]} />;
}

describe('Tabs accessibility', () => {
  it('supports arrows, wrapping, Home/End, disabled skipping and one tab stop', async () => {
    render(<TestTabs />);
    const user = userEvent.setup();
    await user.tab();
    const first = screen.getByRole('tab', { name: 'Первая' });
    expect(first).toHaveFocus();
    await user.keyboard('{ArrowRight}');
    expect(screen.getByRole('tab', { name: 'Вторая' })).toHaveFocus();
    expect(screen.getByRole('tabpanel')).toHaveTextContent('Второй блок');
    await user.keyboard('{End}');
    expect(screen.getByRole('tab', { name: 'Третья' })).toHaveFocus();
    await user.keyboard('{ArrowRight}');
    expect(first).toHaveFocus();
    await user.keyboard('{ArrowLeft}');
    expect(screen.getByRole('tab', { name: 'Третья' })).toHaveFocus();
    await user.keyboard('{Home}');
    expect(first).toHaveFocus();
    expect(first).toHaveAttribute('aria-selected', 'true');
    expect(screen.getAllByRole('tab').filter(tab => tab.tabIndex === 0)).toHaveLength(1);
    const panel = screen.getByRole('tabpanel');
    expect(first).toHaveAttribute('aria-controls', panel.id);
    expect(panel).toHaveAttribute('aria-labelledby', first.id);
    await user.tab();
    expect(panel).toHaveFocus();
  });
  it('activates by click with correctly labelled panels', async () => {
    render(<TestTabs />);
    await userEvent.click(screen.getByRole('tab', { name: 'Вторая' }));
    expect(screen.getByRole('tabpanel', { name: 'Вторая' })).toBeVisible();
    expect(screen.queryByRole('tabpanel', { name: 'Первая' })).not.toBeInTheDocument();
  });
});

it.each([
  ['NotStarted', 'Не начата'], ['Submitted', 'На проверке'],
  ['NeedsRevision', 'Нужна доработка'], ['Accepted', 'Принята'],
] as const)('StatusBadge %s exposes canonical text', (status, label) => {
  render(<StatusBadge status={status} />);
  expect(screen.getByLabelText(`Статус: ${label}`)).toHaveTextContent(label);
});
it('AvailabilityBadge preserves a supplied explanation without calculating dates', () => {
  render(<AvailabilityBadge isAvailable={false} explanation="Откроется 12 октября" />);
  expect(screen.getByText('Откроется 12 октября')).toBeInTheDocument();
});
it('pending AsyncButton prevents repeat actions and exposes pending state', async () => {
  const action = vi.fn();
  const { rerender } = render(<AsyncButton pending onClick={action}>Сохранить</AsyncButton>);
  const pending = screen.getByRole('button', { name: 'Выполняется…' });
  expect(pending).toBeDisabled();
  expect(pending).toHaveAttribute('aria-busy', 'true');
  await userEvent.click(pending);
  expect(action).not.toHaveBeenCalled();
  rerender(<AsyncButton pending={false} onClick={action}>Сохранить</AsyncButton>);
  await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));
  expect(action).toHaveBeenCalledOnce();
});
it('Button defaults to a non-submitting type', () => {
  render(<Button>Действие</Button>);
  expect(screen.getByRole('button')).toHaveAttribute('type', 'button');
});
it('empty, loading and inline errors have meaningful text', () => {
  render(<><EmptyState title="Пустой раздел" description="Пока нет данных" />
    <LoadingSkeleton label="Загрузка курса" /><InlineError id="input-error">Заполните поле</InlineError></>);
  expect(screen.getByRole('heading', { name: 'Пустой раздел' })).toBeInTheDocument();
  expect(screen.getByRole('status', { name: 'Загрузка курса' })).toHaveTextContent('Загрузка курса');
  expect(screen.getByRole('alert')).toHaveTextContent('Заполните поле');
});
it('ErrorState announces the error and permits an explicit retry', async () => {
  const retry = vi.fn();
  render(<ErrorState description="Проверьте соединение" onRetry={retry} />);
  expect(screen.getByRole('alert')).toHaveTextContent('Проверьте соединение');
  await userEvent.click(screen.getByRole('button', { name: 'Повторить' }));
  expect(retry).toHaveBeenCalledOnce();
});
it.each([[25, 25], [-10, 0], [110, 100], [NaN, 0]])('ProgressBar safely displays %s', (value, expected) => {
  render(<ProgressBar value={value} label="Прогресс курса" />);
  expect(screen.getByRole('progressbar', { name: 'Прогресс курса' })).toHaveAttribute('aria-valuenow', String(expected));
});

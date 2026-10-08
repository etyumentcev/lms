// Optional browser smoke using an existing Playwright installation; no snapshots/baselines.
// Start `pnpm preview --port 4173 --strictPort` after building first.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const output = resolve(process.env.LMS_SMOKE_OUTPUT || '.qa');
await mkdir(output, { recursive: true });
const base = process.env.LMS_SMOKE_URL || 'http://127.0.0.1:4173';
const browser = await chromium.launch({ headless: true, channel: 'chrome' });
const page = await browser.newPage();
const errors = [];
const apiRequests = [];
page.on('pageerror', error => errors.push(error.message));
page.on('console', message => { if (['error', 'warning'].includes(message.type())) errors.push(message.text()); });
page.on('request', request => { if (new URL(request.url()).pathname.startsWith('/api/')) apiRequests.push(request.url()); });
const report = [];
async function geometry() {
  return page.evaluate(() => {
    const rect = selector => {
      const { x, y, width, height } = document.querySelector(selector).getBoundingClientRect();
      return { x, y, width, height };
    };
    return { sidebar: rect('.sidebar'), main: rect('main'), context: rect('aside'),
      overflow: document.documentElement.scrollWidth > window.innerWidth,
      regions: document.querySelectorAll('aside').length };
  });
}
async function layout(width) {
  const bounds = await geometry();
  assert.equal(bounds.overflow, false, `horizontal overflow at ${width}`);
  assert.equal(bounds.regions, 1);
  assert(await page.getByRole('heading', { name: 'О пространстве', exact: true }).isVisible());
  if (width >= 1200) {
    assert(bounds.sidebar.x + bounds.sidebar.width <= bounds.main.x + 1);
    assert(bounds.main.x + bounds.main.width <= bounds.context.x + 1);
    assert.equal(bounds.main.y, bounds.context.y);
  } else {
    assert(bounds.context.y >= bounds.main.y + bounds.main.height - 1, 'context follows main');
  }
  return bounds;
}
try {
  for (const [width, height] of [[1440, 1000], [1024, 900], [768, 1024], [390, 844], [320, 720]]) {
    await page.setViewportSize({ width, height });
    await page.goto(base);
    await page.getByRole('heading', { level: 1 }).waitFor();
    report.push({ viewport: [width, height], ...await layout(width) });
    await page.screenshot({ path: `${output}/shell-${width}.png`, fullPage: true });
  }
  // Tablet collapse -> mobile inline -> desktop rail: identical context DOM and preserved value.
  await page.setViewportSize({ width: 1024, height: 900 });
  await page.getByLabel('Навигация', { exact: true }).selectOption('Teacher');
  await page.locator('.context-body').evaluate(node => { node.dataset.smokeIdentity = 'preserved'; });
  await page.getByRole('button', { name: 'Свернуть контекст' }).click();
  assert.equal(await page.locator('.context-body').isVisible(), false);
  await page.setViewportSize({ width: 390, height: 844 });
  assert(await page.locator('.context-body').isVisible());
  assert.equal(await page.getByLabel('Навигация', { exact: true }).inputValue(), 'Teacher');
  assert.equal(await page.locator('.context-body').getAttribute('data-smoke-identity'), 'preserved');
  await layout(390);
  await page.getByRole('button', { name: 'Меню', exact: true }).click();
  assert(await page.getByRole('navigation', { name: 'Основная навигация' }).isVisible());
  assert(await page.getByRole('navigation', { name: 'Основная навигация' }).getByRole('link', { name: 'Проверка' }).isVisible());
  await page.keyboard.press('Escape');
  assert.equal(await page.getByRole('navigation', { name: 'Основная навигация' }).isVisible(), false);
  assert(await page.getByRole('button', { name: 'Меню', exact: true }).evaluate(node => node === document.activeElement));
  await page.setViewportSize({ width: 1440, height: 1000 });
  assert(await page.locator('.context-body').isVisible());
  await layout(1440);
  // Native keyboard entry, skip link, tabs and focus visibility.
  await page.goto(base);
  await page.getByRole('heading', { level: 1 }).waitFor();
  await page.keyboard.press('Tab');
  assert(await page.getByRole('link', { name: 'К основному содержимому' }).evaluate(node => node === document.activeElement));
  await page.keyboard.press('Enter');
  assert(await page.getByRole('main').evaluate(node => node === document.activeElement));
  await page.getByRole('tab', { name: 'Разделы', exact: true }).focus();
  await page.keyboard.press('ArrowRight');
  assert.equal(await page.getByRole('tab', { name: 'Элементы интерфейса' }).getAttribute('aria-selected'), 'true');
  assert(await page.getByRole('tabpanel', { name: 'Элементы интерфейса' }).isVisible());
  assert.equal(await page.getByRole('tab', { name: 'Элементы интерфейса' }).evaluate(node => getComputedStyle(node).outlineStyle), 'solid');
  await page.screenshot({ path: `${output}/primitives-desktop.png`, fullPage: true });
  // Check actual CSS token contrast against WCAG AA normal text threshold.
  const contrast = await page.evaluate(() => {
    const css = getComputedStyle(document.documentElement);
    const pairs = [['--text', '--surface'], ['--text-muted', '--surface'], ['--on-primary', '--primary'],
      ['--primary', '--primary-soft'], ...['neutral', 'info', 'warning', 'success'].map(tone =>
        [`--status-${tone}-text`, `--status-${tone}-surface`]), ['--error-text', '--error-surface']];
    const luminance = token => {
      const hex = css.getPropertyValue(token).trim().slice(1);
      const c = [0, 2, 4].map(i => parseInt(hex.slice(i, i + 2), 16) / 255)
        .map(v => v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4);
      return c[0] * 0.2126 + c[1] * 0.7152 + c[2] * 0.0722;
    };
    return pairs.map(([fg, bg]) => {
      const a = luminance(fg), b = luminance(bg);
      return { fg, bg, ratio: (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05) };
    });
  });
  for (const pair of contrast) assert(pair.ratio >= 4.5, `Contrast: ${JSON.stringify(pair)}`);
  const routes = ['/courses', '/problem-assignments/one',
    '/problem-assignments/one?tab=history', '/teacher/reviews', '/teacher/reviews/one'];
  for (const width of [1440, 390]) {
    await page.setViewportSize({ width, height: 900 });
    for (const path of routes) {
      await page.goto(`${base}${path}`);
      await page.getByRole('heading', { name: 'Раздел готовится' }).waitFor();
      await layout(width);
      assert.equal(await page.getByRole('main').count(), 1);
    }
  }
  assert.deepEqual(apiRequests, [], 'shell must not send feature requests');
  assert.deepEqual(errors, [], 'browser console must be clean');
  const result = { passed: true, report, routesChecked: routes.length * 2, contrast, errors, apiRequests };
  await writeFile(`${output}/results.json`, JSON.stringify(result, null, 2));
  console.log(JSON.stringify(result, null, 2));
} finally { await browser.close(); }

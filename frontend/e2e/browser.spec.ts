import { expect, test } from '@playwright/test';

const apiBase = 'http://localhost:5276/api/v1';

test('home loads public events, searches, and opens an event detail', async ({
  page,
  request,
}) => {
  const pageErrors: string[] = [];
  page.on('pageerror', (error) => pageErrors.push(error.message));

  const response = await request.get(`${apiBase}/events?limit=50`);
  expect(response.ok()).toBeTruthy();
  const body = (await response.json()) as {
    items: Array<{ id: string; title: string; venue: string }>;
  };
  test.skip(body.items.length === 0, 'No public events are available to browse.');

  let first: { id: string; title: string; venue: string } | undefined;
  for (const item of body.items) {
    const detail = await request.get(`${apiBase}/events/${item.id}`);
    if (!detail.ok()) {
      continue;
    }
    const parsed = (await detail.json()) as { ticketTypes: unknown[] };
    if (parsed.ticketTypes.length > 0) {
      first = item;
      break;
    }
  }
  test.skip(!first, 'No public event detail is available to open.');

  await page.goto('/');
  await expect(page.getByRole('heading', { name: /Good nights/ })).toBeVisible();
  await expect(page.locator('.card').first()).toBeVisible();
  expect(await page.locator('.card').count()).toBeGreaterThan(0);
  const search = page.getByLabel('Search events or venues');
  await expect(search).toHaveCSS('border-radius', '14px');
  expect((await search.boundingBox())?.height).toBeLessThan(80);

  await search.fill(first!.title);
  await page.getByRole('button', { name: /Search/ }).click();
  await expect(page.getByRole('heading', { name: 'Search results' })).toBeVisible();
  const card = page.locator('.card').filter({ hasText: first!.title }).first();
  await expect(card).toBeVisible();

  await card.click();
  await expect(page).toHaveURL(new RegExp(`/events/${first!.id}$`));
  await expect(page.getByRole('heading', { name: first!.title, level: 1 })).toBeVisible();
  await expect(page.getByText('Ticket type').first()).toBeVisible();

  expect(pageErrors).toEqual([]);
});

test('guest routes redirect to login and unknown pages show the 404 page', async ({
  page,
}) => {
  await page.goto('/wallet');
  await expect(page).toHaveURL(/\/login\?next=%2Fwallet/);
  await expect(page.getByRole('heading', { name: 'Log in', level: 1 })).toBeVisible();

  await page.goto('/organizer/check-in');
  await expect(page).toHaveURL(/\/login\?next=%2Forganizer%2Fcheck-in/);

  await page.goto('/this-route-does-not-exist');
  await expect(page.getByRole('heading', { name: 'Wrong gate.' })).toBeVisible();
  await expect(page.getByText('404')).toBeVisible();
});

test('signup validates matching passwords and login rejects bad credentials', async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 700 });
  await page.goto('/signup');
  await page.getByLabel('First name').fill('Ada');
  await page.getByLabel('Last name').fill('Lovelace');
  await page.getByLabel('Email').fill(`mismatch-${Date.now()}@example.invalid`);
  await page.getByLabel('Password', { exact: true }).fill('Password123!');
  const confirm = page.getByLabel('Confirm password');
  await confirm.fill('Different123!');
  await page.getByRole('button', { name: 'Create account' }).click();
  await expect(confirm).toBeFocused();
  await expect(confirm).toHaveAttribute('aria-invalid', 'true');
  const mismatch = page.getByText('Passwords do not match.');
  await expect(mismatch).toBeVisible();
  await expect(mismatch).toBeInViewport();

  await page.goto('/login');
  await page.getByLabel('Email').fill(`missing-${Date.now()}@example.invalid`);
  await page.getByLabel('Password').fill('WrongPassword123!');
  await page.getByRole('button', { name: 'Log in' }).click();
  await expect(page.locator('.alert')).toContainText('Invalid email or password.');
});

test('public pages do not overflow a phone viewport', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/');
  await expect(page.locator('.card').first()).toBeVisible();
  expect(
    (await page.getByLabel('Search events or venues').boundingBox())?.height,
  ).toBeLessThan(80);

  const fits = await page.evaluate(
    () => document.documentElement.scrollWidth <= window.innerWidth + 1,
  );
  expect(fits).toBe(true);
});

test('mobile menu navigates, closes and meets the tap target size', async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/');

  const menuButton = page.getByRole('button', { name: 'Navigation menu' });
  await expect(menuButton).toBeVisible();
  expect(
    await menuButton.evaluate((element) => element.getBoundingClientRect().height),
  ).toBeGreaterThanOrEqual(44);

  await menuButton.click();
  await expect(menuButton).toHaveAttribute('aria-expanded', 'true');
  const menu = page.locator('#mobile-menu');
  await expect(menu).toBeVisible();
  await expect(menu.getByRole('link', { name: 'Discover' })).toBeFocused();

  await menu.getByRole('link', { name: 'Log in' }).click();
  await expect(page).toHaveURL(/\/login/);
  await expect(page.getByRole('button', { name: 'Navigation menu' })).toHaveAttribute(
    'aria-expanded',
    'false',
  );
});

test('seeded admin can review and browse users, then log out', async ({ page }) => {
  const email = process.env.E2E_ADMIN_EMAIL;
  const password = process.env.E2E_ADMIN_PASSWORD;
  test.skip(!email || !password, 'E2E_ADMIN_EMAIL / E2E_ADMIN_PASSWORD not set.');

  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/login');
  await page.getByLabel('Email').fill(email!);
  await page.getByLabel('Password').fill(password!);
  await page.getByRole('button', { name: 'Log in' }).click();

  await expect(page).toHaveURL(/\/admin$/);
  await expect(page.getByRole('heading', { name: 'Review queue' })).toBeVisible();

  await page.getByRole('button', { name: 'Navigation menu' }).click();
  await page
    .locator('#mobile-menu')
    .getByRole('link', { name: 'Users' })
    .click();
  await expect(page).toHaveURL(/\/admin\/users$/);
  await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible();
  const adminSearch = page.getByLabel('Search name, organization or email');
  await expect(adminSearch).toBeVisible();
  expect((await adminSearch.boundingBox())?.height).toBeLessThan(80);

  await page.getByRole('button', { name: 'Navigation menu' }).click();
  await page
    .locator('#mobile-menu')
    .getByRole('button', { name: 'Log out' })
    .click();
  await expect(page).toHaveURL('/');
  await expect(page.getByRole('button', { name: 'Navigation menu' })).toBeVisible();
});

test('seeded organizer can open My events and the check-in page', async ({ page }) => {
  const email = process.env.E2E_ORGANIZER_EMAIL;
  const password = process.env.E2E_ORGANIZER_PASSWORD;
  test.skip(
    !email || !password,
    'E2E_ORGANIZER_EMAIL / E2E_ORGANIZER_PASSWORD not set.',
  );

  await page.goto('/login');
  await page.getByLabel('Email').fill(email!);
  await page.getByLabel('Password').fill(password!);
  await page.getByRole('button', { name: 'Log in' }).click();

  await expect(page).toHaveURL(/\/organizer$/);
  await expect(page.getByRole('heading', { name: 'My events' })).toBeVisible();
  await expect(page.getByLabel('Search your events')).toBeVisible();

  await page
    .getByRole('navigation', { name: 'Main' })
    .getByRole('link', { name: 'Check in' })
    .click();
  await expect(page).toHaveURL(/\/organizer\/check-in$/);

  await page.setViewportSize({ width: 390, height: 700 });
  await page.goto('/organizer/events/new');
  await page.getByRole('button', { name: 'Save and submit for review' }).click();
  const validation = page.getByRole('alert').filter({ hasText: 'Check these fields:' });
  await expect(validation).toBeVisible();
  await expect(validation).toBeInViewport();

  await page.getByRole('button', { name: 'Navigation menu' }).click();
  await page
    .locator('#mobile-menu')
    .getByRole('button', { name: 'Log out' })
    .click();
  await expect(page).toHaveURL('/');
});

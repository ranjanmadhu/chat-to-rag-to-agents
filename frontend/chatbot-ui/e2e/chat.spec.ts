import { expect, test } from '@playwright/test';

test('sends a chat message and displays the assistant response', async ({ page }) => {
  await page.route('**/api/chat', async route => {
    const request = route.request().postDataJSON() as { message: string };

    await route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        message: `Mocked response for: ${request.message}`,
        model: 'playwright'
      })
    });
  });

  await page.goto('/');
  await expect(page.getByText('LLM Tutorial Chatbot')).toBeVisible();

  await page.getByRole('textbox', { name: 'Message' }).fill('Explain RAG');
  await page.getByRole('button', { name: 'Send message' }).click();

  await expect(page.getByText('Explain RAG', { exact: true })).toBeVisible();
  await expect(page.getByText('Mocked response for: Explain RAG')).toBeVisible();
});

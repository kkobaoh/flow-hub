import { expect, test } from '@playwright/test';
import { api } from '../playwright.config';
import { collectErrors } from './errors';

// サーバーの AGV-2 の状態を変えるので、AGV-2 を使うテストは、このファイルの1つにまとめる
test('切り離しの AGV を、位置を選んでオートに戻すと、位置リセットが AGV に届き、終わったらマップの上に描く', async ({ page, request }) => {
  const errors = collectErrors(page);
  await page.goto('/');
  const agv = page.locator('[data-agv="AGV-2"]');
  await expect(agv).toHaveAttribute('data-unplaced', '');

  await agv.click();
  const card = page.locator('[data-agv-card]');
  await card.getByRole('button', { name: 'オート', exact: true }).click();
  const dialog = page.getByRole('alertdialog');
  await expect(dialog).toContainText('AGV-2 を「オート」に戻しますか');
  await dialog.getByLabel('パネル').selectOption('SB');
  await dialog.getByLabel('向き').selectOption('90');
  await dialog.getByRole('button', { name: 'オートに戻す' }).click();

  // AGV の役：コマンドを受け取り、位置リセットを終えたと知らせる
  const commands = (await (await request.get(`${api}/agv/AGV-2/commands?lastReceived=-1`)).json()) as { commands: { type: string; number: number; panel: string; heading: number }[] };
  const reset = commands.commands.find((c) => c.type === 'positionReset');
  expect(reset).toMatchObject({ panel: 'SB', heading: 90 });
  const done = await request.post(`${api}/agv/AGV-2/notifications`, { data: { type: 'positionResetCompleted', number: reset!.number } });
  expect(done.status()).toBe(204);

  await expect(agv).not.toHaveAttribute('data-unplaced', '');
  await expect(card).toContainText('SB　90°（東）');
  await expect(card).toContainText('オート');
  await expect(page.locator('[data-reserved]')).not.toHaveCount(0); // いるパネル（SB）を予約している
  expect(errors).toEqual([]);
});

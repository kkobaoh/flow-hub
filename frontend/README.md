# FlowHub の画面（フロントエンド）

React + TypeScript で作り、Vite でビルドします。部品は Storybook で、1つずつ確かめながら作ります。画面の仕様は [docs/screens.md](../docs/screens.md) にあります。

## 使い方

このディレクトリ（`frontend/`）で実行します。Node.js が要ります。

| やること | コマンド |
|---|---|
| 初めに、パッケージを入れる | `npm ci` |
| 開発用のサーバーで画面を開く | `npm run dev` |
| 型を確かめる | `npm run typecheck` |
| ビルドする（`dist/` に出る） | `npm run build` |
| Storybook を開く（http://localhost:6006） | `npm run storybook` |
| Storybook をビルドする（`storybook-static/` に出る） | `npm run build-storybook` |

## 決まりごと

- サードパーティーのライブラリは、基本的に使いません（サーバー側と同じ方針。CLAUDE.md の「サードパーティーのライブラリ」を参照）。
- パッケージのバージョンは、`package.json` に **決まった版** で書きます（`^` や `~` を付けません）。
- Storybook のテレメトリ（利用状況の送信）は止めています（`.storybook/main.ts`）。

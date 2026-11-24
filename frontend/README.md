# FlowHub の画面（フロントエンド）

React + TypeScript で作り、Vite でビルドします。見た目は Tailwind CSS で書き、テストは Vitest で書き、画面を本物のブラウザーで開いて確かめる E2E テストは Playwright で書きます。部品は Storybook で、1つずつ確かめながら作ります。画面の仕様は [docs/screens.md](../docs/screens.md) にあります。

## 使い方

このディレクトリ（`frontend/`）で実行します。Node.js が要ります。

| やること | コマンド |
|---|---|
| 初めに、パッケージを入れる | `npm ci` |
| E2E テストのブラウザー（Chromium）を入れる（初めてのときだけ） | `npx playwright install chromium` |
| 開発用のサーバーで画面を開く（先に、サーバーの Api を起こしておく。下の「サーバーとのつながり」） | `npm run dev` |
| 型を確かめる | `npm run typecheck` |
| テストを流す | `npm test`（変更を見張って流し続けるときは `npm run test:watch`） |
| E2E テストを流す（サーバーの Api と開発用のサーバーは、テストが自分で起こす。.NET が要る） | `npm run e2e` |
| ビルドする（`dist/` に出る） | `npm run build` |
| Storybook を開く（http://localhost:6006） | `npm run storybook` |
| Storybook をビルドする（`storybook-static/` に出る） | `npm run build-storybook` |

- Windows では、**ドライブ文字を大文字（`C:\`）にした場所** から流します。小文字の `c:\` から流すと、Vitest がすべてのテストで「suite not found」になって落ちます。VS Code の拡張機能（Vitest）から流すときも、リポジトリを `C:\` の形で開きます。
- VS Code では、上のコマンドをタスク（「画面: …」）からも流せます。デバッグの「Api + 画面」は、Api と開発用のサーバーを起こしてから、Edge で画面を開きます。

## サーバーとのつながり

画面は、サーバー（`src/FlowHub.Api`）と SignalR と HTTP でつながります（決まりは [docs/screens.md](../docs/screens.md) の「画面とサーバーの通信」）。

- 開発のときは、リポジトリの一番上で `dotnet run --project src/FlowHub.Api` を起こしてから、`npm run dev` で画面を開きます。開発用のサーバー（Vite）が、`/hubs`、`/system`、`/transports` を Api（既定は `http://localhost:5020`）へ渡します。つなぐ先は、環境変数 `FLOWHUB_API_URL` で変えられます。
- Api を起こしていないときは、画面の下に「サーバーとつないでいます」の帯が出て、運転の切り替えは押せません。
- 通信の部品は `src/server/` にあります。部品やテストは、本物のつながり（`createSignalRLink`）の代わりに、偽物（`createFakeLink`）を渡して動かします。
- E2E テストは、Api をポート 5199、開発用のサーバーを 5190 で起こします。Api の DB（SQLite）は `frontend/.e2e-data/` に置きます（開発用の DB とは分けている。git には入れない）。

## 決まりごと

- サードパーティーのライブラリは、基本的に使いません（サーバー側と同じ方針。CLAUDE.md の「サードパーティーのライブラリ」を参照）。
- パッケージのバージョンは、`package.json` に **決まった版** で書きます（`^` や `~` を付けません）。
- Storybook のテレメトリ（利用状況の送信）は止めています（`.storybook/main.ts`）。
- テストを先に書きます（TDD）。テストのファイルは、テストする部品の隣に `*.test.tsx` で置きます。
  - テストは jsdom（ブラウザーの代わり）の中で動きます。部品を描いて押すときは、`src/test/render.tsx` の `render`、`click`、`pressKey`、`hover` を使います（Testing Library は入れていません）。
  - 描く位置や状態の判断（標示灯の色など）は、部品から切り離した関数にして、そちらを細かくテストします。
- E2E テストは `e2e/*.spec.ts` に置きます。ふだんの `npm test` には含めません。画面をまたぐ流れ（メニューで移る、確かめる画面、URL に覚えたもの）、マップの操作（ドラッグ、拡大・縮小、カード、一覧）、スマートフォンの幅（`phone.spec.ts`。横にはみ出さない、下から出す欄）と、ブラウザーでエラーが出ないことを確かめます。
- 色と字は、デザインの試作の値を `src/index.css` の `@theme` にまとめています。部品の中に色の値（`#23262B` など）を直接書かず、`bg-paper`、`text-graphite` のように名前で使います。

# 開発の手引き

CLAUDE.md から、ふだんは読まなくてよい、調べるときに見る部分を移したものです。決めごと（何をしてよいか）は CLAUDE.md にあり、ここには表、図、手順を置きます。

## 使っているライブラリ

使ってよい条件（商用で使える OSS、コピーレフトでない、ユーザーに確認してから足す）は、CLAUDE.md の「サードパーティーのライブラリ」にあります。

| パッケージ | 用途 | ライセンス |
|---|---|---|
| xunit.v3 | テスト | Apache-2.0 |
| Npgsql.EntityFrameworkCore.PostgreSQL | 本番の DB（PostgreSQL）とのやり取り（EF Core） | PostgreSQL |
| Npgsql | PostgreSQL のドライバー（上の部品が使う） | PostgreSQL |
| @microsoft/signalr（npm） | 画面のリアルタイム更新（SignalR のブラウザ側のクライアント。Microsoft 公式） | MIT |
| react、react-dom（npm） | 画面を作る | MIT |
| typescript（npm） | 画面の型の確かめ | Apache-2.0 |
| vite、@vitejs/plugin-react（npm） | 画面の開発用のサーバーとビルド | MIT |
| storybook、@storybook/react-vite（npm） | 画面の部品を1つずつ確かめる | MIT |
| @types/react、@types/react-dom（npm） | React の型 | MIT |
| tailwindcss、@tailwindcss/vite（npm） | 画面の見た目（CSS） | MIT |
| vitest（npm） | 画面のテスト | MIT |
| jsdom（npm） | 画面のテストで、ブラウザーの代わりをする | MIT |
| @playwright/test（npm） | 画面を本物のブラウザーで確かめる E2E テスト（Microsoft 製。依存の playwright、playwright-core も Apache-2.0） | Apache-2.0 |

- npm のパッケージは、依存しているものも含めてライセンスを確かめています。vite が CSS の処理に使う **lightningcss は MPL-2.0** です（ファイル単位の弱いコピーレフト）。ビルドの道具の中でだけ動き、配る成果物には入らず、手を加えずに使うので、使ってよいことにしています。
- @microsoft/signalr の依存先（Node.js で動かすときに使う ws、node-fetch、eventsource、fetch-cookie、tough-cookie など）も、MIT、BSD、Unlicense（パブリックドメイン相当）で、コピーレフトのものはありません。

## 開発の道具（VS Code）

- 開発は **VS Code** で行います。設定は `.vscode/`（`extensions.json`、`settings.json`、`launch.json`、`tasks.json`）と `.editorconfig` にあります。
  - デバッグは「Api + 画面」で、Api（http://localhost:5020）と画面（Vite の開発用のサーバー、http://localhost:5173 を Edge で）を一緒に起こします。
  - テスト、統合テスト、マイグレーションを作る、画面の型の確かめ・テスト・E2E・Storybook は、タスク（「タスクの実行」）から流せます。
  - `.editorconfig` には、書き方（字下げ、文字コード、行末の空白）だけを書きます。コード分析の指摘の重さやスタイルの規則は、ここで変えません（変えると、指摘を抑える特例と同じになるため）。
- 開発の道具にも、ライブラリと同じ条件（商用で使えること、商用の利用に条件がないこと）を当てはめます。
  - **C# Dev Kit（`ms-dotnettools.csdevkit`）は使いません**（商用の組織で使うには Visual Studio の契約が要るため）。C# のテストは、テストエクスプローラーではなく、タスクとターミナルから流します。
  - **Docker は使いません**（商用の利用に条件があるため）。統合テストの PostgreSQL は、ローカルに入れたものを使います。
  - TypeScript 7 の拡張機能（`typescriptteam.native-preview`）は、まだ試しの段階のため入れません。エディターの補完は VS Code に組み込みの TypeScript で、型の確かめは `npm run typecheck` で行います。

| 拡張機能 | 用途 | ライセンス |
|---|---|---|
| ms-dotnettools.csharp | C# の補完、デバッグ | MIT（同梱の言語サーバーとデバッガーは Microsoft のライセンス。VS Code などと一緒に使うこと、ほかへ配らないことが条件で、商用の開発に使えます） |
| ms-dotnettools.vscode-dotnet-runtime | .NET Install Tool（上の拡張機能と一緒に入る） | MIT |
| vitest.explorer | 画面のテストを、エディターから流す | MIT |
| ms-playwright.playwright | E2E テストを流す、デバッグする | Apache-2.0 |
| bradlc.vscode-tailwindcss | Tailwind のクラスの補完 | MIT |
| editorconfig.editorconfig | `.editorconfig` に従わせる | MIT |

開発のときのつながり：

```mermaid
flowchart LR
    subgraph dev["開発（デバッグの「Api + 画面」）"]
        Edge["Edge"] -->|"http://localhost:5173"| Vite["Vite の開発用のサーバー<br/>（frontend）"]
        Vite -->|"/hubs（SignalR）、/identity/、/system、<br/>/transports、/agvs、/agv-types、<br/>/agv-registrations、/configuration、<br/>/map/、/devices、/consumables を渡す"| Api["FlowHub.Api<br/>http://localhost:5020<br/>（装置・ステーション・上位システムは<br/>中のシミュレーターが代わりをする）"]
        AGV["AGV"] -->|"/agv/{agvId}/…（HTTP）"| Api
        Api --> Sqlite[("SQLite<br/>src/FlowHub.Api/data/<br/>コンテキストごとに1ファイル")]
    end
    subgraph e2e["E2E テスト（npm run e2e）"]
        Chromium["Playwright<br/>（Chromium）"] --> ViteE2E["Vite<br/>:5190"] --> ApiE2E["FlowHub.Api<br/>:5199"] --> SqliteE2E[("SQLite<br/>frontend/.e2e-data/")]
    end
    subgraph it["統合テスト"]
        IntegrationTests["FlowHub.IntegrationTests"] --> Postgres[("ローカルの PostgreSQL<br/>接続文字列はユーザーシークレット")]
    end
```

## 初回起動の設定

初期設定が済んでいない環境では、API を起動する前に導入担当者が ASP.NET Core の設定 `Identity:SetupPassword`（環境変数名は `Identity__SetupPassword`）に設置先専用のパスワードを設定します。設定値はリポジトリに保存しません。

画面の `/setup` を開きます（開発時は `http://localhost:5173/setup`）。初期設定 API のパスは `/identity/setup/` で始まります。本番の画面と API は HTTPS で提供します。

作成した管理者は `/login` からログインします。通常ログインの API は `/identity/user/` です。初回変更で必要な文字数は `Identity:MinimumPasswordLength`（初期値8）、管理者の無操作期限は `Identity:Administrator:IdleTimeout`（初期値8時間）で設定できます。

通常ログイン後、メニューの「ユーザーとロールの管理」（`/#/users`）を開くと、画面が `GET /identity/user/users` と `GET /identity/user/roles` から一覧を読みます。

## 新しい機能をどこに置くか

新しい機能を、別のプロジェクト（コンテキスト）に分けるか、既存のプロジェクトに足すかは、次の指標で決めます。

**分ける理由**（どれか1つでも当てはまれば、別のプロジェクトにする）

| 問い | 例 |
|---|---|
| **取り外したり、差し替えたりする単位か**。将来、別のものに置き換える、なくす、両立させる見込みがあるか | ルーティングと全台の経路の計画（既存システムのルート計算に差し替える）。消耗品の管理（ワーク管理と入れ替える、両立させる） |
| **入れると、依存の向きが崩れるか**。知るべきでない相手を知る、矢印が逆を向く、輪になる | 画面のメッセージ（どのコンテキストに入れても、ほかのコンテキストのイベントを知ることになる）。配車は運ぶものを知らない |
| **自分の状態と DB のスキーマを持ち、変わる理由が違うか**。変える人、変える頻度、版の考え方が違う | マップ（版のスナップショット。編集するのは管理者）と交通整理（走っている間に刻々と変わる） |
| **同じ言葉が、違う意味になるか**。入れると、1つの型に2つの意味が混ざる | 「予約」は、交通整理ではパネル、消耗品の管理では届ける消耗品 |

**足す理由**（こちらが強ければ、既存のプロジェクトに足す）

| 問い | 例 |
|---|---|
| **既存の集約の状態を、同じロックや同じトランザクションで読み書きするか**。分けると、結果整合やイベントの往復が要るようになる | 段取り替え（ストッカーの設定と在庫を一緒に見る）は、消耗品の管理に足した |
| **既存のコンテキストの言葉だけで説明できるか** | 指名（AGV での交換）は、消耗品の管理の言葉で説明できる |
| **分けても、公開インターフェースが広がるだけか**。差し替える見込みがない | マニュアル操作は、交通整理に足した |

**あいだの選び方**：分ける理由がまだ確かでないとき（仕様が固まっていないときなど）は、**同じプロジェクトの中で、インターフェース（ポート）とフォルダで切り離して** おき、理由が確かになったら外に出します（例：ステーション）。

**組み立てでつなぐだけのもの**：状態を持たず、あるコンテキストのイベントを別の形に変えて渡すだけなら、プロジェクトにせず、組み立て（`FlowHub.Api`）の購読者にします（例：イベントを文にして、画面のメッセージに渡す）。状態を持つようになったら、分ける理由に当てはまるかを考え直します（画面のメッセージは「DB に残す」が加わって、`FlowHub.Notifications` に分けた）。

**判断の順番**
1. 組み立てでつなぐだけで済むかを見る。
2. 分ける理由に当てはまるかを見る。当てはまらなければ、既存のプロジェクトに足す。
3. 迷ったら、まず同じプロジェクトの中で切り離しておく。分けるのはあとからでもできるが、まとめ直すのは高くつくため。
4. 分けると決めたら、CLAUDE.md のコンテキストの表、依存の向きの表、ディレクトリと、アーキテクチャテスト（`tests/FlowHub.ArchitectureTests` の `ContextRules`）を、一緒に直す。
- 分けるか足すかは、構成の決めごとなので、ユーザーに確認してから決めます。

## ディレクトリ

```
src/
  FlowHub.BuildingBlocks/
  FlowHub.Map/
  FlowHub.Routing/
  FlowHub.PathPlanning/
  FlowHub.Notifications/
  FlowHub.Identity/
  FlowHub.Traffic/
  FlowHub.Dispatching/
  FlowHub.ToolPlanning/
  FlowHub.Consumables/
  FlowHub.Simulator/
  FlowHub.Api/
tests/
  FlowHub.Api.Tests/
  FlowHub.BuildingBlocks.Tests/
  FlowHub.Map.Tests/
  FlowHub.Routing.Tests/
  FlowHub.PathPlanning.Tests/
  FlowHub.Notifications.Tests/
  FlowHub.Identity.Tests/
  FlowHub.Traffic.Tests/
  FlowHub.Dispatching.Tests/
  FlowHub.ToolPlanning.Tests/
  FlowHub.Consumables.Tests/
  FlowHub.Simulator.Tests/
  FlowHub.IntegrationTests/
  FlowHub.ArchitectureTests/
frontend/            （画面。React + TypeScript + Vite + Tailwind CSS + Vitest + Storybook）
```

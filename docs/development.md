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

管理者が「ユーザーを加える」を選んだときの流れです。

```mermaid
sequenceDiagram
    actor Admin as 管理者
    participant UI as 管理画面
    participant API as 認証 API
    participant DB as Identity DB
    Admin->>UI: ユーザー ID を入力して追加
    UI->>API: GET /identity/user/session
    API-->>UI: CSRF トークン
    UI->>API: POST /identity/user/users（ID、Cookie、CSRF トークン）
    API->>DB: 管理者の権限、ID、デフォルトのロールを確認
    alt 登録成功
        API->>DB: ユーザー、ロール、仮パスワードのハッシュを保存
        API-->>UI: 仮パスワード
        UI-->>Admin: 仮パスワードを一度だけ表示
        Admin->>UI: 閉じる
        UI->>API: 一覧を再取得
    else 登録を拒否
        API-->>UI: エラー
        UI-->>Admin: 追加できなかったことを表示
    end
```

ロール欄で選択したときの流れです。同時変更はサーバーが保存する順に反映します。

```mermaid
sequenceDiagram
    actor Admin as 管理者
    participant UI as 管理画面
    participant API as 認証 API
    participant DB as Identity DB
    Admin->>UI: 対象ユーザーのロールを選ぶ
    UI->>API: POST /identity/user/users/role（ユーザー ID、ロール ID、CSRF トークン）
    API->>DB: トランザクションを開始し、固定管理者ロールの行をロック
    API->>DB: 操作者の現在の権限、対象ユーザー、ロール、他の管理者を照合
    alt 変更できる
        API->>DB: ロールを更新してコミット
        API-->>UI: 204
    else 最後の管理者の降格
        API-->>UI: 409
    else 管理権限がない
        API-->>UI: 403
    end
    UI->>API: 一覧と現在の権限を再取得
```

通常ログインの無操作期限は、認証 Cookie を受けたリクエストごとに現在のロールで判定します。画面は SignalR のロングポーリングを使います。更新データが届かないときもサーバーは最長15秒でポーリングを返し、クライアントが次の要求を送るため、接続維持も期限の延長に含まれます。

管理者が「ロックを外す」を押したときの流れです。

```mermaid
sequenceDiagram
    actor Admin as 管理者
    participant UI as 管理画面
    participant API as 認証 API
    participant DB as Identity DB
    Admin->>UI: ロックを外す
    UI->>API: POST /identity/user/users/unlock（ユーザー ID、表示したロックの識別番号、CSRF トークン）
    API->>DB: 管理権限を確認し、識別番号が一致する有効なロックだけ解除
    alt 解除成功、解除済み、または期限切れ
        API-->>UI: 204
    else 別のロックが有効
        API-->>UI: 409
        UI-->>Admin: 新しいロックがかかったことを表示
    end
    UI->>API: 一覧を再取得
```

他のユーザーの仮パスワードを再発行するときの流れです。

```mermaid
sequenceDiagram
    actor Admin as 管理者
    participant UI as 管理画面
    participant API as 認証 API
    participant DB as Identity DB
    participant Client as 対象ユーザーの端末
    Admin->>UI: 対象ユーザーを選び、確認画面で再発行を確定
    UI->>API: POST /identity/user/users/temporary-password（対象 ID、CSRF トークン）
    API->>DB: 現在の管理権限と対象ユーザーを確認
    alt 管理者が他のユーザーを指定
        API->>DB: 新しいハッシュと変更必須状態を保存。失敗回数とロックは保持
        API-->>UI: 仮パスワード
        UI-->>Admin: 一度だけ表示
        Client->>API: 旧 Cookie で認証が必要な操作
        API-->>Client: 401（再ログイン）
    else 自分自身への再発行または権限不足
        API-->>UI: 403
    end
```

本人がメニューからパスワードを変えるときは、通常ログインの変更画面（`/login?change-password`）を開きます。

```mermaid
sequenceDiagram
    actor User as 本人
    participant UI as 変更した端末
    participant API as 認証 API
    participant DB as Identity DB
    participant Other as 別の端末
    User->>UI: 現在のパスワードと新しいパスワードを入力
    UI->>API: POST /identity/user/password（認証 Cookie、CSRF トークン）
    API->>DB: セッションとロックを確認し、現在のパスワードを照合
    alt ロック中または照合失敗
        API-->>UI: ロック中なら429。照合失敗はログインと共通の回数に記録
    else 新しい値が同じか短い
        API-->>UI: 400（入力し直す理由）
    else 変更成功
        API->>DB: 新しいパスワードのハッシュを保存
        API-->>UI: 新しい認証 Cookie
        Other->>API: 古い Cookie で認証を必要とする操作
        API-->>Other: 401（再ログイン）
    end
```

ロールの追加と編集は、同じ API で保存します。

```mermaid
sequenceDiagram
    actor A as 管理者
    participant UI as ロール画面
    participant API as 認証 API
    participant DB as 認証 DB
    A->>UI: 名前と無操作期限を入力して保存
    UI->>API: POST /identity/user/roles/save（編集時は ID、CSRF トークン付き）
    API->>DB: 管理者ロール行をロックし、操作者と対象を確認
    alt 名前と期限が有効
        API->>DB: ロールを追加または更新してコミット
        API-->>UI: 保存成功
        UI->>API: ロール一覧を再取得
    else 保存できない
        API-->>UI: エラー
        UI-->>A: 入力を保持して理由を表示
    end
```

ロール一覧から新規ユーザーのデフォルトを変更する流れです。

```mermaid
sequenceDiagram
    participant A as 管理者Aの画面
    participant B as 管理者Bの画面
    participant API as 認証 API
    participant DB as Identity DB
    Note over A,B: 両者が「閲覧のみ」がデフォルトの一覧を開く
    A->>API: POST /identity/user/roles/default（運用、CSRFトークン）
    API->>DB: 管理権限を確認し、デフォルトを「運用」に変更してコミット
    API-->>A: 204。画面が一覧を再取得
    B->>API: 同じ一覧から「保守」を指定
    API->>DB: 管理権限を確認し、デフォルトを「保守」に変更してコミット
    API-->>B: 204。画面が一覧を再取得
    B->>API: ユーザーを追加
    API->>DB: 現在のデフォルト「保守」で登録
```

ロールを削除するときは、確認画面を開いてから変わる可能性がある条件をサーバーでも調べます。

```mermaid
sequenceDiagram
    actor Admin as 管理者
    participant UI as 管理画面
    participant API as 認証 API
    participant DB as Identity DB
    Admin->>UI: ロール名を確認して削除を確定
    UI->>API: POST /identity/user/roles/delete（ロール ID、CSRF トークン）
    API->>DB: 管理権限を確認し、ユーザー追加・ロール変更と削除を直列に処理
    alt 未使用で、固定でもデフォルトでもない
        API->>DB: ロールと対応するパーミッションを削除してコミット
        API-->>UI: 204
    else 削除条件を満たさない
        API-->>UI: 409
    else 対象がすでに存在しない
        API-->>UI: 204（削除済み）
    end
    UI->>API: 一覧を再取得
```

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

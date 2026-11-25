# 写経の順番

このプロジェクトを写経して理解を深めるときの、おすすめの順番です。

- **依存の少ない内側から外側へ** 進めます。
- サーバー側の各コンテキストでは、**公開の型 → Domain → Application → Infrastructure** の順に、実装がある層を写します。
- どの段階でも、**対応するテストを先に写し**、Red を見てから本体を写すと、TDD の流れも一緒に追えます。
- 写すときは、別のブランチか別のディレクトリで行います。

2026-11-24 時点で、サーバー側の C# は約13,700行、フロントエンドの TypeScript・CSS は約10,200行です。ビルド生成物、マイグレーション、テスト、Storybook は集計から外しています。

## 第0段：前提を頭に入れる（写経はしない）

- [ ] [domain.md](domain.md) の用語と [screens.md](screens.md) の画面一覧を読む
- [ ] [CLAUDE.md](../CLAUDE.md) の「コンテキスト」と「依存の向き」の表を読む
- [ ] [Directory.Build.props](../Directory.Build.props)、[Directory.Packages.props](../Directory.Packages.props)、[global.json](../global.json) に目を通す

## 第1段：土台（約200行）

- [ ] [BuildingBlocks/Events.cs](../src/FlowHub.BuildingBlocks/Events.cs) → [InProcessEventBus.cs](../src/FlowHub.BuildingBlocks/InProcessEventBus.cs)
  - イベントを介してコンテキスト同士をつなぐ仕組みを学びます。
- [ ] [tests/FlowHub.ArchitectureTests](../tests/FlowHub.ArchitectureTests/)
  - 依存のルールを、コードでどう守らせているかがわかります。

## 第2段：マップと経路計算（約2,700行）

- [ ] **Map**（約1,200行）
  - [PanelId.cs](../src/FlowHub.Map/PanelId.cs)、[Heading.cs](../src/FlowHub.Map/Heading.cs)、[Panel.cs](../src/FlowHub.Map/Panel.cs)、[MapSnapshot.cs](../src/FlowHub.Map/MapSnapshot.cs)
  - → [Domain/MapValidator.cs](../src/FlowHub.Map/Domain/MapValidator.cs)
  - → [MapDrawing.cs](../src/FlowHub.Map/MapDrawing.cs) → [Domain/MapDrawingValidator.cs](../src/FlowHub.Map/Domain/MapDrawingValidator.cs)。マップの描画データの検証は [MapDrawingTests.cs](../tests/FlowHub.Map.Tests/MapDrawingTests.cs) と一緒に追います。
  - → [Infrastructure](../src/FlowHub.Map/Infrastructure/)
  - 値の型と検証の書き方を学ぶのに、いちばん小さくて素直な題材です。
- [ ] **Routing**（約400行）
  - [RouteRequest.cs](../src/FlowHub.Routing/RouteRequest.cs) → [RouteGraph.cs](../src/FlowHub.Routing/RouteGraph.cs) → [RoutePlanner.cs](../src/FlowHub.Routing/RoutePlanner.cs) → [RouteResult.cs](../src/FlowHub.Routing/RouteResult.cs)
- [ ] **PathPlanning**（約1,100行）
  - [PlanningInput.cs](../src/FlowHub.PathPlanning/PlanningInput.cs) → [MotionGraph.cs](../src/FlowHub.PathPlanning/MotionGraph.cs) → [Occupancy.cs](../src/FlowHub.PathPlanning/Occupancy.cs) → [SafeIntervalSearch.cs](../src/FlowHub.PathPlanning/SafeIntervalSearch.cs) → [PrioritizedPlanner.cs](../src/FlowHub.PathPlanning/PrioritizedPlanner.cs)
  - アルゴリズムが重いので、難しければ後に回してもかまいません。

## 第3段：交通整理 Traffic（約3,600行。最大の山場）

- [ ] 公開の型
  - [AgvId.cs](../src/FlowHub.Traffic/AgvId.cs)、[ControlStatus.cs](../src/FlowHub.Traffic/ControlStatus.cs)、[TrafficEvents.cs](../src/FlowHub.Traffic/TrafficEvents.cs)、[ITrafficService.cs](../src/FlowHub.Traffic/ITrafficService.cs)
- [ ] Domain の小さな部品
  - [PanelReservations.cs](../src/FlowHub.Traffic/Domain/PanelReservations.cs)、[TravelCommand.cs](../src/FlowHub.Traffic/Domain/TravelCommand.cs)、[TravelSection.cs](../src/FlowHub.Traffic/Domain/TravelSection.cs)、[PassingOrderGate.cs](../src/FlowHub.Traffic/Domain/PassingOrderGate.cs)、[AgvCommand.cs](../src/FlowHub.Traffic/Domain/AgvCommand.cs)
- [ ] [Domain/AgvTravel.cs](../src/FlowHub.Traffic/Domain/AgvTravel.cs)（約850行。AGV の集約）
- [ ] [Application/TrafficService.cs](../src/FlowHub.Traffic/Application/TrafficService.cs)（約1,160行）
  - ロックの取り方、後始末を1か所にまとめる作り、ロックの外での通信を見ます。
- [ ] [Infrastructure](../src/FlowHub.Traffic/Infrastructure/)
  - [AgvEndpoints.cs](../src/FlowHub.Traffic/Infrastructure/AgvEndpoints.cs) は、[agv-protocol.md](agv-protocol.md) と見比べながら写します。

## 第4段：配車 Dispatching（約2,200行）

- [ ] Domain
  - [TransportRequest.cs](../src/FlowHub.Dispatching/Domain/TransportRequest.cs) → [Transport.cs](../src/FlowHub.Dispatching/Domain/Transport.cs)（状態遷移と `TransportAction`）→ [TransportQueue.cs](../src/FlowHub.Dispatching/Domain/TransportQueue.cs) → [AgvAssigner.cs](../src/FlowHub.Dispatching/Domain/AgvAssigner.cs) → [AgvCharging.cs](../src/FlowHub.Dispatching/Domain/AgvCharging.cs)
- [ ] Application
  - [DispatchingService.cs](../src/FlowHub.Dispatching/Application/DispatchingService.cs) → [Subscriptions.cs](../src/FlowHub.Dispatching/Application/Subscriptions.cs)
  - 「ドメインはやることを返し、Application が実行する」という分け方が、いちばんよく見える部分です。
- [ ] [Infrastructure](../src/FlowHub.Dispatching/Infrastructure/)

## 第5段：消耗品の管理と搬送計画（約2,800行）

- [ ] **Consumables**
  - [ConsumableIds.cs](../src/FlowHub.Consumables/ConsumableIds.cs)、[Locations.cs](../src/FlowHub.Consumables/Locations.cs)、[Gateways.cs](../src/FlowHub.Consumables/Gateways.cs)
  - → [Domain/StockerEvaluator.cs](../src/FlowHub.Consumables/Domain/StockerEvaluator.cs)
  - → [ConsumablesService.cs](../src/FlowHub.Consumables/Application/ConsumablesService.cs) と、partial に分けたファイル（DeviceNotifications、Station、Designation、Status）
  - [Statuses.cs](../src/FlowHub.Consumables/Statuses.cs) は、画面に出す装置とステーションの様子（気をつけること、スロットごとの中身、ステーションにある消耗品）の型です。
  - → [Infrastructure](../src/FlowHub.Consumables/Infrastructure/)
- [ ] **ToolPlanning**
  - [Domain/TripPlanner.cs](../src/FlowHub.ToolPlanning/Domain/TripPlanner.cs) → [ToolPlanningService.cs](../src/FlowHub.ToolPlanning/Application/ToolPlanningService.cs) → [Subscriptions.cs](../src/FlowHub.ToolPlanning/Application/Subscriptions.cs)
  - [TransportDescriptions.cs](../src/FlowHub.ToolPlanning/TransportDescriptions.cs) は、配車が知らない「頼んだ搬送の中身」を画面のために出す型です。

## 第6段：通知・シミュレーター・API の組み立て（約2,300行）

- [ ] **Notifications**
  - [Notice.cs](../src/FlowHub.Notifications/Notice.cs) → [INoticeBoard.cs](../src/FlowHub.Notifications/INoticeBoard.cs) → [DbNoticeBoard.cs](../src/FlowHub.Notifications/Infrastructure/DbNoticeBoard.cs)
  - [通知のテスト](../tests/FlowHub.Notifications.Tests/) と一緒に、通知への採番、保持件数、DB からの復元を追います。
- [ ] [Simulator](../src/FlowHub.Simulator/)
- [ ] [Api/Database](../src/FlowHub.Api/Database/) → [Composition/FlowHubSystem.cs](../src/FlowHub.Api/Composition/FlowHubSystem.cs) → [Subscriptions.cs](../src/FlowHub.Api/Subscriptions.cs) → [InitialData](../src/FlowHub.Api/InitialData/) → [Program.cs](../src/FlowHub.Api/Program.cs)
- [ ] [ScreenFeed.cs](../src/FlowHub.Api/Composition/Screens/ScreenFeed.cs) → [ScreenHub.cs](../src/FlowHub.Api/Composition/Screens/ScreenHub.cs)
  - [ScreenFeedTests.cs](../tests/FlowHub.Api.Tests/ScreenFeedTests.cs) と一緒に、運転状態・AGV・メッセージ・マップ・装置とステーション・搬送の一覧の変更が番号付きで配信される流れを追います。
  - 送る形は [Screens](../src/FlowHub.Api/Composition/Screens/) にあります（[EquipmentStatusView.cs](../src/FlowHub.Api/Composition/Screens/EquipmentStatusView.cs)、[TransportsView.cs](../src/FlowHub.Api/Composition/Screens/TransportsView.cs) など）。搬送の一覧は、配車の一覧と搬送計画の中身を、組み立てで結び付けています。
- [ ] 画面から呼ぶ HTTP の窓口：[AgvOperationEndpoints.cs](../src/FlowHub.Api/Composition/AgvOperationEndpoints.cs)、[ConsumableEndpoints.cs](../src/FlowHub.Api/Composition/ConsumableEndpoints.cs)、[TransportEndpoints.cs](../src/FlowHub.Api/Composition/TransportEndpoints.cs)

## 第7段：画面（frontend、約10,200行）

[フロントエンドの README](../frontend/README.md#ソースの構成) でフォルダーの責務と起動方法を確認します。部品のテストと Story は対象ファイルの隣にあります。

- [ ] **テストの土台と共通部品**
  - [vitest.config.ts](../frontend/vitest.config.ts) → [testing/render.tsx](../frontend/src/testing/render.tsx) を見て、テストで画面を描いて操作する方法を確認します。
  - [shared/components](../frontend/src/shared/components/) と [styles/global.css](../frontend/src/styles/global.css) で、画面間で使う部品と Tailwind CSS のテーマを確認します。
- [ ] **業務の型・計算と画面部品**
  - [routing/model.ts](../frontend/src/features/routing/model.ts) → [drawing.ts](../frontend/src/features/routing/drawing.ts) → [MapCanvas.tsx](../frontend/src/features/routing/MapCanvas.tsx) の順に、マップのデータが描画に変わるところを追います。
  - [fleet/controlStatus.ts](../frontend/src/features/fleet/controlStatus.ts) → [AgvCard.tsx](../frontend/src/features/fleet/AgvCard.tsx) を写し、[AgvCard のテスト](../frontend/src/features/fleet/AgvCard.test.tsx) と [Story](../frontend/src/features/fleet/AgvCard.stories.tsx) で、状態ごとの表示と操作を確認します。
- [ ] **API とリアルタイム通信**
  - [agvSnapshots.ts](../frontend/src/features/fleet/api/agvSnapshots.ts) で受信データの変換、[agvCommands.ts](../frontend/src/features/fleet/api/agvCommands.ts) で HTTP による操作の送信を追います。操作の送信の共通部分は [postCommand.ts](../frontend/src/shared/api/postCommand.ts) の `postOperation` です。
  - 同じ形で、装置とステーション（[equipmentStatus.ts](../frontend/src/features/tool/api/equipmentStatus.ts)、[consumableCommands.ts](../frontend/src/features/tool/api/consumableCommands.ts)、画面の形にする [inside.ts](../frontend/src/features/tool/inside.ts)）と、搬送の一覧（[transportsSnapshot.ts](../frontend/src/features/dispatch/api/transportsSnapshot.ts)、[transportCommands.ts](../frontend/src/features/dispatch/api/transportCommands.ts)）があります。
  - [versioned.ts](../frontend/src/shared/realtime/versioned.ts) → [signalRLink.ts](../frontend/src/app/realtime/signalRLink.ts) → [useServer.ts](../frontend/src/app/realtime/useServer.ts) を、第6段の `ScreenFeed.cs` と見比べます。[fakeLink.ts](../frontend/src/testing/fakeLink.ts) を使うテストや Storybook から始められます。
- [ ] **URL ごとの画面とアプリの組み立て**
  - [pages](../frontend/src/pages/) の [MapPage.tsx](../frontend/src/pages/MapPage.tsx) と [FleetPage.tsx](../frontend/src/pages/FleetPage.tsx) を題材に、業務の部品を画面として組み合わせる方法を学びます。
  - [router.ts](../frontend/src/app/router.ts) → [MainLayout.tsx](../frontend/src/app/layouts/MainLayout.tsx) → [App.tsx](../frontend/src/app/App.tsx) → [main.tsx](../frontend/src/main.tsx) の順に、URL と画面、通信、起動処理のつながりを確認します。
  - 最後に [screens.spec.ts](../frontend/e2e/screens.spec.ts) で画面遷移と URL の復元、[connection.spec.ts](../frontend/e2e/connection.spec.ts) でサーバーから画面への反映を確認します。

## 進め方のヒント

- **短く一周したいとき** は、BuildingBlocks → Map → Routing → Dispatching の Domain だけ（約2,500行）で、設計の考え方の大部分をつかめます。
- 1つのコンテキストを写し終えるたびに、[CLAUDE.md](../CLAUDE.md) の「設計の観点」を読み直し、「この書き方はどの観点のためか」を確かめます。

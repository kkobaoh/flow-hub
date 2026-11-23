# 写経の順番

このプロジェクトを写経して理解を深めるときの、おすすめの順番です。

- **依存の少ない内側から外側へ** 進めます。
- 各コンテキストの中では、**公開の型 → Domain → Application → Infrastructure** の順に写します。
- どの段階でも、**対応するテストを先に写し**、Red を見てから本体を写すと、TDD の流れも一緒に追えます。
- 写すときは、別のブランチか別のディレクトリで行います。

量の目安（2026-11-23 時点）：サーバー側の C# が約1.2万行（マイグレーションを除く）、画面が約8,500行です。

## 第0段：前提を頭に入れる（写経はしない）

- [ ] [domain.md](domain.md) の用語を読む
- [ ] [CLAUDE.md](../CLAUDE.md) の「コンテキスト」と「依存の向き」の表を読む
- [ ] [Directory.Build.props](../Directory.Build.props)、[Directory.Packages.props](../Directory.Packages.props)、[global.json](../global.json) に目を通す

## 第1段：土台（約200行）

- [ ] [BuildingBlocks/Events.cs](../src/FlowHub.BuildingBlocks/Events.cs) → [InProcessEventBus.cs](../src/FlowHub.BuildingBlocks/InProcessEventBus.cs)
  - イベントの仕組みは、すべてのコンテキストで使います。
- [ ] [tests/FlowHub.ArchitectureTests](../tests/FlowHub.ArchitectureTests/)
  - 依存のルールを、コードでどう守らせているかがわかります。

## 第2段：何にも依存しない計算（約2,400行）

- [ ] **Map**（約900行）
  - [PanelId.cs](../src/FlowHub.Map/PanelId.cs)、[Heading.cs](../src/FlowHub.Map/Heading.cs)、[Panel.cs](../src/FlowHub.Map/Panel.cs)、[MapSnapshot.cs](../src/FlowHub.Map/MapSnapshot.cs)
  - → [Domain/MapValidator.cs](../src/FlowHub.Map/Domain/MapValidator.cs)
  - → [Infrastructure](../src/FlowHub.Map/Infrastructure/)
  - 値の型と検証の書き方を学ぶのに、いちばん小さくて素直な題材です。
- [ ] **Routing**（約400行）
  - [RouteRequest.cs](../src/FlowHub.Routing/RouteRequest.cs) → [RouteGraph.cs](../src/FlowHub.Routing/RouteGraph.cs) → [RoutePlanner.cs](../src/FlowHub.Routing/RoutePlanner.cs) → [RouteResult.cs](../src/FlowHub.Routing/RouteResult.cs)
- [ ] **PathPlanning**（約1,100行）
  - [PlanningInput.cs](../src/FlowHub.PathPlanning/PlanningInput.cs) → [MotionGraph.cs](../src/FlowHub.PathPlanning/MotionGraph.cs) → [Occupancy.cs](../src/FlowHub.PathPlanning/Occupancy.cs) → [SafeIntervalSearch.cs](../src/FlowHub.PathPlanning/SafeIntervalSearch.cs) → [PrioritizedPlanner.cs](../src/FlowHub.PathPlanning/PrioritizedPlanner.cs)
  - アルゴリズムが重いので、難しければ後に回してもかまいません。

## 第3段：交通整理 Traffic（約3,500行。最大の山場）

- [ ] 公開の型
  - [AgvId.cs](../src/FlowHub.Traffic/AgvId.cs)、[ControlStatus.cs](../src/FlowHub.Traffic/ControlStatus.cs)、[TrafficEvents.cs](../src/FlowHub.Traffic/TrafficEvents.cs)、[ITrafficService.cs](../src/FlowHub.Traffic/ITrafficService.cs)
- [ ] Domain の小さな部品
  - [PanelReservations.cs](../src/FlowHub.Traffic/Domain/PanelReservations.cs)、[TravelCommand.cs](../src/FlowHub.Traffic/Domain/TravelCommand.cs)、[TravelSection.cs](../src/FlowHub.Traffic/Domain/TravelSection.cs)、[PassingOrderGate.cs](../src/FlowHub.Traffic/Domain/PassingOrderGate.cs)、[AgvCommand.cs](../src/FlowHub.Traffic/Domain/AgvCommand.cs)
- [ ] [Domain/AgvTravel.cs](../src/FlowHub.Traffic/Domain/AgvTravel.cs)（約900行。AGV の集約）
- [ ] [Application/TrafficService.cs](../src/FlowHub.Traffic/Application/TrafficService.cs)（約1,100行）
  - ロックの取り方、後始末を1か所にまとめる作り、ロックの外での通信を見ます。
- [ ] [Infrastructure](../src/FlowHub.Traffic/Infrastructure/)
  - [AgvEndpoints.cs](../src/FlowHub.Traffic/Infrastructure/AgvEndpoints.cs) は、[agv-protocol.md](agv-protocol.md) と見比べながら写します。

## 第4段：配車 Dispatching（約2,000行）

- [ ] Domain
  - [TransportRequest.cs](../src/FlowHub.Dispatching/Domain/TransportRequest.cs) → [Transport.cs](../src/FlowHub.Dispatching/Domain/Transport.cs)（状態遷移と `TransportAction`）→ [TransportQueue.cs](../src/FlowHub.Dispatching/Domain/TransportQueue.cs) → [AgvAssigner.cs](../src/FlowHub.Dispatching/Domain/AgvAssigner.cs) → [AgvCharging.cs](../src/FlowHub.Dispatching/Domain/AgvCharging.cs)
- [ ] Application
  - [DispatchingService.cs](../src/FlowHub.Dispatching/Application/DispatchingService.cs) → [Subscriptions.cs](../src/FlowHub.Dispatching/Application/Subscriptions.cs)
  - 「ドメインはやることを返し、Application が実行する」という分け方が、いちばんよく見える部分です。
- [ ] [Infrastructure](../src/FlowHub.Dispatching/Infrastructure/)

## 第5段：消耗品の管理と搬送計画（約2,700行）

- [ ] **Consumables**
  - [ConsumableIds.cs](../src/FlowHub.Consumables/ConsumableIds.cs)、[Locations.cs](../src/FlowHub.Consumables/Locations.cs)、[Gateways.cs](../src/FlowHub.Consumables/Gateways.cs)
  - → [Domain/StockerEvaluator.cs](../src/FlowHub.Consumables/Domain/StockerEvaluator.cs)
  - → [ConsumablesService.cs](../src/FlowHub.Consumables/Application/ConsumablesService.cs) と、partial に分けたファイル（DeviceNotifications、Station、Designation）
  - → [Infrastructure](../src/FlowHub.Consumables/Infrastructure/)
- [ ] **ToolPlanning**
  - [Domain/TripPlanner.cs](../src/FlowHub.ToolPlanning/Domain/TripPlanner.cs) → [ToolPlanningService.cs](../src/FlowHub.ToolPlanning/Application/ToolPlanningService.cs) → [Subscriptions.cs](../src/FlowHub.ToolPlanning/Application/Subscriptions.cs)

## 第6段：組み立て（約1,200行）

- [ ] [Simulator](../src/FlowHub.Simulator/)
- [ ] [Api/Database](../src/FlowHub.Api/Database/) → [Composition/FlowHubSystem.cs](../src/FlowHub.Api/Composition/FlowHubSystem.cs) → [Subscriptions.cs](../src/FlowHub.Api/Subscriptions.cs) → [InitialData](../src/FlowHub.Api/InitialData/) → [Program.cs](../src/FlowHub.Api/Program.cs)
  - ここまで写すと、全体が1本の線でつながります。

## 第7段：画面（frontend、約8,500行）

- [ ] 型と API のクライアント → 小さな部品（Storybook と一緒に）→ 画面
  - サーバー側とは独立しているので、別の機会に回してもかまいません。

## 進め方のヒント

- **短く一周したいとき** は、BuildingBlocks → Map → Routing → Dispatching の Domain だけ（約2,500行）で、設計の考え方の大部分をつかめます。
- 1つのコンテキストを写し終えるたびに、[CLAUDE.md](../CLAUDE.md) の「設計の観点」を読み直し、「この書き方はどの観点のためか」を確かめます。

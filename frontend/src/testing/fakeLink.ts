import type { CommandResult } from '../shared/api/postCommand';
import type { ConnectionState, ServerHandlers, ServerLink } from '../app/realtime/link';
import type { AgvId } from '../features/fleet/agv';
import type { AgvCommand, AgvCommandResult } from '../features/fleet/api/agvCommands';
import type { AgvsSnapshot } from '../features/fleet/api/agvSnapshots';
import type { MapSnapshot } from '../features/routing/api/mapSnapshot';
import type { EquipmentStatusSnapshot } from '../features/tool/api/equipmentStatus';
import type { TransportsSnapshot } from '../features/dispatch/api/transportsSnapshot';
import type { ConsumablesSnapshot } from '../features/tool/api/consumablesSnapshot';
import type { ConfigurationCommand, ConfigurationResult, ConfigurationSnapshot } from '../features/tool/api/configurationSnapshot';
import type { MapDraftLoad, MapDraftSaveOutcome, MapPublishOutcome } from '../features/routing/api/mapDraft';
import type { MapDraft } from '../features/routing/mapEdit';
import { demoMapDraft } from '../app/demo/demo';
import type { AgvRegistrationCommand, AgvRegistrationResult, AgvTypeCommand, AgvTypeCommandResult, FleetSnapshot } from '../features/fleet/api/fleetSnapshot';
import type { TransportCancelResult } from '../features/dispatch/api/transportCommands';
import type { ConsumableCommand, ConsumableCommandResult } from '../features/tool/api/consumableCommands';
import type { MessagesSnapshot } from '../features/alarm/api/messages';
import type { SystemStatusSnapshot } from '../features/system/systemStatus';

type Command = 'start' | 'stop';

/** テストと Storybook で使う、偽物のつながり。届くものを手で送り、送られた操作を覚える。 */
export interface FakeLink extends ServerLink {
  /** 送られた操作。 */
  readonly sent: readonly Command[];
  /** AGV へ送られた操作。 */
  readonly agvCommands: readonly { readonly id: AgvId; readonly command: AgvCommand }[];
  /** AGV への操作の結果を決める（既定は accepted）。holdResponses で止めておける。 */
  respondToAgv(result: AgvCommandResult): void;
  /** 消耗品へ送られた操作。 */
  readonly consumableCommands: readonly ConsumableCommand[];
  /** 消耗品の操作の結果を決める（既定は、受け付けたときの結果）。holdResponses で止めておける。 */
  respondToConsumables(result: ConsumableCommandResult): void;
  /** 今つないでいるか。 */
  readonly connected: boolean;
  /** 操作の結果を決める（既定は ok）。 */
  respondWith(result: CommandResult): void;
  /** 操作の結果を、release を呼ぶまで返さずにおく。 */
  holdResponses(): void;
  /** 返さずにおいた操作の結果を返す。 */
  release(): void;
  emitConnection(state: ConnectionState): void;
  emitSystemStatus(snapshot: SystemStatusSnapshot): void;
  emitAgvs(snapshot: AgvsSnapshot): void;
  emitMessages(snapshot: MessagesSnapshot): void;
  emitMap(snapshot: MapSnapshot): void;
  emitEquipment(snapshot: EquipmentStatusSnapshot): void;
  emitTransports(snapshot: TransportsSnapshot): void;
  emitConsumables(snapshot: ConsumablesSnapshot): void;
  emitFleet(snapshot: FleetSnapshot): void;
  emitConfiguration(snapshot: ConfigurationSnapshot): void;
  /** 消耗品の構成へ送られた操作。 */
  readonly configurationCommands: readonly ConfigurationCommand[];
  /** 消耗品の構成の操作の結果を決める（既定は saved）。 */
  respondToConfiguration(result: ConfigurationResult): void;
  /** 保存を頼まれたマップの下書き（読んだときの番号と、中身）。 */
  readonly savedMapDrafts: readonly { readonly revision: number; readonly draft: MapDraft }[];
  /** マップの下書きを読んだときの答えを決める（既定は、番号 0 の見本の下書き）。 */
  respondToMapDraftLoad(result: MapDraftLoad | 'failed'): void;
  /** マップの下書きを保存したときの答えを決める（既定は、番号を1つ進めて saved）。 */
  respondToMapDraftSave(result: MapDraftSaveOutcome): void;
  /** 検証を頼まれたマップの下書き。 */
  readonly validatedMapDrafts: readonly MapDraft[];
  /** マップの下書きを検証したときの問題を決める（既定は、問題なし）。 */
  respondToMapDraftValidation(problems: readonly string[]): void;
  /** 公開を頼まれたマップの下書き（読んだときの番号と、中身）。 */
  readonly publishedMapDrafts: readonly { readonly revision: number; readonly draft: MapDraft }[];
  /** マップの下書きを公開したときの答えを決める（既定は、版2として published）。 */
  respondToMapPublish(result: MapPublishOutcome): void;
  /** AGV の種類へ送られた操作。 */
  readonly agvTypeCommands: readonly AgvTypeCommand[];
  /** AGV の種類の操作の結果を決める（既定は、受け付けたときの結果）。 */
  respondToAgvTypes(result: AgvTypeCommandResult): void;
  /** AGV の登録へ送られた操作。 */
  readonly agvRegistrationCommands: readonly AgvRegistrationCommand[];
  /** AGV の登録の操作の結果を決める（既定は、受け付けたときの結果）。 */
  respondToAgvRegistrations(result: AgvRegistrationResult): void;
  /** 取りやめを頼まれた搬送（頼まれた順）。 */
  readonly canceledTransports: readonly number[];
  /** 取りやめの結果を決める（既定は canceled）。holdResponses で止めておける。 */
  respondToCancel(result: TransportCancelResult): void;
}

/**
 * 偽物のつながりを作る。
 * @param initial つないだときに、すぐ送るもの（Storybook で、つながった形を見るため）。
 */
export function createFakeLink(initial?: {
  readonly connection: ConnectionState;
  readonly status?: SystemStatusSnapshot;
  readonly agvs?: AgvsSnapshot;
  readonly messages?: MessagesSnapshot;
  readonly map?: MapSnapshot;
  readonly equipment?: EquipmentStatusSnapshot;
  readonly transports?: TransportsSnapshot;
  readonly consumables?: ConsumablesSnapshot;
  readonly fleet?: FleetSnapshot;
  readonly configuration?: ConfigurationSnapshot;
}): FakeLink {
  let handlers: ServerHandlers | null = null;
  let result: CommandResult = 'ok';
  let holding = false;
  let held: (() => void)[] = [];
  const sent: Command[] = [];
  const agvCommands: { id: AgvId; command: AgvCommand }[] = [];
  let agvResult: AgvCommandResult = 'accepted';
  const consumableCommands: ConsumableCommand[] = [];
  const canceledTransports: number[] = [];
  const agvTypeCommands: AgvTypeCommand[] = [];
  let agvTypeResult: AgvTypeCommandResult | null = null;
  const agvRegistrationCommands: AgvRegistrationCommand[] = [];
  let agvRegistrationResult: AgvRegistrationResult | null = null;
  let cancelResult: TransportCancelResult = 'canceled';
  let consumableResult: ConsumableCommandResult | null = null;
  const accepted = { stockerSetting: 'saved', ship: 'requested', correct: 'corrected', designate: 'designated', cancelDesignation: 'canceled', register: 'saved', edit: 'saved', delete: 'saved' } as const;

  const send = (command: Command): Promise<CommandResult> => {
    sent.push(command);
    const answer = result;
    if (!holding) {
      return Promise.resolve(answer);
    }
    return new Promise((resolve) => held.push(() => resolve(answer)));
  };

  const operate = (id: AgvId, command: AgvCommand): Promise<AgvCommandResult> => {
    agvCommands.push({ id, command });
    const answer = agvResult;
    if (!holding) {
      return Promise.resolve(answer);
    }
    return new Promise((resolve) => held.push(() => resolve(answer)));
  };

  const configurationCommands: ConfigurationCommand[] = [];
  const savedMapDrafts: { revision: number; draft: MapDraft }[] = [];
  let mapDraftLoad: MapDraftLoad | 'failed' = { revision: 0, savedAt: null, publishedVersion: 1, draft: demoMapDraft };
  let mapDraftSave: MapDraftSaveOutcome | null = null;
  const validatedMapDrafts: MapDraft[] = [];
  let mapDraftProblems: readonly string[] = [];
  const publishedMapDrafts: { revision: number; draft: MapDraft }[] = [];
  let mapPublish: MapPublishOutcome = { result: 'published', version: 2, agvs: [] };
  let configurationResult: ConfigurationResult = 'saved';

  const operateConsumables = (command: ConsumableCommand): Promise<ConsumableCommandResult> => {
    consumableCommands.push(command);
    const answer = consumableResult ?? accepted[command.kind];
    if (!holding) {
      return Promise.resolve(answer);
    }
    return new Promise((resolve) => held.push(() => resolve(answer)));
  };

  return {
    sent,
    agvCommands,
    consumableCommands,
    canceledTransports,
    agvTypeCommands,
    agvRegistrationCommands,
    respondToAgvRegistrations(next) {
      agvRegistrationResult = next;
    },
    operateAgvRegistrations(command) {
      agvRegistrationCommands.push(command);
      const answer = agvRegistrationResult ?? (command.kind === 'delete' ? 'deleted' : 'saved');
      if (!holding) {
        return Promise.resolve(answer);
      }
      return new Promise((resolve) => held.push(() => resolve(answer)));
    },
    respondToAgvTypes(next) {
      agvTypeResult = next;
    },
    operateAgvTypes(command) {
      agvTypeCommands.push(command);
      const answer = agvTypeResult ?? (command.kind === 'delete' ? 'deleted' : 'saved');
      if (!holding) {
        return Promise.resolve(answer);
      }
      return new Promise((resolve) => held.push(() => resolve(answer)));
    },
    respondToCancel(next) {
      cancelResult = next;
    },
    cancelTransport(number) {
      canceledTransports.push(number);
      const answer = cancelResult;
      if (!holding) {
        return Promise.resolve(answer);
      }
      return new Promise((resolve) => held.push(() => resolve(answer)));
    },
    respondToConsumables(next) {
      consumableResult = next;
    },
    configurationCommands,
    savedMapDrafts,
    respondToMapDraftLoad(next) {
      mapDraftLoad = next;
    },
    respondToMapDraftSave(next) {
      mapDraftSave = next;
    },
    loadMapDraft: () => Promise.resolve(mapDraftLoad),
    publishedMapDrafts,
    respondToMapPublish(next) {
      mapPublish = next;
    },
    publishMapDraft(revision, draft) {
      publishedMapDrafts.push({ revision, draft });
      return Promise.resolve(mapPublish);
    },
    validatedMapDrafts,
    respondToMapDraftValidation(next) {
      mapDraftProblems = next;
    },
    validateMapDraft(draft) {
      validatedMapDrafts.push(draft);
      return Promise.resolve(mapDraftProblems);
    },
    saveMapDraft(revision, draft) {
      savedMapDrafts.push({ revision, draft });
      return Promise.resolve(mapDraftSave ?? { result: 'saved', revision: revision + 1 });
    },
    respondToConfiguration(next) {
      configurationResult = next;
    },
    operateConfiguration(command) {
      configurationCommands.push(command);
      const answer = configurationResult;
      if (!holding) {
        return Promise.resolve(answer);
      }
      return new Promise((resolve) => held.push(() => resolve(answer)));
    },
    operateConsumables,
    respondToAgv(next) {
      agvResult = next;
    },
    operateAgv: operate,
    get connected() {
      return handlers !== null;
    },
    connect(h) {
      handlers = h;
      if (initial) {
        h.onConnection(initial.connection);
        if (initial.status) {
          h.onSystemStatus(initial.status);
        }
        if (initial.agvs) {
          h.onAgvs(initial.agvs);
        }
        if (initial.messages) {
          h.onMessages(initial.messages);
        }
        if (initial.map) {
          h.onMap(initial.map);
        }
        if (initial.equipment) {
          h.onEquipment(initial.equipment);
        }
        if (initial.transports) {
          h.onTransports(initial.transports);
        }
        if (initial.consumables) {
          h.onConsumables(initial.consumables);
        }
        if (initial.fleet) {
          h.onFleet(initial.fleet);
        }
        if (initial.configuration) {
          h.onConfiguration(initial.configuration);
        }
      }
      return () => {
        if (handlers === h) {
          handlers = null;
        }
      };
    },
    startSystem: () => send('start'),
    stopSystem: () => send('stop'),
    respondWith(next) {
      result = next;
    },
    holdResponses() {
      holding = true;
    },
    release() {
      holding = false;
      const pending = held;
      held = [];
      pending.forEach((resolve) => resolve());
    },
    emitConnection(state) {
      handlers?.onConnection(state);
    },
    emitSystemStatus(snapshot) {
      handlers?.onSystemStatus(snapshot);
    },
    emitAgvs(snapshot) {
      handlers?.onAgvs(snapshot);
    },
    emitMessages(snapshot) {
      handlers?.onMessages(snapshot);
    },
    emitMap(snapshot) {
      handlers?.onMap(snapshot);
    },
    emitEquipment(snapshot) {
      handlers?.onEquipment(snapshot);
    },
    emitTransports(snapshot) {
      handlers?.onTransports(snapshot);
    },
    emitConsumables(snapshot) {
      handlers?.onConsumables(snapshot);
    },
    emitFleet(snapshot) {
      handlers?.onFleet(snapshot);
    },
    emitConfiguration(snapshot) {
      handlers?.onConfiguration(snapshot);
    },
  };
}

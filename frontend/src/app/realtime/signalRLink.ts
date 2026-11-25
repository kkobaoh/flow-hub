import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { postCommand } from '../../shared/api/postCommand';
import { COMMAND_TIMEOUT_MS, retryDelayMs, type ServerHandlers, type ServerLink } from './link';
import { sendAgvCommand } from '../../features/fleet/api/agvCommands';
import { parseAgvs } from '../../features/fleet/api/agvSnapshots';
import { parseMap } from '../../features/routing/api/mapSnapshot';
import { parseEquipmentStatus } from '../../features/tool/api/equipmentStatus';
import { parseTransports } from '../../features/dispatch/api/transportsSnapshot';
import { parseConsumables } from '../../features/tool/api/consumablesSnapshot';
import { parseConfiguration, sendConfigurationCommand } from '../../features/tool/api/configurationSnapshot';
import { loadMapDraft, publishMapDraft, saveCarrierHeight, saveMapDraft, validateMapDraft } from '../../features/routing/api/mapDraft';
import { parseFleet, sendAgvRegistrationCommand, sendAgvTypeCommand } from '../../features/fleet/api/fleetSnapshot';
import { sendCancelTransport } from '../../features/dispatch/api/transportCommands';
import { sendConsumableCommand } from '../../features/tool/api/consumableCommands';
import { parseMessages } from '../../features/alarm/api/messages';
import { parseSystemStatus } from '../../features/system/systemStatus';

/** 運転状態の様子を受け取る名前（サーバーの ScreenFeed.SystemStatusMethod と同じ）。 */
const SYSTEM_STATUS = 'systemStatus';

/** AGV の様子を受け取る名前（サーバーの ScreenFeed.AgvsMethod と同じ）。 */
const AGVS = 'agvs';

/** メッセージの様子を受け取る名前（サーバーの ScreenFeed.MessagesMethod と同じ）。 */
const MESSAGES = 'messages';

/** マップの様子を受け取る名前（サーバーの ScreenFeed.MapMethod と同じ）。 */
const MAP = 'map';

/** 装置とステーションの様子を受け取る名前（サーバーの ScreenFeed.EquipmentMethod と同じ）。 */
const EQUIPMENT = 'equipment';

/** 搬送の一覧を受け取る名前（サーバーの ScreenFeed.TransportsMethod と同じ）。 */
const TRANSPORTS = 'transports';

/** 消耗品ごとの記録を受け取る名前（サーバーの ScreenFeed.ConsumablesMethod と同じ）。 */
const CONSUMABLES = 'consumables';

/** AGV と AGV の種類の登録を受け取る名前（サーバーの ScreenFeed.FleetMethod と同じ）。 */
const FLEET = 'fleet';

/** 消耗品の構成を受け取る名前（サーバーの ScreenFeed.ConfigurationMethod と同じ）。 */
const CONFIGURATION = 'configuration';

/**
 * 本物のつながり。SignalR のハブ（/hubs/screen）で様子を受け取り、操作は HTTP の窓口へ送る。
 * 切れたら、間を空けてずっとつなぎ直す。初めてつなぐときに失敗しても、同じように試し続ける。
 * @param baseUrl サーバーの場所。開発用のサーバー（Vite）では、プロキシを通すので空にする。
 */
export function createSignalRLink(baseUrl = ''): ServerLink {
  return {
    connect: (handlers) => connect(baseUrl, handlers),
    startSystem: () => postCommand(baseUrl, '/system/start', COMMAND_TIMEOUT_MS),
    stopSystem: () => postCommand(baseUrl, '/system/stop', COMMAND_TIMEOUT_MS),
    operateAgv: (id, command) => sendAgvCommand(baseUrl, id, command, COMMAND_TIMEOUT_MS),
    operateConsumables: (command) => sendConsumableCommand(baseUrl, command, COMMAND_TIMEOUT_MS),
    cancelTransport: (number) => sendCancelTransport(baseUrl, number, COMMAND_TIMEOUT_MS),
    operateAgvTypes: (command) => sendAgvTypeCommand(baseUrl, command, COMMAND_TIMEOUT_MS),
    operateAgvRegistrations: (command) => sendAgvRegistrationCommand(baseUrl, command, COMMAND_TIMEOUT_MS),
    operateConfiguration: (command) => sendConfigurationCommand(baseUrl, command, COMMAND_TIMEOUT_MS),
    loadMapDraft: () => loadMapDraft(baseUrl, COMMAND_TIMEOUT_MS),
    saveMapDraft: (revision, draft) => saveMapDraft(baseUrl, revision, draft, COMMAND_TIMEOUT_MS),
    validateMapDraft: (draft) => validateMapDraft(baseUrl, draft, COMMAND_TIMEOUT_MS),
    publishMapDraft: (revision, draft) => publishMapDraft(baseUrl, revision, draft, COMMAND_TIMEOUT_MS),
    saveCarrierHeight: (panel, heightMm) => saveCarrierHeight(baseUrl, panel, heightMm, COMMAND_TIMEOUT_MS),
  };
}

function connect(baseUrl: string, handlers: ServerHandlers): () => void {
  const connection = new HubConnectionBuilder()
    .withUrl(`${baseUrl}/hubs/screen`)
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (context) => retryDelayMs(context.previousRetryCount) })
    .configureLogging(LogLevel.Warning)
    .build();
  let closed = false;
  let timer: ReturnType<typeof setTimeout> | undefined;

  const start = (attempt: number) => {
    connection.start().then(
      () => {
        if (!closed) {
          handlers.onConnection('connected');
        }
      },
      (error: unknown) => {
        if (closed) {
          return;
        }
        console.warn('サーバーとつなげませんでした。間を空けて、つなぎ直します。', error);
        timer = setTimeout(() => start(attempt + 1), retryDelayMs(attempt + 1));
      },
    );
  };

  connection.on(SYSTEM_STATUS, (value: unknown) => {
    try {
      handlers.onSystemStatus(parseSystemStatus(value));
    } catch (error) {
      // 形の正しくない様子は使わず、今の様子のままにする
      console.error('サーバーから届いた運転状態の様子を使えませんでした。', error);
    }
  });
  connection.on(AGVS, (value: unknown) => {
    try {
      handlers.onAgvs(parseAgvs(value));
    } catch (error) {
      // 形の正しくない様子は使わず、今の様子のままにする
      console.error('サーバーから届いた AGV の様子を使えませんでした。', error);
    }
  });
  connection.on(MESSAGES, (value: unknown) => {
    try {
      handlers.onMessages(parseMessages(value));
    } catch (error) {
      // 形の正しくない様子は使わず、今の様子のままにする
      console.error('サーバーから届いたメッセージの様子を使えませんでした。', error);
    }
  });
  connection.on(MAP, (value: unknown) => {
    try {
      handlers.onMap(parseMap(value));
    } catch (error) {
      // 形の正しくない様子は使わず、今の様子のままにする
      console.error('サーバーから届いたマップの様子を使えませんでした。', error);
    }
  });
  connection.on(EQUIPMENT, (value: unknown) => {
    try {
      handlers.onEquipment(parseEquipmentStatus(value));
    } catch (error) {
      // 形の正しくない様子は使わず、今の様子のままにする
      console.error('サーバーから届いた装置とステーションの様子を使えませんでした。', error);
    }
  });
  connection.on(TRANSPORTS, (value: unknown) => {
    try {
      handlers.onTransports(parseTransports(value));
    } catch (error) {
      // 形の正しくない様子は使わず、今の様子のままにする
      console.error('サーバーから届いた搬送の一覧を使えませんでした。', error);
    }
  });
  connection.on(CONSUMABLES, (value: unknown) => {
    try {
      handlers.onConsumables(parseConsumables(value));
    } catch (error) {
      // 形の正しくない様子は使わず、今の様子のままにする
      console.error('サーバーから届いた消耗品の記録を使えませんでした。', error);
    }
  });
  connection.on(FLEET, (value: unknown) => {
    try {
      handlers.onFleet(parseFleet(value));
    } catch (error) {
      // 形の正しくない様子は使わず、今の様子のままにする
      console.error('サーバーから届いた AGV と AGV の種類の登録を使えませんでした。', error);
    }
  });
  connection.on(CONFIGURATION, (value: unknown) => {
    try {
      handlers.onConfiguration(parseConfiguration(value));
    } catch (error) {
      // 形の正しくない様子は使わず、今の様子のままにする
      console.error('サーバーから届いた消耗品の構成を使えませんでした。', error);
    }
  });
  connection.onreconnecting(() => handlers.onConnection('reconnecting'));
  connection.onreconnected(() => handlers.onConnection('connected'));
  connection.onclose(() => {
    // 自動のつなぎ直しはずっと続けるので、ここに来るのは、こちらから切ったときか、つなぎ直しが止まったときだけ
    if (!closed) {
      handlers.onConnection('reconnecting');
      start(0);
    }
  });

  handlers.onConnection('connecting');
  // つなぎ始めは、ひと呼吸おく。描いてすぐ消された（React の StrictMode など）ときに、交渉の途中で切らないため
  timer = setTimeout(() => start(0), 0);

  return () => {
    closed = true;
    clearTimeout(timer);
    void connection.stop();
  };
}

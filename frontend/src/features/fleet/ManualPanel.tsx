import { useId, useRef, useState } from 'react';
import { heading as toHeading, type Heading, type PanelId } from '../routing/model';
import { manualAvailability, outcomeMessage, type ManualInput, type ManualOutcome } from './manual';

export type ManualStepCommand =
  | { readonly kind: 'forward' }
  | { readonly kind: 'backward' }
  /** ラインを読み取る回転（0・90・180・270度）。 */
  | { readonly kind: 'rotate'; readonly heading: Heading }
  /** 角度を指定した回転（0〜359度）。 */
  | { readonly kind: 'freeRotate'; readonly heading: Heading };

export interface DestinationOption {
  readonly id: PanelId;
  readonly label: string;
  /** 着いたときの向きが決まっているパネル（特殊消耗品受け渡しパネル。キャリアを下ろす角度）なら、その向き。 */
  readonly fixedArrival?: Heading;
}

export interface ManualPanelProps {
  readonly input: ManualInput;
  readonly destinations: readonly DestinationOption[];
  /** 行き先へ動かす。arrival は着いたときの向き（指定しなければ null）。 */
  readonly onMove: (destination: PanelId, arrival: Heading | null) => Promise<ManualOutcome>;
  readonly onStep: (step: ManualStepCommand) => Promise<ManualOutcome>;
  readonly onCarrier: (action: 'raise' | 'lower') => Promise<ManualOutcome>;
  /** サーバーとつながっていないか（どれも送れなくする）。 */
  readonly offline: boolean;
}

const CARDINALS = [
  [0, '北'],
  [90, '東'],
  [180, '南'],
  [270, '西'],
] as const;

const button =
  'min-h-11 rounded-lg border border-control-border bg-white px-3 text-sm font-bold text-graphite focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus disabled:cursor-not-allowed disabled:border-disabled-border disabled:bg-ground disabled:text-disabled-text';
const select =
  'min-h-11 rounded-lg border border-control-border bg-white px-2.5 text-[15px] text-graphite focus-visible:outline-3 focus-visible:outline-focus';
const tab = 'min-h-11 px-3 text-[13px] font-bold focus-visible:outline-3 focus-visible:outline-focus';

function parseFreeHeading(text: string): Heading | null {
  const n = Number(text);
  return text.trim() !== '' && Number.isInteger(n) && n >= 0 && n < 360 ? toHeading(n) : null;
}

/** マニュアル操作の欄（AGV のカードの中に出す）。送れないものは押せなくし、理由を出す。 */
export function ManualPanel({ input, destinations, onMove, onStep, onCarrier, offline }: ManualPanelProps) {
  const ids = { destination: useId(), arrival: useId(), free: useId() };
  const [mode, setMode] = useState<'destination' | 'steps'>('destination');
  const [destination, setDestination] = useState<string>(destinations[0]?.id ?? '');
  const [arrival, setArrival] = useState('');
  const [freeText, setFreeText] = useState('0');
  const [pending, setPending] = useState(false);
  const sending = useRef(false);
  const [message, setMessage] = useState<string | null>(null);

  const a = manualAvailability(input);
  // 送ってから結果が返るまでと、つながっていないあいだは、どれも送れない
  const blocked = pending || offline;
  const fixedArrival = destinations.find((d) => d.id === destination)?.fixedArrival;
  const freeHeading = parseFreeHeading(freeText);

  // 送ってから結果が返るまでは、続けて送らない（ボタンの連打を防ぐ）
  const send = async (kind: 'move' | 'step' | 'carrier', run: () => Promise<ManualOutcome>) => {
    if (sending.current) {
      return;
    }
    sending.current = true;
    setPending(true);
    setMessage(null);
    try {
      setMessage(outcomeMessage(kind, await run()));
    } finally {
      sending.current = false;
      setPending(false);
    }
  };

  const reasons = [
    ...new Set(
      [mode === 'destination' ? a.destination.reason : a.step.reason, mode === 'steps' ? a.forwardBackward.reason : null, a.carrier.reason].filter(
        (r): r is string => r !== null,
      ),
    ),
  ];

  return (
    <div className="flex flex-col gap-3.5 border-t border-line px-5 pt-4 pb-6">
      <h3 className="m-0 text-sm font-bold">マニュアル操作</h3>
      <div role="group" aria-label="動かし方" className="grid grid-cols-2 overflow-hidden rounded-lg border border-control-border bg-white">
        {(
          [
            ['destination', '行き先を指定する'],
            ['steps', '1つずつ動かす'],
          ] as const
        ).map(([key, label]) => (
          <button
            key={key}
            type="button"
            aria-pressed={mode === key}
            onClick={() => setMode(key)}
            className={`${tab} ${mode === key ? 'bg-graphite text-white' : 'bg-white text-graphite'}`}
          >
            {label}
          </button>
        ))}
      </div>

      {mode === 'destination' ? (
        <div className="flex flex-col gap-2.5">
          <label htmlFor={ids.destination} className="text-[13px] font-bold">
            行き先のパネル
          </label>
          <select id={ids.destination} value={destination} onChange={(e) => setDestination(e.target.value)} className={select}>
            {destinations.map((d) => (
              <option key={d.id} value={d.id}>
                {d.label}
              </option>
            ))}
          </select>
          <label htmlFor={ids.arrival} className="text-[13px] font-bold">
            着いたときの向き
          </label>
          <select
            id={ids.arrival}
            value={fixedArrival === undefined ? arrival : ''}
            disabled={fixedArrival !== undefined}
            onChange={(e) => setArrival(e.target.value)}
            className={`${select} disabled:bg-ground disabled:text-disabled-text`}
          >
            <option value="">指定しない</option>
            {CARDINALS.map(([deg, dir]) => (
              <option key={deg} value={deg}>
                {deg}°（{dir}）
              </option>
            ))}
          </select>
          {fixedArrival !== undefined && <p className="m-0 text-xs text-ink-muted">着いたら、キャリアを下ろす角度（{fixedArrival}°）を向きます。</p>}
          <button
            type="button"
            disabled={blocked || !a.destination.allowed || destination === ''}
            onClick={() => {
              const target = destinations.find((d) => d.id === destination);
              if (target) {
                // 向きが決まっているパネルでは、向きを送らない（サーバーが決まった向きにする）
                const chosen = target.fixedArrival === undefined && arrival !== '' ? toHeading(Number(arrival)) : null;
                void send('move', () => onMove(target.id, chosen));
              }
            }}
            className="min-h-11 rounded-lg bg-graphite px-4 text-sm font-bold text-white focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-focus disabled:cursor-not-allowed disabled:bg-disabled-border disabled:text-disabled-text"
          >
            この行き先へ動かす
          </button>
          <p className="m-0 text-xs text-ink-muted">経路はシステムが決め、ほかの AGV を避けて動かします。</p>
        </div>
      ) : (
        <div className="flex flex-col gap-2.5">
          <div className="grid grid-cols-2 gap-2">
            <button type="button" disabled={blocked || !a.forwardBackward.allowed} onClick={() => void send('step', () => onStep({ kind: 'forward' }))} className={button}>
              前進
            </button>
            <button type="button" disabled={blocked || !a.forwardBackward.allowed} onClick={() => void send('step', () => onStep({ kind: 'backward' }))} className={button}>
              後退
            </button>
          </div>
          <div className="text-[13px] font-bold">回転（ラインに合わせる）</div>
          <div className="grid grid-cols-2 gap-2">
            {CARDINALS.map(([deg, dir]) => (
              <button
                key={deg}
                type="button"
                disabled={blocked || !a.step.allowed}
                onClick={() => void send('step', () => onStep({ kind: 'rotate', heading: toHeading(deg) }))}
                className={button}
              >
                {deg}°（{dir}）
              </button>
            ))}
          </div>
          <label htmlFor={ids.free} className="text-[13px] font-bold">
            角度を指定した回転（0〜359度。北が0度、時計回り）
          </label>
          <div className="flex gap-2">
            <input
              id={ids.free}
              type="number"
              min={0}
              max={359}
              value={freeText}
              onChange={(e) => setFreeText(e.target.value)}
              className={`${select} w-28 font-number text-[17px] font-semibold`}
            />
            <button
              type="button"
              disabled={blocked || !a.step.allowed || freeHeading === null}
              onClick={() => {
                if (freeHeading !== null) {
                  void send('step', () => onStep({ kind: 'freeRotate', heading: freeHeading }));
                }
              }}
              className={button}
            >
              この角度に回す
            </button>
          </div>
          {freeHeading === null && <p className="m-0 text-xs text-attention">0〜359 の整数を入れてください。</p>}
          <p className="m-0 text-xs text-ink-muted">前の動作が終わる前に押すと、順に並べます。</p>
        </div>
      )}

      <div className="grid grid-cols-2 gap-2">
        <button type="button" disabled={blocked || !a.carrier.allowed} onClick={() => void send('carrier', () => onCarrier('raise'))} className={button}>
          キャリアを上げる
        </button>
        <button type="button" disabled={blocked || !a.carrier.allowed} onClick={() => void send('carrier', () => onCarrier('lower'))} className={button}>
          キャリアを下げる
        </button>
      </div>

      {reasons.length > 0 && (
        <ul className="m-0 flex list-none flex-col gap-1 p-0 text-xs text-ink-muted">
          {reasons.map((r) => (
            <li key={r}>{r}</li>
          ))}
        </ul>
      )}
      <p role="status" className="m-0 min-h-5 text-[13px] font-bold text-attention">
        {message}
      </p>
    </div>
  );
}

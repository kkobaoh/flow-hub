import { describe, expect, it } from 'vitest';
import { agvId, indicatorLight, type AgvStatus } from './agv';

const idle: AgvStatus = { activity: 'idle', charging: false, carrierMoving: false };

describe('agvId', () => {
  it('空でない文字列から作れる', () => {
    expect(agvId('AGV-1')).toBe('AGV-1');
  });

  it('空の文字列からは作れない', () => {
    expect(() => agvId('')).toThrow(RangeError);
  });
});

describe('indicatorLight（AGV の標示灯）', () => {
  it('走行動作中は、青の点滅', () => {
    expect(indicatorLight({ ...idle, activity: 'moving' }, false)).toEqual({ color: 'moving', blinking: true });
    expect(indicatorLight({ ...idle, activity: 'moving' }, true)).toEqual({ color: 'moving', blinking: true });
  });

  it('充電パネル以外で待機しているときは、黄', () => {
    expect(indicatorLight(idle, false)).toEqual({ color: 'waiting', blinking: false });
  });

  it('充電パネルで充電しているときは、緑', () => {
    expect(indicatorLight({ ...idle, charging: true }, true)).toEqual({ color: 'charging', blinking: false });
  });

  it('充電パネルにいるのに充電できていないときは、紫', () => {
    expect(indicatorLight(idle, true)).toEqual({ color: 'chargeFailed', blinking: false });
  });

  it('緑と紫は、キャリアを上げ下げしているあいだ点滅する', () => {
    expect(indicatorLight({ ...idle, charging: true, carrierMoving: true }, true)).toEqual({ color: 'charging', blinking: true });
    expect(indicatorLight({ ...idle, carrierMoving: true }, true)).toEqual({ color: 'chargeFailed', blinking: true });
  });
});

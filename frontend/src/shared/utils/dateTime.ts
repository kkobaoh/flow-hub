/** 時刻を「10:02」の形にする。 */
export function clockOf(time: Date): string {
  return `${String(time.getHours()).padStart(2, '0')}:${String(time.getMinutes()).padStart(2, '0')}`;
}

/** 日時を「11/20 14:02」の形にする。 */
export function dateTimeOf(time: Date): string {
  return `${time.getMonth() + 1}/${time.getDate()} ${clockOf(time)}`;
}

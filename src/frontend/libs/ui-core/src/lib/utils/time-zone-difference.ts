/** How the time zone times are shown in differs from the viewer's own, at one instant. */
export interface TimeZoneDifference {
  /** The zone times are shown in (IANA), and its short name at that instant, e.g. "WAT" or "GMT+1". */
  shownZone: string;
  shownZoneName: string;
  /** The viewer's device zone (IANA) and its short name. */
  viewerZone: string;
  viewerZoneName: string;
  /** Minutes the shown zone is ahead of the viewer's (negative when behind). */
  aheadByMinutes: number;
}

/** Minutes a zone is ahead of UTC at an instant. */
export function utcOffsetMinutes(zone: string, at: Date): number {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: zone, hourCycle: 'h23', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit',
  }).formatToParts(at);
  const part = (type: Intl.DateTimeFormatPartTypes) => Number(parts.find((p) => p.type === type)?.value);
  const asUtc = Date.UTC(part('year'), part('month') - 1, part('day'), part('hour'), part('minute'));
  return Math.round((asUtc - Math.floor(at.getTime() / 60_000) * 60_000) / 60_000);
}

function zoneName(zone: string, at: Date): string {
  return new Intl.DateTimeFormat('en-US', { timeZone: zone, timeZoneName: 'short' }).formatToParts(at)
    .find((p) => p.type === 'timeZoneName')?.value ?? zone;
}

/**
 * Whether times shown in <paramref name="shownZone"/> read differently from the viewer's own clock at <paramref name="at"/>;
 * null when there is no shown zone (times already follow the viewer) or both zones give the same clock time.
 */
export function timeZoneDifference(shownZone: string | null | undefined, viewerZone: string, at: Date): TimeZoneDifference | null {
  if (!shownZone) return null;
  const ahead = utcOffsetMinutes(shownZone, at) - utcOffsetMinutes(viewerZone, at);
  if (ahead === 0) return null;
  return { shownZone, shownZoneName: zoneName(shownZone, at), viewerZone, viewerZoneName: zoneName(viewerZone, at), aheadByMinutes: ahead };
}

/** "1 hour ahead of you", "5 hours 30 minutes behind you". */
export function describeZoneGap(aheadByMinutes: number): string {
  const total = Math.abs(aheadByMinutes);
  const hours = Math.floor(total / 60);
  const minutes = total % 60;
  const parts = [hours ? `${hours} hour${hours === 1 ? '' : 's'}` : '', minutes ? `${minutes} minute${minutes === 1 ? '' : 's'}` : ''].filter(Boolean);
  return `${parts.join(' ')} ${aheadByMinutes > 0 ? 'ahead of' : 'behind'} you`;
}

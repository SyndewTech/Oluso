import { describeZoneGap, timeZoneDifference, utcOffsetMinutes } from './time-zone-difference';

describe('time zone difference', () => {
  const summer = new Date(Date.UTC(2026, 6, 1, 12, 0));
  const winter = new Date(Date.UTC(2026, 0, 15, 12, 0));

  it('reads a zone offset including daylight saving', () => {
    expect(utcOffsetMinutes('Africa/Lagos', summer)).toBe(60);
    expect(utcOffsetMinutes('Europe/London', summer)).toBe(60);
    expect(utcOffsetMinutes('Europe/London', winter)).toBe(0);
    expect(utcOffsetMinutes('Asia/Kolkata', winter)).toBe(330);
  });

  it('is null when there is no shown zone or the clocks agree', () => {
    expect(timeZoneDifference(undefined, 'Europe/London', summer)).toBeNull();
    expect(timeZoneDifference('Africa/Lagos', 'Europe/London', summer)).toBeNull();
  });

  it('says how far the shown zone is from the viewer', () => {
    const diff = timeZoneDifference('Africa/Lagos', 'Europe/London', winter)!;
    expect(diff.aheadByMinutes).toBe(60);
    expect(describeZoneGap(diff.aheadByMinutes)).toBe('1 hour ahead of you');
    expect(describeZoneGap(-330)).toBe('5 hours 30 minutes behind you');
  });
});

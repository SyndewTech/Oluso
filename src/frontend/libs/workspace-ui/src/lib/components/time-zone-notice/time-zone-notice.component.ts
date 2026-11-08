import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { IconComponent, LocaleService, describeZoneGap, timeZoneDifference } from '@oluso/ui-core';

/**
 * Shown in the top bar only when the organisation's time zone, which every time on the site is shown in, gives a
 * different clock time from the viewer's device: the organisation's time now beside the viewer's own, so a
 * "09:30" on a list is not mistaken for the viewer's 09:30.
 */
@Component({
  selector: 'workspace-time-zone-notice',
  standalone: true,
  imports: [IconComponent],
  template: `
    @if (difference(); as d) {
      <span class="inline-flex items-center gap-1.5 rounded-lg bg-amber-50 px-2.5 py-1 text-xs text-amber-900 ring-1 ring-amber-200"
        [title]="explanation()" role="status" [attr.aria-label]="explanation()">
        <ui-icon name="clock" class="h-3.5 w-3.5 text-amber-700" />
        <span class="font-semibold tabular-nums">{{ shownTime() }} {{ d.shownZoneName }}</span>
        <span class="text-amber-700">· yours {{ viewerTime() }}</span>
      </span>
    }
  `,
})
export class TimeZoneNoticeComponent {
  private readonly locale = inject(LocaleService);
  private readonly viewerZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  private readonly now = signal(new Date());

  readonly difference = computed(() => timeZoneDifference(this.locale.timezone(), this.viewerZone, this.now()));

  readonly shownTime = computed(() => this.clock(this.difference()?.shownZone));
  readonly viewerTime = computed(() => this.clock(this.viewerZone));

  readonly explanation = computed(() => {
    const d = this.difference();
    if (!d) return '';
    return `Times here are in ${this.place(d.shownZone)} time (${d.shownZoneName}), ${describeZoneGap(d.aheadByMinutes)}. `
      + `It is ${this.shownTime()} there and ${this.viewerTime()} where you are (${this.place(d.viewerZone)}).`;
  });

  constructor() {
    const timer = setInterval(() => this.now.set(new Date()), 30_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  private clock(zone: string | undefined): string {
    return zone ? new Intl.DateTimeFormat(this.locale.locale() || undefined, { timeZone: zone, hour: 'numeric', minute: '2-digit' }).format(this.now()) : '';
  }

  private place(zone: string): string {
    return zone.split('/').pop()!.replace(/_/g, ' ');
  }
}

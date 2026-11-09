import { Pipe, PipeTransform, inject } from '@angular/core';
import { LocaleService, type LocaleDateInput, type LocaleDateStyle } from '../services/locale.service';

/**
 * Tenant-aware replacement for Angular's `date` pipe. Same preset names
 * (`mediumDate`, `short`, `shortTime`…) but the locale, day/month order and
 * time zone come from tenant settings, and calendar dates from the backend
 * are never shifted by the viewer's zone. Impure so it re-renders when the
 * tenant settings signal changes.
 */
@Pipe({ name: 'localeDate', standalone: true, pure: false })
export class LocaleDatePipe implements PipeTransform {
  private readonly locale = inject(LocaleService);

  transform(value: LocaleDateInput, style: LocaleDateStyle = 'mediumDate', timeZone?: string): string {
    return this.locale.formatDate(value, style, timeZone);
  }
}

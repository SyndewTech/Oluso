import { Injectable, inject } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { TerminologyTranslocoLoader, ensureTerminologyLang } from './terminology-loader';

/**
 * Re-fetches the active terminology bundle on demand and re-renders bound
 * views with the new labels. Used for the live relabel: when a tenant admin
 * changes a term (or the tenant industry), the backend broadcasts on the
 * terminology realtime channel; a consumer calls {@link refresh} so open
 * screens pick up the new wording without a full reload.
 *
 * Lives in `@syndew/oluso-ui-core` (no realtime dependency) so any app can wire it to
 * whatever transport it already has.
 */
@Injectable({ providedIn: 'root' })
export class TerminologyRefreshService {
  private readonly transloco = inject(TranslocoService);
  private readonly loader = inject(TerminologyTranslocoLoader);

  /**
   * Reload the bundle for the given lang (defaults to the active lang) and
   * re-render. We re-run the HTTP loader explicitly (rather than `load()`,
   * which returns Transloco's cached copy) and replace the stored translation
   * with `merge: false`. Setting the active lang afterwards triggers the
   * `reRenderOnLangChange` subscribers so the new labels paint immediately.
   *
   * Best-effort: a failed refresh keeps the current labels rather than
   * blanking the UI.
   */
  async refresh(lang?: string): Promise<void> {
    const target = lang ?? this.transloco.getActiveLang();
    try {
      const translation = await firstValueFrom(this.loader.getTranslation(target));
      ensureTerminologyLang(this.transloco, target);
      this.transloco.setTranslation(translation, target, { merge: false });
      this.transloco.setActiveLang(target);
    } catch {
      // Keep existing labels on failure.
    }
  }
}

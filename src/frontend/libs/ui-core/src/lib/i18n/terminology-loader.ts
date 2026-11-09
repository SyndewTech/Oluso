import { Injectable, InjectionToken, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { Translation, TranslocoLoader, TranslocoService } from '@jsverse/transloco';

/**
 * Configuration for the terminology / i18n layer.
 *
 * The runtime read path is a single
 * server-merged flat map: `GET {endpoint}?locale=`. The cascade
 * (generic default ◁ industry pack ◁ tenant override) is resolved
 * server-side; the client just consumes the result.
 */
export interface TerminologyConfig {
  /**
   * Full URL of the shared platform terminology endpoint, e.g.
   * `${serverUrl}/api/terminology`. The loader appends `?locale=<lang>`.
   */
  endpoint: string;
  /** Locale codes the app may switch between. Defaults to `[defaultLang]`. */
  availableLangs?: string[];
  /** Default/active locale. Defaults to `DEFAULT_LOCALE_SETTINGS.locale`. */
  defaultLang?: string;
}

export const TERMINOLOGY_CONFIG = new InjectionToken<TerminologyConfig>('TERMINOLOGY_CONFIG');

/**
 * Register a tenant locale with Transloco before it is activated. Transloco
 * reads any lang outside `availableLangs` as a scope name and prefixes every
 * key with it (`en-NG` → `enNG.common.entity.singular`), so a tenant whose
 * locale differs from the configured default would see raw keys everywhere.
 */
export function ensureTerminologyLang(transloco: TranslocoService, lang: string): void {
  const known = transloco.getAvailableLangs().map((l) => (typeof l === 'string' ? l : l.id));
  if (!known.includes(lang)) {
    transloco.setAvailableLangs([...known, lang]);
  }
}

/**
 * Turns a flat dotted map — `{ "common.entity.singular": "Item" }` —
 * into the nested object Transloco traverses on a `t('common.entity...')` lookup.
 * The server hands us a flat map (one merge, one payload); Transloco wants
 * nesting, so we bridge here rather than forcing either side to change.
 */
export function unflattenTerminology(flat: Record<string, string>): Translation {
  const result: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(flat ?? {})) {
    const parts = key.split('.');
    let node = result;
    for (let i = 0; i < parts.length - 1; i++) {
      const segment = parts[i];
      // Guard against a leaf colliding with a branch (e.g. both "a" and
      // "a.b" present); the branch wins so deeper keys stay reachable.
      if (typeof node[segment] !== 'object' || node[segment] === null) {
        node[segment] = {};
      }
      node = node[segment] as Record<string, unknown>;
    }
    node[parts[parts.length - 1]] = value;
  }
  return result as Translation;
}

/**
 * Transloco loader backed by the server-merged terminology endpoint. Each
 * "language" is a locale; the server returns the resolved labels for the
 * current tenant's industry + that locale. Failures degrade to an empty
 * bundle so the app still renders (keys fall back to their literal text).
 */
@Injectable({ providedIn: 'root' })
export class TerminologyTranslocoLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);
  private readonly config = inject(TERMINOLOGY_CONFIG);

  getTranslation(lang: string): Observable<Translation> {
    return this.http
      .get<Record<string, string>>(this.config.endpoint, { params: { locale: lang } })
      .pipe(
        map((flat) => unflattenTerminology(flat)),
        catchError(() => of({} as Translation)),
      );
  }
}

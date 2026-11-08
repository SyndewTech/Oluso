import { EnvironmentProviders, Injector, makeEnvironmentProviders } from '@angular/core';
import { provideTransloco, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { DEFAULT_LOCALE_SETTINGS, LocaleService } from '../services/locale.service';
import {
  TERMINOLOGY_CONFIG,
  TerminologyConfig,
  TerminologyTranslocoLoader,
  ensureTerminologyLang,
} from './terminology-loader';

/**
 * Wire Transloco once, here in `@oluso/ui-core`, so every app and lib
 * relabels consistently with no per-lib setup. The three standalone apps opt in
 * with a single
 * `provideTerminology({ endpoint })` in their `app.config.ts`.
 *
 * Domain nouns are namespaced (`<module>.*`, `common.*`) and resolved
 * server-side, so templates just use the stock Transloco pipe:
 * `{{ 'common.entity.singular' | transloco }}`.
 */
export function provideTerminology(config: TerminologyConfig): EnvironmentProviders {
  const defaultLang = config.defaultLang ?? DEFAULT_LOCALE_SETTINGS.locale;
  const availableLangs = config.availableLangs ?? [defaultLang];

  return makeEnvironmentProviders([
    { provide: TERMINOLOGY_CONFIG, useValue: config },
    provideTransloco({
      config: {
        availableLangs,
        defaultLang,
        // Domain labels change at runtime (language switch / tenant override
        // push), so re-render bound views when the active lang changes.
        reRenderOnLangChange: true,
        // A missing key is a seeding bug, not a user-facing one — keep the
        // literal key visible in dev rather than blanking the UI.
        missingHandler: { logMissingKey: true, useFallbackTranslation: false, allowEmpty: false },
      },
      loader: TerminologyTranslocoLoader,
    }),
  ]);
}

/**
 * Load the terminology bundle for the current locale **before the app
 * renders**. Call from each app's existing `APP_INITIALIZER` factory, after
 * `LocaleService.loadCurrentLocale()` has resolved, so the active language
 * matches the tenant locale and the first paint already has labels.
 *
 * An initializer factory keeps its injection context only until its first
 * `await`, so capture `inject(Injector)` synchronously at the top of the
 * factory and pass it here; the services are resolved from that injector.
 *
 * Tolerant by design: a terminology fetch failure must never block boot, so
 * we swallow load errors (the loader already degrades to an empty bundle).
 */
export async function loadTerminology(injector: Injector): Promise<void> {
  const transloco = injector.get(TranslocoService);
  const locale = injector.get(LocaleService);

  const lang = locale.locale() || transloco.getDefaultLang();
  ensureTerminologyLang(transloco, lang);
  transloco.setActiveLang(lang);
  try {
    await firstValueFrom(transloco.load(lang));
  } catch {
    // Best-effort — render with literal keys rather than fail boot.
  }
}

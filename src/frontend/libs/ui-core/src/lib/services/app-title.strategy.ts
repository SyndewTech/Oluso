import { Injectable, InjectionToken, Injector, inject, type Provider } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { type RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { TRANSLOCO_LOADER, TranslocoService } from '@jsverse/transloco';
import { BrandingService } from './branding.service';

/** The app a page title ends with, and the branding id a host can rename it under. */
export interface AppTitleConfig {
  /** The app's own name, such as "Admin Portal". */
  name: string;
  /** The app's id in the runtime branding config (branding.apps.<id>.name), which renames it when set. */
  brandingId?: string;
}

export const APP_TITLE_CONFIG = new InjectionToken<AppTitleConfig>('APP_TITLE_CONFIG');

/** A route title that is a translation key ("crm.objects.title") rather than words. */
const KEY_SHAPE = /^[a-z0-9_-]+(\.[a-z0-9_-]+)+$/i;

/**
 * Titles the browser tab "<Page> · <App>" from the deepest route's `title`, or the app name alone when no route
 * gives one. A title shaped like a translation key is translated where the app runs Transloco.
 */
@Injectable()
export class AppTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly config = inject(APP_TITLE_CONFIG);
  private readonly branding = inject(BrandingService);
  private readonly injector = inject(Injector);
  /** Only apps that set up terminology run Transloco; elsewhere its service can't be made. */
  private readonly translates = inject(TRANSLOCO_LOADER, { optional: true }) !== null;

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const app = this.config.brandingId ? this.branding.getAppName(this.config.brandingId, this.config.name) : this.config.name;
    const page = this.pageTitle(this.buildTitle(snapshot));
    this.title.setTitle(page && page !== app ? `${page} · ${app}` : app);
  }

  private pageTitle(title: string | undefined): string | undefined {
    if (!title || !this.translates || !KEY_SHAPE.test(title)) return title;
    const translated = this.injector.get(TranslocoService).translate(title);
    return translated && translated !== title ? translated : title;
  }
}

/** Registers the shared title strategy for a standalone app. */
export function provideAppTitle(config: AppTitleConfig): Provider[] {
  return [
    { provide: APP_TITLE_CONFIG, useValue: config },
    { provide: TitleStrategy, useClass: AppTitleStrategy },
  ];
}

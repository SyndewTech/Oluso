import { Component, DestroyRef, ElementRef, effect, inject, input, output, untracked, viewChild } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { OlusoClient, mountJourney, type JourneyMount, type JourneySession, type MountJourneyOptions } from '@oluso/sdk';
import { httpClientFetch } from '@oluso/auth';

/**
 * Runs a journey session the app's backend started for the signed-in user, drawn by the Oluso SDK. Every step goes
 * through the app's HttpClient, so the session continues with this user's access token, tenant and DPoP proof.
 */
@Component({
  selector: 'oluso-journey',
  standalone: true,
  template: `<div #host></div>`,
})
export class JourneyHostComponent {
  /** The session to run, as the backend's start endpoint returned it. */
  readonly session = input.required<JourneySession>();
  /** Base URL of the API that serves `/api/public/journeys`. */
  readonly serverUrl = input.required<string>();
  /** The host app's colour scheme, so the form matches the page around it rather than the device setting. */
  readonly theme = input.required<NonNullable<MountJourneyOptions['theme']>>();

  readonly completed = output<JourneySession>();
  readonly failed = output<Error>();

  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
  private readonly http = inject(HttpClient);
  private mount: JourneyMount | null = null;

  constructor() {
    effect(() => {
      const session = this.session();
      const serverUrl = this.serverUrl();
      const theme = this.theme();
      const container = this.host().nativeElement;
      untracked(() => {
        this.mount?.destroy();
        container.replaceChildren();
        this.mount = mountJourney(container, {
          client: new OlusoClient({ serverUrl, fetch: httpClientFetch(this.http) }),
          session,
          theme,
          onComplete: (done) => this.completed.emit(done),
          onError: (error) => this.failed.emit(error),
        });
      });
    });
    inject(DestroyRef).onDestroy(() => this.mount?.destroy());
  }
}

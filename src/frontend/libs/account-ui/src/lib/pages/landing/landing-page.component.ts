import { Component, inject, Type } from '@angular/core';
import { NgComponentOutlet } from '@angular/common';
import { ACCOUNT_LANDING_COMPONENT } from './landing.token';
import { DefaultLandingPageComponent } from './default-landing-page.component';

@Component({
  selector: 'account-landing-page',
  standalone: true,
  imports: [NgComponentOutlet],
  template: `<ng-container *ngComponentOutlet="component"></ng-container>`,
})
export class LandingPageComponent {
  readonly component: Type<unknown> =
    inject(ACCOUNT_LANDING_COMPONENT, { optional: true }) ?? DefaultLandingPageComponent;
}

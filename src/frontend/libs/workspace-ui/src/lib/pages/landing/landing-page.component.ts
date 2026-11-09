import { Component, inject, Type } from '@angular/core';
import { NgComponentOutlet } from '@angular/common';
import { WORKSPACE_LANDING_COMPONENT } from './landing.token';
import { DefaultLandingPageComponent } from './default-landing-page.component';

@Component({
  selector: 'workspace-landing-page',
  standalone: true,
  imports: [NgComponentOutlet],
  template: `<ng-container *ngComponentOutlet="component"></ng-container>`,
})
export class LandingPageComponent {
  readonly component: Type<unknown> =
    inject(WORKSPACE_LANDING_COMPONENT, { optional: true }) ?? DefaultLandingPageComponent;
}

import {
  Directive,
  Input,
  TemplateRef,
  ViewContainerRef,
  inject,
  effect,
} from '@angular/core';
import { FeatureService } from '../services/feature.service';

/**
 * Structural directive to conditionally render content based on feature flags.
 *
 * Usage:
 * ```html
 * <div *uiHasFeature="'new-dashboard'">New Dashboard Content</div>
 *
 * <!-- With any feature -->
 * <div *uiHasAnyFeature="['feature-a', 'feature-b']">...</div>
 *
 * <!-- With all features -->
 * <div *uiHasAllFeatures="['feature-a', 'feature-b']">...</div>
 * ```
 */
@Directive({
  selector: '[uiHasFeature]',
  standalone: true,
})
export class HasFeatureDirective {
  private templateRef = inject(TemplateRef<unknown>);
  private viewContainer = inject(ViewContainerRef);
  private featureService = inject(FeatureService);
  private hasView = false;

  @Input() set uiHasFeature(feature: string) {
    effect(() => {
      const hasFeature = this.featureService.hasFeature(feature);
      this.updateView(hasFeature);
    });
  }

  private updateView(hasFeature: boolean): void {
    if (hasFeature && !this.hasView) {
      this.viewContainer.createEmbeddedView(this.templateRef);
      this.hasView = true;
    } else if (!hasFeature && this.hasView) {
      this.viewContainer.clear();
      this.hasView = false;
    }
  }
}

@Directive({
  selector: '[uiHasAnyFeature]',
  standalone: true,
})
export class HasAnyFeatureDirective {
  private templateRef = inject(TemplateRef<unknown>);
  private viewContainer = inject(ViewContainerRef);
  private featureService = inject(FeatureService);
  private hasView = false;

  @Input() set uiHasAnyFeature(features: string[]) {
    effect(() => {
      const hasFeature = features.some((f) => this.featureService.hasFeature(f));
      this.updateView(hasFeature);
    });
  }

  private updateView(hasFeature: boolean): void {
    if (hasFeature && !this.hasView) {
      this.viewContainer.createEmbeddedView(this.templateRef);
      this.hasView = true;
    } else if (!hasFeature && this.hasView) {
      this.viewContainer.clear();
      this.hasView = false;
    }
  }
}

@Directive({
  selector: '[uiHasAllFeatures]',
  standalone: true,
})
export class HasAllFeaturesDirective {
  private templateRef = inject(TemplateRef<unknown>);
  private viewContainer = inject(ViewContainerRef);
  private featureService = inject(FeatureService);
  private hasView = false;

  @Input() set uiHasAllFeatures(features: string[]) {
    effect(() => {
      const hasFeature = features.every((f) => this.featureService.hasFeature(f));
      this.updateView(hasFeature);
    });
  }

  private updateView(hasFeature: boolean): void {
    if (hasFeature && !this.hasView) {
      this.viewContainer.createEmbeddedView(this.templateRef);
      this.hasView = true;
    } else if (!hasFeature && this.hasView) {
      this.viewContainer.clear();
      this.hasView = false;
    }
  }
}

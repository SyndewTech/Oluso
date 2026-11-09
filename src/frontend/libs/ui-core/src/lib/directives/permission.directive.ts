import {
  Directive,
  Input,
  TemplateRef,
  ViewContainerRef,
  inject,
  effect,
  signal,
} from '@angular/core';
import { PermissionsService } from '../services/permissions.service';

/**
 * Structural directive to conditionally render content based on permissions.
 *
 * Usage:
 * ```html
 * <button *uiHasPermission="'users.create'">Create User</button>
 *
 * <!-- With any permission -->
 * <div *uiHasAnyPermission="['users.read', 'users.write']">...</div>
 *
 * <!-- With all permissions -->
 * <div *uiHasAllPermissions="['users.read', 'users.write']">...</div>
 * ```
 */
@Directive({
  selector: '[uiHasPermission]',
  standalone: true,
})
export class HasPermissionDirective {
  private templateRef = inject(TemplateRef<unknown>);
  private viewContainer = inject(ViewContainerRef);
  private permissionsService = inject(PermissionsService);
  private hasView = false;
  private permission = signal<string>('');

  constructor() {
    effect(() => {
      const perm = this.permission();
      if (!perm) return;

      // Read from permissions service to establish reactive dependency
      const hasPermission = this.permissionsService.hasPermission(perm);
      this.updateView(hasPermission);
    });
  }

  @Input() set uiHasPermission(permission: string) {
    this.permission.set(permission);
  }

  private updateView(hasPermission: boolean): void {
    if (hasPermission && !this.hasView) {
      this.viewContainer.createEmbeddedView(this.templateRef);
      this.hasView = true;
    } else if (!hasPermission && this.hasView) {
      this.viewContainer.clear();
      this.hasView = false;
    }
  }
}

@Directive({
  selector: '[uiHasAnyPermission]',
  standalone: true,
})
export class HasAnyPermissionDirective {
  private templateRef = inject(TemplateRef<unknown>);
  private viewContainer = inject(ViewContainerRef);
  private permissionsService = inject(PermissionsService);
  private hasView = false;
  private permissions = signal<string[]>([]);

  constructor() {
    effect(() => {
      const perms = this.permissions();
      if (perms.length === 0) return;

      const hasPermission = this.permissionsService.hasAnyPermission(perms);
      this.updateView(hasPermission);
    });
  }

  @Input() set uiHasAnyPermission(permissions: string[]) {
    this.permissions.set(permissions);
  }

  private updateView(hasPermission: boolean): void {
    if (hasPermission && !this.hasView) {
      this.viewContainer.createEmbeddedView(this.templateRef);
      this.hasView = true;
    } else if (!hasPermission && this.hasView) {
      this.viewContainer.clear();
      this.hasView = false;
    }
  }
}

@Directive({
  selector: '[uiHasAllPermissions]',
  standalone: true,
})
export class HasAllPermissionsDirective {
  private templateRef = inject(TemplateRef<unknown>);
  private viewContainer = inject(ViewContainerRef);
  private permissionsService = inject(PermissionsService);
  private hasView = false;
  private permissions = signal<string[]>([]);

  constructor() {
    effect(() => {
      const perms = this.permissions();
      if (perms.length === 0) return;

      const hasPermission = this.permissionsService.hasAllPermissions(perms);
      this.updateView(hasPermission);
    });
  }

  @Input() set uiHasAllPermissions(permissions: string[]) {
    this.permissions.set(permissions);
  }

  private updateView(hasPermission: boolean): void {
    if (hasPermission && !this.hasView) {
      this.viewContainer.createEmbeddedView(this.templateRef);
      this.hasView = true;
    } else if (!hasPermission && this.hasView) {
      this.viewContainer.clear();
      this.hasView = false;
    }
  }
}

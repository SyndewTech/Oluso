import { Component, ElementRef, EventEmitter, HostListener, Input, Output, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AuthStore } from '@oluso/auth';
import { IconComponent } from '@oluso/ui-core';
import { accountUrl, workspaceUrl } from '../../config';

@Component({
  selector: 'admin-user-menu',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <button
      type="button"
      (click)="toggle()"
      class="flex items-center gap-2 rounded-md px-2 py-1.5 transition-colors hover:bg-gray-100 focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1"
      [attr.aria-expanded]="isOpen()"
      aria-haspopup="menu"
    >
      @if (picture(); as picture) {
        <img
          [src]="picture"
          [alt]="displayName()"
          class="h-8 w-8 flex-shrink-0 rounded-full object-cover"
          (error)="pictureFailed.set(true)"
        />
      } @else {
        <div class="h-8 w-8 flex-shrink-0 rounded-full bg-primary-600 flex items-center justify-center">
          <span class="text-sm font-medium text-white">{{ initials() }}</span>
        </div>
      }
      <div class="text-left min-w-0 max-w-[12rem]">
        <p class="text-sm font-medium text-gray-700 truncate">{{ displayName() }}</p>
        @if (isSuperAdmin()) {
          <p class="text-xs text-primary-600 font-medium leading-tight">Super Admin</p>
        } @else if (showEmail) {
          <p class="text-xs text-gray-500 truncate leading-tight">{{ email() }}</p>
        }
      </div>
      <ui-icon name="chevron-down" class="h-4 w-4 text-gray-400 flex-shrink-0" />
    </button>

    @if (isOpen()) {
      <div
        role="menu"
        class="absolute right-0 z-50 w-56 rounded-md bg-white shadow-lg ring-1 ring-black ring-opacity-5 focus:outline-none"
        [class.bottom-full]="placement === 'top'"
        [class.mb-2]="placement === 'top'"
        [class.mt-2]="placement === 'bottom'"
      >
        <div class="px-4 py-3 border-b border-gray-100 flex items-center gap-3">
          @if (picture(); as picture) {
            <img [src]="picture" [alt]="displayName()" class="h-10 w-10 flex-shrink-0 rounded-full object-cover" />
          } @else {
            <div class="h-10 w-10 flex-shrink-0 rounded-full bg-primary-600 flex items-center justify-center">
              <span class="text-sm font-semibold text-white">{{ initials() }}</span>
            </div>
          }
          <div class="min-w-0">
            <p class="text-sm font-medium text-gray-900 truncate">{{ displayName() }}</p>
            <p class="text-xs text-gray-500 truncate">{{ email() }}</p>
          </div>
        </div>
        <div class="py-1">
          @if (accountUrl) {
            <a
              role="menuitem"
              [href]="accountProfileUrl"
              (click)="close()"
              class="flex items-center gap-2 px-4 py-2 text-sm text-gray-700 hover:bg-gray-100"
            >
              <ui-icon name="user" class="h-4 w-4 text-gray-400" />
              My account
            </a>
          }
          @if (workspaceUrl) {
            <a
              role="menuitem"
              [href]="workspaceUrl"
              (click)="close()"
              class="flex items-center gap-2 px-4 py-2 text-sm text-gray-700 hover:bg-gray-100"
            >
              <ui-icon name="arrow-left" class="h-4 w-4 text-gray-400" />
              Back to workspace
            </a>
          }
          <button
            type="button"
            role="menuitem"
            (click)="onSignOut()"
            class="flex w-full items-center gap-2 px-4 py-2 text-sm text-gray-700 hover:bg-gray-100"
          >
            <ui-icon name="logout" class="h-4 w-4 text-gray-400" />
            Sign out
          </button>
        </div>
      </div>
    }
  `,
  host: {
    class: 'relative inline-block',
  },
})
export class UserMenuComponent {
  @Input() placement: 'top' | 'bottom' = 'bottom';
  @Input() showEmail = false;
  @Output() signOut = new EventEmitter<void>();

  readonly authStore = inject(AuthStore);
  private readonly elementRef = inject(ElementRef);
  readonly accountUrl = accountUrl;
  readonly workspaceUrl = workspaceUrl;
  // Land on /profile rather than the landing page so the account app's
  // APP_INITIALIZER kicks off auto-SSO via checkAuth() and the user
  // arrives already authenticated, instead of seeing a Sign-in CTA they
  // didn't ask for.
  readonly accountProfileUrl = accountUrl ? accountUrl.replace(/\/$/, '') + '/profile' : '';

  readonly isOpen = signal(false);

  readonly user = computed(() => this.authStore.user());
  readonly displayName = computed(() => this.user()?.displayName || this.user()?.userName || '');
  readonly email = computed(() => this.user()?.email ?? '');
  readonly pictureFailed = signal(false);
  readonly picture = computed(() => (this.pictureFailed() ? null : this.user()?.picture || null));
  readonly initials = computed(() => {
    const u = this.user();
    const fromName = [u?.firstName?.[0], u?.lastName?.[0]].filter(Boolean).join('');
    if (fromName) return fromName.toUpperCase();
    const name = u?.displayName || u?.userName || u?.email || '';
    return (
      name
        .split(' ')
        .slice(0, 2)
        .map((n) => n[0])
        .join('')
        .toUpperCase() || 'U'
    );
  });
  readonly isSuperAdmin = computed(() => this.user()?.roles?.includes('SuperAdmin') ?? false);

  toggle(): void {
    this.isOpen.update((v) => !v);
  }

  close(): void {
    this.isOpen.set(false);
  }

  onSignOut(): void {
    this.close();
    this.signOut.emit();
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.elementRef.nativeElement.contains(event.target)) {
      this.close();
    }
  }
}

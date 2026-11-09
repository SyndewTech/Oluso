import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CardComponent, ButtonComponent, InputComponent, IconComponent } from '@syndew/oluso-ui-core';
import { AccountService } from '../../services/account.service';
import { AccountAuthService } from '../../services/account-auth.service';
import { UserProfile, UpdateProfileRequest } from '../../models';
import { switchMap } from 'rxjs';

@Component({
  selector: 'account-profile-page',
  standalone: true,
  imports: [CommonModule, FormsModule, CardComponent, ButtonComponent, InputComponent, IconComponent],
  template: `
    <div class="space-y-6">
      <div>
        <h1 class="text-2xl font-semibold text-gray-900">Profile</h1>
        <p class="text-gray-500 mt-1">Manage your personal information</p>
      </div>

      @if (loading()) {
        <div class="flex justify-center py-12">
          <svg
            class="animate-spin h-8 w-8 text-primary-600"
            xmlns="http://www.w3.org/2000/svg"
            fill="none"
            viewBox="0 0 24 24"
          >
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
          </svg>
        </div>
      } @else if (profile()) {
        <ui-card title="Personal Information">
          <form (ngSubmit)="saveProfile()" class="space-y-6">
            <!-- Profile Picture Section -->
            <div class="flex items-start gap-6">
              <div class="relative group">
                @if (previewUrl() || profile()?.picture) {
                  <img
                    [src]="previewUrl() || profile()?.picture"
                    alt="Profile"
                    class="h-24 w-24 rounded-full object-cover border-2 border-gray-200"
                  />
                } @else {
                  <div class="h-24 w-24 rounded-full bg-primary-100 flex items-center justify-center border-2 border-gray-200">
                    <span class="text-3xl font-medium text-primary-600">
                      {{ getInitials() }}
                    </span>
                  </div>
                }
                <!-- Upload overlay -->
                <button
                  type="button"
                  (click)="fileInput.click()"
                  class="absolute inset-0 flex items-center justify-center bg-black bg-opacity-50 rounded-full opacity-0 group-hover:opacity-100 transition-opacity cursor-pointer"
                >
                  <ui-icon name="camera" class="h-8 w-8 text-white" />
                </button>
                <!-- Uploading indicator -->
                @if (uploadingPicture()) {
                  <div class="absolute inset-0 flex items-center justify-center bg-black bg-opacity-50 rounded-full">
                    <svg class="animate-spin h-8 w-8 text-white" fill="none" viewBox="0 0 24 24">
                      <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                      <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                    </svg>
                  </div>
                }
              </div>
              <div class="flex-1">
                <p class="text-sm font-medium text-gray-700 mb-2">Profile Picture</p>
                <p class="text-xs text-gray-500 mb-3">
                  Click on the image to upload a new picture. Supported formats: JPEG, PNG, GIF, WebP. Max size: 5MB.
                </p>
                <div class="flex items-center gap-2">
                  <ui-button
                    type="button"
                    variant="secondary"
                    size="sm"
                    (clicked)="fileInput.click()"
                    [loading]="uploadingPicture()"
                  >
                    Upload Photo
                  </ui-button>
                  @if (profile()?.picture) {
                    <ui-button
                      type="button"
                      variant="danger"
                      size="sm"
                      (clicked)="removePicture()"
                    >
                      Remove
                    </ui-button>
                  }
                </div>
                @if (uploadError()) {
                  <p class="mt-2 text-sm text-red-600">{{ uploadError() }}</p>
                }
                <input
                  #fileInput
                  type="file"
                  accept="image/jpeg,image/png,image/gif,image/webp"
                  class="hidden"
                  (change)="onFileSelected($event)"
                />
              </div>
            </div>

            <hr class="border-gray-200" />

            <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
              <ui-input
                label="First Name"
                [(ngModel)]="formData.firstName"
                name="firstName"
                placeholder="John"
              ></ui-input>

              <ui-input
                label="Last Name"
                [(ngModel)]="formData.lastName"
                name="lastName"
                placeholder="Doe"
              ></ui-input>
            </div>

            <ui-input
              label="Display Name"
              [(ngModel)]="formData.displayName"
              name="displayName"
              placeholder="How you want to be addressed"
            ></ui-input>

            <div>
              <label class="block text-sm font-medium text-gray-700 mb-1">Email</label>
              <div class="flex items-center gap-2">
                <span class="text-gray-900">{{ profile()?.email }}</span>
                @if (profile()?.emailVerified) {
                  <span class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-green-100 text-green-800">
                    Verified
                  </span>
                } @else {
                  <span class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-yellow-100 text-yellow-800">
                    Not Verified
                  </span>
                }
              </div>
            </div>

            <ui-input
              label="Phone Number"
              [(ngModel)]="formData.phoneNumber"
              name="phoneNumber"
              placeholder="+1 (555) 123-4567"
            ></ui-input>

            @if (error()) {
              <div class="bg-red-50 text-red-700 p-3 rounded-md text-sm">
                {{ error() }}
              </div>
            }

            @if (success()) {
              <div class="bg-green-50 text-green-700 p-3 rounded-md text-sm">
                Profile updated successfully
              </div>
            }

            <div class="flex justify-end pt-4">
              <ui-button type="submit" [loading]="saving()">
                Save Changes
              </ui-button>
            </div>
          </form>
        </ui-card>

        <ui-card title="Account Information">
          <dl class="space-y-4">
            <div>
              <dt class="text-sm font-medium text-gray-500">User ID</dt>
              <dd class="mt-1 text-sm text-gray-900 font-mono">{{ profile()?.id }}</dd>
            </div>
            <div>
              <dt class="text-sm font-medium text-gray-500">Username</dt>
              <dd class="mt-1 text-sm text-gray-900">{{ profile()?.userName || '-' }}</dd>
            </div>
          </dl>
        </ui-card>
      }
    </div>
  `,
})
export class ProfilePageComponent implements OnInit {
  private readonly accountService = inject(AccountService);
  private readonly accountAuth = inject(AccountAuthService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly success = signal(false);
  readonly profile = signal<UserProfile | null>(null);
  readonly uploadingPicture = signal(false);
  readonly uploadError = signal<string | null>(null);
  readonly previewUrl = signal<string | null>(null);

  formData: UpdateProfileRequest = {
    firstName: '',
    lastName: '',
    displayName: '',
    phoneNumber: '',
    picture: '',
  };

  ngOnInit(): void {
    this.loadProfile();
  }

  loadProfile(): void {
    this.loading.set(true);
    this.accountService.getProfile().subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.formData = {
          firstName: profile.firstName || '',
          lastName: profile.lastName || '',
          displayName: profile.displayName || '',
          phoneNumber: profile.phoneNumber || '',
          picture: profile.picture || '',
        };
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Failed to load profile:', err);
        this.error.set('Failed to load profile');
        this.loading.set(false);
      },
    });
  }

  saveProfile(): void {
    this.saving.set(true);
    this.error.set(null);
    this.success.set(false);

    this.accountService.updateProfile(this.formData).subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.saving.set(false);
        this.success.set(true);
        void this.accountAuth.refresh();
        setTimeout(() => this.success.set(false), 3000);
      },
      error: (err) => {
        console.error('Failed to update profile:', err);
        this.error.set(err.error?.message || 'Failed to update profile');
        this.saving.set(false);
      },
    });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) return;

    // Validate file type
    const allowedTypes = ['image/jpeg', 'image/png', 'image/gif', 'image/webp'];
    if (!allowedTypes.includes(file.type)) {
      this.uploadError.set('Invalid file type. Please upload a JPEG, PNG, GIF, or WebP image.');
      return;
    }

    // Validate file size (5MB)
    const maxSize = 5 * 1024 * 1024;
    if (file.size > maxSize) {
      this.uploadError.set('File too large. Maximum size is 5MB.');
      return;
    }

    this.uploadError.set(null);

    // Show preview
    const reader = new FileReader();
    reader.onload = () => {
      this.previewUrl.set(reader.result as string);
    };
    reader.readAsDataURL(file);

    // Upload the file
    this.uploadPicture(file);

    // Reset input
    input.value = '';
  }

  uploadPicture(file: File): void {
    this.uploadingPicture.set(true);
    this.uploadError.set(null);

    // Try direct upload first, fall back to SAS token if needed
    this.accountService.uploadPicture(file).subscribe({
      next: (result) => {
        if (result.success && result.url) {
          this.profile.update((p) => (p ? { ...p, picture: result.url } : p));
          this.formData.picture = result.url || '';
          this.previewUrl.set(null);
          void this.accountAuth.refresh();
        }
        this.uploadingPicture.set(false);
      },
      error: (err) => {
        console.error('Direct upload failed, trying SAS token upload:', err);
        // Try SAS token upload as fallback
        this.uploadWithSasToken(file);
      },
    });
  }

  uploadWithSasToken(file: File): void {
    this.accountService
      .getPictureUploadUrl(file.type)
      .pipe(
        switchMap((urlResult) =>
          this.accountService.uploadPictureToBlob(urlResult.uploadUrl, file).pipe(
            switchMap(() => this.accountService.confirmPictureUpload(urlResult.fileName))
          )
        )
      )
      .subscribe({
        next: (result) => {
          if (result.success && result.url) {
            this.profile.update((p) => (p ? { ...p, picture: result.url } : p));
            this.formData.picture = result.url || '';
            this.previewUrl.set(null);
            void this.accountAuth.refresh();
          }
          this.uploadingPicture.set(false);
        },
        error: (err) => {
          console.error('SAS token upload failed:', err);
          this.uploadError.set(err.error?.error || 'Failed to upload picture');
          this.uploadingPicture.set(false);
          this.previewUrl.set(null);
        },
      });
  }

  removePicture(): void {
    this.formData.picture = '';
    this.previewUrl.set(null);
    this.profile.update((p) => (p ? { ...p, picture: undefined } : p));

    // Save immediately
    this.saving.set(true);
    this.accountService.updateProfile({ ...this.formData, picture: '' }).subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.saving.set(false);
        this.success.set(true);
        void this.accountAuth.refresh();
        setTimeout(() => this.success.set(false), 3000);
      },
      error: (err) => {
        console.error('Failed to remove picture:', err);
        this.error.set('Failed to remove picture');
        this.saving.set(false);
      },
    });
  }

  getInitials(): string {
    const profile = this.profile();
    if (!profile) return '?';

    const first = profile.firstName?.[0] || profile.displayName?.[0] || profile.email?.[0] || '?';
    const last = profile.lastName?.[0] || '';
    return (first + last).toUpperCase();
  }
}

import { Component, ElementRef, ViewChild, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import {
  CardComponent,
  ButtonComponent,
  IconComponent,
  PermissionsService,
  InputComponent,
  PromptDialogComponent,
  ToastService,
  extractErrorMessage,
} from '@oluso/ui-core';
import { JourneyService } from '../../services/journey.service';
import {
  JourneyPolicyListItem,
  POLICY_TYPE_COLORS,
  ImportPolicyResult,
  ImportPolicyOptions,
  POLICY_SLUG_MAX_LENGTH,
  POLICY_SLUG_PATTERN,
  policySlugConflict,
  slugify,
} from '../../models/journey.model';
import { JOURNEY_FILE_EXTENSION, journeyFileName, readFileText, saveBlob } from './journey-file';

@Component({
  selector: 'admin-journeys-page',
  standalone: true,
  imports: [CommonModule, FormsModule, CardComponent, ButtonComponent, IconComponent, InputComponent, PromptDialogComponent],
  templateUrl: './journeys-page.component.html',
})
export class JourneysPageComponent {
  private readonly journeyService = inject(JourneyService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly permissions = inject(PermissionsService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly policies = signal<JourneyPolicyListItem[]>([]);
  readonly searchTerm = signal(this.route.snapshot.queryParamMap.get('search') ?? '');
  readonly visiblePolicies = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    if (!term) return this.policies();
    return this.policies().filter(x => (x.name ?? '').toLowerCase().includes(term) || (x.description ?? '').toLowerCase().includes(term) || (x.type ?? '').toLowerCase().includes(term));
  });
  readonly includeGlobal = signal(true);

  readonly policyTypeColors = POLICY_TYPE_COLORS;

  readonly importing = signal(false);
  readonly importError = signal<string | null>(null);
  readonly importResult = signal<ImportPolicyResult | null>(null);
  readonly enablingImported = signal(false);
  readonly canPublish = computed(() => this.permissions.hasPermission('journeys.publish'));
  /** Enable is offered only when nothing was redacted, so a journey never goes live missing a secret. */
  readonly canEnableImported = computed(() => {
    const result = this.importResult();
    return !!result && !result.policy.enabled && result.redactedSettings.length === 0 && this.canPublish();
  });
  readonly journeyFileAccept = `${JOURNEY_FILE_EXTENSION},.json`;

  /**
   * Asks for the journey's slug: when cloning, and when an imported file's slug is
   * already in use. The slug is what clients use (?policy=, share links); blank
   * derives one from the name.
   */
  readonly slugPrompt = signal<SlugPrompt | null>(null);
  readonly slugPromptBusy = signal(false);
  readonly slugMaxLength = POLICY_SLUG_MAX_LENGTH;
  readonly validateSlug = (value: string): string | null =>
    value && !POLICY_SLUG_PATTERN.test(value)
      ? 'Use letters, digits, ".", "_" or "-", starting with a letter or digit.'
      : null;

  @ViewChild('importInput') importInput?: ElementRef<HTMLInputElement>;

  constructor() {
    this.loadPolicies();
  }

  loadPolicies(): void {
    this.loading.set(true);
    this.journeyService.getPolicies(this.includeGlobal()).subscribe({
      next: (policies) => {
        this.policies.set(policies);
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load journey policies:', error);
        this.loading.set(false);
      },
    });
  }

  onIncludeGlobalChange(event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.includeGlobal.set(checked);
    this.loadPolicies();
  }

  createJourney(): void {
    this.router.navigate(['/experience/journeys/new']);
  }

  editJourney(policy: JourneyPolicyListItem): void {
    this.router.navigate(['/experience/journeys', policy.id]);
  }

  clonePolicy(policy: JourneyPolicyListItem, event: MouseEvent): void {
    event.stopPropagation();
    this.slugPrompt.set({
      kind: 'clone',
      policy,
      title: `Clone "${policy.name}"`,
      message: 'The slug is how clients name this journey (?policy=, share links).',
      initialValue: slugify(this.cloneName(policy)),
    });
  }

  onSlugConfirmed(slug: string): void {
    const prompt = this.slugPrompt();
    if (!prompt) return;
    this.slugPromptBusy.set(true);

    if (prompt.kind === 'clone') {
      this.journeyService.clonePolicy(prompt.policy.id, slug || undefined, this.cloneName(prompt.policy)).subscribe({
        next: () => {
          this.closeSlugPrompt();
          this.loadPolicies();
        },
        error: (error) => this.onSlugRejected(prompt, error, 'Failed to clone journey'),
      });
    } else {
      this.runImport(prompt.content, slug ? { newSlug: slug } : { generateSlug: true });
    }
  }

  closeSlugPrompt(): void {
    this.slugPrompt.set(null);
    this.slugPromptBusy.set(false);
  }

  private cloneName(policy: JourneyPolicyListItem): string {
    return `${policy.name} (Custom)`;
  }

  /** A slug in use keeps the prompt open with the server's suggestion; anything else closes it. */
  private onSlugRejected(prompt: SlugPrompt, error: unknown, fallbackMessage: string): void {
    this.slugPromptBusy.set(false);
    const conflict = policySlugConflict(error);
    if (conflict) {
      this.slugPrompt.set({
        ...prompt,
        message: `${conflict.errors[0] ?? 'That slug is in use.'} Choose another, or leave blank to derive one from the name.`,
        initialValue: conflict.suggestedSlug,
      });
      return;
    }
    this.closeSlugPrompt();
    if (prompt.kind === 'import') {
      const errors: string[] | undefined = (error as { error?: { errors?: string[] } })?.error?.errors;
      this.importError.set(errors?.join(' ') || fallbackMessage);
    } else {
      this.toast.error(extractErrorMessage(error, fallbackMessage));
    }
  }

  exportPolicy(policy: JourneyPolicyListItem, event: MouseEvent): void {
    event.stopPropagation();
    this.journeyService.exportPolicy(policy.id).subscribe({
      next: (blob) => saveBlob(blob, journeyFileName(policy.name)),
      error: (error) => console.error('Failed to export policy:', error),
    });
  }

  openImport(): void {
    this.importError.set(null);
    this.importInput?.nativeElement.click();
  }

  async onImportFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;

    this.importing.set(true);
    this.importError.set(null);
    this.importResult.set(null);
    try {
      this.runImport(await readFileText(file));
    } catch (error) {
      console.error('Failed to read journey file:', error);
      this.importError.set('Could not read the selected file.');
      this.importing.set(false);
    }
  }

  /** Imports keep the file's slug; one in use opens the slug prompt with a suggestion. */
  private runImport(content: string, options: ImportPolicyOptions = {}): void {
    this.importing.set(true);
    this.journeyService.importPolicy(content, options).subscribe({
      next: (result) => {
        this.closeSlugPrompt();
        this.importResult.set(result);
        this.importing.set(false);
        this.loadPolicies();
      },
      error: (error) => {
        this.importing.set(false);
        this.onSlugRejected(
          this.slugPrompt() ?? {
            kind: 'import',
            content,
            title: 'Journey slug already in use',
            message: '',
            initialValue: '',
          },
          error,
          'Failed to import journey.',
        );
      },
    });
  }

  openImported(): void {
    const result = this.importResult();
    if (result) this.router.navigate(['/experience/journeys', result.policy.id]);
  }

  enableImported(): void {
    const result = this.importResult();
    if (!result || !this.canEnableImported()) return;
    this.enablingImported.set(true);
    this.journeyService.setStatus(result.policy.id, true).subscribe({
      next: () => {
        this.importResult.set({ ...result, policy: { ...result.policy, enabled: true } });
        this.enablingImported.set(false);
        this.loadPolicies();
      },
      error: (error) => {
        console.error('Failed to enable imported policy:', error);
        this.importError.set('Imported, but enabling failed. Use the status badge in the list.');
        this.enablingImported.set(false);
      },
    });
  }

  dismissImportResult(): void {
    this.importResult.set(null);
  }

  toggleStatus(policy: JourneyPolicyListItem, event: MouseEvent): void {
    event.stopPropagation();
    if (policy.isGlobal) return;

    this.journeyService.setStatus(policy.id, !policy.enabled).subscribe({
      next: () => {
        this.loadPolicies();
      },
      error: (error) => {
        console.error('Failed to toggle policy status:', error);
      },
    });
  }

  getPolicyTypeColor(type: string): string {
    return this.policyTypeColors[type] || 'bg-gray-100 text-gray-800';
  }
}

type SlugPrompt =
  | { kind: 'clone'; policy: JourneyPolicyListItem; title: string; message: string; initialValue: string }
  | { kind: 'import'; content: string; title: string; message: string; initialValue: string };

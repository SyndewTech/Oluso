import { Component, inject, signal, computed, OnInit, viewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { CardComponent, ButtonComponent, ModalComponent, IconComponent, CountryCatalogService, PermissionsService } from '@oluso/ui-core';
import { JourneyService } from '../../services/journey.service';
import { ConditionBuilderComponent } from '../../components/journey/condition-builder.component';
import { MappingBuilderComponent } from '../../components/journey/mapping-builder.component';
import { JourneyStepConfigFormComponent } from './journey-step-config-form.component';
import { LocaleOverrideEditorComponent } from '../../components/locale-override-editor.component';
import { journeyFileName, saveBlob } from './journey-file';
import {
  JourneyPolicy,
  JourneyStep,
  StepTypeInfo,
  StepTestResult,
  StepBranchInfo,
  StepCondition,
  ClaimMapping,
  TransformMapping,
  STEP_CATEGORY_COLORS,
  STEP_CATEGORY_ORDER,
  DEFAULT_STEPS,
  DEFAULT_SESSION_CONFIGURATION,
  DEFAULT_UI_CONFIGURATION,
  FIELD_TYPES,
  FILE_FIELD_KINDS,
  DEFAULT_FILE_FIELD_KINDS,
  HTTP_METHODS,
  AUTH_TYPES,
  POLICY_SLUG_MAX_LENGTH,
  POLICY_SLUG_PATTERN,
  policySlugConflict,
  slugify,
  TransformCatalogEntry,
  JourneyOwnerModule,
} from '../../models/journey.model';

@Component({
  selector: 'admin-journey-builder-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    CardComponent,
    ButtonComponent,
    ModalComponent,
    IconComponent,
    ConditionBuilderComponent,
    MappingBuilderComponent,
    JourneyStepConfigFormComponent,
    LocaleOverrideEditorComponent,
  ],
  templateUrl: './journey-builder-page.component.html',
})
export class JourneyBuilderPageComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly journeyService = inject(JourneyService);
  private readonly permissions = inject(PermissionsService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly validating = signal(false);
  readonly togglingStatus = signal(false);
  readonly canPublish = computed(() => this.permissions.hasPermission('journeys.publish'));
  readonly isDirty = signal(false);
  readonly slugMaxLength = POLICY_SLUG_MAX_LENGTH;
  readonly slugError = signal<string | null>(null);
  /** A new journey's slug follows its name until the admin edits the slug. */
  private slugEdited = false;

  readonly policy = signal<Partial<JourneyPolicy>>({
    name: '',
    description: '',
    type: 'SignIn',
    enabled: true,
    priority: 100,
    steps: [],
    conditions: [],
    session: { ...DEFAULT_SESSION_CONFIGURATION },
    ui: { ...DEFAULT_UI_CONFIGURATION },
    requiresAuthentication: true,
    persistSubmissions: false,
    allowDuplicates: true,
    maxSubmissions: 0,
    requireApiKey: false,
  });

  readonly stepTypes = signal<StepTypeInfo[]>([]);
  readonly uiLocales = signal<string[]>([]);
  readonly fieldLocale = signal<string>('en');
  readonly selectedStepIndex = signal<number | null>(null);
  readonly validationErrors = signal<string[]>([]);
  readonly validationWarnings = signal<string[]>([]);
  /** True once the admin has clicked Validate at least once. Lets the UI
   * distinguish "haven't validated yet" from "validated and got back zero
   * issues" — the former should stay quiet, the latter should show a
   * green confirmation banner. */
  readonly validationRan = signal(false);
  readonly expandedFormField = signal<number | null>(null);
  readonly showStepModal = signal(false);

  readonly policyId = computed(() => this.route.snapshot.paramMap.get('policyId') || '');
  readonly isNew = computed(() => this.policyId() === 'new');

  readonly stepCategoryColors = STEP_CATEGORY_COLORS;
  readonly defaultSteps = DEFAULT_STEPS;
  readonly fieldTypes = FIELD_TYPES;
  readonly fileFieldKinds = FILE_FIELD_KINDS;
  readonly phoneFormats = [
    { value: 'international', label: 'International (country picker, stored as E.164)' },
    { value: 'local', label: 'Local numbers only' },
  ];
  private readonly countryCatalog = inject(CountryCatalogService);
  /** Platform country list for the phone field's default-country picker. */
  get countryOptions() { return this.countryCatalog.countries(); }
  readonly httpMethods = HTTP_METHODS;
  readonly authTypes = AUTH_TYPES;

  readonly groupedStepTypes = computed(() => {
    const types = this.stepTypes();
    return types.reduce(
      (acc, type) => {
        if (!acc[type.category]) acc[type.category] = [];
        acc[type.category].push(type);
        return acc;
      },
      {} as Record<string, StepTypeInfo[]>
    );
  });

  readonly stepSearch = signal('');
  readonly stepCategoryFilter = signal<string | null>(null);
  private readonly stepSearchInput = viewChild<ElementRef<HTMLInputElement>>('stepSearchInput');

  /** Categories in a stable order, with how many step types each has. */
  readonly stepCategories = computed(() => {
    const counts = new Map<string, number>();
    for (const t of this.stepTypes()) counts.set(t.category, (counts.get(t.category) ?? 0) + 1);
    const rank = (c: string) => {
      const i = STEP_CATEGORY_ORDER.indexOf(c);
      return i < 0 ? STEP_CATEGORY_ORDER.length : i;
    };
    return [...counts.entries()]
      .map(([name, count]) => ({ name, count }))
      .sort((a, b) => rank(a.name) - rank(b.name) || a.name.localeCompare(b.name));
  });

  /** Step types matching the picker's search and category, grouped by category. */
  readonly filteredStepGroups = computed(() => {
    const terms = this.stepSearch().trim().toLowerCase().split(/\s+/).filter(Boolean);
    const only = this.stepCategoryFilter();
    const matches = (t: StepTypeInfo) => {
      const text = `${t.displayName} ${t.description} ${t.type} ${t.category} ${t.module ?? ''}`.toLowerCase();
      return terms.every((term) => text.includes(term));
    };
    return this.stepCategories()
      .filter((c) => !only || c.name === only)
      .map((c) => ({
        category: c.name,
        types: this.stepTypes()
          .filter((t) => t.category === c.name && matches(t))
          .sort((a, b) => (a.displayName || a.type).localeCompare(b.displayName || b.type)),
      }))
      .filter((g) => g.types.length > 0);
  });

  readonly filteredStepCount = computed(() => this.filteredStepGroups().reduce((n, g) => n + g.types.length, 0));

  /** How many times each step type is already in this journey. */
  readonly stepTypeUsage = computed(() => {
    const usage: Record<string, number> = {};
    for (const s of this.policy().steps ?? []) usage[s.type] = (usage[s.type] ?? 0) + 1;
    return usage;
  });

  readonly selectedStep = computed(() => {
    const index = this.selectedStepIndex();
    const steps = this.policy().steps;
    if (index === null || !steps || index >= steps.length) return null;
    return steps[index];
  });

  /** Modules the admin may make this journey's owner. */
  readonly ownerModules = signal<JourneyOwnerModule[]>([]);

  /** The owner select's choices: platform, the modules the admin administers, and the current owner even when it is not one of them. */
  readonly ownerOptions = computed(() => {
    const current = this.policy().ownerModule;
    const modules = this.ownerModules();
    const options = modules.map((m) => ({ key: m.key, name: m.name }));
    if (current && !modules.some((m) => m.key === current)) {
      options.push({ key: current, name: this.policy().ownerModuleName || current });
    }
    return options;
  });

  /** A module's journey can only be re-assigned by an admin of that module. */
  readonly canChangeOwner = computed(() => {
    const current = this.policy().ownerModule;
    return !current || this.ownerModules().some((m) => m.key === current);
  });

  readonly isDataCollectionType = computed(() => {
    const type = this.policy().type;
    return ['Waitlist', 'ContactForm', 'Survey', 'Feedback', 'DataCollection'].includes(type || '');
  });

  ngOnInit(): void {
    this.loadStepTypes();
    this.journeyService.getOwnerModules().subscribe({
      next: (modules) => this.ownerModules.set(modules),
      error: () => this.ownerModules.set([]),
    });
    this.loadUiLocales();
    if (!this.isNew()) {
      this.loadPolicy();
    } else {
      this.loading.set(false);
    }
  }

  loadStepTypes(): void {
    this.journeyService.getStepTypes().subscribe({
      next: (types) => this.stepTypes.set(types),
      error: (error) => console.error('Failed to load step types:', error),
    });
  }

  loadUiLocales(): void {
    this.journeyService.getUiLocales().subscribe({
      next: (locales) => this.uiLocales.set(locales),
      error: () => this.uiLocales.set(['en']),
    });
  }

  loadPolicy(): void {
    this.loading.set(true);
    this.journeyService.getPolicy(this.policyId()).subscribe({
      next: (policy) => {
        this.policy.set(policy);
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load policy:', error);
        this.loading.set(false);
      },
    });
  }

  goBack(): void {
    this.router.navigate(['/experience/journeys']);
  }

  toggleEnabled(): void {
    if (this.isNew() || this.togglingStatus()) return;
    const next = !this.policy().enabled;
    this.togglingStatus.set(true);
    this.journeyService.setStatus(this.policyId(), next).subscribe({
      next: (result) => {
        this.policy.update((p) => ({ ...p, enabled: result.enabled }));
        this.togglingStatus.set(false);
      },
      error: (error) => {
        console.error('Failed to change journey status:', error);
        this.togglingStatus.set(false);
      },
    });
  }

  exportPolicy(): void {
    if (this.isNew()) return;
    const name = this.policy().name || this.policyId();
    this.journeyService.exportPolicy(this.policyId()).subscribe({
      next: (blob) => saveBlob(blob, journeyFileName(name)),
      error: (error) => console.error('Failed to export policy:', error),
    });
  }

  updatePolicy(updates: Partial<JourneyPolicy>): void {
    this.policy.set({ ...this.policy(), ...updates });
    this.isDirty.set(true);
  }

  onNameChange(name: string): void {
    this.updatePolicy(this.isNew() && !this.slugEdited ? { name, slug: slugify(name) } : { name });
  }

  onSlugChange(slug: string): void {
    this.slugEdited = true;
    this.slugError.set(null);
    this.updatePolicy({ slug: slug.trim() });
  }

  /** A slug in use (409) shows the server's free suggestion; anything else is logged. */
  private onSaveError(error: unknown, action: string): void {
    this.saving.set(false);
    const conflict = policySlugConflict(error);
    if (conflict) {
      this.slugError.set(`${conflict.errors[0] ?? 'That slug is in use.'} Try "${conflict.suggestedSlug}".`);
      return;
    }
    console.error(`Failed to ${action} policy:`, error);
  }

  // Step Management
  openStepModal(): void {
    this.stepSearch.set('');
    this.stepCategoryFilter.set(null);
    this.showStepModal.set(true);
    setTimeout(() => this.stepSearchInput()?.nativeElement.focus());
  }

  closeStepModal(): void {
    this.showStepModal.set(false);
  }

  addFirstMatchingStep(): void {
    const first = this.filteredStepGroups()[0]?.types.find((t) => t.isAvailable);
    if (first) this.addStep(first);
  }

  toggleStepCategory(category: string): void {
    this.stepCategoryFilter.update((current) => (current === category ? null : category));
  }

  stepIcon(type: StepTypeInfo | undefined, category: string): string {
    return type?.icon || this.getCategoryIcon(category);
  }

  /** "Oluso.Enterprise.Ldap" → "Ldap"; feature keys pass through. */
  moduleLabel(module: string): string {
    return module.split('.').pop() ?? module;
  }

  addStep(stepType: StepTypeInfo): void {
    const currentPolicy = this.policy();
    const defaultConfig = this.defaultSteps[stepType.type];

    const newStep: JourneyStep = {
      id: `step_${Date.now()}`,
      type: stepType.type,
      displayName: defaultConfig?.displayName || stepType.displayName,
      configuration: defaultConfig?.configuration || {},
    };

    this.policy.set({
      ...currentPolicy,
      steps: [...(currentPolicy.steps || []), newStep],
    });
    this.isDirty.set(true);
    this.selectedStepIndex.set((currentPolicy.steps?.length || 0));
    this.showStepModal.set(false);
  }

  updateStep(index: number, updates: Partial<JourneyStep>): void {
    const currentPolicy = this.policy();
    const steps = currentPolicy.steps?.map((s, i) => (i === index ? { ...s, ...updates } : s));
    this.policy.set({ ...currentPolicy, steps });
    this.isDirty.set(true);
  }

  removeStep(index: number): void {
    const currentPolicy = this.policy();
    const steps = currentPolicy.steps?.filter((_, i) => i !== index);
    this.policy.set({ ...currentPolicy, steps });
    this.isDirty.set(true);
    this.selectedStepIndex.set(null);
  }

  moveStep(index: number, direction: 'up' | 'down'): void {
    const currentPolicy = this.policy();
    const steps = [...(currentPolicy.steps || [])];
    const newIndex = direction === 'up' ? index - 1 : index + 1;

    if (newIndex < 0 || newIndex >= steps.length) return;

    [steps[index], steps[newIndex]] = [steps[newIndex], steps[index]];
    this.policy.set({ ...currentPolicy, steps });
    this.isDirty.set(true);
    this.selectedStepIndex.set(newIndex);
  }

  selectStep(index: number): void {
    this.selectedStepIndex.set(index);
    this.stepTest.set(null);
  }

  /** The builder's step Test: the step checks its current settings (stored secrets fill redacted ones). */
  readonly stepTest = signal<{ stepId: string; running: boolean; result?: StepTestResult } | null>(null);

  testStep(step: JourneyStep): void {
    this.stepTest.set({ stepId: step.id, running: true });
    this.journeyService
      .testStep({
        type: step.type,
        configuration: step.configuration,
        policyId: this.isNew() ? undefined : this.policyId(),
        stepId: step.id,
      })
      .subscribe({
        next: (result) => this.stepTest.set({ stepId: step.id, running: false, result }),
        error: (err) =>
          this.stepTest.set({
            stepId: step.id,
            running: false,
            result: { success: false, message: err?.error?.error ?? err?.message ?? 'The test could not run' },
          }),
      });
  }

  // Validation
  validate(): void {
    this.validating.set(true);
    const steps = this.policy().steps as JourneyStep[];
    this.journeyService.validatePolicy(steps).subscribe({
      next: (result) => {
        this.validationErrors.set(result.errors);
        this.validationWarnings.set(result.warnings ?? []);
        this.validationRan.set(true);
        this.validating.set(false);
      },
      error: (error) => {
        console.error('Failed to validate policy:', error);
        this.validating.set(false);
      },
    });
  }

  // Save
  save(): void {
    const currentPolicy = this.policy();
    const { name, type } = currentPolicy;
    if (!name || !type) return;
    const slug = currentPolicy.slug?.trim();
    if (slug && !POLICY_SLUG_PATTERN.test(slug)) {
      this.slugError.set('Use letters, digits, ".", "_" or "-", starting with a letter or digit.');
      return;
    }
    this.slugError.set(null);
    this.saving.set(true);

    if (this.isNew()) {
      this.journeyService
        .createPolicy({
          name,
          slug: slug || undefined,
          description: currentPolicy.description,
          type,
          enabled: currentPolicy.enabled,
          priority: currentPolicy.priority,
          ownerModule: currentPolicy.ownerModule || null,
          sharing: currentPolicy.sharing ?? 'Private',
          steps: currentPolicy.steps as JourneyStep[],
          conditions: currentPolicy.conditions,
          session: currentPolicy.session,
          ui: currentPolicy.ui,
          persistSubmissions: currentPolicy.persistSubmissions,
          requiresAuthentication: currentPolicy.requiresAuthentication,
          allowDuplicates: currentPolicy.allowDuplicates,
          duplicateCheckFields: currentPolicy.duplicateCheckFields,
          maxSubmissions: currentPolicy.maxSubmissions,
          submissionCollection: currentPolicy.submissionCollection,
          successRedirectUrl: currentPolicy.successRedirectUrl,
          successMessage: currentPolicy.successMessage,
          allowedOrigins: currentPolicy.allowedOrigins,
          apiKey: currentPolicy.apiKey,
          requireApiKey: currentPolicy.requireApiKey,
        })
        .subscribe({
          next: () => {
            this.saving.set(false);
            this.isDirty.set(false);
            this.router.navigate(['/experience/journeys']);
          },
          error: (error) => this.onSaveError(error, 'create'),
        });
    } else {
      this.journeyService
        .updatePolicy(this.policyId(), {
          name: currentPolicy.name,
          slug: slug || undefined,
          description: currentPolicy.description,
          enabled: currentPolicy.enabled,
          priority: currentPolicy.priority,
          ownerModule: currentPolicy.ownerModule ?? '',
          sharing: currentPolicy.sharing ?? 'Private',
          steps: currentPolicy.steps as JourneyStep[],
          conditions: currentPolicy.conditions,
          session: currentPolicy.session,
          ui: currentPolicy.ui,
          persistSubmissions: currentPolicy.persistSubmissions,
          requiresAuthentication: currentPolicy.requiresAuthentication,
          allowDuplicates: currentPolicy.allowDuplicates,
          duplicateCheckFields: currentPolicy.duplicateCheckFields,
          maxSubmissions: currentPolicy.maxSubmissions,
          submissionCollection: currentPolicy.submissionCollection,
          successRedirectUrl: currentPolicy.successRedirectUrl,
          successMessage: currentPolicy.successMessage,
          allowedOrigins: currentPolicy.allowedOrigins,
          apiKey: currentPolicy.apiKey,
          requireApiKey: currentPolicy.requireApiKey,
        })
        .subscribe({
          next: () => {
            this.saving.set(false);
            this.isDirty.set(false);
          },
          error: (error) => this.onSaveError(error, 'update'),
        });
    }
  }

  // Dynamic Form Field Management
  addFormField(): void {
    const step = this.selectedStep();
    if (!step) return;

    const fields = (step.configuration?.['fields'] as unknown[]) || [];
    const newField = {
      name: `field_${Date.now()}`,
      type: 'text',
      label: 'New Field',
      required: false,
    };

    this.updateStepConfig({ fields: [...fields, newField] });
    this.expandedFormField.set(fields.length);
  }

  updatePolicyLocalized(key: string, value: string): void {
    const locale = this.fieldLocale();
    const existing = (this.policy() as Record<string, unknown>)[key];

    let localeMap: Record<string, string>;
    if (existing && typeof existing === 'object' && !Array.isArray(existing)) {
      localeMap = { ...(existing as Record<string, string>) };
    } else if (typeof existing === 'string' && existing) {
      localeMap = { en: existing };
    } else {
      localeMap = {};
    }

    if (value) { localeMap[locale] = value; } else { delete localeMap[locale]; }
    this.updatePolicy({ [key]: Object.keys(localeMap).length > 0 ? localeMap : undefined });
  }

  updateStepConfigLocalized(key: string, value: string): void {
    const step = this.selectedStep();
    if (!step) return;
    const config = { ...(step.configuration || {}) } as Record<string, unknown>;
    const locale = this.fieldLocale();
    const existing = config[key];

    let localeMap: Record<string, string>;
    if (existing && typeof existing === 'object' && !Array.isArray(existing)) {
      localeMap = { ...(existing as Record<string, string>) };
    } else if (typeof existing === 'string' && existing) {
      localeMap = { en: existing };
    } else {
      localeMap = {};
    }

    if (value) { localeMap[locale] = value; } else { delete localeMap[locale]; }
    config[key] = Object.keys(localeMap).length > 0 ? localeMap : undefined;
    this.updateStepConfig(config);
  }

  getFieldLocalized(field: Record<string, unknown>, key: string): string {
    const v = field[key];
    const locale = this.fieldLocale();
    if (v === undefined || v === null) return '';
    if (typeof v === 'string') return locale === 'en' ? v : '';
    if (v && typeof v === 'object' && !Array.isArray(v)) {
      const val = (v as Record<string, unknown>)[locale];
      return typeof val === 'string' ? val : '';
    }
    return '';
  }

  getFieldLocalizedPlaceholder(field: Record<string, unknown>, key: string, fallback: string): string {
    if (this.fieldLocale() === 'en') return fallback;
    const v = field[key];
    if (typeof v === 'string') return v;
    if (v && typeof v === 'object' && !Array.isArray(v)) {
      const en = (v as Record<string, unknown>)['en'];
      return typeof en === 'string' ? en : fallback;
    }
    return fallback;
  }

  updateFormFieldLocalized(fieldIndex: number, key: string, value: string): void {
    const step = this.selectedStep();
    if (!step) return;
    const fields = [...((step.configuration?.['fields'] as unknown[]) || [])];
    const field = { ...(fields[fieldIndex] as Record<string, unknown>) };
    const locale = this.fieldLocale();
    const existing = field[key];

    let localeMap: Record<string, string>;
    if (existing && typeof existing === 'object' && !Array.isArray(existing)) {
      localeMap = { ...(existing as Record<string, string>) };
    } else if (typeof existing === 'string' && existing) {
      localeMap = { en: existing };
    } else {
      localeMap = {};
    }

    if (value) { localeMap[locale] = value; } else { delete localeMap[locale]; }
    field[key] = Object.keys(localeMap).length > 0 ? localeMap : undefined;
    fields[fieldIndex] = field;
    this.updateStepConfig({ fields });
  }

  /** The kinds a file field accepts, defaulting to PDF, JPEG and PNG like the server. */
  fileKinds(field: { fileTypes?: string[] | null }): string[] {
    return field.fileTypes?.length ? field.fileTypes : DEFAULT_FILE_FIELD_KINDS;
  }

  toggleFileKind(fieldIndex: number, field: { fileTypes?: string[] | null }, kind: string, checked: boolean): void {
    const current = this.fileKinds(field);
    const next = checked ? [...new Set([...current, kind])] : current.filter((k) => k !== kind);
    this.updateFormField(fieldIndex, { fileTypes: next.length ? next : DEFAULT_FILE_FIELD_KINDS });
  }

  updateFormField(fieldIndex: number, updates: Record<string, unknown>): void {
    const step = this.selectedStep();
    if (!step) return;

    const fields = [...((step.configuration?.['fields'] as unknown[]) || [])];
    fields[fieldIndex] = { ...(fields[fieldIndex] as Record<string, unknown>), ...updates };
    this.updateStepConfig({ fields });
  }

  removeFormField(fieldIndex: number): void {
    const step = this.selectedStep();
    if (!step) return;

    const fields = ((step.configuration?.['fields'] as unknown[]) || []).filter(
      (_, i) => i !== fieldIndex
    );
    this.updateStepConfig({ fields });
    this.expandedFormField.set(null);
  }

  moveFormField(fieldIndex: number, direction: 'up' | 'down'): void {
    const step = this.selectedStep();
    if (!step) return;

    const fields = [...((step.configuration?.['fields'] as unknown[]) || [])];
    const newIndex = direction === 'up' ? fieldIndex - 1 : fieldIndex + 1;

    if (newIndex < 0 || newIndex >= fields.length) return;

    [fields[fieldIndex], fields[newIndex]] = [fields[newIndex], fields[fieldIndex]];
    this.updateStepConfig({ fields });
    this.expandedFormField.set(newIndex);
  }

  addFieldOption(fieldIndex: number): void {
    const step = this.selectedStep();
    if (!step) return;

    const fields = [...((step.configuration?.['fields'] as Record<string, unknown>[]) || [])];
    const field = fields[fieldIndex];
    const options = [...((field['options'] as string[]) || []), ''];
    fields[fieldIndex] = { ...field, options };
    this.updateStepConfig({ fields });
  }

  updateFieldOption(fieldIndex: number, optionIndex: number, value: string): void {
    const step = this.selectedStep();
    if (!step) return;

    const fields = [...((step.configuration?.['fields'] as Record<string, unknown>[]) || [])];
    const field = fields[fieldIndex];
    const options = [...((field['options'] as string[]) || [])];
    options[optionIndex] = value;
    fields[fieldIndex] = { ...field, options };
    this.updateStepConfig({ fields });
  }

  removeFieldOption(fieldIndex: number, optionIndex: number): void {
    const step = this.selectedStep();
    if (!step) return;

    const fields = [...((step.configuration?.['fields'] as Record<string, unknown>[]) || [])];
    const field = fields[fieldIndex];
    const options = ((field['options'] as string[]) || []).filter((_, i) => i !== optionIndex);
    fields[fieldIndex] = { ...field, options };
    this.updateStepConfig({ fields });
  }

  addHeader(): void {
    const step = this.selectedStep();
    if (!step) return;

    const headers = { ...((step.configuration?.['headers'] as Record<string, string>) || {}) };
    headers[`header_${Date.now()}`] = '';
    this.updateStepConfig({ headers });
  }

  updateHeader(oldKey: string, newKey: string, value: string): void {
    const step = this.selectedStep();
    if (!step) return;

    const headers = { ...((step.configuration?.['headers'] as Record<string, string>) || {}) };
    if (oldKey !== newKey) {
      delete headers[oldKey];
    }
    headers[newKey] = value;
    this.updateStepConfig({ headers });
  }

  removeHeader(key: string): void {
    const step = this.selectedStep();
    if (!step) return;

    const headers = { ...((step.configuration?.['headers'] as Record<string, string>) || {}) };
    delete headers[key];
    this.updateStepConfig({ headers });
  }

  updateAuth(key: string, value: unknown): void {
    const step = this.selectedStep();
    if (!step) return;

    const authentication = {
      ...((step.configuration?.['authentication'] as Record<string, unknown>) || {}),
      [key]: value,
    };
    this.updateStepConfig({ authentication });
  }

  addOutputMapping(): void {
    const step = this.selectedStep();
    if (!step) return;

    const outputMapping = {
      ...((step.configuration?.['outputMapping'] as Record<string, string>) || {}),
    };
    outputMapping[`path_${Date.now()}`] = '';
    this.updateStepConfig({ outputMapping });
  }

  updateOutputMapping(oldPath: string, newPath: string, claimType: string): void {
    const step = this.selectedStep();
    if (!step) return;

    const outputMapping = {
      ...((step.configuration?.['outputMapping'] as Record<string, string>) || {}),
    };
    if (oldPath !== newPath) {
      delete outputMapping[oldPath];
    }
    outputMapping[newPath] = claimType;
    this.updateStepConfig({ outputMapping });
  }

  removeOutputMapping(path: string): void {
    const step = this.selectedStep();
    if (!step) return;

    const outputMapping = {
      ...((step.configuration?.['outputMapping'] as Record<string, string>) || {}),
    };
    delete outputMapping[path];
    this.updateStepConfig({ outputMapping });
  }

  // Branch Management
  addBranch(): void {
    const step = this.selectedStep();
    if (!step) return;
    const index = this.selectedStepIndex();
    if (index === null) return;

    // Default to the first unused known branch when the step type declares
    // them; otherwise fall back to a placeholder key for free-text entry.
    const firstUnused = this.unusedBranchOptions('')[0]?.id;
    const key = firstUnused ?? `branch_${Date.now()}`;
    const branches = { ...(step.branches || {}), [key]: '' };
    this.updateStep(index, { branches });
  }

  updateBranch(oldName: string, newName: string, targetStepId: string): void {
    const step = this.selectedStep();
    if (!step) return;
    const index = this.selectedStepIndex();
    if (index === null) return;

    const branches = { ...(step.branches || {}) };
    if (oldName !== newName) {
      delete branches[oldName];
    }
    branches[newName] = targetStepId;
    this.updateStep(index, { branches });
  }

  removeBranch(name: string): void {
    const step = this.selectedStep();
    if (!step) return;
    const index = this.selectedStepIndex();
    if (index === null) return;

    const branches = { ...(step.branches || {}) };
    delete branches[name];
    this.updateStep(index, { branches });
  }

  // Helper methods
  private updateStepConfig(configUpdates: Record<string, unknown>): void {
    const index = this.selectedStepIndex();
    if (index === null) return;

    const step = this.selectedStep();
    if (!step) return;

    this.updateStep(index, {
      configuration: { ...step.configuration, ...configUpdates },
    });
  }

  getStepType(step: JourneyStep): StepTypeInfo | undefined {
    return this.stepTypes().find((t) => t.type === step.type);
  }

  /** A transform step's mapping fields, as its schema publishes them. */
  private transformMappingFields(step: JourneyStep): Record<string, any> | undefined {
    const mappings = this.getStepType(step)?.configurationSchema?.['mappings'] as Record<string, any> | undefined;
    return mappings?.['items']?.['properties'];
  }

  transformCatalog(step: JourneyStep): TransformCatalogEntry[] {
    return this.transformMappingFields(step)?.['transform']?.['x-transforms'] ?? [];
  }

  transformSourceTypes(step: JourneyStep): string[] {
    return this.transformMappingFields(step)?.['sourceType']?.['enum'] ?? [];
  }

  getStepCategoryColor(category: string): { bg: string; border: string; text: string } {
    return this.stepCategoryColors[category] || this.stepCategoryColors['Custom'];
  }

  getStepButtonClasses(category: string): string {
    const classes: Record<string, string> = {
      Authentication: 'bg-blue-50 border-blue-200 text-blue-800 hover:bg-blue-100 hover:border-blue-300 hover:shadow-sm',
      'User Interaction': 'bg-purple-50 border-purple-200 text-purple-800 hover:bg-purple-100 hover:border-purple-300 hover:shadow-sm',
      'User Management': 'bg-green-50 border-green-200 text-green-800 hover:bg-green-100 hover:border-green-300 hover:shadow-sm',
      'Flow Control': 'bg-amber-50 border-amber-200 text-amber-800 hover:bg-amber-100 hover:border-amber-300 hover:shadow-sm',
      Logic: 'bg-amber-50 border-amber-200 text-amber-800 hover:bg-amber-100 hover:border-amber-300 hover:shadow-sm',
      'Account Management': 'bg-indigo-50 border-indigo-200 text-indigo-800 hover:bg-indigo-100 hover:border-indigo-300 hover:shadow-sm',
      Billing: 'bg-emerald-50 border-emerald-200 text-emerald-800 hover:bg-emerald-100 hover:border-emerald-300 hover:shadow-sm',
      Plugins: 'bg-pink-50 border-pink-200 text-pink-800 hover:bg-pink-100 hover:border-pink-300 hover:shadow-sm',
      Custom: 'bg-gray-50 border-gray-200 text-gray-800 hover:bg-gray-100 hover:border-gray-300 hover:shadow-sm',
    };
    return classes[category] || classes['Custom'];
  }

  getCategoryIcon(category: string): string {
    const icons: Record<string, string> = {
      Authentication: 'lock',
      'User Interaction': 'user',
      'User Management': 'users',
      'Flow Control': 'bolt',
      Logic: 'bolt',
      'Account Management': 'cog',
      Billing: 'credit-card',
      Plugins: 'puzzle',
      Custom: 'wrench',
    };
    return icons[category] || 'wrench';
  }

  isDynamicFormStep(step: JourneyStep): boolean {
    return step.type === 'DynamicForm' || step.type === 'dynamic_form';
  }

  isApiCallStep(step: JourneyStep): boolean {
    return step.type === 'ApiCall' || step.type === 'api_call';
  }

  /**
   * True when the generic schema-driven config form should render for
   * this step. The form fills the gap for step types that have a
   * <c>configurationSchema</c> declared on the backend but no bespoke
   * form block in the journey-builder template — i.e. everything except
   * DynamicForm / Condition / Transform, which still own their hand-
   * rolled UIs. ApiCall used to be bespoke; the api_call schema's
   * x-sections annotation now drives a section-grouped form via the
   * generic renderer.
   */
  shouldUseGenericConfigForm(step: JourneyStep): boolean {
    if (this.isDynamicFormStep(step)) return false;
    if (this.isConditionStep(step)) return false;
    if (this.isTransformStep(step)) return false;
    const stepType = this.getStepType(step);
    return !!(stepType?.configurationSchema && Object.keys(stepType.configurationSchema).length > 0);
  }

  /**
   * Resolves "$steps" pseudo-endpoint references in the schema to the
   * journey's actual local step list. Lets the schema declare a step
   * picker dropdown without round-tripping through the API — the
   * admin sees only steps they've already added to this journey.
   *
   * Schema shape:
   *   x-enumSource: { endpoint: "$steps", filterByType?: "passwordless_email" }
   *
   * After resolution, the field becomes a plain `enum` so the renderer
   * shows it as a normal dropdown of step IDs.
   */
  resolveSchemaForStep(step: JourneyStep): Record<string, unknown> | null {
    const schema = this.getStepType(step)?.configurationSchema;
    if (!schema) return null;
    const steps = this.policy().steps ?? [];
    return this.resolveLocalEnumSources(schema as Record<string, unknown>, steps, step.id);
  }

  /** The condition step's settings other than its conditions (which have their own editor). */
  conditionTargetsSchema(step: JourneyStep): Record<string, unknown> | null {
    const schema = this.resolveSchemaForStep(step);
    if (!schema) return null;
    const { conditions: _conditions, ...targets } = schema;
    return targets;
  }

  private isObject(v: unknown): v is Record<string, unknown> {
    return !!v && typeof v === 'object' && !Array.isArray(v);
  }

  private resolveLocalEnumSources(
    schema: Record<string, unknown>,
    steps: JourneyStep[],
    currentStepId: string | undefined,
  ): Record<string, unknown> {
    const out: Record<string, unknown> = {};
    for (const [key, raw] of Object.entries(schema)) {
      if (raw && typeof raw === 'object' && !Array.isArray(raw)) {
        const field = raw as Record<string, unknown>;

        // Recurse into nested shapes BEFORE the $steps short-circuit so a
        // marker buried in branch.branches[].targetStepId or
        // condition.onTrue gets resolved too. Without this, the resolver
        // only flattened top-level fields and the array-of-object editor
        // rendered a free-text input where the picker was meant to appear.
        if (field['type'] === 'array' && this.isObject(field['items'])) {
          const items = field['items'] as Record<string, unknown>;
          if (this.isObject(items['properties'])) {
            const resolvedProps = this.resolveLocalEnumSources(
              items['properties'] as Record<string, unknown>,
              steps,
              currentStepId,
            );
            field['items'] = { ...items, properties: resolvedProps };
          }
        } else if (field['type'] === 'object' && this.isObject(field['properties'])) {
          field['properties'] = this.resolveLocalEnumSources(
            field['properties'] as Record<string, unknown>,
            steps,
            currentStepId,
          );
        }

        const src = field['x-enumSource'];
        if (
          src &&
          typeof src === 'object' &&
          (src as Record<string, unknown>)['endpoint'] === '$steps'
        ) {
          const srcObj = src as Record<string, unknown>;
          const filter = srcObj['filterByType'];
          const filterStr = typeof filter === 'string' ? filter : null;
          const candidates = filterStr
            ? steps.filter(
                (s) => (s.type ?? '').toLowerCase() === filterStr.toLowerCase(),
              )
            : steps;
          // Don't let a step pick itself as the branch target —
          // admin would create an immediate loop.
          const visible = candidates.filter(
            (s): s is JourneyStep => !!s.id && s.id !== currentStepId,
          );
          const enumValues = visible.map((s) => s.id);
          // Pretty labels: prefer the step's displayName, fall back to
          // ID. Show the ID parenthesised when both are present so the
          // admin can disambiguate two steps with the same display name.
          const enumLabels: Record<string, string> = {};
          for (const s of visible) {
            enumLabels[s.id] =
              s.displayName && s.displayName !== s.id
                ? `${s.displayName} (${s.id})`
                : s.id;
          }
          const cleaned: Record<string, unknown> = { ...field };
          delete cleaned['x-enumSource'];
          cleaned['enum'] = enumValues;
          cleaned['type'] = 'string';
          cleaned['x-enumLabels'] = enumLabels;
          // Inform the renderer what step type to suggest creating
          // when the dropdown is empty. Without this, the empty-state
          // hint doesn't render.
          if (filterStr) {
            cleaned['x-emptyAction'] = {
              type: 'addStep',
              stepType: filterStr,
            };
          }
          out[key] = cleaned;
          continue;
        }
      }
      out[key] = raw;
    }
    return out;
  }

  /**
   * Handler wired to (addStep) output of the schema renderer. Looks up
   * the step type in the registered list and delegates to addStep().
   * Called when the admin clicks the empty-state link in a step picker.
   */
  onConfigFormAddStep(stepType: string): void {
    const typeInfo = this.stepTypes().find(
      (t) => (t.type ?? '').toLowerCase() === stepType.toLowerCase(),
    );
    if (!typeInfo) {
      console.warn(
        `Cannot add step of type "${stepType}" — step type not registered.`,
      );
      return;
    }
    this.addStep(typeInfo);
  }

  isConditionStep(step: JourneyStep): boolean {
    return step.type === 'Condition' || step.type === 'condition';
  }

  isTransformStep(step: JourneyStep): boolean {
    return step.type === 'Transform' || step.type === 'transform';
  }

  onConditionsChange(conditions: StepCondition[]): void {
    const index = this.selectedStepIndex();
    if (index === null) return;
    this.updateStep(index, { conditions });
  }

  onStepConditionsChange(conditions: StepCondition[]): void {
    const step = this.selectedStep();
    if (!step) return;
    this.updateStepConfig({ conditions });
  }

  onMappingsChange(mappings: (ClaimMapping | TransformMapping)[]): void {
    this.updateStepConfig({ mappings });
  }

  getOtherSteps(): JourneyStep[] {
    const step = this.selectedStep();
    if (!step) return this.policy().steps || [];
    return (this.policy().steps || []).filter((s) => s.id !== step.id);
  }

  // Branch IDs the selected step's type can emit. Empty for custom/plugin
  // steps that emit arbitrary IDs — those fall back to free-text entry.
  availableBranchesForSelectedStep(): StepBranchInfo[] {
    const step = this.selectedStep();
    if (!step) return [];
    return this.getStepType(step)?.availableBranches ?? [];
  }

  // Available branch IDs not already mapped on the step, plus the row's own
  // current id so the active selection stays visible in its dropdown.
  unusedBranchOptions(currentId: string): StepBranchInfo[] {
    const step = this.selectedStep();
    const used = new Set(Object.keys(step?.branches || {}));
    used.delete(currentId);
    return this.availableBranchesForSelectedStep().filter((b) => !used.has(b.id));
  }

  objectKeys(obj: Record<string, unknown>): string[] {
    return Object.keys(obj || {});
  }

  objectEntries(obj: Record<string, unknown>): [string, unknown][] {
    return Object.entries(obj || {});
  }

  onDuplicateCheckFieldsChange(value: string): void {
    this.updatePolicy({
      duplicateCheckFields: value
        .split(',')
        .map((s) => s.trim())
        .filter(Boolean),
    });
  }

  getDuplicateCheckFieldsString(): string {
    return (this.policy().duplicateCheckFields || []).join(', ');
  }

  getAllowedOriginsString(): string {
    return this.policy().allowedOrigins || '';
  }

  onAllowedOriginsChange(value: string): void {
    this.updatePolicy({ allowedOrigins: value || undefined });
  }

  generateApiKey(): void {
    const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
    let key = 'pk_';
    for (let i = 0; i < 32; i++) {
      key += chars.charAt(Math.floor(Math.random() * chars.length));
    }
    this.updatePolicy({ apiKey: key });
  }

  parseClaimsList(value: string): string[] {
    return value.split(',').map((s) => s.trim()).filter(Boolean);
  }

  parseStatusCodesList(value: string): number[] {
    return value.split(',').map((s) => parseInt(s.trim(), 10)).filter((n) => !isNaN(n));
  }

  onContinueOnStatusChange(value: string): void {
    const statusCodes = this.parseStatusCodesList(value);
    this.updateStepConfig({ continueOnStatus: statusCodes });
  }

  onBodyFromClaimsChange(value: string): void {
    const claims = this.parseClaimsList(value);
    this.updateStepConfig({ bodyFromClaims: claims });
  }
}

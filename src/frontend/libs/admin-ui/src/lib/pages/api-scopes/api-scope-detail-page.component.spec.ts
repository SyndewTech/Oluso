import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ToastService } from '@syndew/oluso-ui-core';
import {
  ApiScopeDetailPageComponent,
  suffixMaxLengthError,
  suffixPatternError,
} from './api-scope-detail-page.component';
import { ApiScopeService } from '../../services/api-scope.service';
import { SettingsService } from '../../services/settings.service';
import { ApiScopeDto } from '../../models/api-scope.model';

@Component({ template: '' })
class BlankComponent {}

describe('ApiScopeDetailPageComponent prefix scope settings', () => {
  const scope = (fields: Partial<ApiScopeDto> = {}): ApiScopeDto => ({
    id: 3,
    name: 'device:',
    displayName: 'Device',
    enabled: true,
    required: false,
    emphasize: false,
    showInDiscoveryDocument: true,
    userClaims: [],
    created: '2026-01-01T00:00:00Z',
    isPrefix: true,
    suffixPattern: '[0-9]+',
    suffixMaxLength: 8,
    ...fields,
  });

  function setup(id: string, existing: ApiScopeDto = scope(), tenantDefault?: number) {
    const settings = {
      getSettings: () => of(tenantDefault === undefined ? {} : { protocolSettings: { prefixScopeSuffixMaxLength: tenantDefault } }),
    };
    const create = vi.fn((request: unknown) => of({ ...existing, ...(request as object), id: 9 }));
    const update = vi.fn((_: number, dto: ApiScopeDto) => of(dto));
    TestBed.configureTestingModule({
      imports: [ApiScopeDetailPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: '**', component: BlankComponent }]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id }), data: {} } } },
        { provide: ApiScopeService, useValue: { getById: () => of(existing), create, update } },
        { provide: ToastService, useValue: { error: vi.fn(), success: vi.fn() } },
        { provide: SettingsService, useValue: settings },
      ],
    });
    const fixture = TestBed.createComponent(ApiScopeDetailPageComponent);
    const page = fixture.componentInstance;
    fixture.detectChanges();
    const q = (testId: string) => fixture.nativeElement.querySelector(`[data-testid="${testId}"]`) as HTMLElement | null;
    return { fixture, page, q, create, update };
  }

  it('shows the suffix fields only while Prefix scope is ticked', async () => {
    const { fixture, q } = setup('new');
    expect(q('prefix-fields')).toBeNull();

    (q('prefix-scope-checkbox') as HTMLInputElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(q('prefix-fields')).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain(
      'Leave empty to allow any characters a scope can contain. Anchored to the whole suffix.',
    );
  });

  it('creates a prefix scope with its pattern and length, and drops empty fields', () => {
    const { page, create } = setup('new');
    page.formData = { name: 'files:', isPrefix: true, suffixPattern: '[a-z/]+', suffixMaxLength: 64 };
    page.onCreate();
    expect(create).toHaveBeenCalledWith(
      expect.objectContaining({ name: 'files:', isPrefix: true, suffixPattern: '[a-z/]+', suffixMaxLength: 64 }),
    );

    page.formData = { name: 'other:', isPrefix: true, suffixPattern: '  ', suffixMaxLength: null };
    page.onCreate();
    expect(create).toHaveBeenLastCalledWith(
      expect.objectContaining({ isPrefix: true, suffixPattern: null, suffixMaxLength: null }),
    );
  });

  it('does not send a suffix rule for an ordinary scope', () => {
    const { page, create } = setup('new');
    page.formData = { name: 'read:orders', isPrefix: false, suffixPattern: '[0-9]+', suffixMaxLength: 4 };
    page.onCreate();
    expect(create).toHaveBeenCalledWith(
      expect.objectContaining({ isPrefix: false, suffixPattern: null, suffixMaxLength: null }),
    );
  });

  it('edits the fields and sends cleared ones as null so the server removes them', () => {
    const { page, update } = setup('3');
    page.openEditModal();
    expect(page.editFormData.suffixPattern).toBe('[0-9]+');
    expect(page.editFormData.suffixMaxLength).toBe(8);

    page.editFormData.suffixPattern = '';
    page.editFormData.suffixMaxLength = 32;
    page.onUpdate();

    expect(update).toHaveBeenCalledWith(
      3,
      expect.objectContaining({ isPrefix: true, suffixPattern: null, suffixMaxLength: 32 }),
    );
  });

  it('flags a pattern that does not compile', async () => {
    const { fixture, page, q } = setup('3');
    page.openEditModal();
    page.editFormData.suffixPattern = '[a-';
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(q('suffix-pattern-error')?.textContent).toContain('not a valid regular expression');
  });

  it('refuses a length that is not a positive whole number', () => {
    const { page, update } = setup('3');
    page.openEditModal();
    page.editFormData.suffixMaxLength = 0;
    page.onUpdate();
    expect(update).not.toHaveBeenCalled();
  });

  it("shows the tenant's default length as the placeholder", async () => {
    const { fixture, q } = setup('new', scope(), 512);
    (q('prefix-scope-checkbox') as HTMLInputElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect((q('suffix-max-length') as HTMLInputElement).placeholder).toBe('512 (default)');
    expect(fixture.nativeElement.textContent).toContain("this tenant's default of 512 characters");
  });

  it('falls back to 256 when the tenant default is not available', async () => {
    const { fixture, q } = setup('new');
    (q('prefix-scope-checkbox') as HTMLInputElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect((q('suffix-max-length') as HTMLInputElement).placeholder).toBe('256 (default)');
  });

  it('checks patterns and lengths', () => {
    expect(suffixPatternError('')).toBeNull();
    expect(suffixPatternError('[A-Za-z0-9._~-]+')).toBeNull();
    expect(suffixPatternError('(unclosed')).not.toBeNull();
    expect(suffixMaxLengthError(null)).toBeNull();
    expect(suffixMaxLengthError('')).toBeNull();
    expect(suffixMaxLengthError(12)).toBeNull();
    expect(suffixMaxLengthError(0)).not.toBeNull();
    expect(suffixMaxLengthError(2.5)).not.toBeNull();
  });
});

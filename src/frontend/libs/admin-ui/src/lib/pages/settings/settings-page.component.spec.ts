import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { PluginService, ToastService } from '@syndew/oluso-ui-core';
import { SettingsPageComponent } from './settings-page.component';
import { SettingsService } from '../../services/settings.service';
import { ServerSettings, prefixScopeSuffixMaxLengthError } from '../../models/server-settings.model';

describe('SettingsPageComponent prefix scope default', () => {
  const serverSettings = (prefixScopeSuffixMaxLength?: number): ServerSettings => ({
    tokenSettings: { defaultAccessTokenLifetime: 3600, defaultIdentityTokenLifetime: 300, defaultRefreshTokenLifetime: 2592000 },
    securitySettings: { requireHttps: true, emitStaticClaims: false, enableBackchannelLogout: true },
    corsSettings: { allowAllOrigins: false, allowedOrigins: [] },
    protocolSettings: {
      enforceOAuth21: false,
      enableDynamicClientRegistration: true,
      allowOpenDynamicRegistration: false,
      dynamicRegistrationRequirePkce: true,
      dynamicRegistrationMaxRedirectUris: 10,
      ...(prefixScopeSuffixMaxLength === undefined ? {} : { prefixScopeSuffixMaxLength }),
    } as ServerSettings['protocolSettings'],
  });

  function setup(loaded: ServerSettings) {
    const updateSettings = vi.fn((s: ServerSettings) => of(s));
    const toast = { error: vi.fn(), success: vi.fn() };
    TestBed.configureTestingModule({
      imports: [SettingsPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap({ tab: 'protocol' }), paramMap: convertToParamMap({}), data: {} } },
        },
        { provide: SettingsService, useValue: { getSettings: () => of(loaded), updateSettings } },
        { provide: PluginService, useValue: { settingsTabs: signal([]) } },
        { provide: ToastService, useValue: toast },
      ],
    });
    const fixture = TestBed.createComponent(SettingsPageComponent);
    fixture.detectChanges();
    const q = (testId: string) => fixture.nativeElement.querySelector(`[data-testid="${testId}"]`) as HTMLElement | null;
    return { fixture, page: fixture.componentInstance, q, updateSettings, toast };
  }

  async function settle(fixture: { detectChanges(): void; whenStable(): Promise<unknown> }) {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('shows the setting on the Protocol tab with its helper text', async () => {
    const { fixture, q } = setup(serverSettings(512));
    await settle(fixture);

    expect((q('prefix-scope-suffix-max-length') as HTMLInputElement).value).toBe('512');
    expect(fixture.nativeElement.textContent).toContain('Default maximum suffix length for prefix scopes');
    expect(fixture.nativeElement.textContent).toContain("Used by prefix scopes that don't set their own maximum.");
    expect(fixture.nativeElement.textContent).toContain('Dynamic Client Registration');
  });

  it('defaults to 256 when the server sends no value', async () => {
    const { fixture, page, q } = setup(serverSettings());
    await settle(fixture);

    expect(page.formData.protocolSettings?.prefixScopeSuffixMaxLength).toBe(256);
    expect((q('prefix-scope-suffix-max-length') as HTMLInputElement).value).toBe('256');
  });

  it('saves the value with the other protocol settings', () => {
    const { page, updateSettings } = setup(serverSettings());
    page.updateProtocolSettings('prefixScopeSuffixMaxLength', 1024);
    page.onSave();

    expect(updateSettings).toHaveBeenCalledWith(
      expect.objectContaining({ protocolSettings: expect.objectContaining({ prefixScopeSuffixMaxLength: 1024 }) }),
    );
  });

  it('flags an out-of-range value and does not save it', async () => {
    const { fixture, page, q, updateSettings, toast } = setup(serverSettings());
    page.updateProtocolSettings('prefixScopeSuffixMaxLength', 4096);
    await settle(fixture);

    expect(q('prefix-scope-suffix-max-length-error')?.textContent).toContain('from 1 to 2048');
    page.onSave();
    expect(updateSettings).not.toHaveBeenCalled();
    expect(toast.error).toHaveBeenCalled();
  });

  it('checks the range', () => {
    expect(prefixScopeSuffixMaxLengthError(1)).toBeNull();
    expect(prefixScopeSuffixMaxLengthError(256)).toBeNull();
    expect(prefixScopeSuffixMaxLengthError(2048)).toBeNull();
    expect(prefixScopeSuffixMaxLengthError(0)).not.toBeNull();
    expect(prefixScopeSuffixMaxLengthError(2049)).not.toBeNull();
    expect(prefixScopeSuffixMaxLengthError(12.5)).not.toBeNull();
    expect(prefixScopeSuffixMaxLengthError('')).not.toBeNull();
    expect(prefixScopeSuffixMaxLengthError(null)).not.toBeNull();
  });
});

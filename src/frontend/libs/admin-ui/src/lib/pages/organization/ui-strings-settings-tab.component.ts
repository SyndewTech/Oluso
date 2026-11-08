import { Component, inject } from '@angular/core';
import { TAB_CONTEXT, TabContext } from '@oluso/ui-core';
import { LocaleOverrideEditorComponent } from '../../components/locale-override-editor.component';

@Component({
  selector: 'admin-ui-strings-settings-tab',
  standalone: true,
  imports: [LocaleOverrideEditorComponent],
  template: `
    <div class="py-4">
      <h3 class="text-sm font-medium text-gray-700 mb-1">UI String Translations</h3>
      <p class="text-xs text-gray-500 mb-4">
        Override system default strings for your login, signup, and authentication pages.
        Empty overrides fall back to the system default.
      </p>
      <admin-locale-override-editor scope="tenant" [scopeId]="tenantId" />
    </div>
  `,
})
export class UiStringsSettingsTabComponent {
  private readonly context = inject<TabContext>(TAB_CONTEXT);
  readonly tenantId = this.context.entityId;
}

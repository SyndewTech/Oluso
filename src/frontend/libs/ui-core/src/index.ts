// Models
export * from './lib/models/index';

// Tokens
export * from './lib/tokens/tab-context.token';
export * from './lib/tokens/api-base-url.token';
export * from './lib/tokens/http-context.token';

// i18n / terminology (Transloco wired once for the whole platform)
export * from './lib/i18n/index';

// Services
export * from './lib/services/plugin.service';
export * from './lib/services/currency-catalog.service';
export * from './lib/utils/minor-units';
export * from './lib/utils/open-file';
export * from './lib/utils/time-zone-difference';
export * from './lib/services/country-catalog.service';
export * from './lib/services/extension-registry.service';
export * from './lib/services/permissions.service';
export * from './lib/services/feature.service';
export * from './lib/services/module-seat.service';
export * from './lib/services/help-panel.service';
export * from './lib/services/feedback.service';
export * from './lib/services/branding.service';
export * from './lib/services/app-title.strategy';
export * from './lib/services/locale.service';
export * from './lib/services/toast.service';
export * from './lib/services/file-upload.service';
export * from './lib/services/form-validation.service';
export * from './lib/services/logger.service';

// Components
export * from './lib/components/badge/badge.component';
export * from './lib/components/button/button.component';
export * from './lib/components/card/card.component';
export * from './lib/components/confirm-dialog/confirm-dialog.component';
export * from './lib/components/prompt-dialog/prompt-dialog.component';
export * from './lib/components/icon/icon.component';
export * from './lib/components/icon/icon-paths';
export * from './lib/components/icon-picker/icon-picker.component';
export * from './lib/components/input/input.component';
export * from './lib/components/modal/modal.component';
export * from './lib/components/select/select.component';
export * from './lib/components/select/remote-options';
export * from './lib/components/slide-panel/slide-panel.component';
export * from './lib/components/table/table.component';
export * from './lib/components/data-table/data-table.component';
export * from './lib/components/pagination/pagination.component';
export * from './lib/components/page-slot-host/page-slot-host.component';
export * from './lib/components/detail-section-host/detail-section-host.component';
export * from './lib/components/dynamic-form/dynamic-form.types';
export * from './lib/components/dynamic-form/dynamic-form-field.component';
export * from './lib/components/dynamic-form/dynamic-form.component';
export * from './lib/components/kanban/kanban.types';
export * from './lib/components/kanban/kanban.component';
export * from './lib/components/module-roles/index';
export * from './lib/components/help-panel/help-panel.component';
export * from './lib/components/feedback/feedback-fab.component';
export * from './lib/components/feedback/feedback-panel.component';
export * from './lib/components/feedback/feedback.models';
export * from './lib/components/file-upload/file-upload.component';

// Generic UX components (promoted for cross-module reuse)
export * from './lib/components/context-menu/context-menu.component';
export * from './lib/components/inline-edit/inline-text-edit.component';
export * from './lib/components/inline-edit/inline-textarea-edit.component';
export * from './lib/components/bulk-actions/bulk-actions-toolbar.component';
export * from './lib/components/loading-skeleton/loading-skeleton.component';
export * from './lib/components/saved-filters/saved-filters.service';
export * from './lib/components/saved-filters/saved-filters.component';

// Global command palette + keyboard shortcuts (extensible via registries — any module can register)
export * from './lib/components/command-palette/command-registry.service';
export * from './lib/components/command-palette/command-app-area';
export * from './lib/components/command-palette/nav-item-command';
export * from './lib/components/command-palette/workspace-command-scope';
export * from './lib/components/command-palette/admin-command-scope';
export * from './lib/components/command-palette/command-palette.component';
export * from './lib/components/keyboard-shortcuts/keyboard-shortcuts.service';
export * from './lib/components/keyboard-shortcuts/shortcuts-help.component';

// Cross-module visualisation (generic — each consumer supplies its own baseUrl)
export * from './lib/components/entity-timeline/entity-timeline.component';
export * from './lib/components/approval-flow/approval-flow.component';
export * from './lib/components/approval-history/approval-history.component';
export * from './lib/components/attachment-preview/attachment-preview.component';
export * from './lib/components/toast-container/toast-container.component';

// Pipes
export * from './lib/pipes/safe-html.pipe';
export * from './lib/pipes/locale-currency.pipe';
export * from './lib/pipes/markdown.pipe';
export * from './lib/pipes/minor-units.pipe';
export * from './lib/pipes/currency-symbol.pipe';
export * from './lib/pipes/locale-date.pipe';

// Directives
export * from './lib/directives/permission.directive';
export * from './lib/maps/map-tiles';
export * from './lib/directives/feature.directive';

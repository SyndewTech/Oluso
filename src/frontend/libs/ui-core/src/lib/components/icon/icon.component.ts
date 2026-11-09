import { Component, input, computed } from '@angular/core';
import { ICON_PATHS, CIRCLE_ICONS } from './icon-paths';

export type IconName =
  | 'home'
  | 'squares'
  | 'link'
  | 'user-group'
  | 'server'
  | 'fingerprint'
  | 'shield-check'
  | 'shield'
  | 'cloud-upload'
  | 'sparkles'
  | 'chart-bar'
  | 'credit-card'
  | 'play-circle'
  | 'window'
  | 'key'
  | 'globe'
  | 'globe-alt'
  | 'user'
  | 'map'
  | 'inbox'
  | 'document-text'
  | 'queue-list'
  | 'tag'
  | 'users'
  | 'list-bullet'
  | 'envelope'
  | 'mail'
  | 'clock'
  | 'puzzle'
  | 'lock'
  | 'building'
  | 'bolt'
  | 'clipboard'
  | 'cog'
  | 'close'
  | 'chevron-right'
  | 'chevron-down'
  | 'search'
  | 'logout'
  | 'menu'
  | 'application'
  | 'folder'
  | 'palette'
  | 'eye'
  | 'calculator'
  | 'warning'
  | 'dot'
  // Nav / app icons
  | 'building-office'
  | 'building-office-2'
  | 'wrench-screwdriver'
  | 'calendar-days'
  | 'cube'
  | 'squares-2x2'
  | 'clipboard-document-check'
  | 'archive-box'
  | 'truck'
  | 'cog-6-tooth'
  | 'exclamation-triangle'
  | 'information-circle'
  | 'building-storefront'
  | 'building-library'
  | 'receipt-percent'
  | 'document-currency-dollar'
  | 'clipboard-document-list'
  | 'arrow-up-tray'
  | 'arrow-down-tray'
  | 'scale'
  | 'chart-pie'
  | 'banknotes'
  | 'briefcase'
  | 'identification'
  | 'document-duplicate'
  | 'book-open'
  // App-relevant extra icons
  | 'phone'
  | 'chat-bubble-left'
  | 'megaphone'
  | 'bell'
  | 'at-symbol'
  | 'newspaper'
  | 'bookmark'
  | 'paper-clip'
  | 'academic-cap'
  | 'hand-raised'
  | 'sun'
  | 'moon'
  | 'cloud'
  | 'fire'
  | 'heart'
  | 'plus'
  | 'minus'
  | 'check'
  | 'x-mark'
  | 'funnel'
  | 'adjustments-horizontal'
  | 'computer-desktop'
  | 'device-phone-mobile'
  | 'cpu-chip'
  | 'wifi'
  | 'qr-code'
  | 'command-line'
  | 'wrench'
  | 'scissors'
  | 'pencil'
  | 'camera'
  | 'photo'
  | 'musical-note'
  | 'gift'
  | 'map-pin'
  | 'check-circle'
  | 'x-circle'
  | 'exclamation-circle'
  | 'question-mark-circle'
  | 'flag'
  | 'star'
  | 'trophy'
  | 'rocket-launch'
  | 'lifebuoy'
  | 'light-bulb'
  | 'shopping-cart'
  | 'currency-dollar'
  | 'chevron-left'
  | 'chevron-up'
  | 'arrow-left'
  | 'arrow-right'
  | 'trash'
  | 'ellipsis-horizontal'
  | 'bars-3'
  | 'calendar'
  // Extended icons added during SVG replacement
  | 'arrow-top-right-on-square'
  | 'refresh'
  | 'arrow-path'
  | 'pause-circle'
  | 'no-symbol'
  | 'stop-circle'
  | 'server-stack'
  | 'presentation-chart-line'
  | 'clipboard-document'
  | 'arrows-right-left'
  | 'eye-slash'
  | 'document-magnifying-glass'
  | 'bars-arrow-down'
  | 'bars-arrow-up'
  | 'arrows-pointing-out'
  | 'user-plus'
  | 'clipboard-document-check-outline'
  | 'arrow-trending-down'
  // Aliases and additional icons
  | 'adjustments'
  | 'archive'
  | 'arrow-uturn-left'
  | 'beaker'
  | 'circle-stack'
  | 'clipboard-copy'
  | 'currency'
  | 'document-chart-bar'
  | 'download'
  | 'ellipsis-vertical'
  | 'filter'
  | 'inbox-stack'
  | 'info-circle'
  | 'paper-airplane'
  | 'pencil-square'
  | 'play'
  | 'table-cells'
  | 'user-minus'
  | 'handshake'
  | 'stethoscope';

/**
 * Lightweight icon component that renders Heroicons-style SVG icons.
 * Size and color are controlled via CSS classes on the host element.
 *
 * Usage:
 *   <ui-icon name="home" class="h-4 w-4 text-gray-400" />
 *   <ui-icon [name]="item.icon" class="h-5 w-5" />
 */
@Component({
  selector: 'ui-icon',
  standalone: true,
  template: `
    <svg
      class="w-full h-full"
      fill="none"
      viewBox="0 0 24 24"
      [attr.stroke-width]="strokeWidth()"
      stroke="currentColor"
    >
      @for (path of paths(); track $index) {
        <path stroke-linecap="round" stroke-linejoin="round" [attr.d]="path" />
      }
      @for (circle of circles(); track $index) {
        <circle [attr.cx]="circle.cx" [attr.cy]="circle.cy" [attr.r]="circle.r" [attr.stroke-width]="strokeWidth()" />
      }
    </svg>
  `,
  host: {
    class: 'inline-flex items-center flex-shrink-0',
  },
})
export class IconComponent {
  name = input.required<string>();
  strokeWidth = input<string | number>('2');

  paths = computed(() => ICON_PATHS[this.name()] || ICON_PATHS['menu']);
  circles = computed(() => CIRCLE_ICONS[this.name()] || []);
}

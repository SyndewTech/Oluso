import { AdminPlugin, NavItem } from '@syndew/oluso-ui-core';
import { VerificationSettingsTabComponent } from './components/verification-settings-tab.component';
import { VERIFICATION_ROUTES } from './verification.routes';

/**
 * Business (KYB) verification admin plugin: verified businesses with the people acting
 * for them and their documents, the review queue, and the provider settings tab.
 */
export const VERIFICATION_PLUGIN: AdminPlugin = {
  name: 'oluso-verification',
  version: '1.0.0',

  routes: [
    {
      path: 'verification',
      children: VERIFICATION_ROUTES,
    },
  ],

  navItems: [
    {
      id: 'verification',
      label: 'Business Verification',
      path: '/verification',
      icon: 'building-office-2',
      order: 15,
      permission: 'business_verification.read',
      mergeIntoPath: '/experience',
      children: [
        { label: 'Businesses', path: '/verification/businesses', icon: 'building-office-2', order: 0 },
        { label: 'Review queue', path: '/verification/review', icon: 'inbox', order: 10 },
      ],
    } as NavItem,
  ],

  settingsTabs: [
    {
      id: 'business-verification',
      label: 'Business verification',
      icon: 'building-office-2',
      component: VerificationSettingsTabComponent,
      order: 39,
      permission: 'business_verification.manage',
    },
  ],
};

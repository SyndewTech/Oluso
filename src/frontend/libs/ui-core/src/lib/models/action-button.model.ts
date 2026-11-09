export interface ActionButton {
  id: string;
  label: string;
  icon?: string;
  action: string;
  variant?: 'primary' | 'secondary' | 'danger' | 'ghost';
  permission?: string;
  feature?: string;
  order?: number;
}

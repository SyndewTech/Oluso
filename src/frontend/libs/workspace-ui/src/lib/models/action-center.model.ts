export interface ActionItem {
  id: string;
  providerId: string;
  title: string;
  subtitle?: string;
  description?: string;
  icon: string;
  category: string;
  actionUrl?: string;
  adminActionUrl?: string;
  workspaceActionUrl?: string;
  referenceId?: string;
  referenceType?: string;
  priority: 'Low' | 'Normal' | 'High' | 'Urgent';
  status: 'Pending' | 'Overdue' | 'Info';
  createdAt: string;
  dueDate?: string;
  scope?: 'Both' | 'WorkspaceOnly' | 'AdminOnly';
}

export interface ActionCenterGroup {
  providerId: string;
  displayName: string;
  icon: string;
  workspaceActionUrl?: string;
  count: number;
  items: ActionItem[];
}

export interface ActionCenterResult {
  totalCount: number;
  groups: ActionCenterGroup[];
}

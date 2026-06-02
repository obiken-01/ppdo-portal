/** Mirrors PPDO.Application/DTOs/ResourceLinks/ */

export interface ResourceLinkItem {
  id: string;
  title: string;
  url: string;
  category: string;
  categoryOrder: number;
  linkOrder: number;
  isActive: boolean;
  isAdminCreated: boolean;
  submittedById: string | null;
}

export interface ResourceLinkCategory {
  category: string;
  categoryOrder: number;
  links: ResourceLinkItem[];
}

export interface CreateResourceLinkRequest {
  title: string;
  url: string;
  category: string;
  categoryOrder: number;
  linkOrder: number;
}

export interface UpdateResourceLinkRequest {
  title: string;
  url: string;
  category: string;
  categoryOrder: number;
  linkOrder: number;
}

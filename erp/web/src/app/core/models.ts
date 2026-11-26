export interface ApiEnvelope<T> {
  success: boolean;
  data: T | null;
  message: string | null;
  errors: Record<string, string[]> | null;
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface Session {
  id: number;
  email: string;
  fullName: string;
  roles: string[];
  csrfToken: string;
}

export interface UserDto {
  id: number;
  email: string;
  fullName: string;
  isActive: boolean;
  roles: string[];
}

/** Roles, mirrored from Erp.Domain.Common.Roles. */
export const ROLES = ['Admin', 'Accountant', 'StoreKeeper', 'Purchasing', 'Sales', 'Dispatcher'] as const;
export type Role = (typeof ROLES)[number];

/** Who may write each area — mirrors Erp.Domain.Common.Policies (the API is the real enforcement). */
export const WRITE_ROLES = {
  users: ['Admin'],
  currencies: ['Admin', 'Accountant'],
  items: ['Admin', 'StoreKeeper', 'Purchasing'],
  warehouses: ['Admin', 'StoreKeeper'],
  suppliers: ['Admin', 'Purchasing', 'Accountant'],
  customers: ['Admin', 'Sales', 'Accountant'],
  fleet: ['Admin', 'Dispatcher'],
} as const satisfies Record<string, readonly string[]>;

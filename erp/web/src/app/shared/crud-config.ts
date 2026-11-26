export type FieldType = 'text' | 'email' | 'number' | 'textarea' | 'select' | 'checkbox' | 'date';

export interface Field {
  key: string;
  /** i18n key for the label */
  label: string;
  type: FieldType;
  required?: boolean;
  step?: string;
  maxlength?: number;
  /** Static options for a select (label is an i18n key). */
  options?: { value: string | number; label: string }[];
  /** API endpoint that supplies options for a select (value = id, label = localized name). */
  lookup?: string;
  /** Select may be left empty (sends null). */
  nullable?: boolean;
  /** Hide this column in the list table. */
  noColumn?: boolean;
  /** Cannot be changed after creation. */
  lockedOnEdit?: boolean;
}

export interface CrudConfig {
  title: string;
  endpoint: string;
  writeRoles: readonly string[];
  fields: Field[];
  defaults: Record<string, unknown>;
  /** Extra query-string filters for the list call, e.g. a parent id. */
  searchable?: boolean;
}

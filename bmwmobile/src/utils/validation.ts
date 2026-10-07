export const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

export function validateEmail(value: string): string | null {
  const v = value.trim();
  if (!v) return 'Enter your email';
  if (!EMAIL_PATTERN.test(v)) return 'Enter a valid email address';
  return null;
}

export type PasswordRule = { id: string; label: string; test: (p: string) => boolean };

/** Same rules as IdentityService (Services/PasswordRules.cs). */
export const PASSWORD_RULES: PasswordRule[] = [
  { id: 'length', label: 'At least 8 characters', test: p => p.length >= 8 },
  { id: 'upper', label: 'One uppercase letter', test: p => /[A-Z]/.test(p) },
  { id: 'lower', label: 'One lowercase letter', test: p => /[a-z]/.test(p) },
  { id: 'digit', label: 'One number', test: p => /\d/.test(p) },
  { id: 'special', label: 'One special character', test: p => /[^A-Za-z0-9]/.test(p) },
];

export function validatePassword(value: string): string | null {
  if (!value) return 'Enter a password';
  const failed = PASSWORD_RULES.find(r => !r.test(value));
  return failed ? `Password needs: ${failed.label.toLowerCase()}` : null;
}

export function validateFullName(value: string): string | null {
  const v = value.trim();
  if (!v) return 'Enter your full name';
  if (v.length < 2) return 'Name is too short';
  return null;
}

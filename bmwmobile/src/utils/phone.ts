export type PhoneCountry = {
  code: 'IN' | 'US' | 'NG' | 'IE';
  name: string;
  flag: string;
  dialCode: string;
  /** Allowed national-number lengths (digits only, without the dial code). */
  lengths: number[];
  example: string;
};

export const PHONE_COUNTRIES: PhoneCountry[] = [
  { code: 'IN', name: 'India', flag: '🇮🇳', dialCode: '+91', lengths: [10], example: '98765 43210' },
  { code: 'US', name: 'United States', flag: '🇺🇸', dialCode: '+1', lengths: [10], example: '201 555 0123' },
  { code: 'NG', name: 'Nigeria', flag: '🇳🇬', dialCode: '+234', lengths: [10], example: '803 123 4567' },
  { code: 'IE', name: 'Ireland', flag: '🇮🇪', dialCode: '+353', lengths: [9], example: '85 123 4567' },
];

export const DEFAULT_COUNTRY = PHONE_COUNTRIES[0];

export const digitsOnly = (s: string) => s.replace(/\D/g, '');

/** Nigerian and Irish numbers are often typed with the trunk 0 (0803..., 085...); it is not part of the international number. */
export const nationalDigits = (country: Pick<PhoneCountry, 'code'>, text: string) =>
  country.code === 'NG' || country.code === 'IE' ? digitsOnly(text).replace(/^0/, '') : digitsOnly(text);

export const maxLength = (c: PhoneCountry) => Math.max(...c.lengths);

export function validatePhone(country: PhoneCountry, national: string): string | null {
  const d = nationalDigits(country, national);
  if (!d) return 'Enter your mobile number';
  if (!country.lengths.includes(d.length)) {
    return `Enter a ${country.lengths.join(' or ')}-digit ${country.name} number`;
  }
  if (country.code === 'IN' && !/^[6-9]/.test(d)) return 'Enter a valid Indian mobile number';
  if (country.code === 'NG' && !/^[789]/.test(d)) return 'Enter a valid Nigerian mobile number';
  if (country.code === 'US' && !/^[2-9]/.test(d)) return 'Enter a valid US phone number';
  if (country.code === 'IE' && !/^8/.test(d)) return 'Enter a valid Irish mobile number';
  return null;
}

/** E.164 format for the API, e.g. +919876543210. */
export const toE164 = (country: PhoneCountry, national: string) => `${country.dialCode}${nationalDigits(country, national)}`;

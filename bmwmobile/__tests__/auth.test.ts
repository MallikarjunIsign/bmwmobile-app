import { PHONE_COUNTRIES, toE164, validatePhone } from '../src/utils/phone';
import { PASSWORD_RULES, validateEmail, validateFullName, validatePassword } from '../src/utils/validation';
import { currentUsername, DEMO_OWNER, isCurrentUser, setSession } from '../src/auth/session';

const country = (code: string) => PHONE_COUNTRIES.find(c => c.code === code)!;

describe('phone numbers (India, USA, Nigeria, Ireland)', () => {
  test('India is the default and only these four countries are offered', () => {
    expect(PHONE_COUNTRIES.map(c => c.dialCode)).toEqual(['+91', '+1', '+234', '+353']);
  });

  test('valid numbers become E.164', () => {
    expect(validatePhone(country('IN'), '98765 43210')).toBeNull();
    expect(toE164(country('IN'), '98765 43210')).toBe('+919876543210');
    expect(validatePhone(country('US'), '201-555-0123')).toBeNull();
    expect(toE164(country('US'), '201-555-0123')).toBe('+12015550123');
    expect(validatePhone(country('NG'), '0803 123 4567')).toBeNull();
    expect(toE164(country('NG'), '0803 123 4567')).toBe('+2348031234567');
    expect(validatePhone(country('IE'), '085 123 4567')).toBeNull();
    expect(toE164(country('IE'), '085 123 4567')).toBe('+353851234567');
  });

  test('wrong lengths and prefixes are rejected', () => {
    expect(validatePhone(country('IN'), '12345')).toMatch(/10-digit/);
    expect(validatePhone(country('IN'), '1234567890')).toMatch(/Indian/);
    expect(validatePhone(country('NG'), '6031234567')).toMatch(/Nigerian/);
    expect(validatePhone(country('US'), '')).toMatch(/Enter/);
    expect(validatePhone(country('IE'), '12 345 6789')).toMatch(/Irish/);
    expect(validatePhone(country('IE'), '85 123 456')).toMatch(/9-digit/);
  });
});

describe('form rules', () => {
  test('password checklist matches the server rules', () => {
    expect(PASSWORD_RULES.map(r => r.id)).toEqual(['length', 'upper', 'lower', 'digit', 'special']);
    expect(validatePassword('Passw0rd!')).toBeNull();
    expect(validatePassword('password1!')).toMatch(/uppercase/);
    expect(validatePassword('Password1')).toMatch(/special/);
  });

  test('email and name', () => {
    expect(validateEmail(' you@example.com ')).toBeNull();
    expect(validateEmail('you@example')).toMatch(/valid/);
    expect(validateFullName('A')).toMatch(/short/);
  });
});

describe('current user (replaces demo_buyer)', () => {
  afterEach(() => setSession(null));

  test('guests see the demo data owned by "you"', () => {
    expect(currentUsername()).toBe(DEMO_OWNER);
    expect(isCurrentUser(DEMO_OWNER)).toBe(true);
  });

  test('signed-in users are matched by their username', () => {
    setSession({
      accessToken: 'a',
      accessTokenExpiresAt: Date.now() + 3600_000,
      refreshToken: 'r',
      user: { id: '1', username: 'mallikarjun', fullName: 'Mallikarjun R', email: 'm@example.com', phoneNumber: null, role: 'user', emailVerified: true, hasLocation: true },
    });
    expect(currentUsername()).toBe('mallikarjun');
    expect(isCurrentUser('mallikarjun')).toBe(true);
    expect(isCurrentUser(DEMO_OWNER)).toBe(false);
    expect(isCurrentUser('autoworks_lagos')).toBe(false);
    expect(isCurrentUser(undefined)).toBe(false);
  });
});

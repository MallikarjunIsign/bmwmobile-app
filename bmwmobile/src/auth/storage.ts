import * as Keychain from 'react-native-keychain';
import type { AuthSession } from '../types/auth';

/**
 * Everything sign-in related lives in the OS secure store (iOS Keychain / Android Keystore),
 * never in plain AsyncStorage. No passwords are stored.
 */
const SESSION_SERVICE = 'com.bidmywork.session';
const REMEMBER_EMAIL_SERVICE = 'com.bidmywork.remember-email';
const DEVICE_SERVICE = 'com.bidmywork.device';

const secure = { accessible: Keychain.ACCESSIBLE.WHEN_UNLOCKED_THIS_DEVICE_ONLY };

async function read(service: string) {
  try {
    return (await Keychain.getGenericPassword({ service })) || null;
  } catch {
    // Missing / corrupt / unreadable (e.g. after a device restore).
    return null;
  }
}

async function write(service: string, username: string, password: string) {
  try {
    await Keychain.setGenericPassword(username, password, { service, ...secure });
  } catch {
    // Non-fatal: it just won't be remembered after a restart.
  }
}

async function remove(service: string) {
  try {
    await Keychain.resetGenericPassword({ service });
  } catch {
    /* best effort */
  }
}

/** "Remember me": the session (with its refresh token) survives app restarts. */
export async function saveSession(session: AuthSession) {
  await write(SESSION_SERVICE, 'session', JSON.stringify(session));
}

export async function loadSession(): Promise<AuthSession | null> {
  const saved = await read(SESSION_SERVICE);
  if (!saved) return null;
  try {
    const parsed = JSON.parse(saved.password) as AuthSession;
    return parsed?.refreshToken && parsed?.user ? parsed : null;
  } catch {
    return null;
  }
}

export const clearSession = () => remove(SESSION_SERVICE);

/** The email shown on the login screen next time ("Remember me"). */
export async function getRememberedEmail(): Promise<string | null> {
  return (await read(REMEMBER_EMAIL_SERVICE))?.password ?? null;
}

export const saveRememberedEmail = (email: string) => write(REMEMBER_EMAIL_SERVICE, 'email', email);

export const clearRememberedEmail = () => remove(REMEMBER_EMAIL_SERVICE);

/**
 * This installation's random device ID, plus the server-issued device token
 * from "Remember this device" (lets this phone skip the email code for 30 days).
 */
export type DeviceCredentials = { deviceId: string; deviceToken: string | null };

let cachedDevice: DeviceCredentials | null = null;

function randomId(): string {
  const hex = () => Math.floor(Math.random() * 16).toString(16);
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c =>
    c === 'x' ? hex() : (8 + Math.floor(Math.random() * 4)).toString(16),
  );
}

export async function getDeviceCredentials(): Promise<DeviceCredentials> {
  if (cachedDevice) return cachedDevice;
  const saved = await read(DEVICE_SERVICE);
  if (saved) {
    cachedDevice = { deviceId: saved.username, deviceToken: saved.password || null };
    return cachedDevice;
  }
  // The device ID only names this phone; the security comes from the server-issued device token.
  cachedDevice = { deviceId: randomId(), deviceToken: null };
  await write(DEVICE_SERVICE, cachedDevice.deviceId, '');
  return cachedDevice;
}

export async function saveDeviceToken(deviceToken: string | null) {
  const device = await getDeviceCredentials();
  cachedDevice = { ...device, deviceToken };
  await write(DEVICE_SERVICE, device.deviceId, deviceToken ?? '');
}

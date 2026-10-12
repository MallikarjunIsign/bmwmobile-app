import { PermissionsAndroid, Platform } from 'react-native';
import { getApps } from '@react-native-firebase/app';
import {
  AuthorizationStatus,
  getInitialNotification,
  getMessaging,
  getToken,
  onNotificationOpenedApp,
  onTokenRefresh,
  requestPermission,
  setBackgroundMessageHandler,
} from '@react-native-firebase/messaging';
import { ChatApi } from './chatApi';

/**
 * Push notifications for new chat messages (Firebase Cloud Messaging). Everything here is a no-op until the Firebase
 * project files are added (android/app/google-services.json, ios/GoogleService-Info.plist) – the app then still gets
 * messages live while it's open, just no notifications in the background.
 */
export const pushAvailable = (): boolean => {
  try {
    return getApps().length > 0;
  } catch {
    return false;
  }
};

let registeredToken: string | null = null;

/** After sign-in: ask for permission and register this phone with ChatService. Returns the cleanup. */
export async function registerForPush(): Promise<() => void> {
  if (!pushAvailable()) return () => undefined;
  try {
    const messaging = getMessaging();
    if (Platform.OS === 'android' && Number(Platform.Version) >= 33) {
      const granted = await PermissionsAndroid.request('android.permission.POST_NOTIFICATIONS' as any);
      if (granted !== PermissionsAndroid.RESULTS.GRANTED) return () => undefined;
    } else if (Platform.OS === 'ios') {
      const status = await requestPermission(messaging);
      if (status !== AuthorizationStatus.AUTHORIZED && status !== AuthorizationStatus.PROVISIONAL) return () => undefined;
    }

    const send = async (token: string) => {
      registeredToken = token;
      await ChatApi.registerDevice(token, Platform.OS === 'ios' ? 'ios' : 'android').catch(() => undefined);
    };
    await send(await getToken(messaging));
    return onTokenRefresh(messaging, send);
  } catch {
    // Push isn't essential: the app works without it.
    return () => undefined;
  }
}

/** Before sign-out: this phone stops getting the user's notifications (needs the still-valid session). */
export async function unregisterPush(): Promise<void> {
  const token = registeredToken;
  registeredToken = null;
  if (!token) return;
  await Promise.race([ChatApi.removeDevice(token).catch(() => undefined), new Promise(r => setTimeout(r, 3000))]);
}

/**
 * Tapping a chat notification (app in the background, or started by the tap): calls `open` with the conversation id.
 * Returns the cleanup.
 */
export function onChatNotificationOpened(open: (conversationId: string) => void, openOrders?: () => void): () => void {
  if (!pushAvailable()) return () => undefined;
  const messaging = getMessaging();
  const handle = (message: { data?: Record<string, unknown> } | null) => {
    const id = message?.data?.conversationId;
    if (message?.data?.type === 'chat' && typeof id === 'string') open(id);
    // "How was your purchase?" reminder: the order (and its Rate button) is in Orders.
    else if (message?.data?.type === 'review') openOrders?.();
  };
  getInitialNotification(messaging).then(handle).catch(() => undefined);
  return onNotificationOpenedApp(messaging, handle);
}

/**
 * index.js: messages that arrive while the app is in the background are shown by the system (they carry a
 * notification); nothing else to do, but Firebase wants a handler registered early.
 */
export function registerBackgroundPushHandler() {
  if (!pushAvailable()) return;
  setBackgroundMessageHandler(getMessaging(), async () => undefined);
}

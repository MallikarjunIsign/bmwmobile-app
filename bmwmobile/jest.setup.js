/* eslint-env jest */
jest.mock('react-native-safe-area-context', () =>
  require('react-native-safe-area-context/jest/mock').default,
);

// Native modules used for sign-in. Nothing is stored in tests: every app start is signed out.
jest.mock('react-native-keychain', () => ({
  ACCESSIBLE: { WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'AccessibleWhenUnlockedThisDeviceOnly' },
  getGenericPassword: jest.fn(async () => false),
  setGenericPassword: jest.fn(async () => true),
  resetGenericPassword: jest.fn(async () => true),
}));

jest.mock('@react-native-google-signin/google-signin', () => ({
  GoogleSignin: {
    configure: jest.fn(),
    hasPlayServices: jest.fn(async () => true),
    signIn: jest.fn(),
    signOut: jest.fn(async () => null),
  },
  isErrorWithCode: () => false,
  isSuccessResponse: () => false,
  statusCodes: {},
}));

// Firebase isn't configured in tests: push is simply off.
jest.mock('@react-native-firebase/app', () => ({ getApps: () => [] }));
jest.mock('@react-native-firebase/messaging', () => ({
  AuthorizationStatus: { AUTHORIZED: 1, PROVISIONAL: 2 },
  getMessaging: jest.fn(),
  getToken: jest.fn(),
  onTokenRefresh: jest.fn(() => () => undefined),
  requestPermission: jest.fn(),
  getInitialNotification: jest.fn(async () => null),
  onNotificationOpenedApp: jest.fn(() => () => undefined),
  setBackgroundMessageHandler: jest.fn(),
}));

jest.mock('@react-native-async-storage/async-storage', () => ({
  __esModule: true,
  default: { getItem: jest.fn(async () => null), setItem: jest.fn(async () => undefined), removeItem: jest.fn(async () => undefined) },
}));

jest.mock('@react-native-community/geolocation', () => ({
  __esModule: true,
  default: {
    setRNConfiguration: jest.fn(),
    requestAuthorization: jest.fn(),
    getCurrentPosition: jest.fn((_ok, fail) => fail({ code: 1, PERMISSION_DENIED: 1, TIMEOUT: 3 })),
  },
}));

jest.mock('@invertase/react-native-apple-authentication', () => ({
  __esModule: true,
  default: { isSupported: false, Operation: {}, Scope: {}, Error: {}, performRequest: jest.fn() },
  AppleButton: () => null,
}));

// Stripe's payment sheet is native: tests set what it answers (global.__stripeSheet).
jest.mock('@stripe/stripe-react-native', () => {
  global.__stripeSheet = {
    initPaymentSheet: jest.fn(async () => ({})),
    presentPaymentSheet: jest.fn(async () => ({})),
  };
  return {
    StripeProvider: ({ children }) => children,
    useStripe: () => global.__stripeSheet,
    initStripe: jest.fn(async () => undefined),
  };
});

jest.mock('react-native-image-picker', () => ({
  launchCamera: jest.fn(async () => ({ didCancel: true })),
  launchImageLibrary: jest.fn(async () => ({ didCancel: true })),
}));


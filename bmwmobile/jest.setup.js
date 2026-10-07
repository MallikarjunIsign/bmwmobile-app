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

jest.mock('@invertase/react-native-apple-authentication', () => ({
  __esModule: true,
  default: { isSupported: false, Operation: {}, Scope: {}, Error: {}, performRequest: jest.fn() },
  AppleButton: () => null,
}));

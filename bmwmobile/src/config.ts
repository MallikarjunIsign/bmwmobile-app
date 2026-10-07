import { Platform } from 'react-native';

/**
 * App configuration. Change values here — nothing else needs editing.
 *
 * Development URLs:
 *  - Android emulator reaches your PC at 10.0.2.2
 *  - iOS simulator reaches your PC at localhost
 *  - A real phone needs your PC's Wi-Fi IP, e.g. http://192.168.1.20:6001
 */
const DEV_HOST = Platform.OS === 'android' ? '10.0.2.2' : 'localhost';

export const config = {
  /** GatewayService (YARP) – auctions, search, bids, ... */
  GATEWAY_URL: `http://${DEV_HOST}:6001`,

  /** IdentityService – sign-up, login (/connect/token) and account API. */
  IDENTITY_URL: `http://${DEV_HOST}:5001`,

  /** The mobile client registered in IdentityService/Config.cs. */
  OAUTH_CLIENT_ID: 'mobileApp',
  OAUTH_SCOPE: 'openid profile auctionApp IdentityServerApi offline_access',

  /**
   * Customer / Service Pro accounts. false = everyone is one plain user.
   * Turn on together with the Identity setting Features:RolesEnabled.
   */
  ENABLE_ROLES: false,

  /** Request timeout for sign-in calls, in milliseconds. */
  AUTH_TIMEOUT_MS: 20000,

  /**
   * Google sign-in (Google Cloud Console → APIs & Services → Credentials):
   *  - "Web application" client → GOOGLE_WEB_CLIENT_ID (also in Identity's SocialAuth:GoogleClientIds)
   *  - "iOS" client             → GOOGLE_IOS_CLIENT_ID (not set up yet)
   *  - "Android" client for package com.bidmywork.app + the debug keystore SHA-1 (no value needed here)
   */
  GOOGLE_WEB_CLIENT_ID: '708012264103-uana7c3n4qsca3gep7ib3pbo52an7fkl.apps.googleusercontent.com',
  GOOGLE_IOS_CLIENT_ID: '',

  TERMS_URL: 'https://www.bidmywork.com/terms/',
  PRIVACY_URL: 'https://www.bidmywork.com/privacy/',
} as const;

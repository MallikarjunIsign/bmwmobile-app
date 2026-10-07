import { Auction, Purchase, PartRequest, ContainerSchedule } from './models';

export type RootStackParamList = {
  Splash: undefined;
  Login: { email?: string; notice?: string } | undefined;
  SignUp: undefined;
  Otp: { challengeId: string; maskedEmail: string; resendAfterSeconds: number; devOtp?: string | null; rememberMe: boolean };
  ForgotPassword: { email?: string } | undefined;
  ResetPassword: { email: string; resendAfterSeconds: number; devOtp?: string | null };
  ProfilePreferences: undefined;
  SetLocation: { mode: 'onboarding' } | { mode: 'add' } | { mode: 'edit'; locationId: string } | undefined;
  ManageLocations: undefined;

  MainTabs: { screen?: string } | undefined;

  PartDetails: { auctionId: string; auction?: Auction };
  PlaceBidModal: { auction: Auction };
  HighestBidder: { auction: Auction };
  Outbid: { auction: Auction };
  EndingSoon: { auction: Auction };
  AuctionWon: { auction: Auction };
  AuctionLost: { auction: Auction };

  CompletePurchase: { auctionId: string; auction?: Auction };
  PaymentConfirmed: { purchase: Purchase };
  PickupReceive: { purchase: Purchase };

  PartUnavailable: { auctionTitle?: string; make?: string; model?: string };
  OrderPartRequest: { prefillMake?: string; prefillModel?: string; prefillPart?: string };
  RequestSubmitted: { request: PartRequest };
  PlannedContainers: undefined;
};

export type MainTabParamList = {
  Home: undefined;
  Marketplace: { searchTerm?: string; category?: string } | undefined;
  MyBids: undefined;
  MyPurchases: undefined;
  Tracking: { containerNumber?: string } | undefined;
};

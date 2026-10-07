import { Auction, Purchase, PartRequest, ContainerSchedule } from './models';

export type RootStackParamList = {
  Splash: undefined;
  Login: undefined;
  Otp: { phoneNumber: string };
  ProfilePreferences: undefined;

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

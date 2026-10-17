import React, { useRef } from 'react';
import { View, Text, StyleSheet } from 'react-native';
import { NavigationContainer } from '@react-navigation/native';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { createBottomTabNavigator } from '@react-navigation/bottom-tabs';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { RootStackParamList, MainTabParamList } from '../types/navigation';
import { COLORS } from '../theme/colors';
import { FEATURES } from '../config';
import { CustomOrderAlerts } from '../components/CustomOrderAlerts';
import { NotificationToasts } from '../components/NotificationToasts';
import { SellStartScreen } from '../screens/sell/SellStartScreen';
import { SellWizardScreen } from '../screens/sell/SellWizardScreen';
import { MyListingsScreen } from '../screens/MyListingsScreen';
import { SellerProfileScreen } from '../screens/SellerProfileScreen';
import { WriteReviewScreen } from '../screens/WriteReviewScreen';
import { InboxScreen } from '../screens/chat/InboxScreen';
import { ConversationScreen } from '../screens/chat/ConversationScreen';
import { ChatPushOpener } from '../components/ChatPushOpener';
import { GarageScreen } from '../screens/GarageScreen';
import { LocationsScreen } from '../screens/LocationsScreen';
import { AddressFormScreen } from '../screens/AddressFormScreen';
import { ProfileScreen } from '../screens/ProfileScreen';
import { BuyerRequestsScreen } from '../screens/BuyerRequestsScreen';
import { isSeller } from '../utils/roles';
import { BrowseProvider } from '../search/BrowseContext';

// Screens
import { SplashScreen } from '../screens/SplashScreen';
import { LoginScreen } from '../screens/LoginScreen';
import { OtpScreen } from '../screens/OtpScreen';
import { SignUpScreen } from '../screens/SignUpScreen';
import { ForgotPasswordScreen } from '../screens/ForgotPasswordScreen';
import { ResetPasswordScreen } from '../screens/ResetPasswordScreen';
import { SellerApplicationScreen } from '../screens/SellerApplicationScreen';
import { useAuth } from '../auth/AuthContext';
import { useLocations } from '../location/LocationContext';
import { ProfilePreferencesScreen } from '../screens/ProfilePreferencesScreen';
import { HomeScreen } from '../screens/HomeScreen';
import { MarketplaceSearchScreen } from '../screens/MarketplaceSearchScreen';
import { MyBidsScreen } from '../screens/MyBidsScreen';
import { MyPurchasesScreen } from '../screens/MyPurchasesScreen';
import { ContainerTrackingScreen } from '../screens/ContainerTrackingScreen';

import { PartDetailsScreen } from '../screens/PartDetailsScreen';
import { PlaceBidModal } from '../screens/PlaceBidModal';
import { CheckoutScreen } from '../screens/CheckoutScreen';
import { HighestBidderScreen } from '../screens/HighestBidderScreen';
import { OutbidScreen } from '../screens/OutbidScreen';
import { EndingSoonScreen } from '../screens/EndingSoonScreen';
import { AuctionWonScreen } from '../screens/AuctionWonScreen';
import { AuctionLostScreen } from '../screens/AuctionLostScreen';

import { CompletePurchaseScreen } from '../screens/CompletePurchaseScreen';
import { PaymentConfirmedScreen } from '../screens/PaymentConfirmedScreen';
import { PickupReceiveScreen } from '../screens/PickupReceiveScreen';

import { PartUnavailableScreen } from '../screens/PartUnavailableScreen';
import { OrderPartRequestScreen } from '../screens/OrderPartRequestScreen';
import { RequestSubmittedScreen } from '../screens/RequestSubmittedScreen';
import { PlannedContainersScreen } from '../screens/PlannedContainersScreen';

const Stack = createNativeStackNavigator<RootStackParamList>();
const Tab = createBottomTabNavigator<MainTabParamList>();

const TabIcon = ({ icon, label, focused }: { icon: string; label: string; focused: boolean }) => (
  <View style={styles.tabItem}>
    <Text style={[styles.tabIcon, focused && styles.tabIconActive]}>{icon}</Text>
    <Text style={[styles.tabLabel, focused && styles.tabLabelActive]}>{label}</Text>
  </View>
);

function MainTabNavigator() {
  // Keep the bar's 64px look and add room for the system navigation bar (edge-to-edge).
  const insets = useSafeAreaInsets();
  // Sell is for seller accounts only (role from the token; the backend refuses buyers too).
  const seller = isSeller(useAuth().user);
  return (
    // Home and Market share Parts / Vehicles, All / Auction / Direct sale, the search and the filters.
    <BrowseProvider>
      {/* "A part matching your request is now available" (custom orders) */}
      <CustomOrderAlerts />
      {/* Outbid, new bid, ending in 1 hour, won, lost, sold, ended – no bids */}
      <NotificationToasts />
      {/* Tapping a chat push notification opens that conversation. */}
      <ChatPushOpener />
      <Tab.Navigator
        screenOptions={{
          headerShown: false,
          tabBarStyle: [styles.tabBar, { height: 64 + insets.bottom, paddingBottom: 6 + insets.bottom }],
          tabBarShowLabel: false,
          // React Navigation 7 sizes the icon box to the icon; let our icon+label use the full tab width.
          tabBarIconStyle: styles.tabIconBox,
        }}
      >
        <Tab.Screen 
          name="Home" 
          component={HomeScreen} 
          options={{
            tabBarIcon: ({ focused }) => <TabIcon icon="🏠" label="Home" focused={focused} />
          }}
        />
        <Tab.Screen 
          name="Marketplace" 
          component={MarketplaceSearchScreen} 
          options={{
            tabBarIcon: ({ focused }) => <TabIcon icon="🔍" label="Market" focused={focused} />
          }}
        />
        {seller ? (
          <Tab.Screen
            name="Sell"
            component={SellStartScreen}
            options={{
              tabBarIcon: ({ focused }) => <TabIcon icon="➕" label="Sell" focused={focused} />
            }}
          />
        ) : null}
        <Tab.Screen 
          name="MyBids" 
          component={MyBidsScreen} 
          options={{
            tabBarIcon: ({ focused }) => <TabIcon icon="⚡" label="My Bids" focused={focused} />
          }}
        />
        <Tab.Screen 
          name="MyPurchases" 
          component={MyPurchasesScreen} 
          options={{
            tabBarIcon: ({ focused }) => <TabIcon icon="🛍️" label="Orders" focused={focused} />
          }}
        />
        {/* Container / port logistics are hidden in this phase (FEATURES.containers). */}
        {FEATURES.containers ? (
          <Tab.Screen
            name="Tracking"
            component={ContainerTrackingScreen}
            options={{
              tabBarIcon: ({ focused }) => <TabIcon icon="🚢" label="Tracking" focused={focused} />
            }}
          />
        ) : null}
      </Tab.Navigator>
    </BrowseProvider>
  );
}

export function AppNavigator() {
  const { status } = useAuth();
  const locations = useLocations();

  const signedIn = status === 'signedIn';
  // After a logout (or an expired session) the user goes straight back to Login, not the splash.
  const hadSession = useRef(false);
  if (signedIn) hadSession.current = true;

  // Restoring a remembered session / reading the last known location: keep the dark background.
  // There is no location screen: the location is found automatically (LocationContext).
  if (status === 'loading' || (signedIn && locations.status === 'idle')) {
    return <View style={styles.loading} />;
  }

  return (
    <NavigationContainer>
      <Stack.Navigator
        // Signing in or out starts a fresh stack: the marketplace when signed in, otherwise the splash
        // (first start) or Login (after logging out). There is no guest access to the marketplace.
        key={status}
        initialRouteName={signedIn ? 'MainTabs' : hadSession.current ? 'Login' : 'Splash'}
        screenOptions={{
          headerShown: false,
          contentStyle: { backgroundColor: COLORS.background },
        }}
      >
        {signedIn ? (
          <Stack.Group>
            <Stack.Screen name="MainTabs" component={MainTabNavigator} />
            <Stack.Screen name="ProfilePreferences" component={ProfilePreferencesScreen} />

            <Stack.Screen name="PartDetails" component={PartDetailsScreen} />
            <Stack.Screen name="Checkout" component={CheckoutScreen} />
            <Stack.Screen 
              name="PlaceBidModal" 
              component={PlaceBidModal} 
              options={{ presentation: 'modal' }}
            />
            <Stack.Screen name="HighestBidder" component={HighestBidderScreen} />
            <Stack.Screen name="Outbid" component={OutbidScreen} />
            <Stack.Screen name="EndingSoon" component={EndingSoonScreen} />
            <Stack.Screen name="AuctionWon" component={AuctionWonScreen} />
            <Stack.Screen name="AuctionLost" component={AuctionLostScreen} />

            <Stack.Screen name="CompletePurchase" component={CompletePurchaseScreen} />
            <Stack.Screen name="PaymentConfirmed" component={PaymentConfirmedScreen} />
            <Stack.Screen name="PickupReceive" component={PickupReceiveScreen} />

            <Stack.Screen name="PartUnavailable" component={PartUnavailableScreen} />
            <Stack.Screen name="OrderPartRequest" component={OrderPartRequestScreen} />
            <Stack.Screen name="RequestSubmitted" component={RequestSubmittedScreen} />
            <Stack.Screen name="SellWizard" component={SellWizardScreen} />
            <Stack.Screen name="MyListings" component={MyListingsScreen} />
            <Stack.Screen name="SellerProfile" component={SellerProfileScreen} />
            <Stack.Screen name="WriteReview" component={WriteReviewScreen} />
            <Stack.Screen name="Inbox" component={InboxScreen} />
            <Stack.Screen name="Conversation" component={ConversationScreen} />
            <Stack.Screen name="Garage" component={GarageScreen} />
            <Stack.Screen name="Locations" component={LocationsScreen} />
            <Stack.Screen name="AddressForm" component={AddressFormScreen} />
            <Stack.Screen name="Profile" component={ProfileScreen} />
            <Stack.Screen name="BuyerRequests" component={BuyerRequestsScreen} />
            <Stack.Screen name="SellerApplication" component={SellerApplicationScreen} />
            {FEATURES.containers ? <Stack.Screen name="PlannedContainers" component={PlannedContainersScreen} /> : null}
          </Stack.Group>
        ) : (
          <Stack.Group>
            <Stack.Screen name="Splash" component={SplashScreen} />
            <Stack.Screen name="Login" component={LoginScreen} />
            <Stack.Screen name="SignUp" component={SignUpScreen} />
            <Stack.Screen name="Otp" component={OtpScreen} />
            <Stack.Screen name="ForgotPassword" component={ForgotPasswordScreen} />
            <Stack.Screen name="ResetPassword" component={ResetPasswordScreen} />
            <Stack.Screen name="SellerApplication" component={SellerApplicationScreen} />
          </Stack.Group>
        )}
      </Stack.Navigator>
    </NavigationContainer>
  );
}

const styles = StyleSheet.create({
  loading: {
    flex: 1,
    backgroundColor: COLORS.background,
  },
  tabBar: {
    backgroundColor: COLORS.surface,
    borderTopWidth: 1,
    borderTopColor: COLORS.border,
    height: 64,
    paddingBottom: 6,
    paddingTop: 6,
  },
  tabIconBox: {
    width: '100%',
    height: '100%',
  },
  tabItem: {
    alignItems: 'center',
    justifyContent: 'center',
  },
  tabIcon: {
    fontSize: 20,
    opacity: 0.6,
  },
  tabIconActive: {
    opacity: 1,
    transform: [{ scale: 1.1 }],
  },
  tabLabel: {
    color: COLORS.textMuted,
    fontSize: 10,
    fontWeight: '600',
    marginTop: 2,
  },
  tabLabelActive: {
    color: COLORS.primaryGlow,
    fontWeight: '800',
  },
});

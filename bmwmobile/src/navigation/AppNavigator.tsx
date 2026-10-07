import React from 'react';
import { View, Text, StyleSheet } from 'react-native';
import { NavigationContainer } from '@react-navigation/native';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { createBottomTabNavigator } from '@react-navigation/bottom-tabs';
import { RootStackParamList, MainTabParamList } from '../types/navigation';
import { COLORS } from '../theme/colors';

// Screens
import { SplashScreen } from '../screens/SplashScreen';
import { LoginScreen } from '../screens/LoginScreen';
import { OtpScreen } from '../screens/OtpScreen';
import { ProfilePreferencesScreen } from '../screens/ProfilePreferencesScreen';
import { HomeScreen } from '../screens/HomeScreen';
import { MarketplaceSearchScreen } from '../screens/MarketplaceSearchScreen';
import { MyBidsScreen } from '../screens/MyBidsScreen';
import { MyPurchasesScreen } from '../screens/MyPurchasesScreen';
import { ContainerTrackingScreen } from '../screens/ContainerTrackingScreen';

import { PartDetailsScreen } from '../screens/PartDetailsScreen';
import { PlaceBidModal } from '../screens/PlaceBidModal';
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
  return (
    <Tab.Navigator
      screenOptions={{
        headerShown: false,
        tabBarStyle: styles.tabBar,
        tabBarShowLabel: false,
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
      <Tab.Screen 
        name="Tracking" 
        component={ContainerTrackingScreen} 
        options={{
          tabBarIcon: ({ focused }) => <TabIcon icon="🚢" label="Tracking" focused={focused} />
        }}
      />
    </Tab.Navigator>
  );
}

export function AppNavigator() {
  return (
    <NavigationContainer>
      <Stack.Navigator
        initialRouteName="Splash"
        screenOptions={{
          headerShown: false,
          contentStyle: { backgroundColor: COLORS.background },
        }}
      >
        <Stack.Screen name="Splash" component={SplashScreen} />
        <Stack.Screen name="Login" component={LoginScreen} />
        <Stack.Screen name="Otp" component={OtpScreen} />
        <Stack.Screen name="ProfilePreferences" component={ProfilePreferencesScreen} />

        <Stack.Screen name="MainTabs" component={MainTabNavigator} />

        <Stack.Screen name="PartDetails" component={PartDetailsScreen} />
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
        <Stack.Screen name="PlannedContainers" component={PlannedContainersScreen} />
      </Stack.Navigator>
    </NavigationContainer>
  );
}

const styles = StyleSheet.create({
  tabBar: {
    backgroundColor: COLORS.surface,
    borderTopWidth: 1,
    borderTopColor: COLORS.border,
    height: 64,
    paddingBottom: 6,
    paddingTop: 6,
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

import React from 'react';
import { StatusBar } from 'react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { AppNavigator } from './src/navigation/AppNavigator';
import { AuthProvider } from './src/auth/AuthContext';
import { LocationProvider } from './src/location/LocationContext';
import { NotificationsProvider } from './src/services/notifications';
import { GarageProvider } from './src/location/GarageContext';
import { ChatProvider } from './src/chat/ChatContext';

export default function App() {
  return (
    <SafeAreaProvider>
      <StatusBar barStyle="light-content" />
      <AuthProvider>
        <LocationProvider>
          <GarageProvider>
            <NotificationsProvider>
              <ChatProvider>
                <AppNavigator />
              </ChatProvider>
            </NotificationsProvider>
          </GarageProvider>
        </LocationProvider>
      </AuthProvider>
    </SafeAreaProvider>
  );
}

import { AppRegistry } from 'react-native';
import App from './App';
import { name as appName } from './app.json';
import { registerBackgroundPushHandler } from './src/services/push';

// Chat push notifications while the app is in the background (no-op until Firebase is configured).
registerBackgroundPushHandler();

AppRegistry.registerComponent(appName, () => App);

package com.bidmywork.app

import android.app.Application
import android.app.NotificationChannel
import android.app.NotificationManager
import android.os.Build
import com.facebook.react.PackageList
import com.facebook.react.ReactApplication
import com.facebook.react.ReactHost
import com.facebook.react.ReactNativeApplicationEntryPoint.loadReactNative
import com.facebook.react.defaults.DefaultReactHost.getDefaultReactHost

class MainApplication : Application(), ReactApplication {

  override val reactHost: ReactHost by lazy {
    getDefaultReactHost(
      context = applicationContext,
      packageList =
        PackageList(this).packages.apply {
          // Packages that cannot be autolinked yet can be added manually here, for example:
          // add(MyReactNativePackage())
        },
    )
  }

  override fun onCreate() {
    super.onCreate()
    createChatChannel()
    loadReactNative(this)
  }

  /** The "Messages" channel that chat push notifications use (ChatService sends with channel id "chat"). */
  private fun createChatChannel() {
    if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
    val channel = NotificationChannel("chat", "Messages", NotificationManager.IMPORTANCE_HIGH).apply {
      description = "New messages from buyers and sellers"
    }
    getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
  }
}

package com.quickcart.quickcart_customer

import android.content.pm.PackageManager
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel

class MainActivity : FlutterActivity() {
    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        // The app asks whether this build has a Google Maps key (the key itself is never read by the app); without one the map is hidden.
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "quickcart/maps").setMethodCallHandler { call, result ->
            if (call.method == "hasKey") {
                val data = packageManager.getApplicationInfo(packageName, PackageManager.GET_META_DATA).metaData
                result.success(!data?.getString("com.google.android.geo.API_KEY").isNullOrBlank())
            } else {
                result.notImplemented()
            }
        }
    }
}

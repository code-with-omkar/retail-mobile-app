# Razorpay's checkout is reached through reflection and a JavaScript bridge; shrinking must not remove it.
-keepattributes *Annotation*
-keepattributes JavascriptInterface
-keepclassmembers class * { @android.webkit.JavascriptInterface <methods>; }
-keep class com.razorpay.** { *; }
-dontwarn com.razorpay.**
-optimizations !method/inlining/*
-keepclasseswithmembers class * { public void onPayment*(...); }
# Razorpay's optional analytics classes (Google Pay / Play Services) are not always present.
-dontwarn com.google.android.apps.nbu.paisa.inapp.client.api.**

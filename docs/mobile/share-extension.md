# Share into Hally (Android intent filters, iOS Share Extension)

The Dart side (`lib/features/share`) uses [`receive_sharing_intent`](https://pub.dev/packages/receive_sharing_intent).
`ShareListener` wraps the signed-in shell: a shared **text/link** or **image** becomes a capture with `source=Share`
and opens the approval screen. Only the first item of a multi-share is used, and video or other files are ignored.
Nothing is created until a parent approves it.

The native folders are not committed yet. After `flutter create --org app.hally --platforms android,ios .` in `mobile/`, do the following.

## Android

In `android/app/src/main/AndroidManifest.xml`, inside the main `<activity>` (keep `android:launchMode="singleTask"`):

```xml
<intent-filter>
  <action android:name="android.intent.action.SEND" />
  <category android:name="android.intent.category.DEFAULT" />
  <data android:mimeType="text/*" />
</intent-filter>
<intent-filter>
  <action android:name="android.intent.action.SEND" />
  <category android:name="android.intent.category.DEFAULT" />
  <data android:mimeType="image/*" />
</intent-filter>
```

## iOS

1. In Xcode: **File → New → Target → Share Extension**, name it `ShareExtension` and set its deployment target to the Runner's.
2. Add an App Group (e.g. `group.app.hally`) to **both** Runner and ShareExtension under *Signing & Capabilities*.
3. Replace `ShareExtension/ShareViewController.swift` with:

   ```swift
   import receive_sharing_intent
   class ShareViewController: RSIShareViewController {
     override func shouldAutoRedirect() -> Bool { true }
   }
   ```
4. In `ShareExtension/Info.plist`, set `NSExtensionActivationRule` to accept text, URLs and up to one image:

   ```xml
   <key>NSExtensionActivationRule</key>
   <dict>
     <key>NSExtensionActivationSupportsText</key><true/>
     <key>NSExtensionActivationSupportsWebURLWithMaxCount</key><integer>1</integer>
     <key>NSExtensionActivationSupportsImageWithMaxCount</key><integer>1</integer>
   </dict>
   ```
5. In `Runner/Info.plist` add `AppGroupId` = `$(CUSTOM_GROUP_ID)` and a `CFBundleURLTypes` entry with scheme `ShareMedia-$(PRODUCT_BUNDLE_IDENTIFIER)`.
   Define the `CUSTOM_GROUP_ID` build setting on both targets.
6. In `ios/Podfile` add the extension target:

   ```ruby
   target 'ShareExtension' do
     use_frameworks!
     pod 'receive_sharing_intent', :path => '.symlinks/plugins/receive_sharing_intent/ios'
   end
   ```

## Manual test

Share a WhatsApp message and a screenshot to Hally while signed in. You should see the "Paylaşılan içerik okunuyor…" snackbar,
then the approval screen. If you share before signing in, the capture runs once you reach the home screen.

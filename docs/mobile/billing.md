# Billing setup (Family Plus)

The server decides who has Family Plus. The app only shows the paywall, asks the store to sell, and sends the
store's purchase id to `POST /families/{id}/subscription/purchases`, where the server asks the store itself.

## Products

Create the same two auto-renewing subscriptions in App Store Connect and Google Play Console:

| Product id | Period | Note |
|---|---|---|
| `hally_plus_monthly` | 1 month | |
| `hally_plus_yearly` | 1 year | about 2 months free compared to monthly |

Ids are configurable with `Billing:Products` (each `{ Id, Period, FallbackPrices }`). Prices are set in the stores, per country;
the paywall shows the store's localized price. **TL prices are not decided yet** (the spec suggests about ₺129–149 a month).
`FallbackPrices` is only for display before the store SDK answers and can stay empty.

Do not configure a store-side free trial: the 7-day trial is handled by the server so it works without a card.

## App Store

1. App Store Connect → Users and Access → Integrations → In-App Purchase: create a key. Set
   `APPLE_IAP_ISSUER_ID`, `APPLE_IAP_KEY_ID`, `APPLE_IAP_PRIVATE_KEY` (the .p8 content, a secret) and `APPLE_BUNDLE_ID`.
   Use `APPLE_IAP_ENVIRONMENT=Sandbox` for TestFlight.
2. App Store Server Notifications V2 URL: `https://<api>/api/v1/billing/notifications/apple`.
3. The app sends the StoreKit 2 `transactionId` as the purchase token.

## Google Play

1. Create a service account with "View financial data" and "Manage orders and subscriptions" in Play Console,
   and set `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON` (a secret) and `GOOGLE_PLAY_PACKAGE_NAME`.
2. Real-time developer notifications: a Pub/Sub topic with a push subscription to
   `https://<api>/api/v1/billing/notifications/google?token=<GOOGLE_PLAY_NOTIFICATION_TOKEN>`.
3. The app sends the Play `purchaseToken`. The server acknowledges new purchases (Google refunds unacknowledged ones after 3 days).

## App side

`StoreGateway` (`lib/features/billing/billing_repository.dart`) is the device boundary. Until the native folders and the
`in_app_purchase` plugin are added, `UnavailableStoreGateway` is used: the trial works and "Get Family Plus" explains
that store purchases open with the beta. To wire the stores, implement `StoreGateway` with `in_app_purchase`
(`queryProductDetails` for prices, `buyNonConsumable` for the subscription, `restorePurchases`) and override
`storeGatewayProvider` in `main.dart`.

## Local testing

With `Billing:SandboxPurchases=true` in Development, tokens like `sandbox:hally_plus_monthly:any-ref` are accepted without a store
(`sandbox-cancelled:` will not renew, `sandbox-expired:` has already expired). This is ignored in Production.

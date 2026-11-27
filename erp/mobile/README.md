# Shannana Driver (Flutter)

Mobile app for delivery drivers. It talks to the ERP's driver API (`/api/v1/driver`, see `erp/README.md` - "Driver app") with a bearer token. Arabic (right-to-left) by default, English switchable, light/dark.

## What the driver can do
- Sign in (a user with the **Driver** role linked to a driver record) and stay signed in for the shift.
- See **my trips** (on the road / planned / recently completed) and open one: vehicle, progress, load vs capacity, ordered stops.
- **Start** the trip, then per stop: customer, call button, address (opens Maps), the goods and total.
  - **Delivered**: optionally collect money (cash / transfer, amount pre-filled; it becomes a *draft* receipt the accountant confirms) and attach a **signature** or a **photo**.
  - **Could not deliver**: pick a reason (shop closed, refused, no answer, wrong address, other) + note. Completing the trip puts those goods back in stock.
- **Complete** the trip once every stop has a result.
- The phone's position is recorded with each result when location is allowed (never required).

Not included: offline mode, live tracking for the dispatcher, push notifications.

## Run
```powershell
cd erp/mobile
flutter pub get
# Android emulator (10.0.2.2 = this PC):
flutter run --dart-define=API_BASE=http://10.0.2.2:5004
# real phone on the same Wi-Fi: use the PC's LAN address
flutter run --dart-define=API_BASE=http://192.168.1.20:5004
# browser preview (the API allows localhost origins in Development):
flutter run -d chrome --dart-define=API_BASE=http://localhost:5004
```
The API must listen on an address the phone can reach (`dotnet run --project src/Erp.Api --urls http://0.0.0.0:5004`, and allow it in Windows Firewall).

## Build an APK
```powershell
flutter build apk --release --dart-define=API_BASE=https://erp.example.com
```
Output: `build/app/outputs/flutter-apk/`. Debug builds may talk to a plain-`http` LAN test server; **release builds are https only**. Sign release builds with your own key: create `android/key.properties` (`storeFile`, `storePassword`, `keyAlias`, `keyPassword`; git-ignored) - without it the release APK is signed with the debug key and is only good for trying it. Full production steps (key creation, server address) are in `deploy/README.md` section 7.

## Test
```powershell
flutter analyze
flutter test        # sign in, a full trip (start, deliver with money, failed stop, complete), language switch - against an in-memory fake API
```

## Layout
`lib/core` (api client, session/token storage, settings, strings, theme) - `lib/models` - `lib/features/{auth,trips}` (login, trips, trip, stop, deliver, proof picker) - `lib/widgets`. Brand colours and Cairo come from the website; images are in `assets/`.
